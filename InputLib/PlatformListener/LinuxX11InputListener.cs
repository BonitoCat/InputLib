using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using InputLib.EventArgs;

namespace InputLib.PlatformListener;

/// <summary>
/// Input listener for Linux using X11. If unsure about the OS, use <see cref="InputListener.New()"/> instead.
/// </summary>
public class LinuxX11InputListener : InputListener
{
    private IntPtr _display;
    private bool _windowHasFocus;
    private bool _lastActive;
    private int _windowWidth;
    private int _windowHeight;
    private int _xiOpcode = -1;
    private bool _xiInitialized;
    private int _lastMouseWinX = -1;
    private int _lastMouseWinY = -1;
    private uint _lastMouseMask;
    private readonly HashSet<uint> _heldMouseButtons = new();

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
    private static extern int XSelectInput(IntPtr display, IntPtr window, IntPtr event_mask);

    [DllImport("X11")]
    private static extern int XPending(IntPtr display);

    [DllImport("X11")]
    private static extern int XNextEvent(IntPtr display, out XEvent event_return);

    [DllImport("X11")]
    private static extern int XQueryTree(
        IntPtr display,
        IntPtr window,
        out IntPtr root_return,
        out IntPtr parent_return,
        out IntPtr children_return,
        out uint nchildren_return);

    [DllImport("X11")]
    private static extern bool XQueryPointer(
        IntPtr display,
        IntPtr window,
        out IntPtr rootReturn,
        out IntPtr childReturn,
        out int rootX,
        out int rootY,
        out int winX,
        out int winY,
        out uint maskReturn);

    [DllImport("X11")]
    private static extern int XGetGeometry(
        IntPtr display,
        IntPtr drawable,
        out IntPtr rootReturn,
        out int xReturn,
        out int yReturn,
        out uint widthReturn,
        out uint heightReturn,
        out uint borderWidthReturn,
        out uint depthReturn);

    [DllImport("X11")]
    private static extern bool XGetEventData(IntPtr display, ref XGenericEventCookie cookie);

    [DllImport("X11")]
    private static extern void XFreeEventData(IntPtr display, ref XGenericEventCookie cookie);

    [DllImport("X11")]
    private static extern bool XQueryExtension(
        IntPtr display,
        string name,
        out int majorOpcode,
        out int firstEvent,
        out int firstError);

    [DllImport("libXi.so.6", EntryPoint = "XIQueryVersion")]
    private static extern int XIQueryVersion(IntPtr display, ref int major, ref int minor);

    [DllImport("libXi.so.6", EntryPoint = "XISelectEvents")]
    private static extern int XISelectEvents(
        IntPtr display,
        IntPtr window,
        [In] ref XIEventMask masks,
        int numMasks);

    [DllImport("X11")]
    private static extern IntPtr XSetErrorHandler(XErrorHandler handler);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int XErrorHandler(IntPtr display, ref XErrorEvent errorEvent);

    private const long StructureNotifyMask = 1L << 17;
    private const long FocusChangeMask = 1L << 21;
    private const int FocusIn = 9;
    private const int FocusOut = 10;
    private const int ConfigureNotify = 22;
    private const int GenericEvent = 35;
    private const int XI_RawButtonPress = 15;
    private const int XI_RawButtonRelease = 16;
    private const int NotifyInferior = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct XIEventMask
    {
        public int deviceid;
        public int mask_len;
        public IntPtr mask;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XGenericEventCookie
    {
        public int type;
        public IntPtr serial;
        public int send_event;
        public IntPtr display;
        public int extension;
        public int evtype;
        public uint cookie;
        public IntPtr data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XConfigureEvent
    {
        public int type;
        public IntPtr serial;
        public int send_event;
        public IntPtr display;
        public IntPtr @event;
        public IntPtr window;
        public int x;
        public int y;
        public int width;
        public int height;
        public int border_width;
        public IntPtr above;
        public int override_redirect;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XFocusChangeEvent
    {
        public int type;
        public IntPtr serial;
        public int send_event;
        public IntPtr display;
        public IntPtr window;
        public int mode;
        public int detail;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XErrorEvent
    {
        public int type;
        public IntPtr display;
        public IntPtr resourceid;
        public IntPtr serial;
        public byte error_code;
        public byte request_code;
        public byte minor_code;
    }

    [StructLayout(LayoutKind.Explicit, Size = 192)]
    private struct XEvent
    {
        [FieldOffset(0)]
        public int type;

        [FieldOffset(0)]
        public XGenericEventCookie cookie;

        [FieldOffset(0)]
        public XConfigureEvent configure;

        [FieldOffset(0)]
        public XFocusChangeEvent focus;
    }

    private static int OnXError(IntPtr display, ref XErrorEvent errorEvent)
    {
        return 0;
    }

    private static readonly XErrorHandler ErrorHandler = OnXError;

    public LinuxX11InputListener()
    {
        _display = XOpenDisplay(IntPtr.Zero);
        if (_display == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to open X11 display.");
        }

        XSetErrorHandler(ErrorHandler);
        InitXInput2();
        _handle = FindTerminalWindow();

        if (_handle != IntPtr.Zero)
        {
            SetupWindow();
        }
    }

    public LinuxX11InputListener(IntPtr windowHandle)
    {
        _display = XOpenDisplay(IntPtr.Zero);
        if (_display == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to open X11 display.");
        }

        XSetErrorHandler(ErrorHandler);
        InitXInput2();
        _handle = windowHandle;

        if (_handle != IntPtr.Zero)
        {
            SetupWindow();
        }
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

                bool isFocused = CheckWindowFocused();
                UpdateMousePointer(isFocused);

                if (!GlobalInput && !isFocused)
                {
                    if (_wasFocused)
                    {
                        for (int i = 0; i < 256; i++)
                        {
                            if (lastState[i])
                            {
                                HandleKeyUp((uint) i);
                            }
                        }

                        foreach (uint btn in _heldMouseButtons)
                        {
                            MouseButton mb = MouseButton.FromId(btn);
                            OnMouseButtonUp?.Invoke(btn);
                            OnMouseButton?.Invoke(CreateMouseButtonArgs(mb, MouseX, MouseY, MouseScreenX, MouseScreenY, false));
                        }

                        _heldMouseButtons.Clear();
                        ClearKeyState();
                        _pressTimers.Clear();
                        Array.Clear(lastState, 0, lastState.Length);

                        _wasFocused = false;
                        _lastMouseMask = 0;
                        _lastMouseWinX = -1;
                        _lastMouseWinY = -1;
                        IsMouseInsideWindow = false;
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
                    Thread.Sleep(50);
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

    private void SetupWindow()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        XSelectInput(_display, _handle, (IntPtr) (FocusChangeMask | StructureNotifyMask));
        UpdateWindowDimensions();
        _windowHasFocus = GetActiveTopLevelWindow() == _handle;
        _lastActive = _windowHasFocus;
    }

    private void UpdateWindowDimensions()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        if (XGetGeometry(_display, _handle, out _, out _, out _, out uint width, out uint height, out _, out _) != 0)
        {
            _windowWidth = (int) width;
            _windowHeight = (int) height;
        }
    }

    private void InitXInput2()
    {
        try
        {
            if (!XQueryExtension(_display, "XInputExtension", out int majorOpcode, out _, out _))
            {
                return;
            }

            int major = 2;
            int minor = 0;
            if (XIQueryVersion(_display, ref major, ref minor) != 0)
            {
                return;
            }

            IntPtr root = XDefaultRootWindow(_display);
            byte[] maskBytes = new byte[4];
            maskBytes[XI_RawButtonPress / 8] |= (byte) (1 << (XI_RawButtonPress % 8));
            maskBytes[XI_RawButtonRelease / 8] |= (byte) (1 << (XI_RawButtonRelease % 8));

            GCHandle handle = GCHandle.Alloc(maskBytes, GCHandleType.Pinned);
            try
            {
                XIEventMask eventMask = new()
                {
                    deviceid = 0,
                    mask_len = maskBytes.Length,
                    mask = handle.AddrOfPinnedObject()
                };

                if (XISelectEvents(_display, root, ref eventMask, 1) == 0)
                {
                    _xiOpcode = majorOpcode;
                    _xiInitialized = true;
                }
            }
            finally
            {
                handle.Free();
            }
        }
        catch
        {
            _xiInitialized = false;
        }
    }

    private void UpdateMousePointer(bool isFocused)
    {
        if (_handle == IntPtr.Zero || !isFocused)
        {
            IsMouseInsideWindow = false;
            return;
        }

        if (!XQueryPointer(_display, _handle, out _, out _, out int rootX, out int rootY, out int winX, out int winY, out uint mask))
        {
            return;
        }

        MouseX = winX;
        MouseY = winY;
        MouseScreenX = rootX;
        MouseScreenY = rootY;

        if (_windowWidth <= 0 || _windowHeight <= 0)
        {
            UpdateWindowDimensions();
        }

        bool inside = winX >= 0 && winY >= 0 && winX < _windowWidth && winY < _windowHeight;
        IsMouseInsideWindow = inside;

        if (inside && (winX != _lastMouseWinX || winY != _lastMouseWinY))
        {
            _lastMouseWinX = winX;
            _lastMouseWinY = winY;
            OnMouseMove?.Invoke(winX, winY);
        }

        if (!_xiInitialized && inside)
        {
            CheckFallbackButton(mask, 1 << 8, 1, winX, winY, rootX, rootY);
            CheckFallbackButton(mask, 1 << 9, 2, winX, winY, rootX, rootY);
            CheckFallbackButton(mask, 1 << 10, 3, winX, winY, rootX, rootY);
            _lastMouseMask = mask;
        }
    }

    private void CheckFallbackButton(uint currentMask, uint bitMask, uint button, int winX, int winY, int rootX, int rootY)
    {
        bool wasDown = (_lastMouseMask & bitMask) != 0;
        bool isDown = (currentMask & bitMask) != 0;

        if (!wasDown && isDown)
        {
            if (_heldMouseButtons.Add(button))
            {
                MouseButton mouseButton = MouseButton.FromId(button);
                OnMouseButtonDown?.Invoke(button);
                OnMouseButton?.Invoke(CreateMouseButtonArgs(mouseButton, winX, winY, rootX, rootY, true));
            }
        }
        else if (wasDown && !isDown)
        {
            if (_heldMouseButtons.Remove(button))
            {
                MouseButton mouseButton = MouseButton.FromId(button);
                OnMouseButtonUp?.Invoke(button);
                OnMouseButton?.Invoke(CreateMouseButtonArgs(mouseButton, winX, winY, rootX, rootY, false));
            }
        }
    }

    private void ProcessXInputEvent(ref XGenericEventCookie cookie)
    {
        if (!XGetEventData(_display, ref cookie))
        {
            return;
        }

        try
        {
            int evtype = cookie.evtype;
            if (evtype != XI_RawButtonPress && evtype != XI_RawButtonRelease)
            {
                return;
            }

            if (_handle == IntPtr.Zero || !_windowHasFocus)
            {
                return;
            }

            int detail = Marshal.ReadInt32(cookie.data, 56);
            if (!XQueryPointer(_display, _handle, out _, out _, out int rootX, out int rootY, out int winX, out int winY, out _))
            {
                return;
            }

            MouseX = winX;
            MouseY = winY;
            MouseScreenX = rootX;
            MouseScreenY = rootY;

            if (_windowWidth <= 0 || _windowHeight <= 0)
            {
                UpdateWindowDimensions();
            }

            bool inside = winX >= 0 && winY >= 0 && winX < _windowWidth && winY < _windowHeight;
            IsMouseInsideWindow = inside;

            uint button = (uint) detail;
            bool isHeldByWindow = _heldMouseButtons.Contains(button);

            if (!inside && !GlobalInput && !isHeldByWindow)
            {
                return;
            }

            if (evtype == XI_RawButtonPress)
            {
                if (detail == 4)
                {
                    OnMouseScroll?.Invoke(1);
                    OnMouseWheel?.Invoke(0, 1);
                    return;
                }

                if (detail == 5)
                {
                    OnMouseScroll?.Invoke(-1);
                    OnMouseWheel?.Invoke(0, -1);
                    return;
                }

                if (detail == 6)
                {
                    OnMouseWheel?.Invoke(-1, 0);
                    return;
                }

                if (detail == 7)
                {
                    OnMouseWheel?.Invoke(1, 0);
                    return;
                }

                if (!_heldMouseButtons.Add(button))
                {
                    return;
                }

                MouseButton mouseButton = MouseButton.FromId(button);
                OnMouseButtonDown?.Invoke(button);
                OnMouseButton?.Invoke(CreateMouseButtonArgs(mouseButton, winX, winY, rootX, rootY, true));
            }
            else if (evtype == XI_RawButtonRelease)
            {
                if (detail == 4 || detail == 5 || detail == 6 || detail == 7)
                {
                    return;
                }

                if (!_heldMouseButtons.Remove(button))
                {
                    return;
                }

                MouseButton mouseButton = MouseButton.FromId(button);
                OnMouseButtonUp?.Invoke(button);
                OnMouseButton?.Invoke(CreateMouseButtonArgs(mouseButton, winX, winY, rootX, rootY, false));
            }
        }
        finally
        {
            XFreeEventData(_display, ref cookie);
        }
    }

    private bool CheckWindowFocused()
    {
        if (_handle == IntPtr.Zero)
        {
            _handle = FindTerminalWindow();
            if (_handle == IntPtr.Zero)
            {
                return false;
            }

            SetupWindow();
        }

        IntPtr activeTopLevel = GetActiveTopLevelWindow();
        bool isActive = activeTopLevel == _handle;
        if (!isActive)
        {
            while (XPending(_display) > 0)
            {
                XNextEvent(_display, out XEvent _);
            }

            _lastActive = false;
            _windowHasFocus = false;
            return false;
        }

        if (!_lastActive)
        {
            _lastActive = true;
            _windowHasFocus = true;
        }

        while (XPending(_display) > 0)
        {
            XNextEvent(_display, out XEvent ev);
            if (ev.type == FocusIn)
            {
                _windowHasFocus = true;
            }
            else if (ev.type == FocusOut)
            {
                if (ev.focus.detail != NotifyInferior)
                {
                    _windowHasFocus = false;
                }
            }
            else if (ev.type == ConfigureNotify)
            {
                _windowWidth = ev.configure.width;
                _windowHeight = ev.configure.height;
            }
            else if (_xiInitialized && ev.type == GenericEvent && ev.cookie.extension == _xiOpcode)
            {
                ProcessXInputEvent(ref ev.cookie);
            }
        }

        return _windowHasFocus;
    }

    private IntPtr FindTerminalWindow()
    {
        string? windowIdStr = Environment.GetEnvironmentVariable("WINDOWID");
        if (long.TryParse(windowIdStr, out long windowId) && windowId > 0)
        {
            IntPtr parsedWindow = (IntPtr) windowId;
            if (IsValidWindow(parsedWindow))
            {
                return parsedWindow;
            }
        }

        IntPtr probedWindow = ProbeWindowViaTitle();
        if (probedWindow != IntPtr.Zero && IsValidWindow(probedWindow))
        {
            return probedWindow;
        }

        List<int> ancestorPids = GetAncestorPids(3);
        List<IntPtr> clientWindows = GetClientWindows();

        List<IntPtr> matchingWindows = new();
        for (int i = 0; i < clientWindows.Count; i++)
        {
            IntPtr window = clientWindows[i];
            int windowPid = GetWindowPid(window);
            if (windowPid > 0 && ancestorPids.Contains(windowPid))
            {
                matchingWindows.Add(window);
            }
        }

        if (matchingWindows.Count == 1)
        {
            return matchingWindows[0];
        }

        if (matchingWindows.Count > 1)
        {
            string processName = Process.GetCurrentProcess().ProcessName;
            for (int i = 0; i < matchingWindows.Count; i++)
            {
                IntPtr window = matchingWindows[i];
                string title = GetWindowTitle(window);
                if (!string.IsNullOrEmpty(title) && title.IndexOf(processName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return window;
                }
            }

            IntPtr active = GetActiveTopLevelWindow();
            if (matchingWindows.Contains(active))
            {
                return active;
            }

            return matchingWindows[0];
        }

        return GetActiveTopLevelWindow();
    }

    private IntPtr ProbeWindowViaTitle()
    {
        string originalTitle = string.Empty;
        try
        {
            originalTitle = Console.Title;
        }
        catch
        {
        }

        string probeToken = "FE_PROBE_" + Process.GetCurrentProcess().Id.ToString() + "_" + Environment.TickCount.ToString();
        try
        {
            Console.Write("\x1b]0;" + probeToken + "\x07");
            Console.Out.Flush();
        }
        catch
        {
            return IntPtr.Zero;
        }

        Thread.Sleep(20);

        List<IntPtr> clientWindows = GetClientWindows();
        IntPtr foundWindow = IntPtr.Zero;

        for (int i = 0; i < clientWindows.Count; i++)
        {
            IntPtr window = clientWindows[i];
            string title = GetWindowTitle(window);
            if (!string.IsNullOrEmpty(title) && title.IndexOf(probeToken, StringComparison.Ordinal) >= 0)
            {
                foundWindow = window;
                break;
            }
        }

        try
        {
            if (!string.IsNullOrEmpty(originalTitle))
            {
                Console.Title = originalTitle;
            }
            else
            {
                Console.Write("\x1b]0;" + Process.GetCurrentProcess().ProcessName + "\x07");
                Console.Out.Flush();
            }
        }
        catch
        {
        }

        return foundWindow;
    }

    private List<IntPtr> GetClientWindows()
    {
        List<IntPtr> windows = new();
        IntPtr root = XDefaultRootWindow(_display);
        IntPtr atom = XInternAtom(_display, "_NET_CLIENT_LIST", false);

        int result = XGetWindowProperty(
            _display,
            root,
            atom,
            IntPtr.Zero,
            (IntPtr) 1024,
            false,
            IntPtr.Zero,
            out _,
            out _,
            out IntPtr nitems,
            out _,
            out IntPtr prop);

        if (result != 0 || prop == IntPtr.Zero)
        {
            return windows;
        }

        int count = nitems.ToInt32();
        for (int i = 0; i < count; i++)
        {
            IntPtr win = Marshal.ReadIntPtr(prop, i * IntPtr.Size);
            if (win != IntPtr.Zero)
            {
                windows.Add(win);
            }
        }

        XFree(prop);
        return windows;
    }

    private int GetWindowPid(IntPtr window)
    {
        IntPtr atom = XInternAtom(_display, "_NET_WM_PID", false);

        int result = XGetWindowProperty(
            _display,
            window,
            atom,
            IntPtr.Zero,
            (IntPtr) 1,
            false,
            IntPtr.Zero,
            out _,
            out _,
            out IntPtr nitems,
            out _,
            out IntPtr prop);

        if (result != 0 || prop == IntPtr.Zero || nitems == IntPtr.Zero)
        {
            return 0;
        }

        int pid = Marshal.ReadInt32(prop);
        XFree(prop);
        return pid;
    }

    private string GetWindowTitle(IntPtr window)
    {
        IntPtr atom = XInternAtom(_display, "_NET_WM_NAME", false);
        int result = XGetWindowProperty(
            _display,
            window,
            atom,
            IntPtr.Zero,
            (IntPtr) 512,
            false,
            IntPtr.Zero,
            out _,
            out _,
            out IntPtr nitems,
            out _,
            out IntPtr prop);

        if (result == 0 && prop != IntPtr.Zero && nitems != IntPtr.Zero)
        {
            string title = PtrToStringUtf8(prop);
            XFree(prop);
            if (!string.IsNullOrEmpty(title))
            {
                return title;
            }
        }

        atom = XInternAtom(_display, "WM_NAME", false);
        result = XGetWindowProperty(
            _display,
            window,
            atom,
            IntPtr.Zero,
            (IntPtr) 512,
            false,
            IntPtr.Zero,
            out _,
            out _,
            out nitems,
            out _,
            out prop);

        if (result == 0 && prop != IntPtr.Zero && nitems != IntPtr.Zero)
        {
            string title = PtrToStringUtf8(prop);
            XFree(prop);
            return title;
        }

        return string.Empty;
    }

    private IntPtr GetActiveTopLevelWindow()
    {
        IntPtr root = XDefaultRootWindow(_display);
        IntPtr atom = XInternAtom(_display, "_NET_ACTIVE_WINDOW", false);

        int result = XGetWindowProperty(
            _display,
            root,
            atom,
            IntPtr.Zero,
            (IntPtr) 1,
            false,
            IntPtr.Zero,
            out _,
            out _,
            out IntPtr nitems,
            out _,
            out IntPtr prop);

        if (result != 0 || nitems == IntPtr.Zero || prop == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr window = Marshal.ReadIntPtr(prop);
        XFree(prop);

        return window;
    }

    private bool IsValidWindow(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return false;
        }

        int ret = XQueryTree(_display, window, out _, out _, out IntPtr children, out _);
        if (children != IntPtr.Zero)
        {
            XFree(children);
        }

        return ret != 0;
    }

    private static List<int> GetAncestorPids(int maxDepth)
    {
        List<int> pids = new();
        int currentPid = Process.GetCurrentProcess().Id;
        pids.Add(currentPid);

        int depth = 0;
        while (currentPid > 1 && depth < maxDepth)
        {
            int ppid = GetParentPid(currentPid);
            if (ppid <= 1 || pids.Contains(ppid))
            {
                break;
            }

            pids.Add(ppid);
            currentPid = ppid;
            depth++;
        }

        return pids;
    }

    private static int GetParentPid(int pid)
    {
        string statusPath = $"/proc/{pid}/status";
        if (!File.Exists(statusPath))
        {
            return 0;
        }

        try
        {
            string[] lines = File.ReadAllLines(statusPath);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.StartsWith("PPid:", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && int.TryParse(parts[1], out int ppid))
                    {
                        return ppid;
                    }
                }
            }
        }
        catch
        {
            return 0;
        }

        return 0;
    }

    private static string PtrToStringUtf8(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero)
        {
            return string.Empty;
        }

        int len = 0;
        while (Marshal.ReadByte(ptr, len) != 0)
        {
            len++;
        }

        if (len == 0)
        {
            return string.Empty;
        }

        byte[] buffer = new byte[len];
        Marshal.Copy(ptr, buffer, 0, len);
        return Encoding.UTF8.GetString(buffer);
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
}
