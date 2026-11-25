using NAudio.Wave;
using System.Diagnostics;
using System.Globalization;

namespace MusicPlayer;

public sealed class AudioPlaybackService : IDisposable
{
    private const int SampleRate = 44100;
    private readonly WaveFormat _waveFormat = new(SampleRate, 16, 2);
    private Process? _decoder;
    private WaveOutEvent? _output;
    private ProcessPcmProvider? _provider;
    private TimeSpan _seekOffset;
    private bool _disposed;

    public event EventHandler? PlaybackStopped;
    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;
    public TimeSpan Position => _seekOffset + (_provider?.Position ?? TimeSpan.Zero);

    public void Play(string path, TimeSpan startAt)
    {
        Stop();
        var ffmpeg = FindTool("ffmpeg.exe", "ffmpeg");
        var start = startAt < TimeSpan.Zero ? TimeSpan.Zero : startAt;
        var process = StartFfmpeg(ffmpeg, path, start);
        var provider = new ProcessPcmProvider(process.StandardOutput.BaseStream, _waveFormat);
        var output = new WaveOutEvent { DesiredLatency = 120, NumberOfBuffers = 3 };
        output.PlaybackStopped += (_, _) => PlaybackStopped?.Invoke(this, EventArgs.Empty);
        output.Init(provider);

        _decoder = process;
        _provider = provider;
        _output = output;
        _seekOffset = start;
        output.Play();
        _ = process.StandardError.ReadToEndAsync();
    }

    public void Pause() => _output?.Pause();

    public void Resume() => _output?.Play();

    public void Stop()
    {
        try { _output?.Stop(); } catch { }
        _output?.Dispose();
        _output = null;
        _provider = null;

        if (_decoder is { HasExited: false })
        {
            try { _decoder.Kill(true); } catch { }
        }
        _decoder?.Dispose();
        _decoder = null;
        _seekOffset = TimeSpan.Zero;
    }

    public async Task<double[]> BuildWaveformAsync(string path, int maxBars = 2400, CancellationToken cancellationToken = default)
    {
        var ffmpeg = FindTool("ffmpeg.exe", "ffmpeg");
        var startInfo = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-i", path, "-vn", "-map", "0:a:0",
                     "-ac", "1", "-ar", "1000", "-f", "s16le", "pipe:1" })
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 FFmpeg。");
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var peaks = new List<double>(Math.Min(maxBars, 2400));
        var buffer = new byte[64 * 1024];
        const int samplesPerBar = 100;
        var peak = 0.0;
        var samples = 0;
        while (true)
        {
            var read = await process.StandardOutput.BaseStream.ReadAsync(buffer, cancellationToken);
            if (read <= 0) break;
            for (var i = 0; i + 1 < read; i += 2)
            {
                var sample = Math.Abs((short)(buffer[i] | buffer[i + 1] << 8)) / 32768.0;
                if (sample > peak) peak = sample;
                if (++samples >= samplesPerBar)
                {
                    peaks.Add(peak);
                    peak = 0;
                    samples = 0;
                }
            }
        }
        if (samples > 0) peaks.Add(peak);
        await process.WaitForExitAsync(cancellationToken);
        var error = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "无法读取该文件的音频轨道。" : error.Trim());

        if (peaks.Count > maxBars)
        {
            var reduced = new double[maxBars];
            for (var i = 0; i < maxBars; i++)
            {
                var from = (int)((long)i * peaks.Count / maxBars);
                var to = Math.Max(from + 1, (int)((long)(i + 1) * peaks.Count / maxBars));
                reduced[i] = peaks.Skip(from).Take(to - from).Max();
            }
            return Normalize(reduced);
        }
        return Normalize(peaks.ToArray());
    }

    public static async Task<TimeSpan> GetDurationAsync(string path, CancellationToken cancellationToken = default)
    {
        var ffprobe = FindTool("ffprobe.exe", "ffprobe");
        var startInfo = new ProcessStartInfo(ffprobe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in new[] { "-v", "error", "-show_entries", "format=duration",
                     "-of", "default=noprint_wrappers=1:nokey=1", path })
            startInfo.ArgumentList.Add(arg);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 FFprobe。");
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0 || !double.TryParse(output.Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var seconds) || !double.IsFinite(seconds))
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "无法读取媒体时长。" : error.Trim());
        return TimeSpan.FromSeconds(Math.Max(0, seconds));
    }

    private static Process StartFfmpeg(string executable, string path, TimeSpan startAt)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (startAt > TimeSpan.Zero)
        {
            info.ArgumentList.Add("-ss");
            info.ArgumentList.Add(startAt.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
        }
        foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-i", path, "-vn", "-map", "0:a:0",
                     "-ac", "2", "-ar", SampleRate.ToString(CultureInfo.InvariantCulture),
                     "-acodec", "pcm_s16le", "-f", "s16le", "pipe:1" })
            info.ArgumentList.Add(arg);
        return Process.Start(info) ?? throw new InvalidOperationException("无法启动 FFmpeg。");
    }

    private static string FindTool(string bundledName, string commandName)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "ffmpeg", "bin", bundledName),
            Path.Combine(AppContext.BaseDirectory, bundledName),
            commandName
        };
        return candidates.FirstOrDefault(x => x == commandName || File.Exists(x)) ?? commandName;
    }

    private static double[] Normalize(double[] values)
    {
        if (values.Length == 0) return values;
        var max = values.Max();
        if (max <= 0) return values;
        for (var i = 0; i < values.Length; i++)
            values[i] = Math.Clamp(Math.Pow(values[i] / max, 0.72), 0.025, 1.0);
        return values;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }

    private sealed class ProcessPcmProvider(Stream stream, WaveFormat waveFormat) : IWaveProvider
    {
        private long _bytesRead;
        public WaveFormat WaveFormat { get; } = waveFormat;
        public TimeSpan Position => TimeSpan.FromSeconds((double)Interlocked.Read(ref _bytesRead) / WaveFormat.AverageBytesPerSecond);
        public int Read(byte[] buffer, int offset, int count)
        {
            var read = stream.Read(buffer, offset, count);
            Interlocked.Add(ref _bytesRead, read);
            return read;
        }
    }
}
