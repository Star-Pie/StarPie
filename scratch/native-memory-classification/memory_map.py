"""Read Windows memory metadata with QUERY_INFORMATION; never read target bytes."""

import argparse
import collections
import ctypes as C
from ctypes import wintypes as W
import datetime as D
import hashlib
import json
import os
from pathlib import Path
import re
import sys
import time

MEM_COMMIT, MEM_RESERVE, MEM_FREE = 0x1000, 0x2000, 0x10000
MEM_PRIVATE, MEM_MAPPED, MEM_IMAGE = 0x20000, 0x40000, 0x1000000
TYPE_NAMES = {MEM_PRIVATE: "private", MEM_MAPPED: "mapped", MEM_IMAGE: "image"}
EPOCH = D.datetime(1601, 1, 1, tzinfo=D.timezone.utc)


class MBI(C.Structure):
    _fields_ = [("BaseAddress", C.c_void_p), ("AllocationBase", C.c_void_p),
                ("AllocationProtect", W.DWORD), ("PartitionId", W.WORD),
                ("RegionSize", C.c_size_t), ("State", W.DWORD),
                ("Protect", W.DWORD), ("Type", W.DWORD)]


class SystemInfo(C.Structure):
    _fields_ = [("Architecture", W.WORD), ("Reserved", W.WORD),
                ("PageSize", W.DWORD), ("MinimumAddress", C.c_void_p),
                ("MaximumAddress", C.c_void_p), ("ProcessorMask", C.c_size_t),
                ("Processors", W.DWORD), ("ProcessorType", W.DWORD),
                ("Granularity", W.DWORD), ("Level", W.WORD), ("Revision", W.WORD)]


class MemoryCounters(C.Structure):
    _fields_ = [("cb", W.DWORD), ("PageFaultCount", W.DWORD)] + [
        (name, C.c_size_t) for name in (
            "PeakWorkingSetSize", "WorkingSetSize", "QuotaPeakPagedPoolUsage",
            "QuotaPagedPoolUsage", "QuotaPeakNonPagedPoolUsage", "QuotaNonPagedPoolUsage",
            "PagefileUsage", "PeakPagefileUsage", "PrivateUsage")]


class WorkingSetEntry(C.Structure):
    _fields_ = [("Address", C.c_void_p), ("Flags", C.c_size_t)]


def api(dll, name, result, args):
    fn = getattr(dll, name)
    fn.restype, fn.argtypes = result, args
    return fn


def native_api():
    if os.name != "nt" or C.sizeof(C.c_void_p) != 8:
        raise RuntimeError("64-bit Windows Python required")
    kernel = C.WinDLL("kernel32", use_last_error=True)
    psapi = C.WinDLL("psapi", use_last_error=True)
    functions = {
        "open": api(kernel, "OpenProcess", W.HANDLE, [W.DWORD, W.BOOL, W.DWORD]),
        "close": api(kernel, "CloseHandle", W.BOOL, [W.HANDLE]),
        "query": api(kernel, "VirtualQueryEx", C.c_size_t,
                     [W.HANDLE, C.c_void_p, C.POINTER(MBI), C.c_size_t]),
        "system": api(kernel, "GetSystemInfo", None, [C.POINTER(SystemInfo)]),
        "times": api(kernel, "GetProcessTimes", W.BOOL,
                     [W.HANDLE] + [C.POINTER(W.FILETIME)] * 4),
        "image": api(kernel, "QueryFullProcessImageNameW", W.BOOL,
                     [W.HANDLE, W.DWORD, W.LPWSTR, C.POINTER(W.DWORD)]),
        "exit": api(kernel, "GetExitCodeProcess", W.BOOL, [W.HANDLE, C.POINTER(W.DWORD)]),
        "memory": api(psapi, "GetProcessMemoryInfo", W.BOOL,
                      [W.HANDLE, C.POINTER(MemoryCounters), W.DWORD]),
        "ws": api(psapi, "QueryWorkingSetEx", W.BOOL, [W.HANDLE, C.c_void_p, W.DWORD]),
    }
    assert C.sizeof(MBI) == 48 and MBI.RegionSize.offset == 24
    assert C.sizeof(MemoryCounters) == 80 and C.sizeof(WorkingSetEntry) == 16
    return functions, kernel


def checked(ok, action):
    if not ok:
        raise C.WinError(C.get_last_error(), action)


def expected_ticks(value):
    match = re.fullmatch(r"(\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d)(?:\.(\d{1,7}))?Z", value)
    if not match:
        raise ValueError("Expected exact UTC creation time, up to seven fractional digits")
    delta = D.datetime.fromisoformat(match[1] + "+00:00") - EPOCH
    return (delta.days * 86400 + delta.seconds) * 10_000_000 + int((match[2] or "").ljust(7, "0"))


def identity(fn, handle, expected):
    values = [W.FILETIME() for _ in range(4)]
    checked(fn["times"](handle, *(C.byref(v) for v in values)), "GetProcessTimes")
    ticks = (values[0].dwHighDateTime << 32) | values[0].dwLowDateTime
    buf, length = C.create_unicode_buffer(32768), W.DWORD(32768)
    checked(fn["image"](handle, 0, buf, C.byref(length)), "QueryFullProcessImageName")
    exit_code = W.DWORD()
    checked(fn["exit"](handle, C.byref(exit_code)), "GetExitCodeProcess")
    if exit_code.value != 259 or ticks != expected_ticks(expected["createdUtc"]):
        raise RuntimeError("Process ended or creation time changed")
    if os.path.normcase(buf.value) != os.path.normcase(expected["exePath"]):
        raise RuntimeError("Executable path mismatch")
    for name, digest in expected["fileSha256"].items():
        if Path(name).name != name:
            raise ValueError("Identity hashes must name candidate top-level files")
        actual = hashlib.sha256((Path(buf.value).parent / name).read_bytes()).hexdigest()
        if actual.upper() != digest.upper():
            raise RuntimeError("Candidate on-disk hash mismatch: " + name)
    return {"pid": expected["pid"], "creationFileTime": ticks,
            "exePathVerified": True, "candidateFileSha256": expected["fileSha256"],
            "createdUtc": expected["createdUtc"], "alive": True}


def process_counters(fn, handle):
    value = MemoryCounters()
    value.cb = C.sizeof(value)
    checked(fn["memory"](handle, C.byref(value), value.cb), "GetProcessMemoryInfo")
    return {"timeUtc": D.datetime.now(D.timezone.utc).isoformat(),
            **{name: int(getattr(value, name)) for name, _ in value._fields_}}


def residency(flags):
    if not flags & 1:
        return "notResidentBytes"
    return "residentShareableBytes" if flags & (1 << 15) else "residentPrivateBytes"


def check_deadline(deadline):
    if deadline is not None and time.monotonic() > deadline:
        raise RuntimeError("Metadata scan deadline exceeded; incomplete result rejected")


def query_residency(fn, handle, start, size, page_size, deadline=None):
    counts = collections.Counter()
    # Only page metadata is requested. No access, VirtualLock or fault-in is performed.
    for offset in range(0, size, 4096 * page_size):
        check_deadline(deadline)
        count = min(4096, (size - offset) // page_size)
        entries = (WorkingSetEntry * count)()
        for index, entry in enumerate(entries):
            entry.Address = start + offset + index * page_size
        checked(fn["ws"](handle, entries, C.sizeof(entries)), "QueryWorkingSetEx")
        check_deadline(deadline)
        for entry in entries:
            counts[residency(entry.Flags)] += page_size
    return dict(counts)


def classify(fn, handle):
    system = SystemInfo()
    fn["system"](C.byref(system))
    page_size = system.PageSize
    started = time.monotonic()
    deadline = started + 20
    cursor, calls, committed = 0, 0, 0
    groups, allocations, regions = {}, {}, []
    while cursor <= system.MaximumAddress:
        check_deadline(deadline)
        if calls >= 200000:
            raise RuntimeError("Metadata scan limit reached; incomplete result rejected")
        region = MBI()
        count = fn["query"](handle, cursor, C.byref(region), C.sizeof(region))
        if count != C.sizeof(region):
            raise C.WinError(C.get_last_error(), "VirtualQueryEx")
        calls += 1
        base, size = int(region.BaseAddress or 0), int(region.RegionSize)
        if size <= 0 or base + size <= cursor or size % page_size:
            raise RuntimeError("Invalid region progress/alignment")
        cursor = base + size
        state = {MEM_COMMIT: "committed", MEM_RESERVE: "reserved", MEM_FREE: "free"}.get(region.State)
        if state is None:
            raise RuntimeError("Unexpected region state")
        type_name = TYPE_NAMES.get(region.Type, "other") if state != "free" else "free"
        key = state + "/" + type_name
        group = groups.setdefault(key, collections.Counter())
        group["bytes"] += size
        group["regions"] += 1
        if state == "free":
            continue
        item = {"base": base, "allocationBase": int(region.AllocationBase or 0),
                "bytes": size, "state": state, "type": type_name,
                "protect": int(region.Protect) if state == "committed" else None}
        allocation = allocations.setdefault(item["allocationBase"], collections.Counter())
        allocation[state + "Bytes"] += size
        if state == "committed":
            committed += size
            if committed > 2 * 1024**3:
                raise RuntimeError("Committed page metadata cap exceeded")
            ws = query_residency(fn, handle, base, size, page_size, deadline)
            item.update(ws)
            group.update(ws)
            allocation.update(ws)
        regions.append(item)
    check_deadline(deadline)
    return {"pageSize": page_size, "virtualQueryCalls": calls,
            "scanSeconds": time.monotonic() - started,
            "groups": groups, "allocations": [dict(base=key, **value) for key, value in allocations.items()],
            "regions": regions, "completedAddressSpace": True}


def self_test(fn, kernel):
    current = api(kernel, "GetCurrentProcess", W.HANDLE, [])()
    alloc = api(kernel, "VirtualAlloc", C.c_void_p, [C.c_void_p, C.c_size_t, W.DWORD, W.DWORD])
    free = api(kernel, "VirtualFree", W.BOOL, [C.c_void_p, C.c_size_t, W.DWORD])
    base = alloc(None, 16 * 1024**2, MEM_RESERVE, 0x04)
    checked(base, "self VirtualAlloc reserve")
    checks = []
    try:
        region = MBI()
        checked(fn["query"](current, base, C.byref(region), C.sizeof(region)), "self query reserve")
        assert region.State == MEM_RESERVE and region.RegionSize == 16 * 1024**2
        checks.append("16 MiB reserve is not commit")
        allocated = alloc(base, 4 * 1024**2, MEM_COMMIT, 0x04)
        assert allocated == base
        # Only our own explicitly owned allocation is touched in self-test mode.
        C.memset(base, 1, 4 * 1024**2)
        checked(fn["query"](current, base, C.byref(region), C.sizeof(region)), "self query commit")
        assert region.State == MEM_COMMIT and region.Type == MEM_PRIVATE and region.RegionSize == 4 * 1024**2
        ws = query_residency(fn, current, base, 4 * 1024**2, 4096)
        assert ws.get("residentPrivateBytes") == 4 * 1024**2, ws
        assert sum(ws.values()) == 4 * 1024**2
        checks.extend(["4 MiB commit remains distinct from 12 MiB reserve", "touched own pages are private resident"])
        assert residency(0) == "notResidentBytes"
        assert residency(1) == "residentPrivateBytes"
        assert residency(1 | (1 << 15)) == "residentShareableBytes"
        checks.append("valid and shared bit synthetic controls")
    finally:
        checked(free(base, 0, 0x8000), "self VirtualFree")
    region = MBI()
    checked(fn["query"](current, base, C.byref(region), C.sizeof(region)), "self query free")
    assert region.State == MEM_FREE
    checks.append("owned allocation becomes free after release")
    print(json.dumps({"selfTest": "PASS", "checks": checks, "targetAttached": False}))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--identity", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    fn, kernel = native_api()
    if args.self_test:
        if args.identity or args.output:
            parser.error("Self-test cannot attach or write a result file")
        self_test(fn, kernel)
        return
    if not args.identity or not args.output or not args.output.is_absolute() or args.output.exists():
        parser.error("Explicit identity and new absolute output file required")
    expected = json.loads(args.identity.read_text(encoding="utf-8-sig"))
    handle = fn["open"](0x0400, False, int(expected["pid"]))
    checked(handle, "OpenProcess QUERY_INFORMATION")
    try:
        bound = identity(fn, handle, expected)
        before = process_counters(fn, handle)
        result = classify(fn, handle)
        after = process_counters(fn, handle)
        identity(fn, handle, expected)
    finally:
        checked(fn["close"](handle), "CloseHandle")
    result.update(identity=bound, before=before, after=after,
                  requestedAccess="PROCESS_QUERY_INFORMATION only (0x0400)",
                  limitations=["Page/region metadata only; no target contents or allocation stacks.",
                               "Live scan is non-atomic; pages may change while queried.",
                               "Shared bit means shareable, not necessarily shared right now.",
                               "Private copy-on-write pages can retain IMAGE/MAPPED region types.",
                               "Reservation, region commit, resident pages and process private commit differ."])
    with args.output.open("x", encoding="utf-8") as out:
        json.dump(result, out, indent=2)
        out.write("\n")
    print(json.dumps({"output": str(args.output), "scanSeconds": result["scanSeconds"],
                      "groups": result["groups"], "privateCommit": after["PrivateUsage"]}))


if __name__ == "__main__":
    main()
