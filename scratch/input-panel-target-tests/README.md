# Input panel target guard tests

This is a no-window, dependency-injected regression test for the new `WinPieGestures.Plugins.InputPanelTargetGuard(long startedAt, Func<long> clock, Func<bool> isOpen, Action close)` API and `OnTargetEvent(uint eventTime)`. It loads the internal guard through reflection and never constructs `PinnedWheelMonitor`, `InputPanelSession`, a WPF window, an input hook, or a plugin runtime.

Current evidence candidate:

```text
G:\Users\2 Better\.codex\worktrees\profile-trigger-wheels\design\WinPieGestures\bin\Release\build-out-20261104-0939
host SHA256 6C403E1C34991DE5397F1167C9A118A4936934845B16DF20E468A2D4EC5264CD
sdk SHA256 492E2752AD4D0B309CB2826FA889E7682B0BF1F99D3ADA36DE46F4301A5193DA
```

Build from the worktree root with an explicit candidate and evidence output:

```powershell
dotnet build .\scratch\input-panel-target-tests\InputPanelTargetTests.csproj -c Release --nologo "-p:StarPieCandidateDir=G:\Users\2 Better\.codex\worktrees\profile-trigger-wheels\design\WinPieGestures\bin\Release\build-out-20261104-0939" "-p:OutDir=H:\starpie-task-evidence\keyboard-repair-20261103\target-worker\"
```

Run:

```powershell
dotnet H:\starpie-task-evidence\keyboard-repair-20261103\target-worker\test_input_panel_target.dll "G:\Users\2 Better\.codex\worktrees\profile-trigger-wheels\design\WinPieGestures\bin\Release\build-out-20261104-0939" 6C403E1C34991DE5397F1167C9A118A4936934845B16DF20E468A2D4EC5264CD 492E2752AD4D0B309CB2826FA889E7682B0BF1F99D3ADA36DE46F4301A5193DA
```

The runner refuses to start without an explicit candidate and both hashes. It verifies the candidate host and SDK with SHA-256, loads the candidate in an isolated assembly context, and checks the internal guard constructor and `OnTargetEvent(uint)` signature by reflection.

Coverage includes pre-epoch ignoring, equal-epoch and post-epoch immediate close, delayed notification after target refocus, 32-bit tick wrap, uptime beyond 49 days, closed-state idempotence without a clock read, and closed-queue rejection of an unconsumed request. Legacy fixtures remain as no-side-effect red-state counterexamples for the rejected deferred-recovery behavior.

Boundaries: this is test-harness evidence only. It does not exercise WinEvent hooks, real foreground windows, WPF dispatch, plugin actions, input simulation, or end-to-end input-panel behavior.
