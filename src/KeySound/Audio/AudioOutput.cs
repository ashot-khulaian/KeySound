using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace KeySound.Audio;

/// <summary>Opens either an ASIO driver (lowest latency) or the Windows default device via WASAPI.</summary>
public sealed class AudioOutput : IDisposable
{
    public const string WasapiName = "Windows default (WASAPI)";
    private const int WasapiLatencyMs = 20;

    private readonly IWavePlayer _player;
    private readonly AsioOut? _asio;

    public string Description { get; }

    private AudioOutput(IWavePlayer player, AsioOut? asio, string description)
    {
        _player = player;
        _asio = asio;
        Description = description;
    }

    /// <summary>ASIO drivers first (preferred), then the WASAPI fallback.</summary>
    public static IReadOnlyList<string> GetOutputNames()
    {
        var names = new List<string>();
        try { names.AddRange(AsioOut.GetDriverNames()); } catch { /* no ASIO installed */ }
        names.Add(WasapiName);
        return names;
    }

    /// <summary>Picks a sample rate the output accepts; the synth must be built at this rate.</summary>
    public static int ChooseSampleRate(string outputName)
    {
        if (outputName == WasapiName) return 48000;
        using var asio = new AsioOut(outputName);
        foreach (int rate in new[] { 48000, 44100, 96000 })
            if (asio.IsSampleRateSupported(rate)) return rate;
        return 48000;
    }

    public static AudioOutput Open(string outputName, ISampleProvider source)
    {
        if (outputName == WasapiName)
        {
            var wasapi = new WasapiPlayerBuilder()
                .WithSharedMode()
                .WithLowLatency(true)
                .WithLatency(WasapiLatencyMs)
                .WithMmcssThreadPriority("Pro Audio")
                .Build();
            wasapi.Init(new SampleToWaveProvider(source));
            wasapi.Play();
            string mode = wasapi.LowLatencyActive ? "low-latency shared" : "shared";
            return new AudioOutput(wasapi, null, $"WASAPI {mode} · {source.WaveFormat.SampleRate} Hz · ~{wasapi.LatencyMilliseconds} ms");
        }

        var asio = new AsioOut(outputName);
        try
        {
            asio.Init(new SampleToWaveProvider(source));
            asio.Play();
        }
        catch
        {
            asio.Dispose();
            throw;
        }

        int rate = source.WaveFormat.SampleRate;
        double bufferMs = 1000.0 * asio.FramesPerBuffer / rate;
        double totalMs = 1000.0 * asio.PlaybackLatency / rate;
        return new AudioOutput(asio, asio,
            $"ASIO · {rate} Hz · {asio.FramesPerBuffer} samples ({bufferMs:0.0} ms) · output latency {totalMs:0.0} ms");
    }

    public bool CanShowControlPanel => _asio != null;

    public void ShowControlPanel() => _asio?.ShowControlPanel();

    public void Dispose()
    {
        _player.Stop();
        _player.Dispose();
    }
}
