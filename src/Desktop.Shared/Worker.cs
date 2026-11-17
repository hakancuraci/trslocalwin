using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using TrsLocal;

namespace TrsLocal.Desktop;

internal static class Worker
{
    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    internal static int Run(string[] args)
    {
        string? Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        using var runtime = new PortableRuntime();
        var folder = Path.GetFullPath(Arg("--folder") ?? runtime.Root);
        if (args.Contains("--stop")) { Directory.CreateDirectory(Path.Combine(folder, "transcript")); File.WriteAllText(Path.Combine(folder, "transcript", "STOP"), ""); return 0; }
        void Emit(string kind, string file, string message) => Console.WriteLine(JsonSerializer.Serialize(new WatchEvent(kind, file, message, DateTimeOffset.Now), Json));
        try
        {
            Emit("loading", "", "EXE yanındaki yerel model yükleniyor…");
            var job = Arg("--job") ?? "watch";
            if(job!="watch") runtime.LoadAsync().GetAwaiter().GetResult();
            if (job == "model") { Emit("done", "", "Yerel model hazır. Model indirme işlemi yapılmadı."); return 0; }
            if (job == "transcribe")
            {
                var path = Arg("--file") ?? throw new ArgumentException("Dosya belirtilmedi.");
                Emit("status", Path.GetFileName(path), "Ses metne dönüştürülüyor…");
                var hotwords = (Arg("--hotwords") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var text = runtime.TranscribeAsync(path, hotwords).GetAwaiter().GetResult();
                Emit("text", Path.GetFileName(path), text);
                Emit("done", "", "Dönüştürme tamamlandı. Metni düzenleyebilir ve kaydedebilirsin."); return 0;
            }
            var processor = new FolderProcessor(folder, (path, token) => runtime.TranscribeAsync(path, cancellationToken: token));
            processor.Event += e => { Console.WriteLine(JsonSerializer.Serialize(e, Json)); runtime.Log(e.Kind + ": " + e.File + " — " + e.Message, file: "islem.log", directory: Path.Combine(folder,"transcript")); };
            processor.RunAsync(args.Contains("--once"), initialize: async () => { await runtime.LoadAsync(); Emit("ready", "", "Klasör izleniyor. Yeni dosyaların sabit kalması bekleniyor."); }).GetAwaiter().GetResult(); return 0;
        }
        catch (Exception error) { runtime.Log("İşlem tamamlanamadı", error, "error.log"); Emit("fatal", "", error.Message); return 1; }
    }
}

internal sealed class WorkerController : IDisposable
{
    private Process? process;
    private SafeFileHandle? job;
    public event Action<WatchEvent>? Event;
    public bool Running => process is { HasExited: false };
    public async Task StartAsync(params string[] arguments)
    {
        if (Running) throw new InvalidOperationException("İşlem zaten sürüyor.");
        var info = new ProcessStartInfo(Environment.ProcessPath!) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("--worker");
        foreach (var arg in arguments) info.ArgumentList.Add(arg);
        process = new Process { StartInfo = info };
        process.Start();
        try { job = WindowsJob.Own(process); }
        catch { process.Kill(true); throw; }
        bool fatalReported = false;
        var errors = process.StandardError.ReadToEndAsync();
        string? line;
        while ((line = await process.StandardOutput.ReadLineAsync()) is not null)
        {
            try { var e = JsonSerializer.Deserialize<WatchEvent>(line, Worker.Json); if (e is not null) { if(e.Kind=="fatal") fatalReported=true; Event?.Invoke(e); } }
            catch (JsonException) { }
        }
        await process.WaitForExitAsync();
        var detail = await errors;
        if (process.ExitCode != 0 && !fatalReported) Event?.Invoke(new("fatal", "", "İşlem durdu. Ayrıntılar data/error.log içinde. " + detail[^Math.Min(1000, detail.Length)..], DateTimeOffset.Now));
    }
    public void Dispose()
    {
        // The Job handle also owns FFmpeg and any other children of the worker.
        job?.Dispose();
        if (process is { HasExited: false }) { try { process.Kill(true); } catch (InvalidOperationException) { } }
        process?.Dispose(); process = null;
    }
}

internal static class WindowsJob
{
    [StructLayout(LayoutKind.Sequential)] private struct Basic { public long ProcessTime, JobTime; public uint Flags; public UIntPtr MinWorking, MaxWorking; public uint ActiveProcesses; public UIntPtr Affinity; public uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] private struct Counters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct Extended { public Basic Basic; public Counters IO; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(SafeFileHandle job, int kind, ref Extended info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
    public static SafeFileHandle Own(Process process)
    {
        var job = CreateJobObject(IntPtr.Zero, null);
        var limits = new Extended { Basic = new Basic { Flags = 0x2000 } };
        if (job.IsInvalid || !SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<Extended>()) || !AssignProcessToJobObject(job, process.Handle))
        { int error = Marshal.GetLastWin32Error(); job.Dispose(); throw new System.ComponentModel.Win32Exception(error); }
        return job;
    }
}
