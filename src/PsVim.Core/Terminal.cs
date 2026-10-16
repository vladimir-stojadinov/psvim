using System.Runtime.InteropServices;
using System.Text;

namespace PsVim;

/// <summary>Apstrakcija terminala: ulaz tastera i izlaz teksta. Omogućava testove bez pravog konzola.</summary>
public interface ITerminal
{
    int Width { get; }
    int Height { get; }
    /// <summary>Blokirajuće čitanje jednog tastera u vim notaciji ("a", "&lt;CR&gt;", "&lt;C-r&gt;").</summary>
    string ReadKey();
    /// <summary>Čeka taster najviše ms milisekundi. Vraća true ako je taster dostupan.</summary>
    bool WaitForKey(int ms);
    void Write(string s);
    void Enter();
    void Leave();
}

public sealed class ConsoleTerminal : ITerminal
{
    private Stream _out;
    private bool _oldCtrlC;
    private Encoding _oldOutEnc;
    private uint _oldOutMode, _oldInMode;
    private bool _active;

    public int Width => Math.Max(20, SafeGet(() => Console.WindowWidth, 80));
    public int Height => Math.Max(6, SafeGet(() => Console.WindowHeight, 24));

    private static int SafeGet(Func<int> f, int def) { try { return f(); } catch { return def; } }

    public void Enter()
    {
        if (_active) return;
        _active = true;
        _out = Console.OpenStandardOutput();
        try { _oldOutEnc = Console.OutputEncoding; Console.OutputEncoding = new UTF8Encoding(false); } catch { }
        try { _oldCtrlC = Console.TreatControlCAsInput; Console.TreatControlCAsInput = true; } catch { }
        if (OperatingSystem.IsWindows()) EnableVt();
        // alternativni ekran, očisti, sakrij kursor dok crtamo
        Write("\x1b[?1049h\x1b[H\x1b[2J");
    }

    public void Leave()
    {
        if (!_active) return;
        _active = false;
        Write("\x1b[0m\x1b[0 q\x1b[?25h\x1b[?1049l");
        try { Console.TreatControlCAsInput = _oldCtrlC; } catch { }
        try { if (_oldOutEnc != null) Console.OutputEncoding = _oldOutEnc; } catch { }
        if (OperatingSystem.IsWindows()) RestoreVt();
    }

    public void Write(string s)
    {
        if (_out == null) _out = Console.OpenStandardOutput();
        var bytes = Encoding.UTF8.GetBytes(s);
        _out.Write(bytes, 0, bytes.Length);
        _out.Flush();
    }

    public string ReadKey()
    {
        while (true)
        {
            var k = Console.ReadKey(true);
            var s = Keys.FromConsoleKey(k);
            if (s != null) return s;
        }
    }

    public bool WaitForKey(int ms)
    {
        var until = Environment.TickCount64 + ms;
        while (true)
        {
            try { if (Console.KeyAvailable) return true; } catch { return false; }
            if (Environment.TickCount64 >= until) return false;
            Thread.Sleep(10);
        }
    }

    // ---- Windows: uključi VT obradu ----
    private const int STD_OUTPUT_HANDLE = -11, STD_INPUT_HANDLE = -10;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004, DISABLE_NEWLINE_AUTO_RETURN = 0x0008;

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GetStdHandle(int n);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetConsoleMode(IntPtr h, out uint mode);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetConsoleMode(IntPtr h, uint mode);

    private void EnableVt()
    {
        try
        {
            var h = GetStdHandle(STD_OUTPUT_HANDLE);
            if (GetConsoleMode(h, out _oldOutMode))
                SetConsoleMode(h, _oldOutMode | ENABLE_VIRTUAL_TERMINAL_PROCESSING | DISABLE_NEWLINE_AUTO_RETURN);
            var hi = GetStdHandle(STD_INPUT_HANDLE);
            GetConsoleMode(hi, out _oldInMode);
        }
        catch { }
    }

    private void RestoreVt()
    {
        try
        {
            SetConsoleMode(GetStdHandle(STD_OUTPUT_HANDLE), _oldOutMode);
            SetConsoleMode(GetStdHandle(STD_INPUT_HANDLE), _oldInMode);
        }
        catch { }
    }
}

/// <summary>Lažni terminal za testove.</summary>
public sealed class FakeTerminal : ITerminal
{
    private readonly Queue<string> _keys;
    public int Width { get; set; } = 100;
    public int Height { get; set; } = 30;
    public StringBuilder Output { get; } = new();
    public FakeTerminal(string keys) { _keys = new Queue<string>(Keys.Parse(keys)); }
    public void Feed(string keys) { foreach (var k in Keys.Parse(keys)) _keys.Enqueue(k); }
    public string ReadKey()
    {
        if (_keys.Count == 0) throw new EndOfInputException();
        return _keys.Dequeue();
    }
    public bool WaitForKey(int ms) => _keys.Count > 0;
    public void Write(string s) { if (Output.Length < 1_000_000) Output.Append(s); }
    public void Enter() { }
    public void Leave() { }
}

public sealed class EndOfInputException : Exception { }
