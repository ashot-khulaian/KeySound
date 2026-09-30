using System.IO;
using MeltySynth;

namespace KeySound.Audio;

/// <summary>
/// Plays an .sf2 SoundFont (preset 0 on channel 0 — normally Acoustic Grand Piano).
/// SoundFonts often combine softly recorded layers with the SF2 velocity→attenuation curve,
/// giving 60+ dB between the softest and loudest note. On load we measure the actual response
/// and remap velocities so the range is <see cref="DynamicRangeDb"/>, like a real piano.
/// </summary>
public sealed class SoundFontSynth : IPianoSynth
{
    public const float DynamicRangeDb = 30f;
    private const int CalibrationKey = 60;
    private const float TargetPeak = 0.4f;

    private readonly Synthesizer _synth;
    private readonly byte[] _velocityMap = new byte[128];

    public string Name { get; }

    public SoundFontSynth(string path, int sampleRate)
    {
        Name = Path.GetFileNameWithoutExtension(path);
        _synth = new Synthesizer(path, sampleRate);
        Calibrate(sampleRate);
    }

    public void NoteOn(int key, int velocity) => _synth.NoteOn(0, key, _velocityMap[velocity]);
    public void NoteOff(int key) => _synth.NoteOff(0, key);
    public void ControlChange(int controller, int value) => _synth.ProcessMidiMessage(0, 0xB0, controller, value);
    public void AllNotesOff() => _synth.NoteOffAll(false);
    public void Render(Span<float> left, Span<float> right) => _synth.Render(left, right);

    private void Calibrate(int sampleRate)
    {
        var levels = new float[128];
        var left = new float[sampleRate / 20];
        var right = new float[left.Length];

        // Peak level of middle C over its first 150 ms, per velocity.
        for (int v = 1; v <= 127; v++)
        {
            _synth.NoteOn(0, CalibrationKey, v);
            float peak = 0f;
            for (int block = 0; block < 3; block++)
            {
                _synth.Render(left, right);
                foreach (float s in left) peak = MathF.Max(peak, MathF.Abs(s));
            }
            levels[v] = 20f * MathF.Log10(MathF.Max(peak, 1e-9f));
            _synth.NoteOffAll(true);
            _synth.Render(left, right);
        }
        _synth.Reset();

        float topDb = levels[127];
        if (topDb < -150f)
        {
            // Preset 0 made no sound at middle C; leave the SoundFont's own response alone.
            for (int v = 0; v < 128; v++) _velocityMap[v] = (byte)v;
            return;
        }

        // Input velocity maps linearly onto a DynamicRangeDb-wide dB scale ending at the loudest level;
        // pick the smallest SoundFont velocity that reaches each target level.
        for (int v = 1; v <= 127; v++)
        {
            float target = topDb - DynamicRangeDb * (1f - v / 127f);
            int mapped = 127;
            for (int candidate = 1; candidate <= 127; candidate++)
            {
                if (levels[candidate] >= target) { mapped = candidate; break; }
            }
            _velocityMap[v] = (byte)mapped;
        }

        _synth.MasterVolume *= TargetPeak / MathF.Pow(10f, topDb / 20f);
    }
}
