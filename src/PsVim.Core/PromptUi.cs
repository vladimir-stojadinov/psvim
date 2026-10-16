namespace PsVim;

/// <summary>
/// UI elementi koje PowerShell prompt (PSReadLine) koristi bez otvaranja editora:
/// which-key meni, fuzzy picker fajlova, istorije i direktorijuma, grep i prikaz teksta.
/// Svaki poziv privremeno prelazi na alternativni ekran i vraća se nazad.
/// </summary>
public static class PromptUi
{
    private static T WithUi<T>(Func<Ui, T> f)
    {
        var opts = Options.Load();
        var term = new ConsoleTerminal();
        var ui = new Ui(term, opts);
        term.Enter();
        try { return f(ui); }
        catch (EndOfInputException) { return default; }
        finally { term.Leave(); }
    }

    private static Action Background(Ui ui, string title, string hint)
    {
        return () =>
        {
            var sc = ui.Screen; var th = ui.Theme;
            sc.Put(2, 1, title, new Style(th.Blue, th.Bg, Attr.Bold));
            if (!string.IsNullOrEmpty(hint))
            {
                var lines = Editor.Wrap(hint, Math.Max(20, sc.W - 6));
                for (int i = 0; i < lines.Count && i < sc.H - 6; i++)
                    sc.Put(4, 3 + i, lines[i], new Style(th.FgDark, th.Bg));
            }
            sc.Fill(0, sc.H - 1, sc.W, 1, new Style(th.Fg, th.BgDark));
            sc.Put(1, sc.H - 1, " PROMPT ", new Style(th.BgDark, th.Teal, Attr.Bold));
            sc.Put(10, sc.H - 1, "Esc = nazad", new Style(th.Comment, th.BgDark));
            sc.Shape = CursorShape.Hidden;
        };
    }

    /// <summary>Which-key meni. Ulaz: "tasteri\topis" (opis koji počinje sa + je grupa). Vraća izabranu putanju tastera ili null.</summary>
    public static string WhichKey(string[] entries, string currentLine = "")
    {
        return WithUi(ui =>
        {
            var root = new KeyNode();
            string result = null;
            foreach (var e in entries ?? Array.Empty<string>())
            {
                var p = e.Split('\t', 2);
                if (p.Length < 2) continue;
                if (p[1].StartsWith("+")) root.Group(p[0], p[1].Substring(1));
                else { var keys = p[0]; root.Map(keys, p[1], () => result = keys); }
            }
            var bg = Background(ui, "psvim · prompt", string.IsNullOrEmpty(currentLine) ? "" : "PS> " + currentLine);
            var act = PsVim.WhichKey.Run(ui, root, "␣ ", bg, 0);
            act?.Invoke();
            return result;
        });
    }

    public static string PickFile(string cwd, string title = "Fajlovi")
    {
        return WithUi(ui =>
        {
            var ix = new FileIndex(cwd, ui.Options);
            try
            {
                var pick = Pickers.FilesPicker(ix, title).Run(ui, Background(ui, "psvim · " + title, cwd));
                return pick?.Path;
            }
            finally { ix.Cancel(); }
        });
    }

    public static string PickDirectory(string cwd)
    {
        return WithUi(ui =>
        {
            var items = new List<PickerItem> { new() { Text = "..", Path = Path.GetDirectoryName(cwd) ?? cwd } };
            items.AddRange(Files.EnumerateDirs(cwd, ui.Options).Select(d => new PickerItem { Text = d, Path = Path.Combine(cwd, d) }));
            var recent = Files.LoadRecent().Select(r => Path.GetDirectoryName(r.Path)).Where(d => d != null && Directory.Exists(d)).Distinct().Take(30);
            foreach (var d in recent) if (!items.Any(i => string.Equals(i.Path, d, StringComparison.OrdinalIgnoreCase))) items.Add(new PickerItem { Text = d, Path = d, Color = Tok.Comment });
            var pick = new Picker { Title = "Direktorijum", Items = items }.Run(ui, Background(ui, "psvim · cd", cwd));
            return pick?.Path;
        });
    }

    public static string PickRecent()
    {
        return WithUi(ui =>
        {
            var items = Files.LoadRecent().Where(r => File.Exists(r.Path)).Select(r => new PickerItem { Text = r.Path, Path = r.Path, Line = r.Line }).ToList();
            var pick = new Picker { Title = "Nedavni fajlovi", Items = items }.Run(ui, Background(ui, "psvim · nedavni", ""));
            return pick?.Path;
        });
    }

    /// <summary>Picker za proizvoljnu listu (npr. istoriju komandi). Vraća izabrani tekst.</summary>
    public static string PickFromList(string[] items, string title, string initialQuery = "")
    {
        return WithUi(ui =>
        {
            var list = (items ?? Array.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => new PickerItem { Text = s.Replace("\r", "").Replace('\n', '⏎'), Tag = s }).ToList();
            var p = new Picker { Title = title, Items = list, Preview = false, InitialQuery = initialQuery ?? "" };
            var pick = p.Run(ui, Background(ui, "psvim · " + title, ""));
            return pick?.Tag as string;
        });
    }

    /// <summary>Live grep. Vraća "putanja\tlinija" (1-bazirano) ili null.</summary>
    public static string PickGrep(string cwd)
    {
        return WithUi(ui =>
        {
            var ix = new FileIndex(cwd, ui.Options);
            try
            {
                var pick = Pickers.GrepPicker(ix, "").Run(ui, Background(ui, "psvim · grep", cwd));
                return pick == null ? null : $"{pick.Path}\t{pick.Line + 1}";
            }
            finally { ix.Cancel(); }
        });
    }

    /// <summary>Prikazuje tekst (npr. Get-Help) u prozoru sa skrolovanjem.</summary>
    public static void ShowText(string title, string text)
    {
        WithUi<object>(ui =>
        {
            var lines = (text ?? "").Replace("\r", "").Split('\n');
            int scroll = 0;
            void Draw()
            {
                ui.BeginFrame();
                var sc = ui.Screen; var th = ui.Theme;
                int h = sc.H - 2;
                scroll = Compat.Clamp(scroll, 0, Math.Max(0, lines.Length - h));
                sc.Put(1, 0, " " + title + " ", new Style(th.Bg, th.Orange, Attr.Bold));
                for (int r = 0; r < h && scroll + r < lines.Length; r++)
                {
                    var l = lines[scroll + r];
                    var st = l.Length > 0 && l == l.ToUpperInvariant() && l.Any(char.IsLetter) && !l.StartsWith(" ") ? new Style(th.Blue, th.Bg, Attr.Bold) : new Style(th.Fg, th.Bg);
                    sc.Put(1, 1 + r, l, st);
                }
                sc.Fill(0, sc.H - 1, sc.W, 1, new Style(th.Fg, th.BgDark));
                sc.Put(1, sc.H - 1, $" j/k skrol · q izlaz · {scroll + 1}/{lines.Length} ", new Style(th.Comment, th.BgDark));
                sc.Shape = CursorShape.Hidden;
                sc.Flush();
            }
            using (ui.PushRenderer(Draw))
            {
                while (true)
                {
                    var k = ui.Input.Next();
                    int page = Math.Max(1, ui.Screen.H - 3);
                    switch (k)
                    {
                        case "j": case "<Down>": case "<C-e>": scroll++; break;
                        case "k": case "<Up>": case "<C-y>": scroll--; break;
                        case " ": case "<C-d>": case "<C-f>": case "<PageDown>": scroll += page / 2; break;
                        case "<C-u>": case "<C-b>": case "<PageUp>": scroll -= page / 2; break;
                        case "g": scroll = 0; break;
                        case "G": scroll = int.MaxValue / 2; break;
                        default: return null;
                    }
                }
            }
        });
    }
}

/// <summary>Gotovi picker-i za fajlove i grep nad indeksom koji se puni u pozadini.</summary>
public static class Pickers
{
    public static Picker FilesPicker(FileIndex ix, string title)
    {
        var p = new Picker { Title = title };
        int taken = 0;
        p.Poll = () =>
        {
            var add = ix.Snapshot(taken);
            if (add.Count == 0) return false;
            taken += add.Count;
            foreach (var rel in add) p.Items.Add(new PickerItem { Text = rel, Path = Path.Combine(ix.Root, rel) });
            return true;
        };
        p.Loading = () => !ix.Done || taken < ix.Count;
        p.Status = () => $"indeksiram… {ix.Count}";
        return p;
    }

    public static Picker GrepPicker(FileIndex ix, string initial)
    {
        var p = new Picker { Title = "Grep", InitialQuery = initial ?? "" };
        p.LiveAsync = (q, ct, add) => Files.GrepIndex(ix, q, ct, add);
        p.Status = () => $"{ix.Scanned}/{ix.Count}{(ix.Done ? "" : "+")} fajlova";
        return p;
    }
}
