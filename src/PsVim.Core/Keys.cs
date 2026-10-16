using System.Text;

namespace PsVim;

/// <summary>Konverzija tastera u vim notaciju i parsiranje notacije ("ihello&lt;Esc&gt;").</summary>
public static class Keys
{
    public const string Esc = "<Esc>", CR = "<CR>", BS = "<BS>", Tab = "<Tab>", Del = "<Del>";

    public static string FromConsoleKey(ConsoleKeyInfo k)
    {
        bool ctrl = (k.Modifiers & ConsoleModifiers.Control) != 0;
        bool alt = (k.Modifiers & ConsoleModifiers.Alt) != 0;
        bool shift = (k.Modifiers & ConsoleModifiers.Shift) != 0;
        switch (k.Key)
        {
            case ConsoleKey.Escape: return Esc;
            case ConsoleKey.Enter: return CR;
            case ConsoleKey.Backspace: return ctrl ? "<C-BS>" : BS;
            case ConsoleKey.Tab: return shift ? "<S-Tab>" : Tab;
            case ConsoleKey.Delete: return Del;
            case ConsoleKey.UpArrow: return "<Up>";
            case ConsoleKey.DownArrow: return "<Down>";
            case ConsoleKey.LeftArrow: return ctrl ? "<C-Left>" : "<Left>";
            case ConsoleKey.RightArrow: return ctrl ? "<C-Right>" : "<Right>";
            case ConsoleKey.Home: return "<Home>";
            case ConsoleKey.End: return "<End>";
            case ConsoleKey.PageUp: return "<PageUp>";
            case ConsoleKey.PageDown: return "<PageDown>";
            case ConsoleKey.Insert: return "<Insert>";
        }
        if (k.Key >= ConsoleKey.F1 && k.Key <= ConsoleKey.F12) return $"<F{k.Key - ConsoleKey.F1 + 1}>";

        // AltGr (Ctrl+Alt) na srpskom/hrvatskom/nemačkom rasporedu daje [ ] { } \ @ ...
        if (ctrl && alt && k.KeyChar >= ' ') return k.KeyChar.ToString();
        if (ctrl && k.Key == ConsoleKey.Spacebar) return "<C-Space>";
        if (ctrl && !alt)
        {
            if (k.Key >= ConsoleKey.A && k.Key <= ConsoleKey.Z)
            {
                char c = (char)('a' + (k.Key - ConsoleKey.A));
                if (c == 'm') return CR;
                if (c == 'i') return Tab;
                return $"<C-{c}>";
            }
            if (k.KeyChar == '\0' && k.Key == ConsoleKey.Spacebar) return "<C-Space>";
            if (k.KeyChar == (char)0x1f || k.Key == ConsoleKey.Oem2) return "<C-/>";
            if (k.KeyChar == (char)0x1b) return Esc;
        }
        char ch = k.KeyChar;
        if (ch == '\0') return null;
        if (ch == '\r' || ch == '\n') return CR;
        if (ch == (char)0x1b) return Esc;
        if (ch == (char)8 || ch == (char)127) return BS;
        if (ch < ' ')
        {
            // kontrolni karakter bez Key informacije (npr. na Linuxu)
            return $"<C-{(char)('a' + ch - 1)}>";
        }
        if (alt) return $"<A-{ch}>";
        return ch.ToString();
    }

    /// <summary>"dw&lt;Esc&gt;" =&gt; ["d","w","&lt;Esc&gt;"]. "&lt;lt&gt;" je znak &lt;.</summary>
    public static List<string> Parse(string s)
    {
        var res = new List<string>();
        int i = 0;
        while (i < s.Length)
        {
            if (s[i] == '<')
            {
                int j = s.IndexOf('>', i + 1);
                if (j > i + 1)
                {
                    var name = s.Substring(i + 1, j - i - 1);
                    if (IsKeyName(name))
                    {
                        res.Add(Normalize(name));
                        i = j + 1;
                        continue;
                    }
                }
            }
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length) { res.Add(s.Substring(i, 2)); i += 2; continue; }
            res.Add(s[i].ToString());
            i++;
        }
        return res;
    }

    private static readonly HashSet<string> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        "Esc","CR","Enter","Return","BS","Tab","S-Tab","Del","Up","Down","Left","Right","Home","End",
        "PageUp","PageDown","Insert","Space","lt","leader","Leader","C-Space","C-BS","C-Left","C-Right","Bar","Bslash"
    };

    private static bool IsKeyName(string n)
    {
        if (Named.Contains(n)) return true;
        if (n.Length >= 2 && n[0] == 'F' && int.TryParse(n.AsSpan(1), out var f) && f >= 1 && f <= 12) return true;
        if (n.Length == 3 && (n.StartsWith("C-") || n.StartsWith("c-") || n.StartsWith("A-") || n.StartsWith("M-"))) return true;
        return false;
    }

    private static string Normalize(string n)
    {
        switch (n.ToLowerInvariant())
        {
            case "esc": return Esc;
            case "cr": case "enter": case "return": return CR;
            case "bs": return BS;
            case "tab": return Tab;
            case "del": return Del;
            case "space": return " ";
            case "lt": return "<";
            case "bar": return "|";
            case "bslash": return "\\";
            case "leader": return "<leader>";
        }
        if (n.Length == 3 && (n[0] == 'C' || n[0] == 'c') && n[1] == '-') return $"<C-{char.ToLowerInvariant(n[2])}>";
        if (n.Length == 3 && (n[0] == 'A' || n[0] == 'M') && n[1] == '-') return $"<A-{n[2]}>";
        if (n.Equals("S-Tab", StringComparison.OrdinalIgnoreCase)) return "<S-Tab>";
        return "<" + char.ToUpperInvariant(n[0]) + n.Substring(1) + ">";
    }

    public static bool IsPrintable(string k) => k.Length >= 1 && !(k.Length > 1 && k[0] == '<' && k[^1] == '>');

    /// <summary>Lep prikaz tastera za which-key i showcmd.</summary>
    public static string Display(string k) => k switch
    {
        " " => "␣",
        _ => k
    };

    public static string Join(IEnumerable<string> keys)
    {
        var sb = new StringBuilder();
        foreach (var k in keys) sb.Append(k == "<" ? "<lt>" : k);
        return sb.ToString();
    }
}
