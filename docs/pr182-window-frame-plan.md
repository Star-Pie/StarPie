# PR #182 caption and brand polish

Base: e0ba89c2918ba14036e4c43eeb81c9d9556918ca; main: 830893fe84e9b298d8df05bbc7b0fb7feca75ede. Worktree: G:\Users\2 Better\.codex\worktrees\pr182-review\design, codex/pr182-fixes; initially clean. The user's original acceptance passed; this added UI scope requires another visual acceptance before merge.

Owner: host WPF presentation. Implement a shared frame at the existing AppThemeManager boundary, flat theme-aware in-app brand rendering, and a themed message dialog used by host UI windows. Retain the original settings Closing path, modal results, owners and standard window operations. Exclude transparent/overlay and plugin-owned windows.

Allowed: the new presentation classes, AppThemeManager, settings logo loading, I18n, host window message-call replacements, focused scratch regressions and these design notes. Forbidden: hook/action execution/SDK/plugin runtime logic, user configuration or installed plugins, dependency changes and release artifacts.

Stages: shared brand/frame; modal prompt integration; behavior/offscreen regressions plus independent review and user acceptance. Reuse semantic brushes and frozen drawing resources. No global window/event ownership or recurring polling.

Budget: one implementation pass, at most two repairs after review; target 60 minutes. Stop and report the useful verified subset if frame compatibility or modal semantics cannot be preserved within that budget. Do not merge a changed candidate using the earlier visual acceptance.

Gates: Release zero warnings/errors, original theme and interaction regressions, focused frame/modal/localization/lifetime behavior checks with baseline failure evidence, diff hygiene, independent review, offscreen inspection, then human checks of drag/resize/maximize/restore/close, dialogs and DPI.
