using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinPieGestures;
using PathShape = System.Windows.Shapes.Path;
internal static class Program
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static void Check(bool condition, string name) { if(!condition) throw new InvalidOperationException(name); }
    [STAThread]
    private static int Main(string[] args)
    {
        if(args.Length != 3 || args[2] != "--test-instance") throw new ArgumentException("hash sandbox --test-instance required");
        foreach(string key in new[]{"LOCALAPPDATA","APPDATA","TEMP","TMP"}) { string path=Path.Combine(args[1],key); Directory.CreateDirectory(path); Environment.SetEnvironmentVariable(key,path); }
        string hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(AppConfig).Assembly.Location)));
        Console.WriteLine("HOST_SHA256="+hash); Check(hash == args[0], "candidate hash");
        typeof(ConfigManager).GetProperty("DisableAutoStartSync")!.SetValue(null,true);
        typeof(ConfigManager).GetProperty("CurrentConfig")!.SetValue(null,new AppConfig { ShowSelectedActionText=false });
        _ = new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        try { HighlightTransition(); Console.WriteLine("PASS parent-child-parent highlight transition; no Show, hooks or input"); return 0; }
        catch(Exception ex) { Console.WriteLine("FAIL "+ex.Message); return 1; }
    }
    private static void HighlightTransition()
    {
        var type = typeof(RadialWindow); var wheel = (RadialWindow)RuntimeHelpers.GetUninitializedObject(type);
        void Set(string name, object value) => type.GetField(name, Fields)!.SetValue(wheel, value);
        void Highlight(int main, int sub) => type.GetMethod("HighlightSector", Fields, null, [typeof(int), typeof(int), typeof(bool), typeof(bool)], null)!.Invoke(wheel, [main, sub, false, true]);
        var parent = new PathShape(); var child = new PathShape();
        Set("_sectorPaths", new List<PathShape> { parent }); Set("_contentPanels", new List<StackPanel>());
        Set("_sectorTransforms", new List<TranslateTransform>()); Set("_containerTransforms", new List<TranslateTransform>()); Set("_sectorAngles", new List<double>());
        Set("_subSectorPaths", new List<PathShape> { child }); Set("_subContentContainers", new List<Grid>());
        Set("_subSectorTransforms", new List<TranslateTransform>()); Set("_subContainerTransforms", new List<TranslateTransform>()); Set("_subSectorAngles", new List<double>());
        Set("_defaultSectorBrush", Brushes.White); Set("_highlightSectorBrush", Brushes.Red); Set("_sectorBorderBrush", Brushes.Gray);
        Set("_subDefaultSectorBrush", Brushes.White); Set("_subHighlightSectorBrush", Brushes.Blue); Set("_subSectorBorderBrush", Brushes.Gray);
        Set("_highlightBorderBrush", Brushes.Red); Set("_subHighlightBorderBrush", Brushes.Blue); Set("_textColorBrush", Brushes.Black);
        Set("_currentHighlightedSector", -999); Set("_currentHighlightedSubSector", -1); Set("_activeSubTierParentSector", 0);
        Set("CoreExitIcon", new PathShape()); Set("CoreCustomImageEllipse", new System.Windows.Shapes.Ellipse());
        Set("CoreSelectionTextPanel", new StackPanel());
        Set("CoreSelectionOverlay", Activator.CreateInstance(type.GetField("CoreSelectionOverlay", Fields)!.FieldType)!);
        ConfigManager.CurrentConfig.ShowSelectedActionText = false;
        Highlight(0, -1); Check(ReferenceEquals(parent.Fill, Brushes.Red), "parent selected alone");
        Highlight(0, 0); Check(ReferenceEquals(parent.Fill, Brushes.White) && ReferenceEquals(child.Fill, Brushes.Blue), "child selection removes parent highlight");
        Highlight(0, -1); Check(ReferenceEquals(parent.Fill, Brushes.Red) && !ReferenceEquals(child.Fill, Brushes.Blue), "returning to parent clears child highlight");
    }
}