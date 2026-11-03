namespace InputLib;

[Flags]
public enum KeyModifiers : byte
{
    None  = 0,
    Shift = 1 << 0,
    Ctrl  = 1 << 1,
    Alt   = 1 << 2,
}