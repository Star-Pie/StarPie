# StarPie host window design

## Foundation

Native WPF with semantic DynamicResource brushes from AppThemeManager and ArtStyles. Paper, Obsidian, Grove, Glacier and Pixel remain the existing visual palette; legacy light/dark/titanium themes remain available.

## Typography and spacing

Ordinary UI text follows Windows through dynamic SystemFonts resources by default. The interface-font preference can explicitly select theme typography or an installed family; wheel and action font overrides retain their own priority. Captions are compact supporting text; page headings retain their existing hierarchy. The host caption occupies a stable 36 DIP and keeps window controls independent of settings content scaling.

## Window frame and controls

Host captions share the current window background, a subtle border, a small wheel-and-pointer mark and the current window title. Minimize, maximize/restore and close are clear vector strokes with standard Windows meanings. Close hover uses the familiar red treatment. Resize and system caption behavior belong to WPF WindowChrome/SystemCommands.

Restored host windows use an 8 DIP client corner and a matching native rounding region. Maximized frames keep square work-area bounds. Overlays retain their own transparency and geometry.

## Dialogs

Host dialogs consume the same frame and brushes. Message prompts use a compact icon, wrapping message, and standard localized result buttons. Preserve owners, default button, cancellation and Yes/No semantics. Overlays and plugin-owned windows retain their own presentation.

## Boundaries

No changes to wheel geometry, hit testing, input hooks, user actions, SDK, installed plugins, branding files or release version. Actual caption drag/resize/snap, DPI and visual quality require renewed user acceptance.
