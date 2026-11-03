namespace InputLib.Tty;

internal sealed class TerminalMouseReader
{
    private const int PollTimeoutMs = 50;

    private readonly TerminalMouseParser _parser;
    private Thread? _thread;
    private volatile bool _running;

    public TerminalMouseReader(TerminalMouseParser parser)
    {
        _parser = parser;
    }

    public void Start()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        _thread = new(Run)
        {
            IsBackground = true,
            Name = "TerminalMouseReader",
        };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        if (_thread != null && _thread != Thread.CurrentThread)
        {
            _thread.Join(PollTimeoutMs * 4);
        }

        _thread = null;
    }

    private void Run()
    {
        byte[] buffer = new byte[256];
        while (_running)
        {
            if (!UnixTty.WaitForInput(PollTimeoutMs))
            {
                _parser.FlushPending();
                continue;
            }

            int count = UnixTty.read(0, buffer, buffer.Length);
            if (count <= 0)
            {
                Thread.Sleep(PollTimeoutMs);
                continue;
            }

            _parser.Feed(buffer, count);
        }
    }
}
