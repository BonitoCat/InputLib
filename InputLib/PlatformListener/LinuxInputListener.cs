using System.Runtime.InteropServices;

namespace InputLib.PlatformListener;

/// <summary>
/// Input listener for Linux. If unsure about the OS, use <see cref="InputListener.New()"/> instead.
/// </summary>
[Obsolete("This way of detecting input is outdated and comes with many weird quirks. Use LinuxX11InputListener instead.")]
public class LinuxInputListener : InputListener
{
    private IntPtr _display;

    #region X11 P/Invoke
    
    [DllImport("X11")]
    private static extern IntPtr XInternAtom(IntPtr display, string atom_name, bool only_if_exists);

    [DllImport("X11")]
    private static extern int XGetWindowProperty(
        IntPtr display,
        IntPtr window,
        IntPtr property,
        IntPtr long_offset,
        IntPtr long_length,
        bool delete,
        IntPtr req_type,
        out IntPtr actual_type_return,
        out int actual_format_return,
        out IntPtr nitems_return,
        out IntPtr bytes_after_return,
        out IntPtr prop_return);

    [DllImport("X11")]
    private static extern int XFree(IntPtr data);

    [DllImport("X11")]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);
    
    [DllImport("X11")]
    private static extern IntPtr XOpenDisplay(IntPtr display);

    [DllImport("X11")]
    private static extern int XCloseDisplay(IntPtr display);
    
    [DllImport("X11")]
    private static extern int XQueryKeymap(IntPtr display, byte[] keys);
    
    [DllImport("X11")]
    private static extern int XLookupString(IntPtr keyEvent, byte[] buffer, int bufSize, out UIntPtr keysym, IntPtr compose);
    
    private const int KeyPress = 2;
    private const int KeyRelease = 3;
    private const int ButtonPress = 4;
    private const int ButtonRelease = 5;
    private const int MotionNotify = 6;

    private const long KeyPressMask = 1 << 0;
    private const long KeyReleaseMask = 1 << 1;
    private const long ButtonPressMask = 1 << 2;
    private const long ButtonReleaseMask = 1 << 3;
    private const long PointerMotionMask = 1 << 6;
    
    const uint ShiftMask = 1 << 0;
    const uint Mod2Mask = 1 << 4;
    const uint ControlMask = 1 << 2;
    const uint Mod1Mask = 1 << 3;
    const uint Mod5Mask = 1 << 7;
    
    [StructLayout(LayoutKind.Sequential)]
    struct XKeyEvent
    {
        public int type;
        public IntPtr serial;
        public bool send_event;
        public IntPtr display;
        public IntPtr window;
        public IntPtr root;
        public IntPtr subwindow;
        public IntPtr time;
        public int x, y, x_root, y_root;
        public uint state;
        public uint keycode;
        public bool same_screen;
    }
    
    #endregion
    
    public LinuxInputListener()
    {
        _display = XOpenDisplay(IntPtr.Zero);
        if (_display == IntPtr.Zero)
        {
            throw new Exception("Failed to open X11 display");
        }
        
        string? windowIdStr = Environment.GetEnvironmentVariable("WINDOWID");
        if (long.TryParse(windowIdStr, out long windowId))
        {
            _handle = (IntPtr) windowId;
        }
        else
        {
            _handle = GetActiveWindow();
        }
    }
    
    public LinuxInputListener(IntPtr windowHandle)
    {
        _display = XOpenDisplay(IntPtr.Zero);
        if (_display == IntPtr.Zero)
        {
            throw new Exception("Failed to open X11 display");
        }

        _handle = windowHandle;
    }
    
    public override Task StartListening(CancellationToken token)
    {
        if (_listening && _listeningTask != null)
        {
            return _listeningTask;
        }

        _listeningCtx = CancellationTokenSource.CreateLinkedTokenSource(token);
        
        _listening = true;
        _listeningTask = Task.Factory.StartNew(() =>
        {
            byte[] keys = new byte[32];
            bool[] lastState = new bool[256];
            
            while (_listeningCtx != null && !_listeningCtx.IsCancellationRequested)
            {
                if (PauseListening)
                {
                    _pauseEvent.Set();

                    while (PauseListening && !_listeningCtx.IsCancellationRequested)
                    {
                        Thread.Sleep(5);
                    }

                    _pauseEvent.Reset();
                    continue;
                }
                
                IntPtr active = GetActiveWindow();
                bool isFocused = active == _handle;

                if (!GlobalInput && !isFocused)
                {
                    if (_wasFocused)
                    {
                        ClearKeyState();
                        _pressTimers.Clear();
                        Array.Clear(lastState, 0, lastState.Length);

                        _wasFocused = false;
                    }

                    Thread.Sleep(5);
                    continue;
                }

                if (!_wasFocused && isFocused)
                {
                    ClearKeyState();
                    _pressTimers.Clear();
                    Array.Clear(lastState, 0, lastState.Length);
                    
                    _wasFocused = true;
                    Thread.Sleep(100);
                    continue;
                }

                _wasFocused = true;
                XQueryKeymap(_display, keys);
                for (int i = 0; i < 256; i++)
                {
                    if (PauseListening)
                    {
                        continue;
                    }
                    
                    int byteIndex = i / 8;
                    int bitIndex = i % 8;

                    bool pressed = (keys[byteIndex] & (1 << bitIndex)) != 0;
                    if (pressed)
                    {
                        if (!lastState[i])
                        {
                            HandleKeyDown((uint) i, false);
                            _delayTimer.Restart();
                            _repeatTimer.Restart();
                        }
                        else if (_delayTimer.ElapsedMilliseconds >= RepeatDelayMs && _repeatTimer.ElapsedMilliseconds >= RepeatIntervalMs && SendRepeatEvents)
                        {
                            HandleKeyDown((uint) i, true);
                            _delayTimer.Stop();
                            _repeatTimer.Restart();
                            
                            Thread.Sleep(Math.Max(RepeatIntervalMs - 1, 0));
                        }
                    }
                    else if (lastState[i])
                    {
                        HandleKeyUp((uint) i);
                    }
                    
                    lastState[i] = pressed;
                }

                if (_listeningCtx != null && !_listeningCtx.IsCancellationRequested)
                {
                    Thread.Sleep(1);
                }
            }

            XCloseDisplay(_display);
            _listening = false;
        }, _listeningCtx.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        
        return _listeningTask;
    }
    
    private uint BuildModifierState()
    {
        uint s = 0;
        if (IsKeyDown(Key.LeftShift) || IsKeyDown(Key.RightShift)) s |= ShiftMask;
        if (IsKeyDown(Key.LeftCtrl)  || IsKeyDown(Key.RightCtrl)) s |= ControlMask;
        if (IsKeyDown(Key.Alt)) s |= Mod1Mask;
        if (IsKeyDown(Key.AltGr)) s |= Mod5Mask;
        if (IsKeyDown(Key.NumLock)) s |= Mod2Mask;
        return s;
    }
    
    private uint? GetKeycodeFromKey(Key key)
    {
        foreach (uint code in Enumerable.Range(0, 256))
        {
            if (MapKey(code) == key)
            {
                return code;
            }
        }
        
        return null;
    }
    
    protected override Key? MapKey(uint keycode)
    {
        switch (keycode)
        {
            case 9: return Key.Escape;
            case 50: return Key.LeftShift;
            case 62: return Key.RightShift;
            case 66: return Key.CapsLock;
            case 37: return Key.LeftCtrl;
            case 105: return Key.RightCtrl;
            case 64: return Key.Alt;
            case 108: return Key.AltGr;
            case 23: return Key.Tab;
            case 133: return Key.Win;
            case 135: return Key.Menu;
            case 36: return Key.Enter;
            case 65: return Key.Space;
            case 22: return Key.Backspace;
            case 107: return Key.Print;
            case 118: return Key.Insert;
            case 119: return Key.Delete;
            
            case 67: return Key.F1;
            case 68: return Key.F2;
            case 69: return Key.F3;
            case 70: return Key.F4;
            case 71: return Key.F5;
            case 72: return Key.F6;
            case 73: return Key.F7;
            case 74: return Key.F8;
            case 75: return Key.F9;
            case 76: return Key.F10;
            case 95: return Key.F11;
            case 96: return Key.F12;
            //case : return Key.F13;
            //case : return Key.F14;
            //case : return Key.F15;
            //case : return Key.F16;
            //case : return Key.F17;
            //case : return Key.F18;
            //case : return Key.F19;
            //case : return Key.F20;
            //case : return Key.F21;
            //case : return Key.F22;
            //case : return Key.F23;
            //case : return Key.F24;
            
            case 10: return Key.D1;
            case 11: return Key.D2;
            case 12: return Key.D3;
            case 13: return Key.D4;
            case 14: return Key.D5;
            case 15: return Key.D6;
            case 16: return Key.D7;
            case 17: return Key.D8;
            case 18: return Key.D9;
            case 19: return Key.D0;
            
            case 77: return Key.NumLock;
            case 87: return Key.N1;
            case 88: return Key.N2;
            case 89: return Key.N3;
            case 83: return Key.N4;
            case 84: return Key.N5;
            case 85: return Key.N6;
            case 79: return Key.N7;
            case 80: return Key.N8;
            case 81: return Key.N9;
            case 90: return Key.N0;
            
            case 49: return Key.Backtick;
            case 20: return Key.Minus;
            case 21: return Key.Equal;
            case 110: return Key.Home;
            case 115: return Key.End;
            case 112: return Key.PageUp;
            case 117: return Key.PageDown;
            
            case 24: return Key.Q;
            case 25: return Key.W;
            case 26: return Key.E;
            case 27: return Key.R;
            case 28: return Key.T;
            case 29: return Key.Y;
            case 30: return Key.U;
            case 31: return Key.I;
            case 32: return Key.O;
            case 33: return Key.P;
            
            case 38: return Key.A;
            case 39: return Key.S;
            case 40: return Key.D;
            case 41: return Key.F;
            case 42: return Key.G;
            case 43: return Key.H;
            case 44: return Key.J;
            case 45: return Key.K;
            case 46: return Key.L;
            
            case 52: return Key.Z;
            case 53: return Key.X;
            case 54: return Key.C;
            case 55: return Key.V;
            case 56: return Key.B;
            case 57: return Key.N;
            case 58: return Key.M;
            
            case 59: return Key.Comma;
            case 60: return Key.Period;
            case 61: return Key.Slash;
            case 51: return Key.BackSlash;
            case 47: return Key.SemiColon;
            case 48: return Key.Apostrophe;
            case 34: return Key.SquareBracketLeft;
            case 35: return Key.SquareBracketRight;
            
            case 111: return Key.ArrowUp;
            case 116: return Key.ArrowDown;
            case 113: return Key.ArrowLeft;
            case 114: return Key.ArrowRight;

            default: return null;
        }
    }
    
    private IntPtr GetActiveWindow()
    {
        IntPtr root = XDefaultRootWindow(_display);
        IntPtr atom = XInternAtom(_display, "_NET_ACTIVE_WINDOW", false);

        XGetWindowProperty(
            _display,
            root,
            atom,
            IntPtr.Zero,
            (IntPtr)1,
            false,
            IntPtr.Zero,
            out _,
            out _,
            out IntPtr nitems,
            out _,
            out IntPtr prop);

        if (nitems == IntPtr.Zero || prop == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr window = Marshal.ReadIntPtr(prop);
        XFree(prop);

        return window;
    }
}
