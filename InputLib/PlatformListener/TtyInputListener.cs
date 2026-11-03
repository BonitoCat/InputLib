using InputLib.EventArgs;

namespace InputLib.PlatformListener;

public class TtyInputListener : InputListener
{
    private readonly Dictionary<Key, DateTime> _heldKeysTimers = new();
    private readonly TimeSpan _keyTimeout = TimeSpan.FromMilliseconds(300);
    private List<Key> _removeKeys = new();

    private object _removeKeyLock = new();

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
            while (_listeningCtx != null && !_listeningCtx.Token.IsCancellationRequested)
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

                UpdateKeys();
                Thread.Sleep(1);
            }

            _listening = false;
        }, _listeningCtx.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        return _listeningTask;
    }

    private void UpdateKeys()
    {
        while (Console.KeyAvailable)
        {
            ConsoleKeyInfo cKey = Console.ReadKey(!IsEchoEnabled);
            Key key = MapConsoleKey(cKey.Key);

            if (key != Key.LeftCtrl &&
                key != Key.LeftShift &&
                key != Key.Alt)
            {
                lock (_removeKeyLock)
                {
                    _removeKeys = _heldKeys
                                  .Where(k => k != Key.LeftCtrl &&
                                              k != Key.LeftShift &&
                                              k != Key.Alt)
                                  .ToList();

                    foreach (Key k in _removeKeys)
                    {
                        _heldKeys.Remove(k);
                        _heldKeysTimers.Remove(k);

                        if (RaiseEvents)
                        {
                            OnKeyUp?.Invoke(this, new(k));
                        }
                    }
                }
            }
            
            if (key != Key.None)
            {
                _heldKeys.Add(key);
                _heldKeysTimers[key] = DateTime.UtcNow;

                if (RaiseEvents)
                {
                    OnKeyDown?.Invoke(this, new KeyDownEventArgs(key, _heldKeys.Contains(key)));
                }
            }

            if ((cKey.Modifiers & ConsoleModifiers.Control) != 0)
            {
                AddModifierKey(Key.LeftCtrl);
            }
            if ((cKey.Modifiers & ConsoleModifiers.Shift) != 0)
            {
                AddModifierKey(Key.LeftShift);
            }
            if ((cKey.Modifiers & ConsoleModifiers.Alt) != 0)
            {
                AddModifierKey(Key.Alt);
            }
        }

        lock (_removeKeyLock)
        {
            foreach (KeyValuePair<Key, DateTime> kvp in _heldKeysTimers)
            {
                if (DateTime.UtcNow - kvp.Value > _keyTimeout)
                {
                    _removeKeys.Add(kvp.Key);
                }
            }

            List<Key> removedKeys = new();
            foreach (Key key in _removeKeys)
            {
                removedKeys.Add(key);
                _heldKeys.Remove(key);
                _heldKeysTimers.Remove(key);

                if (RaiseEvents)
                {
                    OnKeyUp?.Invoke(this, new(key));
                }
            }
            
            _removeKeys.RemoveAll(key => removedKeys.Contains(key));
        }
    }

    private void AddModifierKey(Key modKey)
    {
        if (_heldKeys.Add(modKey))
        {
            _heldKeysTimers[modKey] = DateTime.UtcNow;
            if (RaiseEvents)
            {
                OnKeyDown?.Invoke(this, new KeyDownEventArgs(modKey, _heldKeys.Contains(modKey)));
            }
        }
    }

    private Key MapConsoleKey(ConsoleKey ck) => ck switch
    {
        ConsoleKey.UpArrow => Key.ArrowUp,
        ConsoleKey.DownArrow => Key.ArrowDown,
        ConsoleKey.LeftArrow => Key.ArrowLeft,
        ConsoleKey.RightArrow => Key.ArrowRight,

        ConsoleKey.A => Key.A,
        ConsoleKey.B => Key.B,
        ConsoleKey.C => Key.C,
        ConsoleKey.D => Key.D,
        ConsoleKey.E => Key.E,
        ConsoleKey.F => Key.F,
        ConsoleKey.G => Key.G,
        ConsoleKey.H => Key.H,
        ConsoleKey.I => Key.I,
        ConsoleKey.J => Key.J,
        ConsoleKey.K => Key.K,
        ConsoleKey.L => Key.L,
        ConsoleKey.M => Key.M,
        ConsoleKey.N => Key.N,
        ConsoleKey.O => Key.O,
        ConsoleKey.P => Key.P,
        ConsoleKey.Q => Key.Q,
        ConsoleKey.R => Key.R,
        ConsoleKey.S => Key.S,
        ConsoleKey.T => Key.T,
        ConsoleKey.U => Key.U,
        ConsoleKey.V => Key.V,
        ConsoleKey.W => Key.W,
        ConsoleKey.X => Key.X,
        ConsoleKey.Y => Key.Y,
        ConsoleKey.Z => Key.Z,

        ConsoleKey.D0 => Key.D0,
        ConsoleKey.D1 => Key.D1,
        ConsoleKey.D2 => Key.D2,
        ConsoleKey.D3 => Key.D3,
        ConsoleKey.D4 => Key.D4,
        ConsoleKey.D5 => Key.D5,
        ConsoleKey.D6 => Key.D6,
        ConsoleKey.D7 => Key.D7,
        ConsoleKey.D8 => Key.D8,
        ConsoleKey.D9 => Key.D9,

        ConsoleKey.NumPad0 => Key.N0,
        ConsoleKey.NumPad1 => Key.N1,
        ConsoleKey.NumPad2 => Key.N2,
        ConsoleKey.NumPad3 => Key.N3,
        ConsoleKey.NumPad4 => Key.N4,
        ConsoleKey.NumPad5 => Key.N5,
        ConsoleKey.NumPad6 => Key.N6,
        ConsoleKey.NumPad7 => Key.N7,
        ConsoleKey.NumPad8 => Key.N8,
        ConsoleKey.NumPad9 => Key.N9,
        ConsoleKey.Decimal => Key.Period,
        ConsoleKey.Add => Key.Plus,
        ConsoleKey.Subtract => Key.Minus,
        ConsoleKey.Multiply => Key.None,
        ConsoleKey.Divide => Key.Slash,

        ConsoleKey.F1 => Key.F1,
        ConsoleKey.F2 => Key.F2,
        ConsoleKey.F3 => Key.F3,
        ConsoleKey.F4 => Key.F4,
        ConsoleKey.F5 => Key.F5,
        ConsoleKey.F6 => Key.F6,
        ConsoleKey.F7 => Key.F7,
        ConsoleKey.F8 => Key.F8,
        ConsoleKey.F9 => Key.F9,
        ConsoleKey.F10 => Key.F10,
        ConsoleKey.F11 => Key.F11,
        ConsoleKey.F12 => Key.F12,
        ConsoleKey.F13 => Key.F13,
        ConsoleKey.F14 => Key.F14,
        ConsoleKey.F15 => Key.F15,
        ConsoleKey.F16 => Key.F16,
        ConsoleKey.F17 => Key.F17,
        ConsoleKey.F18 => Key.F18,
        ConsoleKey.F19 => Key.F19,
        ConsoleKey.F20 => Key.F20,
        ConsoleKey.F21 => Key.F21,
        ConsoleKey.F22 => Key.F22,
        ConsoleKey.F23 => Key.F23,
        ConsoleKey.F24 => Key.F24,

        /*ConsoleKey.LeftShift => Key.LeftShift,
        ConsoleKey.RightShift => Key.RightShift,
        ConsoleKey.LeftCtrl => Key.LeftCtrl,
        ConsoleKey.RightCtrl => Key.RightCtrl,*/
        ConsoleKey.Escape => Key.Escape,
        ConsoleKey.Tab => Key.Tab,
        ConsoleKey.Spacebar => Key.Space,
        ConsoleKey.Enter => Key.Enter,
        ConsoleKey.Backspace => Key.Backspace,
        ConsoleKey.Home => Key.Home,
        ConsoleKey.End => Key.End,
        ConsoleKey.PageUp => Key.PageUp,
        ConsoleKey.PageDown => Key.PageDown,
        ConsoleKey.Insert => Key.Insert,
        ConsoleKey.Delete => Key.Delete,
        ConsoleKey.PrintScreen => Key.Print,
        
        ConsoleKey.Oem1 => Key.SemiColon,
        ConsoleKey.Oem2 => Key.Slash,
        ConsoleKey.Oem3 => Key.Backtick,
        ConsoleKey.Oem4 => Key.SquareBracketLeft,
        ConsoleKey.Oem5 => Key.BackSlash,
        ConsoleKey.Oem6 => Key.SquareBracketRight,
        ConsoleKey.Oem7 => Key.Apostrophe,
        ConsoleKey.OemPlus => Key.Equal,
        ConsoleKey.OemMinus => Key.Minus,
        
        _ => Key.None,
    };

    protected override bool SupportsTerminalMouse => false;

    protected override Key? MapKey(uint keycode) => Key.None;
}
