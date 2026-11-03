namespace InputLib.EventArgs;

public class KeyJustPressedEventArgs
{
    public Key Key { get; set; }

    public KeyJustPressedEventArgs(Key key)
    {
        Key = key;
    }
}