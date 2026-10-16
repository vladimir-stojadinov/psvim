using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace PsVim;

public sealed class HoverState
{
    public string Title, Text; public int Scroll; public bool Diff;
}

/// <summary>LazyVim sloj: leader mapiranja, picker-i, stablo, flash, dijagnostika, git...</summary>
public sealed partial class Editor
{
    private HoverState _hover;
    private readonly Dictionary<Buffer, (int Version, List<Diagnostic> Items)> _lint = new();
    private bool _diagBroken;

    // =================================================================
    //  Leader mapa (kao LazyVim)
    // =================================================================
    private void BuildLeaderMap()
    {
        var m = _leader;
        m.Map(" ", "Pronađi fajlove", FindFiles);
        m.Map(",", "Baferi", PickBuffers);
        m.Map("/", "Grep (projekat)", () => LiveGrep(null));
        m.Map(":", "Istorija komandi", CommandHistory);
        m.Map("e", "Stablo fajlova", ToggleTree);
        m.Map("E", "Stablo (cwd)", () => { OpenTree(true); _tree.SetRoot(Cwd); });
        m.Map("`", "Prethodni bafer", AltBuffer);
        m.Map("?", "Prečice", Keymaps);
        m.Map("l", "Lazy", () => ExecuteEx("Lazy"));

        m.Group("b", "bafer");
        m.Map("bb", "Prethodni bafer", AltBuffer);
        m.Map("bd", "Zatvori bafer", () => CloseBuffer(Buf, false));
        m.Map("bD", "Zatvori bafer (bez čuvanja)", () => CloseBuffer(Buf, true));
        m.Map("bo", "Zatvori ostale bafere", CloseOthers);
        m.Map("bn", "Sledeći bafer", () => CycleBuffer(1));
        m.Map("bp", "Prethodni bafer", () => CycleBuffer(-1));

        m.Group("c", "kod");
        m.Map("cf", "Formatiraj", () => FormatBuffer());
        m.Map("cd", "Dijagnostika linije", LineDiagnostics);
        m.Map("cx", "Pokreni fajl", RunCurrentFile);
        m.Map("cR", "Preimenuj fajl", RenameFile);
        m.Map("cs", "Simboli", Symbols);

        m.Group("f", "fajl/traži");
        m.Map("ff", "Pronađi fajlove", FindFiles);
        m.Map("fF", "Pronađi fajlove (osveži)", () => { Index(true); FindFiles(); });
        m.Map("fr", "Nedavni fajlovi", RecentFiles);
        m.Map("fb", "Baferi", PickBuffers);
        m.Map("fn", "Novi fajl", NewFile);
        m.Map("fc", "Konfiguracija", OpenConfig);
        m.Map("ft", "Terminal", OpenTerminal);
        m.Map("fd", "Promeni direktorijum (cd)", PickDirectory);

        m.Group("g", "git");
        m.Map("gg", "Lazygit", Lazygit);
        m.Map("gs", "Git status", GitStatus);
        m.Map("gl", "Git log", () => GitPopup("Git log", "log --oneline --decorate -n 80"));
        m.Map("gd", "Git diff (fajl)", () => GitPopup("diff", "diff -- " + Quote(Buf.Path ?? ".")));
        m.Map("gD", "Git diff (sve)", () => GitPopup("diff", "diff"));
        m.Map("gb", "Git blame linija", GitBlameLine);

        m.Group("q", "izlaz");
        m.Map("qq", "Izađi iz svega", () => QuitAll(false));
        m.Map("qQ", "Izađi bez čuvanja", () => QuitAll(true));

        m.Group("s", "pretraga");
        m.Map("sg", "Grep", () => LiveGrep(null));
        m.Map("sw", "Reč pod kursorom", GrepWord);
        m.Map("sb", "Linije bafera", BufferLines);
        m.Map("ss", "Simboli", Symbols);
        m.Map("sk", "Prečice", Keymaps);
        m.Map("sc", "Istorija komandi", CommandHistory);
        m.Map("s/", "Istorija pretrage", SearchHistory);
        m.Map("sd", "Dijagnostika", PickDiagnostics);
        m.Map("sm", "Oznake", () => ExecuteEx("marks"));
        m.Map("s\"", "Registri", () => ExecuteEx("registers"));
        m.Map("sr", "Traži i zameni", SearchReplace);
        m.Map("sh", "Pomoć", ShowHelp);
        m.Map("sH", "Get-Help za reč", Hover);

        m.Group("u", "ui");
        m.Map("ul", "Brojevi linija", () => { Opt.Number = !Opt.Number; if (!Opt.Number) Opt.RelativeNumber = false; });
        m.Map("uL", "Relativni brojevi", () => Opt.RelativeNumber = !Opt.RelativeNumber);
        m.Map("uC", "Tema", PickTheme);
        m.Map("ud", "Dijagnostika on/off", () => { Opt.Diagnostics = !Opt.Diagnostics; if (!Opt.Diagnostics) Buf.Diagnostics.Clear(); else Buf.DiagVersion = -1; Notify("Dijagnostika: " + (Opt.Diagnostics ? "uključena" : "isključena")); });
        m.Map("ug", "Vodiči uvlačenja", () => Opt.IndentGuides = !Opt.IndentGuides);
        m.Map("up", "Auto zagrade", () => { Opt.AutoPairs = !Opt.AutoPairs; Notify("Auto zagrade: " + (Opt.AutoPairs ? "uključene" : "isključene")); });
        m.Map("uc", "Linija kursora", () => Opt.CursorLine = !Opt.CursorLine);
        m.Map("uv", "Virtuelni tekst", () => Opt.VirtualText = !Opt.VirtualText);
        m.Map("un", "Ukloni obaveštenja", () => _notes.Clear());

        m.Group("x", "dijagnostika");
        m.Map("xx", "Dijagnostika (Trouble)", PickDiagnostics);
        m.Map("xX", "Dijagnostika bafera", PickDiagnostics);

        m.Group("w", "prozori");
        m.Map("ww", "Promeni fokus", () => { if (_treeOpen) _treeFocus = !_treeFocus; });
        m.Map("wd", "Zatvori stablo", () => { _treeOpen = false; _treeFocus = false; });
    }

    private void LoadUserKeymaps()
    {
        foreach (var km in Opt.Keymaps ?? new List<KeymapConfig>())
        {
            if (string.IsNullOrEmpty(km.Lhs) || km.Rhs == null) continue;
            if (km.Mode != null && km.Mode != "n") continue;
            var lhs = Keys.Parse(km.Lhs);
            var rhs = Keys.Parse(km.Rhs).Select(k => k == "<leader>" ? Opt.Leader : k).ToList();
            if (lhs.Count > 1 && lhs[0] == "<leader>")
            {
                var rest = Keys.Join(lhs.Skip(1));
                _leader.Map(rest, km.Desc ?? km.Rhs, () => Ui.Input.Push(rhs));
            }
            else if (lhs.Count == 1) _userMaps[lhs[0]] = (rhs, km.Desc);
        }
    }

    private void AltBuffer()
    {
        if (_alt != null && Buffers.Contains(_alt)) SwitchTo(_alt);
        else CycleBuffer(-1);
    }

    private void CloseOthers()
    {
        foreach (var b in Buffers.ToList())
            if (b != Buf && !b.Modified) { Buffers.Remove(b); _hl.Remove(b); }
        _alt = null;
    }

    // =================================================================
    //  Picker-i
    // =================================================================
    private PickerItem RunPicker(Picker p) => p.Run(Ui, DrawBase);

    public void FindFiles()
    {
        var p = Pickers.FilesPicker(Index(), $"Fajlovi ({ShortPath(Cwd)})");
        var pick = RunPicker(p);
        if (pick != null) Open(pick.Path, restorePos: true);
    }

    public void RecentFiles()
    {
        var items = Files.LoadRecent().Where(r => File.Exists(r.Path))
            .Select(r => new PickerItem { Text = ShortPath(r.Path), Path = r.Path, Line = r.Line }).ToList();
        var pick = RunPicker(new Picker { Title = "Nedavni fajlovi", Items = items });
        if (pick != null) Open(pick.Path, restorePos: true);
    }

    public void LiveGrep(string initial)
    {
        var pick = RunPicker(Pickers.GrepPicker(Index(), initial ?? ""));
        if (pick != null) Open(pick.Path, line: pick.Line, col: pick.Col);
    }

    private void GrepWord()
    {
        var w = WordUnderCursor();
        if (string.IsNullOrEmpty(w)) return;
        LiveGrep(Regex.Escape(w));
    }

    private void PickBuffers()
    {
        var items = Buffers.Where(b => b.FileType != "dashboard")
            .Select(b => new PickerItem { Text = b.DisplayName + (b.Modified ? " ●" : ""), Detail = b.Path != null ? ShortPath(Path.GetDirectoryName(b.Path)) : "", Path = b.Path, Tag = b, Line = b.Cursor.L })
            .ToList();
        // trenutni na kraj (kao u snacks picker-u)
        var cur = items.FirstOrDefault(i => i.Tag == Buf);
        if (cur != null && items.Count > 1) { items.Remove(cur); items.Insert(0, cur); var t = items[0]; items.RemoveAt(0); items.Insert(Math.Min(1, items.Count), t); }
        var p = new Picker { Title = "Baferi  (<C-x> zatvori)", Items = items };
        p.Actions["<C-x>"] = (it, q) =>
        {
            if (it?.Tag is Buffer b) { try { CloseBuffer(b, false); } catch (VimError e) { Notify(e.Message, true); } p.Items.Remove(it); }
            return p.Items.Count == 0;
        };
        var pick = RunPicker(p);
        if (pick?.Tag is Buffer bb && Buffers.Contains(bb)) SwitchTo(bb);
    }

    private static readonly Dictionary<string, Regex> SymbolRx = new()
    {
        ["powershell"] = new Regex(@"^\s*(function|filter|workflow|class|enum|configuration)\s+([\w\-:.]+)", RegexOptions.IgnoreCase),
        ["cs"] = new Regex(@"^\s*(?:public|private|protected|internal|static|sealed|abstract|partial|async|override|virtual|\s)*\s*(class|interface|struct|enum|record|void|[\w<>\[\],]+)\s+(\w+)\s*[\(<{:]"),
        ["python"] = new Regex(@"^\s*(def|class)\s+(\w+)"),
        ["javascript"] = new Regex(@"^\s*(?:export\s+)?(?:async\s+)?(function|class|const|let)\s+(\w+)"),
        ["typescript"] = new Regex(@"^\s*(?:export\s+)?(?:async\s+)?(function|class|interface|type|const)\s+(\w+)"),
        ["go"] = new Regex(@"^\s*(func|type)\s+(?:\([^)]*\)\s*)?(\w+)"),
        ["rust"] = new Regex(@"^\s*(?:pub\s+)?(fn|struct|enum|trait|impl)\s+(\w+)"),
        ["lua"] = new Regex(@"^\s*(?:local\s+)?(function)\s+([\w.:]+)"),
        ["sh"] = new Regex(@"^\s*(function\s+)?(\w+)\s*\(\)"),
        ["markdown"] = new Regex(@"^(#{1,6})\s+(.+)$"),
    };

    private void Symbols()
    {
        if (!SymbolRx.TryGetValue(Buf.FileType, out var rx)) rx = new Regex(@"^\s*(function|def|class|func|fn|sub)\s+([\w\-.]+)", RegexOptions.IgnoreCase);
        var items = new List<PickerItem>();
        for (int l = 0; l < Buf.Count; l++)
        {
            var m = rx.Match(Buf[l]);
            if (!m.Success) continue;
            var kind = m.Groups[1].Value.Trim();
            var name = m.Groups[2].Value.Trim();
            if (Buf.FileType == "cs" && (kind is "return" or "new" or "else" or "if")) continue;
            items.Add(new PickerItem { Text = name, Detail = $"{kind}  :{l + 1}", Path = Buf.Path, Line = l, Color = kind.Equals("class", StringComparison.OrdinalIgnoreCase) || kind.Equals("enum", StringComparison.OrdinalIgnoreCase) ? Tok.Type : Tok.Function });
        }
        if (items.Count == 0) { Notify("Nema simbola u ovom fajlu"); return; }
        var pick = RunPicker(new Picker { Title = "Simboli", Items = items, Preview = Buf.Path != null });
        if (pick != null) { PushJump(); Cur = new Pos(pick.Line, FirstNonBlankIndex(pick.Line)); CenterCursor(); }
    }

    private void BufferLines()
    {
        var items = new List<PickerItem>();
        for (int l = 0; l < Buf.Count; l++)
            if (Buf[l].Trim().Length > 0) items.Add(new PickerItem { Text = Buf[l].Trim(), Detail = (l + 1).ToString(), Path = Buf.Path, Line = l });
        var pick = RunPicker(new Picker { Title = "Linije bafera", Items = items, Preview = Buf.Path != null });
        if (pick != null) { PushJump(); Cur = new Pos(pick.Line, FirstNonBlankIndex(pick.Line)); CenterCursor(); }
    }

    private static readonly (string Keys, string Desc)[] BuiltinKeys =
    {
        ("s", "Flash skok"), ("gd", "Idi na definiciju"), ("gr", "Reference (grep)"), ("K", "Pomoć (Get-Help)"), ("gf", "Otvori fajl pod kursorom"),
        ("gcc", "Komentariši liniju"), ("gc{kretanje}", "Komentariši opseg"), ("H / L", "Prethodni / sledeći bafer"), ("]d / [d", "Sledeća / prethodna dijagnostika"),
        ("<C-s>", "Sačuvaj"), ("<C-/>", "Terminal"), ("<C-h> / <C-l>", "Fokus stablo / editor"), ("<C-o> / <C-i>", "Skokovi nazad / napred"),
        ("<Esc>", "Ukloni isticanje pretrage"), ("q{r} / @{r}", "Snimi / pusti makro"), (".", "Ponovi izmenu"), ("u / <C-r>", "Poništi / vrati"),
        ("<C-a> / <C-x>", "Uvećaj / umanji broj"), ("<C-Space>", "Dopuna (insert)"), ("gv", "Poslednja selekcija"), ("ZZ / ZQ", "Sačuvaj i izađi / izađi")
    };

    private void Keymaps()
    {
        var items = _leader.Walk().Select(x => new PickerItem { Text = "␣" + x.Path.Replace("<Space>", "␣") + "  " + x.Node.Desc, Tag = x.Node, Color = Tok.Default }).ToList();
        items.AddRange(BuiltinKeys.Select(b => new PickerItem { Text = b.Keys + "  " + b.Desc, Color = Tok.Comment }));
        var pick = RunPicker(new Picker { Title = "Prečice", Items = items, Preview = false });
        if (pick?.Tag is KeyNode n) n.Action?.Invoke();
    }

    private void CommandHistory()
    {
        var items = _cmdHistory.AsEnumerable().Reverse().Select(c => new PickerItem { Text = c }).ToList();
        var pick = RunPicker(new Picker { Title = "Istorija komandi", Items = items, Preview = false });
        if (pick != null) { _cmdHistory.Remove(pick.Text); _cmdHistory.Add(pick.Text); ExecuteEx(pick.Text); }
    }

    private void SearchHistory()
    {
        var items = _searchHistory.AsEnumerable().Reverse().Select(c => new PickerItem { Text = c }).ToList();
        var pick = RunPicker(new Picker { Title = "Istorija pretrage", Items = items, Preview = false });
        if (pick != null)
        {
            _lastSearch = pick.Text; _lastSearchFwd = true; _hlActive = true;
            var q = Search(pick.Text, true, Cur);
            if (q != null) { PushJump(); Cur = q.Value; }
        }
    }

    private void PickDiagnostics()
    {
        var items = new List<PickerItem>();
        foreach (var b in Buffers)
            foreach (var d in b.Diagnostics.OrderBy(d => d.Line))
                items.Add(new PickerItem
                {
                    Text = $"{(d.Severity == "Error" ? "E" : d.Severity == "Warning" ? "W" : "I")} {b.DisplayName}:{d.Line + 1}  {d.Message}",
                    Path = b.Path, Line = d.Line, Col = d.Col, Tag = b,
                    Color = d.Severity == "Error" ? Tok.Tag : d.Severity == "Warning" ? Tok.Attribute : Tok.Type
                });
        if (items.Count == 0) { Notify("Nema dijagnostike ✓"); return; }
        var pick = RunPicker(new Picker { Title = "Dijagnostika", Items = items });
        if (pick?.Tag is Buffer bb) { SwitchTo(bb); PushJump(); Cur = Buf.Clamp(new Pos(pick.Line, pick.Col)); CenterCursor(); }
    }

    private void PickTheme()
    {
        var items = Theme.Names.Select(n => new PickerItem { Text = n, Color = n == Opt.Theme ? Tok.Constant : Tok.Default }).ToList();
        var old = Opt.Theme;
        var pick = RunPicker(new Picker { Title = "Tema", Items = items, Preview = false });
        if (pick != null) { SetTheme(pick.Text); Notify($"Tema: {pick.Text} (za trajno: \"theme\" u konfiguraciji)"); }
    }

    private void PickDirectory()
    {
        var dirs = Files.EnumerateDirs(Cwd, Opt);
        var items = new List<PickerItem> { new() { Text = "..", Path = Path.GetDirectoryName(Cwd) } };
        items.AddRange(dirs.Select(d => new PickerItem { Text = d, Path = Path.Combine(Cwd, d) }));
        var pick = RunPicker(new Picker { Title = "Direktorijum", Items = items });
        if (pick?.Path != null) ExecuteEx("cd " + pick.Path);
    }

    private void SearchReplace()
    {
        var w = WordUnderCursor();
        var keys = new List<string> { ":" };
        keys.AddRange(Keys.Parse($"%s/\\<{w}\\>//gI"));
        keys.Add("<Left>"); keys.Add("<Left>"); keys.Add("<Left>");
        Ui.Input.Push(keys);
    }

    // =================================================================
    //  Fajlovi
    // =================================================================
    private void NewFile()
    {
        var name = Prompt("Novi fajl:", Buf?.Path != null ? ShortPath(Path.GetDirectoryName(Buf.Path)) + Path.DirectorySeparatorChar : "");
        if (string.IsNullOrWhiteSpace(name)) return;
        Open(ResolvePath(name));
    }

    private void OpenConfig()
    {
        var p = Options.DefaultPath();
        if (!File.Exists(p)) { Options.WriteDefault(p); Notify("Napravljena podrazumevana konfiguracija"); }
        Open(p);
    }

    private void ReloadConfigIfNeeded(Buffer b)
    {
        if (b.Path == null || !string.Equals(Path.GetFullPath(Options.DefaultPath()), b.Path, StringComparison.OrdinalIgnoreCase)) return;
        var o = Options.Load();
        if (o.LoadedFrom?.StartsWith("GREŠKA") == true) { Notify("Konfiguracija nije učitana: " + o.LoadedFrom, true); return; }
        Ui.Options = o;
        Ui.Theme = Theme.Get(o.Theme);
        Ui.Screen.Invalidate();
        _userMaps.Clear();
        LoadUserKeymaps();
        Notify("Konfiguracija ponovo učitana ✓");
    }

    private void RenameFile()
    {
        if (Buf.Path == null) throw new VimError("Bafer nema fajl");
        var name = Prompt("Preimenuj u:", ShortPath(Buf.Path));
        if (string.IsNullOrWhiteSpace(name)) return;
        var np = ResolvePath(name);
        if (File.Exists(Buf.Path)) { Directory.CreateDirectory(Path.GetDirectoryName(np)!); File.Move(Buf.Path, np); }
        Buf.Path = np; Buf.FileType = Syntax.DetectFileType(np);
        _index = null; _tree?.Refresh();
        Notify("Preimenovano u " + ShortPath(np));
    }

    private void GotoFile()
    {
        var s = Line;
        if (s.Length == 0) return;
        int c = Math.Min(Cur.C, s.Length - 1);
        bool PathChar(char ch) => !char.IsWhiteSpace(ch) && ch != '"' && ch != '\'' && ch != '(' && ch != ')' && ch != ',' && ch != ';' && ch != '<' && ch != '>' && ch != '[' && ch != ']';
        int st = c, en = c;
        while (st > 0 && PathChar(s[st - 1])) st--;
        while (en + 1 < s.Length && PathChar(s[en + 1])) en++;
        var word = s.Substring(st, en - st + 1);
        int line = -1;
        var lm = Regex.Match(word, @"^(.*?):(\d+)(?::\d+)?:?$");
        if (lm.Success && !Regex.IsMatch(word, @"^[A-Za-z]:[\\/]?$")) { word = lm.Groups[1].Value; line = int.Parse(lm.Groups[2].Value) - 1; }
        var dir = Buf.Path != null ? Path.GetDirectoryName(Buf.Path) : Cwd;
        word = Regex.Replace(word, @"\$PSScriptRoot", dir, RegexOptions.IgnoreCase);
        word = word.Replace("$HOME", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.OrdinalIgnoreCase);
        if (word.StartsWith("~")) word = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + word.Substring(1);
        foreach (var cand in new[] { Path.Combine(dir, word), Path.Combine(Cwd, word), word })
        {
            try
            {
                var full = Path.GetFullPath(cand);
                if (File.Exists(full)) { Open(full, line: line); return; }
                if (Directory.Exists(full)) { OpenTree(true); _tree.SetRoot(full); return; }
            }
            catch { }
        }
        throw new VimError("E447: Fajl nije pronađen: " + word);
    }

    private void OpenUrlUnderCursor()
    {
        foreach (Match m in Regex.Matches(Line, @"https?://[^\s""'<>)\]]+"))
        {
            if (Cur.C >= m.Index - 1 && Cur.C <= m.Index + m.Length || true)
            {
                try { Process.Start(new ProcessStartInfo(m.Value) { UseShellExecute = true }); Notify("Otvaram " + m.Value); }
                catch (Exception e) { Notify("Ne mogu da otvorim: " + e.Message, true); }
                return;
            }
        }
        Notify("Nema URL-a u liniji");
    }

    // =================================================================
    //  Definicije
    // =================================================================
    private void GotoDefinition()
    {
        var w = WordUnderCursor();
        if (string.IsNullOrEmpty(w)) return;
        bool isVar = false;
        {
            var s = Line; int i = s.IndexOf(w, Math.Max(0, Math.Min(Cur.C, s.Length) - w.Length), StringComparison.Ordinal);
            if (i > 0 && s[i - 1] == '$') isVar = true;
        }
        var esc = Regex.Escape(w);
        var rx = isVar
            ? new Regex(@"(\$" + esc + @"\s*=[^=]|\[\w+\]\s*\$" + esc + @"\b|^\s*param\s*\(.*\$" + esc + @"\b)", RegexOptions.IgnoreCase)
            : new Regex(@"^\s*(function|filter|workflow|class|enum|def|func|fn|interface|struct|type|configuration)\s+(\w+:)?" + esc + @"\b|^\s*(public|private|internal|protected|static|\s)+[\w<>\[\]]+\s+" + esc + @"\s*\(|\b(const|let|var)\s+" + esc + @"\s*=", RegexOptions.IgnoreCase);
        // trenutni bafer
        for (int l = 0; l < Buf.Count; l++)
        {
            var m = rx.Match(Buf[l]);
            if (m.Success) { PushJump(); Cur = new Pos(l, Math.Max(0, Buf[l].IndexOf(w, StringComparison.OrdinalIgnoreCase))); CenterCursor(); return; }
        }
        if (isVar) { Notify("Definicija nije pronađena"); return; }
        // ostali fajlovi u projektu
        var hits = new List<PickerItem>();
        foreach (var rel in ProjectFiles().Take(5000))
        {
            var full = Path.Combine(Cwd, rel);
            if (Buf.Path != null && string.Equals(full, Buf.Path, StringComparison.OrdinalIgnoreCase)) continue;
            var lines = Files.ReadLinesCached(full);
            if (lines == null) continue;
            for (int l = 0; l < lines.Length; l++)
                if (rx.IsMatch(lines[l])) hits.Add(new PickerItem { Text = $"{rel}:{l + 1}: {lines[l].Trim()}", Path = full, Line = l });
            if (hits.Count > 50) break;
        }
        if (hits.Count == 0) { Notify($"Definicija za '{w}' nije pronađena"); return; }
        var pick = hits.Count == 1 ? hits[0] : RunPicker(new Picker { Title = "Definicije: " + w, Items = hits });
        if (pick != null) Open(pick.Path, line: pick.Line);
    }

    // =================================================================
    //  Pomoć / popup
    // =================================================================
    private void Hover()
    {
        var w = WordUnderCursor();
        if (Buf.Diagnostics.Any(d => d.Line == Cur.L) && (string.IsNullOrEmpty(w) || Hooks.Help == null)) { LineDiagnostics(); return; }
        if (string.IsNullOrEmpty(w)) return;
        if (Hooks.Help == null) { Notify("Pomoć nije dostupna (pokreni psvim iz PowerShell modula)"); return; }
        string text;
        try { text = Hooks.Help(w); } catch (Exception e) { text = "Greška: " + e.Message; }
        if (string.IsNullOrWhiteSpace(text)) { Notify($"Nema pomoći za '{w}'"); return; }
        ShowPopup(w, text.Trim('\r', '\n'));
    }

    public void ShowPopup(string title, string text)
    {
        _hover = new HoverState { Title = title, Text = text ?? "", Diff = title == "diff" };
        try
        {
            while (true)
            {
                var k = NextKey();
                var h = _hover;
                int page = Math.Max(1, Ui.Screen.H - 8);
                switch (k)
                {
                    case "j": case "<Down>": case "<C-e>": h.Scroll++; continue;
                    case "k": case "<Up>": case "<C-y>": h.Scroll = Math.Max(0, h.Scroll - 1); continue;
                    case "<C-d>": case "<C-f>": case "<PageDown>": case " ": h.Scroll += page / 2; continue;
                    case "<C-u>": case "<C-b>": case "<PageUp>": h.Scroll = Math.Max(0, h.Scroll - page / 2); continue;
                    case "g": h.Scroll = 0; continue;
                    case "G": h.Scroll = int.MaxValue / 2; continue;
                    case "y":
                        SetReg(null, h.Text, false, true); Notify("Kopirano"); continue;
                    case "q": case "<Esc>": case "<CR>": case "<C-c>": return;
                    default: Ui.Input.Push(new[] { k }); return;
                }
            }
        }
        finally { _hover = null; }
    }

    private void DrawHover()
    {
        var sc = Ui.Screen; var th = Th; var h = _hover;
        int maxW = Math.Min(sc.W - 4, 110);
        var lines = Wrap(h.Text, maxW - 4);
        int w = Math.Min(maxW, Math.Max(30, lines.Max(l => l.Length) + 4));
        int maxH = sc.H - 4;
        int bh = Math.Min(maxH, lines.Count + 2);
        int x = (sc.W - w) / 2, y = Math.Max(1, (sc.H - 2 - bh) / 2);
        int inner = bh - 2;
        h.Scroll = Math.Clamp(h.Scroll, 0, Math.Max(0, lines.Count - inner));
        var bg = th.PopupBg;
        var title = h.Title + (lines.Count > inner ? $"  {h.Scroll + 1}-{Math.Min(lines.Count, h.Scroll + inner)}/{lines.Count}" : "");
        sc.Box(x, y, w, bh, new Style(th.Border, bg), new Style(th.Fg, bg), title, new Style(th.Orange, bg, Attr.Bold));
        for (int r = 0; r < inner; r++)
        {
            int i = h.Scroll + r;
            if (i >= lines.Count) break;
            var l = lines[i];
            var st = new Style(th.Fg, bg);
            if (h.Diff || l.StartsWith("diff --git"))
            {
                if (l.StartsWith("+")) st = new Style(th.Green, bg);
                else if (l.StartsWith("-")) st = new Style(th.Red, bg);
                else if (l.StartsWith("@@")) st = new Style(th.Cyan, bg);
                else if (l.StartsWith("diff") || l.StartsWith("index")) st = new Style(th.Comment, bg, Attr.Bold);
            }
            else if (l.Length > 0 && l == l.ToUpperInvariant() && l.Any(char.IsLetter) && !l.StartsWith(" ")) st = new Style(th.Blue, bg, Attr.Bold);
            else if (l.TrimStart().StartsWith("-") && h.Title != "Poruke") st = new Style(th.Yellow, bg);
            else if (l.StartsWith("E ")) st = new Style(th.Red, bg);
            sc.Put(x + 2, y + 1 + r, l, st, x + w - 1);
        }
    }

    // =================================================================
    //  Dijagnostika
    // =================================================================
    private void RunDiagnostics(bool withLint)
    {
        if (_diagBroken) return;
        var b = Buf;
        var list = new List<Diagnostic>();
        try
        {
            var text = b.GetAllText("\n");
            var raw = Hooks.Diagnose?.Invoke(text, b.Path) ?? Array.Empty<string>();
            list.AddRange(ParseDiags(raw, "parser"));
            if (withLint && Hooks.Lint != null)
            {
                var lr = ParseDiags(Hooks.Lint(text, b.Path) ?? Array.Empty<string>(), "PSScriptAnalyzer").ToList();
                _lint[b] = (b.Version, lr);
            }
        }
        catch (Exception e)
        {
            _diagBroken = true;
            Notify("Dijagnostika isključena: " + e.Message, true);
        }
        if (_lint.TryGetValue(b, out var li) && li.Version == b.Version) list.AddRange(li.Items);
        b.Diagnostics = list.Where(d => d.Line >= 0 && d.Line < b.Count).ToList();
        b.DiagVersion = b.Version;
    }

    private static IEnumerable<Diagnostic> ParseDiags(string[] raw, string src)
    {
        foreach (var r in raw)
        {
            if (string.IsNullOrEmpty(r)) continue;
            var p = r.Split('\t', 4);
            if (p.Length < 4) continue;
            if (!int.TryParse(p[0], out var l) || !int.TryParse(p[1], out var c)) continue;
            yield return new Diagnostic { Line = l - 1, Col = Math.Max(0, c - 1), Severity = p[2], Message = p[3], Source = src };
        }
    }

    private void GotoDiagnostic(int dir, string sev)
    {
        var ds = Buf.Diagnostics.Where(d => sev == null || d.Severity == sev).OrderBy(d => d.Line).ThenBy(d => d.Col).ToList();
        if (ds.Count == 0) { Notify("Nema dijagnostike"); return; }
        Diagnostic target = dir > 0
            ? ds.FirstOrDefault(d => d.Line > Cur.L || (d.Line == Cur.L && d.Col > Cur.C)) ?? ds[0]
            : ds.LastOrDefault(d => d.Line < Cur.L || (d.Line == Cur.L && d.Col < Cur.C)) ?? ds[^1];
        PushJump();
        Cur = Buf.Clamp(new Pos(target.Line, target.Col));
        Notify($"{target.Severity}: {target.Message}", target.Severity == "Error");
    }

    private void LineDiagnostics()
    {
        var ds = Buf.Diagnostics.Where(d => d.Line == Cur.L).ToList();
        if (ds.Count == 0) { Notify("Nema dijagnostike na ovoj liniji"); return; }
        ShowPopup("Dijagnostika", string.Join("\n", ds.Select(d => $"{d.Severity} [{d.Source}] {d.Line + 1}:{d.Col + 1}\n  {d.Message}")));
    }

    private void FormatBuffer(bool quiet = false)
    {
        if (Hooks.Format == null) { if (!quiet) Notify("Formatiranje nije dostupno (Install-Module PSScriptAnalyzer)", true); return; }
        if (Buf.FileType != "powershell") { if (!quiet) Notify("Formatiranje je podržano za PowerShell fajlove"); return; }
        string res;
        try { res = Hooks.Format(Buf.GetAllText("\n"), Buf.Path); }
        catch (Exception e) { Notify("Format: " + e.Message, true); return; }
        if (res == null) { if (!quiet) Notify("Formatiranje nije uspelo", true); return; }
        res = res.Replace("\r\n", "\n").TrimEnd('\n');
        if (res == Buf.GetAllText("\n")) { if (!quiet) Notify("Već je formatirano ✓"); return; }
        var snap = Buf.TakeSnapshot();
        var cur = Cur;
        Buf.ReplaceAll(new List<string>(res.Split('\n')));
        Cur = Buf.Clamp(cur);
        if (!quiet) Notify("Formatirano ✓");
    }

    // =================================================================
    //  Shell, terminal, git
    // =================================================================
    private string RunShellCapture(string cmd, string input = null)
    {
        try
        {
            if (Hooks.Shell != null) return Hooks.Shell(cmd, input);
            return RunProcess(Environment.ProcessPath ?? "pwsh", "-NoProfile -NoLogo -Command " + Quote(cmd), input);
        }
        catch (Exception e) { Notify("Greška: " + e.Message, true); return null; }
    }

    private void RunShell(string cmd, string input)
    {
        if (string.IsNullOrWhiteSpace(cmd)) return;
        var outp = RunShellCapture(cmd, input);
        if (outp == null) return;
        outp = outp.TrimEnd();
        if (outp.Length == 0) { Notify($":!{cmd} — završeno (nema izlaza)"); return; }
        if (!outp.Contains('\n') && outp.Length < 80) { Notify(outp); return; }
        ShowPopup(":!" + cmd, outp);
    }

    private void RunCurrentFile()
    {
        if (Buf.Path == null) throw new VimError("Bafer nema fajl");
        if (Buf.Modified) Save();
        if (Buf.FileType != "powershell") { RunShell("& " + Quote(Buf.Path), null); return; }
        RunShell($"& {{ . '{Buf.Path.Replace("'", "''")}' }} *>&1 | Out-String -Width 200", null);
    }

    private static string Quote(string s) => "\"" + (s ?? "").Replace("\"", "\\\"") + "\"";

    private static string RunProcess(string exe, string args, string input = null, string cwd = null)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            RedirectStandardInput = input != null, CreateNoWindow = true,
            WorkingDirectory = cwd ?? Directory.GetCurrentDirectory(),
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        using var p = Process.Start(psi)!;
        if (input != null) { p.StandardInput.Write(input); p.StandardInput.Close(); }
        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(15000)) { try { p.Kill(true); } catch { } return "(prekinuto posle 15s)"; }
        return outTask.Result + errTask.Result;
    }

    /// <summary>Privremeno napušta editor i pokreće interaktivni program (lazygit, pwsh...).</summary>
    public void RunExternal(string exe, string args)
    {
        if (Ui.Term is FakeTerminal) return;
        Ui.Term.Leave();
        try
        {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, WorkingDirectory = Cwd };
            using var p = Process.Start(psi);
            p?.WaitForExit();
        }
        catch (Exception e) { Notify($"Ne mogu da pokrenem {exe}: {e.Message}", true); }
        finally
        {
            Ui.Term.Enter();
            Ui.Screen.Invalidate();
            // osveži bafere koji su promenjeni spolja
            foreach (var b in Buffers.Where(b => b.Path != null && !b.Modified && File.Exists(b.Path)).ToList())
            {
                try
                {
                    var text = File.ReadAllText(b.Path).Replace("\r\n", "\n").TrimEnd('\n');
                    if (text != b.GetAllText("\n").TrimEnd('\n'))
                    {
                        var nb = Buffer.Load(b.Path); nb.Cursor = nb.Clamp(b.Cursor);
                        int i = Buffers.IndexOf(b); Buffers[i] = nb; _hl.Remove(b);
                        if (Buf == b) Buf = nb;
                    }
                }
                catch { }
            }
            _index = null;
            _tree?.Refresh();
        }
    }

    private void OpenTerminal()
    {
        var shell = Environment.ProcessPath;
        if (shell == null || !Path.GetFileNameWithoutExtension(shell).StartsWith("pwsh", StringComparison.OrdinalIgnoreCase))
            shell = Files.FindInPath("pwsh") ?? Files.FindInPath("powershell") ?? "pwsh";
        if (Ui.Term is not FakeTerminal) Ui.Term.Write("");
        RunExternal(shell, "-NoLogo -NoExit -Command \"Write-Host 'psvim terminal — ukucaj exit za povratak' -ForegroundColor Cyan\"");
    }

    private void Lazygit()
    {
        var lg = Files.FindInPath("lazygit");
        if (lg == null) { Notify("lazygit nije instaliran (winget install lazygit)", true); return; }
        RunExternal(lg, "");
    }

    private string Git(string args)
    {
        var git = Files.FindInPath("git") ?? throw new VimError("git nije pronađen u PATH-u");
        var dir = Buf?.Path != null ? Path.GetDirectoryName(Buf.Path) : Cwd;
        return RunProcess(git, args, null, dir);
    }

    private void GitPopup(string title, string args)
    {
        var o = Git(args).TrimEnd();
        if (o.Length == 0) { Notify("Nema izmena"); return; }
        ShowPopup(title, o);
    }

    private void GitStatus()
    {
        var root = Git("rev-parse --show-toplevel").Trim();
        if (root.StartsWith("fatal")) { Notify("Nije git repozitorijum", true); return; }
        var o = Git("status --porcelain");
        var items = new List<PickerItem>();
        foreach (var l in o.Replace("\r", "").Split('\n'))
        {
            if (l.Length < 4) continue;
            var st = l.Substring(0, 2); var f = l.Substring(3).Trim().Trim('"');
            if (f.Contains(" -> ")) f = f.Substring(f.IndexOf(" -> ") + 4);
            items.Add(new PickerItem
            {
                Text = $"{st} {f}", Path = Path.GetFullPath(Path.Combine(root, f)),
                Color = st.Contains('?') ? Tok.Comment : st.Contains('D') ? Tok.Tag : st.Contains('A') ? Tok.String : Tok.Attribute
            });
        }
        if (items.Count == 0) { Notify("Radni direktorijum je čist ✓"); return; }
        var pick = RunPicker(new Picker { Title = "Git status", Items = items });
        if (pick != null && File.Exists(pick.Path)) Open(pick.Path);
    }

    private void GitBlameLine()
    {
        if (Buf.Path == null) return;
        var o = Git($"log -1 --format=\"%h %an, %ar — %s\" -L {Cur.L + 1},{Cur.L + 1}:{Quote(Path.GetFileName(Buf.Path))}");
        var first = o.Split('\n').FirstOrDefault()?.Trim();
        Notify(string.IsNullOrEmpty(first) ? "Nema git istorije" : first);
    }

    // =================================================================
    //  Flash
    // =================================================================
    private const string FlashLabels = "asdfghjklqwertyuiopzxcvbnm";

    private Pos? Flash()
    {
        var pat = "";
        var labeled = new Dictionary<char, Pos>();
        try
        {
            while (true)
            {
                _showCmd = "flash: " + pat;
                var k = NextKey();
                if (k == "<Esc>" || k == "<C-c>") return null;
                if (pat.Length > 0 && k.Length == 1 && labeled.TryGetValue(k[0], out var target)) return target;
                if (k == "<BS>") { if (pat.Length == 0) return null; pat = pat.Substring(0, pat.Length - 1); }
                else if (k == "<CR>") { if (_flash?.Count > 0) return _flash[0].P; return null; }
                else if (Keys.IsPrintable(k)) pat += k;
                else return null;
                labeled.Clear();
                if (pat.Length == 0) { _flash = null; continue; }
                var matches = FlashMatches(pat);
                if (matches.Count == 0) { _flash = null; return null; }
                // oznake koje ne smeju da se poklope sa nastavkom šablona
                var banned = new HashSet<char>();
                foreach (var m in matches)
                {
                    var s = Buf[m.L];
                    int ni = m.C + pat.Length;
                    if (ni < s.Length) banned.Add(char.ToLowerInvariant(s[ni]));
                }
                var labels = FlashLabels.Where(c => !banned.Contains(c)).ToList();
                var sorted = matches.OrderBy(m => Math.Abs(m.L - Cur.L) * 1000 + Math.Abs(m.C - Cur.C)).ToList();
                _flash = new List<(Pos, int, char)>();
                for (int i = 0; i < sorted.Count; i++)
                {
                    char lab = i < labels.Count ? labels[i] : '\0';
                    if (lab != '\0') labeled[lab] = sorted[i];
                    _flash.Add((sorted[i], pat.Length, lab));
                }
            }
        }
        finally { _flash = null; _showCmd = ""; }
    }

    private List<Pos> FlashMatches(string pat)
    {
        var res = new List<Pos>();
        bool cs = pat.Any(char.IsUpper);
        var cmp = cs ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int end = Math.Min(Buf.Count, Buf.Top + Math.Max(1, _textH));
        for (int l = Buf.Top; l < end; l++)
        {
            var s = Buf[l];
            int i = 0;
            while ((i = s.IndexOf(pat, i, cmp)) >= 0)
            {
                if (!(l == Cur.L && i == Cur.C)) res.Add(new Pos(l, i));
                i++;
            }
        }
        return res;
    }

    // =================================================================
    //  Stablo fajlova
    // =================================================================
    public void OpenTree(bool focus)
    {
        _tree ??= new FileTree(Cwd, Opt);
        if (Buf?.Path != null) _tree.Reveal(Buf.Path);
        _treeOpen = true;
        _treeFocus = focus;
    }

    private void ToggleTree()
    {
        if (_treeOpen) { _treeOpen = false; _treeFocus = false; return; }
        OpenTree(true);
    }

    private void TreeCommand()
    {
        var t = _tree;
        var k = NextKey();
        var n = t.Current;
        int half = Math.Max(1, _textH / 2);
        switch (k)
        {
            case "j": case "<Down>": t.Sel = Math.Min(t.Visible.Count - 1, t.Sel + 1); break;
            case "k": case "<Up>": t.Sel = Math.Max(0, t.Sel - 1); break;
            case "<C-d>": t.Sel = Math.Min(t.Visible.Count - 1, t.Sel + half); break;
            case "<C-u>": t.Sel = Math.Max(0, t.Sel - half); break;
            case "G": t.Sel = t.Visible.Count - 1; break;
            case "g": if (NextKey() == "g") t.Sel = 0; break;
            case "l": case "<CR>": case "o": case "<Right>":
                if (n == null) break;
                if (n.IsDir) t.Toggle(n);
                else { Open(n.Path, restorePos: true); _treeFocus = false; }
                break;
            case "h": case "<Left>": t.Collapse(); break;
            case "-": case "<BS>": { var p = Path.GetDirectoryName(t.Root.TrimEnd('\\', '/')); if (p != null) t.SetRoot(p); break; }
            case ".": case "C": if (n?.IsDir == true) { t.SetRoot(n.Path); } break;
            case "a": case "A":
                {
                    var dir = t.TargetDir();
                    var name = Prompt(k == "A" ? "Novi folder:" : "Novi fajl (završi sa / za folder):");
                    if (string.IsNullOrWhiteSpace(name)) break;
                    var full = Path.GetFullPath(Path.Combine(dir, name));
                    try
                    {
                        if (k == "A" || name.EndsWith("/") || name.EndsWith("\\")) Directory.CreateDirectory(full);
                        else
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                            if (!File.Exists(full)) File.WriteAllText(full, "");
                        }
                        t.Reveal(full.TrimEnd('\\', '/'));
                        _index = null;
                        if (File.Exists(full)) { Open(full); _treeFocus = false; }
                    }
                    catch (Exception e) { Notify("Greška: " + e.Message, true); }
                    break;
                }
            case "d":
                {
                    if (n == null) break;
                    if (!Confirm($"Obriši {n.Name}{(n.IsDir ? "/ (sa sadržajem)" : "")}?")) break;
                    try
                    {
                        if (n.IsDir) Directory.Delete(n.Path, true); else File.Delete(n.Path);
                        foreach (var b in Buffers.Where(b => b.Path != null && b.Path.StartsWith(n.Path, StringComparison.OrdinalIgnoreCase)).ToList())
                            CloseBuffer(b, true);
                        t.Refresh(); _index = null;
                        Notify("Obrisano: " + n.Name);
                    }
                    catch (Exception e) { Notify("Greška: " + e.Message, true); }
                    break;
                }
            case "r":
                {
                    if (n == null) break;
                    var name = Prompt("Preimenuj u:", n.Name);
                    if (string.IsNullOrWhiteSpace(name) || name == n.Name) break;
                    var np = Path.Combine(Path.GetDirectoryName(n.Path)!, name);
                    try
                    {
                        if (n.IsDir) Directory.Move(n.Path, np); else File.Move(n.Path, np);
                        foreach (var b in Buffers.Where(b => b.Path != null && b.Path.StartsWith(n.Path, StringComparison.OrdinalIgnoreCase)))
                        { b.Path = np + b.Path.Substring(n.Path.Length); b.FileType = Syntax.DetectFileType(b.Path); }
                        t.Reveal(np); _index = null;
                    }
                    catch (Exception e) { Notify("Greška: " + e.Message, true); }
                    break;
                }
            case "c":
                {
                    if (n == null || n.IsDir) break;
                    var name = Prompt("Kopiraj u:", n.Name);
                    if (string.IsNullOrWhiteSpace(name)) break;
                    var np = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(n.Path)!, name));
                    try { File.Copy(n.Path, np); t.Reveal(np); _index = null; } catch (Exception e) { Notify("Greška: " + e.Message, true); }
                    break;
                }
            case "y": case "Y":
                if (n == null) break;
                {
                    var p = k == "y" ? Path.GetRelativePath(Cwd, n.Path) : n.Path;
                    SetReg(null, p, false, true);
                    TrySetClipboard(p, false);
                    Notify("Kopirano: " + p);
                }
                break;
            case "H": Opt.ShowHidden = !Opt.ShowHidden; t.Refresh(); _index = null; Notify("Skriveni fajlovi: " + (Opt.ShowHidden ? "prikazani" : "sakriveni")); break;
            case "R": t.Refresh(); _index = null; break;
            case "q": _treeOpen = false; _treeFocus = false; break;
            case "<Esc>": break;
            case "<C-l>": case "<Tab>": case "<C-w>l": case "<C-w>w": _treeFocus = false; break;
            case "?":
                ShowPopup("Stablo — prečice", "j/k  kretanje            l/Enter  otvori / raširi\nh    skupi / roditelj    a  novi fajl (/ = folder)   A novi folder\nd    obriši              r  preimenuj               c  kopiraj\ny    kopiraj rel. putanju Y  apsolutnu putanju\nH    skriveni fajlovi    R  osveži\n-    folder iznad        .  postavi kao koren\nq    zatvori             <C-l>  fokus na editor");
                break;
            case ":": { var cmd = ReadCmd(":"); if (cmd != null) ExecuteEx(cmd); break; }
            case "/": FindFiles(); break;
            default:
                if (k == Opt.Leader)
                {
                    var act = WhichKey.Run(Ui, _leader, "␣ ", DrawBase, Opt.WhichKeyDelay);
                    act?.Invoke();
                }
                else if (k == "<C-s>") Save();
                break;
        }
    }
}
