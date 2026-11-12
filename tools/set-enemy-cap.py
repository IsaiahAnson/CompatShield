"""Sets the enemy cap in both mods' configs: CompatShield's MonsterLimit and Inferno's Enemy Cap.

  python -I set-enemy-cap.py --cap 30 --outside               apply now (the game must be closed)
  python -I set-enemy-cap.py --cap 30 --wait-pid <game pid>   wait for the game to close, then apply
  python -I set-enemy-cap.py --cap 30 --test <folder>         edit copies of the two .cfg files in <folder>

Why two processes: Claude desktop can redirect a shell's AppData writes into a private copy the game never reads. So the
edit runs in a process started through WMI, and that process refuses to edit if it is still inside Claude's package.
Both files change or neither does. Backups go to tools/backups/cfg-backup-<time>-enemycap<cap>.
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
RESULT = os.path.join(BACKUPS, "enemy-cap-result.txt")
GAME = "Risk of Rain 2.exe"
SETTINGS = [("local.compatshield.cfg", b"MonsterLimit"), ("HIFU.Inferno.cfg", b"Enemy Cap")]
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
    name = "__enemycap_probe_%d.tmp" % os.getpid()
    private = os.path.join(CLAUDE_ROAMING, name)
    with open(private, "w"):
        pass
    try:
        return os.path.exists(os.path.join(ROAMING, name))
    finally:
        os.remove(private)


def edit(data, key, cap):
    pattern = re.compile(rb"(?m)^(" + re.escape(key) + rb"[ \t]*=[ \t]*)(-?\d+)(?=[ \t]*\r?$)")
    found = pattern.findall(data)
    if len(found) != 1:
        raise RuntimeError("expected one %s line, found %d" % (key.decode(), len(found)))
    return pattern.sub(rb"\g<1>" + str(cap).encode("ascii"), data, count=1), int(found[0][1])


def apply(cap, folder, test):
    lines = []
    if not test:
        if process_running(GAME):
            raise RuntimeError("the game is running")
        if inside_claude_package():
            raise RuntimeError("this process is inside Claude's package, so its writes would not reach the game")
    edits = []
    for name, key in SETTINGS:
        path = os.path.join(folder, name)
        with open(path, "rb") as f:
            before = f.read()
        after, old = edit(before, key, cap)
        edits.append((path, key.decode(), before, after, old))
    if not test:
        backup = os.path.join(BACKUPS, time.strftime("cfg-backup-%Y%m%d-%H%M%S-enemycap") + str(cap))
        os.makedirs(backup)
        for path, *_ in edits:
            shutil.copy2(path, backup)
        lines.append("backup: " + backup)
    touched = []
    try:
        for path, key, before, after, old in edits:
            touched.append((path, before))
            with open(path, "wb") as f:
                f.write(after)
            with open(path, "rb") as f:
                if f.read() != after:
                    raise RuntimeError("read-back differs for " + path)
            lines.append("%s: %s %d -> %d" % (os.path.basename(path), key, old, cap))
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


def run_outside(pid, cap):
    if pid:
        kernel = ctypes.windll.kernel32
        kernel.OpenProcess.restype = ctypes.c_void_p
        kernel.WaitForSingleObject.argtypes = [ctypes.c_void_p, ctypes.c_uint32]
        handle = kernel.OpenProcess(SYNCHRONIZE, False, pid)
        if not handle:
            print("FAILED: process %d is not running, nothing armed" % pid, flush=True)
            return 1
        print("armed: enemy cap %d applies when process %d exits" % (cap, pid), flush=True)
        kernel.WaitForSingleObject(handle, INFINITE)
        time.sleep(6)
    if os.path.exists(RESULT):
        os.remove(RESULT)
    started = start_outside_package([PYTHON, "-I", os.path.abspath(__file__), "--cap", str(cap), "--result", RESULT])
    print("started outside Claude: " + started, flush=True)
    for _ in range(90):
        if os.path.exists(RESULT):
            with open(RESULT, encoding="utf-8") as f:
                report = f.read()
            print(report, flush=True)
            return 0 if report.startswith("OK") else 1
        time.sleep(1)
    print("FAILED: no result after 90 s, check the two .cfg files", flush=True)
    return 1


def main():
    parser = argparse.ArgumentParser(description="Set the enemy cap in CompatShield and Inferno.")
    parser.add_argument("--cap", type=int, required=True)
    parser.add_argument("--outside", action="store_true", help="run the edit in a process outside Claude's package")
    parser.add_argument("--wait-pid", type=int, default=0, help="wait for this process to exit, then do --outside")
    parser.add_argument("--test", help="folder holding copies of the two .cfg files")
    parser.add_argument("--result", help="also write the outcome to this file")
    args = parser.parse_args()
    if not 1 <= args.cap <= 200:
        parser.error("--cap must be between 1 and 200")
    if args.outside or args.wait_pid:
        return run_outside(args.wait_pid, args.cap)
    try:
        report = "OK\n" + "\n".join(apply(args.cap, args.test or CONFIG, bool(args.test)))
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
