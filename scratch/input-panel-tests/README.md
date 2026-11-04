# Input Panel Safety Tests

This is a no-window console harness for the host input-panel queue and static
action-binding helpers. It uses the candidate host and SDK assemblies from
`StarPieCandidateDir` and does not rebuild the host.

The program requires one argument: an absolute candidate directory containing
`StarPie.dll` and `StarPie.Plugin.Abstractions.dll`. It compares the SHA256 of
the files loaded at runtime with the supplied candidate files before running
tests.

The queue tests use a captured in-memory list as the backend. Captured requests
are never passed to `ActionExecutor.Execute`, and the harness creates no
`Application` or `Window`.

Build with a candidate-bound output directory:

```cmd
dotnet build .\scratch\input-panel-tests\InputPanelTests.csproj -c Release --nologo -p:StarPieCandidateDir="G:\Users\2 Better\.codex\worktrees\profile-trigger-wheels\design\WinPieGestures\bin\Release\build-out-20261103-1752" -p:OutDir=H:\starpie-task-evidence\temporary-keyboard-20261103\host-test-worker\
dotnet H:\starpie-task-evidence\temporary-keyboard-20261103\host-test-worker\test_process_launch.dll "G:\Users\2 Better\.codex\worktrees\profile-trigger-wheels\design\WinPieGestures\bin\Release\build-out-20261103-1752"
```

Coverage:

- queue ordering and deep snapshots of `PluginActionRef` and `ExtensionData`
- 64 pending requests and `Busy` on the 65th
- one-shot `OnConsumed` release and repeated-consumption behavior
- close invalidation, validity callback invalidation, and enqueue-failure release
- static binding parsing and `BuildAction` boundaries
- structural-only checks for the additive SDK 1.15 context
- a safe shallow-copy counterexample proving the deep-snapshot assertion can fail
- OEM and punctuation hotkey aliases normalized to one parseable key step
- malformed, multi-step, duplicate-modifier, whitespace, and overlong hotkey rejection
- pre-scheduler target rejection and sequential/background post-scheduler invalidation
- target-context identity preservation instead of fresh foreground collection
- session-cancellation cleanup without counting a plugin fault
- 64 live fake background contributions retaining capacity until real completion
- sequential and background timeout observation retaining capacity until true completion
- one-shot capacity release and `DescribeAction` requiring a registered action
- additive SDK `ResolveActionAsync` structural coverage
- private panel hotkey execution through the fake seam with letter, digit, and modifier chords
- private hotkey log-delta inspection using the isolated AppLogger file
- invalid private hotkey rejection before fake execution or real input
- private hotkey exception logging reduced to exception type only
- real private `ExecuteCore` reflection path and `ProcessActionEnvelope` exception sanitization

The invoker tests use a `DispatchProxy` fake contribution. They create no
`Application` or `Window`, execute no real plugin code, inject no input, and do
not access the real plugin installation.

The 1752 and 1808 candidate results are temporary evidence. The 1817 candidate
adds deferred consumption coverage; each result remains bound to its exact
candidate and is not final acceptance.

Candidate `build-out-20261103-1821` contains
`ExecuteHotkey(string, bool)` and `InputPanelHotkeyExecutionOverride`. The full
suite passed `22 passed / 0 failed`, including private letter, digit and chord
execution through the fake seam with no value-bearing log delta. Its host DLL
SHA256 is `EB2FB98605F7FA6DF156F10BB2793FC82C0517A294C2A08AE7F4449346B53916`.

The private-exception logging regression targets the next frozen candidate.
Candidate 1821 predates the sanitized `ExecuteCore` and `ProcessActionEnvelope`
catch changes, so that test must not be run against 1821.
