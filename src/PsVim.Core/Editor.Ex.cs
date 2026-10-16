using System.Text;
using System.Text.RegularExpressions;

namespace PsVim;

public sealed partial class Editor
{
    private string _lastSubPat, _lastSubRep, _lastSubFlags;

    public void ExecuteEx(string cmdline)
    {
        var cmd = cmdline.Trim();
        while (cmd.StartsWith(":")) cmd = cmd.Substring(1).TrimStart();
        if (cmd.Length == 0) return;

        // opseg
        int i = 0;
        int? l1 = null, l2 = null;
        bool hasRange = false;
        if (cmd[0] == '%') { l1 = 0; l2 = Buf.Count - 1; i = 1; hasRange = true; }
        else
        {
            var a = ParseAddr(cmd, ref i);
            if (a != null)
            {
                hasRange = true; l1 = a; l2 = a;
                if (i < cmd.Length && (cmd[i] == ',' || cmd[i] == ';'))
                {
                    i++;
                    var b2 = ParseAddr(cmd, ref i);
                    l2 = b2 ?? l1;
                }
            }
        }
        var rest = cmd.Substring(i).TrimStart();
        if (hasRange)
        {
            l1 = Compat.Clamp(l1.Value, 0, Buf.Count - 1); l2 = Compat.Clamp(l2.Value, 0, Buf.Count - 1);
            if (l2 < l1) (l1, l2) = (l2, l1);
        }
        if (rest.Length == 0)
        {
            if (hasRange) { PushJump(); Cur = new Pos(l2.Value, FirstNonBlankIndex(l2.Value)); }
            return;
        }

        // ime komande i argumenti
        int j = 0;
        if (!char.IsLetter(rest[0])) j = 1;
        else while (j < rest.Length && char.IsLetter(rest[j])) j++;
        var name = rest.Substring(0, j);
        bool bang = j < rest.Length && rest[j] == '!';
        if (bang) j++;
        var arg = rest.Substring(j).Trim();
        int from = l1 ?? Cur.L, to = l2 ?? Cur.L;

        switch (name)
        {
            case "w": case "write": case "up": case "update": case "sav": case "saveas":
                if (name is "up" or "update" && !Buf.Modified) return;
                if (arg.StartsWith("!") || (name is "w" or "write" && rest.Length > j && rest.Substring(j).TrimStart().StartsWith("!")))
                { RunShell(arg.TrimStart('!'), Buf.GetAllText()); return; }
                Save(arg.Length > 0 ? ResolvePath(arg) : null);
                return;
            case "wq": case "x": case "xit": case "exi": case "exit":
                if (name == "wq" || Buf.Modified) Save(arg.Length > 0 ? ResolvePath(arg) : null);
                QuitBuffer(bang);
                return;
            case "wa": case "wall":
                foreach (var b in Buffers.Where(b => b.Modified && b.Path != null)) SaveBuffer(b);
                return;
            case "wqa": case "wqall": case "xa": case "xall":
                foreach (var b in Buffers.Where(b => b.Modified && b.Path != null)) SaveBuffer(b);
                QuitAll(true);
                return;
            case "q": case "quit": case "clo": case "close":
                QuitBuffer(bang);
                return;
            case "qa": case "qall": case "quita": case "quitall":
                QuitAll(bang);
                return;
            case "cq": ExitCode = 1; QuitAll(true); return;
            case "e": case "edit":
                if (arg.Length == 0)
                {
                    if (Buf.Path == null) return;
                    if (Buf.Modified && !bang) throw new VimError("E37: Izmene nisu sačuvane (dodaj ! da odbaciš)");
                    var p = Buf.Cursor; var path = Buf.Path;
                    var nb = Buffer.Load(path);
                    nb.Cursor = nb.Clamp(p);
                    int idx = Buffers.IndexOf(Buf);
                    Buffers[idx] = nb; _hl.Remove(Buf); Buf = nb;
                    Notify($"\"{ShortPath(path)}\" ponovo učitan");
                    return;
                }
                Open(ResolvePath(arg));
                return;
            case "ene": case "enew": case "new":
                {
                    var b = new Buffer { FileType = "text", Eol = Compat.IsWindows ? "\r\n" : "\n" };
                    Buffers.Add(b); SwitchTo(b);
                    return;
                }
            case "bn": case "bnext": CycleBuffer(1); return;
            case "bp": case "bprevious": case "bN": case "bNext": CycleBuffer(-1); return;
            case "bd": case "bdelete": case "bw": case "bwipeout":
                {
                    var b = Buf;
                    if (arg.Length > 0) b = FindBuffer(arg) ?? throw new VimError("E94: Nema bafera " + arg);
                    CloseBuffer(b, bang);
                    return;
                }
            case "b": case "buffer":
                {
                    var b = FindBuffer(arg) ?? throw new VimError("E94: Nema bafera " + arg);
                    SwitchTo(b); return;
                }
            case "ls": case "buffers": case "files": PickBuffers(); return;
            case "set": case "se":
                foreach (var part in Regex.Split(arg, @"\s+").Where(x => x.Length > 0))
                {
                    var err = Opt.Set(part);
                    if (err != null) { if (err.Contains("=")) Notify(err); else throw new VimError(err); }
                }
                Ui.Theme = Theme.Get(Opt.Theme);
                return;
            case "noh": case "nohlsearch": _hlActive = false; return;
            case "colo": case "colorscheme":
                if (arg.Length == 0) { Notify(Opt.Theme); return; }
                SetTheme(arg); return;
            case "s": case "substitute": case "&": case "&&":
                Substitute(from, to, name == "&" || name == "&&" ? null : rest.Substring(name.Length));
                _repeatable = false;
                return;
            case "g": case "global": case "v": case "vglobal":
                Global(hasRange ? from : 0, hasRange ? to : Buf.Count - 1, arg, name.StartsWith("v") || bang);
                return;
            case "norm": case "normal":
                {
                    var keys = Keys.Parse(arg);
                    for (int l = from; l <= to && l < Buf.Count; l++)
                    {
                        Cur = new Pos(l, 0);
                        Ui.Input.Push(keys.Concat(new[] { "<Esc>" }));
                        while (Ui.Input.HasPending) ExecNormalSafe();
                    }
                    return;
                }
            case "d": case "delete":
                {
                    char? reg = arg.Length > 0 ? arg[0] : null;
                    SetReg(reg, string.Join("\n", Buf.GetLines(from, to)), true, false);
                    Buf.DeleteLines(from, to);
                    Cur = new Pos(Math.Min(from, Buf.Count - 1), 0);
                    return;
                }
            case "y": case "yank":
                SetReg(arg.Length > 0 ? arg[0] : null, string.Join("\n", Buf.GetLines(from, to)), true, true);
                return;
            case "m": case "move":
                {
                    int dest = ParseAddrStr(arg);
                    var lines = Buf.GetLines(from, to);
                    Buf.DeleteLines(from, to);
                    if (dest > to) dest -= lines.Count;
                    Buf.InsertLines(Compat.Clamp(dest + 1, 0, Buf.Count), lines);
                    Cur = new Pos(Compat.Clamp(dest + lines.Count, 0, Buf.Count - 1), 0);
                    return;
                }
            case "t": case "co": case "copy":
                {
                    int dest = ParseAddrStr(arg);
                    var lines = Buf.GetLines(from, to);
                    Buf.InsertLines(Compat.Clamp(dest + 1, 0, Buf.Count), lines);
                    return;
                }
            case "sor": case "sort":
                {
                    if (!hasRange) { from = 0; to = Buf.Count - 1; }
                    var lines = Buf.GetLines(from, to);
                    bool ic = arg.Contains('i'), num = arg.Contains('n'), uniq = arg.Contains('u');
                    IEnumerable<string> sorted = num
                        ? lines.OrderBy(x => { var m = Regex.Match(x, @"-?\d+"); return m.Success ? long.Parse(m.Value) : long.MinValue; })
                        : lines.OrderBy(x => x, ic ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                    if (bang) sorted = sorted.Reverse();
                    var list = sorted.ToList();
                    if (uniq) list = list.Distinct(ic ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToList();
                    Buf.DeleteLines(from, to);
                    if (Buf.Count == 1 && Buf[0].Length == 0 && from == 0) { Buf.ReplaceAll(list); }
                    else Buf.InsertLines(from, list);
                    return;
                }
            case "ret": case "retab":
                {
                    for (int l = 0; l < Buf.Count; l++)
                    {
                        var s = Buf[l]; var ind = Buffer.IndentOf(s);
                        int cols = 0; foreach (var ch in ind) cols = ch == '\t' ? (cols / Opt.TabStop + 1) * Opt.TabStop : cols + 1;
                        var ni = Opt.ExpandTab ? new string(' ', cols) : new string('\t', cols / Opt.TabStop) + new string(' ', cols % Opt.TabStop);
                        if (ni != ind) Buf.SetLine(l, ni + s.Substring(ind.Length));
                    }
                    return;
                }
            case "u": case "undo": _skipUndo = true; UndoOnce(); return;
            case "red": case "redo": _skipUndo = true; RedoOnce(); return;
            case "h": case "help": ShowHelp(); return;
            case "cd":
                {
                    var p = arg.Length == 0 ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : ResolvePath(arg);
                    if (!Directory.Exists(p)) throw new VimError("E344: Ne postoji direktorijum " + arg);
                    Cwd = Path.GetFullPath(p); _index = null;
                    if (_tree != null) _tree.SetRoot(Cwd);
                    Notify(Cwd); return;
                }
            case "pw": case "pwd": Notify(Cwd); return;
            case "redr": case "redraw": Ui.Screen.Invalidate(); return;
            case "r": case "read":
                if (arg.StartsWith("!"))
                {
                    var outp = RunShellCapture(arg.Substring(1));
                    if (outp == null) return;
                    Buf.InsertLines(to + 1, outp.Replace("\r\n", "\n").TrimEnd('\n').Split('\n'));
                    return;
                }
                {
                    var p = ResolvePath(arg);
                    if (!File.Exists(p)) throw new VimError("E484: Ne mogu da otvorim " + arg);
                    Buf.InsertLines(to + 1, File.ReadAllText(p).Replace("\r\n", "\n").TrimEnd('\n').Split('\n'));
                    return;
                }
            case "!":
                {
                    var c = rest.Substring(1).Trim();
                    if (hasRange)
                    {
                        // filtriraj linije kroz komandu
                        var input = string.Join("\n", Buf.GetLines(from, to));
                        var outp = RunShellCapture(c, input);
                        if (outp == null) return;
                        Buf.DeleteLines(from, to);
                        Buf.InsertLines(from, outp.Replace("\r\n", "\n").TrimEnd('\n').Split('\n'));
                        return;
                    }
                    RunShell(c, null);
                    return;
                }
            case "Ex": case "Explore": case "Neotree": case "NvimTreeToggle": ToggleTree(); return;
            case "Format": case "Fmt": FormatBuffer(); return;
            case "Lazy": case "LazyExtras": case "Mason":
                Notify($"psvim {Version}: svi \"pluginovi\" su već ugrađeni (picker, neo-tree, which-key, flash, lualine, bufferline, noice, mini.pairs, mini.comment, blink.cmp)");
                return;
            case "checkhealth": case "health": CheckHealth(); return;
            case "mes": case "messages": ShowPopup("Poruke", string.Join("\n", _messages.Skip(Math.Max(0, _messages.Count - 60)))); return;
            case "reg": case "registers": case "di": case "display":
                {
                    var sb = new StringBuilder();
                    foreach (var kv in _regs.OrderBy(x => x.Key))
                        sb.Append('"').Append(kv.Key).Append("  ").Append(kv.Value.Linewise ? "L " : "c ").Append(kv.Value.Text.Replace("\n", "⏎").Substring(0, Math.Min(70, kv.Value.Text.Replace("\n", "⏎").Length))).Append('\n');
                    ShowPopup("Registri", sb.Length == 0 ? "(prazno)" : sb.ToString().TrimEnd());
                    return;
                }
            case "marks":
                ShowPopup("Oznake", string.Join("\n", Buf.Marks.OrderBy(x => x.Key).Select(x => $"{x.Key}  {x.Value.L + 1}:{x.Value.C + 1}  {Buf[Math.Min(x.Value.L, Buf.Count - 1)].Trim()}")));
                return;
            case "ter": case "terminal": case "sh": case "shell": OpenTerminal(); return;
            case "grep": LiveGrep(arg.Length > 0 ? arg : null); return;
            case "ol": case "oldfiles": RecentFiles(); return;
            case "diagnostics": case "Trouble": PickDiagnostics(); return;
            case "on": case "only": _treeOpen = false; _treeFocus = false; return;
            default:
                if (int.TryParse(name, out _)) return;
                throw new VimError("E492: Nije komanda editora: " + rest);
        }
    }

    private void ExecNormalSafe()
    {
        try { NormalCommandInner(); }
        catch (VimError) { }
    }

    private void NormalCommandInner()
    {
        Mode = Mode.Normal;
        ExecNormal();
        if (Mode == Mode.Normal) ClampNormal();
    }

    private Buffer FindBuffer(string arg)
    {
        if (int.TryParse(arg, out var num)) return Buffers.FirstOrDefault(b => b.Number == num);
        return Buffers.FirstOrDefault(b => b.DisplayName.Equals(arg, StringComparison.OrdinalIgnoreCase))
            ?? Buffers.FirstOrDefault(b => b.DisplayName.Contains(arg, StringComparison.OrdinalIgnoreCase));
    }

    private int? ParseAddr(string s, ref int i)
    {
        int? baseL = null;
        if (i >= s.Length) return null;
        char c = s[i];
        if (c == '.') { baseL = Cur.L; i++; }
        else if (c == '$') { baseL = Buf.Count - 1; i++; }
        else if (char.IsDigit(c))
        {
            int st = i; while (i < s.Length && char.IsDigit(s[i])) i++;
            baseL = int.Parse(s.Substring(st, i - st)) - 1;
        }
        else if (c == '\'' && i + 1 < s.Length)
        {
            char m = s[i + 1]; i += 2;
            if (m == '<' && _lastVisual != null) baseL = Math.Min(_lastVisual.Value.A.L, _lastVisual.Value.B.L);
            else if (m == '>' && _lastVisual != null) baseL = Math.Max(_lastVisual.Value.A.L, _lastVisual.Value.B.L);
            else if (Buf.Marks.TryGetValue(m, out var mp)) baseL = mp.L;
            else throw new VimError("E20: Oznaka nije postavljena");
        }
        else if (c == '+' || c == '-') baseL = Cur.L;
        else return null;
        while (i < s.Length && (s[i] == '+' || s[i] == '-'))
        {
            int sign = s[i] == '+' ? 1 : -1; i++;
            int st = i; while (i < s.Length && char.IsDigit(s[i])) i++;
            int v = i > st ? int.Parse(s.Substring(st, i - st)) : 1;
            baseL += sign * v;
        }
        return baseL;
    }

    private int ParseAddrStr(string s)
    {
        int i = 0;
        if (s.Trim() == "0") return -1;
        return ParseAddr(s.Trim(), ref i) ?? throw new VimError("E14: Neispravna adresa");
    }

    private string ResolvePath(string p)
    {
        p = p.Trim().Trim('"');
        if (p.StartsWith("~")) p = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + p.Substring(1);
        if (p == "%" && Buf.Path != null) return Buf.Path;
        var baseDir = Cwd;
        return Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(baseDir, p));
    }

    private void QuitBuffer(bool force)
    {
        if (_treeFocus && _treeOpen) { _treeOpen = false; _treeFocus = false; return; }
        var real = Buffers.Where(b => !b.IsScratch).ToList();
        if (Buf.IsScratch && Buffers.Count > 1 && Buf.FileType != "dashboard") { CloseBuffer(Buf, true); return; }
        if (real.Count <= 1 || Buf.IsScratch)
        {
            QuitAll(force);
            return;
        }
        if (Buf.Modified && !force) throw new VimError("E37: Izmene nisu sačuvane (dodaj ! da odbaciš)");
        // kao u vimu bez prozora: :q zatvara editor ako ima još bafera? LazyVim: zatvara prozor => izlaz
        var others = real.Where(b => b != Buf && b.Modified).ToList();
        if (others.Count > 0 && !force) throw new VimError($"E162: Bafer \"{others[0].DisplayName}\" nije sačuvan");
        QuitAll(force);
    }

    private void QuitAll(bool force)
    {
        if (!force)
        {
            var mod = Buffers.FirstOrDefault(b => b.Modified && !b.IsScratch);
            if (mod != null) { SwitchTo(mod); throw new VimError($"E37: Bafer \"{mod.DisplayName}\" nije sačuvan (dodaj ! da odbaciš)"); }
        }
        Quit = true;
    }

    public void Save(string path = null)
    {
        if (Buf.IsScratch && path == null) throw new VimError("Ovaj bafer nema fajl (koristi :w ime)");
        if (Buf.Path == null && path == null)
        {
            var name = Prompt("Sačuvaj kao:");
            if (string.IsNullOrWhiteSpace(name)) return;
            path = ResolvePath(name);
        }
        if (path != null) { Buf.IsScratch = false; Buf.ReadOnly = false; }
        if (Opt.AutoFormatOnSave) FormatBuffer(quiet: true);
        SaveBuffer(Buf, path);
    }

    private void SaveBuffer(Buffer b, string path = null)
    {
        try { b.Save(path); }
        catch (Exception e) { throw new VimError("E212: Ne mogu da sačuvam: " + e.Message); }
        long bytes = 0; try { bytes = new FileInfo(b.Path).Length; } catch { }
        Notify($"\"{ShortPath(b.Path)}\" {b.Count}L, {bytes}B sačuvano");
        _index = null;
        ReloadConfigIfNeeded(b);
        if (b == Buf && Hooks.Lint != null && b.FileType == "powershell" && Opt.Diagnostics) RunDiagnostics(true);
        _tree?.Refresh();
    }

    // ---------------- :s ----------------
    private void Substitute(int from, int to, string spec)
    {
        string pat, rep, flags;
        if (spec == null || spec.Trim().Length == 0 || char.IsLetterOrDigit(spec.TrimStart()[0]))
        {
            if (_lastSubPat == null) throw new VimError("E35: Nema prethodne zamene");
            pat = _lastSubPat; rep = _lastSubRep; flags = (spec ?? "").Trim() + (_lastSubFlags ?? "");
        }
        else
        {
            spec = spec.TrimStart();
            char d = spec[0];
            var parts = SplitDelim(spec.Substring(1), d);
            pat = parts.Count > 0 ? parts[0] : "";
            rep = parts.Count > 1 ? parts[1] : "";
            flags = parts.Count > 2 ? parts[2] : "";
            if (pat.Length == 0) pat = _lastSearch ?? throw new VimError("E35: Nema prethodnog šablona");
            _lastSubPat = pat; _lastSubRep = rep; _lastSubFlags = flags;
        }
        bool global = flags.Contains('g');
        bool ic = flags.Contains('i') || (!flags.Contains('I') && Opt.IgnoreCase && !(Opt.SmartCase && pat.Any(char.IsUpper)));
        var rx = new Regex(VimToNetRegex(pat), ic ? RegexOptions.IgnoreCase : RegexOptions.None);
        var netRep = VimToNetReplacement(rep);
        int count = 0, linesChanged = 0, lastLine = -1;
        for (int l = from; l <= to && l < Buf.Count; l++)
        {
            var s = Buf[l];
            int localCount = 0;
            var ns = rx.Replace(s, m => { localCount++; return m.Result(netRep); }, global ? -1 : 1);
            if (localCount > 0 && ns != s)
            {
                count += localCount; linesChanged++; lastLine = l;
                if (ns.Contains('\n') || ns.Contains('\r'))
                {
                    var parts = ns.Replace("\r", "\n").Split('\n');
                    Buf.SetLine(l, parts[0]);
                    Buf.InsertLines(l + 1, parts.Skip(1));
                    to += parts.Length - 1; l += parts.Length - 1;
                }
                else Buf.SetLine(l, ns);
            }
        }
        if (count == 0) throw new VimError("E486: Šablon nije pronađen: " + pat);
        _lastSearch = pat; _hlActive = Opt.HlSearch;
        Cur = new Pos(lastLine, FirstNonBlankIndex(lastLine));
        if (linesChanged > 1) Notify($"{count} zamena u {linesChanged} linija");
    }

    private static List<string> SplitDelim(string s, char d)
    {
        var res = new List<string>(); var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length && s[i + 1] == d) { sb.Append(d); i++; continue; }
            if (s[i] == d) { res.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(s[i]);
        }
        res.Add(sb.ToString());
        return res;
    }

    /// <summary>Osnovna konverzija vim regex sintakse u .NET (\&lt; \&gt; \( \) \| \{ \} \v).</summary>
    public static string VimToNetRegex(string p)
    {
        if (p.StartsWith("\\v")) return p.Substring(2).Replace("<", "\\b").Replace(">", "\\b");
        var sb = new StringBuilder();
        for (int i = 0; i < p.Length; i++)
        {
            if (p[i] == '\\' && i + 1 < p.Length)
            {
                char n = p[i + 1];
                switch (n)
                {
                    case '<': case '>': sb.Append("\\b"); i++; continue;
                    case '(': sb.Append('('); i++; continue;
                    case ')': sb.Append(')'); i++; continue;
                    case '|': sb.Append('|'); i++; continue;
                    case '{': sb.Append('{'); i++; continue;
                    case '}': sb.Append('}'); i++; continue;
                    case '+': sb.Append('+'); i++; continue;
                    case '?': sb.Append('?'); i++; continue;
                    case '=': sb.Append('?'); i++; continue;
                }
            }
            sb.Append(p[i]);
        }
        return sb.ToString();
    }

    private static string VimToNetReplacement(string r)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < r.Length; i++)
        {
            char c = r[i];
            if (c == '\\' && i + 1 < r.Length)
            {
                char n = r[i + 1]; i++;
                if (char.IsDigit(n)) sb.Append("${").Append(n).Append('}');
                else if (n == 'n' || n == 'r') sb.Append('\n');
                else if (n == 't') sb.Append('\t');
                else if (n == '&') sb.Append('&');
                else if (n == '$') sb.Append("$$");
                else sb.Append(n);
                continue;
            }
            if (c == '&') { sb.Append("$0"); continue; }
            if (c == '$') { sb.Append("$$"); continue; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private void Global(int from, int to, string arg, bool invert)
    {
        if (arg.Length < 2) throw new VimError("E476: Neispravna komanda");
        char d = arg[0];
        var parts = SplitDelim(arg.Substring(1), d);
        var pat = parts[0];
        var command = parts.Count > 1 ? string.Join(d.ToString(), parts.Skip(1)).Trim() : "p";
        var rx = Files.MakeRegex(VimToNetRegex(pat), Opt.IgnoreCase, Opt.SmartCase);
        // označi linije
        var marked = new List<string>();
        var markers = new HashSet<int>();
        for (int l = from; l <= to; l++) if (rx.IsMatch(Buf[l]) != invert) markers.Add(l);
        if (command == "d" || command == "delete")
        {
            var keep = new List<string>(); int removed = 0;
            for (int l = 0; l < Buf.Count; l++) { if (markers.Contains(l)) removed++; else keep.Add(Buf[l]); }
            Buf.ReplaceAll(keep);
            Cur = Buf.Clamp(Cur);
            Notify($"{removed} linija obrisano");
            return;
        }
        if (command == "p" || command == "print")
        {
            var items = markers.OrderBy(x => x).Select(l => new PickerItem { Text = $"{l + 1}: {Buf[l].Trim()}", Path = Buf.Path, Line = l }).ToList();
            var pick = new Picker { Title = "Globalno", Items = items }.Run(Ui, DrawBase);
            if (pick != null) { PushJump(); Cur = new Pos(pick.Line, 0); CenterCursor(); }
            return;
        }
        // ostale komande: izvrši na svakoj liniji odozdo nagore
        foreach (var l in markers.OrderByDescending(x => x))
        {
            if (l >= Buf.Count) continue;
            Cur = new Pos(l, 0);
            ExecuteEx(command.StartsWith("norm") ? command : $"{l + 1}{command}");
        }
    }

    private void SetTheme(string name)
    {
        if (!Theme.Names.Contains(name.ToLowerInvariant())) throw new VimError("E185: Nepoznata tema: " + name + " (dostupne: " + string.Join(", ", Theme.Names) + ")");
        Opt.Theme = name.ToLowerInvariant();
        Ui.Theme = Theme.Get(Opt.Theme);
        Ui.Screen.Invalidate();
    }

    private void CheckHealth()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"psvim {Version}");
        sb.AppendLine($"Konfiguracija: {Options.DefaultPath()} {(File.Exists(Options.DefaultPath()) ? "✓" : "(ne postoji — ␣fc je pravi)")}");
        sb.AppendLine($"Podaci: {Options.DataDir()}");
        sb.AppendLine($"Terminal: {Ui.Term.Width}x{Ui.Term.Height}");
        sb.AppendLine($"Clipboard: {(Hooks.ClipboardGet != null ? "✓" : "✗")}");
        sb.AppendLine($"Shell (:!): {(Hooks.Shell != null ? "✓" : "✗")}");
        sb.AppendLine($"Dijagnostika (parser): {(Hooks.Diagnose != null ? "✓" : "✗")}");
        sb.AppendLine($"PSScriptAnalyzer (lint/format): {(Hooks.Lint != null ? "✓" : "✗ (Install-Module PSScriptAnalyzer)")}");
        sb.AppendLine($"Pomoć (K): {(Hooks.Help != null ? "✓" : "✗")}");
        sb.AppendLine($"Dopuna (TabExpansion2): {(Hooks.Complete != null ? "✓" : "✗")}");
        sb.AppendLine($"lazygit: {(Files.FindInPath("lazygit") ?? "✗")}");
        sb.AppendLine($"git: {(Files.FindInPath("git") ?? "✗")}");
        sb.AppendLine($"Nerd Font ikonice: {(Opt.NerdFont ? "uključene" : "isključene (:set nerdfont)")}");
        ShowPopup("checkhealth", sb.ToString().TrimEnd());
    }

    public void ShowHelp()
    {
        var help = HelpText.Replace("{leader}", "␣");
        var existing = Buffers.FirstOrDefault(b => b.ScratchName == "[Pomoć]");
        if (existing != null) { SwitchTo(existing); return; }
        NewScratch("[Pomoć]", help, "markdown", true);
    }

    public const string HelpText = @"# psvim — pomoć

Leader taster je razmak (␣). Pritisni ␣ i sačekaj da vidiš meni (which-key).

## Modovi
- `i a I A o O`   insert mod        `Esc` nazad u normal
- `v V`           vizuelni mod      `gv` ponovo poslednja selekcija
- `:`             komandna linija   `/ ?` pretraga  `n N * #`
- `R`             zamena teksta

## Kretanje
- `h j k l`  `w b e ge  W B E`  `0 ^ $ g_`  `gg G  {N}G`
- `f F t T ; ,`  `%`  `{ }`  `M`  `<C-d> <C-u> <C-f> <C-b>`  `zz zt zb`
- `s` flash skok: otkucaj 1-2 slova pa oznaku
- `<C-o> <C-i>` lista skokova    `m{a-z}` oznaka, `'a` / `` `a `` skok

## Izmene
- operatori: `d c y > < = gu gU g~ gc` + kretanje ili tekst objekat
- tekst objekti: `iw aw iW aW i"" a"" i' a' i( a( i{ a{ i[ a[ ip ap if af ig`
- `x X r s S C D Y J p P ~ <C-a> <C-x>`  `u <C-r>`  `.` ponavljanje
- `gcc` komentariši liniju   `q{a-z}` snimi makro, `@{a-z}` pusti, `@@`

## LazyVim prečice
- `␣␣` / `␣ff` fajlovi     `␣/` / `␣sg` grep       `␣,` / `␣fb` baferi
- `␣fr` nedavni fajlovi    `␣fn` novi fajl         `␣fc` konfiguracija
- `␣e` stablo fajlova      `␣sw` traži reč         `␣ss` simboli (funkcije)
- `␣sb` linije bafera      `␣sk` sve prečice       `␣sc` / `␣:` istorija komandi
- `␣bd` zatvori bafer      `␣bo` zatvori ostale    `H L` / `[b ]b` prethodni/sledeći
- `␣cf` formatiraj         `␣cd` dijagnostika linije  `␣xx` lista dijagnostike
- `␣cx` pokreni fajl       `␣cR` preimenuj fajl    `]d [d` sledeća/prethodna greška
- `␣uC` tema               `␣ul ␣uL` brojevi linija `␣ud` dijagnostika on/off
- `␣gg` lazygit            `␣gs` git status        `␣gl` git log  `␣gd` git diff
- `␣qq` izlaz              `<C-s>` sačuvaj         `<C-/>` terminal (exit za povratak)
- `gd` definicija          `gr` reference          `K` pomoć (Get-Help)   `gf` otvori fajl
- `<C-h> <C-l>` fokus stablo/editor
- insert: `<C-Space>`/`<C-n>` dopuna (PowerShell IntelliSense)

## Stablo fajlova
- `j k` kretanje, `l`/`Enter` otvori, `h` zatvori, `a` novi (završi sa / za folder)
- `d` obriši, `r` preimenuj, `y` kopiraj putanju, `H` skriveni fajlovi, `R` osveži
- `-`/`BS` roditeljski folder, `.` postavi kao koren, `q` zatvori

## Ex komande
- `:w :q :wq :x :q! :qa :wa :e fajl :enew :bn :bp :bd :ls`
- `:%s/staro/novo/g`  `:g/šablon/d`  `:v/šablon/d`  `:sort`  `:norm A;`
- `:set nu rnu ts=4 sw=4 et ic scs hls`  `:colorscheme tokyonight|catppuccin|gruvbox|kanagawa`
- `:!komanda` PowerShell   `:r !komanda` ubaci izlaz   `:'<,'>!Sort-Object` filter
- `:cd :pwd :Format :checkhealth :messages :registers :marks :help`
";
}
