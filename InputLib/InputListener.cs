using System.Data.Common;
using System.Diagnostics;
using System.Runtime.InteropServices;
using InputLib.EventArgs;
using InputLib.PlatformListener;
using InputLib.Tty;

namespace InputLib;

public abstract class InputListener : IDisposable
{
    private static bool _initialized;
    
    public bool GlobalInput { get; set; }
    public bool RaiseEvents { get; set; } = true;
    
    private volatile bool _pauseListening;
    public bool PauseListening
    {
        get => _pauseListening;
        set => _pauseListening = value;
    }
    protected readonly ManualResetEventSlim _pauseEvent = new(false);
    public bool SendRepeatEvents { get; set; } = true;
    public int RepeatDelayMs
    {
        get => _repeatDelayMs;
        set
        {
            _repeatDelayMs = value;
            if (!_delayTimer.IsRunning && _delayTimer.ElapsedMilliseconds < _repeatDelayMs)
                _delayTimer.Start();
        }
    }
    public int RepeatIntervalMs { get; set; } = 50;

    private int _repeatDelayMs = 250;

    private volatile bool _consumeNextKeyDown;
    private List<Key> _consumeDownKeys = new();
    private volatile bool _consumeNextKeyUp;
    private List<Key> _consumeUpKeys = new();

    protected Stopwatch _delayTimer = new();
    protected Stopwatch _repeatTimer = new();
    protected IntPtr _handle;

    protected CancellationTokenSource? _listeningCtx;
    protected bool _listening;
    protected Task? _listeningTask;
    private readonly AutoResetEvent _inputEvent = new(false);

    public static bool IsEchoEnabled { get; private set; } = true;

    protected HashSet<Key> _heldKeys = new();
    protected HashSet<Button> _heldButtons = new();

    public delegate void KeyDownHandler(object? sender, KeyDownEventArgs args);
    public delegate void KeyUpHandler(object? sender, KeyUpEventArgs args);
    public delegate void KeyJustPressedHandler(object? sender, KeyJustPressedEventArgs args);

    public KeyDownHandler OnKeyDown = (_, _) => { };
    public KeyUpHandler OnKeyUp = (_, _) => { };
    public KeyJustPressedHandler OnKeyJustPressed = (_, _) => { };

    public Action<int, int>? OnMouseMove;
    public Action<uint>? OnMouseButtonDown;
    public Action<uint>? OnMouseButtonUp;
    public Action<int>? OnMouseScroll;
    public Action<int, int>? OnMouseWheel;
    public Action<MouseButtonEventArgs>? OnMouseButton;

    public int MouseX { get; protected set; }
    public int MouseY { get; protected set; }
    public int MouseScreenX { get; protected set; }
    public int MouseScreenY { get; protected set; }
    public bool IsMouseInsideWindow { get; protected set; }

    public Action<TerminalMouseEventArgs>? OnTerminalMouse;
    public Action<string>? OnTerminalInput;

    public int MouseColumn { get; protected set; } = -1;
    public int MouseRow { get; protected set; } = -1;
    public bool IsTerminalMouseEnabled => _terminalMouseReader != null;

    protected virtual bool SupportsTerminalMouse => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    private TerminalMouseReader? _terminalMouseReader;
    private bool _disabledEchoForMouse;

    protected bool _wasFocused;

    protected readonly Dictionary<Key, Stopwatch> _pressTimers = new();
    private const int TapThresholdMs = 150;

    static InputListener()
    {
        Init();
    }

    public static void Init()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            UnixTty.Init();
        }
        
        _initialized = true;
    }

    public static InputListener? New()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("Init has not yet been called.");
        }
        
        bool isSsh = Environment.GetEnvironmentVariable("SSH_CLIENT") != null
                     || Environment.GetEnvironmentVariable("SSH_TTY") != null;

        if (isSsh)
        {
            return new TtyInputListener();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsInputListener();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            bool hasDisplay = Environment.GetEnvironmentVariable("DISPLAY") != null
                           || Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") != null;

            return hasDisplay ? new LinuxX11InputListener() : new TtyInputListener();
        }

        return null;
    }

    public static InputListener? New(IntPtr windowHandle)
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("Init has not yet been called.");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsInputListener(windowHandle);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new LinuxX11InputListener(windowHandle);
        }

        return null;
    }

    ~InputListener()
    {
        try
        {
            PauseListening = true;
            StopListening().GetAwaiter().GetResult();
        }
        catch { }
    }

    public void Dispose()
    {
        DisableTerminalMouse();
        PauseListening = true;
        StopListening().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    public Task StartListening() => StartListening(CancellationToken.None);

    public abstract Task StartListening(CancellationToken token);

    private async Task StopListening()
    {
        CancellationTokenSource? ctx = _listeningCtx;
        Task? task = _listeningTask;

        if (ctx == null || task == null)
        {
            _listeningTask = null;
            _listeningCtx?.Dispose();
            _listeningCtx = null;
            return;
        }

        ctx.Cancel();

        if (Task.CurrentId == task.Id)
        {
            _listeningTask = null;
            ctx.Dispose();
            _listeningCtx = null;
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch { }
        finally
        {
            _listeningTask = null;
            ctx.Dispose();
            _listeningCtx = null;
        }
    }

    protected void HandleKeyDown(uint keycode, bool continuous)
    {
        Key? key = MapKey(keycode);
        if (key is null || !_listening)
        {
            return;
        }

        if (_heldKeys.Add(key))
        {
            Stopwatch watch = new();
            watch.Start();
            _pressTimers[key] = watch;
        }

        if (_consumeNextKeyDown)
        {
            _consumeNextKeyDown = false;
            if (_consumeDownKeys.Contains(key))
            {
                _consumeDownKeys.Clear();
                return;
            }
            
            _consumeDownKeys.Clear();
        }

        if (!RaiseEvents)
        {
            return;
        }

        OnKeyDown.Invoke(this, new(key, continuous));
    }

    protected void HandleKeyUp(uint keycode)
    {
        Key? key = MapKey(keycode);
        if (key is null || !_listening)
        {
            return;
        }

        if (_pressTimers.TryGetValue(key, out Stopwatch? watch))
        {
            watch.Stop();
            if (watch.ElapsedMilliseconds <= TapThresholdMs)
            {
                HandleKeyJustPressed(keycode);
            }

            _pressTimers.Remove(key);
        }

        _heldKeys.Remove(key);

        if (_consumeNextKeyUp)
        {
            _consumeNextKeyUp = false;
            if (_consumeUpKeys.Contains(key))
            {
                _consumeUpKeys.Clear();
                return;
            }
            
            _consumeUpKeys.Clear();
        }

        if (!RaiseEvents)
        {
            return;
        }

        OnKeyUp.Invoke(this, new(key));
    }

    public void HandleKeyJustPressed(uint keycode)
    {
        Key? key = MapKey(keycode);
        if (key is null || !_listening)
        {
            return;
        }

        if (_consumeNextKeyDown || _consumeNextKeyUp)
        {
            if (_consumeDownKeys.Contains(key) || _consumeUpKeys.Contains(key))
            {
                return;
            }
        }

        if (!RaiseEvents)
        {
            return;
        }

        OnKeyJustPressed.Invoke(this, new(key));
    }

    public bool EnableTerminalMouse()
    {
        if (_terminalMouseReader != null)
        {
            return true;
        }

        if (!SupportsTerminalMouse || !UnixTty.IsTerminal)
        {
            return false;
        }

        if (IsEchoEnabled)
        {
            DisableEcho();
            _disabledEchoForMouse = true;
        }

        TerminalMouseParser parser = new()
        {
            OnMouse = HandleTerminalMouse,
            OnText = text => OnTerminalInput?.Invoke(text),
        };

        UnixTty.EnableMouseTracking();
        _terminalMouseReader = new(parser);
        _terminalMouseReader.Start();
        return true;
    }

    public void DisableTerminalMouse()
    {
        TerminalMouseReader? reader = _terminalMouseReader;
        if (reader == null)
        {
            return;
        }

        _terminalMouseReader = null;
        UnixTty.DisableMouseTracking();
        reader.Stop();
        MouseColumn = -1;
        MouseRow = -1;

        if (_disabledEchoForMouse)
        {
            _disabledEchoForMouse = false;
            EnableEcho();
        }
    }

    private void HandleTerminalMouse(TerminalMouseEventArgs args)
    {
        MouseColumn = args.Column;
        MouseRow = args.Row;

        if (!RaiseEvents)
        {
            return;
        }

        OnTerminalMouse?.Invoke(args);
    }

    protected MouseButtonEventArgs CreateMouseButtonArgs(MouseButton button, int x, int y, int screenX, int screenY, bool isDown)
    {
        return new(button, x, y, screenX, screenY, isDown)
        {
            Column = MouseColumn,
            Row = MouseRow,
        };
    }

    public void ConsumeNextKeyDown() => _consumeNextKeyDown = true;
    public void ConsumeNextKeyDown(Key key) { _consumeNextKeyDown = true; _consumeDownKeys.Add(key); }
    public void ConsumeNextKeyDown(Key[] keys) { _consumeNextKeyDown = true; _consumeDownKeys.AddRange(keys); }

    public void ConsumeNextKeyUp() => _consumeNextKeyUp = true;
    public void ConsumeNextKeyUp(Key key) { _consumeNextKeyUp = true; _consumeUpKeys.Add(key); }
    public void ConsumeNextKeyUp(Key[] keys) { _consumeNextKeyUp = true; _consumeUpKeys.AddRange(keys); }

    protected abstract Key? MapKey(uint keycode);

    public static void DisableEcho()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("Init has not yet been called.");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            WinTty.DisableEcho();
            IsEchoEnabled = false;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            UnixTty.DisableEcho();
            IsEchoEnabled = false;
        }
    }

    public static void EnableEcho()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("Init has not yet been called.");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            WinTty.EnableEcho();
            IsEchoEnabled = true;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            UnixTty.EnableEcho();
            IsEchoEnabled = true;
        }
    }

    public static void Flush()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("Init has not yet been called.");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            UnixTty.Flush();
        }
    }

    public bool IsKeyDown(Key key) => _heldKeys.Contains(key);
    public bool IsKeyUp(Key key) => !_heldKeys.Contains(key);

    public void WaitForDispose() => WaitForDispose(CancellationToken.None);
    public void WaitForDispose(CancellationToken token)
    {
        while (_listeningCtx != null && !_listeningCtx.Token.IsCancellationRequested && !token.IsCancellationRequested)
        {
            Thread.Sleep(10);
        }
    }

    public void WaitForPause() => WaitForPause(CancellationToken.None);
    public void WaitForPause(CancellationToken token) => _pauseEvent.Wait(token);

    public void WaitForInput() => WaitForInput(CancellationToken.None);
    public void WaitForInput(CancellationToken token)
    {
        using (token.Register(() => _inputEvent.Set()))
        {
            _inputEvent.WaitOne();
        }
    }

    public void WaitForKeyInput() => WaitForKeyInput(CancellationToken.None);
    public void WaitForKeyInput(CancellationToken token) { }

    public void WaitForKeyInput(Key key) => WaitForKeyInput(key, CancellationToken.None);
    public void WaitForKeyInput(Key key, CancellationToken token)
    {
        while (!token.IsCancellationRequested && IsKeyDown(key))
        {
            Thread.Sleep(10);
        }
    }

    public HashSet<Key> GetHeldKeys() => new(_heldKeys);

    public void ClearKeyState() => _heldKeys.Clear();
}
