using System.Runtime.InteropServices;

namespace KeySound.Midi;

/// <summary>A raw short MIDI message (status byte + up to two data bytes).</summary>
public readonly record struct MidiMessage(byte Status, byte Data1, byte Data2)
{
    public int Command => Status & 0xF0;
    public int Channel => Status & 0x0F;

    public bool IsNoteOn => Command == 0x90 && Data2 > 0;
    public bool IsNoteOff => Command == 0x80 || (Command == 0x90 && Data2 == 0);
    public bool IsControlChange => Command == 0xB0;
}

/// <summary>
/// Minimal WinMM MIDI input. Messages are raised on the driver's callback thread,
/// so handlers must be quick and must not touch the UI directly.
/// </summary>
public sealed class MidiInput : IDisposable
{
    private const int CallbackFunction = 0x00030000;
    private const int MimData = 0x3C3;

    private delegate void MidiInProc(IntPtr hMidiIn, int wMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2);

    // Held in a field so the GC never collects the delegate while the driver still calls it.
    private readonly MidiInProc _callback;
    private IntPtr _handle;

    public string DeviceName { get; }

    public event Action<MidiMessage>? MessageReceived;

    private MidiInput(int deviceIndex, string name)
    {
        DeviceName = name;
        _callback = OnMidiIn;
        Check(midiInOpen(out _handle, deviceIndex, _callback, IntPtr.Zero, CallbackFunction), "midiInOpen");
        Check(midiInStart(_handle), "midiInStart");
    }

    public static IReadOnlyList<string> GetDeviceNames()
    {
        int count = midiInGetNumDevs();
        var names = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            var caps = new MIDIINCAPS();
            midiInGetDevCaps(i, ref caps, Marshal.SizeOf<MIDIINCAPS>());
            names.Add(caps.szPname);
        }
        return names;
    }

    public static MidiInput Open(string deviceName)
    {
        var names = GetDeviceNames();
        for (int i = 0; i < names.Count; i++)
            if (names[i] == deviceName)
                return new MidiInput(i, deviceName);
        throw new InvalidOperationException($"MIDI input '{deviceName}' was not found.");
    }

    private void OnMidiIn(IntPtr hMidiIn, int wMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2)
    {
        if (wMsg != MimData) return;
        int raw = (int)dwParam1;
        var msg = new MidiMessage((byte)raw, (byte)(raw >> 8), (byte)(raw >> 16));
        // Ignore realtime clock / active sensing chatter.
        if (msg.Status >= 0xF0) return;
        MessageReceived?.Invoke(msg);
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        midiInStop(_handle);
        midiInReset(_handle);
        midiInClose(_handle);
        _handle = IntPtr.Zero;
    }

    private static void Check(int result, string function)
    {
        if (result != 0)
            throw new InvalidOperationException($"{function} failed (MMRESULT {result}). The device may be in use by another program.");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MIDIINCAPS
    {
        public ushort wMid;
        public ushort wPid;
        public uint vDriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szPname;
        public uint dwSupport;
    }

    [DllImport("winmm.dll")] private static extern int midiInGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "midiInGetDevCapsW")]
    private static extern int midiInGetDevCaps(nint uDeviceId, ref MIDIINCAPS caps, int cbMidiInCaps);
    [DllImport("winmm.dll")] private static extern int midiInOpen(out IntPtr handle, int deviceId, MidiInProc callback, IntPtr instance, int flags);
    [DllImport("winmm.dll")] private static extern int midiInStart(IntPtr handle);
    [DllImport("winmm.dll")] private static extern int midiInStop(IntPtr handle);
    [DllImport("winmm.dll")] private static extern int midiInReset(IntPtr handle);
    [DllImport("winmm.dll")] private static extern int midiInClose(IntPtr handle);
}
