using System.IO;

namespace KeySound.Audio;

/// <summary>Finds .sf2 files and creates the synth for a chosen sound.</summary>
public static class SoundLibrary
{
    public const string BuiltInName = "Built-in piano (basic)";

    /// <summary>
    /// The SoundFonts folder next to the exe, plus the one at the repository root
    /// so files dropped there work straight from a dev build.
    /// </summary>
    public static IEnumerable<string> SearchFolders()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "SoundFonts");
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "KeySound.slnx")))
            {
                yield return Path.Combine(dir.FullName, "SoundFonts");
                yield break;
            }
        }
    }

    /// <summary>Display name → full path (null for the built-in synth).</summary>
    public static IReadOnlyList<(string Name, string? Path)> GetSounds()
    {
        var sounds = new List<(string, string?)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in SearchFolders())
        {
            if (!Directory.Exists(folder)) continue;
            foreach (var file in Directory.EnumerateFiles(folder, "*.sf2").Order())
                if (seen.Add(Path.GetFileName(file)))
                    sounds.Add((Path.GetFileNameWithoutExtension(file), file));
        }
        sounds.Add((BuiltInName, null));
        return sounds;
    }

    public static IPianoSynth Create(string? path, int sampleRate) =>
        path == null ? new SimplePianoSynth(sampleRate) : new SoundFontSynth(path, sampleRate);
}
