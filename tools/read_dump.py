# Offline reader for a Unity crash.dmp: names each thread's frames with dbghelp + Unity's PDB.
# Usage: python -I read_dump.py <crash.dmp> <symbol dir> [thread ids...]
import collections
import ctypes
import ctypes.wintypes as wt
import struct
import sys

DUMP, SYMDIR = sys.argv[1], sys.argv[2]
FOCUS = [int(x) for x in sys.argv[3:]]
b = open(DUMP, "rb").read()

_, _, nstreams, dirrva = struct.unpack_from("<4sIII", b, 0)
streams = {}
for i in range(nstreams):
    t, size, rva = struct.unpack_from("<III", b, dirrva + 12 * i)
    streams.setdefault(t, (size, rva))


def mdstr(rva):
    n, = struct.unpack_from("<I", b, rva)
    return b[rva + 4: rva + 4 + n].decode("utf-16-le", "replace")


# Modules (stream 4), 108-byte packed records.
mods = []
cnt, = struct.unpack_from("<I", b, streams[4][1])
for k in range(cnt):
    o = streams[4][1] + 4 + 108 * k
    base, size = struct.unpack_from("<QI", b, o)
    name_rva, = struct.unpack_from("<I", b, o + 20)
    mods.append((base, base + size, mdstr(name_rva)))
mods.sort()


def mod_of(a):
    for lo, hi, path in mods:
        if lo <= a < hi:
            return lo, path
    return None, None


# Executable bytes of each module file, for checking that a stack value is a real return address.
pe_cache = {}


def pe_sections(path):
    if path in pe_cache:
        return pe_cache[path]
    try:
        data = open(path, "rb").read()
        pe = struct.unpack_from("<I", data, 0x3C)[0]
        nsec, = struct.unpack_from("<H", data, pe + 6)
        optsz, = struct.unpack_from("<H", data, pe + 20)
        secs = []
        for s in range(nsec):
            o = pe + 24 + optsz + 40 * s
            vsz, va, rawsz, rawptr = struct.unpack_from("<IIII", data, o + 8)
            ch, = struct.unpack_from("<I", data, o + 36)
            if ch & 0x20000000:
                secs.append((va, va + max(vsz, rawsz), rawptr))
        pe_cache[path] = (data, secs)
    except OSError:
        pe_cache[path] = (None, [])
    return pe_cache[path]


def follows_call(path, rva):
    data, secs = pe_sections(path)
    if data is None:
        return False
    for va, end, raw in secs:
        if va + 8 <= rva < end:
            p = raw + (rva - va)
            w = data[p - 7:p]
            if w[2] == 0xE8:                                   # call rel32
                return True
            if w[1] == 0xFF and (w[2] & 0x38) == 0x10:          # call [rip+disp32] / [reg+disp32]
                return True
            if w[5] == 0xFF and 0xD0 <= w[6] <= 0xD7:           # call reg
                return True
            if w[4] == 0x41 and w[5] == 0xFF and 0xD0 <= w[6] <= 0xD7:
                return True
            if w[4] == 0xFF and 0x50 <= w[5] <= 0x57:           # call [reg+disp8]
                return True
            if w[3] == 0x41 and w[4] == 0xFF and 0x50 <= w[5] <= 0x57:
                return True
            if w[5] == 0xFF and 0x10 <= w[6] <= 0x17:           # call [reg]
                return True
            if w[0] == 0x41 and w[1] == 0xFF and 0x90 <= w[2] <= 0x97:
                return True
            if w[4] == 0xFF and w[5] == 0x14:                   # call [reg+reg*s]
                return True
            return False
    return False


# dbghelp with the PDB folder as the only search path.
dbg = ctypes.WinDLL(r"C:\Windows\System32\dbghelp.dll")
H = wt.HANDLE(0x4242)
dbg.SymSetOptions(0x2 | 0x200 | 0x80000)
dbg.SymInitializeW.argtypes = [wt.HANDLE, wt.LPCWSTR, wt.BOOL]
dbg.SymInitializeW(H, SYMDIR, False)
dbg.SymLoadModuleExW.restype = ctypes.c_uint64
dbg.SymLoadModuleExW.argtypes = [wt.HANDLE, wt.HANDLE, wt.LPCWSTR, wt.LPCWSTR, ctypes.c_uint64, wt.DWORD, ctypes.c_void_p, wt.DWORD]
for lo, hi, path in mods:
    dbg.SymLoadModuleExW(H, None, path, None, lo, hi - lo, None, 0)


class SYMBOL_INFO(ctypes.Structure):
    _fields_ = [("SizeOfStruct", wt.ULONG), ("TypeIndex", wt.ULONG), ("Reserved", ctypes.c_uint64 * 2),
                ("Index", wt.ULONG), ("Size", wt.ULONG), ("ModBase", ctypes.c_uint64), ("Flags", wt.ULONG),
                ("Value", ctypes.c_uint64), ("Address", ctypes.c_uint64), ("Register", wt.ULONG),
                ("Scope", wt.ULONG), ("Tag", wt.ULONG), ("NameLen", wt.ULONG), ("MaxNameLen", wt.ULONG),
                ("Name", ctypes.c_char * 1)]


dbg.SymFromAddr.argtypes = [wt.HANDLE, ctypes.c_uint64, ctypes.POINTER(ctypes.c_uint64), ctypes.c_void_p]
NAME_OFF = SYMBOL_INFO.Name.offset
names = {}


def name(a):
    if a in names:
        return names[a]
    lo, path = mod_of(a)
    if lo is None:
        names[a] = None
        return None
    short = path.rsplit("\\", 1)[-1]
    buf = ctypes.create_string_buffer(ctypes.sizeof(SYMBOL_INFO) + 1024)
    si = SYMBOL_INFO.from_buffer(buf)
    si.SizeOfStruct = ctypes.sizeof(SYMBOL_INFO)
    si.MaxNameLen = 1024
    disp = ctypes.c_uint64()
    if dbg.SymFromAddr(H, a, ctypes.byref(disp), buf):
        nm = ctypes.string_at(ctypes.addressof(buf) + NAME_OFF, si.NameLen).decode("latin1")
        s = "%s!%s+0x%x" % (short.replace(".dll", "").replace(".DLL", ""), nm, disp.value)
    else:
        s = "%s+0x%x" % (short, a - lo)
    names[a] = s
    return s


# Thread names (stream 24), 12-byte records.
tnames = {}
if 24 in streams:
    o = streams[24][1]
    n, = struct.unpack_from("<I", b, o)
    for k in range(n):
        tid, rva = struct.unpack_from("<IQ", b, o + 4 + 12 * k)
        tnames[tid] = mdstr(rva)

# Exception (stream 6): faulting thread and its context.
exc_tid, exc_ctx = None, None
if 6 in streams:
    o = streams[6][1]
    exc_tid, = struct.unpack_from("<I", b, o)
    exc_ctx, = struct.unpack_from("<I", b, o + 8 + 152 + 4)
    code, _, _, addr = struct.unpack_from("<IIQQ", b, o + 8)
    print("fault: thread %d code 0x%x at %s" % (exc_tid, code, name(addr)))


def frames(tid, stack_start, stack_size, stack_rva, ctx_rva, limit=40):
    rsp, = struct.unpack_from("<Q", b, ctx_rva + 0x98)
    rip, = struct.unpack_from("<Q", b, ctx_rva + 0xF8)
    out = [name(rip) or "JIT/unknown 0x%x" % rip]
    off = max(0, rsp - stack_start)
    off -= off % 8
    while off + 8 <= stack_size and len(out) < limit:
        v, = struct.unpack_from("<Q", b, stack_rva + off)
        off += 8
        lo, path = mod_of(v)
        if lo is None:
            continue
        if not follows_call(path, v - lo):
            continue
        nm = name(v)
        if nm and nm != out[-1]:
            out.append(nm)
    return out


cnt, = struct.unpack_from("<I", b, streams[3][1])
groups = collections.defaultdict(list)
for k in range(cnt):
    o = streams[3][1] + 4 + 48 * k
    tid, = struct.unpack_from("<I", b, o)
    sstart, = struct.unpack_from("<Q", b, o + 24)
    ssize, srva, csize, crva = struct.unpack_from("<IIII", b, o + 32)
    ctx = exc_ctx if tid == exc_tid and exc_ctx else crva
    fr = frames(tid, sstart, ssize, srva, ctx, 40 if (tid in FOCUS or tid == exc_tid) else 6)
    if tid in FOCUS or tid == exc_tid:
        print("\n== thread %d %s" % (tid, tnames.get(tid, "")))
        for f in fr:
            print("   " + f[:160])
    key = " < ".join(x.split("+0x")[0] for x in fr[:3])
    groups[key].append("%d %s" % (tid, tnames.get(tid, "")))

print("\n== all %d threads grouped by their top three frames" % cnt)
for key, tids in sorted(groups.items(), key=lambda kv: -len(kv[1])):
    print("%3d  %s  [%s]" % (len(tids), key[:170], "; ".join(tids[:3])[:120]))
