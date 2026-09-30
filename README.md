# KeySound

A piano practice app for Windows. It plays a USB MIDI keyboard through your audio interface with low latency, and shows which notes you're holding and what chord they make.

## Run

```
dotnet run --project src/KeySound
```

Requires the .NET 10 SDK. On first start it picks the first hardware MIDI input and the Audient ASIO driver. It saves your device and sound choices to `%AppData%\KeySound\settings.json`.

## Features
- **Low-latency output:** ASIO (preferred). If ASIO isn't available it falls back to the WASAPI shared low-latency mode.
- **Sounds:** any `.sf2` SoundFont placed in [`SoundFonts/`](SoundFonts/README.md). Without one it uses a basic built-in piano.
- **Display:** the held note or chord name, including inversions such as `C/E`, and two-note intervals.
- **On-screen keyboard:** 88 keys. Held keys light up, and keys sustained by the pedal show in a lighter colour. You can also click keys to play them; clicking lower on a key plays louder.

## Layout
| Path | Purpose |
| --- | --- |
| `src/KeySound/Midi/MidiInput.cs` | MIDI input using the Windows WinMM API |
| `src/KeySound/Audio/AudioOutput.cs` | Opens ASIO or WASAPI output |
| `src/KeySound/Audio/SynthSampleProvider.cs` | Passes MIDI to the audio thread, handles volume and soft clipping |
| `src/KeySound/Audio/SoundFontSynth.cs` / `SimplePianoSynth.cs` | Sound engines (MeltySynth for SoundFonts; the additive built-in piano) |
| `src/KeySound/Music/` | Note names and chord detection |
| `src/KeySound/Controls/PianoKeyboard.cs` | On-screen keyboard control |

## Velocity
Some keyboards send the same very low velocity for every note, which is almost silent. KeySound notices this and switches **Velocity** to a fixed level. You can choose a mode yourself at any time. Each note's velocity is shown under the note names.

If every note shows velocity 1 no matter how you play, check the keyboard's own settings. On the M-Vave SMK-25 Mini, open M-Vave's configuration software, go to the **Keybed** tab, set **Min Vel** to 1 and **Max Vel** to 127, then click **Save Config**.

## Latency tip
Open **ASIO settings** and lower the buffer size to 64–128 samples, then click **Restart audio**. The default 512 samples adds about 15 ms of delay.
