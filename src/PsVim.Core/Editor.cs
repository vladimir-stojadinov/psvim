using System.Text;

namespace PsVim;

public enum Mode { Normal, Insert, Replace, Visual, VisualLine, Command, Tree }

public sealed class VimError : Exception { public VimError(string m) : base(m) { } }

/// <summary>Kuke koje PowerShell sloj može da postavi (clipboard, shell, dijagnostika, pomoć, formatiranje, dopuna).</summary>
public sealed class Hooks
{
    public Func<string> ClipboardGet;
    public Action<string> ClipboardSet;
    /// <summary>(komanda, ulaz ili null) =&gt; izlaz kao tekst. Izvršava PowerShell kod.</summary>
    public Func<string, string, string> Shell;
    /// <summary>(tekst, putanja) =&gt; linije "linija\tkolona\tozbiljnost\tporuka" (1-bazirano).</summary>
    public Func<string, string, string[]> Diagnose;
    /// <summary>Sporija analiza (PSScriptAnalyzer) — pokreće se pri snimanju.</summary>
    public Func<string, string, string[]> Lint;
    /// <summary>reč =&gt; tekst pomoći (Get-Help).</summary>
    public Func<string, string> Help;
    /// <summary>(tekst, putanja) =&gt; formatiran tekst ili null.</summary>
    public Func<string, string, string> Format;
    /// <summary>(tekst, pozicija kursora) =&gt; "početak\tdužina\ttekst\tprikaz\ttip" (TabExpansion2).</summary>
    public Func<string, int, string[]> Complete;
}

public sealed class Register
{
    public string Text = ""; public bool Linewise;
    public Register() { }
    public Register(string t, bool l) { Text = t; Linewise = l; }
}

public sealed class Notification
{
    public string Text; public bool Error; public long Until;
}

public sealed partial class Editor
{
    public const string Version = "0.2.0";

    public readonly Ui Ui;
    public Options Opt => Ui.Options;
    public Theme Th => Ui.Theme;
    public readonly Hooks Hooks;
    public readonly List<Buffer> Buffers = new();
    public Buffer Buf;
    private Buffer _alt;
    public string Cwd;
    public Mode Mode = Mode.Normal;
    public bool Quit { get; private set; }
    public int ExitCode;

    private readonly Dictionary<Buffer, HighlightCache> _hl = new();
    private readonly Dictionary<char, Register> _regs = new();
    private readonly List<Notification> _notes = new();
    public string LastMessage { get; private set; }
    private string _showCmd = "";
    private string _cmdlineOverlay; // tekst komandne linije za crtanje
    private int _cmdlineCursor;
    private string _cmdlinePrompt;

    // pretraga
    private string _lastSearch;
    private bool _lastSearchFwd = true;
    private bool _hlActive;
    private string _incSearch;
    // f/t
    private string _lastFind; private char _lastFindChar;
    // dot repeat, makroi
    private List<string> _lastChange;
    private char? _macroReg; private List<string> _macroKeys;
    private char _lastMacro = '\0';
    // vizuelni
    private Pos _vAnchor;
    private (Pos A, Pos B, Mode M)? _lastVisual;
    private Pos _lastInsert;
    // istorije
    private readonly List<string> _cmdHistory = new();
    private readonly List<string> _searchHistory = new();
    private readonly List<(Buffer B, Pos P)> _jumps = new();
    private int _jumpIdx = -1;
    // stablo fajlova
    private FileTree _tree;
    private bool _treeOpen, _treeFocus;
    // flash
    private List<(Pos P, int Len, char Label)> _flash;
    // keymape
    private readonly KeyNode _leader = new() { Desc = "leader" };
    private readonly Dictionary<string, (List<string> Rhs, string Desc)> _userMaps = new();
    private FileIndex _index;

    public Editor(Ui ui, Hooks hooks = null, string cwd = null)
    {
        Ui = ui;
        Hooks = hooks ?? new Hooks();
        Cwd = cwd ?? Directory.GetCurrentDirectory();
        BuildLeaderMap();
        LoadUserKeymaps();
    }

    // =================================================================
    //  Ulazna tačka
    // =================================================================
    public static int Start(string[] files, string cwd = null, Hooks hooks = null, string configPath = null)
    {
        var opts = Options.Load(configPath);
        var term = new ConsoleTerminal();
        var ui = new Ui(term, opts);
        var ed = new Editor(ui, hooks, cwd);
        int pendingLine = -1;
        foreach (var f in files ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(f)) continue;
            // vim stil: psvim +12 fajl.ps1
            if (f.StartsWith("+") && int.TryParse(f.Substring(1), out var ln)) { pendingLine = Math.Max(0, ln - 1); continue; }
            var p = Path.IsPathRooted(f) ? f : Path.Combine(ed.Cwd, f);
            if (Directory.Exists(p)) { ed.Cwd = Path.GetFullPath(p); ed.OpenTree(true); continue; }
            ed.Open(p, restorePos: pendingLine < 0, line: pendingLine);
            pendingLine = -1;
        }
        if (ed.Buffers.Count == 0) ed.ShowDashboard();
        if (opts.LoadedFrom?.StartsWith("GREŠKA") == true) ed.Notify("Konfiguracija: " + opts.LoadedFrom, true);
        ed.Run();
        return ed.ExitCode;
    }

    public void Run()
    {
        Ui.Term.Enter();
        Ui.Screen.Invalidate();
        Ui.Input.BeforeBlock = Render;
        try
        {
            while (!Quit)
            {
                try
                {
                    if (_treeFocus && _treeOpen) TreeCommand();
                    else NormalCommand();
                    AfterCommand();
                }
                catch (EndOfInputException) { throw; }
                catch (VimError e) { Mode = Mode.Normal; Notify(e.Message, true); }
                catch (Exception e) { Mode = Mode.Normal; Notify("Greška: " + e.Message, true); }
            }
        }
        catch (EndOfInputException) { /* testovi */ }
        finally
        {
            SavePositions();
            Ui.Term.Leave();
        }
    }

    /// <summary>Za testove: obradi tastere bez pravog terminala.</summary>
    public void RunKeys(string keys)
    {
        if (Ui.Term is FakeTerminal ft) ft.Feed(keys);
        Ui.Input.BeforeBlock = Render;
        try
        {
            while (!Quit)
            {
                try
                {
                    if (_treeFocus && _treeOpen) TreeCommand(); else NormalCommand();
                    AfterCommand();
                }
                catch (EndOfInputException) { throw; }
                catch (VimError e) { Mode = Mode.Normal; Notify(e.Message, true); }
            }
        }
        catch (EndOfInputException) { }
    }

    private void AfterCommand()
    {
        if (Buf == null) return;
        if (Opt.Diagnostics && Hooks.Diagnose != null && Buf.FileType == "powershell" && Buf.DiagVersion != Buf.Version && !Ui.Input.HasPending)
            RunDiagnostics(false);
    }

    private void SavePositions()
    {
        var list = Buffers.Where(b => b.Path != null).Select(b => (b.Path, b.Cursor.L, b.Cursor.C)).ToList();
        if (Buf?.Path != null) { list.RemoveAll(x => x.Path == Buf.Path); list.Insert(0, (Buf.Path, Buf.Cursor.L, Buf.Cursor.C)); }
        Files.SaveRecent(list);
    }

    // =================================================================
    //  Baferi
    // =================================================================
    public Buffer Open(string path, bool restorePos = false, int line = -1, int col = 0)
    {
        var full = Path.GetFullPath(path);
        var b = Buffers.FirstOrDefault(x => x.Path != null && string.Equals(x.Path, full, StringComparison.OrdinalIgnoreCase));
        if (b == null)
        {
            if (Directory.Exists(full)) { _tree ??= new FileTree(full, Opt); _tree.SetRoot(full); _treeOpen = true; _treeFocus = true; return Buf; }
            b = Buffer.Load(full);
            // zatvori prazan početni bafer
            if (Buffers.Count == 1 && Buffers[0].IsScratch && !Buffers[0].Modified) Buffers.Clear();
            Buffers.Add(b);
            if (restorePos && line < 0)
            {
                var r = Files.LoadRecent().FirstOrDefault(x => string.Equals(x.Path, full, StringComparison.OrdinalIgnoreCase));
                if (r.Path != null) b.Cursor = b.Clamp(new Pos(r.Line, r.Col));
            }
            if (!File.Exists(full)) Notify($"\"{Path.GetFileName(full)}\" [Novi fajl]");
        }
        SwitchTo(b);
        if (line >= 0)
        {
            PushJump();
            Buf.Cursor = Buf.Clamp(new Pos(line, col));
            CenterCursor();
        }
        return b;
    }

    public Buffer NewScratch(string name, string text, string ft = "text", bool readOnly = false)
    {
        var b = Buffer.FromText(text);
        b.IsScratch = true; b.ScratchName = name; b.FileType = ft; b.ReadOnly = readOnly;
        Buffers.Add(b);
        SwitchTo(b);
        return b;
    }

    public void SwitchTo(Buffer b)
    {
        if (b == Buf) return;
        if (Buf != null && Buffers.Contains(Buf)) _alt = Buf;
        Buf = b;
        Ui.Screen.Invalidate(); // pun redraw pri promeni fajla
        if (Mode != Mode.Normal && Mode != Mode.Tree) Mode = Mode.Normal;
        if (b.IsBinary) Notify($"\"{b.DisplayName}\" je binarni fajl — otvoren samo za čitanje");
        if (_treeOpen && b.Path != null) _tree?.Reveal(b.Path);
    }

    public void CloseBuffer(Buffer b, bool force)
    {
        if (b.Modified && !force && !b.IsScratch) throw new VimError("E89: Bafer nije sačuvan (dodaj ! da odbaciš izmene)");
        int idx = Buffers.IndexOf(b);
        Buffers.Remove(b);
        _hl.Remove(b);
        if (Buf == b)
        {
            Buf = null;
            if (_alt != null && Buffers.Contains(_alt)) SwitchTo(_alt);
            else if (Buffers.Count > 0) SwitchTo(Buffers[Compat.Clamp(idx - 1, 0, Buffers.Count - 1)]);
            else ShowDashboard();
        }
    }

    private void CycleBuffer(int d)
    {
        var list = Buffers.Where(b => !b.IsScratch || b == Buf).ToList();
        if (list.Count <= 1) return;
        int i = list.IndexOf(Buf);
        SwitchTo(list[(i + d + list.Count) % list.Count]);
    }

    public void ShowDashboard()
    {
        var sb = new StringBuilder();
        string[] logo =
        {
            "",
            "   ██████╗ ███████╗██╗   ██╗██╗███╗   ███╗",
            "   ██╔══██╗██╔════╝██║   ██║██║████╗ ████║",
            "   ██████╔╝███████╗██║   ██║██║██╔████╔██║",
            "   ██╔═══╝ ╚════██║╚██╗ ██╔╝██║██║╚██╔╝██║",
            "   ██║     ███████║ ╚████╔╝ ██║██║ ╚═╝ ██║",
            "   ╚═╝     ╚══════╝  ╚═══╝  ╚═╝╚═╝     ╚═╝",
            "",
            $"   vim za PowerShell  ·  v{Version}",
            "",
            "   f  Pronađi fajl          n  Novi fajl",
            "   r  Nedavni fajlovi       g  Pretraži tekst",
            "   e  Stablo fajlova        c  Konfiguracija",
            "   k  Sve prečice           ?  Pomoć",
            "   q  Izlaz",
            "",
            "   (u editoru: ␣ = leader, sačekaj da vidiš meni)",
            ""
        };
        foreach (var l in logo) sb.Append(l).Append('\n');
        var recent = Files.LoadRecent().Where(r => File.Exists(r.Path)).Take(8).ToList();
        if (recent.Count > 0)
        {
            sb.Append("   Nedavno:\n");
            foreach (var r in recent) sb.Append("     ").Append(ShortPath(r.Path)).Append('\n');
        }
        var b = Buffer.FromText(sb.ToString().TrimEnd('\n'));
        b.IsScratch = true; b.ScratchName = "[Dashboard]"; b.FileType = "dashboard"; b.ReadOnly = true;
        Buffers.Add(b);
        Buf = b;
    }

    public string ShortPath(string full)
    {
        if (full == null) return "";
        try
        {
            var rel = Compat.RelativePath(Cwd, full);
            if (!rel.StartsWith("..")) return rel;
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (full.StartsWith(home, StringComparison.OrdinalIgnoreCase)) return "~" + full.Substring(home.Length);
        }
        catch { }
        return full;
    }

    // =================================================================
    //  Poruke
    // =================================================================
    public void Notify(string text, bool error = false)
    {
        LastMessage = text;
        if (string.IsNullOrEmpty(text)) return;
        _notes.RemoveAll(n => n.Text == text);
        _notes.Add(new Notification { Text = text, Error = error, Until = Compat.Ticks + (error ? 6000 : 3000) });
        if (_notes.Count > 4) _notes.RemoveAt(0);
        _messages.Add((error ? "E " : "  ") + text);
        if (_messages.Count > 200) _messages.RemoveAt(0);
    }
    private readonly List<string> _messages = new();

    // =================================================================
    //  Registri
    // =================================================================
    private bool UsePlus => Opt.Clipboard?.Contains("unnamed") == true && Hooks.ClipboardSet != null;

    private void SetReg(char? name, string text, bool linewise, bool yank)
    {
        if (name == '_') return;
        var r = new Register(text, linewise);
        if (name is >= 'A' and <= 'Z')
        {
            char low = char.ToLowerInvariant(name.Value);
            if (_regs.TryGetValue(low, out var old))
                r = new Register(old.Text + (old.Linewise || linewise ? "\n" : "") + text, old.Linewise || linewise);
            _regs[low] = r;
        }
        else if (name is >= 'a' and <= 'z') _regs[name.Value] = r;
        else if (name == '+' || name == '*') TrySetClipboard(text, linewise);
        if (yank && name == null) _regs['0'] = r;
        if (!yank)
        {
            if (linewise || text.Contains('\n'))
            {
                for (char c = '9'; c > '1'; c--) if (_regs.TryGetValue((char)(c - 1), out var p)) _regs[c] = p;
                _regs['1'] = r;
            }
            else _regs['-'] = r;
        }
        _regs['"'] = r;
        if (name == null && UsePlus) TrySetClipboard(text, linewise);
    }

    private void TrySetClipboard(string text, bool linewise)
    {
        try { Hooks.ClipboardSet?.Invoke(linewise ? text + "\n" : text); } catch { }
    }

    private Register GetReg(char? name)
    {
        if (name == '+' || name == '*' || (name == null && UsePlus && Hooks.ClipboardGet != null))
        {
            string cb = null;
            try { cb = Hooks.ClipboardGet?.Invoke(); } catch { }
            if (cb != null)
            {
                cb = cb.Replace("\r\n", "\n");
                if (_regs.TryGetValue('"', out var un))
                {
                    var unText = un.Linewise ? un.Text + "\n" : un.Text;
                    if (unText == cb || un.Text == cb.TrimEnd('\n')) return un;
                }
                if (cb.Length == 0) return _regs.GetValueOrDefault('"') ?? new Register();
                bool lw = cb.EndsWith('\n');
                return new Register(lw ? cb.Substring(0, cb.Length - 1) : cb, lw);
            }
        }
        if (name == null) return _regs.GetValueOrDefault('"') ?? new Register();
        char n = char.ToLowerInvariant(name.Value);
        if (n == '%') return new Register(Buf.Path ?? "", false);
        if (n == ':') return new Register(_cmdHistory.LastOrDefault() ?? "", false);
        if (n == '/') return new Register(_lastSearch ?? "", false);
        return _regs.GetValueOrDefault(n) ?? new Register();
    }

    // =================================================================
    //  Pomoćne funkcije
    // =================================================================
    private HighlightCache Hl(Buffer b)
    {
        if (!_hl.TryGetValue(b, out var h)) _hl[b] = h = new HighlightCache();
        return h;
    }

    private string Line => Buf.Lines[Buf.Cursor.L];
    private Pos Cur { get => Buf.Cursor; set => Buf.Cursor = value; }

    private void ClampNormal()
    {
        var p = Buf.Clamp(Cur);
        int len = Buf.LineLen(p.L);
        if (Mode != Mode.Insert && Mode != Mode.Replace && p.C >= len) p.C = Math.Max(0, len - 1);
        Cur = p;
    }

    private void SetCol(int c, bool updateWant = true)
    {
        Cur = new Pos(Cur.L, c);
        if (updateWant) Buf.WantCol = DisplayCol(Buf.Lines[Cur.L], c);
    }

    public int DisplayCol(string line, int c)
    {
        int dc = 0, ts = Opt.TabStop;
        for (int i = 0; i < c && i < line.Length; i++) dc = line[i] == '\t' ? (dc / ts + 1) * ts : dc + 1;
        if (c > line.Length) dc += c - line.Length;
        return dc;
    }

    private int ColFromDisplay(string line, int want)
    {
        int dc = 0, ts = Opt.TabStop;
        for (int i = 0; i < line.Length; i++)
        {
            int next = line[i] == '\t' ? (dc / ts + 1) * ts : dc + 1;
            if (next > want) return i;
            dc = next;
        }
        return line.Length;
    }

    private void CheckWritable()
    {
        if (Buf.ReadOnly) throw new VimError("Bafer je samo za čitanje");
    }

    private void PushJump()
    {
        if (Buf == null) return;
        if (_jumpIdx < _jumps.Count - 1) _jumps.RemoveRange(_jumpIdx + 1, _jumps.Count - _jumpIdx - 1);
        if (_jumps.Count > 0 && _jumps[^1].B == Buf && _jumps[^1].P.L == Cur.L) { _jumpIdx = _jumps.Count - 1; return; }
        _jumps.Add((Buf, Cur));
        if (_jumps.Count > 100) _jumps.RemoveAt(0);
        _jumpIdx = _jumps.Count - 1;
    }

    private void JumpOlder(int d)
    {
        if (_jumps.Count == 0) return;
        if (d < 0 && _jumpIdx == _jumps.Count - 1)
        {
            // sačuvaj trenutnu poziciju da bi <C-i> mogao nazad
            if (_jumps[^1].B != Buf || _jumps[^1].P.L != Cur.L) { _jumps.Add((Buf, Cur)); _jumpIdx = _jumps.Count - 1; }
        }
        int ni = _jumpIdx + d;
        while (ni >= 0 && ni < _jumps.Count && !Buffers.Contains(_jumps[ni].B)) ni += d;
        if (ni < 0 || ni >= _jumps.Count) return;
        _jumpIdx = ni;
        var (b, p) = _jumps[ni];
        SwitchTo(b);
        Cur = b.Clamp(p);
    }

    /// <summary>Indeks fajlova projekta; puni se u pozadini i osvežava posle 30s.</summary>
    public FileIndex Index(bool refresh = false)
    {
        if (_index == null || refresh || !string.Equals(_index.Root, Cwd, StringComparison.OrdinalIgnoreCase)
            || (_index.Done && (DateTime.UtcNow - _index.Created).TotalSeconds > 30))
        {
            _index?.Cancel();
            _index = new FileIndex(Cwd, Opt);
        }
        return _index;
    }

    /// <summary>Lista fajlova (čeka indeks najviše 3s) — za gd i slično.</summary>
    public List<string> ProjectFiles(bool refresh = false)
    {
        var ix = Index(refresh);
        ix.Wait(3000);
        return ix.Snapshot();
    }

    // =================================================================
    //  Crtanje
    // =================================================================
    private int _textX, _textY, _textW, _textH, _gutterW;

    public void Render()
    {
        Ui.BeginFrame();
        DrawBase();
        DrawCmdline();
        Ui.Screen.Flush();
    }

    /// <summary>Crta ceo editor osim komandne linije (koristi se i kao pozadina za popup-ove).</summary>
    public void DrawBase()
    {
        var sc = Ui.Screen; var th = Th;
        int W = sc.W, H = sc.H;
        if (Opt.Transparent) sc.Clear(new Style(th.Fg, -1));
        int top = 0;
        bool tabline = Buffers.Count(b => !b.IsScratch || b == Buf) > 1;
        if (tabline) { DrawTabline(0); top = 1; }
        int treeW = 0;
        if (_treeOpen && _tree != null)
        {
            treeW = Math.Min(Opt.TreeWidth, W / 2);
            DrawTree(0, top, treeW, H - 2 - top);
        }
        _textX = treeW; _textY = top; _textW = W - treeW; _textH = Math.Max(1, H - 2 - top);
        if (Buf != null) DrawWindow();
        DrawStatusline(H - 2);
        DrawNotifications();
        if (_hover != null) DrawHover();
        if (_completion != null) DrawCompletion();
        if (_treeFocus && _treeOpen && _tree != null)
        {
            int y = _tree.Sel - _tree.Scroll + top;
            sc.CursorX = 1; sc.CursorY = y; sc.Shape = CursorShape.Hidden;
        }
    }

    private void DrawTabline(int y)
    {
        var sc = Ui.Screen; var th = Th;
        sc.Fill(0, y, sc.W, 1, new Style(th.FgGutter, th.BgDark));
        int x = 0;
        foreach (var b in Buffers)
        {
            if (b.IsScratch && b != Buf) continue;
            bool cur = b == Buf;
            var bg = cur ? th.Bg : th.BgDark;
            var label = $" {b.DisplayName}{(b.Modified ? " ●" : "")} ";
            if (cur) sc.Set(x, y, "▎", new Style(th.Blue, bg));
            else sc.Set(x, y, " ", new Style(th.Fg, bg));
            x = sc.Put(x + 1, y, label, new Style(cur ? th.Fg : th.Comment, bg, cur ? Attr.Bold : Attr.None));
            if (x >= sc.W) break;
        }
    }

    private void EnsureCursorVisible()
    {
        int so = Math.Min(Opt.ScrollOff, (_textH - 1) / 2);
        if (Cur.L < Buf.Top + so) Buf.Top = Math.Max(0, Cur.L - so);
        if (Cur.L > Buf.Top + _textH - 1 - so) Buf.Top = Cur.L - _textH + 1 + so;
        Buf.Top = Compat.Clamp(Buf.Top, 0, Math.Max(0, Buf.Count - 1));
        int textW = _textW - _gutterW;
        int dc = DisplayCol(Line, Cur.C);
        if (dc < Buf.Left) Buf.Left = dc;
        if (dc >= Buf.Left + textW - 1) Buf.Left = dc - textW + 2;
        if (Buf.Left < 0) Buf.Left = 0;
    }

    private void CenterCursor() { if (_textH > 0) Buf.Top = Math.Max(0, Cur.L - _textH / 2); else Buf.Top = Math.Max(0, Cur.L - 10); }

    private void DrawWindow()
    {
        var sc = Ui.Screen; var th = Th; var b = Buf;
        bool showNum = Opt.Number || Opt.RelativeNumber;
        int numW = showNum ? Math.Max(3, b.Count.ToString().Length) : 0;
        bool dash = b.FileType == "dashboard";
        _gutterW = dash ? 0 : 2 + numW + (showNum ? 1 : 0);
        EnsureCursorVisible();

        // vizuelna selekcija
        Pos vs = default, ve = default; bool vis = Mode == Mode.Visual || Mode == Mode.VisualLine;
        if (vis) { vs = _vAnchor < Cur ? _vAnchor : Cur; ve = _vAnchor < Cur ? Cur : _vAnchor; }

        var rx = (_hlActive && Opt.HlSearch && !string.IsNullOrEmpty(_lastSearch)) || _incSearch != null ? SafeRegex(_incSearch ?? _lastSearch) : null;
        var diagByLine = b.Diagnostics.GroupBy(d => d.Line).ToDictionary(g => g.Key, g => g.OrderBy(d => SevRank(d.Severity)).ToList());
        var hl = Hl(b);
        int textW = _textW - _gutterW;

        for (int row = 0; row < _textH; row++)
        {
            int l = b.Top + row;
            int y = _textY + row;
            bool curLine = l == Cur.L && !dash;
            int rowBg = (curLine && Opt.CursorLine && Mode != Mode.Visual && Mode != Mode.VisualLine) ? th.BgHighlight : (Opt.Transparent ? -1 : th.Bg);
            sc.Fill(_textX, y, _textW, 1, new Style(th.Fg, Opt.Transparent && !curLine ? -1 : (curLine && Opt.CursorLine ? th.BgHighlight : th.Bg)));
            if (l >= b.Count) continue;
            int x = _textX;
            if (!dash)
            {
                // znak dijagnostike
                if (diagByLine.TryGetValue(l, out var dl))
                {
                    var d = dl[0];
                    sc.Put(x, y, Opt.NerdFont ? (d.Severity == "Error" ? " " : " ") : (d.Severity == "Error" ? "E " : "W "), new Style(SevColor(d.Severity), -1).WithBg(th.Bg));
                }
                x += 2;
                if (showNum)
                {
                    string num;
                    if (Opt.RelativeNumber && l != Cur.L) num = Math.Abs(l - Cur.L).ToString().PadLeft(numW);
                    else num = (l + 1).ToString().PadLeft(numW);
                    sc.Put(x, y, num, new Style(l == Cur.L ? th.CursorLineNr : th.LineNr, th.Bg, l == Cur.L ? Attr.Bold : Attr.None));
                    x += numW + 1;
                }
            }
            var line = b.Lines[l];
            // stilovi po karakteru
            var toks = new Tok[line.Length];
            if (dash) DashboardStyles(line, toks, l);
            else
            {
                var spans = hl.Get(b, l);
                if (spans != null)
                    foreach (var s in spans)
                        for (int k = Math.Max(0, s.Start); k < s.Start + s.Len && k < toks.Length; k++) toks[k] = s.Kind;
            }
            // pretraga
            bool[] match = null;
            if (rx != null && line.Length > 0)
            {
                try
                {
                    foreach (System.Text.RegularExpressions.Match m in rx.Matches(line))
                    {
                        if (m.Length == 0) continue;
                        match ??= new bool[line.Length];
                        for (int k = m.Index; k < m.Index + m.Length; k++) match[k] = true;
                    }
                }
                catch { }
            }
            // crtanje karaktera sa tabovima i horizontalnim pomeranjem
            int dc = 0, ts = Opt.TabStop;
            int indentEnd = 0; while (indentEnd < line.Length && (line[indentEnd] == ' ' || line[indentEnd] == '\t')) indentEnd++;
            bool blank = indentEnd == line.Length;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                int w = ch == '\t' ? (dc / ts + 1) * ts - dc : 1;
                for (int k = 0; k < w; k++, dc++)
                {
                    int sx = dc - b.Left;
                    if (sx < 0) continue;
                    if (sx >= textW) break;
                    var st = th.Tok(toks[i]).WithBg(rowBg);
                    string t = ch == '\t' ? " " : (ch < ' ' ? "?" : ch.ToString());
                    if (char.IsHighSurrogate(ch) && i + 1 < line.Length) { t = line.Substring(i, 2); }
                    else if (char.IsLowSurrogate(ch)) continue;
                    // indent vodiči
                    if (Opt.IndentGuides && i < indentEnd && !blank && t == " " && dc > 0 && dc % Opt.ShiftWidth == 0 && !dash)
                    { t = "│"; st = new Style(th.IndentGuide == th.Bg ? th.FgGutter : th.IndentGuide, rowBg); }
                    if (match != null && match[i]) st = new Style(th.Fg, th.BgSearch);
                    if (vis)
                    {
                        bool inSel = Mode == Mode.VisualLine ? (l >= vs.L && l <= ve.L) : (new Pos(l, i) >= vs && new Pos(l, i) <= ve);
                        if (inSel) st = st.WithBg(th.BgVisual);
                    }
                    sc.Set(_textX + _gutterW + sx, y, t, st);
                }
            }
            // prazna linija u vizuelnom modu
            if (vis && line.Length == 0 && ((Mode == Mode.VisualLine && l >= vs.L && l <= ve.L) || (Mode == Mode.Visual && l > vs.L && l <= ve.L)))
                sc.SetBg(_textX + _gutterW, y, th.BgVisual);
            if (vis && Mode == Mode.VisualLine && l >= vs.L && l <= ve.L)
                for (int sx = Math.Max(0, DisplayCol(line, line.Length) - b.Left); sx < textW; sx++) sc.SetBg(_textX + _gutterW + sx, y, th.BgVisual);
            // podvlačenje i virtuelni tekst dijagnostike
            if (diagByLine.TryGetValue(l, out var dl2))
            {
                var d = dl2[0];
                int c0 = Compat.Clamp(d.Col, 0, Math.Max(0, line.Length - 1));
                int c1 = c0; while (c1 < line.Length && !char.IsWhiteSpace(line[c1])) c1++;
                if (c1 == c0) c1 = Math.Min(line.Length, c0 + 1);
                for (int k = c0; k < c1; k++)
                {
                    int sx = DisplayCol(line, k) - b.Left;
                    if (sx >= 0 && sx < textW)
                    {
                        var st = sc.GetStyle(_textX + _gutterW + sx, y);
                        sc.SetStyle(_textX + _gutterW + sx, y, new Style(SevColor(d.Severity), st.Bg, st.Attr | Attr.Underline));
                    }
                }
                if (Opt.VirtualText)
                {
                    int vx = _textX + _gutterW + Math.Max(0, DisplayCol(line, line.Length) - b.Left) + 2;
                    var msg = (Opt.NerdFont ? "■ " : "● ") + d.Message.Replace('\n', ' ').Replace('\r', ' ');
                    if (dl2.Count > 1) msg += $" (+{dl2.Count - 1})";
                    sc.Put(vx, y, msg, new Style(SevColor(d.Severity), rowBg, Attr.Italic), _textX + _textW);
                }
            }
        }
        // flash oznake
        if (_flash != null)
        {
            foreach (var (p, len, label) in _flash)
            {
                int row = p.L - b.Top; if (row < 0 || row >= _textH) continue;
                var line = b.Lines[p.L];
                for (int k = 0; k < len && p.C + k < line.Length; k++)
                {
                    int sx = DisplayCol(line, p.C + k) - b.Left;
                    if (sx >= 0 && sx < textW) sc.Set(_textX + _gutterW + sx, _textY + row, line[p.C + k] == '\t' ? " " : line[p.C + k].ToString(), new Style(th.Fg, th.BgSearch));
                }
                if (label != '\0')
                {
                    int sx = DisplayCol(line, p.C + len) - b.Left;
                    if (sx >= 0 && sx < textW) sc.Set(_textX + _gutterW + sx, _textY + row, label.ToString(), new Style(0x000000, th.Magenta, Attr.Bold));
                }
            }
        }
        // kursor
        if (!_treeFocus)
        {
            int cdc = DisplayCol(Line, Cur.C);
            sc.CursorX = _textX + _gutterW + cdc - b.Left;
            sc.CursorY = _textY + Cur.L - b.Top;
            sc.Shape = Mode == Mode.Insert ? CursorShape.Bar : Mode == Mode.Replace ? CursorShape.Underline : CursorShape.Block;
            if (dash) sc.Shape = CursorShape.Hidden;
        }
    }

    private void DashboardStyles(string line, Tok[] toks, int l)
    {
        Tok t = Tok.Default;
        if (line.Contains('█') || line.Contains('╗') || line.Contains('╚')) t = Tok.Function;
        else if (line.Contains("vim za PowerShell")) t = Tok.Comment;
        else if (line.TrimStart().StartsWith("Nedavno")) t = Tok.Keyword;
        else if (line.StartsWith("   (")) t = Tok.Comment;
        for (int i = 0; i < toks.Length; i++) toks[i] = t;
        if (t == Tok.Comment) return;
        // tasteri u meniju: "   f  Tekst"
        for (int i = 1; i + 2 < line.Length; i++)
            if (line[i] != ' ' && line[i - 1] == ' ' && line[i + 1] == ' ' && line[i + 2] == ' ' && (i < 2 || line[i - 2] == ' ') && !line.Contains('█'))
                toks[i] = Tok.Constant;
    }

    private static int SevRank(string s) => s == "Error" ? 0 : s == "Warning" ? 1 : 2;
    private int SevColor(string s) => s == "Error" ? Th.Red : s == "Warning" ? Th.Yellow : Th.Teal;

    private System.Text.RegularExpressions.Regex SafeRegex(string pat)
    {
        if (string.IsNullOrEmpty(pat)) return null;
        try { return Files.MakeRegex(pat, Opt.IgnoreCase, Opt.SmartCase); } catch { return null; }
    }

    private void DrawStatusline(int y)
    {
        var sc = Ui.Screen; var th = Th;
        sc.Fill(0, y, sc.W, 1, new Style(th.FgDark, th.BgDark));
        if (Buf == null) return;
        (string name, int color) = (_treeFocus && _treeOpen) ? ("STABLO", th.Teal) : Mode switch
        {
            Mode.Insert => ("INSERT", th.Green),
            Mode.Visual => ("VISUAL", th.Magenta),
            Mode.VisualLine => ("V-LINE", th.Magenta),
            Mode.Replace => ("REPLACE", th.Red),
            Mode.Command => ("COMMAND", th.Yellow),
            _ => ("NORMAL", th.Blue)
        };
        bool nf = Opt.NerdFont;
        int x = sc.Put(0, y, $" {name} ", new Style(th.BgDark, color, Attr.Bold));
        if (nf) x = sc.Put(x, y, "", new Style(color, th.BgHighlight));
        var branch = Files.GitBranch(Buf.Path ?? Cwd);
        if (branch != null) x = sc.Put(x, y, (nf ? "  " : " ⎇ ") + branch + " ", new Style(color, th.BgHighlight));
        else x = sc.Put(x, y, " ", new Style(color, th.BgHighlight));
        x = sc.Put(x, y, " ", new Style(th.Fg, th.BgDark));
        // dijagnostika
        int ne = Buf.Diagnostics.Count(d => d.Severity == "Error"), nw = Buf.Diagnostics.Count(d => d.Severity == "Warning");
        if (ne > 0) x = sc.Put(x, y, (nf ? " " : "E:") + ne + " ", new Style(th.Red, th.BgDark));
        if (nw > 0) x = sc.Put(x, y, (nf ? " " : "W:") + nw + " ", new Style(th.Yellow, th.BgDark));
        var fname = Buf.Path != null ? ShortPath(Buf.Path) : Buf.DisplayName;
        x = sc.Put(x, y, fname, new Style(th.Fg, th.BgDark, Attr.Bold));
        if (Buf.Modified) x = sc.Put(x, y, " ●", new Style(th.Orange, th.BgDark));
        if (Buf.ReadOnly) x = sc.Put(x, y, " [RO]", new Style(th.Red, th.BgDark));

        // desna strana
        var parts = new List<(string, Style)>();
        if (_macroReg != null) parts.Add(($"snima @{_macroReg} ", new Style(th.Orange, th.BgDark, Attr.Bold)));
        if (_showCmd.Length > 0) parts.Add((_showCmd + " ", new Style(th.Fg, th.BgDark)));
        var sCount = SearchCount();
        if (sCount != null) parts.Add((sCount + " ", new Style(th.Yellow, th.BgDark)));
        parts.Add((Buf.FileType + " ", new Style(th.Comment, th.BgDark)));
        int pct = Buf.Count <= 1 ? 100 : (int)Math.Round(100.0 * Cur.L / (Buf.Count - 1));
        string pos = Buf.Top == 0 && Buf.Count <= _textH ? "Sve" : Cur.L == 0 ? "Vrh" : Cur.L == Buf.Count - 1 ? "Dno" : pct + "%";
        parts.Add(($" {pos} ", new Style(color, th.BgHighlight)));
        parts.Add(($" {Cur.L + 1}:{Cur.C + 1} ", new Style(th.BgDark, color, Attr.Bold)));
        parts.Add(($" {DateTime.Now:HH:mm} ", new Style(th.BgDark, color)));
        int total = parts.Sum(p => p.Item1.Length);
        int rx = Math.Max(x + 1, sc.W - total);
        foreach (var (t, s) in parts) rx = sc.Put(rx, y, t, s);
    }

    private string SearchCount()
    {
        if (!_hlActive || string.IsNullOrEmpty(_lastSearch)) return null;
        var rx = SafeRegex(_lastSearch);
        if (rx == null) return null;
        int total = 0, idx = 0;
        try
        {
            for (int l = 0; l < Buf.Count && total < 1000; l++)
                foreach (System.Text.RegularExpressions.Match m in rx.Matches(Buf.Lines[l]))
                {
                    if (m.Length == 0) continue;
                    total++;
                    if (l < Cur.L || (l == Cur.L && m.Index <= Cur.C)) idx = total;
                }
        }
        catch { return null; }
        if (total == 0) return null;
        return $"[{idx}/{(total >= 1000 ? ">999" : total.ToString())}]";
    }

    private void DrawNotifications()
    {
        var now = Compat.Ticks;
        _notes.RemoveAll(n => n.Until < now - 400 && !_keepNotes);
        _keepNotes = false;
        var sc = Ui.Screen; var th = Th;
        int y = _textY;
        foreach (var n in _notes.AsEnumerable().Reverse())
        {
            var lines = Wrap(n.Text, Math.Min(60, sc.W - 6));
            int w = Math.Min(sc.W - 2, lines.Max(l => l.Length) + 4);
            int h = lines.Count + 2;
            int x = sc.W - w - 1;
            if (y + h > sc.H - 2) break;
            var border = new Style(n.Error ? th.Red : th.Green, th.PopupBg);
            sc.Box(x, y, w, h, border, new Style(th.Fg, th.PopupBg), n.Error ? "Greška" : "Info", border);
            for (int i = 0; i < lines.Count; i++) sc.Put(x + 2, y + 1 + i, lines[i], new Style(th.Fg, th.PopupBg), x + w - 1);
            y += h;
        }
    }
    private bool _keepNotes;

    public static List<string> Wrap(string text, int width)
    {
        var res = new List<string>();
        foreach (var raw in (text ?? "").Replace("\r", "").Split('\n'))
        {
            var l = raw.Replace('\t', ' ');
            while (l.Length > width)
            {
                int cut = l.LastIndexOf(' ', width);
                if (cut <= width / 3) cut = width;
                res.Add(l.Substring(0, cut));
                l = l.Substring(cut).TrimStart();
            }
            res.Add(l);
        }
        if (res.Count == 0) res.Add("");
        return res;
    }

    private void DrawCmdline()
    {
        var sc = Ui.Screen; var th = Th;
        int y = sc.H - 1;
        sc.Fill(0, y, sc.W, 1, new Style(th.Fg, Opt.Transparent ? -1 : th.Bg));
        if (_cmdlineOverlay == null) return;
        if (_cmdlinePrompt == ":")
        {
            // noice stil: plutajući prozor u gornjoj trećini
            int w = Math.Min(sc.W - 4, Math.Max(50, sc.W / 2));
            int x = (sc.W - w) / 2, by = Math.Max(1, sc.H / 4);
            var bg = th.PopupBg;
            sc.Box(x, by, w, 3, new Style(th.Yellow, bg), new Style(th.Fg, bg), "Komanda", new Style(th.Yellow, bg, Attr.Bold));
            sc.Put(x + 2, by + 1, Opt.NerdFont ? " " : "> ", new Style(th.Yellow, bg, Attr.Bold));
            int avail = w - 6;
            int off = Math.Max(0, _cmdlineCursor - avail + 1);
            sc.Put(x + 4, by + 1, _cmdlineOverlay.Substring(Math.Min(off, _cmdlineOverlay.Length)), new Style(th.Fg, bg), x + w - 1);
            sc.CursorX = x + 4 + _cmdlineCursor - off; sc.CursorY = by + 1; sc.Shape = CursorShape.Bar;
            return;
        }
        int px = sc.Put(0, y, _cmdlinePrompt, new Style(th.Yellow, th.Bg, Attr.Bold));
        int av = sc.W - px - 1;
        int o2 = Math.Max(0, _cmdlineCursor - av + 1);
        sc.Put(px, y, _cmdlineOverlay.Substring(Math.Min(o2, _cmdlineOverlay.Length)), new Style(th.Fg, th.Bg));
        sc.CursorX = px + _cmdlineCursor - o2; sc.CursorY = y; sc.Shape = CursorShape.Bar;
    }

    private void DrawTree(int x, int y, int w, int h)
    {
        var sc = Ui.Screen; var th = Th; var t = _tree;
        var bg = th.BgDark;
        sc.Fill(x, y, w, h, new Style(th.Fg, bg));
        for (int yy = y; yy < y + h; yy++) sc.Set(x + w - 1, yy, "│", new Style(th.FgGutter, bg));
        var title = " " + (Path.GetFileName(t.Root.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : t.Root) + " ";
        sc.Put(x + 1, y, title, new Style(th.Blue, bg, Attr.Bold), x + w - 1);
        int listY = y + 1, listH = h - 1;
        if (t.Sel < t.Scroll) t.Scroll = t.Sel;
        if (t.Sel >= t.Scroll + listH) t.Scroll = t.Sel - listH + 1;
        for (int r = 0; r < listH; r++)
        {
            int i = t.Scroll + r;
            if (i >= t.Visible.Count) break;
            var (node, depth) = t.Visible[i];
            bool sel = i == t.Sel;
            bool open = Buf?.Path != null && string.Equals(Buf.Path, node.Path, StringComparison.OrdinalIgnoreCase);
            int rbg = sel ? (_treeFocus ? th.BgVisual : th.BgHighlight) : bg;
            sc.Fill(x, listY + r, w - 1, 1, new Style(th.Fg, rbg));
            int xx = x + 1 + depth * 2;
            string icon;
            int iconColor;
            if (node.IsDir)
            {
                bool exp = t.Expanded.Contains(node.Path);
                icon = Opt.NerdFont ? (exp ? " " : " ") : (exp ? "▾ " : "▸ ");
                iconColor = th.Blue;
            }
            else { icon = Opt.NerdFont ? FileIcon(node.Name) : "  "; iconColor = FileIconColor(node.Name); }
            xx = sc.Put(xx, listY + r, icon, new Style(iconColor, rbg), x + w - 1);
            var mod = Buffers.Any(b => b.Modified && b.Path != null && string.Equals(b.Path, node.Path, StringComparison.OrdinalIgnoreCase));
            var nameStyle = new Style(node.IsDir ? th.Blue : (open ? th.Orange : th.Fg), rbg, open || node.IsDir ? Attr.Bold : Attr.None);
            xx = sc.Put(xx, listY + r, node.Name, nameStyle, x + w - 2);
            if (mod) sc.Put(Math.Min(xx + 1, x + w - 3), listY + r, "●", new Style(th.Orange, rbg));
        }
    }

    private static string FileIcon(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".ps1" or ".psm1" or ".psd1" => "\U000f0a0a ",
        ".cs" => "\U000f031b ",
        ".json" => " ",
        ".md" => " ",
        ".js" => " ",
        ".ts" => " ",
        ".py" => " ",
        ".xml" or ".csproj" => "󰗀 ",
        ".yml" or ".yaml" => " ",
        _ => " "
    };

    private int FileIconColor(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".ps1" or ".psm1" or ".psd1" => Th.Blue,
        ".cs" => Th.Magenta,
        ".json" => Th.Yellow,
        ".md" => Th.Teal,
        _ => Th.FgDark
    };
}
