using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using KeySound.Audio;
using KeySound.Controls;
using KeySound.Midi;
using KeySound.Music;

namespace KeySound;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly bool[] _held = new bool[128];
    private readonly bool[] _sustained = new bool[128];
    private bool _sustainPedal;

    private MidiInput? _midi;
    private AudioOutput? _output;
    private volatile SynthSampleProvider? _provider;
    private int _sampleRate;
    private int _audioVersion;
    private bool _populating;

    // Some keyboards send a constant, near-zero velocity (e.g. a fixed-velocity mode), which is inaudible.
    // A fixed mode replaces the keyboard's velocity; 0 means "use what the keyboard sends".
    private static readonly (string Name, int Velocity)[] VelocityModes =
        [("From keyboard", 0), ("Fixed soft (64)", 64), ("Fixed medium (90)", 90), ("Fixed loud (110)", 110)];
    private const string AutoFixedVelocityMode = "Fixed medium (90)";
    private const int BrokenVelocityThreshold = 2;
    private const int BrokenVelocityNotes = 4;
    private volatile int _fixedVelocity;
    private int _lowVelocityStreak;

    public MainWindow()
    {
        InitializeComponent();
        PianoKeys.NotePressed += (note, velocity) => HandleMessage(new MidiMessage(0x90, (byte)note, (byte)velocity), fromKeyboard: false);
        PianoKeys.NoteReleased += note => HandleMessage(new MidiMessage(0x80, (byte)note, 0), fromKeyboard: false);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        VolumeSlider.Value = _settings.Volume;
        _populating = true;
        VelocityCombo.ItemsSource = VelocityModes.Select(m => m.Name).ToList();
        VelocityCombo.SelectedItem = VelocityModes.Any(m => m.Name == _settings.VelocityMode) ? _settings.VelocityMode : VelocityModes[0].Name;
        _populating = false;
        PopulateDevices();
        OpenMidi();
        await StartAudioAsync();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _settings.Save();
        _midi?.Dispose();
        _output?.Dispose();
    }

    // ---------- Devices ----------

    private void PopulateDevices()
    {
        _populating = true;
        try
        {
            var midiNames = MidiInput.GetDeviceNames();
            MidiCombo.ItemsSource = midiNames;
            MidiCombo.SelectedItem = Pick(midiNames, _settings.MidiInput,
                n => !n.Contains("vMIDI", StringComparison.OrdinalIgnoreCase) && !n.StartsWith("MIDIIN", StringComparison.OrdinalIgnoreCase));

            var outputs = AudioOutput.GetOutputNames();
            OutputCombo.ItemsSource = outputs;
            OutputCombo.SelectedItem = Pick(outputs, _settings.AudioOutput,
                n => n.Contains("Audient", StringComparison.OrdinalIgnoreCase))
                ?? outputs.FirstOrDefault(n => n != AudioOutput.WasapiName && !n.Contains("Synchronous", StringComparison.OrdinalIgnoreCase));

            var sounds = SoundLibrary.GetSounds();
            SoundCombo.ItemsSource = sounds.Select(s => s.Name).ToList();
            SoundCombo.SelectedItem = Pick(sounds.Select(s => s.Name).ToList(), _settings.Sound, _ => true);
        }
        finally
        {
            _populating = false;
        }
    }

    /// <summary>The saved choice if still present, else the first item matching the preference, else the first item.</summary>
    private static string? Pick(IReadOnlyList<string> items, string? saved, Func<string, bool> preferred)
    {
        if (saved != null && items.Contains(saved)) return saved;
        return items.FirstOrDefault(preferred) ?? items.FirstOrDefault();
    }

    private string? SelectedSoundPath =>
        SoundLibrary.GetSounds().FirstOrDefault(s => s.Name == (string?)SoundCombo.SelectedItem).Path;

    private void OpenMidi()
    {
        _midi?.Dispose();
        _midi = null;
        if (MidiCombo.SelectedItem is not string name) return;
        try
        {
            _midi = MidiInput.Open(name);
            _midi.MessageReceived += msg => HandleMessage(msg, fromKeyboard: true);
            _settings.MidiInput = name;
        }
        catch (Exception ex)
        {
            ShowError($"MIDI: {ex.Message}");
        }
    }

    private async Task StartAudioAsync()
    {
        int version = ++_audioVersion;
        _provider = null;
        _output?.Dispose();
        _output = null;
        AsioPanelButton.IsEnabled = false;

        if (OutputCombo.SelectedItem is not string outputName) return;
        string? soundPath = SelectedSoundPath;
        StatusText.Text = "Starting audio…";

        try
        {
            int rate = AudioOutput.ChooseSampleRate(outputName);
            var synth = await Task.Run(() => SoundLibrary.Create(soundPath, rate));
            if (version != _audioVersion) return;

            var provider = new SynthSampleProvider(synth, rate) { Volume = (float)VolumeSlider.Value };
            _output = AudioOutput.Open(outputName, provider);
            _provider = provider;
            _sampleRate = rate;
            _settings.AudioOutput = outputName;
            _settings.Sound = (string?)SoundCombo.SelectedItem;
            AsioPanelButton.IsEnabled = _output.CanShowControlPanel;
            UpdateStatus(synth.Name);
        }
        catch (Exception ex)
        {
            ShowError($"Audio: {ex.Message}");
        }
    }

    private async Task ChangeSoundAsync()
    {
        if (_provider is not { } provider) { await StartAudioAsync(); return; }

        int version = ++_audioVersion;
        string? soundPath = SelectedSoundPath;
        int rate = _sampleRate;
        StatusText.Text = "Loading sound…";
        try
        {
            var synth = await Task.Run(() => SoundLibrary.Create(soundPath, rate));
            if (version != _audioVersion) return;
            provider.SetSynth(synth);
            _settings.Sound = (string?)SoundCombo.SelectedItem;
            UpdateStatus(synth.Name);
        }
        catch (Exception ex)
        {
            ShowError($"Sound: {ex.Message}");
        }
    }

    private void UpdateStatus(string soundName)
    {
        string midi = _midi?.DeviceName ?? "no MIDI input";
        StatusText.Foreground = System.Windows.Media.Brushes.Gray;
        StatusText.Text = $"{midi}  →  {soundName}  →  {_output?.Description}";
    }

    private void ShowError(string message)
    {
        StatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
        StatusText.Text = message;
    }

    // ---------- MIDI handling ----------

    /// <summary>Called from the MIDI driver thread or the UI (mouse). Sound first, UI after.</summary>
    private void HandleMessage(MidiMessage msg, bool fromKeyboard)
    {
        var played = msg;
        int fixedVelocity = _fixedVelocity;
        if (fromKeyboard && msg.IsNoteOn && fixedVelocity > 0)
            played = msg with { Data2 = (byte)fixedVelocity };

        _provider?.Send(played);
        if (msg.IsNoteOn || msg.IsNoteOff || (msg.IsControlChange && msg.Data1 == 64))
            Dispatcher.BeginInvoke(() => UpdateDisplay(played, fromKeyboard ? msg.Data2 : -1));
    }

    /// <param name="keyboardVelocity">Velocity the keyboard actually sent, or -1 for mouse input.</param>
    private void UpdateDisplay(MidiMessage msg, int keyboardVelocity)
    {
        int note = msg.Data1;
        if (msg.IsNoteOn)
        {
            _held[note] = true;
            _sustained[note] = _sustainPedal;
            VelocityText.Text = keyboardVelocity < 0 || keyboardVelocity == msg.Data2
                ? $"Velocity {msg.Data2}"
                : $"Velocity {keyboardVelocity} from keyboard → played at {msg.Data2}";
            DetectBrokenVelocity(keyboardVelocity);
        }
        else if (msg.IsNoteOff)
        {
            _held[note] = false;
        }
        else
        {
            _sustainPedal = msg.Data2 >= 64;
            SustainText.Visibility = _sustainPedal ? Visibility.Visible : Visibility.Hidden;
            for (int n = 0; n < 128; n++)
                _sustained[n] = _sustainPedal && (_held[n] || _sustained[n]);
        }

        for (int n = 0; n < 128; n++)
            PianoKeys.SetKeyState(n, _held[n] ? KeyState.Down : _sustained[n] ? KeyState.Sustained : KeyState.Up);

        var sounding = Enumerable.Range(0, 128).Where(n => _held[n] || _sustained[n]).ToList();
        if (sounding.Count == 0)
        {
            ChordText.Opacity = 0.35;
            NotesText.Opacity = 0.35;
            return;
        }

        ChordText.Opacity = 1;
        NotesText.Opacity = 1;
        NotesText.Text = string.Join("  ", sounding.Select(Notes.Name));
        ChordText.Text = sounding.Count == 1 ? Notes.Name(sounding[0]) : ChordDetector.Detect(sounding) ?? "?";
    }

    /// <summary>
    /// If the user never chose a velocity mode and the keyboard keeps sending near-zero velocities,
    /// switch to a fixed velocity so notes are audible.
    /// </summary>
    private void DetectBrokenVelocity(int keyboardVelocity)
    {
        if (keyboardVelocity < 0 || _settings.VelocityMode != null) return;
        _lowVelocityStreak = keyboardVelocity <= BrokenVelocityThreshold ? _lowVelocityStreak + 1 : 0;
        if (_lowVelocityStreak < BrokenVelocityNotes) return;

        VelocityCombo.SelectedItem = AutoFixedVelocityMode;
        StatusText.Foreground = System.Windows.Media.Brushes.Goldenrod;
        StatusText.Text = $"Your keyboard sends velocity {keyboardVelocity} for every note, so Velocity was switched to " +
                          $"\"{AutoFixedVelocityMode}\". Check the keyboard's velocity-curve setting to get dynamics back.";
    }

    // ---------- UI events ----------

    private void VelocityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var mode = VelocityModes.FirstOrDefault(m => m.Name == (string?)VelocityCombo.SelectedItem);
        _fixedVelocity = mode.Velocity;
        if (!_populating) _settings.VelocityMode = mode.Name;
    }

    private void MidiCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_populating) return;
        OpenMidi();
        if (_output != null) UpdateStatus((string?)SoundCombo.SelectedItem ?? "");
    }

    private async void OutputCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_populating) await StartAudioAsync();
    }

    private async void SoundCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_populating) await ChangeSoundAsync();
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _settings.Volume = e.NewValue;
        if (_provider is { } p) p.Volume = (float)e.NewValue;
    }

    private void AsioPanelButton_Click(object sender, RoutedEventArgs e)
    {
        _output?.ShowControlPanel();
        MessageBox.Show(this, "After changing the buffer size in the ASIO panel, click \"Restart audio\" to apply it.",
            "KeySound", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void RestartAudio_Click(object sender, RoutedEventArgs e) => await StartAudioAsync();

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        PopulateDevices();
        OpenMidi();
        await StartAudioAsync();
    }

    private void SoundFolder_Click(object sender, RoutedEventArgs e)
    {
        string folder = SoundLibrary.SearchFolders().Last();
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }
}
