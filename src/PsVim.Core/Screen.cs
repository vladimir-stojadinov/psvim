using System.Text;

namespace PsVim;

[Flags]
public enum Attr : byte { None = 0, Bold = 1, Italic = 2, Underline = 4, Reverse = 8, Strike = 16 }

public readonly struct Style : IEquatable<Style>
{
    public readonly int Fg, Bg; // 0xRRGGBB, -1 = podrazumevano
    public readonly Attr Attr;
    public Style(int fg, int bg, Attr a = Attr.None) { Fg = fg; Bg = bg; Attr = a; }
    public Style WithBg(int bg) => new(Fg, bg, Attr);
    public Style WithFg(int fg) => new(fg, Bg, Attr);
    public Style With(Attr a) => new(Fg, Bg, Attr | a);
    public bool Equals(Style o) => Fg == o.Fg && Bg == o.Bg && Attr == o.Attr;
    public override bool Equals(object obj) => obj is Style s && Equals(s);
    public override int GetHashCode() => Compat.Hash(Fg, Bg, (int)Attr);
}

public enum CursorShape { Block, Bar, Underline, Hidden }

/// <summary>Dvostruko baferovan ekran: crta se u memoriju, a u terminal se šalju samo razlike.</summary>
public sealed class Screen
{
    private struct Cell { public string T; public Style S; }
    private Cell[] _cur = Array.Empty<Cell>(), _prev = Array.Empty<Cell>();
    private readonly ITerminal _term;
    private bool _full = true;
    public int W { get; private set; }
    public int H { get; private set; }
    public int CursorX, CursorY;
    public CursorShape Shape = CursorShape.Block;
    private CursorShape _lastShape = (CursorShape)(-1);

    public Screen(ITerminal t) { _term = t; }

    public void Resize(int w, int h)
    {
        if (w == W && h == H) return;
        W = w; H = h;
        _cur = new Cell[w * h];
        _prev = new Cell[w * h];
        _full = true;
    }

    public void Invalidate() => _full = true;

    public void Clear(Style s)
    {
        for (int i = 0; i < _cur.Length; i++) { _cur[i].T = " "; _cur[i].S = s; }
    }

    public void Set(int x, int y, string t, Style s)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return;
        ref var c = ref _cur[y * W + x];
        c.T = Sanitize(t); c.S = s;
    }

    /// <summary>
    /// Garantuje da svaka ćelija zauzima tačno jednu kolonu u terminalu:
    /// kontrolni, nevidljivi i kombinujući znakovi postaju '?', a široki (CJK, emoji) '□'.
    /// Bez ovoga terminal i model ekrana se raziđu i ostaju artefakti.
    /// </summary>
    public static string Sanitize(string t)
    {
        if (string.IsNullOrEmpty(t)) return " ";
        if (t.Length == 1)
        {
            char ch = t[0];
            if (ch < 0x7f) return ch < ' ' ? "?" : t;
            if (ch < 0xa0 || char.IsSurrogate(ch)) return "?";
            switch (char.GetUnicodeCategory(ch))
            {
                case System.Globalization.UnicodeCategory.Control:
                case System.Globalization.UnicodeCategory.Format:
                case System.Globalization.UnicodeCategory.NonSpacingMark:
                case System.Globalization.UnicodeCategory.EnclosingMark:
                case System.Globalization.UnicodeCategory.SpacingCombiningMark:
                case System.Globalization.UnicodeCategory.LineSeparator:
                case System.Globalization.UnicodeCategory.ParagraphSeparator:
                case System.Globalization.UnicodeCategory.PrivateUse when ch < 0xe000:
                    return "?";
            }
            return IsWide(ch) ? "□" : t;
        }
        // par surogata (emoji i sl.) — skoro uvek dve kolone
        if (t.Length == 2 && char.IsSurrogatePair(t[0], t[1]))
        {
            int cp = char.ConvertToUtf32(t[0], t[1]);
            if (cp >= 0xF0000) return t; // Nerd Font ikonice (privatna zona) su jedna kolona
            return "□";
        }
        return "?";
    }

    private static bool IsWide(char c) =>
        (c >= 0x1100 && c <= 0x115F) || (c >= 0x2E80 && c <= 0x303E) || (c >= 0x3041 && c <= 0x33FF) ||
        (c >= 0x3400 && c <= 0x4DBF) || (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0xA000 && c <= 0xA4CF) ||
        (c >= 0xAC00 && c <= 0xD7A3) || (c >= 0xF900 && c <= 0xFAFF) || (c >= 0xFE30 && c <= 0xFE4F) ||
        (c >= 0xFF00 && c <= 0xFF60) || (c >= 0xFFE0 && c <= 0xFFE6) ||
        c == 0x231A || c == 0x231B || c == 0x23E9 || c == 0x23EA || c == 0x23EB || c == 0x23EC || c == 0x23F0 || c == 0x23F3 ||
        c == 0x25FD || c == 0x25FE || c == 0x2614 || c == 0x2615 || (c >= 0x2648 && c <= 0x2653) || c == 0x267F ||
        c == 0x2693 || c == 0x26A1 || c == 0x26AA || c == 0x26AB || c == 0x26BD || c == 0x26BE || c == 0x26C4 || c == 0x26C5 ||
        c == 0x26CE || c == 0x26D4 || c == 0x26EA || c == 0x26F2 || c == 0x26F3 || c == 0x26F5 || c == 0x26FA || c == 0x26FD ||
        c == 0x2705 || c == 0x270A || c == 0x270B || c == 0x2728 || c == 0x274C || c == 0x274E || (c >= 0x2753 && c <= 0x2755) ||
        c == 0x2757 || (c >= 0x2795 && c <= 0x2797) || c == 0x27B0 || c == 0x27BF || c == 0x2B1B || c == 0x2B1C || c == 0x2B50 || c == 0x2B55;

    public void SetStyle(int x, int y, Style s)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return;
        _cur[y * W + x].S = s;
    }

    public void SetBg(int x, int y, int bg)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return;
        ref var c = ref _cur[y * W + x];
        c.S = c.S.WithBg(bg);
    }

    public string GetText(int x, int y) => (x < 0 || y < 0 || x >= W || y >= H) ? "" : _cur[y * W + x].T;
    public Style GetStyle(int x, int y) => (x < 0 || y < 0 || x >= W || y >= H) ? default : _cur[y * W + x].S;

    /// <summary>Upisuje tekst; vraća x posle poslednjeg upisanog znaka.</summary>
    public int Put(int x, int y, string s, Style st, int maxX = int.MaxValue)
    {
        if (s == null) return x;
        maxX = Math.Min(maxX, W);
        for (int i = 0; i < s.Length && x < maxX; i++)
        {
            string t;
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length) { t = s.Substring(i, 2); i++; }
            else t = s[i] < ' ' ? "?" : s[i].ToString();
            Set(x, y, t, st);
            x++;
        }
        return x;
    }

    public void Fill(int x, int y, int w, int h, Style st, string ch = " ")
    {
        for (int yy = y; yy < y + h; yy++)
            for (int xx = x; xx < x + w; xx++)
                Set(xx, yy, ch, st);
    }

    public void Box(int x, int y, int w, int h, Style border, Style inside, string title = null, Style? titleStyle = null)
    {
        Fill(x, y, w, h, inside);
        for (int xx = x + 1; xx < x + w - 1; xx++) { Set(xx, y, "─", border); Set(xx, y + h - 1, "─", border); }
        for (int yy = y + 1; yy < y + h - 1; yy++) { Set(x, yy, "│", border); Set(x + w - 1, yy, "│", border); }
        Set(x, y, "╭", border); Set(x + w - 1, y, "╮", border);
        Set(x, y + h - 1, "╰", border); Set(x + w - 1, y + h - 1, "╯", border);
        if (!string.IsNullOrEmpty(title))
        {
            var t = " " + title + " ";
            int tx = x + Math.Max(1, (w - t.Length) / 2);
            Put(tx, y, t, titleStyle ?? border, x + w - 1);
        }
    }

    public string RowText(int y)
    {
        var sb = new StringBuilder();
        for (int x = 0; x < W; x++) sb.Append(_cur[y * W + x].T ?? " ");
        return sb.ToString().TrimEnd();
    }

    /// <summary>HTML snimak ekrana (za dokumentaciju i vizuelne testove).</summary>
    public string ToHtml()
    {
        var sb = new StringBuilder("<pre style=\"font-family:'Cascadia Mono',Consolas,monospace;font-size:14px;line-height:1.25;margin:0;display:inline-block\">");
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                var c = _cur[y * W + x];
                var s = c.S;
                int fg = s.Fg < 0 ? 0xc0caf5 : s.Fg, bg = s.Bg < 0 ? 0x1a1b26 : s.Bg;
                if ((s.Attr & Attr.Reverse) != 0) (fg, bg) = (bg, fg);
                if (x == CursorX && y == CursorY && Shape != CursorShape.Hidden) (fg, bg) = (bg, fg);
                var t = System.Net.WebUtility.HtmlEncode(c.T ?? " ");
                sb.Append($"<span style=\"color:#{fg:x6};background:#{bg:x6}");
                if ((s.Attr & Attr.Bold) != 0) sb.Append(";font-weight:bold");
                if ((s.Attr & Attr.Italic) != 0) sb.Append(";font-style:italic");
                if ((s.Attr & Attr.Underline) != 0) sb.Append(";text-decoration:underline");
                sb.Append("\">").Append(t).Append("</span>");
            }
            sb.Append('\n');
        }
        sb.Append("</pre>");
        return sb.ToString();
    }

    public void Flush()
    {
        var sb = new StringBuilder(W * H * 2);
        sb.Append("\x1b[?25l");
        if (_full) sb.Append("\x1b[0m\x1b[2J");
        Style? last = null;
        for (int y = 0; y < H; y++)
        {
            int lastX = -2;
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                var c = _cur[i];
                if (c.T == null) { c.T = " "; _cur[i].T = " "; }
                if (!_full && c.T == _prev[i].T && c.S.Equals(_prev[i].S)) continue;
                if (lastX != x - 1) sb.Append("\x1b[").Append(y + 1).Append(';').Append(x + 1).Append('H');
                if (last == null || !last.Value.Equals(c.S)) { AppendSgr(sb, c.S); last = c.S; }
                sb.Append(c.T);
                // posle ne-ASCII znaka uvek eksplicitno pozicioniraj kursor:
                // terminali se razlikuju u širini "dvosmislenih" znakova (●, ␣, │...)
                lastX = (c.T.Length == 1 && c.T[0] < 0x7f) ? x : -2;
                _prev[i] = c;
            }
        }
        _full = false;
        sb.Append("\x1b[0m");
        if (Shape != _lastShape)
        {
            sb.Append(Shape switch { CursorShape.Bar => "\x1b[6 q", CursorShape.Underline => "\x1b[4 q", _ => "\x1b[2 q" });
            _lastShape = Shape;
        }
        sb.Append("\x1b[").Append(CursorY + 1).Append(';').Append(CursorX + 1).Append('H');
        if (Shape != CursorShape.Hidden) sb.Append("\x1b[?25h");
        _term.Write(sb.ToString());
    }

    private static void AppendSgr(StringBuilder sb, Style s)
    {
        sb.Append("\x1b[0");
        if ((s.Attr & Attr.Bold) != 0) sb.Append(";1");
        if ((s.Attr & Attr.Italic) != 0) sb.Append(";3");
        if ((s.Attr & Attr.Underline) != 0) sb.Append(";4");
        if ((s.Attr & Attr.Reverse) != 0) sb.Append(";7");
        if ((s.Attr & Attr.Strike) != 0) sb.Append(";9");
        if (s.Fg >= 0) sb.Append(";38;2;").Append((s.Fg >> 16) & 255).Append(';').Append((s.Fg >> 8) & 255).Append(';').Append(s.Fg & 255);
        if (s.Bg >= 0) sb.Append(";48;2;").Append((s.Bg >> 16) & 255).Append(';').Append((s.Bg >> 8) & 255).Append(';').Append(s.Bg & 255);
        sb.Append('m');
    }
}
