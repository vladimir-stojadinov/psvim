using System.Text;

namespace PsVim;

public sealed class CompletionItem
{
    public Pos Start, End;
    public string Insert, Display, Kind;
}

public sealed class CompletionState
{
    public List<CompletionItem> Items;
    public int Sel, Scroll;
}

public sealed partial class Editor
{
    private CompletionState _completion;

    private static readonly Dictionary<char, char> Pairs = new() { ['('] = ')', ['['] = ']', ['{'] = '}', ['"'] = '"', ['\''] = '\'' };
    private static readonly HashSet<char> Closers = new() { ')', ']', '}' };

    // =================================================================
    //  Insert mod
    // =================================================================
    private void RunInsert()
    {
        Mode = Mode.Insert;
        try
        {
            while (true)
            {
                var k = NextKey();
                if (!InsertKey(k)) break;
            }
        }
        finally
        {
            _completion = null;
            _lastInsert = Cur;
            Mode = Mode.Normal;
            if (Cur.C > 0) Cur = new Pos(Cur.L, Cur.C - 1);
            Buf.WantCol = DisplayCol(Line, Cur.C);
        }
    }

    /// <summary>Obrađuje jedan taster u insert modu. Vraća false kada treba izaći.</summary>
    private bool InsertKey(string k)
    {
        var b = Buf;
        switch (k)
        {
            case "<Esc>": case "<C-c>": case "<C-[>":
                return false;
            case "<CR>":
                {
                    var s = Line;
                    var ind = Buffer.IndentOf(s);
                    char prev = Cur.C > 0 ? s[Cur.C - 1] : '\0';
                    char next = Cur.C < s.Length ? s[Cur.C] : '\0';
                    // obriši razmake na kraju linije pre preloma
                    int cut = Cur.C;
                    while (cut > 0 && s[cut - 1] == ' ' && cut > ind.Length) cut--;
                    if (cut < Cur.C) { b.Delete(new Pos(Cur.L, cut), Cur); Cur = new Pos(Cur.L, cut); prev = cut > 0 ? s[cut - 1] : '\0'; }
                    bool opener = prev == '{' || prev == '(' || prev == '[';
                    if (opener && Pairs.TryGetValue(prev, out var cl) && next == cl)
                    {
                        b.Insert(Cur, "\n" + ind + IndentUnit + "\n" + ind);
                        Cur = new Pos(Cur.L + 1, ind.Length + IndentUnit.Length);
                    }
                    else
                    {
                        var extra = opener ? IndentUnit : "";
                        // ne nosi vodeće razmake ostatka linije
                        int rest = Cur.C; while (rest < Line.Length && Line[rest] == ' ') rest++;
                        if (rest > Cur.C) b.Delete(Cur, new Pos(Cur.L, rest));
                        b.Insert(Cur, "\n" + ind + extra);
                        Cur = new Pos(Cur.L + 1, ind.Length + extra.Length);
                    }
                    return true;
                }
            case "<BS>": case "<C-h>":
                {
                    if (Cur.C > 0)
                    {
                        var s = Line;
                        char prev = s[Cur.C - 1];
                        char next = Cur.C < s.Length ? s[Cur.C] : '\0';
                        if (Opt.AutoPairs && Pairs.TryGetValue(prev, out var cl) && next == cl)
                        {
                            b.Delete(new Pos(Cur.L, Cur.C - 1), new Pos(Cur.L, Cur.C + 1));
                            Cur = new Pos(Cur.L, Cur.C - 1);
                            return true;
                        }
                        // briši do prethodnog tab stopa u uvlačenju
                        int ind = 0; while (ind < s.Length && s[ind] == ' ') ind++;
                        if (Opt.ExpandTab && Cur.C <= ind && prev == ' ')
                        {
                            int to = ((Cur.C - 1) / Opt.ShiftWidth) * Opt.ShiftWidth;
                            b.Delete(new Pos(Cur.L, to), Cur);
                            Cur = new Pos(Cur.L, to);
                            return true;
                        }
                        int del = char.IsLowSurrogate(prev) && Cur.C > 1 ? 2 : 1;
                        b.Delete(new Pos(Cur.L, Cur.C - del), Cur);
                        Cur = new Pos(Cur.L, Cur.C - del);
                    }
                    else if (Cur.L > 0)
                    {
                        int len = b.LineLen(Cur.L - 1);
                        b.Delete(new Pos(Cur.L - 1, len), new Pos(Cur.L, 0));
                        Cur = new Pos(Cur.L - 1, len);
                    }
                    return true;
                }
            case "<Del>":
                if (Cur.C < Line.Length) b.Delete(Cur, new Pos(Cur.L, Cur.C + 1));
                else if (Cur.L + 1 < b.Count) b.Delete(Cur, new Pos(Cur.L + 1, 0));
                return true;
            case "<Tab>":
                {
                    if (Opt.ExpandTab)
                    {
                        int dc = DisplayCol(Line, Cur.C);
                        int spaces = Opt.ShiftWidth - dc % Opt.ShiftWidth;
                        Cur = b.Insert(Cur, new string(' ', spaces));
                    }
                    else Cur = b.Insert(Cur, "\t");
                    return true;
                }
            case "<S-Tab>":
                Shift(Cur.L, Cur.L, -1, 1);
                Cur = b.Clamp(new Pos(Cur.L, Math.Max(0, Cur.C - Opt.ShiftWidth)));
                return true;
            case "<C-t>":
                Shift(Cur.L, Cur.L, 1, 1); Cur = new Pos(Cur.L, Cur.C + IndentUnit.Length); return true;
            case "<C-d>":
                {
                    int before = Line.Length;
                    Shift(Cur.L, Cur.L, -1, 1);
                    Cur = b.Clamp(new Pos(Cur.L, Math.Max(0, Cur.C - (before - Line.Length))));
                    return true;
                }
            case "<C-w>": case "<C-BS>":
                {
                    if (Cur.C == 0) return InsertKey("<BS>");
                    var s = Line; int c = Cur.C;
                    while (c > 0 && char.IsWhiteSpace(s[c - 1])) c--;
                    if (c > 0)
                    {
                        int cls = Cls(s[c - 1], false);
                        while (c > 0 && Cls(s[c - 1], false) == cls) c--;
                    }
                    b.Delete(new Pos(Cur.L, c), Cur); Cur = new Pos(Cur.L, c);
                    return true;
                }
            case "<C-u>":
                {
                    int ind = FirstNonBlankIndex(Cur.L);
                    int to = Cur.C > ind ? ind : 0;
                    b.Delete(new Pos(Cur.L, to), Cur); Cur = new Pos(Cur.L, to);
                    return true;
                }
            case "<C-r>":
                {
                    var r = NextKey();
                    if (r.Length != 1) return true;
                    var reg = GetReg(r[0] == '"' ? null : r[0]);
                    Cur = b.Insert(Cur, reg.Linewise ? reg.Text + "\n" : reg.Text);
                    return true;
                }
            case "<C-o>":
                {
                    // jedna normal komanda
                    Mode = Mode.Normal;
                    var save = Cur;
                    ExecNormal();
                    if (Mode == Mode.Normal) Mode = Mode.Insert;
                    else return false;
                    return true;
                }
            case "<Left>": if (Cur.C > 0) Cur = new Pos(Cur.L, Cur.C - 1); return true;
            case "<Right>": if (Cur.C < Line.Length) Cur = new Pos(Cur.L, Cur.C + 1); return true;
            case "<C-Left>": Cur = WordBack(Cur, false); return true;
            case "<C-Right>": { var q = WordFwd(Cur, false, true); Cur = q; return true; }
            case "<Up>": if (Cur.L > 0) { Cur = new Pos(Cur.L - 1, ColFromDisplay(b[Cur.L - 1], b.WantCol)); } return true;
            case "<Down>": if (Cur.L + 1 < b.Count) { Cur = new Pos(Cur.L + 1, ColFromDisplay(b[Cur.L + 1], b.WantCol)); } return true;
            case "<Home>": Cur = new Pos(Cur.L, 0); return true;
            case "<End>": Cur = new Pos(Cur.L, Line.Length); return true;
            case "<C-s>": Save(); return true;
            case "<C-Space>": case "<C-n>": case "<C-p>": Complete(k == "<C-p>"); return true;
            case "<C-a>": case "<C-e>": return true;
        }
        if (!Keys.IsPrintable(k)) return true;
        InsertChar(k);
        return true;
    }

    private void InsertChar(string k)
    {
        var b = Buf;
        var s = Line;
        char ch = k[0];
        char next = Cur.C < s.Length ? s[Cur.C] : '\0';
        char prev = Cur.C > 0 ? s[Cur.C - 1] : '\0';
        if (Opt.AutoPairs && k.Length == 1)
        {
            // preskoči zatvarajući znak
            if ((Closers.Contains(ch) || ch == '"' || ch == '\'') && next == ch)
            {
                Cur = new Pos(Cur.L, Cur.C + 1);
                return;
            }
            if (Pairs.TryGetValue(ch, out var cl))
            {
                bool quote = ch == '"' || ch == '\'';
                bool ok = next == '\0' || char.IsWhiteSpace(next) || Closers.Contains(next) || next == ',' || next == ';';
                if (quote && (char.IsLetterOrDigit(prev) || prev == ch)) ok = false;
                if (ok)
                {
                    b.Insert(Cur, k + cl);
                    Cur = new Pos(Cur.L, Cur.C + 1);
                    return;
                }
            }
        }
        Cur = b.Insert(Cur, k);
        // električno uvlačenje: '}' na praznoj liniji
        if ((ch == '}' || ch == ')' || ch == ']') && Line.Trim() == k && Cur.L > 0)
        {
            var open = ch == '}' ? '{' : ch == ')' ? '(' : '[';
            var m = ScanBracket(new Pos(Cur.L, Cur.C - 1), ch, open, false);
            if (m != null)
            {
                var ind = Buffer.IndentOf(b[m.Value.L]);
                b.SetLine(Cur.L, ind + k);
                Cur = new Pos(Cur.L, ind.Length + 1);
            }
        }
    }

    private void OpenLine(bool below)
    {
        var b = Buf;
        var s = Line;
        var ind = Buffer.IndentOf(s);
        if (below)
        {
            var t = s.TrimEnd();
            if (t.EndsWith("{") || t.EndsWith("(") || t.EndsWith("[")) ind += IndentUnit;
            b.InsertLines(Cur.L + 1, new[] { ind });
            Cur = new Pos(Cur.L + 1, ind.Length);
        }
        else
        {
            b.InsertLines(Cur.L, new[] { ind });
            Cur = new Pos(Cur.L, ind.Length);
        }
    }

    private void RunReplace()
    {
        Mode = Mode.Replace;
        var originals = new Stack<char?>();
        try
        {
            while (true)
            {
                var k = NextKey();
                if (k == "<Esc>" || k == "<C-c>") break;
                if (k == "<BS>")
                {
                    if (originals.Count == 0) { if (Cur.C > 0) Cur = new Pos(Cur.L, Cur.C - 1); continue; }
                    var o = originals.Pop();
                    Cur = new Pos(Cur.L, Cur.C - 1);
                    if (o == null) Buf.Delete(Cur, new Pos(Cur.L, Cur.C + 1));
                    else { var s = Line; Buf.SetLine(Cur.L, s.Substring(0, Cur.C) + o + s.Substring(Cur.C + 1)); }
                    continue;
                }
                if (k == "<CR>") { InsertKey("<CR>"); originals.Clear(); continue; }
                if (!Keys.IsPrintable(k)) continue;
                var line = Line;
                if (Cur.C < line.Length)
                {
                    originals.Push(line[Cur.C]);
                    Buf.SetLine(Cur.L, line.Substring(0, Cur.C) + k + line.Substring(Cur.C + 1));
                    Cur = new Pos(Cur.L, Cur.C + 1);
                }
                else
                {
                    originals.Push(null);
                    Cur = Buf.Insert(Cur, k);
                }
            }
        }
        finally
        {
            Mode = Mode.Normal;
            if (Cur.C > 0) Cur = new Pos(Cur.L, Cur.C - 1);
        }
    }

    // =================================================================
    //  Dopuna (blink.cmp stil)
    // =================================================================
    private void Complete(bool reverse)
    {
        var items = GetCompletions();
        if (items.Count == 0) { Notify("Nema predloga"); return; }
        if (items.Count == 1) { AcceptCompletion(items[0]); return; }
        _completion = new CompletionState { Items = items, Sel = reverse ? items.Count - 1 : 0 };
        try
        {
            while (_completion != null)
            {
                var k = NextKey();
                var cs = _completion;
                switch (k)
                {
                    case "<C-n>": case "<Down>": case "<Tab>": cs.Sel = (cs.Sel + 1) % cs.Items.Count; continue;
                    case "<C-p>": case "<Up>": case "<S-Tab>": cs.Sel = (cs.Sel - 1 + cs.Items.Count) % cs.Items.Count; continue;
                    case "<CR>": case "<C-y>": AcceptCompletion(cs.Items[cs.Sel]); return;
                    case "<C-e>": return;
                    case "<Esc>": Ui.Input.Push(new[] { "<Esc>" }); return;
                    case "<BS>":
                        InsertKey("<BS>");
                        break;
                    default:
                        if (Keys.IsPrintable(k) && k.Length == 1 && (char.IsLetterOrDigit(k[0]) || k[0] == '_' || k[0] == '-' || k[0] == '$'))
                        {
                            Cur = Buf.Insert(Cur, k);
                            break;
                        }
                        Ui.Input.Push(new[] { k });
                        return;
                }
                var again = GetCompletions();
                if (again.Count == 0) return;
                _completion.Items = again; _completion.Sel = 0; _completion.Scroll = 0;
            }
        }
        finally { _completion = null; }
    }

    private void AcceptCompletion(CompletionItem it)
    {
        var start = it.Start; var end = it.End;
        Buf.Delete(start, end);
        Cur = Buf.Insert(start, it.Insert);
    }

    private List<CompletionItem> GetCompletions()
    {
        var res = new List<CompletionItem>();
        if (Buf.FileType == "powershell" && Hooks.Complete != null)
        {
            try
            {
                var text = Buf.GetAllText("\n");
                int off = 0;
                for (int l = 0; l < Cur.L; l++) off += Buf.LineLen(l) + 1;
                off += Cur.C;
                var raw = Hooks.Complete(text, off) ?? Array.Empty<string>();
                foreach (var r in raw.Take(300))
                {
                    var p = r.Split('\t');
                    if (p.Length < 3) continue;
                    int ri = int.Parse(p[0]), rl = int.Parse(p[1]);
                    var sp = OffsetToPos(ri); var ep = OffsetToPos(ri + rl);
                    res.Add(new CompletionItem { Start = sp, End = ep, Insert = p[2], Display = p.Length > 3 ? p[3] : p[2], Kind = p.Length > 4 ? p[4] : "" });
                }
            }
            catch (Exception e) { Notify("Dopuna: " + e.Message, true); }
            if (res.Count > 0) return res;
        }
        // reči iz bafera
        var s = Line;
        int c = Cur.C;
        int st = c; while (st > 0 && (char.IsLetterOrDigit(s[st - 1]) || s[st - 1] == '_' || s[st - 1] == '-')) st--;
        var prefix = s.Substring(st, c - st);
        if (prefix.Length == 0) return res;
        var words = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var b in Buffers)
            foreach (var line in b.Lines)
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(line, @"[\p{L}_][\p{L}\p{N}_\-]*"))
                {
                    var w = m.Value.TrimEnd('-');
                    if (w.Length < 2 || w == prefix) continue;
                    if (!w.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    words[w] = words.GetValueOrDefault(w) + 1;
                }
        foreach (var w in words.OrderByDescending(x => x.Value).ThenBy(x => x.Key).Take(50))
            res.Add(new CompletionItem { Start = new Pos(Cur.L, st), End = Cur, Insert = w.Key, Display = w.Key, Kind = "Text" });
        return res;
    }

    private Pos OffsetToPos(int off)
    {
        for (int l = 0; l < Buf.Count; l++)
        {
            int len = Buf.LineLen(l);
            if (off <= len) return new Pos(l, off);
            off -= len + 1;
        }
        return new Pos(Buf.Count - 1, Buf.LineLen(Buf.Count - 1));
    }

    private void DrawCompletion()
    {
        var cs = _completion; var sc = Ui.Screen; var th = Th;
        int maxH = Math.Min(10, cs.Items.Count);
        int w = Math.Min(sc.W - 2, Math.Max(20, cs.Items.Take(200).Max(i => i.Display.Length + (i.Kind?.Length ?? 0) + 4)));
        int x = Math.Min(sc.CursorX, sc.W - w - 1);
        int y = sc.CursorY + 1;
        if (y + maxH > sc.H - 2) y = sc.CursorY - maxH;
        if (cs.Sel < cs.Scroll) cs.Scroll = cs.Sel;
        if (cs.Sel >= cs.Scroll + maxH) cs.Scroll = cs.Sel - maxH + 1;
        for (int r = 0; r < maxH; r++)
        {
            int i = cs.Scroll + r;
            if (i >= cs.Items.Count) break;
            var it = cs.Items[i];
            bool sel = i == cs.Sel;
            int bg = sel ? th.PopupSel : th.PopupBg;
            sc.Fill(x, y + r, w, 1, new Style(th.Fg, bg));
            var kindColor = it.Kind switch
            {
                "Command" => th.Blue, "ParameterName" => th.Yellow, "Variable" => th.Cyan, "Property" or "Method" => th.Green1,
                "Type" => th.Teal, "ProviderItem" or "ProviderContainer" => th.Orange, "Keyword" => th.Magenta, _ => th.FgDark
            };
            sc.Put(x + 1, y + r, it.Display, new Style(sel ? th.Fg : th.FgDark, bg, sel ? Attr.Bold : Attr.None), x + w - (it.Kind?.Length ?? 0) - 2);
            if (!string.IsNullOrEmpty(it.Kind)) sc.Put(x + w - it.Kind.Length - 1, y + r, it.Kind, new Style(kindColor, bg, Attr.Italic));
        }
        if (cs.Items.Count > maxH)
        {
            int barH = Math.Max(1, maxH * maxH / cs.Items.Count);
            int barY = (int)((long)cs.Scroll * (maxH - barH) / Math.Max(1, cs.Items.Count - maxH));
            for (int r = 0; r < maxH; r++) sc.Set(x + w - 1, y + r, r >= barY && r < barY + barH ? "█" : "│", new Style(th.FgGutter, th.PopupBg));
        }
    }

    // =================================================================
    //  Komandna linija
    // =================================================================
    private string ReadLine(string prompt, string initial, List<string> history, Func<string, int, List<string>> completer = null, Action<string> onChange = null)
    {
        var sb = new StringBuilder(initial ?? "");
        int c = sb.Length;
        int hidx = history?.Count ?? 0;
        string typed = null;
        List<string> cands = null; int candIdx = -1; int candStart = 0; string candTail = null;
        var prevMode = Mode;
        if (prompt == ":" ) Mode = Mode.Command;
        _cmdlinePrompt = prompt;
        try
        {
            while (true)
            {
                _cmdlineOverlay = sb.ToString(); _cmdlineCursor = c;
                var k = NextKey();
                if (k != "<Tab>" && k != "<S-Tab>") cands = null;
                switch (k)
                {
                    case "<Esc>": case "<C-c>": return null;
                    case "<CR>":
                        {
                            var res = sb.ToString();
                            if (history != null && res.Length > 0)
                            {
                                history.Remove(res);
                                history.Add(res);
                            }
                            return res;
                        }
                    case "<BS>": case "<C-h>":
                        if (sb.Length == 0) return null;
                        if (c > 0) { sb.Remove(c - 1, 1); c--; }
                        break;
                    case "<Del>": if (c < sb.Length) sb.Remove(c, 1); break;
                    case "<Left>": if (c > 0) c--; break;
                    case "<Right>": if (c < sb.Length) c++; break;
                    case "<Home>": case "<C-b>": c = 0; break;
                    case "<End>": case "<C-e>": c = sb.Length; break;
                    case "<C-w>":
                        {
                            int s = c; while (s > 0 && sb[s - 1] == ' ') s--;
                            while (s > 0 && sb[s - 1] != ' ' && sb[s - 1] != '/' && sb[s - 1] != '\\') s--;
                            sb.Remove(s, c - s); c = s; break;
                        }
                    case "<C-u>": sb.Remove(0, c); c = 0; break;
                    case "<C-r>":
                        {
                            var r = NextKey();
                            string ins = r == "<C-w>" ? WordUnderCursor() : r.Length == 1 ? GetReg(r[0] == '"' ? null : r[0]).Text : "";
                            ins = ins.Replace("\n", " ");
                            sb.Insert(c, ins); c += ins.Length; break;
                        }
                    case "<Up>": case "<Down>":
                        {
                            if (history == null || history.Count == 0) break;
                            typed ??= sb.ToString();
                            int d = k == "<Up>" ? -1 : 1;
                            int i = hidx + d;
                            while (i >= 0 && i < history.Count && !history[i].StartsWith(typed)) i += d;
                            if (i < 0) break;
                            hidx = i;
                            sb.Clear();
                            sb.Append(i >= history.Count ? typed : history[i]);
                            c = sb.Length;
                            break;
                        }
                    case "<Tab>": case "<S-Tab>":
                        {
                            if (completer == null) break;
                            if (cands == null)
                            {
                                var text = sb.ToString().Substring(0, c);
                                cands = completer(text, c);
                                candIdx = -1;
                                // početak tokena
                                candStart = Math.Max(text.LastIndexOf(' '), 0);
                                if (text.LastIndexOf(' ') >= 0) candStart++;
                                candTail = sb.ToString().Substring(c);
                                if (cands == null || cands.Count == 0) { cands = null; break; }
                            }
                            candIdx = (candIdx + (k == "<Tab>" ? 1 : -1) + cands.Count) % cands.Count;
                            var pick = cands[candIdx];
                            sb.Remove(candStart, sb.Length - candStart);
                            sb.Append(pick);
                            c = sb.Length;
                            sb.Append(candTail);
                            if (cands.Count > 1) Notify(string.Join("  ", cands.Take(12)) + (cands.Count > 12 ? " …" : ""));
                            break;
                        }
                    default:
                        if (Keys.IsPrintable(k)) { sb.Insert(c, k); c += k.Length; }
                        break;
                }
                onChange?.Invoke(sb.ToString());
            }
        }
        finally
        {
            _cmdlineOverlay = null;
            _cmdlinePrompt = null;
            if (Mode == Mode.Command) Mode = prevMode == Mode.Command ? Mode.Normal : prevMode;
        }
    }

    private string ReadCmd(string prompt, string initial = "") => ReadLine(prompt, initial, _cmdHistory, CompleteEx);

    private string ReadSearch(string k)
    {
        var start = Cur; var top = Buf.Top;
        var res = ReadLine(k, "", _searchHistory, null, text =>
        {
            if (!Opt.IncSearch) return;
            _incSearch = text.Length > 0 ? text : null;
            if (text.Length == 0) { Cur = start; Buf.Top = top; return; }
            try
            {
                var q = Search(text, k == "/", start);
                if (q != null) Cur = q.Value; else Cur = start;
            }
            catch { }
        });
        _incSearch = null;
        Cur = start; Buf.Top = top;
        return res;
    }

    /// <summary>Pita korisnika za tekst u donjoj liniji.</summary>
    public string Prompt(string question, string initial = "")
    {
        return ReadLine(question + " ", initial, null, (t, c) => CompletePath(t));
    }

    public bool Confirm(string question)
    {
        _cmdlinePrompt = question + " [y/N] ";
        _cmdlineOverlay = "";
        _cmdlineCursor = 0;
        try
        {
            var k = NextKey();
            return k == "y" || k == "Y" || k == "d" || k == "D";
        }
        finally { _cmdlineOverlay = null; _cmdlinePrompt = null; }
    }

    private static readonly string[] ExCommands =
    {
        "write","quit","wq","wall","qall","xit","xall","wqall","edit","enew","bnext","bprevious","bdelete","buffer","buffers","ls",
        "set","nohlsearch","colorscheme","sort","substitute","global","vglobal","normal","delete","yank","help","cd","pwd","read",
        "Explore","Neotree","Format","Lazy","messages","registers","marks","jumps","terminal","files","grep","oldfiles","checkhealth",
        "diagnostics","make","only","new","saveas","update","retab","undo","redo"
    };

    private List<string> CompleteEx(string text, int c)
    {
        int sp = text.IndexOf(' ');
        if (sp < 0)
        {
            var pre = text.TrimStart(':');
            return ExCommands.Where(x => x.StartsWith(pre, StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.Length).ToList();
        }
        var cmd = text.Substring(0, sp);
        var arg = text.Substring(sp + 1);
        switch (cmd)
        {
            case "set": case "se":
                {
                    var opts = new[] { "number", "relativenumber", "cursorline", "expandtab", "tabstop=", "shiftwidth=", "ignorecase", "smartcase", "hlsearch", "incsearch", "list", "autopairs", "nerdfont", "hidden", "scrolloff=", "clipboard=" };
                    var last = arg.Split(' ').Last();
                    return opts.Where(o => o.StartsWith(last) || ("no" + o).StartsWith(last)).ToList();
                }
            case "colorscheme": case "colo":
                return Theme.Names.Where(n => n.StartsWith(arg)).ToList();
            case "b": case "buffer":
                return Buffers.Select(b => b.DisplayName).Where(n => n.StartsWith(arg, StringComparison.OrdinalIgnoreCase)).ToList();
            default:
                return CompletePath(arg);
        }
    }

    private List<string> CompletePath(string arg)
    {
        try
        {
            var expanded = arg.StartsWith("~") ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + arg.Substring(1) : arg;
            string dirPart = "", prefix = expanded;
            int slash = Math.Max(expanded.LastIndexOf('/'), expanded.LastIndexOf('\\'));
            if (slash >= 0) { dirPart = expanded.Substring(0, slash + 1); prefix = expanded.Substring(slash + 1); }
            var baseDir = Path.IsPathRooted(dirPart) ? dirPart : Path.Combine(Cwd, dirPart);
            if (!Directory.Exists(baseDir)) return new List<string>();
            var res = new List<string>();
            foreach (var e in Directory.EnumerateFileSystemEntries(baseDir))
            {
                var name = Path.GetFileName(e);
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (name.StartsWith('.') && !prefix.StartsWith('.')) continue;
                var shown = (slash >= 0 ? arg.Substring(0, arg.Length - prefix.Length) : "") + name;
                if (Directory.Exists(e)) shown += Path.DirectorySeparatorChar;
                res.Add(shown);
            }
            res.Sort(StringComparer.OrdinalIgnoreCase);
            return res;
        }
        catch { return new List<string>(); }
    }
}
