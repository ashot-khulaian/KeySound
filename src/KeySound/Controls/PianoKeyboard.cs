using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using KeySound.Music;

namespace KeySound.Controls;

public enum KeyState { Up, Down, Sustained }

/// <summary>On-screen piano that highlights held notes and can be played with the mouse.</summary>
public sealed class PianoKeyboard : FrameworkElement
{
    private const double BlackWidthRatio = 0.6;
    private const double BlackHeightRatio = 0.62;

    private static readonly Brush WhiteBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xF7)));
    private static readonly Brush BlackBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x22)));
    private static readonly Brush DownBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)));
    private static readonly Brush SustainBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x93, 0xC5, 0xFD)));
    private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x88)));
    private static readonly Pen OutlinePen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x5C)), 1));

    private readonly KeyState[] _states = new KeyState[128];
    private int _mouseNote = -1;

    public int LowNote { get; set; } = 21;   // A0
    public int HighNote { get; set; } = 108; // C8

    /// <summary>Raised when the user presses a key with the mouse (note, velocity).</summary>
    public event Action<int, int>? NotePressed;
    public event Action<int>? NoteReleased;

    public void SetKeyState(int note, KeyState state)
    {
        if (_states[note] == state) return;
        _states[note] = state;
        InvalidateVisual();
    }

    public void ClearAll()
    {
        Array.Clear(_states);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double whiteWidth = WhiteKeyWidth;
        if (whiteWidth <= 0) return;
        double height = ActualHeight;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        int whiteIndex = 0;
        for (int note = LowNote; note <= HighNote; note++)
        {
            if (Notes.IsBlackKey(note)) continue;
            var rect = new Rect(whiteIndex * whiteWidth, 0, whiteWidth, height);
            dc.DrawRectangle(FillFor(note, WhiteBrush), OutlinePen, rect);
            if (note % 12 == 0)
            {
                var label = new FormattedText(Notes.Name(note), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), Math.Min(11, whiteWidth * 0.55), LabelBrush, dpi);
                dc.DrawText(label, new Point(rect.X + (whiteWidth - label.Width) / 2, height - label.Height - 4));
            }
            whiteIndex++;
        }

        for (int note = LowNote; note <= HighNote; note++)
        {
            if (!Notes.IsBlackKey(note)) continue;
            dc.DrawRoundedRectangle(FillFor(note, BlackBrush), OutlinePen, BlackKeyRect(note), 2, 2);
        }
    }

    private Brush FillFor(int note, Brush idle) => _states[note] switch
    {
        KeyState.Down => DownBrush,
        KeyState.Sustained => SustainBrush,
        _ => idle,
    };

    private double WhiteKeyWidth
    {
        get
        {
            int whites = 0;
            for (int n = LowNote; n <= HighNote; n++)
                if (!Notes.IsBlackKey(n)) whites++;
            return whites == 0 ? 0 : ActualWidth / whites;
        }
    }

    private int WhiteIndexBefore(int note)
    {
        int count = 0;
        for (int n = LowNote; n < note; n++)
            if (!Notes.IsBlackKey(n)) count++;
        return count;
    }

    private Rect BlackKeyRect(int note)
    {
        double w = WhiteKeyWidth;
        double bw = w * BlackWidthRatio;
        double x = WhiteIndexBefore(note) * w - bw / 2;
        return new Rect(x, 0, bw, ActualHeight * BlackHeightRatio);
    }

    private (int Note, int Velocity) HitTest(Point p)
    {
        double height = ActualHeight;
        double whiteWidth = WhiteKeyWidth;
        if (whiteWidth <= 0) return (-1, 0);

        if (p.Y < height * BlackHeightRatio)
        {
            for (int note = LowNote; note <= HighNote; note++)
            {
                if (!Notes.IsBlackKey(note)) continue;
                var r = BlackKeyRect(note);
                if (r.Contains(p)) return (note, VelocityFromY(p.Y, r.Height));
            }
        }

        int whiteIndex = (int)(p.X / whiteWidth);
        int index = 0;
        for (int note = LowNote; note <= HighNote; note++)
        {
            if (Notes.IsBlackKey(note)) continue;
            if (index++ == whiteIndex) return (note, VelocityFromY(p.Y, height));
        }
        return (-1, 0);
    }

    // Clicking nearer the front edge of the key plays louder, like a real keyboard.
    private static int VelocityFromY(double y, double keyHeight) =>
        Math.Clamp((int)(40 + 87 * y / keyHeight), 1, 127);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        CaptureMouse();
        PressAt(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_mouseNote >= 0 && e.LeftButton == MouseButtonState.Pressed)
            PressAt(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ReleaseMouseNote();
        ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e) => ReleaseMouseNote();

    private void PressAt(Point p)
    {
        var (note, velocity) = HitTest(p);
        if (note == _mouseNote) return;
        ReleaseMouseNote();
        if (note < 0) return;
        _mouseNote = note;
        NotePressed?.Invoke(note, velocity);
    }

    private void ReleaseMouseNote()
    {
        if (_mouseNote < 0) return;
        int note = _mouseNote;
        _mouseNote = -1;
        NoteReleased?.Invoke(note);
    }

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
}
