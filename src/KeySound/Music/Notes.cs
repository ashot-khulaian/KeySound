namespace KeySound.Music;

public static class Notes
{
    private static readonly string[] SharpNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    public static string PitchClassName(int midiNote) => SharpNames[((midiNote % 12) + 12) % 12];

    /// <summary>Scientific pitch notation: MIDI 60 = C4 (middle C).</summary>
    public static string Name(int midiNote) => $"{PitchClassName(midiNote)}{midiNote / 12 - 1}";

    public static bool IsBlackKey(int midiNote) => (midiNote % 12) is 1 or 3 or 6 or 8 or 10;
}
