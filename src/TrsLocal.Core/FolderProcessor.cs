using System.Diagnostics;
using System.Text;
using System.Text.Json;
namespace TrsLocal;

public record WatchEvent(string Kind, string File, string Message, DateTimeOffset Time);
public record WatchOptions
{
    public TimeSpan StableTime { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan ScanInterval { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(300);
}

public sealed class FolderProcessor
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    { ".mp3", ".wav", ".ts", ".wmv", ".wma", ".aiff", ".flac", ".mpeg", ".mpg", ".m4v", ".webm", ".oga", ".mp4", ".aif", ".ogg", ".mkv", ".mov", ".aac", ".m2ts", ".m4a", ".avi", ".opus", ".mts", ".3gp" };
    private readonly string folder, output;
    private readonly Func<string, CancellationToken, Task<string>> transcribe;
    private readonly WatchOptions options;
    private readonly Dictionary<string, ((long Size, long Time) Signature, long Since)> observed = new();
    private readonly Dictionary<string, long> failed = new();
    private readonly HashSet<string> reportedExisting = new();
    public event Action<WatchEvent>? Event;
    public FolderProcessor(string folder, Func<string, CancellationToken, Task<string>> transcribe, WatchOptions? options = null)
    {
        this.folder = Path.GetFullPath(folder); output = Path.Combine(this.folder, "transcript");
        this.transcribe = transcribe; this.options = options ?? new();
        Directory.CreateDirectory(output);
    }
    private void Report(string kind, string path, string message) => Event?.Invoke(new(kind, Path.GetFileName(path), message, DateTimeOffset.Now));
    private static (long Size, long Time) Signature(string path) { var f = new FileInfo(path); return (f.Length, f.LastWriteTimeUtc.Ticks); }
    public static bool Publish(string destination, string text)
    {
        var temp = Path.Combine(Path.GetDirectoryName(destination)!, Guid.NewGuid().ToString("N") + ".part");
        try
        {
            using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            { using(var writer=new StreamWriter(stream,new UTF8Encoding(false),leaveOpen:true)){writer.Write(text);writer.Flush();} stream.Flush(true); }
            try { File.Move(temp, destination, overwrite: false); return true; }
            catch (IOException) when (File.Exists(destination)) { return false; }
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public async Task ScanAsync(bool once = false, CancellationToken cancellationToken = default)
    {
        var files = Directory.EnumerateFiles(folder).Where(f => Extensions.Contains(Path.GetExtension(f)))
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var stale in observed.Keys.Except(files).ToArray()) { observed.Remove(stale); failed.Remove(stale); reportedExisting.Remove(stale); }
        foreach (var source in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(Path.Combine(output, "STOP"))) return;
            var destination = Path.Combine(output, Path.GetFileName(source) + ".txt");
            if (File.Exists(destination)) { if (reportedExisting.Add(source)) Report("existing", source, "Çıktı zaten var"); continue; }
            try
            {
                var before = Signature(source); long now = Stopwatch.GetTimestamp();
                if (!observed.TryGetValue(source, out var previous) || previous.Signature != before)
                {
                    observed[source] = (before, now); failed.Remove(source); Report("waiting", source, "Bekliyor");
                }
                if (!once && Stopwatch.GetElapsedTime(observed[source].Since, now) < options.StableTime) continue;
                if (failed.TryGetValue(source, out var retry) && Stopwatch.GetElapsedTime(retry, now) < options.RetryDelay) continue;
                Report("processing", source, "İşleniyor");
                var text = await transcribe(source, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(Path.Combine(output, "STOP"))) return;
                if (Signature(source) != before) { observed.Remove(source); Report("waiting", source, "Dosya değişti; yeniden beklenecek"); continue; }
                bool published = Publish(destination, text);
                Report(published ? "done" : "existing", source, published ? "Tamamlandı" : "Çıktı zaten var");
                failed.Remove(source);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { failed[source] = Stopwatch.GetTimestamp(); Report("error", source, error.Message); }
        }
    }
    public async Task RunAsync(bool once = false, CancellationToken cancellationToken = default, Func<Task>? initialize = null)
    {
        var lockPath = Path.Combine(output, ".watcher.lock");
        using var fileLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var stop = Path.Combine(output, "STOP"); if (File.Exists(stop)) File.Delete(stop);
        if(initialize is not null) await initialize();
        do
        {
            await ScanAsync(once, cancellationToken);
            if (once || File.Exists(stop)) break;
            await Task.Delay(options.ScanInterval, cancellationToken);
        } while (true);
        Report("stopped", "", "İzleyici durdu.");
    }
}
