using System.Text;
using System.Text.RegularExpressions;

namespace PsVim;

public sealed partial class Editor
{
    private bool _repeatable, _skipUndo;

    private string NextKey() => Ui.Input.Next();

    private void NormalCommand()
    {
        if (Mode != Mode.Normal) Mode = Mode.Normal;
        var buf = Buf;
        var snap = buf.TakeSnapshot();
        int ver = buf.Version;
        _repeatable = false; _skipUndo = false;
        Ui.Input.StartRecording();
        try { ExecNormal(); }
        finally
        {
            _showCmd = "";
            _flash = null;
            var rec = Ui.Input.StopRecording();
            if (buf == Buf && Buffers.Contains(buf) && buf.Version != ver && !_skipUndo) buf.CommitUndo(snap);
            if (_repeatable && rec != null && rec.Count > 0) _lastChange = rec;
            if (Buf != null && Mode == Mode.Normal) ClampNormal();
        }
    }

    private void ExecNormal()
    {
        string k = NextKey();
        char? reg = null;
        int count = 0;
        while (true)
        {
            if (k == "\"")
            {
                var r = NextKey();
                if (r.Length != 1) return;
                reg = r[0]; _showCmd = "\"" + r;
                k = NextKey(); continue;
            }
            if (k.Length == 1 && char.IsDigit(k[0]) && (k != "0" || count > 0))
            {
                count = count * 10 + (k[0] - '0');
                _showCmd = (reg != null ? "\"" + reg : "") + count;
                k = NextKey(); continue;
            }
            break;
        }
        bool hadCount = count > 0;
        int n = Math.Max(1, count);

        // korisnička mapiranja
        if (_userMaps.TryGetValue(k, out var um)) { Ui.Input.Push(um.Rhs); return; }

        if (Buf.FileType == "dashboard" && k.Length == 1 && "frgnceqk?".Contains(k[0])) { DashboardKey(k); return; }

        if (k == Opt.Leader)
        {
            _showCmd = "␣";
            var act = WhichKey.Run(Ui, _leader, "␣ ", DrawBase, Opt.WhichKeyDelay);
            _showCmd = "";
            act?.Invoke();
            return;
        }

        // prefiksi sa dva tastera
        if (k == "g" || k == "z" || k == "Z" || k == "[" || k == "]" || k == "<C-w>")
        {
            _showCmd += k;
            var k2 = NextKey();
            k = k + k2;
        }

        // operatori
        switch (k)
        {
            case "d": case "c": case "y": case ">": case "<": case "g~": case "gu": case "gU": case "gc": case "=":
                OperatorPending(k, reg, count);
                return;
        }

        // kretanja
        var m = Motion(k, n, hadCount, false, false);
        if (m != null)
        {
            if (m.Jump) PushJump();
            Cur = Buf.Clamp(m.B);
            if (!m.KeepWant) Buf.WantCol = DisplayCol(Line, Cur.C);
            return;
        }

        switch (k)
        {
            // ---------- ubacivanje ----------
            case "i": case "<Insert>": CheckWritable(); RunInsert(); _repeatable = true; break;
            case "a": CheckWritable(); if (Line.Length > 0) Cur = new Pos(Cur.L, Cur.C + 1); RunInsert(); _repeatable = true; break;
            case "I": CheckWritable(); Cur = new Pos(Cur.L, FirstNonBlankIndex(Cur.L)); RunInsert(); _repeatable = true; break;
            case "gI": CheckWritable(); Cur = new Pos(Cur.L, 0); RunInsert(); _repeatable = true; break;
            case "A": CheckWritable(); Cur = new Pos(Cur.L, Line.Length); RunInsert(); _repeatable = true; break;
            case "o": CheckWritable(); OpenLine(true); RunInsert(); _repeatable = true; break;
            case "O": CheckWritable(); OpenLine(false); RunInsert(); _repeatable = true; break;
            case "gi": CheckWritable(); Cur = Buf.Clamp(_lastInsert); RunInsert(); _repeatable = true; break;
            case "S": CheckWritable(); Ui.Input.Push(new[] { "c", "c" }); break;
            case "C": CheckWritable(); ApplyOp("c", Motion("$", n, hadCount, true, false), reg); _repeatable = true; break;
            case "D": CheckWritable(); ApplyOp("d", Motion("$", n, hadCount, true, false), reg); _repeatable = true; break;
            case "Y": ApplyOp("y", Motion("$", n, hadCount, true, false), reg); break;
            case "x": case "<Del>":
                CheckWritable();
                if (Line.Length > 0) { ApplyOp("d", new Range { A = Cur, B = new Pos(Cur.L, Math.Min(Line.Length, Cur.C + n)) }, reg); _repeatable = true; }
                break;
            case "X":
                CheckWritable();
                if (Cur.C > 0) { ApplyOp("d", new Range { A = new Pos(Cur.L, Math.Max(0, Cur.C - n)), B = Cur }, reg); _repeatable = true; }
                break;
            case "r":
                {
                    CheckWritable();
                    var c = NextKey();
                    if (c == "<Esc>") break;
                    if (Cur.C + n > Line.Length) break;
                    if (c == "<CR>")
                    {
                        Buf.Delete(Cur, new Pos(Cur.L, Cur.C + n));
                        var ind = Buffer.IndentOf(Line);
                        Buf.Insert(Cur, "\n" + ind);
                        Cur = new Pos(Cur.L + 1, ind.Length);
                    }
                    else if (Keys.IsPrintable(c))
                    {
                        var s = Line;
                        Buf.SetLine(Cur.L, s.Substring(0, Cur.C) + string.Concat(Enumerable.Repeat(c, n)) + s.Substring(Cur.C + n));
                        Cur = new Pos(Cur.L, Cur.C + n - 1);
                    }
                    _repeatable = true;
                    break;
                }
            case "R": CheckWritable(); RunReplace(); _repeatable = true; break;
            case "J": case "gJ": CheckWritable(); JoinLines(Cur.L, Math.Max(2, n), k == "J"); _repeatable = true; break;
            case "~":
                {
                    CheckWritable();
                    var s = Line;
                    if (s.Length == 0) break;
                    int e = Math.Min(s.Length, Cur.C + n);
                    var mid = new string(s.Substring(Cur.C, e - Cur.C).Select(ch => char.IsUpper(ch) ? char.ToLowerInvariant(ch) : char.ToUpperInvariant(ch)).ToArray());
                    Buf.SetLine(Cur.L, s.Substring(0, Cur.C) + mid + s.Substring(e));
                    Cur = new Pos(Cur.L, Math.Min(e, s.Length - 1));
                    _repeatable = true;
                    break;
                }
            case "p": case "P": case "gp": case "gP": CheckWritable(); Paste(reg, k.EndsWith("p"), n, k.StartsWith("g")); _repeatable = true; break;
            case "<C-a>": case "<C-x>": CheckWritable(); Increment(k == "<C-a>" ? n : -n); _repeatable = true; break;

            // ---------- undo ----------
            case "u": _skipUndo = true; for (int i = 0; i < n; i++) if (!UndoOnce()) break; break;
            case "<C-r>": _skipUndo = true; for (int i = 0; i < n; i++) if (!RedoOnce()) break; break;
            case ".":
                if (_lastChange != null) for (int i = 0; i < n; i++) Ui.Input.Push(_lastChange);
                break;

            // ---------- vizuelni ----------
            case "v": case "<C-v>": RunVisual(Mode.Visual, null); break;
            case "V": RunVisual(Mode.VisualLine, null); break;
            case "gv":
                if (_lastVisual != null) { var lv = _lastVisual.Value; RunVisual(lv.M, (lv.A, lv.B)); }
                break;

            // ---------- komande ----------
            case ":":
                {
                    var init = hadCount ? $".,.+{n - 1}" : "";
                    var cmd = ReadCmd(":", init);
                    if (cmd != null) ExecuteEx(cmd);
                    break;
                }
            case "q":
                if (_macroReg != null)
                {
                    var keys = Ui.Input.MacroRec;
                    keys.RemoveAt(keys.Count - 1); // poslednje 'q'
                    _regs[_macroReg.Value] = new Register(Keys.Join(keys), false);
                    Notify($"Snimljen makro @{_macroReg}");
                    _macroReg = null; Ui.Input.MacroRec = null;
                }
                else
                {
                    var r = NextKey();
                    if (r.Length == 1 && (char.IsLetterOrDigit(r[0])))
                    {
                        _macroReg = char.ToLowerInvariant(r[0]);
                        Ui.Input.MacroRec = new List<string>();
                    }
                }
                break;
            case "@":
                {
                    var r = NextKey();
                    if (r == "@") { if (_lastMacro == '\0') break; r = _lastMacro.ToString(); }
                    if (r == ":") { var last = _cmdHistory.LastOrDefault(); if (last != null) for (int i = 0; i < n; i++) ExecuteEx(last); break; }
                    if (r.Length != 1) break;
                    _lastMacro = r[0];
                    var text = GetReg(r[0]).Text;
                    var keys = Keys.Parse(text);
                    for (int i = 0; i < n; i++) Ui.Input.Push(keys);
                    break;
                }
            case "m":
                {
                    var r = NextKey();
                    if (r.Length == 1 && char.IsLetter(r[0])) Buf.Marks[r[0]] = Cur;
                    break;
                }
            case "<C-o>": for (int i = 0; i < n; i++) JumpOlder(-1); break;
            case "<C-i>": case "<Tab>": for (int i = 0; i < n; i++) JumpOlder(1); break;

            // ---------- pomeranje ekrana ----------
            case "<C-d>": ScrollHalf(1); break;
            case "<C-u>": ScrollHalf(-1); break;
            case "<C-f>": case "<PageDown>": ScrollPage(1); break;
            case "<C-b>": case "<PageUp>": ScrollPage(-1); break;
            case "<C-e>": Buf.Top = Math.Min(Buf.Count - 1, Buf.Top + n); if (Cur.L < Buf.Top) Cur = new Pos(Buf.Top, Cur.C); break;
            case "<C-y>": Buf.Top = Math.Max(0, Buf.Top - n); if (Cur.L >= Buf.Top + _textH) Cur = new Pos(Buf.Top + _textH - 1, Cur.C); break;
            case "zz": case "z.": Buf.Top = Math.Max(0, Cur.L - _textH / 2); break;
            case "zt": case "z<CR>": Buf.Top = Math.Max(0, Cur.L - Opt.ScrollOff); break;
            case "zb": case "z-": Buf.Top = Math.Max(0, Cur.L - _textH + 1 + Opt.ScrollOff); break;
            case "ZZ": ExecuteEx("x"); break;
            case "ZQ": ExecuteEx("q!"); break;

            // ---------- LazyVim ----------
            case "<Esc>": _hlActive = false; _notes.Clear(); _hover = null; Ui.Screen.Invalidate(); break;
            case "<C-s>": Save(); break;
            case "H": case "[b": CycleBuffer(-n); break;
            case "L": case "]b": CycleBuffer(n); break;
            case "]d": GotoDiagnostic(1, null); break;
            case "[d": GotoDiagnostic(-1, null); break;
            case "]e": GotoDiagnostic(1, "Error"); break;
            case "[e": GotoDiagnostic(-1, "Error"); break;
            case "]w": GotoDiagnostic(1, "Warning"); break;
            case "[w": GotoDiagnostic(-1, "Warning"); break;
            case "<C-h>": case "<C-w>h": if (_treeOpen) _treeFocus = true; break;
            case "<C-l>": case "<C-w>l": _treeFocus = false; break;
            case "<C-w>w": case "<C-w><C-w>": if (_treeOpen) _treeFocus = !_treeFocus; break;
            case "gd": GotoDefinition(); break;
            case "gr": GrepWord(); break;
            case "gf": GotoFile(); break;
            case "K": Hover(); break;
            case "gx": OpenUrlUnderCursor(); break;
            case "<C-/>": case "<C-_>": OpenTerminal(); break;
            case "<C-g>": Notify($"\"{ShortPath(Buf.Path) ?? Buf.DisplayName}\" {Buf.Count} linija{(Buf.Modified ? " [izmenjen]" : "")} — {(int)(100.0 * (Cur.L + 1) / Buf.Count)}%"); break;
            case "<F1>": ShowHelp(); break;
            case "<C-c>": break;
            default:
                if (Buf.FileType == "dashboard" && k.Length == 1) DashboardKey(k);
                break;
        }
    }

    private int FirstNonBlankIndex(int l)
    {
        var s = Buf[l]; int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        return i;
    }

    private void DashboardKey(string k)
    {
        switch (k)
        {
            case "f": FindFiles(); break;
            case "r": RecentFiles(); break;
            case "g": LiveGrep(null); break;
            case "n": ExecuteEx("enew"); break;
            case "c": OpenConfig(); break;
            case "e": ToggleTree(); break;
            case "k": Keymaps(); break;
            case "?": ShowHelp(); break;
            case "q": ExecuteEx("qa"); break;
        }
    }

    // =================================================================
    //  Operatori
    // =================================================================
    private void OperatorPending(string op, char? reg, int count1)
    {
        if (op != "y") CheckWritable();
        _showCmd += op;
        var k = NextKey();
        int count2 = 0;
        while (k.Length == 1 && char.IsDigit(k[0]) && (k != "0" || count2 > 0))
        {
            count2 = count2 * 10 + (k[0] - '0'); _showCmd += k; k = NextKey();
        }
        bool hadCount = count1 > 0 || count2 > 0;
        int n = Math.Max(1, count1) * Math.Max(1, count2);
        if (k == "<Esc>") return;
        string last = op.Substring(op.Length - 1);
        bool doubled = k == last || (op == "gc" && k == "c") || (op == "g~" && k == "~") || (op == "gu" && k == "u") || (op == "gU" && k == "U");
        if (op.Length == 2 && k == "g")
        {
            var k2 = NextKey();
            if (k2 == op.Substring(1)) doubled = true; else k = "g" + k2;
        }
        else if (!doubled && k == "g") { var k2 = NextKey(); k = "g" + k2; }

        Range r;
        if (doubled)
        {
            int l2 = Math.Min(Buf.Count - 1, Cur.L + n - 1);
            r = new Range { A = new Pos(Cur.L, Cur.C), B = new Pos(l2, 0), Linewise = true };
        }
        else
        {
            // cw == ce kada smo na reči
            if (op == "c" && (k == "w" || k == "W") && Line.Length > 0 && Cur.C < Line.Length && !char.IsWhiteSpace(Line[Cur.C]))
            {
                bool big = k == "W";
                var s0 = Line;
                int cls = Cls(s0[Cur.C], big);
                int e = Cur.C; while (e + 1 < s0.Length && Cls(s0[e + 1], big) == cls) e++;
                var q = new Pos(Cur.L, e);
                for (int i = 1; i < n; i++) q = WordEnd(q, big);
                r = new Range { A = Cur, B = q, Inclusive = true };
            }
            else
            {
                r = Motion(k, n, hadCount, true, false);
                if (r == null) return;
                if (r.Jump) PushJump();
                if ((k == "w" || k == "W") && r.B.L > r.A.L && r.B.C <= FirstNonBlankIndex(r.B.L))
                {
                    int pl = r.B.L - 1;
                    r.B = new Pos(pl, Buf.LineLen(pl));
                }
                else if (!r.Linewise && !r.Inclusive && r.B.C == 0 && r.B.L > r.A.L && k != "w" && k != "W")
                {
                    // o_v pravilo: kraj u koloni 1 => kraj prethodne linije
                    int pl = r.B.L - 1;
                    bool startAtBol = r.A.C <= FirstNonBlankIndex(r.A.L);
                    r.B = new Pos(pl, Buf.LineLen(pl));
                    if (startAtBol && (k == "}" || k == "{")) r.Linewise = true;
                }
            }
        }
        ApplyOp(op, r, reg, n);
        if (op != "y") _repeatable = true;
    }

    private void ApplyOp(string op, Range r, char? reg, int count = 1)
    {
        if (r == null) return;
        var b = Buf;
        Pos a = r.A, e = r.B;
        if (e < a) (a, e) = (e, a);
        if (r.Linewise)
        {
            int l1 = a.L, l2 = e.L;
            var lines = b.GetLines(l1, l2);
            var text = string.Join("\n", lines);
            switch (op)
            {
                case "d":
                    SetReg(reg, text, true, false);
                    b.DeleteLines(l1, l2);
                    Cur = new Pos(Math.Min(l1, b.Count - 1), 0);
                    Cur = new Pos(Cur.L, FirstNonBlankIndex(Cur.L));
                    break;
                case "c":
                    {
                        SetReg(reg, text, true, false);
                        var ind = Buffer.IndentOf(lines[0]);
                        if (l2 > l1) b.DeleteLines(l1 + 1, l2);
                        b.SetLine(l1, ind);
                        Cur = new Pos(l1, ind.Length);
                        RunInsert();
                        break;
                    }
                case "y":
                    SetReg(reg, text, true, true);
                    Cur = new Pos(l1, l1 == Cur.L ? Cur.C : Cur.C);
                    if (r.A.L != r.B.L || true) Cur = new Pos(Math.Min(r.A.L, r.B.L), Cur.C);
                    if (lines.Count > 2) Notify($"{lines.Count} linija kopirano");
                    break;
                case ">": case "<":
                    Shift(l1, l2, op == ">" ? 1 : -1, 1);
                    Cur = new Pos(l1, FirstNonBlankIndex(l1));
                    break;
                case "g~": case "gu": case "gU":
                    for (int l = l1; l <= l2; l++) b.SetLine(l, CaseOp(op, b[l]));
                    Cur = new Pos(l1, Cur.C);
                    break;
                case "gc":
                    ToggleComment(l1, l2);
                    break;
                case "=":
                    Reindent(l1, l2);
                    Cur = new Pos(l1, FirstNonBlankIndex(l1));
                    break;
            }
            return;
        }
        // znakovno
        var end = r.Inclusive ? new Pos(e.L, e.C + 1) : e;
        if (end.C > b.LineLen(end.L)) end = new Pos(end.L, b.LineLen(end.L));
        if (a.C > b.LineLen(a.L)) a = new Pos(a.L, b.LineLen(a.L));
        switch (op)
        {
            case "d":
                {
                    var t = b.Delete(a, end);
                    SetReg(reg, t, false, false);
                    Cur = a;
                    break;
                }
            case "c":
                {
                    var t = b.Delete(a, end);
                    SetReg(reg, t, false, false);
                    Cur = a;
                    RunInsert();
                    break;
                }
            case "y":
                SetReg(reg, b.GetText(a, end), false, true);
                Cur = a;
                break;
            case ">": case "<":
                Shift(a.L, e.L, op == ">" ? 1 : -1, 1);
                Cur = new Pos(a.L, FirstNonBlankIndex(a.L));
                break;
            case "g~": case "gu": case "gU":
                {
                    var t = b.GetText(a, end);
                    var nt = CaseOp(op, t);
                    if (nt != t) { b.Delete(a, end); b.Insert(a, nt); }
                    Cur = a;
                    break;
                }
            case "gc": ToggleComment(a.L, e.L); break;
            case "=": Reindent(a.L, e.L); break;
        }
    }

    private static string CaseOp(string op, string s) => op switch
    {
        "gu" => s.ToLowerInvariant(),
        "gU" => s.ToUpperInvariant(),
        _ => new string(s.Select(ch => char.IsUpper(ch) ? char.ToLowerInvariant(ch) : char.ToUpperInvariant(ch)).ToArray())
    };

    private string IndentUnit => Opt.ExpandTab ? new string(' ', Opt.ShiftWidth) : "\t";

    private void Shift(int l1, int l2, int dir, int times)
    {
        var b = Buf;
        for (int l = l1; l <= l2; l++)
        {
            var s = b[l];
            if (s.Trim().Length == 0) { if (s.Length > 0 && dir < 0) b.SetLine(l, ""); continue; }
            for (int t = 0; t < times; t++)
            {
                if (dir > 0) s = IndentUnit + s;
                else
                {
                    int remove = 0, col = 0;
                    while (remove < s.Length && col < Opt.ShiftWidth)
                    {
                        if (s[remove] == ' ') col++;
                        else if (s[remove] == '\t') col = Opt.ShiftWidth;
                        else break;
                        remove++;
                    }
                    s = s.Substring(remove);
                }
            }
            b.SetLine(l, s);
        }
    }

    private void ToggleComment(int l1, int l2)
    {
        var b = Buf;
        var cs = Syntax.CommentString(b.FileType);
        bool wrap = cs == "<!--";
        bool allCommented = true;
        int minInd = int.MaxValue;
        for (int l = l1; l <= l2; l++)
        {
            var t = b[l].TrimStart();
            if (t.Length == 0) continue;
            minInd = Math.Min(minInd, b[l].Length - t.Length);
            if (!t.StartsWith(cs)) allCommented = false;
        }
        if (minInd == int.MaxValue) return;
        for (int l = l1; l <= l2; l++)
        {
            var s = b[l];
            if (s.Trim().Length == 0) continue;
            if (allCommented)
            {
                int i = s.IndexOf(cs, StringComparison.Ordinal);
                int len = cs.Length + (i + cs.Length < s.Length && s[i + cs.Length] == ' ' ? 1 : 0);
                s = s.Remove(i, len);
                if (wrap)
                {
                    int j = s.LastIndexOf("-->", StringComparison.Ordinal);
                    if (j >= 0) { int st = j > 0 && s[j - 1] == ' ' ? j - 1 : j; s = s.Remove(st, j + 3 - st); }
                }
            }
            else
            {
                s = s.Insert(minInd, cs + " ");
                if (wrap) s += " -->";
            }
            b.SetLine(l, s);
        }
    }

    private void Reindent(int l1, int l2)
    {
        // jednostavno uvlačenje po zagradama { } ( ) [ ]
        var b = Buf;
        int level = 0;
        // izračunaj početni nivo iz prethodne nepraznih linija
        for (int l = l1 - 1; l >= 0; l--)
        {
            if (b[l].Trim().Length == 0) continue;
            var ind = Buffer.IndentOf(b[l]);
            int cols = ind.Sum(ch => ch == '\t' ? Opt.ShiftWidth : 1);
            level = cols / Opt.ShiftWidth + Net(b[l].Trim(), out _);
            break;
        }
        for (int l = l1; l <= l2; l++)
        {
            var t = b[l].Trim();
            if (t.Length == 0) { b.SetLine(l, ""); continue; }
            int net = Net(t, out int leadingClose);
            int cur = Math.Max(0, level - leadingClose);
            b.SetLine(l, string.Concat(Enumerable.Repeat(IndentUnit, cur)) + t);
            level = Math.Max(0, level + net);
        }
    }

    private static int Net(string t, out int leadingClose)
    {
        int net = 0; leadingClose = 0; bool lead = true;
        bool inStr = false; char q = '\0';
        foreach (var ch in t)
        {
            if (inStr) { if (ch == q) inStr = false; continue; }
            if (ch == '"' || ch == '\'') { inStr = true; q = ch; lead = false; continue; }
            if (ch == '#') break;
            if (ch == '{' || ch == '(' || ch == '[') { net++; lead = false; }
            else if (ch == '}' || ch == ')' || ch == ']') { net--; if (lead) leadingClose++; }
            else if (!char.IsWhiteSpace(ch)) lead = false;
        }
        return net;
    }

    private void JoinLines(int l, int count, bool spaces)
    {
        var b = Buf;
        int last = Math.Min(b.Count - 1, l + count - 1);
        if (last == l) return;
        var s = b[l];
        int col = s.Length;
        for (int i = l + 1; i <= last; i++)
        {
            var nx = b[i];
            if (spaces)
            {
                nx = nx.TrimStart();
                s = s.TrimEnd();
                col = s.Length;
                if (nx.Length > 0 && s.Length > 0 && !nx.StartsWith(")")) s += " ";
            }
            else col = s.Length;
            s += nx;
        }
        b.DeleteLines(l + 1, last);
        b.SetLine(l, s);
        Cur = new Pos(l, Math.Max(0, Math.Min(col, s.Length - 1)));
    }

    private void Paste(char? reg, bool after, int count, bool gMove)
    {
        var r = GetReg(reg);
        if (string.IsNullOrEmpty(r.Text) && !r.Linewise) return;
        var b = Buf;
        if (r.Linewise)
        {
            var lines = r.Text.Split('\n');
            var all = new List<string>();
            for (int i = 0; i < count; i++) all.AddRange(lines);
            int at = after ? Cur.L + 1 : Cur.L;
            b.InsertLines(at, all);
            Cur = gMove ? new Pos(Math.Min(b.Count - 1, at + all.Count), 0) : new Pos(at, FirstNonBlankIndex(at));
        }
        else
        {
            var text = string.Concat(Enumerable.Repeat(r.Text, count));
            var p = Cur;
            if (after && Line.Length > 0) p = new Pos(p.L, p.C + 1);
            var end = b.Insert(p, text);
            if (text.Contains('\n')) Cur = gMove ? end : p;
            else Cur = gMove ? end : new Pos(end.L, Math.Max(0, end.C - 1));
        }
    }

    private void Increment(int delta)
    {
        var s = Line;
        Match m = null;
        foreach (Match mm in Regex.Matches(s, @"0[xX][0-9a-fA-F]+|\d+"))
            if (mm.Index + mm.Length > Cur.C) { m = mm; break; }
        if (m == null) return;
        int start = m.Index, endIdx = m.Index + m.Length;
        string res;
        if (m.Value.Length > 2 && (m.Value[1] == 'x' || m.Value[1] == 'X'))
        {
            var hex = m.Value.Substring(2);
            long v = Convert.ToInt64(hex, 16) + delta;
            if (v < 0) v = 0;
            res = m.Value.Substring(0, 2) + v.ToString(hex.Any(char.IsUpper) ? "X" : "x").PadLeft(hex.Length, '0');
        }
        else
        {
            bool neg = start > 0 && s[start - 1] == '-' && (start - 1 == 0 || !char.IsLetterOrDigit(s[start - 2]));
            if (neg) start--;
            long v = long.Parse(m.Value) * (neg ? -1 : 1) + delta;
            res = v.ToString();
        }
        Buf.SetLine(Cur.L, s.Substring(0, start) + res + s.Substring(endIdx));
        Cur = new Pos(Cur.L, start + res.Length - 1);
    }

    private bool UndoOnce()
    {
        if (Buf.Undo.Count == 0) { Notify("Već je najstarija izmena"); return false; }
        var snap = Buf.Undo.Pop();
        var now = Buf.TakeSnapshot();
        now.Cursor = snap.Cursor;
        Buf.Redo.Push(now);
        var keepCursor = snap.Cursor;
        Buf.Restore(snap);
        Cur = Buf.Clamp(keepCursor);
        return true;
    }

    private bool RedoOnce()
    {
        if (Buf.Redo.Count == 0) { Notify("Već je najnovija izmena"); return false; }
        var snap = Buf.Redo.Pop();
        Buf.Undo.Push(Buf.TakeSnapshot());
        Buf.Restore(snap);
        Cur = Buf.Clamp(snap.Cursor);
        return true;
    }

    private void ScrollHalf(int dir)
    {
        int h = Math.Max(1, _textH / 2);
        Buf.Top = Math.Clamp(Buf.Top + dir * h, 0, Math.Max(0, Buf.Count - 1));
        int l = Math.Clamp(Cur.L + dir * h, 0, Buf.Count - 1);
        Cur = new Pos(l, ColFromDisplay(Buf[l], Buf.WantCol));
    }

    private void ScrollPage(int dir)
    {
        int h = Math.Max(1, _textH - 2);
        Buf.Top = Math.Clamp(Buf.Top + dir * h, 0, Math.Max(0, Buf.Count - 1));
        int l = dir > 0 ? Math.Min(Buf.Count - 1, Buf.Top + Opt.ScrollOff) : Math.Min(Buf.Count - 1, Buf.Top + _textH - 1 - Opt.ScrollOff);
        l = Math.Max(0, l);
        Cur = new Pos(l, ColFromDisplay(Buf[l], Buf.WantCol));
    }

    // =================================================================
    //  Vizuelni mod
    // =================================================================
    private void RunVisual(Mode m, (Pos A, Pos B)? restore)
    {
        Mode = m;
        if (restore != null) { _vAnchor = Buf.Clamp(restore.Value.A); Cur = Buf.Clamp(restore.Value.B); }
        else _vAnchor = Cur;
        try
        {
            while (true)
            {
                var k = NextKey();
                int count = 0;
                while (k.Length == 1 && char.IsDigit(k[0]) && (k != "0" || count > 0)) { count = count * 10 + (k[0] - '0'); _showCmd = count.ToString(); k = NextKey(); }
                char? reg = null;
                if (k == "\"") { var r = NextKey(); if (r.Length == 1) reg = r[0]; k = NextKey(); }
                _showCmd = "";
                int n = Math.Max(1, count);
                bool hadCount = count > 0;
                if (k == "g" || k == "z" || k == "[" || k == "]") k += NextKey();
                if (k == Opt.Leader)
                {
                    var act = WhichKey.Run(Ui, _leader, "␣ ", DrawBase, Opt.WhichKeyDelay);
                    if (act != null) { SaveVisual(); Mode = Mode.Normal; act(); return; }
                    continue;
                }
                switch (k)
                {
                    case "<Esc>": case "<C-c>": SaveVisual(); Mode = Mode.Normal; return;
                    case "v": case "<C-v>": if (Mode == Mode.Visual) { SaveVisual(); Mode = Mode.Normal; return; } Mode = Mode.Visual; continue;
                    case "V": if (Mode == Mode.VisualLine) { SaveVisual(); Mode = Mode.Normal; return; } Mode = Mode.VisualLine; continue;
                    case "o": case "O": (_vAnchor, Buf.Cursor) = (Cur, _vAnchor); continue;
                    case ":":
                        {
                            SaveVisual(); Mode = Mode.Normal;
                            var cmd = ReadCmd(":", "'<,'>");
                            if (cmd != null) ExecuteEx(cmd);
                            return;
                        }
                }
                // tekst objekti i kretanja
                if (k == "i" || k == "a")
                {
                    var obj = NextKey();
                    var tr = TextObject(k == "i", obj, n);
                    if (tr != null)
                    {
                        var a = tr.A < tr.B ? tr.A : tr.B; var e = tr.A < tr.B ? tr.B : tr.A;
                        if (!tr.Inclusive && !tr.Linewise && e.C > a.C) e = new Pos(e.L, e.C - 1);
                        if (tr.Linewise && Mode == Mode.Visual) Mode = Mode.VisualLine;
                        _vAnchor = a; Cur = Buf.Clamp(e);
                    }
                    continue;
                }
                var mv = Motion(k, n, hadCount, false, true);
                if (mv != null)
                {
                    if (mv.Jump) PushJump();
                    var to = Buf.Clamp(mv.B);
                    if (Mode == Mode.Visual && to.C >= Buf.LineLen(to.L) && Buf.LineLen(to.L) > 0) to = new Pos(to.L, Buf.LineLen(to.L) - 1);
                    Cur = to;
                    if (!mv.KeepWant) Buf.WantCol = DisplayCol(Line, Cur.C);
                    continue;
                }
                var sel = VisualRange();
                switch (k)
                {
                    case "d": case "x": case "<Del>": case "X": case "D":
                        CheckWritable();
                        if (k == "X" || k == "D") sel.Linewise = true;
                        SaveVisual(); Mode = Mode.Normal; ApplyOp("d", sel, reg); _repeatable = false; return;
                    case "c": case "s": case "S": case "C":
                        CheckWritable();
                        if (k == "S" || k == "C") sel.Linewise = true;
                        SaveVisual(); Mode = Mode.Normal; ApplyOp("c", sel, reg); return;
                    case "y": case "Y":
                        if (k == "Y") sel.Linewise = true;
                        SaveVisual(); Mode = Mode.Normal; ApplyOp("y", sel, reg);
                        Cur = sel.A < sel.B ? sel.A : sel.B;
                        if (sel.Linewise) Cur = new Pos(Cur.L, Math.Min(Cur.C, Math.Max(0, Buf.LineLen(Cur.L) - 1)));
                        return;
                    case ">": case "<":
                        {
                            CheckWritable();
                            var (l1, l2) = (Math.Min(sel.A.L, sel.B.L), Math.Max(sel.A.L, sel.B.L));
                            Shift(l1, l2, k == ">" ? 1 : -1, n);
                            continue; // LazyVim: ostani u selekciji
                        }
                    case "~": case "u": case "U":
                        CheckWritable();
                        SaveVisual(); Mode = Mode.Normal;
                        ApplyOp(k == "~" ? "g~" : k == "u" ? "gu" : "gU", sel, reg); return;
                    case "gc":
                        CheckWritable();
                        SaveVisual(); Mode = Mode.Normal;
                        ToggleComment(Math.Min(sel.A.L, sel.B.L), Math.Max(sel.A.L, sel.B.L)); return;
                    case "=":
                        CheckWritable();
                        SaveVisual(); Mode = Mode.Normal;
                        Reindent(Math.Min(sel.A.L, sel.B.L), Math.Max(sel.A.L, sel.B.L)); return;
                    case "J": case "gJ":
                        {
                            CheckWritable();
                            SaveVisual(); Mode = Mode.Normal;
                            int l1 = Math.Min(sel.A.L, sel.B.L), l2 = Math.Max(sel.A.L, sel.B.L);
                            JoinLines(l1, Math.Max(2, l2 - l1 + 1), k == "J"); return;
                        }
                    case "r":
                        {
                            CheckWritable();
                            var c = NextKey();
                            SaveVisual(); Mode = Mode.Normal;
                            if (!Keys.IsPrintable(c)) return;
                            var a = sel.A < sel.B ? sel.A : sel.B; var e = sel.A < sel.B ? sel.B : sel.A;
                            for (int l = a.L; l <= e.L; l++)
                            {
                                var s = Buf[l];
                                int c1 = sel.Linewise || l > a.L ? 0 : a.C;
                                int c2 = sel.Linewise || l < e.L ? s.Length - 1 : Math.Min(e.C, s.Length - 1);
                                if (c2 < c1) continue;
                                Buf.SetLine(l, s.Substring(0, c1) + new string(c[0], c2 - c1 + 1) + s.Substring(c2 + 1));
                            }
                            Cur = a; return;
                        }
                    case "p": case "P":
                        {
                            CheckWritable();
                            var r = GetReg(reg);
                            SaveVisual(); Mode = Mode.Normal;
                            var a = sel.A < sel.B ? sel.A : sel.B;
                            ApplyOp("d", sel, k == "P" ? '_' : null);
                            if (sel.Linewise)
                            {
                                var lines = r.Text.Split('\n');
                                Buf.InsertLines(a.L, lines);
                                if (Buf.Count > a.L + lines.Length && Buf.Count == lines.Length + 1 && Buf[Buf.Count - 1].Length == 0) Buf.DeleteLines(Buf.Count - 1, Buf.Count - 1);
                                Cur = new Pos(a.L, FirstNonBlankIndex(a.L));
                            }
                            else
                            {
                                var p = Buf.Clamp(a);
                                var text = r.Linewise ? "\n" + r.Text + "\n" : r.Text;
                                var end = Buf.Insert(p, text);
                                Cur = new Pos(end.L, Math.Max(0, end.C - 1));
                            }
                            if (k == "p") SetRegQuiet(r);
                            return;
                        }
                    case "I":
                        {
                            CheckWritable();
                            var a = sel.A < sel.B ? sel.A : sel.B;
                            SaveVisual(); Mode = Mode.Normal;
                            Cur = sel.Linewise ? new Pos(a.L, FirstNonBlankIndex(a.L)) : a;
                            RunInsert(); return;
                        }
                    case "A":
                        {
                            CheckWritable();
                            var e = sel.A < sel.B ? sel.B : sel.A;
                            SaveVisual(); Mode = Mode.Normal;
                            Cur = sel.Linewise ? new Pos(e.L, Buf.LineLen(e.L)) : new Pos(e.L, Math.Min(Buf.LineLen(e.L), e.C + 1));
                            RunInsert(); return;
                        }
                    case "*":
                        {
                            var a = sel.A < sel.B ? sel.A : sel.B; var e = sel.A < sel.B ? sel.B : sel.A;
                            var t = Buf.GetText(a, new Pos(e.L, e.C + 1));
                            SaveVisual(); Mode = Mode.Normal;
                            _lastSearch = Regex.Escape(t); _lastSearchFwd = true; _hlActive = true;
                            var q = Search(_lastSearch, true, a);
                            if (q != null) Cur = q.Value;
                            return;
                        }
                }
            }
        }
        finally { if (Mode == Mode.Visual || Mode == Mode.VisualLine) Mode = Mode.Normal; }
    }

    private void SetRegQuiet(Register r) { /* nakon vizuelnog p, vim stavlja zamenjeni tekst u registar; LazyVim ga zadržava */ }

    private Range VisualRange()
    {
        var a = _vAnchor < Cur ? _vAnchor : Cur;
        var e = _vAnchor < Cur ? Cur : _vAnchor;
        if (Mode == Mode.VisualLine) return new Range { A = a, B = e, Linewise = true };
        if (Buf.LineLen(e.L) == 0) return new Range { A = a, B = e, Inclusive = false };
        return new Range { A = a, B = e, Inclusive = true };
    }

    private void SaveVisual() => _lastVisual = (_vAnchor, Cur, Mode);
}
