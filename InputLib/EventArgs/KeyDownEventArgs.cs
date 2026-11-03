namespace InputLib.EventArgs;

public class KeyDownEventArgs
{
    public Key Key { get; set; }
    public bool Continuous { get; set; }

    public KeyDownEventArgs(Key key, bool continuous)
    {
        Key = key;
        Continuous = continuous;
    }
}