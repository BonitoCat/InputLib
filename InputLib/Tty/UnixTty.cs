using System.Runtime.InteropServices;

namespace InputLib.Tty;

internal static class UnixTty
{
    [StructLayout(LayoutKind.Sequential)]
    struct termios
    {
        public uint c_iflag;
        public uint c_oflag;
        public uint c_cflag;
        public uint c_lflag;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] c_cc;
        public uint c_ispeed;
        public uint c_ospeed;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct pollfd
    {
        public int fd;
        public short events;
        public short revents;
    }
    
    private static bool _isLinux;

    const int STDIN = 0;
    const int STDIN_FILENO = 0;
    const int TCSANOW = 0;
    const uint ICANON = 0x0002;
    const uint ECHO = 0x0008;
    const uint IXON = 0x0400;
    const int VMIN = 0;
    const int VTIME = 1;

    [DllImport("libc")] internal static extern int read(int fd, byte[] buffer, int count);
    [DllImport("libc")] static extern int tcgetattr(int fd, out termios termios);
    [DllImport("libc")] static extern int tcsetattr(int fd, uint actions, ref termios termios);
    [DllImport("libc")] static extern int tcflush(int fd, int queue);
    [DllImport("libc")] static extern int write(int fd, byte[] buffer, int count);
    [DllImport("libc")] static extern int isatty(int fd);
    [DllImport("libc")] static extern int poll([In, Out] pollfd[] fds, uint nfds, int timeout);

    private const int TCIFLUSH = 0;
    private const short POLLIN = 0x0001;

    private const string EnableMouseSeq  = "\x1b[?1000h\x1b[?1006h";
    private const string DisableMouseSeq = "\x1b[?1000l\x1b[?1006l";
    private const string EnableMouseTrackingSeq = "\x1b[?1003h\x1b[?1006h";
    private const string DisableMouseTrackingSeq = "\x1b[?1003l\x1b[?1006l";
    private static volatile bool _mouseTracking;
    private static termios _original;
    private static bool _saved;

    public static void Init()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return;
        }
        
        _isLinux = true;
        if (!_saved)
        {
            tcgetattr(STDIN, out _original);
            _saved = true;
            
            AppDomain.CurrentDomain.ProcessExit += (_, _) => PanicRestore();
            Console.CancelKeyPress += (_, _) => PanicRestore();
            AppDomain.CurrentDomain.UnhandledException += (_, _) => PanicRestore();
        }
    }
    
    private static void PanicRestore()
    {
        if (!_isLinux)
        {
            return;
        }
        
        try
        {
            DisableMouseTracking();
            Flush();
            EnableEcho();
        }
        catch { }
    }
    
    public static void DisableEcho()
    {
        if (!_isLinux)
        {
            return;
        }
        
        tcgetattr(STDIN, out termios t);
        t.c_lflag &= ~(ECHO | ICANON);
        t.c_iflag &= ~IXON;

        tcsetattr(STDIN, TCSANOW, ref t);
    }

    public static void EnableEcho()
    {
        if (!_isLinux)
        {
            return;
        }

        if (_saved)
        {
            Flush();
            tcsetattr(STDIN, TCSANOW, ref _original);
        }
    }

    public static void EnableMouse()
    {
        if (!_isLinux)
        {
            return;
        }
        
        byte[] seq = System.Text.Encoding.ASCII.GetBytes(EnableMouseSeq);
        write(1, seq, seq.Length);
    }

    public static void DisableMouse()
    {
        if (!_isLinux)
        {
            return;
        }
        
        byte[] seq = System.Text.Encoding.ASCII.GetBytes(DisableMouseSeq);
        write(1, seq, seq.Length);
    }

    public static bool IsTerminal => _isLinux && isatty(STDIN) == 1 && isatty(1) == 1;

    public static void EnableMouseTracking()
    {
        if (!_isLinux)
        {
            return;
        }

        byte[] seq = System.Text.Encoding.ASCII.GetBytes(EnableMouseTrackingSeq);
        write(1, seq, seq.Length);
        _mouseTracking = true;
    }

    public static void DisableMouseTracking()
    {
        if (!_isLinux || !_mouseTracking)
        {
            return;
        }

        byte[] seq = System.Text.Encoding.ASCII.GetBytes(DisableMouseTrackingSeq);
        write(1, seq, seq.Length);
        _mouseTracking = false;
    }

    public static bool WaitForInput(int timeoutMs)
    {
        pollfd[] fds = [new() { fd = STDIN, events = POLLIN }];
        return poll(fds, 1, timeoutMs) > 0 && (fds[0].revents & POLLIN) != 0;
    }

    public static void Flush()
    {
        if (!_isLinux)
        {
            return;
        }

        tcflush(STDIN, TCIFLUSH);   
    }
}
