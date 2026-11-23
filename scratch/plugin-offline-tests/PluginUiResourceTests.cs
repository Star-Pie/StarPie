using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Xml;
using System.Xml.Linq;

internal static class PluginUiResourceTests
{
    internal static void LoadDownloadChannelControl()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                // Load the shared styles used by SettingsWindow without starting the application.
                var app = new WinPieGestures.App();
                app.InitializeComponent();
                string source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                    "../../../../../WinPieGestures/SettingsWindow.xaml"));
                var window = XDocument.Load(source).Root!;
                XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                var resources = window.Element(p + "FrameworkElement.Resources")!.Elements().Single();
                var productionControl = window.Descendants(p + "ComboBox")
                    .Single(e => (string?)e.Attribute("Name") == "PluginDownloadChannelComboBox");
                var control = new XElement(productionControl);
                control.Attribute("SelectionChanged")?.Remove();
                // Parse the real control and styles without a SettingsWindow or its handlers.
                var fragment = new XElement(p + "Grid",
                    window.Attributes().Where(a => a.IsNamespaceDeclaration).Select(a =>
                        new XAttribute(a.Name, a.Value.StartsWith("clr-namespace:", StringComparison.Ordinal)
                            && !a.Value.Contains(";assembly=", StringComparison.Ordinal)
                            ? a.Value + ";assembly=StarPie" : a.Value)),
                    new XElement(p + "Grid.Resources", new XElement(resources)),
                    control);
                using var text = new StringReader(fragment.ToString());
                using var reader = XmlReader.Create(text);
                var grid = (Grid)XamlReader.Load(reader);
                var combo = (ComboBox)grid.Children[0];
                combo.ApplyTemplate();
                grid.Measure(new Size(320, 100));
                grid.Arrange(new Rect(0, 0, 320, 100));
                string?[] channels = combo.Items.OfType<ComboBoxItem>().Select(item => item.Tag as string).ToArray();
                if (combo.Style?.TargetType != typeof(ComboBox) || combo.Template == null ||
                    !channels.SequenceEqual(new[] { "atomgit", "ghfast", "gh-proxy", "mirror", "direct", "auto" }))
                    throw new InvalidOperationException("Production download channel control did not load its style, template and six options.");
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new InvalidOperationException(
            "Production XAML resource loading failed: " + failure.GetBaseException().Message, failure);
    }
}
