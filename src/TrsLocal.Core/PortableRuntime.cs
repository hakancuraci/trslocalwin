using Balbal;
using Microsoft.ML.OnnxRuntime;
using System.Runtime.Intrinsics.X86;
using System.Text;

namespace TrsLocal;

public sealed class PortableRuntime : IDisposable
{
    public string Root { get; }
    public string Data => Path.Combine(Root, "data");
    public string Output => Path.Combine(Root, "transcript");
    public string ModelDirectory => Path.Combine(Root, "models", "balbal-v2-manas");
    private Model? model;
    private readonly SemaphoreSlim gate = new(1, 1);

    public PortableRuntime(string? root = null)
    {
        Root = Path.GetFullPath(root ?? AppContext.BaseDirectory);
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Output);
        foreach (var name in new[] { "ORT_DISABLE_TELEMETRY", "HF_HUB_OFFLINE", "HF_HUB_DISABLE_TELEMETRY", "DO_NOT_TRACK", "DISABLE_TELEMETRY", "TRANSFORMERS_OFFLINE" })
            Environment.SetEnvironmentVariable(name, "1");
        OrtEnv.Instance().DisableTelemetryEvents();
    }

    public string BundledFfmpeg()
    {
        var candidates = new[] { Path.Combine(Root, "tools", "ffmpeg"), Path.Combine(Root, "_internal", "imageio_ffmpeg", "binaries") };
        foreach (var directory in candidates)
        {
            if (!Directory.Exists(directory)) continue;
            var executable = OperatingSystem.IsWindows()
                ? Directory.EnumerateFiles(directory, "ffmpeg*.exe").Order(StringComparer.Ordinal).FirstOrDefault()
                : Directory.EnumerateFiles(directory, "ffmpeg*").FirstOrDefault(p => !Path.HasExtension(p));
            if (executable is not null) return executable;
        }
        throw new FileNotFoundException("Pakete dahil FFmpeg bulunamadı. tools/ffmpeg klasörünü kontrol edin; sistemden program aranmayacak.");
    }

    public void CheckModel()
    {
        string[] required = ["model.json", "onnx/model.onnx", "onnx/model.onnx.data", "lm/model.binary", "lm/tokenizer.model"];
        var missing = required.Where(f => !File.Exists(Path.Combine(ModelDirectory, f))).ToArray();
        if (missing.Length > 0) throw new FileNotFoundException("Yerel model eksik. Otomatik indirme kapalı.\nKlasör: " + ModelDirectory + "\nEksik: " + string.Join(", ", missing));
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (model is not null) return;
            CheckModel();
            var level = Avx512F.IsSupported && Avx512BW.IsSupported && Avx512DQ.IsSupported && Avx512Vbmi.IsSupported ? "v4" : Avx2.IsSupported && Fma.IsSupported ? "v3" : "v2";
            var suffix = OperatingSystem.IsWindows() ? ".dll" : ".so";
            var name = (OperatingSystem.IsWindows() ? "" : "lib") + "balbal_ops_" + level + suffix;
            var ops = new[] { Path.Combine(Root, "_internal", "balbal", "_native", name), Path.Combine(Root, name) }.FirstOrDefault(File.Exists);
            if (ops is null) throw new FileNotFoundException("Yerel model operatörü eksik: " + name);
            cancellationToken.ThrowIfCancellationRequested();
            model = await Task.Run(() => new Model(ModelDirectory, customOpsLibrary: ops), cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task<string> TranscribeAsync(string source, IEnumerable<string>? hotwords = null, CancellationToken cancellationToken = default)
    {
        await LoadAsync(cancellationToken);
        var wave = await Audio.ReadAsync(source, new AudioReadOptions
        {
            FfmpegPath = BundledFfmpeg(), ForceFfmpeg = true, Pcm16Intermediate = true,
            ConversionTimeout = TimeSpan.FromHours(6)
        }, cancellationToken);
        // The original media_runtime writes a 48 kHz PCM16 WAV before the same resampler.
        var result = await Task.Run(() => model!.Transcribe(wave, hotwords: hotwords, cancellationToken: cancellationToken), cancellationToken);
        return result.Text;
    }

    public void Log(string message, Exception? error = null, string file = "runtime.log", string? directory = null)
    {
        lock (this)
        {
            var path = Path.Combine(directory ?? (file == "islem.log" ? Output : Data), file);
            if (File.Exists(path) && new FileInfo(path).Length > 2_000_000)
            {
                for (int i = 3; i >= 1; i--)
                {
                    var previous = i == 1 ? path : path + "." + (i - 1);
                    if (File.Exists(previous)) File.Move(previous, path + "." + i, true);
                }
            }
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{(error is null ? "" : "\n" + error)}\n", new UTF8Encoding(false));
        }
    }
    public void Dispose() { model?.Dispose(); gate.Dispose(); }
}
