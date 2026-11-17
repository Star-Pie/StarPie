# Profile presentation and context-wheel regression

Run from the repository root with an explicit built candidate:

```powershell
dotnet run --project .\scratch\profile-presentation-tests\ProfilePresentationTests.csproj -c Release "-p:StarPieCandidateDir=<absolute candidate directory>" -- .\WinPieGestures\SettingsWindow.xaml --test-instance
```

The harness checks loaded DLL identity, context/parallel routing, legacy and explicit process bindings, profile/layer naming, first-layer removal, and configuration import/export persistence using isolated files. The locked-file case verifies failed-save rollback; a throwing language listener verifies that post-commit presentation failure cannot undo a committed import.

The WPF probe loads actual App/Settings resource dictionaries and measures a profile card at two widths without displaying a Window. Event handlers are removed because the probe does not instantiate SettingsWindow. RecordingGuards inspects built IL ordering/control-flow for the four conflict paths and recorder generation changes; it does not run native hooks, inject input or display dialogs.

The process redirects LOCALAPPDATA/APPDATA before ConfigManager initialization and uses --test-instance to suppress system autostart synchronization. Outputs remain under the test build artifacts directory. No real plugins/actions/system commands are invoked. GUI layout, actual recorder interaction, theme/DPI and cross-directory migration require separate human acceptance.

Regression evidence: 2000 candidate fails the recorder order guard; 2007 passes 87 checks. Earlier 1941 fails the first-layer root-key guard. These results do not prove GUI acceptance.
