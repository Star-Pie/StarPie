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
                string source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                    "../../../../../WinPieGestures/SettingsWindow.xaml"));
                var window = XDocument.Load(source).Root!;
                XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                var resources = window.Element(p + "FrameworkElement.Resources")!.Elements().Single();
                var productionControl = window.Descendants(p + "ComboBox")
                    .Single(e => (string?)e.Attribute("Name") == "PluginDownloadChannelComboBox");
                // Parse the real resources and actual control, without constructing App/Window,
                // loading plugins, attaching hooks or displaying a popup.
                var fragment = new XElement(p + "Grid",
                    window.Attributes().Where(a => a.IsNamespaceDeclaration).Select(a =>
                        new XAttribute(a.Name, a.Value.StartsWith("clr-namespace:", StringComparison.Ordinal)
                            && !a.Value.Contains(";assembly=", StringComparison.Ordinal)
                            ? a.Value + ";assembly=StarPie" : a.Value)),
                    new XElement(p + "Grid.Resources", new XElement(resources)),
                    new XElement(productionControl));
                using var text = new StringReader(fragment.ToString());
                using var reader = XmlReader.Create(text);
                var grid = (Grid)XamlReader.Load(reader);
                var combo = (ComboBox)grid.Children[0];
                combo.ApplyTemplate();
                grid.Measure(new Size(320, 100));
                grid.Arrange(new Rect(0, 0, 320, 100));
                if (combo.Style?.TargetType != typeof(ComboBox) || combo.Items.Count != 4 || combo.Template == null)
                    throw new InvalidOperationException("Production download channel control did not load its style/template and four options.");
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
