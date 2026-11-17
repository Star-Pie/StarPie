# Macro and Alt input regression

This no-window harness runs the actual explicitly selected host DLL. Supply its
absolute path, expected SHA-256, and an owned sandbox. The loaded assembly bytes
must match before any test runs. Native input sending, legacy keyboard input,
key-state reads and virtual-key mapping are replaced with in-memory delegates.
Only a plain WPF dispatcher is created; no product startup, windows, hooks,
plugin actions, system registration or real keyboard injection runs.

Build with `StarPieCandidateDir` and an owned `OutDir`, then run the resulting
`test_process_launch.dll` with the three required arguments. Never rebuild or
resolve the host from its default output directory.

Coverage: exact Alt down/up, ordered Alt/A/E and multi-step parsing, modifier
release order, borrowed modifiers and loss before the main key, serialized
concurrent host calls, private input, PrintScreen's atomic pair, extended flags,
no detached Win releases, Unicode text semantics, partial/zero/exceptional
SendInput results, recovery of accepted downs only, and wheel cancellation/end
without blanket modifier releases. Failed sends stop subsequent macro keys.

The observability-only candidate retained the old behavior and failed 14 of 20
checks. An intermediate fix passed 22; independent review then produced two
additional failing concurrency/state cases on that candidate. Final candidate
`build-out-20261105-2006` passed all 24 cases, including an independent rerun.
Receipts are in the separately owned `macro-alt-20261105` task evidence folder.

This proves fake-input contracts, not EPLAN, hardware, focus, AltGr or UIPI
acceptance. The host gate coordinates only its controlled input paths. User
input can change after the final state check, and failed recovery cannot prove
the OS released a key. Those cases retain explicit manual verification.
