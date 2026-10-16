namespace PsVim;

public sealed class Range
{
    public Pos A, B;
    public bool Linewise, Inclusive, KeepWant, Jump;
}

public sealed partial class Editor
{
    // klase karaktera: 0 razmak, 1 reč, 2 interpunkcija
    private static int Cls(char c, bool big)
    {
        if (char.IsWhiteSpace(c)) return 0;
        if (big) return 1;
        return char.IsLetterOrDigit(c) || c == '_' ? 1 : 2;
    }

    private Range R(Pos to, bool lw = false, bool inc = false) => new() { A = Cur, B = to, Linewise = lw, Inclusive = inc };

    /// <summary>Izvršava kretanje. Vraća null ako taster nije kretanje.</summary>
    private Range Motion(string k, int n, bool hadCount, bool forOp, bool visual)
    {
        var p = Cur; var b = Buf;
        switch (k)
        {
            case "h": case "<Left>": case "<BS>":
                return R(new Pos(p.L, Math.Max(0, p.C - n)));
            case "l": case "<Right>":
                {
                    int len = b.LineLen(p.L);
                    int max = forOp ? len : Math.Max(0, len - 1);
                    return R(new Pos(p.L, Math.Min(max, p.C + n)));
                }
            case "j": case "<Down>": case "gj":
                {
                    int l = Math.Min(b.Count - 1, p.L + n);
                    if (forOp && l == p.L) return null;
                    return new Range { A = p, B = new Pos(l, ColFromDisplay(b[l], b.WantCol)), Linewise = true, KeepWant = true };
                }
            case "k": case "<Up>": case "gk":
                {
                    int l = Math.Max(0, p.L - n);
                    if (forOp && l == p.L) return null;
                    return new Range { A = p, B = new Pos(l, ColFromDisplay(b[l], b.WantCol)), Linewise = true, KeepWant = true };
                }
            case "+": case "<CR>":
                { int l = Math.Min(b.Count - 1, p.L + n); return R(new Pos(l, b.FirstNonBlank(l)), true); }
            case "-":
                { int l = Math.Max(0, p.L - n); return R(new Pos(l, b.FirstNonBlank(l)), true); }
            case "_":
                { int l = Math.Min(b.Count - 1, p.L + n - 1); return R(new Pos(l, b.FirstNonBlank(l)), true); }
            case "0": case "<Home>": return R(new Pos(p.L, 0));
            case "^": return R(new Pos(p.L, FirstNonBlankCol(p.L)));
            case "|": return R(new Pos(p.L, Math.Min(Math.Max(0, b.LineLen(p.L) - 1), n - 1)));
            case "$": case "<End>":
                {
                    int l = Math.Min(b.Count - 1, p.L + n - 1);
                    var r = R(new Pos(l, Math.Max(0, b.LineLen(l) - 1)), false, true);
                    b.WantCol = int.MaxValue / 2; r.KeepWant = true;
                    return r;
                }
            case "g_":
                {
                    int l = Math.Min(b.Count - 1, p.L + n - 1);
                    var s = b[l]; int c = s.Length - 1; while (c > 0 && char.IsWhiteSpace(s[c])) c--;
                    return R(new Pos(l, Math.Max(0, c)), false, true);
                }
            case "w": case "W": { var q = p; for (int i = 0; i < n; i++) q = WordFwd(q, k == "W", forOp); return R(q); }
            case "b": case "B": { var q = p; for (int i = 0; i < n; i++) q = WordBack(q, k == "B"); return R(q); }
            case "e": case "E": { var q = p; for (int i = 0; i < n; i++) q = WordEnd(q, k == "E"); return R(q, false, true); }
            case "ge": case "gE": { var q = p; for (int i = 0; i < n; i++) q = WordEndBack(q, k == "gE"); return R(q, false, true); }
            case "G":
                {
                    int l = hadCount ? Math.Clamp(n - 1, 0, b.Count - 1) : b.Count - 1;
                    var r = R(new Pos(l, b.FirstNonBlank(l)), true); r.Jump = true; return r;
                }
            case "gg":
                {
                    int l = hadCount ? Math.Clamp(n - 1, 0, b.Count - 1) : 0;
                    var r = R(new Pos(l, b.FirstNonBlank(l)), true); r.Jump = true; return r;
                }
            case "f": case "F": case "t": case "T":
                {
                    var ck = NextKey();
                    if (!Keys.IsPrintable(ck)) return null;
                    _lastFind = k; _lastFindChar = ck[0];
                    var q = FindChar(k, ck[0], n, false);
                    return q == null ? null : R(q.Value, false, k == "f" || k == "t");
                }
            case ";": case ",":
                {
                    if (_lastFind == null) return null;
                    var kind = _lastFind;
                    if (k == ",") kind = kind switch { "f" => "F", "F" => "f", "t" => "T", _ => "t" };
                    var q = FindChar(kind, _lastFindChar, n, true);
                    return q == null ? null : R(q.Value, false, kind == "f" || kind == "t");
                }
            case "%":
                {
                    if (hadCount) { int l = Math.Clamp((n * b.Count + 99) / 100 - 1, 0, b.Count - 1); return R(new Pos(l, b.FirstNonBlank(l)), true); }
                    var q = MatchBracket(p);
                    if (q == null) return null;
                    var r = R(q.Value, false, true); r.Jump = true; return r;
                }
            case "}":
                {
                    int l = p.L;
                    for (int i = 0; i < n; i++)
                    {
                        while (l < b.Count - 1 && b[l].Trim().Length == 0) l++;
                        while (l < b.Count - 1 && b[l].Trim().Length > 0) l++;
                    }
                    var to = l == b.Count - 1 && b[l].Trim().Length > 0 ? new Pos(l, Math.Max(0, b.LineLen(l) - (forOp ? 0 : 1))) : new Pos(l, 0);
                    var r = R(to); r.Jump = true; return r;
                }
            case "{":
                {
                    int l = p.L;
                    for (int i = 0; i < n; i++)
                    {
                        while (l > 0 && b[l].Trim().Length == 0) l--;
                        while (l > 0 && b[l].Trim().Length > 0) l--;
                    }
                    var r = R(new Pos(l, 0)); r.Jump = true; return r;
                }
            case "M":
                {
                    int l = Math.Min(b.Count - 1, b.Top + Math.Min(_textH, b.Count - b.Top) / 2);
                    return R(new Pos(l, b.FirstNonBlank(l)), true);
                }
            case "n": case "N":
                {
                    if (string.IsNullOrEmpty(_lastSearch)) throw new VimError("E35: Nema prethodnog šablona");
                    bool fwd = k == "n" ? _lastSearchFwd : !_lastSearchFwd;
                    var q = Search(_lastSearch, fwd, p, n);
                    _hlActive = true;
                    if (q == null) throw new VimError("E486: Šablon nije pronađen: " + _lastSearch);
                    var r = R(q.Value); r.Jump = true; return r;
                }
            case "*": case "#":
                {
                    var w = WordUnderCursor();
                    if (string.IsNullOrEmpty(w)) throw new VimError("E348: Nema reči pod kursorom");
                    _lastSearch = @"\b" + System.Text.RegularExpressions.Regex.Escape(w) + @"\b";
                    _lastSearchFwd = k == "*";
                    _hlActive = true;
                    var start = WordStartAtCursor();
                    var q = Search(_lastSearch, _lastSearchFwd, start, n);
                    if (q == null) return null;
                    var r = R(q.Value); r.Jump = true; return r;
                }
            case "/": case "?":
                {
                    var pat = ReadSearch(k);
                    if (pat == null) return null;
                    if (pat.Length == 0) pat = _lastSearch;
                    if (string.IsNullOrEmpty(pat)) return null;
                    _lastSearch = pat; _lastSearchFwd = k == "/";
                    _hlActive = true;
                    var q = Search(pat, _lastSearchFwd, p, n);
                    if (q == null) throw new VimError("E486: Šablon nije pronađen: " + pat);
                    var r = R(q.Value); r.Jump = true; return r;
                }
            case "`": case "'":
                {
                    var mk = NextKey();
                    if (mk.Length != 1) return null;
                    Pos mp;
                    if (mk == "`" || mk == "'") { if (_jumps.Count == 0) return null; mp = _jumps[Math.Max(0, Math.Min(_jumpIdx, _jumps.Count - 1))].P; }
                    else if (mk == "<" && _lastVisual != null) mp = _lastVisual.Value.A < _lastVisual.Value.B ? _lastVisual.Value.A : _lastVisual.Value.B;
                    else if (mk == ">" && _lastVisual != null) mp = _lastVisual.Value.A < _lastVisual.Value.B ? _lastVisual.Value.B : _lastVisual.Value.A;
                    else if (!b.Marks.TryGetValue(mk[0], out mp)) throw new VimError("E20: Oznaka nije postavljena");
                    mp = b.Clamp(mp);
                    var r = k == "'" ? R(new Pos(mp.L, b.FirstNonBlank(mp.L)), true) : R(mp);
                    r.Jump = true; return r;
                }
            case "s":
                {
                    var q = Flash();
                    if (q == null) return null;
                    var r = R(q.Value, false, forOp); r.Jump = true; return r;
                }
            case "i": case "a":
                if (!forOp && !visual) return null;
                {
                    var ok = NextKey();
                    return TextObject(k == "i", ok, n);
                }
        }
        return null;
    }

    private int FirstNonBlankCol(int l)
    {
        var s = Buf[l]; int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        return i >= s.Length ? Math.Max(0, s.Length - 1) : i;
    }

    // ---------------- reči ----------------
    private Pos WordFwd(Pos p, bool big, bool forOp)
    {
        var b = Buf;
        var s = b[p.L];
        if (p.C < s.Length)
        {
            int cls = Cls(s[p.C], big);
            if (cls != 0) while (p.C < s.Length && Cls(s[p.C], big) == cls) p.C++;
        }
        while (true)
        {
            if (p.C >= s.Length)
            {
                if (p.L + 1 >= b.Count) { p.C = s.Length; return forOp ? p : new Pos(p.L, Math.Max(0, s.Length - 1)); }
                p.L++; p.C = 0; s = b[p.L];
                if (s.Length == 0) return p;
                continue;
            }
            if (char.IsWhiteSpace(s[p.C])) { p.C++; continue; }
            return p;
        }
    }

    private Pos WordEnd(Pos p, bool big)
    {
        var b = Buf;
        // pomeri se za jedan
        if (!Step(ref p)) return p;
        // preskoči razmake (i prazne linije)
        while (CharAt(p) is char c && char.IsWhiteSpace(c) || (b.LineLen(p.L) == 0 && p.L < b.Count - 1))
        {
            if (!Step(ref p)) return p;
        }
        var s = b[p.L];
        if (p.C >= s.Length) return p;
        int cls = Cls(s[p.C], big);
        while (p.C + 1 < s.Length && Cls(s[p.C + 1], big) == cls) p.C++;
        return p;
    }

    private Pos WordBack(Pos p, bool big)
    {
        var b = Buf;
        if (!StepBack(ref p)) return p;
        while (true)
        {
            var s = b[p.L];
            if (s.Length == 0) return p; // prazna linija je reč
            if (p.C < s.Length && char.IsWhiteSpace(s[p.C])) { if (!StepBack(ref p)) return p; continue; }
            break;
        }
        var line = b[p.L];
        int cls = Cls(line[p.C], big);
        while (p.C > 0 && Cls(line[p.C - 1], big) == cls) p.C--;
        return p;
    }

    private Pos WordEndBack(Pos p, bool big)
    {
        var b = Buf;
        var s = b[p.L];
        if (p.C < s.Length)
        {
            int cls = Cls(s[p.C], big);
            while (p.C > 0 && Cls(s[p.C - 1], big) == cls) p.C--;
        }
        if (!StepBack(ref p)) return p;
        while (true)
        {
            s = b[p.L];
            if (s.Length == 0) return p;
            if (char.IsWhiteSpace(s[p.C])) { if (!StepBack(ref p)) return p; continue; }
            return p;
        }
    }

    private char? CharAt(Pos p) { var s = Buf[p.L]; return p.C < s.Length ? s[p.C] : null; }

    /// <summary>Jedan karakter napred, prelazeći u sledeću liniju. Vraća false na kraju bafera.</summary>
    private bool Step(ref Pos p)
    {
        var s = Buf[p.L];
        if (p.C + 1 < s.Length) { p.C++; return true; }
        if (p.L + 1 >= Buf.Count) return false;
        p.L++; p.C = 0; return true;
    }

    private bool StepBack(ref Pos p)
    {
        if (p.C > 0) { p.C = Math.Min(p.C - 1, Math.Max(0, Buf.LineLen(p.L) - 1)); return true; }
        if (p.L == 0) return false;
        p.L--; p.C = Math.Max(0, Buf.LineLen(p.L) - 1); return true;
    }

    private Pos? FindChar(string kind, char c, int n, bool repeat)
    {
        var s = Line; int col = Cur.C;
        for (int i = 0; i < n; i++)
        {
            int found = -1;
            if (kind == "f" || kind == "t")
            {
                int start = col + 1 + (kind == "t" && repeat && i == 0 ? 1 : 0);
                for (int j = start; j < s.Length; j++) if (s[j] == c) { found = j; break; }
                if (found < 0) return null;
                col = kind == "t" ? found - 1 : found;
                if (kind == "t" && i < n - 1) col = found;
            }
            else
            {
                int start = col - 1 - (kind == "T" && repeat && i == 0 ? 1 : 0);
                for (int j = start; j >= 0; j--) if (s[j] == c) { found = j; break; }
                if (found < 0) return null;
                col = kind == "T" ? found + 1 : found;
                if (kind == "T" && i < n - 1) col = found;
            }
        }
        return new Pos(Cur.L, col);
    }

    private Pos? MatchBracket(Pos p)
    {
        const string open = "([{", close = ")]}";
        var s = Buf[p.L];
        int c = p.C;
        while (c < s.Length && open.IndexOf(s[c]) < 0 && close.IndexOf(s[c]) < 0) c++;
        if (c >= s.Length) return null;
        char ch = s[c];
        int oi = open.IndexOf(ch);
        if (oi >= 0) return ScanBracket(new Pos(p.L, c), ch, close[oi], true);
        int ci = close.IndexOf(ch);
        return ScanBracket(new Pos(p.L, c), ch, open[ci], false);
    }

    private Pos? ScanBracket(Pos from, char self, char other, bool fwd)
    {
        int depth = 0;
        var b = Buf;
        int l = from.L, c = from.C;
        while (l >= 0 && l < b.Count)
        {
            var s = b[l];
            if (fwd)
            {
                for (; c < s.Length; c++)
                {
                    if (s[c] == self) depth++;
                    else if (s[c] == other) { depth--; if (depth == 0) return new Pos(l, c); }
                }
                l++; c = 0;
            }
            else
            {
                if (c >= s.Length) c = s.Length - 1;
                for (; c >= 0; c--)
                {
                    if (s[c] == self) depth++;
                    else if (s[c] == other) { depth--; if (depth == 0) return new Pos(l, c); }
                }
                l--; if (l >= 0) c = b[l].Length - 1;
            }
        }
        return null;
    }

    /// <summary>Nalazi otvorenu zagradu koja okružuje poziciju.</summary>
    private Pos? EnclosingOpen(Pos p, char open, char close)
    {
        var b = Buf;
        int depth = 0;
        int l = p.L, c = p.C;
        var s0 = b[l];
        if (c < s0.Length && s0[c] == open) return new Pos(l, c);
        if (c < s0.Length && s0[c] == close) c--;
        while (l >= 0)
        {
            var s = b[l];
            if (c >= s.Length) c = s.Length - 1;
            for (; c >= 0; c--)
            {
                if (s[c] == close) depth++;
                else if (s[c] == open) { if (depth == 0) return new Pos(l, c); depth--; }
            }
            l--; if (l >= 0) c = b[l].Length - 1;
        }
        return null;
    }

    // ---------------- tekst objekti ----------------
    private Range TextObject(bool inner, string k, int n)
    {
        var b = Buf; var p = Cur;
        switch (k)
        {
            case "w": case "W":
                {
                    bool big = k == "W";
                    var s = b[p.L];
                    if (s.Length == 0) return new Range { A = p, B = p, Inclusive = true };
                    int c = Math.Min(p.C, s.Length - 1);
                    int cls = Cls(s[c], big);
                    int st = c, en = c;
                    while (st > 0 && Cls(s[st - 1], big) == cls) st--;
                    while (en + 1 < s.Length && Cls(s[en + 1], big) == cls) en++;
                    for (int i = 1; i < n; i++)
                    {
                        if (en + 1 >= s.Length) break;
                        int c2 = Cls(s[en + 1], big);
                        en++; while (en + 1 < s.Length && Cls(s[en + 1], big) == c2) en++;
                    }
                    if (!inner)
                    {
                        if (cls != 0)
                        {
                            int e2 = en;
                            while (e2 + 1 < s.Length && char.IsWhiteSpace(s[e2 + 1])) e2++;
                            if (e2 > en) en = e2;
                            else while (st > 0 && char.IsWhiteSpace(s[st - 1])) st--;
                        }
                        else
                        {
                            if (en + 1 < s.Length) { int c2 = Cls(s[en + 1], big); en++; while (en + 1 < s.Length && Cls(s[en + 1], big) == c2) en++; }
                        }
                    }
                    return new Range { A = new Pos(p.L, st), B = new Pos(p.L, en), Inclusive = true };
                }
            case "\"": case "'": case "`":
                {
                    char q = k[0];
                    var s = b[p.L];
                    var qs = new List<int>();
                    for (int i = 0; i < s.Length; i++)
                    {
                        if (s[i] == q && !(i > 0 && (s[i - 1] == '\\' || s[i - 1] == '`') && q != '`')) qs.Add(i);
                    }
                    int a = -1, e = -1;
                    for (int i = 0; i + 1 < qs.Count; i += 2)
                    {
                        if (p.C >= qs[i] && p.C <= qs[i + 1]) { a = qs[i]; e = qs[i + 1]; break; }
                        if (qs[i] > p.C) { a = qs[i]; e = qs[i + 1]; break; }
                    }
                    if (a < 0) return null;
                    if (inner)
                    {
                        if (e == a + 1) return new Range { A = new Pos(p.L, a + 1), B = new Pos(p.L, a + 1), Inclusive = false };
                        return new Range { A = new Pos(p.L, a + 1), B = new Pos(p.L, e - 1), Inclusive = true };
                    }
                    int ee = e; while (ee + 1 < s.Length && s[ee + 1] == ' ') ee++;
                    return new Range { A = new Pos(p.L, a), B = new Pos(p.L, ee), Inclusive = true };
                }
            case "(": case ")": case "b": return BracketObject('(', ')', inner, n);
            case "{": case "}": case "B": return BracketObject('{', '}', inner, n);
            case "[": case "]": return BracketObject('[', ']', inner, n);
            case "<": case ">": return BracketObject('<', '>', inner, n);
            case "p":
                {
                    int l1 = p.L, l2 = p.L;
                    bool blank = b[p.L].Trim().Length == 0;
                    while (l1 > 0 && (b[l1 - 1].Trim().Length == 0) == blank) l1--;
                    while (l2 < b.Count - 1 && (b[l2 + 1].Trim().Length == 0) == blank) l2++;
                    if (!inner)
                    {
                        int e = l2;
                        while (e < b.Count - 1 && b[e + 1].Trim().Length == 0) e++;
                        if (e == l2) while (l1 > 0 && b[l1 - 1].Trim().Length == 0) l1--;
                        l2 = e;
                    }
                    return new Range { A = new Pos(l1, 0), B = new Pos(l2, 0), Linewise = true };
                }
            case "g": case "e":
                return new Range { A = new Pos(0, 0), B = new Pos(b.Count - 1, 0), Linewise = true };
            case "f":
                {
                    // funkcija (PowerShell: function/filter ... { ... })
                    var rx = new System.Text.RegularExpressions.Regex(@"^\s*(function|filter|def|func|fn|sub)\b|\bfunction\b|=>|\)\s*\{\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    for (int l = p.L; l >= 0; l--)
                    {
                        if (!rx.IsMatch(b[l])) continue;
                        // nađi { od te linije
                        int bl = l, bc = b[l].IndexOf('{');
                        while (bc < 0 && bl + 1 < b.Count && bl < l + 5) { bl++; bc = b[bl].IndexOf('{'); }
                        if (bc < 0) continue;
                        var close = ScanBracket(new Pos(bl, bc), '{', '}', true);
                        if (close == null || close.Value.L < p.L) continue;
                        if (inner)
                        {
                            if (close.Value.L - bl >= 2) return new Range { A = new Pos(bl + 1, 0), B = new Pos(close.Value.L - 1, 0), Linewise = true };
                            return new Range { A = new Pos(bl, bc + 1), B = new Pos(close.Value.L, close.Value.C - 1), Inclusive = true };
                        }
                        return new Range { A = new Pos(l, 0), B = new Pos(close.Value.L, 0), Linewise = true };
                    }
                    return null;
                }
        }
        return null;
    }

    private Range BracketObject(char open, char close, bool inner, int n)
    {
        var p = Cur;
        Pos? o = EnclosingOpen(p, open, close);
        for (int i = 1; i < n && o != null; i++)
            o = o.Value.C > 0 ? EnclosingOpen(new Pos(o.Value.L, o.Value.C - 1), open, close) : (o.Value.L > 0 ? EnclosingOpen(new Pos(o.Value.L - 1, Math.Max(0, Buf.LineLen(o.Value.L - 1) - 1)), open, close) : null);
        if (o == null)
        {
            // kao u mini.ai: traži sledeći par u liniji
            var s = Line;
            int idx = s.IndexOf(open, p.C);
            if (idx < 0) return null;
            o = new Pos(p.L, idx);
        }
        var c = ScanBracket(o.Value, open, close, true);
        if (c == null) return null;
        var a = o.Value; var e = c.Value;
        if (!inner) return new Range { A = a, B = e, Inclusive = true };
        // unutrašnjost bloka u više linija => linijski
        var b = Buf;
        bool openAtEol = b[a.L].Substring(a.C + 1).Trim().Length == 0;
        bool closeAtBol = b[e.L].Substring(0, e.C).Trim().Length == 0;
        if (e.L > a.L && openAtEol && closeAtBol)
        {
            if (e.L - a.L == 1) return new Range { A = new Pos(a.L, a.C + 1), B = new Pos(a.L, a.C + 1), Inclusive = false };
            return new Range { A = new Pos(a.L + 1, 0), B = new Pos(e.L - 1, 0), Linewise = true };
        }
        var start = new Pos(a.L, a.C + 1);
        if (start.C >= b.LineLen(a.L) && a.L < e.L) start = new Pos(a.L + 1, 0);
        if (e.C == a.C + 1 && e.L == a.L) return new Range { A = start, B = start, Inclusive = false };
        var end = e.C > 0 ? new Pos(e.L, e.C - 1) : new Pos(e.L - 1, Math.Max(0, b.LineLen(e.L - 1)));
        return new Range { A = start, B = end, Inclusive = e.C > 0 };
    }

    // ---------------- reč pod kursorom ----------------
    private string WordUnderCursor(bool big = false)
    {
        var s = Line;
        if (s.Length == 0) return "";
        int c = Math.Min(Cur.C, s.Length - 1);
        // ako smo na razmaku/interpunkciji, nađi sledeću reč u liniji
        while (c < s.Length && Cls(s[c], big) != 1) c++;
        if (c >= s.Length) return "";
        int st = c, en = c;
        bool Word(char ch) => big ? !char.IsWhiteSpace(ch) : (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' && Buf.FileType == "powershell");
        while (st > 0 && Word(s[st - 1])) st--;
        while (en + 1 < s.Length && Word(s[en + 1])) en++;
        return s.Substring(st, en - st + 1).Trim('-');
    }

    private Pos WordStartAtCursor()
    {
        var s = Line; int c = Math.Min(Cur.C, Math.Max(0, s.Length - 1));
        while (c > 0 && c < s.Length && Cls(s[c - 1], false) == 1 && Cls(s[c], false) == 1) c--;
        return new Pos(Cur.L, c);
    }

    // ---------------- pretraga ----------------
    private Pos? Search(string pat, bool fwd, Pos from, int count = 1)
    {
        var rx = SafeRegex(pat);
        if (rx == null) throw new VimError("E383: Neispravan šablon: " + pat);
        var b = Buf;
        var p = from;
        for (int i = 0; i < count; i++)
        {
            var q = SearchOnce(rx, fwd, p);
            if (q == null) return null;
            p = q.Value;
        }
        return p;
    }

    private Pos? SearchOnce(System.Text.RegularExpressions.Regex rx, bool fwd, Pos from)
    {
        var b = Buf;
        int n = b.Count;
        if (fwd)
        {
            for (int k = 0; k <= n; k++)
            {
                int l = (from.L + k) % n;
                var s = b[l];
                int startCol = k == 0 ? from.C + 1 : 0;
                if (k == n) { if (from.C < 0) break; startCol = 0; }
                if (startCol > s.Length) continue;
                var m = rx.Match(s, startCol);
                while (m.Success && m.Length == 0 && m.Index < s.Length) m = m.NextMatch();
                if (m.Success && (k < n || m.Index <= from.C))
                {
                    if (l < from.L || (l == from.L && k > 0)) Notify("pretraga je stigla do DNA, nastavljam od VRHA");
                    return new Pos(l, m.Index);
                }
            }
        }
        else
        {
            for (int k = 0; k <= n; k++)
            {
                int l = ((from.L - k) % n + n) % n;
                var s = b[l];
                int last = -1;
                foreach (System.Text.RegularExpressions.Match m in rx.Matches(s))
                {
                    if (m.Length == 0) continue;
                    if (k == 0 && m.Index >= from.C) break;
                    last = m.Index;
                }
                if (last >= 0)
                {
                    if (l > from.L || (l == from.L && k > 0)) Notify("pretraga je stigla do VRHA, nastavljam od DNA");
                    return new Pos(l, last);
                }
            }
        }
        return null;
    }
}
