// dotnet run --project Tests/ConversionQueueSmoke.csproj -- <python> <backend.py> <input-dir>
using System.Collections.ObjectModel;
using System.Diagnostics;
using TosunFluxShared;

if (args.Length != 3)
    throw new ArgumentException("Supply Python, backend script, and a directory containing profile-gradient.png, profile-tiled.png, Broken-Video.mp4.");
var root = Path.GetFullPath(args[2]);
var first = new ConversionQueueItem(Path.Combine(root, "profile-gradient.png"));
var broken = new ConversionQueueItem(Path.Combine(root, "Broken-Video.mp4"));
var removed = new ConversionQueueItem(Path.Combine(root, "profile-tiled.png"));
var later = new ConversionQueueItem(removed.Path);
var queue = new ObservableCollection<ConversionQueueItem> { first, broken, removed };
var command = new ProcessStartInfo(args[0]) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
if (args[1] != "-") command.ArgumentList.Add(Path.GetFullPath(args[1]));
foreach (var argument in new[] { "convert", "--output", Path.Combine(root, "native-queue-output"), "--target", "png", "--scale-factor", "2", "--upscale-engine", "ai" })
    command.ArgumentList.Add(argument);
var argumentCount = command.ArgumentList.Count;
var starts = new List<ConversionQueueItem>();
var notifications = 0;
first.PropertyChanged += (_, _) => notifications++;
await ConversionQueue.RunAsync(queue, command, () =>
{
    var active = queue.FirstOrDefault(file => file.IsProcessing);
    if (active is null) return;
    Check(queue.Count(file => file.IsProcessing) == 1 && !active.CanRemove, "one active GPU job");
    starts.Add(active);
    if (active == first)
    {
        queue.Remove(removed);
        queue.Add(later);
    }
});
Check(starts.SequenceEqual(new[] { first, broken, later }), "FIFO and late enqueue");
Check(first.State == QueueState.Completed && later.State == QueueState.Completed && first.Outputs.All(File.Exists) && later.Outputs.All(File.Exists), "real AI outputs");
Check(broken.State == QueueState.Failed && broken.CanRetry && broken.Detail.Length > 0, "isolated failure");
Check(removed.State == QueueState.Waiting && removed.Outputs.Count == 0, "removed waiting file not run");
Check(command.ArgumentList.Count == argumentCount && notifications == 2 && first.Progress == 100, "settings and property notifications");
Check(ConversionQueue.Summary(queue) == "대기 0 · 처리 중 0 · 완료 2 · 실패 1", "summary");
var notificationCount = notifications;
await ConversionQueue.RunAsync(queue, command, () => throw new Exception("completed queue must not rerun"));
Check(notifications == notificationCount, "completed files not repeated");
broken.SetState(QueueState.Waiting, "다시 대기 중");
Check(!broken.CanRetry && broken.Progress == 0 && broken.Outputs.Count == 0, "retry reset");
Console.WriteLine("PASS: real 2x AI batch, FIFO, late enqueue, waiting removal, failure isolation, notifications, fixed arguments, no rerun, retry");

static void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + label);
}
