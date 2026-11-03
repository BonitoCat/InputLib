using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace InputLib.PlatformListener;

/// <summary>
/// Input listener for Windows. If unsure about the OS, use <see cref="InputListener.New()"/> instead.
/// </summary>
public class WindowsInputListener : InputListener
{
    #region Win P/Invoke

    [DllImport("User32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
    
    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();
    
    [DllImport("user32.dll")]
    private static extern int ToUnicode(
        uint wVirtKey,
        uint wScanCode,
        byte[] lpKeyState,
        [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff,
        int cchBuff,
        uint wFlags);

    [DllImport("user32.dll")]
    private static extern bool GetKeyboardState(byte[] lpKeyState);

    #endregion

    public WindowsInputListener()
    {
        _handle = Process.GetCurrentProcess().MainWindowHandle;
    }
    
    public WindowsInputListener(IntPtr windowHandle)
    {
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
                
                bool isFocused = GetForegroundWindow() == _handle;

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
                
                for (int i = 0; i < 256; i++)
                {
                    if (PauseListening)
                    {
                        continue;
                    }
                    
                    short state = GetAsyncKeyState(i);
                    bool pressed = (state & 0x8000) != 0;

                    if (pressed)
                    {
                        if (!lastState[i])
                        {
                            HandleKeyDown((uint) i, false);
                            _delayTimer.Restart();
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
            
            _listening = false;
        }, _listeningCtx.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        return _listeningTask;
    }

    private uint? GetVirtualKeyFromKey(Key key)
    {
        foreach (int code in Enumerable.Range(0, 256))
        {
            if (MapKey((uint) code) == key)
            {
                return (uint)code;
            }
        }
        
        return null;
    }

    protected override Key? MapKey(uint keycode)
    {
        switch (keycode)
        {
            case 0x1B: return Key.Escape;
            case 0xA0: return Key.LeftShift;
            case 0xA1: return Key.RightShift;
            case 0x14: return Key.CapsLock;
            case 0xA2: return Key.LeftCtrl;
            case 0xA3: return Key.RightCtrl;
            case 0xA4: return Key.Alt;
            case 0xA5: return Key.AltGr;
            case 0x09: return Key.Tab;
            case 0x5B: return Key.Win;
            //case 135: return Key.Menu;
            case 0x0D: return Key.Enter;
            case 0x20: return Key.Space;
            case 0x08: return Key.Backspace;
            case 0x2A:
            case 0x2C: return Key.Print;
            case 0x2D: return Key.Insert;
            case 0x2E: return Key.Delete;
            
            case 0x70: return Key.F1;
            case 0x71: return Key.F2;
            case 0x72: return Key.F3;
            case 0x73: return Key.F4;
            case 0x74: return Key.F5;
            case 0x75: return Key.F6;
            case 0x76: return Key.F7;
            case 0x77: return Key.F8;
            case 0x78: return Key.F9;
            case 0x79: return Key.F10;
            case 0x7A: return Key.F11;
            case 0x7B: return Key.F12;
            case 0x7C: return Key.F13;
            case 0x7D: return Key.F14;
            case 0x7E: return Key.F15;
            case 0x7F: return Key.F16;
            case 0x80: return Key.F17;
            case 0x81: return Key.F18;
            case 0x82: return Key.F19;
            case 0x83: return Key.F20;
            case 0x84: return Key.F21;
            case 0x85: return Key.F22;
            case 0x86: return Key.F23;
            case 0x87: return Key.F24;

            case 0x31: return Key.D1;
            case 0x32: return Key.D2;
            case 0x33: return Key.D3;
            case 0x34: return Key.D4;
            case 0x35: return Key.D5;
            case 0x36: return Key.D6;
            case 0x37: return Key.D7;
            case 0x38: return Key.D8;
            case 0x39: return Key.D9;
            case 0x30: return Key.D0;
            
            case 0x90: return Key.NumLock;
            case 0x61: return Key.N1;
            case 0x62: return Key.N2;
            case 0x63: return Key.N3;
            case 0x64: return Key.N4;
            case 0x65: return Key.N5;
            case 0x66: return Key.N6;
            case 0x67: return Key.N7;
            case 0x68: return Key.N8;
            case 0x69: return Key.N9;
            case 0x60: return Key.N0;

            //case 49: return Key.Backtick;
            case 0xBD: return Key.Minus;
            case 0xBB: return Key.Equal;
            case 0x24: return Key.Home;
            case 0x23: return Key.End;
            case 0x21: return Key.PageUp;
            case 0x22: return Key.PageDown;

            case 0x41: return Key.A;
            case 0x42: return Key.B;
            case 0x43: return Key.C;
            case 0x44: return Key.D;
            case 0x45: return Key.E;
            case 0x46: return Key.F;
            case 0x47: return Key.G;
            case 0x48: return Key.H;
            case 0x49: return Key.I;
            case 0x4A: return Key.J;
            case 0x4B: return Key.K;
            case 0x4C: return Key.L;
            case 0x4D: return Key.M;
            case 0x4E: return Key.N;
            case 0x4F: return Key.O;
            case 0x50: return Key.P;
            case 0x51: return Key.Q;
            case 0x52: return Key.R;
            case 0x53: return Key.S;
            case 0x54: return Key.T;
            case 0x55: return Key.U;
            case 0x56: return Key.V;
            case 0x57: return Key.W;
            case 0x58: return Key.X;
            case 0x5A: return Key.Y;
            case 0x59: return Key.Z;

            case 0xBC: return Key.Comma;
            case 0xBE: return Key.Period;
            case 0xBF: return Key.Slash;
            case 0xDC: return Key.BackSlash;
            case 0xBA: return Key.SemiColon;
            case 0xDE: return Key.Apostrophe;
            case 0xDB: return Key.SquareBracketLeft;
            case 0xDD: return Key.SquareBracketRight;

            case 0x26: return Key.ArrowUp;
            case 0x28: return Key.ArrowDown;
            case 0x25: return Key.ArrowLeft;
            case 0x27: return Key.ArrowRight;

            default: return null;
        }
    }
}