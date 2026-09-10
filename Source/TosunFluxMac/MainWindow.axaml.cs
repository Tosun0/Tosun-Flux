using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace TosunFluxMac;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const string Version = "1.2.1";
    private readonly string _settingsPath;
    private string _target = "png";
    private bool _busy;

    public ObservableCollection<SourceFile> Files { get; } = new();

    public bool IsCustomResolution { get; private set; }

    public string FileSummary => Files.Count == 0
        ? "파일을 추가하면 변환할 수 있습니다."
        : $"{Files.Count}개 파일이 준비되었습니다.";

    public new event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Tosun Flux",
            "settings.json");

        LoadSettings();
        TargetCombo.ItemsSource = Array.Empty<TargetOption>();
        ResolutionCombo.SelectedIndex = 0;
        AspectCombo.SelectedIndex = 0;
        FitCombo.SelectedIndex = 0;
        OptimizeCombo.SelectedIndex = 0;
        FpsCombo.SelectedIndex = 0;
        UpdateControls();
    }

    private async void AddFilesClick(object? sender, RoutedEventArgs e)
    {
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "변환할 파일 선택",
            AllowMultiple = true,
        });
        AddPaths(picked.Select(item => item.Path.LocalPath));
        await RefreshTargetsAsync();
    }

    private async void ChooseOutputClick(object? sender, RoutedEventArgs e)
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "저장 폴더 선택",
            AllowMultiple = false,
        });
        var folder = picked.FirstOrDefault();
        if (folder is not null)
        {
            OutputPathBox.Text = folder.Path.LocalPath;
            SaveSettings();
        }
    }

    private async void DropZoneDrop(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(DataFormat.File))
        {
            return;
        }

        var files = e.DataTransfer.TryGetFiles();
        if (files is not null)
        {
            AddPaths(files.Select(item => item.Path.LocalPath));
            await RefreshTargetsAsync();
        }
        e.Handled = true;
    }

    private void DropZoneDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void RemoveFileClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SourceFile file })
        {
            Files.Remove(file);
            OnPropertyChanged(nameof(FileSummary));
            await RefreshTargetsAsync();
        }
    }

    private async void ClearFilesClick(object? sender, RoutedEventArgs e)
    {
        Files.Clear();
        OnPropertyChanged(nameof(FileSummary));
        await RefreshTargetsAsync();
    }

    private void ResolutionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var value = GetTag(ResolutionCombo);
        IsCustomResolution = value == "custom";
        CustomResolutionRow.IsVisible = IsCustomResolution;
        AspectCombo.IsEnabled = !IsCustomResolution && value is not ("scale-2" or "scale-4");
        OnPropertyChanged(nameof(IsCustomResolution));
    }

    private void TargetChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (TargetCombo.SelectedItem is not TargetOption option)
        {
            return;
        }

        _target = option.Value;
        FpsPanel.IsVisible = option.IsVideo;
    }

    private async void ConvertClick(object? sender, RoutedEventArgs e)
    {
        if (_busy || Files.Count == 0)
        {
            StatusText.Text = Files.Count == 0 ? "먼저 파일을 추가하세요." : "이미 변환 중입니다.";
            return;
        }

        if (!TryGetOptions(out var options, out var validationError))
        {
            StatusText.Text = validationError;
            return;
        }

        var output = OutputPathBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(output))
        {
            StatusText.Text = "저장 위치를 선택하세요.";
            return;
        }

        _busy = true;
        ProgressBar.IsVisible = true;
        ProgressBar.Value = 0;
        StatusText.Text = "변환을 준비하는 중입니다...";
        try
        {
            var command = CreateBackendCommand(
                "convert",
                "--output", output,
                "--target", _target,
                "--optimize", options.Optimize,
                "--resolution", options.Resolution,
                "--aspect", options.Aspect,
                "--fit", options.Fit,
                "--fps", options.Fps,
                "--scale-factor", options.ScaleFactor.ToString(CultureInfo.InvariantCulture),
                "--upscale-engine", options.ScaleFactor == 1d ? "resize" : "ai");
            if (options.Width is not null && options.Height is not null)
            {
                command.ArgumentList.Add("--width");
                command.ArgumentList.Add(options.Width.Value.ToString());
                command.ArgumentList.Add("--height");
                command.ArgumentList.Add(options.Height.Value.ToString());
            }
            foreach (var file in Files)
            {
                command.ArgumentList.Add(file.Path);
            }

            using var process = new Process { StartInfo = command };
            process.Start();
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                if (!TryParseJson(line, out var payload))
                {
                    continue;
                }

                if (payload.RootElement.TryGetProperty("event", out var eventElement)
                    && eventElement.GetString() == "progress")
                {
                    var index = payload.RootElement.GetProperty("index").GetInt32();
                    var total = payload.RootElement.GetProperty("total").GetInt32();
                    ProgressBar.Value = total == 0 ? 0 : (double)index / total;
                    var error = payload.RootElement.TryGetProperty("error", out var errorElement)
                        ? errorElement.GetString()
                        : null;
                    StatusText.Text = error is null
                        ? $"{index}/{total}개 변환 완료"
                        : $"{index}/{total}개 실패: {error}";
                }
            }

            var errorOutput = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode == 0)
            {
                ProgressBar.Value = 1;
                StatusText.Text = "변환이 완료되었습니다.";
            }
            else
            {
                StatusText.Text = string.IsNullOrWhiteSpace(errorOutput)
                    ? "변환에 실패했습니다."
                    : $"변환 실패: {errorOutput.Trim()}";
            }
        }
        catch (Exception error)
        {
            StatusText.Text = $"백엔드를 실행하지 못했습니다: {error.Message}";
        }
        finally
        {
            _busy = false;
        }
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(File.Exists))
        {
            if (Files.All(file => !string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                Files.Add(new SourceFile(path));
            }
        }
        OnPropertyChanged(nameof(FileSummary));
    }

    private async Task RefreshTargetsAsync()
    {
        if (Files.Count == 0)
        {
            TargetCombo.ItemsSource = Array.Empty<TargetOption>();
            TargetCombo.SelectedIndex = -1;
            _target = "png";
            FpsPanel.IsVisible = false;
            UpdateControls();
            return;
        }

        try
        {
            var command = CreateBackendCommand(new[] { "targets" }.Concat(Files.Select(file => file.Path)).ToArray());
            using var process = new Process { StartInfo = command };
            process.Start();
            var line = await process.StandardOutput.ReadLineAsync();
            await process.WaitForExitAsync();
            if (line is null || !TryParseJson(line, out var payload))
            {
                throw new InvalidOperationException("백엔드 응답을 읽지 못했습니다.");
            }

            var targets = payload.RootElement.GetProperty("targets")
                .EnumerateArray()
                .Select(item => item.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => new TargetOption(value!))
                .ToArray();
            TargetCombo.ItemsSource = targets;
            TargetCombo.SelectedIndex = targets.Length == 0 ? -1 : 0;
            _target = targets.Length == 0 ? "" : targets[0].Value;
            UpdateControls();
        }
        catch (Exception error)
        {
            TargetCombo.ItemsSource = Array.Empty<TargetOption>();
            TargetCombo.SelectedIndex = -1;
            StatusText.Text = $"지원 형식을 확인하지 못했습니다: {error.Message}";
        }
    }

    private bool TryGetOptions(out ConversionOptions options, out string error)
    {
        var resolution = GetTag(ResolutionCombo);
        var width = ParseDimension(WidthBox.Text);
        var height = ParseDimension(HeightBox.Text);
        if (resolution == "custom" && (width is null || height is null || width < 2 || height < 2 || width > 16384 || height > 16384))
        {
            options = new ConversionOptions("source", "source", "source", "fit", null, null, "source", 1d);
            error = "직접 해상도는 가로·세로 2~16384 범위로 입력하세요.";
            return false;
        }

        var scaleFactor = resolution switch
        {
            "scale-2" => 2d,
            "scale-4" => 4d,
            _ => 1d,
        };
        var isUpscale = scaleFactor != 1d;
        options = new ConversionOptions(
            GetTag(OptimizeCombo),
            resolution is "custom" or "scale-2" or "scale-4" ? "source" : resolution,
            isUpscale ? "source" : GetTag(AspectCombo),
            GetTag(FitCombo),
            resolution == "custom" ? width : null,
            resolution == "custom" ? height : null,
            FpsPanel.IsVisible ? GetTag(FpsCombo) : "source",
            scaleFactor);
        error = "";
        return true;
    }

    private void LoadSettings()
    {
        var defaultOutput = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        OutputPathBox.Text = defaultOutput;
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return;
            }
            using var document = JsonDocument.Parse(File.ReadAllText(_settingsPath));
            if (document.RootElement.TryGetProperty("outputPath", out var outputPath)
                && Directory.Exists(outputPath.GetString()))
            {
                OutputPathBox.Text = outputPath.GetString();
            }
        }
        catch (JsonException)
        {
            // A corrupt preference should not prevent the converter from opening.
        }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new { outputPath = OutputPathBox.Text }));
        }
        catch (IOException)
        {
            // Preferences are optional; conversion remains available if they cannot be saved.
        }
    }

    private ProcessStartInfo CreateBackendCommand(params string[] arguments)
    {
        var backend = FindBackend();
        var startInfo = new ProcessStartInfo
        {
            FileName = backend.Launcher,
            WorkingDirectory = backend.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var prefix in backend.PrefixArguments)
        {
            startInfo.ArgumentList.Add(prefix);
        }
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        return startInfo;
    }

    private static BackendCommand FindBackend()
    {
        var roots = new List<string> { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 5 && current.Parent is not null; i++)
        {
            current = current.Parent;
            roots.Add(current.FullName);
        }

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var executableCandidates = new[]
            {
                Path.Combine(root, "backend", "TosunFluxBackend", OperatingSystem.IsWindows() ? "TosunFluxBackend.exe" : "TosunFluxBackend"),
                Path.Combine(root, "backend", "TosunFluxBackend", "TosunFluxBackend.exe"),
            };
            var executable = executableCandidates.FirstOrDefault(File.Exists);
            if (executable is not null)
            {
                return new BackendCommand(executable, Path.GetDirectoryName(executable)!, Array.Empty<string>());
            }

            var script = Path.Combine(root, "Source", "TosunFluxBackend", "TosunFluxBackend.py");
            if (File.Exists(script))
            {
                var python = FindOnPath(Environment.GetEnvironmentVariable("TOSUN_PYTHON"))
                    ?? FindOnPath(OperatingSystem.IsMacOS() ? "python3" : "python")
                    ?? throw new InvalidOperationException("Python 실행 파일을 찾을 수 없습니다.");
                return new BackendCommand(python, Path.GetDirectoryName(script)!, new[] { script });
            }
        }

        throw new FileNotFoundException("Tosun Flux 변환 백엔드를 찾을 수 없습니다.");
    }

    private static string? FindOnPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        if (Path.IsPathRooted(value) && File.Exists(value))
        {
            return value;
        }
        if (File.Exists(value))
        {
            return Path.GetFullPath(value);
        }
        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, value);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            if (OperatingSystem.IsWindows() && !value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                candidate += ".exe";
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        return null;
    }

    private void UpdateControls()
    {
        FpsPanel.IsVisible = TargetCombo.SelectedItem is TargetOption { IsVideo: true };
    }

    private static string GetTag(ComboBox comboBox)
        => (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "source";

    private static int? ParseDimension(string? value)
        => int.TryParse(value, out var result) ? result : null;

    private static bool TryParseJson(string line, out JsonDocument payload)
    {
        try
        {
            payload = JsonDocument.Parse(line);
            return true;
        }
        catch (JsonException)
        {
            payload = null!;
            return false;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed record BackendCommand(string Launcher, string WorkingDirectory, IReadOnlyList<string> PrefixArguments);

    private sealed record ConversionOptions(
        string Optimize,
        string Resolution,
        string Aspect,
        string Fit,
        int? Width,
        int? Height,
        string Fps,
        double ScaleFactor);
}

public sealed class SourceFile
{
    public SourceFile(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path);
        var info = new FileInfo(path);
        Detail = $"{info.Length / 1024d / 1024d:0.##} MB  ·  {info.Extension.TrimStart('.').ToUpperInvariant()}";
    }

    public string Path { get; }
    public string Name { get; }
    public string Detail { get; }
}

public sealed class TargetOption
{
    public TargetOption(string value)
    {
        Value = value;
        Label = value switch
        {
            "png-sequence" => ".png Sequence",
            "jpg-sequence" => ".jpg Sequence",
            _ => $".{value}",
        };
        IsVideo = value is "mp4" or "webm" or "mov" or "mkv" or "avi" or "gif" or "png-sequence" or "jpg-sequence";
    }

    public string Value { get; }
    public string Label { get; }
    public bool IsVideo { get; }

    public override string ToString() => Label;
}
