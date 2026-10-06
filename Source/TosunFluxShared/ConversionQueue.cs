using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace TosunFluxShared;

public enum QueueState { Waiting, Processing, Completed, Failed }

public sealed class ConversionQueueItem(string path) : INotifyPropertyChanged
{
    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileName(Path);
    public QueueState State { get; private set; } = QueueState.Waiting;
    public string Detail { get; private set; } = System.IO.Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
    public IReadOnlyList<string> Outputs { get; private set; } = Array.Empty<string>();
    public bool IsProcessing => State == QueueState.Processing;
    public bool CanRemove => !IsProcessing;
    public bool CanRetry => State == QueueState.Failed;
    public double Progress => State is QueueState.Completed or QueueState.Failed ? 100 : 0;
    public string Status => State switch
    {
        QueueState.Processing => "처리 중",
        QueueState.Completed => "완료",
        QueueState.Failed => "실패",
        _ => "대기",
    };
    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetState(QueueState state, string detail, IReadOnlyList<string>? outputs = null)
    {
        State = state;
        Detail = detail;
        Outputs = outputs ?? Array.Empty<string>();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}

public static class ConversionQueue
{
    public static string Summary(IEnumerable<ConversionQueueItem> files)
    {
        var items = files.ToArray();
        return $"대기 {items.Count(file => file.State == QueueState.Waiting)} · 처리 중 {items.Count(file => file.IsProcessing)} · 완료 {items.Count(file => file.State == QueueState.Completed)} · 실패 {items.Count(file => file.CanRetry)}";
    }

    // ponytail: one GPU worker; only add parallel workers after measuring VRAM headroom.
    public static async Task RunAsync(IList<ConversionQueueItem> files, ProcessStartInfo command, Action changed)
    {
        while (files.FirstOrDefault(file => file.State == QueueState.Waiting) is { } item)
        {
            item.SetState(QueueState.Processing, "변환 중…");
            changed();
            command.ArgumentList.Add(item.Path);
            Process? process = null;
            try
            {
                process = Process.Start(command) ?? throw new InvalidOperationException("변환 백엔드를 실행할 수 없습니다.");
                var errorTask = process.StandardError.ReadToEndAsync();
                string? failure = null;
                string[]? outputs = null;
                while (await process.StandardOutput.ReadLineAsync() is { } line)
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (root.TryGetProperty("event", out var eventName) && eventName.GetString() == "progress")
                    {
                        failure = root.GetProperty("error").GetString();
                        outputs = root.GetProperty("outputs").EnumerateArray().Select(value => value.GetString()!).ToArray();
                    }
                }
                await process.WaitForExitAsync();
                var stderr = (await errorTask).Trim();
                if (failure is not null || process.ExitCode != 0 || outputs is not { Length: > 0 })
                    throw new InvalidOperationException(failure ?? (stderr.Length > 0 ? stderr : "변환 결과를 받지 못했습니다."));
                item.SetState(QueueState.Completed, string.Join(", ", outputs.Select(System.IO.Path.GetFileName)), outputs);
            }
            catch (Exception error)
            {
                item.SetState(QueueState.Failed, error.Message);
            }
            finally
            {
                if (process is not null)
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                    process.Dispose();
                }
                command.ArgumentList.RemoveAt(command.ArgumentList.Count - 1);
                changed();
            }
        }
    }
}
