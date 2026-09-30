namespace KeySound.Audio;

/// <summary>
/// Small additive piano-like synth used when no SoundFont is available.
/// Each voice is a handful of slightly inharmonic partials that decay at different rates.
/// </summary>
public sealed class SimplePianoSynth : IPianoSynth
{
    private const int MaxVoices = 48;
    private const int Partials = 8;
    private const float SilenceThreshold = 1e-4f;

    private readonly int _sampleRate;
    private readonly List<Voice> _voices = new(MaxVoices);
    private readonly bool[] _keyDown = new bool[128];
    private bool _sustain;

    public string Name => "Built-in piano (basic)";

    public SimplePianoSynth(int sampleRate) => _sampleRate = sampleRate;

    public void NoteOn(int key, int velocity)
    {
        _keyDown[key] = true;
        // Re-striking a key damps the previous string quickly instead of stacking copies.
        foreach (var v in _voices)
            if (v.Key == key) v.Release(_sampleRate, 0.03f);

        if (_voices.Count >= MaxVoices)
            _voices.RemoveAt(0);
        _voices.Add(new Voice(key, velocity, _sampleRate));
    }

    public void NoteOff(int key)
    {
        _keyDown[key] = false;
        if (_sustain) return;
        foreach (var v in _voices)
            if (v.Key == key && !v.Releasing) v.Release(_sampleRate, ReleaseTime(key));
    }

    public void ControlChange(int controller, int value)
    {
        if (controller != 64) return;
        _sustain = value >= 64;
        if (_sustain) return;
        foreach (var v in _voices)
            if (!_keyDown[v.Key] && !v.Releasing) v.Release(_sampleRate, ReleaseTime(v.Key));
    }

    public void AllNotesOff()
    {
        Array.Clear(_keyDown);
        _sustain = false;
        foreach (var v in _voices) v.Release(_sampleRate, 0.05f);
    }

    public void Render(Span<float> left, Span<float> right)
    {
        left.Clear();
        right.Clear();
        foreach (var v in _voices) v.Render(left, right);
        _voices.RemoveAll(v => v.Level < SilenceThreshold);
    }

    private static float ReleaseTime(int key) => key < 40 ? 0.35f : key < 70 ? 0.22f : 0.12f;

    private sealed class Voice
    {
        private readonly float[] _amp = new float[Partials];
        private readonly float[] _decay = new float[Partials];
        private readonly float[] _cos = new float[Partials];
        private readonly float[] _sin = new float[Partials];
        private readonly float[] _stepCos = new float[Partials];
        private readonly float[] _stepSin = new float[Partials];
        private readonly float _panL, _panR;
        private readonly float _attackStep;
        private float _attack;
        private float _release = 1f;
        private float _releaseCoef = 1f;

        public int Key { get; }
        public bool Releasing { get; private set; }
        public float Level { get; private set; } = 1f;

        public Voice(int key, int velocity, int sampleRate)
        {
            Key = key;
            float freq = 440f * MathF.Pow(2f, (key - 69) / 12f);
            float vel = velocity / 127f;
            // Velocity maps linearly onto the same dB range as SoundFontSynth, so soft notes stay audible.
            float loudness = 0.18f * MathF.Pow(10f, -SoundFontSynth.DynamicRangeDb * (1f - vel) / 20f);
            // Lower strings ring longer; harder strikes are brighter.
            float t60 = Math.Clamp(12f * MathF.Pow(2f, -(key - 21) / 20f), 0.8f, 12f);
            float brightness = 1.1f + 1.4f * (1f - vel);
            const float inharmonicity = 0.0004f;

            for (int n = 0; n < Partials; n++)
            {
                int h = n + 1;
                float f = freq * h * MathF.Sqrt(1f + inharmonicity * h * h);
                if (f >= sampleRate * 0.45f) break;
                _amp[n] = loudness / MathF.Pow(h, brightness);
                float partialT60 = t60 / (1f + 0.6f * n);
                _decay[n] = MathF.Pow(0.001f, 1f / (partialT60 * sampleRate));
                float w = 2f * MathF.PI * f / sampleRate;
                _stepCos[n] = MathF.Cos(w);
                _stepSin[n] = MathF.Sin(w);
                _cos[n] = 1f;
            }

            float pan = Math.Clamp((key - 64) / 64f, -1f, 1f) * 0.35f;
            _panL = MathF.Sqrt(0.5f * (1f - pan));
            _panR = MathF.Sqrt(0.5f * (1f + pan));
            _attackStep = 1f / (0.002f * sampleRate);
        }

        public void Release(int sampleRate, float seconds)
        {
            Releasing = true;
            _releaseCoef = MathF.Pow(0.001f, 1f / (seconds * sampleRate));
        }

        public void Render(Span<float> left, Span<float> right)
        {
            float peak = 0f;
            for (int i = 0; i < left.Length; i++)
            {
                float s = 0f;
                for (int n = 0; n < Partials; n++)
                {
                    if (_amp[n] == 0f) continue;
                    // Rotate the phasor instead of calling Sin per sample.
                    float c = _cos[n] * _stepCos[n] - _sin[n] * _stepSin[n];
                    _sin[n] = _cos[n] * _stepSin[n] + _sin[n] * _stepCos[n];
                    _cos[n] = c;
                    s += _sin[n] * _amp[n];
                    _amp[n] *= _decay[n];
                }

                if (_attack < 1f) _attack = MathF.Min(1f, _attack + _attackStep);
                _release *= _releaseCoef;
                s *= _attack * _release;
                left[i] += s * _panL;
                right[i] += s * _panR;
                peak = MathF.Max(peak, MathF.Abs(s));
            }

            // Renormalise phasors once per block to stop amplitude drift.
            for (int n = 0; n < Partials; n++)
            {
                float mag = MathF.Sqrt(_cos[n] * _cos[n] + _sin[n] * _sin[n]);
                if (mag > 0f) { _cos[n] /= mag; _sin[n] /= mag; }
            }

            Level = _attack < 1f ? 1f : peak / 0.18f;
        }
    }
}
