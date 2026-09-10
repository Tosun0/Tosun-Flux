using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace TosunFlux;

public partial class MainWindow : Window
{
    private const string SettingsKeyPath = @"Software\Tosun\Tosun Flux";
    private const string OutputPathValueName = "OutputPath";
    private readonly List<string> _files = [];
    private readonly System.Windows.Forms.NotifyIcon _trayIcon;
    private readonly System.Windows.Threading.DispatcherTimer _tosunSpeechTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private UpdateInfo? _availableUpdate;
    private bool _allowClose;
    private bool _syncingCustomSizeFields;
    private readonly List<SourceMetadata> _sourceMetadata = [];
    private int? SourceWidth => _sourceMetadata.FirstOrDefault()?.Width;
    private int? SourceHeight => _sourceMetadata.FirstOrDefault()?.Height;
    private double? SourceFrameRate => _sourceMetadata.FirstOrDefault()?.FrameRate;
    private const int Scale2ResolutionIndex = 7;
    private const int Scale4ResolutionIndex = 8;
    private const int CustomResolutionIndex = 9;
    private static readonly HashSet<string> VisualExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tif", ".tiff", ".gif", ".ico",
        ".mp4", ".mov", ".mkv", ".avi", ".webm", ".wmv", ".flv", ".m4v", ".pdf",
    };
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".mkv", ".avi", ".webm", ".wmv", ".flv", ".m4v",
    };

    public MainWindow()
    {
        InitializeComponent();
        _tosunSpeechTimer.Tick += (_, _) =>
        {
            TosunSpeechBubble.Visibility = Visibility.Collapsed;
            _tosunSpeechTimer.Stop();
        };
        OptimizationBox.ItemsSource = new[] { "원본 유지", "품질 우선", "균형", "용량 우선" };
        ResolutionBox.ItemsSource = new[] { "원본", "4K", "4K UHD", "QHD", "FHD", "HD", "SD", "2x AI 업스케일", "4x AI 업스케일", "직접 지정" };
        AspectBox.ItemsSource = new[] { "원본", "16:9", "9:16", "1:1", "4:3", "3:4" };
        FrameRateBox.ItemsSource = new[] { "원본", "23.976", "24", "25", "29.97", "30", "50", "59.94", "60" };
        OptimizationBox.SelectedIndex = 0;
        ResolutionBox.SelectedIndex = 0;
        AspectBox.SelectedIndex = 0;
        FrameRateBox.SelectedIndex = 0;
        OutputPath.Text = LoadOutputPath();
        _trayIcon = CreateTrayIcon();
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
        Loaded += MainWindow_Loaded;
        SourceInitialized += (_, _) => EnableAcrylic();
        UpdateVisualSettings();
    }

    private string BackendPath => Path.Combine(AppContext.BaseDirectory, "backend", "TosunFluxBackend", "TosunFluxBackend.exe");

    private System.Windows.Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("열기", null, (_, _) => ShowFromTray());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => ExitApplication());

        return new System.Windows.Forms.NotifyIcon
        {
            Text = "Tosun Flux",
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
            return;

        e.Cancel = true;
        Hide();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        SaveOutputPath();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _allowClose = true;
        Close();
    }

    private void EnableAcrylic()
    {
        EnableAcrylicBackdrop(this);
    }

    private static void EnableAcrylicBackdrop(Window window, bool useNativeCorners = true)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (HwndSource.FromHwnd(handle) is HwndSource source)
            source.CompositionTarget.BackgroundColor = Colors.Transparent;

        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(handle, ref margins);
        var backdrop = 3; // Acrylic/transient window backdrop on Windows 11.
        DwmSetWindowAttribute(handle, 38, ref backdrop, sizeof(int));
        var corners = useNativeCorners ? 2 : 1;
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

    private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void TosunMascot_Click(object sender, MouseButtonEventArgs e)
    {
        TosunSpeechBubble.Visibility = Visibility.Visible;
        _tosunSpeechTimer.Stop();
        _tosunSpeechTimer.Start();
        e.Handled = true;
    }

    private async void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] paths)
            await AddPathsAsync(paths);
    }

    private async void AddFiles_Click(object sender, RoutedEventArgs e) => await PickFilesAsync();

    private async void DropSurface_Click(object sender, MouseButtonEventArgs e) => await PickFilesAsync();

    private async Task PickFilesAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "변환할 파일 선택", Filter = "모든 파일|*.*" };
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
        _sourceMetadata.Clear();
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
        if (sender is not System.Windows.Controls.Button button)
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
            _sourceMetadata.Clear();
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

    private void OptimizationBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateEstimatedSize();

    private void FrameRateBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateEstimatedSize();

    private void ResolutionBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateVisualSettings();

    private void AspectBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateVisualSettings();

    private void CustomSizeBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingCustomSizeFields || !IsInitialized)
            return;

        if (ResolutionBox.SelectedIndex != CustomResolutionIndex)
        {
            ResolutionBox.SelectedIndex = CustomResolutionIndex;
            UpdateVisualSettings();
            return;
        }

        // 직접 지정 상태에서는 입력 중인 현재 값을 기준으로 예상 용량을 갱신합니다.
        UpdateEstimatedSize();
    }

    private void UpdateVisualSettings()
    {
        if (!IsInitialized)
            return;
        var supportsVisualOptions = _files.Count > 0 && _files.All(path => VisualExtensions.Contains(Path.GetExtension(path)));
        var supportsVideoOptions = _files.Count > 0 && _files.All(path => VideoExtensions.Contains(Path.GetExtension(path)));
        var pdfCompressionOnly = TargetBox.SelectedItem is TargetChoice { Key: "pdf" };
        var customResolution = ResolutionBox.SelectedIndex == CustomResolutionIndex;
        var scaleResolution = ResolutionBox.SelectedIndex is Scale2ResolutionIndex or Scale4ResolutionIndex;
        OptimizationBox.IsEnabled = supportsVisualOptions;
        ResolutionBox.IsEnabled = supportsVisualOptions && !pdfCompressionOnly;
        AspectBox.IsEnabled = supportsVisualOptions && !pdfCompressionOnly && !customResolution && !scaleResolution;
        FrameRateBox.IsEnabled = supportsVideoOptions && !pdfCompressionOnly;
        UpdateFrameRateVisibility(supportsVideoOptions && !pdfCompressionOnly);
        var showCustomResolution = supportsVisualOptions && !pdfCompressionOnly;
        CustomSizePanel.Visibility = showCustomResolution ? Visibility.Visible : Visibility.Collapsed;
        FitChoice.IsEnabled = supportsVisualOptions && !pdfCompressionOnly;
        FillChoice.IsEnabled = supportsVisualOptions && !pdfCompressionOnly;
        StretchChoice.IsEnabled = supportsVisualOptions && !pdfCompressionOnly;
        OptimizationHint.Text = !supportsVisualOptions
            ? "최적화는 이미지 · 영상 · PDF에 적용됩니다."
            : pdfCompressionOnly
                ? "PDF 텍스트는 유지하고 내부 이미지와 구조를 최적화합니다."
                : "원본은 유지하고 새 파일로 저장합니다.";
        FrameRateBox.ToolTip = SourceFrameRate is double sourceFrameRate && sourceFrameRate > 0
            ? $"원본 프레임: {sourceFrameRate:0.###} fps"
            : "영상의 출력 프레임을 선택합니다.";
        UpdateCustomSizePreview();
        UpdateEstimatedSize();
    }

    private void UpdateFrameRateVisibility(bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        FrameRateLabel.Visibility = visibility;
        FrameRatePanel.Visibility = visibility;
        var separatorWidth = visible ? new GridLength(8) : new GridLength(0);
        var frameRateWidth = visible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        OutputLabelsGrid.ColumnDefinitions[3].Width = separatorWidth;
        OutputLabelsGrid.ColumnDefinitions[4].Width = frameRateWidth;
        OutputControlsGrid.ColumnDefinitions[3].Width = separatorWidth;
        OutputControlsGrid.ColumnDefinitions[4].Width = frameRateWidth;
    }

    private void UpdateCustomSizePreview()
    {
        if (_syncingCustomSizeFields || ResolutionBox.SelectedIndex == CustomResolutionIndex)
            return;

        var dimensions = GetPreviewDimensions();
        if (dimensions is null)
            return;

        _syncingCustomSizeFields = true;
        CustomWidthBox.Text = dimensions.Value.Width.ToString();
        CustomHeightBox.Text = dimensions.Value.Height.ToString();
        _syncingCustomSizeFields = false;
    }

    private (int Width, int Height)? GetPreviewDimensions()
    {
        if (_files.Count == 0 || ResolutionBox.SelectedIndex == CustomResolutionIndex)
            return null;

        return GetPreviewDimensions(SourceWidth ?? 1920, SourceHeight ?? 1080);
    }

    private (int Width, int Height) GetPreviewDimensions(int sourceWidth, int sourceHeight)
    {
        if (ResolutionBox.SelectedIndex is Scale2ResolutionIndex or Scale4ResolutionIndex)
        {
            var factor = ResolutionBox.SelectedIndex == Scale4ResolutionIndex ? 4d : 2d;
            return (Even((int)Math.Round(sourceWidth * factor)), Even((int)Math.Round(sourceHeight * factor)));
        }

        var resolution = ResolutionBox.SelectedIndex switch
        {
            1 => (Width: 4096, Height: 2160),
            2 => (Width: 3840, Height: 2160),
            3 => (Width: 2560, Height: 1440),
            4 => (Width: 1920, Height: 1080),
            5 => (Width: 1280, Height: 720),
            6 => (Width: 720, Height: 480),
            _ => (Width: sourceWidth, Height: sourceHeight),
        };

        if (AspectBox.SelectedIndex == 0)
        {
            var scale = Math.Min((double)resolution.Width / sourceWidth, (double)resolution.Height / sourceHeight);
            return (Even((int)Math.Round(sourceWidth * scale)), Even((int)Math.Round(sourceHeight * scale)));
        }

        var ratio = AspectBox.SelectedIndex switch
        {
            1 => 16d / 9d,
            2 => 9d / 16d,
            3 => 1d,
            4 => 4d / 3d,
            5 => 3d / 4d,
            _ => sourceWidth / (double)sourceHeight,
        };
        var longEdge = ResolutionBox.SelectedIndex == 0
            ? Math.Max(sourceWidth, sourceHeight)
            : resolution.Width;
        return ratio >= 1
            ? (Even(longEdge), Even((int)Math.Round(longEdge / ratio)))
            : (Even((int)Math.Round(longEdge * ratio)), Even(longEdge));
    }

    private static int Even(int value) => Math.Max(2, value / 2 * 2);

    private (int Width, int Height)? GetActivePreviewDimensions(int sourceWidth, int sourceHeight)
    {
        if (ResolutionBox.SelectedIndex == CustomResolutionIndex &&
            int.TryParse(CustomWidthBox.Text, out var width) &&
            int.TryParse(CustomHeightBox.Text, out var height) &&
            width is >= 2 and <= 16384 && height is >= 2 and <= 16384)
        {
            return (width, height);
        }

        return GetPreviewDimensions(sourceWidth, sourceHeight);
    }

    private void UpdateEstimatedSize()
    {
        if (!IsInitialized || _files.Count == 0 || TargetBox.SelectedItem is not TargetChoice target)
        {
            OriginalSizeText.Text = string.Empty;
            EstimatedSizeText.Text = "예상 용량은 파일을 추가하면 표시됩니다.";
            return;
        }

        try
        {
            var sourceBytes = _files.Sum(path => new FileInfo(path).Length);
            OriginalSizeText.Text = $"원본 용량 · {FormatBytes(sourceBytes)}";
            var isUnchangedConversion = target.Key is not ("png-sequence" or "jpg-sequence") &&
                OptimizationBox.SelectedIndex == 0 &&
                ResolutionBox.SelectedIndex == 0 &&
                AspectBox.SelectedIndex == 0 &&
                FrameRateBox.SelectedIndex == 0 &&
                _files.All(path => NormalizeFormat(Path.GetExtension(path)) == target.Key);
            if (isUnchangedConversion)
            {
                EstimatedSizeText.Text = $"예상 용량 · {FormatBytes(sourceBytes)} (원본 동일)";
                return;
            }

            var formatMultiplier = target.Key switch
            {
                "png" => 0.95,
                "jpg" => 0.55,
                "webp" => 0.45,
                "bmp" => 3.2,
                "tiff" => 1.2,
                "gif" => 0.7,
                "pdf" => 0.8,
                "png-sequence" => 2.8,
                "jpg-sequence" => 0.7,
                "mp4" => 1.0,
                "webm" => 0.6,
                "mov" => 0.95,
                "mkv" => 0.85,
                "avi" => 1.0,
                "mp3" or "wav" or "flac" or "m4a" or "ogg" => 0.75,
                _ => 1.0,
            };

            var optimizationMultiplier = OptimizationBox.SelectedIndex switch
            {
                1 => 1.1,
                2 => 0.8,
                3 => 0.55,
                _ => 1.0,
            };

            var isVideoTarget = target.Key is "mp4" or "webm" or "mov" or "mkv" or "avi" or "gif" or "png-sequence" or "jpg-sequence";
            var isSequenceTarget = target.Key is "png-sequence" or "jpg-sequence";
            var estimate = 0d;
            for (var index = 0; index < _files.Count; index++)
            {
                var sourceFileBytes = new FileInfo(_files[index]).Length;
                var metadata = index < _sourceMetadata.Count ? _sourceMetadata[index] : null;
                if (isSequenceTarget && metadata is { Width: > 0, Height: > 0, Duration: > 0 })
                {
                    var preview = GetActivePreviewDimensions(metadata.Width.Value, metadata.Height.Value) ?? (metadata.Width.Value, metadata.Height.Value);
                    var frameRate = SelectedFrameRate() ?? metadata.FrameRate ?? 30;
                    var bytesPerPixel = target.Key == "png-sequence" ? 0.5 : 0.12;
                    estimate += preview.Width * (double)preview.Height * metadata.Duration.Value * frameRate * bytesPerPixel * optimizationMultiplier;
                    continue;
                }

                var itemEstimate = sourceFileBytes * formatMultiplier * optimizationMultiplier;
                if (target.Key != "pdf" && metadata is { Width: > 0, Height: > 0 })
                {
                    var preview = GetActivePreviewDimensions(metadata.Width.Value, metadata.Height.Value);
                    if (preview is not null)
                    {
                        var sourceArea = (double)metadata.Width.Value * metadata.Height.Value;
                        itemEstimate *= preview.Value.Width * (double)preview.Value.Height / sourceArea;
                    }
                }

                if (isVideoTarget && metadata?.FrameRate is double sourceFrameRate && sourceFrameRate > 0 && SelectedFrameRate() is double outputFrameRate && outputFrameRate > 0)
                    itemEstimate *= outputFrameRate / sourceFrameRate;
                estimate += itemEstimate;
            }

            var uncertainty = isSequenceTarget ? 0.55
                : isVideoTarget ? 0.4
                : target.Key == "pdf" || _files.All(path => Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) ? 0.45
                : 0.25;
            var lower = Math.Max(1, estimate * (1 - uncertainty));
            var upper = Math.Max(lower, estimate * (1 + uncertainty));
            EstimatedSizeText.Text = $"예상 용량 · {FormatBytes(lower)}~{FormatBytes(upper)}";
        }
        catch (IOException)
        {
            OriginalSizeText.Text = "원본 용량을 읽지 못했습니다.";
            EstimatedSizeText.Text = "예상 용량을 계산하지 못했습니다.";
        }
    }

    private double? SelectedFrameRate()
    {
        return FrameRateBox.SelectedIndex > 0 &&
               double.TryParse(FrameRateBox.SelectedItem?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var frameRate)
            ? frameRate
            : null;
    }

    private static string FormatBytes(double bytes)
    {
        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var unit = 0;
        while (bytes >= 1024 && unit < units.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes:0} {units[unit]}" : $"{bytes:0.0} {units[unit]}";
    }

    private static string NormalizeFormat(string extension)
    {
        var format = extension.TrimStart('.').ToLowerInvariant();
        return format switch
        {
            "jpeg" => "jpg",
            "tif" => "tiff",
            "markdown" => "md",
            _ => format,
        };
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
        _sourceMetadata.Clear();
        ConvertButton.IsEnabled = false;
        if (_files.Count == 0)
            return;
        if (!File.Exists(BackendPath))
        {
            StatusText.Text = "변환 백엔드를 찾을 수 없습니다.";
            return;
        }

        try
        {
            var startInfo = BackendStartInfo("targets");
            foreach (var file in _files)
                startInfo.ArgumentList.Add(file);
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("변환 백엔드를 실행할 수 없습니다.");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = await outputTask;
            if (process.ExitCode != 0)
                throw new InvalidOperationException((await errorTask).Trim());
            using var document = JsonDocument.Parse(output.Trim());
            if (document.RootElement.TryGetProperty("metadata", out var metadata))
            {
                foreach (var item in metadata.EnumerateArray())
                {
                    int? width = item.TryGetProperty("width", out var widthValue) && widthValue.ValueKind == JsonValueKind.Number ? widthValue.GetInt32() : null;
                    int? height = item.TryGetProperty("height", out var heightValue) && heightValue.ValueKind == JsonValueKind.Number ? heightValue.GetInt32() : null;
                    double? frameRate = item.TryGetProperty("fps", out var frameRateValue) && frameRateValue.ValueKind == JsonValueKind.String &&
                                    double.TryParse(frameRateValue.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedFrameRate)
                        ? parsedFrameRate
                        : null;
                    double? duration = item.TryGetProperty("duration", out var durationValue) && durationValue.ValueKind == JsonValueKind.Number
                        ? durationValue.GetDouble()
                        : null;
                    _sourceMetadata.Add(new SourceMetadata(width, height, frameRate, duration));
                }
            }
            foreach (var target in document.RootElement.GetProperty("targets").EnumerateArray())
            {
                var key = target.GetString() ?? string.Empty;
                TargetBox.Items.Add(new TargetChoice(TargetLabel(key), key));
            }
        }
        catch (Exception error)
        {
            FileHint.Text = "변환 백엔드에 연결하지 못했습니다.";
            StatusText.Text = "백엔드 연결 실패";
            AppendLog(error.Message);
            return;
        }

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
        if (_files.Count == 0 || TargetBox.SelectedItem is not TargetChoice selectedTarget)
            return;
        if (!TryGetCustomDimensions(out var customWidth, out var customHeight))
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
        startInfo.ArgumentList.Add(selectedTarget.Key);
        startInfo.ArgumentList.Add("--optimize");
        startInfo.ArgumentList.Add(new[] { "source", "quality", "balanced", "small" }[Math.Max(0, OptimizationBox.SelectedIndex)]);
        startInfo.ArgumentList.Add("--resolution");
        startInfo.ArgumentList.Add(new[] { "source", "4k", "4k-uhd", "qhd", "fhd", "hd", "sd", "source", "source", "source" }[Math.Clamp(ResolutionBox.SelectedIndex, 0, CustomResolutionIndex)]);
        startInfo.ArgumentList.Add("--scale-factor");
        var scaleFactor = ResolutionBox.SelectedIndex == Scale4ResolutionIndex ? 4d : ResolutionBox.SelectedIndex == Scale2ResolutionIndex ? 2d : 1d;
        startInfo.ArgumentList.Add(scaleFactor.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--upscale-engine");
        startInfo.ArgumentList.Add(scaleFactor == 1d ? "resize" : "ai");
        startInfo.ArgumentList.Add("--aspect");
        startInfo.ArgumentList.Add(ResolutionBox.SelectedIndex is Scale2ResolutionIndex or Scale4ResolutionIndex or CustomResolutionIndex ? "source" : new[] { "source", "16:9", "9:16", "1:1", "4:3", "3:4" }[Math.Clamp(AspectBox.SelectedIndex, 0, 5)]);
        startInfo.ArgumentList.Add("--fps");
        startInfo.ArgumentList.Add(FrameRateBox.SelectedIndex <= 0 ? "source" : FrameRateBox.SelectedItem?.ToString() ?? "source");
        startInfo.ArgumentList.Add("--fit");
        startInfo.ArgumentList.Add(FillChoice.IsChecked == true ? "fill" : StretchChoice.IsChecked == true ? "stretch" : "fit");
        if (customWidth is not null && customHeight is not null)
        {
            startInfo.ArgumentList.Add("--width");
            startInfo.ArgumentList.Add(customWidth.Value.ToString());
            startInfo.ArgumentList.Add("--height");
            startInfo.ArgumentList.Add(customHeight.Value.ToString());
        }
        foreach (var file in _files)
            startInfo.ArgumentList.Add(file);

        long outputBytes = 0;
        try
        {
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("변환 백엔드를 실행할 수 없습니다.");
            var errorTask = process.StandardError.ReadToEndAsync();
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
                        var outputs = root.GetProperty("outputs").EnumerateArray().Select(item => item.GetString()).Where(path => path is not null).Cast<string>().ToArray();
                        var convertedBytes = outputs.Where(File.Exists).Sum(path => new FileInfo(path).Length);
                        outputBytes += convertedBytes;
                        AppendLog($"완료: {source} → {string.Join(", ", outputs.Select(Path.GetFileName))} · {FormatBytes(convertedBytes)}");
                    }
                    else
                    {
                        AppendLog($"실패: {source} — {error.GetString()}");
                    }
                }
            }
            await process.WaitForExitAsync();
            var backendError = (await errorTask).Trim();
            if (process.ExitCode != 0)
            {
                StatusText.Text = $"변환 실패 · {success}/{_files.Count}개";
                if (backendError.Length > 0)
                    AppendLog(backendError);
            }
            else
            {
                StatusText.Text = $"변환 완료 · {success}/{_files.Count}개";
            }
            if (outputBytes > 0)
                EstimatedSizeText.Text = $"결과 용량 · {FormatBytes(outputBytes)}";
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

    private bool TryGetCustomDimensions(out int? width, out int? height)
    {
        width = null;
        height = null;
        if (ResolutionBox.SelectedIndex != CustomResolutionIndex)
            return true;

        if (int.TryParse(CustomWidthBox.Text, out var parsedWidth) &&
            int.TryParse(CustomHeightBox.Text, out var parsedHeight) &&
            parsedWidth is >= 2 and <= 16384 && parsedHeight is >= 2 and <= 16384)
        {
            width = parsedWidth;
            height = parsedHeight;
            return true;
        }

        System.Windows.MessageBox.Show(this, "직접 해상도는 가로·세로 모두 2~16384 픽셀로 입력해 주세요.", "Tosun Flux", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private static string TargetLabel(string key) => key switch
    {
        "png-sequence" => ".png Sequence",
        "jpg-sequence" => ".jpg Sequence",
        _ => $".{key}",
    };

    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        var helpWindow = new Window
        {
            Owner = this,
            Title = "Tosun Flux 도움말",
            Width = 650,
            Height = 610,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FontFamily = FontFamily,
            Background = System.Windows.Media.Brushes.Transparent,
            AllowsTransparency = false,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
        };
        helpWindow.SourceInitialized += (_, _) => EnableHelpBackdrop(helpWindow);

        helpWindow.Resources[typeof(System.Windows.Controls.Button)] = FindResource(typeof(System.Windows.Controls.Button));
        helpWindow.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = FindResource(typeof(System.Windows.Controls.Primitives.ScrollBar));

        var layout = new Grid { Margin = new Thickness(24) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid
        {
            MinHeight = 64,
            Background = System.Windows.Media.Brushes.Transparent,
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel();
        heading.VerticalAlignment = VerticalAlignment.Center;
        heading.Children.Add(new TextBlock
        {
            Text = "변환 도움말",
            FontSize = 22,
            FontWeight = FontWeights.Black,
            Foreground = (System.Windows.Media.Brush)FindResource("TextBrush"),
        });
        heading.Children.Add(new TextBlock
        {
            Text = "지원 형식과 출력 동작을 한눈에 확인하세요.",
            Margin = new Thickness(1, 6, 0, 0),
            FontSize = 12,
            FontWeight = FontWeights.Light,
            Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
        });
        header.Children.Add(heading);
        var closeButton = new System.Windows.Controls.Button
        {
            Content = "×",
            Width = 38,
            Height = 38,
            Padding = new Thickness(0),
            FontSize = 20,
            FontWeight = FontWeights.Light,
            Background = (System.Windows.Media.Brush)FindResource("SecondaryBrush"),
            Foreground = (System.Windows.Media.Brush)FindResource("SecondaryTextBrush"),
            ToolTip = "닫기",
            VerticalAlignment = VerticalAlignment.Center,
        };
        closeButton.Click += (_, _) => helpWindow.Close();
        Grid.SetColumn(closeButton, 1);
        header.Children.Add(closeButton);
        header.MouseLeftButtonDown += (_, args) =>
        {
            if (args.OriginalSource is System.Windows.Controls.Button)
                return;

            helpWindow.DragMove();
            args.Handled = true;
        };
        layout.Children.Add(header);

        var guideStack = new StackPanel();
        AddHelpSection(guideStack, "이미지", "PNG · JPG/JPEG · WEBP · BMP · TIFF · GIF → PNG, JPG, WEBP, BMP, TIFF, GIF, PDF");
        AddHelpSection(guideStack, "PDF", "PDF → PNG, JPG, PDF\nPDF 최적화는 텍스트를 유지하면서 내부 이미지와 구조를 줄입니다.");
        AddHelpSection(guideStack, "영상", "MP4 · WEBM · MOV · MKV · AVI · GIF → MP4, WEBM, MOV, MKV, AVI, GIF\n영상 → .png Sequence 또는 .jpg Sequence로 프레임을 추출할 수 있습니다.");
        AddHelpSection(guideStack, "데이터 · 음성", "CSV/TSV/JSON 상호 변환\nMP3 · WAV · FLAC · M4A · OGG 형식 간 변환");
        AddHelpSection(guideStack, "출력 설정", "해상도·화면비·맞춤 방식·프레임 변환은 가능한 파일에서만 표시됩니다. 원본 설정과 같은 형식은 원본을 그대로 저장하며, 예상 용량은 완료 후 실제 결과 용량으로 바뀝니다.");

        var guideSurface = new Border
        {
            Background = (System.Windows.Media.Brush)FindResource("StrongGlassBrush"),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(14),
            ClipToBounds = true,
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = guideStack,
            },
        };
        Grid.SetRow(guideSurface, 2);
        layout.Children.Add(guideSurface);

        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(new TextBlock
        {
            Text = "원본 유지 · 고품질 변환 · 실제 결과 용량 지원",
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
            FontWeight = FontWeights.Light,
            Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
        });
        var footerCloseButton = new System.Windows.Controls.Button
        {
            Content = "닫기",
            Width = 104,
            Height = 42,
            Margin = new Thickness(14, 0, 0, 0),
            Background = (System.Windows.Media.Brush)FindResource("AccentBrush"),
            Foreground = System.Windows.Media.Brushes.White,
        };
        footerCloseButton.Click += (_, _) => helpWindow.Close();
        Grid.SetColumn(footerCloseButton, 1);
        footer.Children.Add(footerCloseButton);
        Grid.SetRow(footer, 4);
        layout.Children.Add(footer);

        helpWindow.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
                helpWindow.Close();
        };
        helpWindow.Content = new Border
        {
            Background = (System.Windows.Media.Brush)FindResource("StrongGlassBrush"),
            CornerRadius = new CornerRadius(30),
            Padding = new Thickness(1),
            ClipToBounds = true,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 34, ShadowDepth = 10, Opacity = 0.42 },
            Child = new Border
            {
                Background = (System.Windows.Media.Brush)FindResource("GlassBrush"),
                CornerRadius = new CornerRadius(29),
                ClipToBounds = true,
                Child = layout,
            },
        };
        helpWindow.ShowDialog();
    }

    private void AddHelpSection(StackPanel host, string title, string description)
    {
        var section = new StackPanel();
        section.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = (System.Windows.Media.Brush)FindResource("TextBrush"),
        });
        section.Children.Add(new TextBlock
        {
            Text = description,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            LineHeight = 19,
            FontWeight = FontWeights.Light,
            Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
        });
        host.Children.Add(new Border
        {
            Background = (System.Windows.Media.Brush)FindResource("FieldBrush"),
            CornerRadius = new CornerRadius(17),
            Padding = new Thickness(15),
            Margin = new Thickness(0, 0, 0, 9),
            Child = section,
        });
    }

    private sealed record SourceMetadata(int? Width, int? Height, double? FrameRate, double? Duration);

    private sealed record TargetChoice(string Label, string Key)
    {
        public override string ToString() => Label;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await VerifyBackendAsync();
        await CheckForUpdatesAsync(false);
    }

    private async Task VerifyBackendAsync()
    {
        if (!File.Exists(BackendPath))
        {
            StatusText.Text = "변환 백엔드를 찾을 수 없습니다.";
            return;
        }

        try
        {
            using var process = Process.Start(BackendStartInfo("health")) ?? throw new InvalidOperationException();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            using var document = JsonDocument.Parse(output);
            if (process.ExitCode != 0 || document.RootElement.GetProperty("status").GetString() != "ok")
                throw new InvalidOperationException();
            StatusText.Text = "준비되었습니다.";
        }
        catch
        {
            StatusText.Text = "변환 백엔드 연결에 실패했습니다.";
        }
    }

    private async Task CheckForUpdatesAsync(bool notify)
    {
        try
        {
            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
            _availableUpdate = await UpdateService.CheckAsync(currentVersion);
            if (_availableUpdate is not null)
            {
                UpdateButton.Content = $"v{_availableUpdate.Version} 업데이트";
                UpdateButton.Visibility = Visibility.Visible;
                StatusText.Text = $"새 버전 v{_availableUpdate.Version}을 사용할 수 있습니다.";
            }
            else if (notify)
            {
                StatusText.Text = $"최신 버전 v{currentVersion.Major}.{currentVersion.Minor}.{currentVersion.Build}입니다.";
                System.Windows.MessageBox.Show(this, "현재 최신 버전을 사용하고 있습니다.", "업데이트 확인", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception error)
        {
            if (notify)
            {
                StatusText.Text = "업데이트를 확인하지 못했습니다.";
                System.Windows.MessageBox.Show(this, error.Message, "업데이트 확인", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        StatusText.Text = "업데이트 확인 중…";
        try
        {
            await CheckForUpdatesAsync(true);
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is null)
            return;
        SetUpdating(true);
        Progress.Maximum = 100;
        Progress.Value = 0;
        var progress = new Progress<int>(value =>
        {
            Progress.Value = value;
            StatusText.Text = $"업데이트 다운로드 중… {value}%";
        });

        try
        {
            var installerPath = await UpdateService.DownloadAsync(_availableUpdate, progress);
            var installer = new ProcessStartInfo(installerPath)
            {
                UseShellExecute = true,
                Arguments = $"--update --wait-for-pid {Environment.ProcessId}"
            };
            Process.Start(installer);
            ExitApplication();
        }
        catch (Exception error)
        {
            SetUpdating(false);
            StatusText.Text = "업데이트를 설치하지 못했습니다.";
            System.Windows.MessageBox.Show(this, error.Message, "업데이트", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetUpdating(bool updating)
    {
        UpdateButton.IsEnabled = !updating;
        CheckUpdateButton.IsEnabled = !updating;
        HelpButton.IsEnabled = !updating;
        WorkspaceGrid.IsEnabled = !updating;
    }

    private static void EnableHelpBackdrop(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        if (HwndSource.FromHwnd(handle) is HwndSource source)
            source.CompositionTarget.BackgroundColor = Colors.Transparent;

        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(handle, ref margins);

        var (width, height, radius) = GetRoundedWindowMetrics(window, 30);
        var blurRegion = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2);
        if (blurRegion != IntPtr.Zero)
        {
            var blurBehind = new DwmBlurBehind
            {
                Flags = DwmBlurBehindEnable | DwmBlurBehindRegion,
                Enable = true,
                Region = blurRegion,
                TransitionOnMaximized = false,
            };
            DwmEnableBlurBehindWindow(handle, ref blurBehind);
            DeleteObject(blurRegion);
        }

        var windowRegion = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2);
        if (windowRegion != IntPtr.Zero && SetWindowRgn(handle, windowRegion, true) == 0)
            DeleteObject(windowRegion);

        ApplySystemTitleBarTheme(handle);
    }

    private static (int Width, int Height, int Radius) GetRoundedWindowMetrics(Window window, double radiusDip)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        var width = Math.Max(1, (int)Math.Round(window.ActualWidth * dpi.DpiScaleX));
        var height = Math.Max(1, (int)Math.Round(window.ActualHeight * dpi.DpiScaleY));
        var radius = Math.Max(1, (int)Math.Round(radiusDip * dpi.DpiScaleX));
        return (width, height, radius);
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

    [DllImport("dwmapi.dll")]
    private static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref DwmBlurBehind blurBehind);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr objectHandle);

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmBlurBehind
    {
        public int Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool Enable;
        public IntPtr Region;
        [MarshalAs(UnmanagedType.Bool)] public bool TransitionOnMaximized;
    }

    private const int DwmBlurBehindEnable = 0x1;
    private const int DwmBlurBehindRegion = 0x2;
}
