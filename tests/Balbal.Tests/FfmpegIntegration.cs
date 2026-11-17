using Balbal;
using System.Diagnostics;

internal static class FfmpegIntegration
{
    public static async Task RunAsync()
    {
        var executable = Ffmpeg.ResolveExecutable();
        var root = Path.Combine(Path.GetTempPath(), "balbal media tests " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        int checks = 0;
        void Check(bool value, string name) { checks++; if (!value) throw new Exception("FAIL: " + name); }
        async Task Encode(params string[] arguments)
        {
            var info = new ProcessStartInfo(executable) { RedirectStandardError = true, UseShellExecute = false };
            foreach (var arg in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-y" }.Concat(arguments)) info.ArgumentList.Add(arg);
            using var process = Process.Start(info)!;
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new Exception("Test media generation failed: " + error);
        }
        async Task Expect<T>(Func<Task> action, string name) where T : Exception
        {
            try { await action(); } catch (T) { checks++; return; }
            throw new Exception("FAIL: expected " + typeof(T).Name + ": " + name);
        }
        try
        {
            var formats = await Ffmpeg.GetFormatsAsync(executable);
            Check(formats.Contains("mp3") && formats.Contains("matroska"), "format discovery");
            var source = Path.Combine(root, "ses boşluk ' özel.wav");
            await Encode("-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100:duration=0.6", "-ac", "2", "-c:a", "pcm_s16le", source);
            var original = await Audio.ReadAsync(source);
            Check(original.Length == 9600, "PCM WAV duration");
            var cases = new (string Name, string Codec)[]
            {
                ("sample.mp3", "libmp3lame"), ("sample.flac", "flac"), ("sample.ogg", "libvorbis"),
                ("sample.opus", "libopus"), ("sample.m4a", "aac"), ("sample.aac", "aac"),
                ("sample.wma", "wmav2"), ("sample.aiff", "pcm_s16be"), ("sample.au", "pcm_s16be"),
                ("sample.ac3", "ac3"), ("sample.mka", "flac"), ("alac.m4a", "alac"),
                ("sample.mp2", "mp2"), ("sample.caf", "pcm_s16le"), ("compressed.wav", "pcm_mulaw")
            };
            foreach (var (name, codec) in cases)
            {
                var target = Path.Combine(root, name);
                await Encode("-i", source, "-c:a", codec, target);
                var samples = await Audio.ReadAsync(target);
                Check(samples.All(float.IsFinite) && Math.Abs(samples.Length / 16000d - .6) < .12, name + " finite/duration");
                Check(samples.Any(x => Math.Abs(x) > .01), name + " audible");
                Console.WriteLine("PASS media: " + name);
            }
            foreach (var (name, videoCodec, audioCodec) in new[] { ("video.mp4", "mpeg4", "aac"), ("video.mkv", "mpeg4", "flac"), ("video.webm", "libvpx", "libopus"), ("video.avi", "mpeg4", "pcm_s16le") })
            {
                var target = Path.Combine(root, name);
                await Encode("-f", "lavfi", "-i", "color=size=64x64:rate=10:duration=0.6", "-i", source, "-c:v", videoCodec, "-c:a", audioCodec, "-shortest", target);
                var samples = await Audio.ReadAsync(target);
                Check(Math.Abs(samples.Length / 16000d - .6) < .12 && samples.Any(x => Math.Abs(x) > .01), name);
                Console.WriteLine("PASS media: " + name);
            }
            var unknown = Path.Combine(root, "unknown.extension");
            File.Copy(Path.Combine(root, "sample.flac"), unknown);
            Check((await Audio.ReadAsync(unknown)).Length > 0, "content detection without extension");
            Check((await Audio.ReadAsync(source, new AudioReadOptions { FfmpegPath = executable, ForceFfmpeg = true })).Length == 9600, "force FFmpeg for WAV");
            var multi = Path.Combine(root, "multi.mka");
            await Encode("-i", source, "-f", "lavfi", "-i", "anullsrc=r=44100:cl=mono", "-t", "0.6", "-map", "0:a:0", "-map", "1:a:0", "-c:a", "flac", multi);
            var silent = await Audio.ReadAsync(multi, new AudioReadOptions { AudioStreamIndex = 1 });
            Check(silent.All(x => Math.Abs(x) < 1e-6), "audio stream selection");
            await Expect<InvalidDataException>(() => Audio.ReadAsync(multi, new AudioReadOptions { AudioStreamIndex = 2 }), "missing audio stream");
            var videoOnly = Path.Combine(root, "silent.mp4");
            await Encode("-f", "lavfi", "-i", "color=size=64x64:duration=0.1", "-an", "-c:v", "mpeg4", videoOnly);
            await Expect<InvalidDataException>(() => Audio.ReadAsync(videoOnly), "video without audio");
            var corrupt = Path.Combine(root, "corrupt.mp3");
            await File.WriteAllTextAsync(corrupt, "invalid media");
            await Expect<InvalidDataException>(() => Audio.ReadAsync(corrupt), "corrupt media");
            await Expect<FileNotFoundException>(() => Audio.ReadAsync(corrupt, new AudioReadOptions { FfmpegPath = Path.Combine(root, "missing") }), "missing FFmpeg");
            await Expect<FileNotFoundException>(() => Audio.ReadAsync(Path.Combine(root, "missing.mp3")), "missing input");
            using (var cancel = new CancellationTokenSource())
            {
                cancel.Cancel();
                await Expect<OperationCanceledException>(() => Audio.ReadAsync(source, cancel.Token), "pre-cancellation");
            }
            var previous = Environment.GetEnvironmentVariable("BALBAL_FFMPEG");
            try
            {
                Environment.SetEnvironmentVariable("BALBAL_FFMPEG", Path.Combine(root, "missing"));
                Check(Ffmpeg.ResolveExecutable(executable) == executable, "explicit FFmpeg overrides environment");
            }
            finally { Environment.SetEnvironmentVariable("BALBAL_FFMPEG", previous); }
            if (!OperatingSystem.IsWindows())
            {
                var fake = Path.Combine(root, "slow-ffmpeg");
                var pidFile = Path.Combine(root, "child.pid");
                await File.WriteAllTextAsync(fake, "#!/bin/sh\necho $$ > \"" + pidFile + "\"\nexec sleep 60\n");
                File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                await Expect<TimeoutException>(() => Audio.ReadAsync(corrupt, new AudioReadOptions { FfmpegPath = fake, ConversionTimeout = TimeSpan.FromMilliseconds(200) }), "conversion timeout");
                using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
                await Expect<OperationCanceledException>(() => Audio.ReadAsync(corrupt, new AudioReadOptions { FfmpegPath = fake }, cancel.Token), "running process cancellation");
                int pid = int.Parse((await File.ReadAllTextAsync(pidFile)).Trim());
                bool gone;
                try { using var child = Process.GetProcessById(pid); gone = child.HasExited; } catch (ArgumentException) { gone = true; }
                Check(gone, "cancelled FFmpeg was reaped");
            }
            Console.WriteLine($"PASS: {checks} FFmpeg integration assertions (19 encoded audio/video formats)");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
