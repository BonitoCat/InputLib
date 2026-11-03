namespace InputLib.EventArgs;

public class KeyUpEventArgs
{
    public Key Key { get; set; }

    public KeyUpEventArgs(Key key)
    {
        Key = key;
    }
}