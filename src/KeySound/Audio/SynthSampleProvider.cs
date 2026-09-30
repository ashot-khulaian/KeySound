using System.Collections.Concurrent;
using KeySound.Midi;
using NAudio.Wave;

namespace KeySound.Audio;

/// <summary>
/// Bridges MIDI (any thread) to the synth (audio thread). Output is stereo IEEE float.
/// </summary>
public sealed class SynthSampleProvider : ISampleProvider
{
    private readonly ConcurrentQueue<MidiMessage> _pending = new();
    private IPianoSynth _synth;
    private IPianoSynth? _nextSynth;
    private float[] _left = [];
    private float[] _right = [];

    public WaveFormat WaveFormat { get; }

    /// <summary>Linear output gain, 0..1.</summary>
    public float Volume { get; set; } = 0.8f;

    public SynthSampleProvider(IPianoSynth synth, int sampleRate)
    {
        _synth = synth;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
    }

    public void Send(MidiMessage message) => _pending.Enqueue(message);

    /// <summary>Swaps the sound source at the next audio block.</summary>
    public void SetSynth(IPianoSynth synth) => Volatile.Write(ref _nextSynth, synth);

    public int Read(Span<float> buffer)
    {
        int count = buffer.Length;
        var next = Interlocked.Exchange(ref _nextSynth, null);
        if (next != null)
        {
            _synth.AllNotesOff();
            _synth = next;
        }

        while (_pending.TryDequeue(out var msg))
        {
            if (msg.IsNoteOn) _synth.NoteOn(msg.Data1, msg.Data2);
            else if (msg.IsNoteOff) _synth.NoteOff(msg.Data1);
            else if (msg.IsControlChange) _synth.ControlChange(msg.Data1, msg.Data2);
        }

        int frames = count / 2;
        if (_left.Length < frames)
        {
            _left = new float[frames];
            _right = new float[frames];
        }

        var left = _left.AsSpan(0, frames);
        var right = _right.AsSpan(0, frames);
        _synth.Render(left, right);

        float gain = Volume;
        for (int i = 0; i < frames; i++)
        {
            buffer[2 * i] = SoftClip(left[i] * gain);
            buffer[2 * i + 1] = SoftClip(right[i] * gain);
        }
        return count;
    }

    // Linear up to ±0.9, then a tanh knee that approaches ±1 without a jump.
    private static float SoftClip(float x)
    {
        float a = MathF.Abs(x);
        if (a <= 0.9f) return x;
        return MathF.CopySign(0.9f + 0.1f * MathF.Tanh((a - 0.9f) / 0.1f), x);
    }
}
