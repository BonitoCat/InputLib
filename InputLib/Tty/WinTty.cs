using System.Runtime.InteropServices;

namespace InputLib.Tty;

internal static class WinTty
{
    const int STD_INPUT_HANDLE = -10;

    const uint ENABLE_ECHO_INPUT      = 0x0004;
    const uint ENABLE_LINE_INPUT      = 0x0002;
    const uint ENABLE_PROCESSED_INPUT = 0x0001;

    [DllImport("kernel32.dll")]
    static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll")]
    static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll")]
    static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    public static void DisableEcho()
    {
        var h = GetStdHandle(STD_INPUT_HANDLE);
        GetConsoleMode(h, out var mode);
        mode &= ~ENABLE_ECHO_INPUT;
        SetConsoleMode(h, mode);
    }

    public static void EnableEcho()
    {
        var h = GetStdHandle(STD_INPUT_HANDLE);
        GetConsoleMode(h, out var mode);
        mode |= ENABLE_ECHO_INPUT;
        SetConsoleMode(h, mode);
    }
}