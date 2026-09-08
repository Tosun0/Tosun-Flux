using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace TosunConverter.Wpf;

public partial class MainWindow : Window
{
    private const string SettingsKeyPath = @"Software\Tosun\Tosun Flux";
    private const string OutputPathValueName = "OutputPath";
    private readonly List<string> _files = [];
    private static readonly HashSet<string> VisualExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tif", ".tiff", ".gif", ".ico",
        ".mp4", ".mov", ".mkv", ".avi", ".webm", ".wmv", ".flv", ".m4v", ".pdf",
    };

    public MainWindow()
    {
        InitializeComponent();
        OptimizationBox.ItemsSource = new[] { "원본 유지", "품질 우선", "균형", "용량 우선" };
        ResolutionBox.ItemsSource = new[] { "원본", "4K", "QHD", "FHD", "HD" };
        AspectBox.ItemsSource = new[] { "원본", "16:9", "9:16", "1:1", "4:3", "3:4" };
        OptimizationBox.SelectedIndex = 0;
        ResolutionBox.SelectedIndex = 0;
        AspectBox.SelectedIndex = 0;
        OutputPath.Text = LoadOutputPath();
        Closed += (_, _) => SaveOutputPath();
        SourceInitialized += (_, _) => EnableAcrylic();
        UpdateVisualSettings();
    }

    private string BackendPath => Path.Combine(AppContext.BaseDirectory, "backend", "TosunConverter.Backend", "TosunConverter.Backend.exe");

    private void EnableAcrylic()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (HwndSource.FromHwnd(handle) is HwndSource source)
            source.CompositionTarget.BackgroundColor = Colors.Transparent;

        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(handle, ref margins);
        var backdrop = 3; // Acrylic/transient window backdrop on Windows 11.
        DwmSetWindowAttribute(handle, 38, ref backdrop, sizeof(int));
        var corners = 2;
        DwmSetWindowAttribute(handle, 33, ref corners, sizeof(int));
        ApplySystemTitleBarTheme(handle);
    }

    private static void ApplySystemTitleBarTheme(IntPtr handle)
    {
        // Keep the native title bar and let Windows' light/dark app setting drive it.
        var darkMode = App.IsSystemDarkMode() ? 1 : 0;
        if (DwmSetWindowAttribute(handle, 20, ref darkMode, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, 19, ref darkMode, sizeof(int));
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            await AddPathsAsync(paths);
    }

    private async void AddFiles_Click(object sender, RoutedEventArgs e) => await PickFilesAsync();

    private async void DropSurface_Click(object sender, MouseButtonEventArgs e) => await PickFilesAsync();

    private async Task PickFilesAsync()
    {
        var dialog = new OpenFileDialog { Multiselect = true, Title = "변환할 파일 선택", Filter = "모든 파일|*.*" };
        if (dialog.ShowDialog(this) == true)
            await AddPathsAsync(dialog.FileNames);
    }

    private async Task AddPathsAsync(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(File.Exists).Select(Path.GetFullPath))
            if (!_files.Contains(path, StringComparer.OrdinalIgnoreCase))
                _files.Add(path);

        FilesList.Items.Clear();
        foreach (var path in _files)
            FilesList.Items.Add($"{Path.GetFileName(path)}   ·   {Path.GetExtension(path).TrimStart('.').ToUpperInvariant()}");
        await RefreshTargetsAsync();
    }

    private async void ClearFiles_Click(object sender, RoutedEventArgs e)
    {
        _files.Clear();
        FilesList.Items.Clear();
        TargetBox.Items.Clear();
        TargetBox.SelectedIndex = -1;
        FileHint.Text = "지원 형식은 파일을 추가하면 자동으로 안내됩니다.";
        StatusText.Text = "파일을 추가해 주세요.";
        ConvertButton.IsEnabled = false;
        UpdateVisualSettings();
        await Task.CompletedTask;
    }

    private async void RemoveFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        if (ItemsControl.ContainerFromElement(FilesList, button) is not ListBoxItem item)
            return;

        var index = FilesList.ItemContainerGenerator.IndexFromContainer(item);
        if (index < 0 || index >= _files.Count)
            return;

        _files.RemoveAt(index);
        FilesList.Items.RemoveAt(index);
        if (_files.Count == 0)
        {
            TargetBox.Items.Clear();
            TargetBox.SelectedIndex = -1;
            FileHint.Text = "지원 형식은 파일을 추가하면 자동으로 안내됩니다.";
            StatusText.Text = "파일을 추가해 주세요.";
            ConvertButton.IsEnabled = false;
            UpdateVisualSettings();
            return;
        }

        await RefreshTargetsAsync();
    }

    private void TargetBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateVisualSettings();

    private void UpdateVisualSettings()
    {
        if (!IsInitialized)
            return;
        var supportsVisualOptions = _files.Count > 0 && _files.All(path => VisualExtensions.Contains(Path.GetExtension(path)));
        var pdfCompressionOnly = TargetBox.SelectedItem is string target && target.Equals(".pdf", StringComparison.OrdinalIgnoreCase);
        OptimizationBox.IsEnabled = supportsVisualOptions;
        ResolutionBox.IsEnabled = supportsVisualOptions && !pdfCompressionOnly;
        AspectBox.IsEnabled = supportsVisualOptions && !pdfCompressionOnly;
        FitChoice.IsEnabled = supportsVisualOptions && !pdfCompressionOnly;
        FillChoice.IsEnabled = supportsVisualOptions && !pdfCompressionOnly;
        StretchChoice.IsEnabled = supportsVisualOptions && !pdfCompressionOnly;
        OptimizationHint.Text = !supportsVisualOptions
            ? "최적화는 이미지 · 영상 · PDF에 적용됩니다."
            : pdfCompressionOnly
                ? "PDF 텍스트는 유지하고 내부 이미지와 구조를 최적화합니다."
                : "원본은 유지하고 새 파일로 저장합니다.";
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var initialDirectory = Directory.Exists(OutputPath.Text)
            ? OutputPath.Text
            : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var dialog = new OpenFolderDialog { Title = "저장 위치 선택", InitialDirectory = initialDirectory };
        if (dialog.ShowDialog(this) == true)
        {
            OutputPath.Text = dialog.FolderName;
            SaveOutputPath();
        }
    }

    private async Task RefreshTargetsAsync()
    {
        TargetBox.Items.Clear();
        ConvertButton.IsEnabled = false;
        if (_files.Count == 0)
            return;
        if (!File.Exists(BackendPath))
        {
            StatusText.Text = "변환 백엔드를 찾을 수 없습니다.";
            return;
        }

        var startInfo = BackendStartInfo("targets");
        foreach (var file in _files)
            startInfo.ArgumentList.Add(file);
        using var process = Process.Start(startInfo)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        using var document = JsonDocument.Parse(output.Trim());
        foreach (var target in document.RootElement.GetProperty("targets").EnumerateArray())
            TargetBox.Items.Add($".{target.GetString()}");

        if (TargetBox.Items.Count > 0)
        {
            TargetBox.SelectedIndex = 0;
            FileHint.Text = $"{_files.Count}개 파일 · 공통 변환 형식 {TargetBox.Items.Count}개";
            StatusText.Text = $"{_files.Count}개 파일을 추가했습니다.";
            ConvertButton.IsEnabled = true;
        }
        else
        {
            FileHint.Text = "선택한 파일에 공통 변환 형식이 없습니다.";
            StatusText.Text = "다른 종류의 파일을 따로 선택해 주세요.";
        }
        UpdateVisualSettings();
    }

    private async void Convert_Click(object sender, RoutedEventArgs e)
    {
        if (_files.Count == 0 || TargetBox.SelectedItem is not string selectedTarget)
            return;

        SaveOutputPath();
        ConvertButton.IsEnabled = false;
        Progress.Maximum = _files.Count;
        Progress.Value = 0;
        LogText.Clear();
        StatusText.Text = "변환 중…";

        var startInfo = BackendStartInfo("convert");
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(OutputPath.Text);
        startInfo.ArgumentList.Add("--target");
        startInfo.ArgumentList.Add(selectedTarget.TrimStart('.'));
        startInfo.ArgumentList.Add("--optimize");
        startInfo.ArgumentList.Add(new[] { "source", "quality", "balanced", "small" }[Math.Max(0, OptimizationBox.SelectedIndex)]);
        startInfo.ArgumentList.Add("--resolution");
        startInfo.ArgumentList.Add(new[] { "source", "4k", "qhd", "fhd", "hd" }[Math.Max(0, ResolutionBox.SelectedIndex)]);
        startInfo.ArgumentList.Add("--aspect");
        startInfo.ArgumentList.Add(new[] { "source", "16:9", "9:16", "1:1", "4:3", "3:4" }[Math.Max(0, AspectBox.SelectedIndex)]);
        startInfo.ArgumentList.Add("--fit");
        startInfo.ArgumentList.Add(FillChoice.IsChecked == true ? "fill" : StretchChoice.IsChecked == true ? "stretch" : "fit");
        foreach (var file in _files)
            startInfo.ArgumentList.Add(file);

        try
        {
            using var process = Process.Start(startInfo)!;
            var success = 0;
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.TryGetProperty("event", out var eventName) && eventName.GetString() == "progress")
                {
                    var index = root.GetProperty("index").GetInt32();
                    var total = root.GetProperty("total").GetInt32();
                    var source = Path.GetFileName(root.GetProperty("source").GetString());
                    var error = root.GetProperty("error");
                    Progress.Value = index;
                    StatusText.Text = $"변환 중… {index}/{total}";
                    if (error.ValueKind == JsonValueKind.Null)
                    {
                        success++;
                        var names = root.GetProperty("outputs").EnumerateArray().Select(item => Path.GetFileName(item.GetString()));
                        AppendLog($"완료: {source} → {string.Join(", ", names)}");
                    }
                    else
                    {
                        AppendLog($"실패: {source} — {error.GetString()}");
                    }
                }
            }
            await process.WaitForExitAsync();
            StatusText.Text = $"변환 완료 · {success}/{_files.Count}개";
        }
        catch (Exception error)
        {
            StatusText.Text = "변환을 시작하지 못했습니다.";
            AppendLog(error.Message);
        }
        finally
        {
            ConvertButton.IsEnabled = true;
        }
    }

    private ProcessStartInfo BackendStartInfo(string command)
    {
        var startInfo = new ProcessStartInfo(BackendPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(command);
        return startInfo;
    }

    private void AppendLog(string text)
    {
        LogText.AppendText(text + Environment.NewLine);
        LogText.ScrollToEnd();
    }

    private static string LoadOutputPath()
    {
        var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Tosun Flux-Output");
        try
        {
            using var settings = Registry.CurrentUser.OpenSubKey(SettingsKeyPath);
            return settings?.GetValue(OutputPathValueName) as string is { Length: > 0 } savedPath
                ? savedPath
                : defaultPath;
        }
        catch (Exception)
        {
            return defaultPath;
        }
    }

    private void SaveOutputPath()
    {
        var outputPath = OutputPath.Text.Trim();
        if (outputPath.Length == 0)
            return;

        try
        {
            using var settings = Registry.CurrentUser.CreateSubKey(SettingsKeyPath);
            settings?.SetValue(OutputPathValueName, outputPath);
        }
        catch (Exception)
        {
            // 설정 저장 실패는 변환 자체를 막지 않습니다.
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
