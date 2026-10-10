# PR #182 next accepted polish stage

The user accepted 8111e4d and explicitly requested three further changes. This stage starts from a clean codex/pr182-fixes worktree at G:\Users\2 Better\.codex\worktrees\pr182-review\design, main 830893f. Previous stages retain their acceptance and repair records; this is a new bounded user authorization, not a reset of their ledger.

Scope: a modest rounded host window silhouette with square maximized bounds; Windows UI font synchronization for settings/captions/host dialogs with explicit UI/theme-font choices; stable secondary text weight on multi-layer switching with auto-expanded outer rings. Existing action fonts, geometry, SDK, hooks, installed plugins and release version remain unchanged.

Implementation: use the existing non-glass WindowChrome region behavior and matching client clip; use WPF's dynamic SystemFonts.MessageFontFamilyKey rather than modifying Windows font settings; correct the mismatch between secondary label construction and normal highlight reset.

Allowed: HostWindowFrame, UiTypography, settings font selector/partial, AppThemeManager, AppConfig's additive UI preference, ArtStyles typography wiring, RadialWindow label construction, I18n, scoped scratch behavior tests and documentation. Do not promise MacType injection into WPF's rendering pipeline, or alter registry/system font settings.

Gates: baseline failures for rounded geometry and actual multi-layer text rebuilding; system resource replacement tests and explicit font preservation; Release, existing themes/windows, interactions, focus, safe plugin selftest, diff checks and independent review; renewed human DPI/window/system-font/scroll-wheel acceptance. One implementation pass, at most two review repairs, target 60 minutes; stop with a truthful verified subset if that budget is exhausted. No merge before this changed candidate is accepted, and no packaging/release.

References: issue #126; WPF SystemFonts system resources; official WindowChromeWorker rounding region implementation. MacType renderer compatibility is a separate limitation from selecting the user's Windows UI font family.
