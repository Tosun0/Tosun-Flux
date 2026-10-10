using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using TosunFluxShared;

internal static class TestWpfQueueBinding
{
    // dotnet run --project Tests/WpfQueueBindingSmoke.csproj -- <MainWindow.xaml> [<backend.exe> <scratch-dir>]
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length is not (1 or 3))
                throw new ArgumentException("Supply MainWindow.xaml and optionally backend.exe and a scratch directory.");

            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
            var source = XDocument.Load(args[0]);
            var template = new XElement(source.Descendants(presentation + "ListBox")
                .Single(element => (string?)element.Attribute(xaml + "Name") == "FilesList")
                .Descendants(presentation + "DataTemplate").Single());
            // Keep the production bindings; window click handlers are outside this template test.
            foreach (var element in template.Descendants())
                element.Attribute("Click")?.Remove();
            template.SetAttributeValue(xaml + "Key", "QueueTemplate");
            var resources = new ResourceDictionary
            {
                ["MutedBrush"] = Brushes.Gray,
                ["SecondaryBrush"] = Brushes.White,
                ["SecondaryTextBrush"] = Brushes.Black,
            };
            var application = new Application { Resources = resources };
            var dictionary = (ResourceDictionary)XamlReader.Parse(
                new XElement(presentation + "ResourceDictionary",
                    new XAttribute(XNamespace.Xmlns + "x", xaml), template).ToString());
            var queue = new ObservableCollection<ConversionQueueItem>();
            var list = new ListBox { ItemsSource = queue, ItemTemplate = (DataTemplate)dictionary["QueueTemplate"] };
            VirtualizingStackPanel.SetIsVirtualizing(list, false);
            var host = new Window
            {
                Content = list, Width = 600, Height = 600,
                Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false,
            };
            host.Show();
            var item = new ConversionQueueItem("sample.png");
            queue.Add(item);
            Layout(list);
            var bar = Descendants(list).OfType<ProgressBar>().Single();
            Check(bar.Value == 0 && !bar.IsIndeterminate, "waiting row renders");
            CheckState(QueueState.Processing, 0, true);
            CheckState(QueueState.Completed, 100, false);
            CheckState(QueueState.Failed, 100, false);
            CheckState(QueueState.Waiting, 0, false);
            Console.WriteLine("PASS: production WPF queue template renders and updates waiting/processing/completed/failed/retry states");

            if (args.Length == 3)
            {
                var task = ConvertAsync(queue, list, Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
                var frame = new DispatcherFrame();
                _ = task.ContinueWith(_ => list.Dispatcher.BeginInvoke(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                task.GetAwaiter().GetResult();
            }
            host.Close();
            application.Shutdown();
            return 0;

            void CheckState(QueueState state, double progress, bool processing)
            {
                item.SetState(state, state.ToString());
                Layout(list);
                Check(bar.Value == progress && bar.IsIndeterminate == processing, state.ToString());
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static async Task ConvertAsync(ObservableCollection<ConversionQueueItem> queue, ListBox list, string backend, string scratch)
    {
        Directory.CreateDirectory(scratch);
        var input = Path.Combine(scratch, "queue-binding-input.png");
        var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255, 255, 255, 255, 255 }, 8);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(input)) encoder.Save(stream);
        var brokenInput = Path.Combine(scratch, "queue-binding-broken.png");
        File.WriteAllText(brokenInput, "invalid image");
        queue.Clear();
        var first = new ConversionQueueItem(input);
        var broken = new ConversionQueueItem(brokenInput);
        var later = new ConversionQueueItem(input);
        queue.Add(first);
        queue.Add(broken);
        Layout(list);
        var command = new ProcessStartInfo(backend)
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var argument in new[] { "convert", "--output", Path.Combine(scratch, "output"), "--target", "jpg" })
            command.ArgumentList.Add(argument);
        var processingRows = 0;
        await ConversionQueue.RunAsync(queue, command, () =>
        {
            if (first.IsProcessing && !queue.Contains(later)) queue.Add(later);
            Layout(list);
            var bars = Descendants(list).OfType<ProgressBar>().ToArray();
            Check(bars.Length == queue.Count, "all queue rows render");
            Check(bars.Count(bar => bar.IsIndeterminate) == queue.Count(row => row.IsProcessing), "processing progress renders");
            if (queue.Any(row => row.IsProcessing)) processingRows++;
        });
        Check(first.State == QueueState.Completed && later.State == QueueState.Completed, "real PNG to JPG conversion and late enqueue");
        Check(first.Outputs.Concat(later.Outputs).All(path => File.Exists(path) && new FileInfo(path).Length > 0), "converted output files");
        Check(broken.State == QueueState.Failed && broken.CanRetry, "broken input stays in failed queue row");
        Check(processingRows == 3 && Descendants(list).OfType<ProgressBar>().All(bar => bar.Value == 100 && !bar.IsIndeterminate), "finished rows render");
        Console.WriteLine("PASS: native backend converts PNG to JPG, accepts a file during processing, and isolates a broken file with WPF rows rendered throughout");
    }

    private static void Layout(ListBox list)
    {
        list.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        list.Measure(new Size(600, 600));
        list.Arrange(new Rect(0, 0, 600, 600));
        list.UpdateLayout();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + label);
    }
}
