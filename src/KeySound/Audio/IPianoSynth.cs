namespace KeySound.Audio;

/// <summary>
/// A sound generator driven from the audio thread only. Implementations need not be thread-safe;
/// <see cref="SynthSampleProvider"/> marshals MIDI events onto the audio thread.
/// </summary>
public interface IPianoSynth
{
    string Name { get; }
    void NoteOn(int key, int velocity);
    void NoteOff(int key);
    void ControlChange(int controller, int value);
    void AllNotesOff();
    void Render(Span<float> left, Span<float> right);
}
