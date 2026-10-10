# StarPie product context

## Register

Product: native Windows desktop settings and gesture tool. Design serves the user's configured actions.

## Users and purpose

Desktop users who want the spatial efficiency of CAD mouse gestures across applications. See architecture.md and the repository README.

## Design principles

- Predictable wheel directions and actions take priority over decoration.
- Use native .NET 8 WPF and Win32; preserve low latency, small working sets and demand-created settings windows.
- Host windows share semantic theme resources. Artwork, caption controls and prompts should read as the same application.
- Preserve the StarPie wheel-and-pointer brand silhouette while adapting its in-app rendering to the selected theme.
- Standard Windows window operations remain familiar; closing settings hides the console and does not terminate the background process.

## Anti-references and accessibility

- Avoid heavy UI frameworks, unrelated decorative motion, mismatched system-colored prompts and glossy artwork against flat themes.
- Use the existing four languages and font fallbacks.
- Maintain at least 4.5:1 body/control text contrast and keyboard-accessible window/dialog controls.

Sources: architecture.md, ui-and-wheel.md, windows-integration.md, and the user's PR #182 acceptance screenshots. This context does not authorize packaging or release.
