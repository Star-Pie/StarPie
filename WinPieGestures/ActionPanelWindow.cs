using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using StarPie.Plugin;
using WinPieGestures.Plugins;

namespace WinPieGestures;

internal sealed class ActionPanelWindow : Window
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint handle, int index, nint value);
    private readonly InputPanelQueue _queue;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly nint _target;
    private readonly uint _pid;
    private PinnedWheelMonitor? _monitor;
    private HwndSource? _source;
    private int _cancelled;
    internal string SchemeId { get; }

    internal ActionPanelWindow(ActionPanelScheme scheme, nint target, uint pid, ActionContext? context = null)
    {
        SchemeId = scheme.Id; _target = target; _pid = pid;
        bool Valid() => Volatile.Read(ref _cancelled) == 0 && !App.IsExiting && App.MainMouseHook?.IsPaused != true && InputPanelCoordinator.IsTargetValid(target, pid);
        context ??= new ActionContext { ForegroundWindowHandle = target, LanguageCode = I18n.CurrentLanguageCode };
        _queue = new InputPanelQueue(target, pid, context.ForegroundProcessName, Valid, ActionExecutor.EnqueueAction, context, _cancellation.Token);
        Title = scheme.Name; Width = 650; Height = 430; MinWidth = 300; MinHeight = 220;
        ShowActivated = false; ShowInTaskbar = false; Focusable = false; Topmost = true;
        WindowStyle = WindowStyle.ToolWindow; ResizeMode = ResizeMode.CanResizeWithGrip;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; ActionPanelUi.Theme(this);
        var root = new Grid { Margin = new Thickness(10) };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new DockPanel(); var close = ActionPanelUi.Button(I18n.T("InputPanelCancel"), Close); close.Focusable = false;
        DockPanel.SetDock(close, Dock.Right); header.Children.Add(close); header.Children.Add(new TextBlock { Text = scheme.Name, FontSize = 16, VerticalAlignment = VerticalAlignment.Center }); root.Children.Add(header);
        var keys = new UniformGrid { Rows = scheme.Rows, Columns = scheme.Columns }; Grid.SetRow(keys, 1); root.Children.Add(keys);
        foreach (var action in scheme.Keys)
        {
            var button = ActionPanelUi.Button("", () =>
            {
                if (action == null) return;
                ActionItem? snapshot = ActionPanelManager.Prepare(action);
                _status.Text = snapshot == null ? I18n.T("InputPanelActionUnavailable") :
                    _queue.Queue(snapshot) == InputPanelQueueResult.Accepted ? "" : ActionPanelUi.T("Busy");
            });
            button.Focusable = false; button.IsEnabled = action != null;
            button.Content = ActionPanelUi.Caption(action?.Name ?? ActionPanelUi.T("Empty"));
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch; keys.Children.Add(button);
        }
        Grid.SetRow(_status, 2); root.Children.Add(_status); Content = root;
        SourceInitialized += (_, _) =>
        {
            nint handle = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(handle, -20, GetWindowLongPtr(handle, -20) | 0x08000000 | 0x00000080);
            _source = HwndSource.FromHwnd(handle); _source?.AddHook(Message);
        };
        Closed += (_, _) =>
        {
            Interlocked.Exchange(ref _cancelled, 1); _queue.Close();
            _monitor?.Dispose(); _monitor = null; _source?.RemoveHook(Message); _source = null;
            _ = System.Threading.Tasks.Task.Run(() => { try { _cancellation.Cancel(); } catch { } finally { _cancellation.Dispose(); } });
            keys.Children.Clear();
        };
    }
    private nint Message(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    { if (message == 0x0021) { handled = true; return 3; } return 0; }
    internal void Open()
    {
        new WindowInteropHelper(this).EnsureHandle();
        var guard = new InputPanelTargetGuard(Environment.TickCount64, () => Environment.TickCount64, () => _queue.IsOpen, Cancel);
        _monitor = new PinnedWheelMonitor(_target, guard.OnTargetEvent);
        if (!InputPanelCoordinator.IsTargetValid(_target, _pid)) { Cancel(); return; }
        Show();
    }
    internal void Cancel()
    {
        if (Interlocked.Exchange(ref _cancelled, 1) != 0) return;
        _queue.Close();
        if (Dispatcher.CheckAccess()) Close();
        else if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke((Action)Close);
    }
}
