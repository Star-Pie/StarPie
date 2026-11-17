"""Allocation commit-charge metadata controls; no target memory contents."""

import ctypes as C
from ctypes import wintypes as W
import argparse
import importlib.util
import json
import os
from pathlib import Path
import time

helper = Path(__file__).resolve().parents[1] / "native-memory-classification" / "memory_map.py"
spec = importlib.util.spec_from_file_location("memory_metadata", helper)
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)


class RegionInfo(C.Structure):
    _fields_ = [("AllocationBase", C.c_void_p), ("AllocationProtect", W.DWORD),
                ("Flags", W.DWORD), ("RegionSize", C.c_size_t), ("CommitSize", C.c_size_t)]


def self_test():
    fn, kernel = m.native_api()
    query_dll = C.WinDLL("api-ms-win-core-memory-l1-1-4", use_last_error=True)
    query = m.api(query_dll, "QueryVirtualMemoryInformation", W.BOOL,
                  [W.HANDLE, C.c_void_p, C.c_int, C.c_void_p, C.c_size_t, C.POINTER(C.c_size_t)])
    alloc = m.api(kernel, "VirtualAlloc", C.c_void_p, [C.c_void_p, C.c_size_t, W.DWORD, W.DWORD])
    free = m.api(kernel, "VirtualFree", W.BOOL, [C.c_void_p, C.c_size_t, W.DWORD])
    current = m.api(kernel, "GetCurrentProcess", W.HANDLE, [])()
    assert C.sizeof(RegionInfo) == 32 and RegionInfo.CommitSize.offset == 24

    def inspect(handle, base):
        result, size = RegionInfo(), C.c_size_t()
        ok = query(handle, base, 0, C.byref(result), C.sizeof(result), C.byref(size))
        if not ok:
            return {"ok": False, "error": C.get_last_error()}
        assert size.value == C.sizeof(result)
        return {"ok": True, "base": int(result.AllocationBase), "bytes": result.RegionSize,
                "committedBytes": result.CommitSize, "private": bool(result.Flags & 1),
                "pagefileMapped": bool(result.Flags & 8)}

    base = alloc(None, 16 * 1024**2, m.MEM_RESERVE, 4)
    m.checked(base, "owned reserve")
    limited = None
    try:
        reserved = inspect(current, base)
        assert reserved["ok"] and reserved["bytes"] == 16 * 1024**2 and reserved["committedBytes"] == 0
        assert alloc(base, 4 * 1024**2, m.MEM_COMMIT, 4) == base
        committed = inspect(current, base)
        assert committed["ok"] and committed["private"] and committed["committedBytes"] == 4 * 1024**2
        limited = fn["open"](0x1000, False, os.getpid())
        m.checked(limited, "owned limited handle")
        limited_result = inspect(limited, base)
        basic = m.MBI()
        basic_bytes = fn["query"](limited, base, C.byref(basic), C.sizeof(basic))
        limited_basic = {"ok": basic_bytes == C.sizeof(basic),
                         "error": C.get_last_error() if not basic_bytes else 0}
        m.checked(free(base, 4 * 1024**2, 0x4000), "owned decommit")
        decommitted = inspect(current, base)
        assert decommitted["ok"] and decommitted["committedBytes"] == 0
    finally:
        if limited:
            m.checked(fn["close"](limited), "close owned limited handle")
        m.checked(free(base, 0, 0x8000), "owned release")

    create_mapping = m.api(kernel, "CreateFileMappingW", W.HANDLE,
                           [W.HANDLE, C.c_void_p, W.DWORD, W.DWORD, W.DWORD, W.LPCWSTR])
    map_view = m.api(kernel, "MapViewOfFile", C.c_void_p,
                     [W.HANDLE, W.DWORD, W.DWORD, W.DWORD, C.c_size_t])
    unmap = m.api(kernel, "UnmapViewOfFile", W.BOOL, [C.c_void_p])
    mapping = create_mapping(W.HANDLE(-1), None, 4, 0, 4 * 1024**2, None)
    m.checked(mapping, "owned pagefile mapping")
    view = None
    try:
        view = map_view(mapping, 1, 0, 0, 4 * 1024**2)  # FILE_MAP_COPY
        m.checked(view, "owned COW view")
        cow = inspect(current, view)
        assert cow["ok"] and cow["committedBytes"] == 4 * 1024**2
    finally:
        if view:
            m.checked(unmap(view), "unmap owned COW view")
        m.checked(fn["close"](mapping), "close owned mapping")
    print(json.dumps({"selfTest": "PASS", "reserved": reserved, "committed": committed,
                      "decommitted": decommitted, "cowView": cow,
                      "queryLimitedHandleControl": limited_result,
                      "virtualQueryLimitedHandleControl": limited_basic,
                      "targetAttached": False, "contentsReadOrTouched": False}))


def capture(expected, output, query_information=False):
    fn, kernel = m.native_api()
    assert C.sizeof(RegionInfo) == 32 and RegionInfo.CommitSize.offset == 24
    query = m.api(C.WinDLL("api-ms-win-core-memory-l1-1-4", use_last_error=True),
                  "QueryVirtualMemoryInformation", W.BOOL,
                  [W.HANDLE, C.c_void_p, C.c_int, C.c_void_p, C.c_size_t, C.POINTER(C.c_size_t)])
    access = 0x0400 if query_information else 0x1000
    handle = fn["open"](access, False, int(expected["pid"]))
    m.checked(handle, "OpenProcess explicit query access")
    try:
        bound = m.identity(fn, handle, expected)
        before = m.process_counters(fn, handle)
        started = time.monotonic()
        deadline = started + 20
        # Reuse the qualified classifier, but include allocation queries in the same deadline.
        observed = dict(fn)
        def bounded_call(call):
            def invoke(*args):
                m.check_deadline(deadline)
                result = call(*args)
                m.check_deadline(deadline)
                return result
            return invoke
        observed["query"] = bounded_call(fn["query"])
        observed["ws"] = bounded_call(fn["ws"])
        result = m.classify(observed, handle)
        types = {}
        for region in result["regions"]:
            types.setdefault(region["allocationBase"], set()).add(region["type"])
        charge = {}
        for allocation in result["allocations"]:
            m.check_deadline(deadline)
            info, size = RegionInfo(), C.c_size_t()
            m.checked(query(handle, allocation["base"], 0, C.byref(info), C.sizeof(info),
                            C.byref(size)), "QueryVirtualMemoryInformation")
            if size.value != C.sizeof(info) or int(info.AllocationBase or 0) != allocation["base"]:
                raise RuntimeError("Allocation metadata changed or ABI size mismatch; result rejected")
            allocation.update(regionBytes=int(info.RegionSize), allocationCommitBytes=int(info.CommitSize),
                              allocationFlags=int(info.Flags), types=sorted(types[allocation["base"]]))
            kind = "+".join(allocation["types"])
            charge[kind] = charge.get(kind, 0) + int(info.CommitSize)
        m.check_deadline(deadline)
        result.update(allocationChargeByType=charge, scanSeconds=time.monotonic()-started)
        after = m.process_counters(fn, handle)
        m.identity(fn, handle, expected)
    finally:
        m.checked(fn["close"](handle), "CloseHandle")
    result.update(identity=bound, before=before, after=after,
                  requestedAccess=("PROCESS_QUERY_INFORMATION only (0x0400)" if query_information else
                                   "PROCESS_QUERY_LIMITED_INFORMATION only (0x1000)"),
                  limitations=["Metadata only; no page contents, target GC, trim, VM_READ or allocation stacks.",
                               "Live metadata is non-atomic; allocation changes reject detectable inconsistencies.",
                               "Allocation CommitSize on mapped views includes prospective copy-on-write charge.",
                               "Region commit, allocation charge and process private commit are different counters."])
    with output.open("x", encoding="utf-8") as stream:
        json.dump(result, stream, indent=2)
        stream.write("\n")
    print(json.dumps({"output":str(output), "scanSeconds":result["scanSeconds"],
                      "groups":result["groups"], "allocationChargeByType":charge,
                      "privateCommit":after["PrivateUsage"]}))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--identity", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--query-information", action="store_true",
                        help="Explicit metadata query right (0x0400); never adds VM_READ or privilege escalation")
    args = parser.parse_args()
    if not args.identity and not args.output:
        self_test()
        return
    if not args.identity or not args.output or not args.output.is_absolute() or args.output.exists():
        parser.error("Explicit identity and new absolute output file required")
    capture(json.loads(args.identity.read_text(encoding="utf-8-sig")), args.output, args.query_information)


if __name__ == "__main__":
    main()
