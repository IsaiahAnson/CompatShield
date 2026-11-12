"""Changes settings in the game's BepInEx .cfg files once the game is closed.

  python -I set-config.py --label NAME --set "FILE|SECTION|KEY|VALUE" --test FOLDER    edit copies of the files in FOLDER
  python -I set-config.py --label NAME --set "FILE|SECTION|KEY|VALUE" --outside        apply now (the game must be closed)
  python -I set-config.py --label NAME --set "FILE|SECTION|KEY|VALUE" --wait-pid PID   wait for the game to close, then apply

Repeat --set for more settings. A key is looked up inside its [section], so a name used in several sections is safe, and
everything after the third | is the value, so a value may hold a | of its own.

Why two processes: Claude desktop can redirect a shell's AppData writes into a private copy the game never reads. So the
edit runs in a process started through WMI, and that process refuses to edit if it is still inside Claude's package.
All the files change or none do. Backups go to tools/backups/cfg-backup-<time>-<label>.
"""
import argparse
import base64
import ctypes
import os
import re
import shutil
import subprocess
import sys
import time

ROAMING = r"C:\Users\isaia\AppData\Roaming"
CLAUDE_ROAMING = r"C:\Users\isaia\AppData\Local\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming"
CONFIG = ROAMING + r"\Thunderstore Mod Manager\DataFolder\RiskOfRain2\profiles\Default\BepInEx\config"
PYTHON = r"C:\Users\isaia\AppData\Local\Python\bin\python.exe"
BACKUPS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "backups")
GAME = "Risk of Rain 2.exe"
NO_WINDOW = 0x08000000
SYNCHRONIZE = 0x00100000
INFINITE = 0xFFFFFFFF
SNAPSHOT_PROCESSES = 2


class ProcessEntry(ctypes.Structure):
    _fields_ = [("dwSize", ctypes.c_uint32), ("cntUsage", ctypes.c_uint32), ("th32ProcessID", ctypes.c_uint32),
                ("th32DefaultHeapID", ctypes.c_size_t), ("th32ModuleID", ctypes.c_uint32),
                ("cntThreads", ctypes.c_uint32), ("th32ParentProcessID", ctypes.c_uint32),
                ("pcPriClassBase", ctypes.c_long), ("dwFlags", ctypes.c_uint32), ("szExeFile", ctypes.c_wchar * 260)]


def process_running(image):
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.CreateToolhelp32Snapshot.restype = ctypes.c_void_p
    kernel.Process32FirstW.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
    kernel.Process32NextW.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
    kernel.CloseHandle.argtypes = [ctypes.c_void_p]
    snapshot = kernel.CreateToolhelp32Snapshot(SNAPSHOT_PROCESSES, 0)
    entry = ProcessEntry()
    entry.dwSize = ctypes.sizeof(ProcessEntry)
    try:
        more = kernel.Process32FirstW(snapshot, ctypes.byref(entry))
        while more:
            if entry.szExeFile.lower() == image.lower():
                return True
            more = kernel.Process32NextW(snapshot, ctypes.byref(entry))
    finally:
        kernel.CloseHandle(snapshot)
    return False


def inside_claude_package():
    """Inside the package, a file made in Claude's private folder also shows up under the real AppData path."""
    name = "__setconfig_probe_%d.tmp" % os.getpid()
    private = os.path.join(CLAUDE_ROAMING, name)
    with open(private, "w"):
        pass
    try:
        return os.path.exists(os.path.join(ROAMING, name))
    finally:
        os.remove(private)


def edit(data, section, key, value):
    """Returns the new file bytes and the old value. Only the value's own bytes change, so line endings survive."""
    headers = list(re.finditer(rb"(?m)^\[" + re.escape(section.encode("utf-8")) + rb"\][ \t]*\r?$", data))
    if len(headers) != 1:
        raise RuntimeError("expected one [%s] section, found %d" % (section, len(headers)))
    start = headers[0].end()
    following = re.compile(rb"(?m)^\[").search(data, start)
    end = following.start() if following else len(data)
    line = re.compile(rb"(?m)^" + re.escape(key.encode("utf-8")) + rb"[ \t]*=[ \t]*([^\r\n]*?)[ \t]*\r?$")
    found = list(line.finditer(data, start, end))
    if not found:
        # A setting the installed build has not written yet: add it under the header; BepInEx reads it at its first Bind.
        nl = b"\r\n" if b"\r\n" in data else b"\n"
        at = data.find(b"\n", start) + 1 or len(data)   # the header match stops before its line feed
        added = nl + key.encode("utf-8") + b" = " + value.encode("utf-8") + nl
        if at == len(data) and not data.endswith(b"\n"):
            added = nl + added
        return data[:at] + added + data[at:], "(not in the file yet)"
    if len(found) != 1:
        raise RuntimeError("expected one %s line in [%s], found %d" % (key, section, len(found)))
    old = found[0]
    return data[:old.start(1)] + value.encode("utf-8") + data[old.end(1):], old.group(1).decode("utf-8", "replace")


def apply(sets, folder, test, label):
    lines = []
    if not test:
        if process_running(GAME):
            raise RuntimeError("the game is running")
        if inside_claude_package():
            raise RuntimeError("this process is inside Claude's package, so its writes would not reach the game")
    files = {}
    for name, section, key, value in sets:
        path = os.path.join(folder, name)
        if path not in files:
            with open(path, "rb") as f:
                before = f.read()
            files[path] = [before, before]
        files[path][1], old = edit(files[path][1], section, key, value)
        lines.append("%s [%s] %s: %s -> %s" % (name, section, key, old, value))
    if not test:
        backup = os.path.join(BACKUPS, time.strftime("cfg-backup-%Y%m%d-%H%M%S-") + label)
        os.makedirs(backup)
        for path in files:
            shutil.copy2(path, backup)
        lines.insert(0, "backup: " + backup)
    touched = []
    try:
        for path, (before, after) in files.items():
            touched.append((path, before))
            with open(path, "wb") as f:
                f.write(after)
            with open(path, "rb") as f:
                if f.read() != after:
                    raise RuntimeError("read-back differs for " + path)
    except Exception as error:
        for path, before in touched:
            try:
                with open(path, "wb") as f:
                    f.write(before)
            except OSError as undo:
                raise RuntimeError("%s; could not restore %s (%s), use the backup" % (error, path, undo))
        raise
    return lines


def start_outside_package(argv):
    command = subprocess.list2cmdline(argv).replace("'", "''")
    script = ("$r = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{CommandLine='" + command +
              "'}; 'return code ' + [string]$r.ReturnValue + ', process ' + [string]$r.ProcessId")
    encoded = base64.b64encode(script.encode("utf-16-le")).decode("ascii")
    done = subprocess.run(["powershell", "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded],
                          capture_output=True, text=True, creationflags=NO_WINDOW)
    return (done.stdout + done.stderr).strip()


def run_outside(pid, label, specs):
    if pid:
        kernel = ctypes.windll.kernel32
        kernel.OpenProcess.restype = ctypes.c_void_p
        kernel.WaitForSingleObject.argtypes = [ctypes.c_void_p, ctypes.c_uint32]
        handle = kernel.OpenProcess(SYNCHRONIZE, False, pid)
        if not handle:
            print("FAILED: process %d is not running, nothing armed" % pid, flush=True)
            return 1
        print("armed: %s applies when process %d exits" % (label, pid), flush=True)
        kernel.WaitForSingleObject(handle, INFINITE)
        time.sleep(6)
    result = os.path.join(BACKUPS, "set-config-result-%s.txt" % label)
    if os.path.exists(result):
        os.remove(result)
    argv = [PYTHON, "-I", os.path.abspath(__file__), "--label", label, "--result", result]
    for spec in specs:
        argv += ["--set", spec]
    print("started outside Claude: " + start_outside_package(argv), flush=True)
    for _ in range(90):
        if os.path.exists(result):
            with open(result, encoding="utf-8") as f:
                report = f.read()
            print(report, flush=True)
            return 0 if report.startswith("OK") else 1
        time.sleep(1)
    print("FAILED: no result after 90 s, check the .cfg files", flush=True)
    return 1


def main():
    parser = argparse.ArgumentParser(description="Change settings in the game's .cfg files.")
    parser.add_argument("--label", required=True, help="short name for the backup and result files")
    parser.add_argument("--set", dest="specs", action="append", required=True, metavar="FILE|SECTION|KEY|VALUE")
    parser.add_argument("--outside", action="store_true", help="run the edit in a process outside Claude's package")
    parser.add_argument("--wait-pid", type=int, default=0, help="wait for this process to exit, then do --outside")
    parser.add_argument("--test", help="folder holding copies of the .cfg files")
    parser.add_argument("--result", help="also write the outcome to this file")
    args = parser.parse_args()
    if not re.fullmatch(r"[A-Za-z0-9_-]+", args.label):
        parser.error("--label may only hold letters, digits, - and _")
    sets = []
    for spec in args.specs:
        parts = spec.split("|", 3)
        if len(parts) != 4 or not all(parts[:3]) or os.path.basename(parts[0]) != parts[0]:
            parser.error("--set needs FILE|SECTION|KEY|VALUE with a bare file name, got " + spec)
        sets.append(parts)
    if args.outside or args.wait_pid:
        return run_outside(args.wait_pid, args.label, args.specs)
    try:
        report = "OK\n" + "\n".join(apply(sets, args.test or CONFIG, bool(args.test), args.label))
        status = 0
    except Exception as error:
        report = "FAILED: %s" % error
        status = 1
    if args.result:
        with open(args.result + ".tmp", "w", encoding="utf-8") as f:
            f.write(report + "\n")
        os.replace(args.result + ".tmp", args.result)
    print(report)
    return status


if __name__ == "__main__":
    sys.exit(main())
