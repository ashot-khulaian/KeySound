namespace KeySound.Music;

/// <summary>Names common chords from a set of held notes, including inversions (slash bass).</summary>
public static class ChordDetector
{
    // Interval sets relative to the root, as 12-bit masks (bit 0 = root).
    private static readonly (string Suffix, int Mask)[] Chords =
    [
        ("maj7", Mask(0, 4, 7, 11)),
        ("7", Mask(0, 4, 7, 10)),
        ("m7", Mask(0, 3, 7, 10)),
        ("m(maj7)", Mask(0, 3, 7, 11)),
        ("m7b5", Mask(0, 3, 6, 10)),
        ("dim7", Mask(0, 3, 6, 9)),
        ("6", Mask(0, 4, 7, 9)),
        ("m6", Mask(0, 3, 7, 9)),
        ("add9", Mask(0, 2, 4, 7)),
        ("", Mask(0, 4, 7)),
        ("m", Mask(0, 3, 7)),
        ("dim", Mask(0, 3, 6)),
        ("aug", Mask(0, 4, 8)),
        ("sus2", Mask(0, 2, 7)),
        ("sus4", Mask(0, 5, 7)),
        ("5", Mask(0, 7)),
    ];

    private static readonly string[] IntervalNames =
        ["unison", "minor 2nd", "major 2nd", "minor 3rd", "major 3rd", "perfect 4th",
         "tritone", "perfect 5th", "minor 6th", "major 6th", "minor 7th", "major 7th", "octave"];

    public static string? Detect(IReadOnlyCollection<int> notes)
    {
        if (notes.Count < 2) return null;

        int bass = notes.Min();
        int pitchClasses = 0;
        foreach (int n in notes) pitchClasses |= 1 << (n % 12);

        if (notes.Count == 2)
        {
            int interval = notes.Max() - bass;
            return interval <= 12 ? IntervalNames[interval] : $"{IntervalNames[interval % 12]} (+{interval / 12} oct)";
        }

        // Prefer the reading whose root is the bass note (root position), then any other root.
        foreach (int root in RootCandidates(bass % 12, pitchClasses))
        {
            int relative = Rotate(pitchClasses, root);
            foreach (var (suffix, mask) in Chords)
            {
                if (relative != mask) continue;
                string name = Notes.PitchClassName(root) + suffix;
                return root == bass % 12 ? name : $"{name}/{Notes.PitchClassName(bass)}";
            }
        }
        return null;
    }

    private static IEnumerable<int> RootCandidates(int bassPc, int pitchClasses)
    {
        yield return bassPc;
        for (int pc = 0; pc < 12; pc++)
            if (pc != bassPc && (pitchClasses & (1 << pc)) != 0)
                yield return pc;
    }

    private static int Rotate(int mask, int root) => ((mask >> root) | (mask << (12 - root))) & 0xFFF;

    private static int Mask(params int[] intervals) => intervals.Aggregate(0, (m, i) => m | (1 << i));
}
