# beta.3 pinned wheel verification

Build with explicit `-p:StarPieCandidateDir=<timestamped Release candidate>` and an owned `OutDir`. Run the resulting DLL with an owned sandbox directory and the expected host DLL SHA-256. The loaded copy must match that hash.

The probe uses a plain WPF dispatcher without creating a Window or running StarPie.App startup. No input hooks are installed. Modifier release, target identity, hit testing and action enqueue are injected; no real commands, plugins, input or system registration run. ConfigManager honors the overridden LOCALAPPDATA and its actual resolved path is checked.

Covers missing/explicit bool roundtrips, native versus owned keyboard releases, modifier/chord matching, mouse trigger release, stale confirmation, outside-click and Esc cancellation, paused hook drains, cancellation/release interleavings, empty slots, submenu navigation and once-only dispatch through the real release path to an injected enqueue endpoint. It does not prove rendered geometry, visual hiding, real ActionExecutor queue consumption or device behavior.

The save-feedback probe follows the actual candidate SaveButton_Click IL twice, substituting save success/failure and recording UI call arguments. It checks that the disk-save result reaches the status and only failure requests a dialog. It does not execute disk writes or a real dialog; unexpected IL/calls fail closed.

Pinned hover cases inject geometry hits and record actual interaction events: outside clears to None rather than Core, repeat outside movement is silent, reentry restores selection, expanded menus survive exit/reentry, moved visual extents remain clickable, latest-point coalescing stays on the UI dispatcher, and superseded hover/click callbacks cannot affect a new session. Presentation completion recalculates a stationary pointer. Real transformed geometry and audible playback remain manual checks.

Manual checks: global switch in simple/advanced settings; right/middle/side/keyboard triggers; hold/release and long waits; core/main/submenu clicks; outer gaps and fan petals; external clicks without click-through; Esc/retrigger; Alt+Tab, target close, pause/resume, layer deletion; two mixed-DPI displays; original mode when disabled. If the trigger itself is the left mouse button, a click on a valid wheel action confirms it; Esc or an outside click cancels, since that physical click also serves as selection.
