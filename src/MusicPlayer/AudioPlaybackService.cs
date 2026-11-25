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
    public float Volume { get => _output?.Volume ?? 1f; set { if (_output is not null) _output.Volume = Math.Clamp(value, 0f, 1f); } }
    public TimeSpan Position => _seekOffset + (_provider?.Position ?? TimeSpan.Zero);

    public void Play(string path, TimeSpan startAt, TimeSpan? duration = null)
    {
        Stop();
        var ffmpeg = FindTool("ffmpeg.exe", "ffmpeg");
        var start = startAt < TimeSpan.Zero ? TimeSpan.Zero : startAt;
        var process = StartFfmpeg(ffmpeg, path, start, duration);
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
        foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-i", path })
            info.ArgumentList.Add(arg);
        if (duration is { } clipDuration && clipDuration > TimeSpan.Zero)
        {
            info.ArgumentList.Add("-t");
            info.ArgumentList.Add(clipDuration.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
        }
        foreach (var arg in new[] { "-vn", "-map", "0:a:0", "-ac", "2",
                     "-ar", SampleRate.ToString(CultureInfo.InvariantCulture),
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
