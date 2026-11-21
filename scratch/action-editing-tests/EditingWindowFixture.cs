using System.ComponentModel;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Interop;
using WinPieGestures;
using ComboBox = System.Windows.Controls.ComboBox;

namespace ActionEditingTests;

internal static class EditingWindowFixture
{
    private static readonly List<SettingsWindow> Opened = new();
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static SettingsWindow Open(WheelProfile profile)
    {
        ConfigManager.CurrentConfig.ConfigMode = "Pro";
        var window = new SettingsWindow();
        Opened.Add(window);
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero) throw new InvalidOperationException("Fixture created an HWND.");
        window.SwitchToTab(2);
        var profiles = (ComboBox)window.FindName("MappingsProfileComboBox");
        profiles.SelectedItem = profile;
        if (!ReferenceEquals(profiles.SelectedItem, profile)) throw new InvalidOperationException("The fixture profile is not in the configuration.");
        return window;
    }

    internal static void Release()
    {
        foreach (var window in Opened)
        {
            // Release production subscriptions and timers; the interactive Closing handler would save the fixture on teardown.
            typeof(SettingsWindow).GetMethod("ReleaseWindowResources", Instance)!.Invoke(window, null);
            var closing = typeof(SettingsWindow).GetMethod("Window_Closing", Instance)!.CreateDelegate<CancelEventHandler>(window);
            window.Closing -= closing;
            window.Close();
        }
        Opened.Clear();
    }
}
