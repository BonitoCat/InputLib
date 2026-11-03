namespace InputLib.EventArgs;

public class MouseButtonEventArgs
{
    public MouseButton Button { get; set; }
    public uint RawButton => Button.Id;
    public int X { get; set; }
    public int Y { get; set; }
    public int ScreenX { get; set; }
    public int ScreenY { get; set; }
    public bool IsDown { get; set; }
    public int Column { get; set; } = -1;
    public int Row { get; set; } = -1;

    public MouseButtonEventArgs(MouseButton button, int x, int y, int screenX, int screenY, bool isDown)
    {
        Button = button;
        X = x;
        Y = y;
        ScreenX = screenX;
        ScreenY = screenY;
        IsDown = isDown;
    }

    public MouseButtonEventArgs(uint button, int x, int y, int screenX, int screenY, bool isDown)
        : this(MouseButton.FromId(button), x, y, screenX, screenY, isDown)
    {
    }
}
