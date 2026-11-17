"""Fixed-identity scalar memory sampling; QUERY_LIMITED_INFORMATION only."""

import argparse
import ctypes as C
import importlib.util
import json
from pathlib import Path
import time

helper = Path(__file__).resolve().parents[1] / "native-memory-classification" / "memory_map.py"
spec = importlib.util.spec_from_file_location("memory_metadata", helper)
metadata = importlib.util.module_from_spec(spec)
spec.loader.exec_module(metadata)


class ExtendedCounters(C.Structure):
    _fields_ = metadata.MemoryCounters._fields_ + [
        ("PrivateWorkingSetSize", C.c_size_t), ("SharedCommitUsage", C.c_ulonglong)]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--identity", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--state", required=True)
    parser.add_argument("--count", type=int, default=5, choices=range(1, 13))
    parser.add_argument("--interval", type=float, default=5)
    args = parser.parse_args()
    if not args.output.is_absolute() or args.output.exists() or not 0 <= args.interval <= 6:
        parser.error("New absolute output and interval <=6 seconds required")
    expected = json.loads(args.identity.read_text(encoding="utf-8-sig"))
    fn, _ = metadata.native_api()
    handle = fn["open"](0x1000, False, int(expected["pid"]))
    metadata.checked(handle, "OpenProcess QUERY_LIMITED_INFORMATION")
    results = []
    try:
        metadata.identity(fn, handle, expected)
        for index in range(args.count):
            metadata.identity(fn, handle, expected)
            value = ExtendedCounters()
            value.cb = C.sizeof(value)
            ok = fn["memory"](handle, C.cast(C.byref(value), C.POINTER(metadata.MemoryCounters)), value.cb)
            if ok:
                row = {"timeUtc": metadata.D.datetime.now(metadata.D.timezone.utc).isoformat(),
                       **{name: int(getattr(value, name)) for name, _ in value._fields_}}
            else:
                row = metadata.process_counters(fn, handle)
                row.update(PrivateWorkingSetSize=None, SharedCommitUsage=None,
                           extendedCountersUnavailable=True)
            row.update(pid=expected["pid"], createdUtc=expected["createdUtc"],
                       state=args.state, hostSha256=expected["fileSha256"]["StarPie.dll"])
            results.append(row)
            if index + 1 < args.count:
                time.sleep(args.interval)
        metadata.identity(fn, handle, expected)
    finally:
        metadata.checked(fn["close"](handle), "CloseHandle")
    with args.output.open("x", encoding="utf-8") as output:
        json.dump(results, output, indent=2)
        output.write("\n")
    print(json.dumps({"count": len(results), "first": results[0], "last": results[-1],
                      "boundary": "Scalar counters and fixed identity only; no target GC, trim, page reads or UI actions."}))


if __name__ == "__main__":
    main()
