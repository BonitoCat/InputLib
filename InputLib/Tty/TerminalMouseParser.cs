using System.Text;
using InputLib.EventArgs;

namespace InputLib.Tty;

public sealed class TerminalMouseParser
{
    private const byte Escape = 0x1b;
    private const int MaxSequenceLength = 32;

    private readonly List<byte> _sequence = new();
    private readonly List<byte> _text = new();
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();

    public Action<TerminalMouseEventArgs>? OnMouse { get; set; }
    public Action<string>? OnText { get; set; }

    public void Feed(byte[] buffer, int count)
    {
        for (int i = 0; i < count; i++)
        {
            FeedByte(buffer[i]);
        }

        EmitText();
    }

    public void FlushPending()
    {
        if (_sequence.Count == 0)
        {
            return;
        }

        _text.AddRange(_sequence);
        _sequence.Clear();
        EmitText();
    }

    private void FeedByte(byte value)
    {
        if (_sequence.Count == 0)
        {
            if (value == Escape)
            {
                _sequence.Add(value);
                return;
            }

            _text.Add(value);
            return;
        }

        _sequence.Add(value);
        if (!IsValidPrefix())
        {
            _sequence.RemoveAt(_sequence.Count - 1);
            _text.AddRange(_sequence);
            _sequence.Clear();
            FeedByte(value);
            return;
        }

        if (value is (byte) 'M' or (byte) 'm')
        {
            ParseSequence(value == (byte) 'm');
            _sequence.Clear();
        }
    }

    private bool IsValidPrefix()
    {
        int last = _sequence.Count - 1;
        byte value = _sequence[last];
        if (last >= MaxSequenceLength)
        {
            return false;
        }

        return last switch
        {
            1 => value == (byte) '[',
            2 => value == (byte) '<',
            _ => value is >= (byte) '0' and <= (byte) '9' or (byte) ';' or (byte) 'M' or (byte) 'm',
        };
    }

    private void ParseSequence(bool isRelease)
    {
        string body = Encoding.ASCII.GetString(_sequence.ToArray(), 3, _sequence.Count - 4);
        string[] parts = body.Split(';');
        if (parts.Length != 3
            || !int.TryParse(parts[0], out int code)
            || !int.TryParse(parts[1], out int column)
            || !int.TryParse(parts[2], out int row))
        {
            _text.AddRange(_sequence);
            return;
        }

        EmitText();
        OnMouse?.Invoke(CreateEvent(code, column - 1, row - 1, isRelease));
    }

    private static TerminalMouseEventArgs CreateEvent(int code, int column, int row, bool isRelease)
    {
        KeyModifiers modifiers = KeyModifiers.None;
        if ((code & 4) != 0)
        {
            modifiers |= KeyModifiers.Shift;
        }

        if ((code & 8) != 0)
        {
            modifiers |= KeyModifiers.Alt;
        }

        if ((code & 16) != 0)
        {
            modifiers |= KeyModifiers.Ctrl;
        }

        bool isMotion = (code & 32) != 0;
        int buttonCode = code & ~(4 | 8 | 16 | 32);
        MouseButton button = buttonCode switch
        {
            0 => MouseButton.Left,
            1 => MouseButton.Middle,
            2 => MouseButton.Right,
            64 => MouseButton.ScrollUp,
            65 => MouseButton.ScrollDown,
            66 => MouseButton.ScrollLeft,
            67 => MouseButton.ScrollRight,
            128 => MouseButton.Back,
            129 => MouseButton.Forward,
            _ => MouseButton.None,
        };

        TerminalMouseEventKind kind = buttonCode is >= 64 and < 128
            ? TerminalMouseEventKind.Scroll
            : isMotion
                ? TerminalMouseEventKind.Move
                : isRelease
                    ? TerminalMouseEventKind.Release
                    : TerminalMouseEventKind.Press;

        return new(kind, button, column, row, modifiers);
    }

    private void EmitText()
    {
        if (_text.Count == 0)
        {
            return;
        }

        byte[] bytes = _text.ToArray();
        _text.Clear();

        char[] chars = new char[_decoder.GetCharCount(bytes, 0, bytes.Length)];
        int charCount = _decoder.GetChars(bytes, 0, bytes.Length, chars, 0);
        if (charCount > 0)
        {
            OnText?.Invoke(new(chars, 0, charCount));
        }
    }
}
