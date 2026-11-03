namespace InputLib.EventArgs;

public class TerminalMouseEventArgs
{
    public TerminalMouseEventKind Kind { get; }
    public MouseButton Button { get; }
    public int Column { get; }
    public int Row { get; }
    public KeyModifiers Modifiers { get; }

    public TerminalMouseEventArgs(TerminalMouseEventKind kind, MouseButton button, int column, int row, KeyModifiers modifiers)
    {
        Kind = kind;
        Button = button;
        Column = column;
        Row = row;
        Modifiers = modifiers;
    }
}
