using System.ComponentModel;
using System.Diagnostics;

namespace Balbal;

public sealed record AudioReadOptions
{
    public string? FfmpegPath { get; init; }
    public TimeSpan ConversionTimeout { get; init; } = TimeSpan.FromMinutes(10);
    /// <summary>Also decode PCM WAV through FFmpeg instead of the built-in reader.</summary>
    public bool ForceFfmpeg { get; init; }
    public bool Pcm16Intermediate { get; init; }
    /// <summary>Zero-based audio stream index, independent of video/subtitle streams.</summary>
    public int AudioStreamIndex { get; init; }
}

/// <summary>Local media decoding using the formats/codecs available in the installed FFmpeg build.</summary>
public static class Ffmpeg
{
    public static string ResolveExecutable(string? configuredPath = null)
    {
        var configured = configuredPath ?? Environment.GetEnvironmentVariable("BALBAL_FFMPEG");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var path = Path.GetFullPath(configured);
            if (!File.Exists(path)) throw new FileNotFoundException("FFmpeg bulunamadı. --ffmpeg veya BALBAL_FFMPEG yolunu kontrol edin.", path);
            return path;
        }

        var name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        foreach (var directory in new[] { AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg") })
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate)) return candidate;
        }
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim('"'), name);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        throw new FileNotFoundException("FFmpeg bulunamadı. ffmpeg/ffmpeg.exe dosyasını uygulamanın tools/ffmpeg klasörüne koyun, PATH'e ekleyin veya --ffmpeg ile tam yolunu belirtin.");
    }

    public static Task<string> GetFormatsAsync(string? executablePath = null, CancellationToken cancellationToken = default)
        => FormatsCoreAsync(executablePath, cancellationToken);

    private static async Task<string> FormatsCoreAsync(string? path, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        await RunAsync(ResolveExecutable(path), ["-hide_banner", "-formats"], output, TimeSpan.FromSeconds(30), cancellationToken);
        return System.Text.Encoding.UTF8.GetString(output.ToArray());
    }

    internal static async Task<float[]> DecodeAsync(string path, AudioReadOptions options, CancellationToken cancellationToken)
    {
        if (options.AudioStreamIndex < 0) throw new ArgumentOutOfRangeException(nameof(options.AudioStreamIndex));
        var streamIndex = options.AudioStreamIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var buffer = new MemoryStream();
        await RunAsync(ResolveExecutable(options.FfmpegPath),
            ["-nostdin", "-hide_banner", "-loglevel", "error", "-protocol_whitelist", "file,pipe",
             "-i", path, "-map", "0:a:" + streamIndex, "-vn", "-sn", "-dn", "-ac", "1", "-ar", "48000",
             "-c:a", options.Pcm16Intermediate ? "pcm_s16le" : "pcm_f32le", "-f", options.Pcm16Intermediate ? "s16le" : "f32le", "pipe:1"],
            buffer, options.ConversionTimeout, cancellationToken);
        var raw = buffer.ToArray();
        int width = options.Pcm16Intermediate ? 2 : 4;
        if (raw.Length == 0 || raw.Length % width != 0)
            throw new InvalidDataException("FFmpeg geçerli ses örnekleri üretemedi. Dosya boş veya seçilen ses kanalı okunamıyor.");
        var samples = new float[raw.Length / width];
        if(options.Pcm16Intermediate)
            for(int i=0;i<samples.Length;i++) samples[i]=BitConverter.ToInt16(raw,i*2)/32768f;
        else Buffer.BlockCopy(raw, 0, samples, 0, raw.Length);
        // Preserve the upstream 48 kHz decode + polyphase resampling path.
        return Audio.Canonical(samples, 48000);
    }

    private static async Task RunAsync(string executable, IEnumerable<string> arguments, Stream output,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        try { process.Start(); }
        catch (Win32Exception error)
        {
            throw new IOException("FFmpeg başlatılamadı. Dosyanın işletim sistemine uygun ve çalıştırılabilir olduğunu kontrol edin: " + executable, error);
        }
        using var deadline = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        // Drain stderr concurrently and retain only its tail, so long error output cannot deadlock or grow without bound.
        var errors = ReadErrorTailAsync(process.StandardError);
        try
        {
            await process.StandardOutput.BaseStream.CopyToAsync(output, linked.Token);
            await process.WaitForExitAsync(linked.Token);
            var detail = await errors;
            if (process.ExitCode != 0)
                throw new InvalidDataException("FFmpeg dosyayı okuyamadı. Dosya bozuk, codec desteklenmiyor veya seçilen ses akışı bulunmuyor. " + detail);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new TimeoutException("FFmpeg ses dönüştürme süresi aşıldı: " + timeout);
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }
            await process.WaitForExitAsync();
            await errors;
        }
    }

    private static async Task<string> ReadErrorTailAsync(StreamReader reader)
    {
        const int limit = 4096;
        var buffer = new char[2048];
        var tail = "";
        int count;
        while ((count = await reader.ReadAsync(buffer)) > 0)
        {
            tail += new string(buffer, 0, count);
            if (tail.Length > limit) tail = tail[^limit..];
        }
        return tail.Trim();
    }
}
