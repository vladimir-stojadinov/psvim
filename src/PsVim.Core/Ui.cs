namespace PsVim;

/// <summary>Zajednički UI kontekst: terminal, ekran, tema i ulaz.</summary>
public sealed class Ui
{
    public readonly ITerminal Term;
    public readonly Screen Screen;
    public Theme Theme;
    public readonly KeyInput Input;
    public Options Options;

    public Ui(ITerminal term, Options opts)
    {
        Term = term;
        Options = opts ?? new Options();
        Screen = new Screen(term);
        Theme = Theme.Get(Options.Theme);
        Input = new KeyInput(term);
    }

    public void BeginFrame()
    {
        Screen.Resize(Term.Width, Term.Height);
        Screen.Clear(Theme.Normal);
    }

    /// <summary>Privremeno menja funkciju koja crta ekran pre blokirajućeg čitanja tastera.</summary>
    public IDisposable PushRenderer(Action render)
    {
        var old = Input.BeforeBlock;
        Input.BeforeBlock = render;
        return new Restore(() => Input.BeforeBlock = old);
    }

    private sealed class Restore : IDisposable
    {
        private readonly Action _a; public Restore(Action a) { _a = a; }
        public void Dispose() => _a();
    }
}

public sealed class KeyInput
{
    private readonly ITerminal _term;
    private readonly LinkedList<string> _pending = new();
    private List<string> _rec;
    public Action BeforeBlock;
    public List<string> MacroRec;

    public KeyInput(ITerminal t) { _term = t; }

    public bool HasPending => _pending.Count > 0;

    public string Next()
    {
        string k;
        if (_pending.Count > 0) { k = _pending.First.Value; _pending.RemoveFirst(); }
        else { BeforeBlock?.Invoke(); k = _term.ReadKey(); }
        _rec?.Add(k);
        MacroRec?.Add(k);
        return k;
    }

    /// <summary>Ubacuje tastere na početak reda (koriste ih mapiranja i '.').</summary>
    public void Push(IEnumerable<string> keys)
    {
        var node = _pending.First;
        foreach (var k in keys)
        {
            if (node == null) _pending.AddLast(k); else _pending.AddBefore(node, k);
        }
    }

    public bool KeyWaiting(int ms) => _pending.Count > 0 || _term.WaitForKey(ms);

    public void StartRecording() => _rec = new List<string>();
    public List<string> StopRecording() { var r = _rec; _rec = null; return r; }
    public bool Recording => _rec != null;
    public void RemoveLastRecorded() { if (_rec != null && _rec.Count > 0) _rec.RemoveAt(_rec.Count - 1); }
}

// =====================================================================
//  Fuzzy pretraga (u stilu fzf-a)
// =====================================================================
public static class Fuzzy
{
    /// <summary>Pripremljen upit (termini razdvojeni razmakom, !negacija, 'tačno).</summary>
    public sealed class Query
    {
        public string[] Terms; public bool[] Neg, Exact, CaseSens;
        public bool Empty => Terms.Length == 0;
    }

    public static Query Compile(string query)
    {
        var parts = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var terms = new List<string>(); var neg = new List<bool>(); var exact = new List<bool>(); var cs = new List<bool>();
        foreach (var p in parts)
        {
            bool n = p.StartsWith('!'); var t = n ? p.Substring(1) : p;
            bool e = t.StartsWith('\''); if (e) t = t.Substring(1);
            if (t.Length == 0) continue;
            terms.Add(t); neg.Add(n); exact.Add(e);
            bool upper = false; foreach (var ch in t) if (char.IsUpper(ch)) { upper = true; break; }
            cs.Add(upper);
        }
        return new Query { Terms = terms.ToArray(), Neg = neg.ToArray(), Exact = exact.ToArray(), CaseSens = cs.ToArray() };
    }

    public static int Score(string text, string query, List<int> positions = null) => Score(text, Compile(query), positions);

    public static int Score(string text, Query q, List<int> positions)
    {
        int total = 0;
        for (int i = 0; i < q.Terms.Length; i++)
        {
            var t = q.Terms[i];
            if (q.Neg[i])
            {
                if (text.Contains(t, StringComparison.OrdinalIgnoreCase)) return int.MinValue;
                continue;
            }
            int s;
            if (q.Exact[i])
            {
                int idx = text.IndexOf(t, q.CaseSens[i] ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
                if (idx < 0) return int.MinValue;
                if (positions != null) for (int k = 0; k < t.Length; k++) positions.Add(idx + k);
                s = 100 + t.Length * 20;
            }
            else s = ScoreTerm(text, t, q.CaseSens[i], positions);
            if (s == int.MinValue) return int.MinValue;
            total += s;
        }
        return total;
    }

    private static int ScoreTerm(string text, string pat, bool cs, List<int> positions)
    {
        int n = text.Length, m = pat.Length;
        if (m > n) return int.MinValue;
        // 1) unapred: nađi kraj prvog podudaranja
        int pi = 0, end = -1;
        for (int i = 0; i < n; i++)
        {
            if (Eq(text[i], pat[pi], cs)) { pi++; if (pi == m) { end = i; break; } }
        }
        if (end < 0) return int.MinValue;
        // 2) unazad: najkraće podudaranje koje se završava na end
        pi = m - 1; int start = end;
        for (int i = end; i >= 0; i--)
        {
            if (Eq(text[i], pat[pi], cs)) { pi--; if (pi < 0) { start = i; break; } }
        }
        // 3) bodovanje unapred od start
        int score = 0, lastMatch = -2; pi = 0;
        int nameStart = Math.Max(text.LastIndexOf('/'), text.LastIndexOf('\\')) + 1;
        for (int i = start; i <= end && pi < m; i++)
        {
            if (!Eq(text[i], pat[pi], cs)) { if (lastMatch >= 0) score -= 1; continue; }
            int s = 16;
            if (lastMatch == i - 1) s += 18;
            char prev = i > 0 ? text[i - 1] : '/';
            if (prev == '/' || prev == '\\') s += 14;
            else if (prev == '_' || prev == '-' || prev == '.' || prev == ' ' || prev == ':') s += 10;
            else if (char.IsLower(prev) && char.IsUpper(text[i])) s += 9;
            if (i == 0) s += 8;
            if (i >= nameStart) s += 6;
            if (text[i] == pat[pi]) s += 1;
            score += s;
            positions?.Add(i);
            lastMatch = i; pi++;
        }
        score -= Math.Min(30, n / 8);
        return score;
    }

    private static bool Eq(char a, char b, bool cs)
    {
        if (a == b) return true;
        if (cs) return false;
        if (b == '/' || b == '\\') return a == '/' || a == '\\';
        if (a < 128 && b < 128) return (a | 0x20) == (b | 0x20) && char.IsLetter(a);
        return char.ToLowerInvariant(a) == char.ToLowerInvariant(b);
    }
}

// =====================================================================
//  Picker (Telescope / snacks.picker)
// =====================================================================
public sealed class PickerItem
{
    public string Text;       // tekst po kome se traži i koji se prikazuje
    public string Path;       // fajl za pregled/otvaranje
    public int Line = -1;     // 0-bazirano
    public int Col;
    public string Detail;     // sivi dodatak desno
    public object Tag;
    public Tok Color = Tok.Default;
    public string Icon;
    internal int Score;
    internal int Order;
    internal List<int> Hits;
    internal string HitsFor;
    public override string ToString() => Text;
}

public sealed class Picker
{
    public string Title = "Pretraga";
    public List<PickerItem> Items = new();
    /// <summary>Sinhrono računanje stavki za svaki upit (za male skupove).</summary>
    public Func<string, List<PickerItem>> Live;
    /// <summary>Pretraga u pozadini (live grep): (upit, otkazivanje, prijavi stavku). Poziva se sa druge niti.</summary>
    public Action<string, CancellationToken, Action<PickerItem>> LiveAsync;
    public int LiveMinChars = 2;
    public int DebounceMs = 150;
    /// <summary>Periodično dopunjava Items (npr. iz indeksa koji se puni). Vraća true ako su dodate stavke.</summary>
    public Func<bool> Poll;
    /// <summary>True dok se stavke još učitavaju.</summary>
    public Func<bool> Loading;
    /// <summary>Tekst statusa (npr. broj pregledanih fajlova).</summary>
    public Func<string> Status;
    public bool Preview = true;
    public string InitialQuery = "";
    public bool MatchHighlight = true;
    /// <summary>Dodatne akcije: taster -&gt; (stavka, upit) =&gt; true ako treba zatvoriti picker.</summary>
    public Dictionary<string, Func<PickerItem, string, bool>> Actions = new();
    public string LastQuery { get; private set; }
    public string LastKey { get; private set; }

    private readonly Dictionary<string, string[]> _fileCache = new();
    private static readonly string[] Spinner = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };

    public PickerItem Run(Ui ui, Action drawBackground)
    {
        var q = new System.Text.StringBuilder(InitialQuery ?? "");
        int qc = q.Length;
        int sel = 0, scroll = 0;
        List<PickerItem> results = null;
        bool dirty = true;
        string lastLiveQuery = null;
        string statusText = null;
        int spin = 0;

        // ---- pozadinska pretraga ----
        CancellationTokenSource liveCts = null;
        Task liveTask = null;
        List<PickerItem> liveList = new();
        string liveQuery = null;
        int liveShown = -1;
        long lastEdit = 0;
        bool LiveBusy() => liveTask != null && !liveTask.IsCompleted;

        void StartLive(string query)
        {
            liveCts?.Cancel();
            liveCts = new CancellationTokenSource();
            var list = new List<PickerItem>();
            liveList = list; liveQuery = query; liveShown = -1;
            liveTask = null;
            if (query.Length < LiveMinChars) return;
            var tok = liveCts.Token;
            var fn = LiveAsync;
            liveTask = Task.Run(() =>
            {
                try { fn(query, tok, it => { lock (list) list.Add(it); }); }
                catch { }
            });
        }

        void Compute()
        {
            var query = q.ToString();
            if (LiveAsync != null)
            {
                List<PickerItem> copy;
                lock (liveList) copy = new List<PickerItem>(liveList);
                liveShown = copy.Count;
                if (MatchHighlight && liveQuery != null)
                    foreach (var it in copy) if (it.Hits == null) { it.Hits = new List<int>(); MarkLiteral(it, liveQuery); }
                results = copy;
            }
            else if (Live != null)
            {
                if (query != lastLiveQuery)
                {
                    lastLiveQuery = query;
                    results = Live(query) ?? new List<PickerItem>();
                    if (MatchHighlight)
                        foreach (var it in results) { it.Hits = new List<int>(); MarkLiteral(it, query); }
                }
            }
            else results = FilterItems(query);
            if (sel >= results.Count) sel = Math.Max(0, results.Count - 1);
            dirty = false;
        }

        bool Busy() => LiveBusy() || (LiveAsync != null && q.ToString() != liveQuery) || (Loading?.Invoke() ?? false);

        void Draw()
        {
            if (results == null || (dirty && LiveAsync == null && Live == null && Poll == null)) Compute();
            else if (dirty && Live != null) Compute();
            statusText = null;
            if (Busy())
            {
                spin = (spin + 1) % Spinner.Length;
                statusText = Spinner[spin] + " " + (Status?.Invoke() ?? "tražim…");
            }
            ui.BeginFrame();
            drawBackground?.Invoke();
            DrawPicker(ui, q.ToString(), qc, results, sel, ref scroll, statusText);
            ui.Screen.Flush();
        }

        /// Obrađuje pozadinski rad dok korisnik ne pritisne taster.
        void Background()
        {
            long lastPoll = 0, lastDraw = 0;
            while (!ui.Input.KeyWaiting(0))
            {
                bool changed = false;
                long now = Environment.TickCount64;
                if (LiveAsync != null)
                {
                    var cur = q.ToString();
                    if (cur != liveQuery && now - lastEdit >= DebounceMs) { StartLive(cur); changed = true; sel = 0; scroll = 0; }
                    int cnt; lock (liveList) cnt = liveList.Count;
                    if (cnt != liveShown) changed = true;
                }
                if (Poll != null && (now - lastPoll >= (q.Length > 0 ? 500 : 250) || results == null))
                {
                    lastPoll = now;
                    if (Poll()) { dirty = true; changed = true; }
                }
                if (dirty && Live == null) changed = true;
                bool busy = Busy();
                if (changed) { Compute(); Draw(); lastDraw = now; }
                else if (busy && now - lastDraw >= 100) { Draw(); lastDraw = now; }
                if (!busy && !dirty) break;
                ui.Term.WaitForKey(busy ? 30 : 0);
            }
        }

        /// Pre izbora sa Enter: sačekaj rezultate tekuće pretrage (najviše par sekundi).
        void Settle()
        {
            if (LiveAsync != null)
            {
                if (q.ToString() != liveQuery) StartLive(q.ToString());
                var until = Environment.TickCount64 + 5000;
                while (LiveBusy() && Environment.TickCount64 < until)
                {
                    int cnt; lock (liveList) cnt = liveList.Count;
                    if (cnt > sel) break;
                    Thread.Sleep(15);
                }
            }
            if (Poll != null && Loading != null)
            {
                var until = Environment.TickCount64 + 5000;
                do { Poll(); } while (Loading() && Items.Count == 0 && Environment.TickCount64 < until && Sleep());
            }
            Compute();
        }
        static bool Sleep() { Thread.Sleep(15); return true; }

        try
        {
            using (ui.PushRenderer(Draw))
            {
                while (true)
                {
                    Background();
                    var k = ui.Input.Next();
                    LastKey = k;
                    if (Actions.TryGetValue(k, out var act))
                    {
                        Settle();
                        var item = results.Count > 0 ? results[sel] : null;
                        LastQuery = q.ToString();
                        if (act(item, LastQuery)) return item;
                        dirty = true; lastLiveQuery = null; liveQuery = null;
                        continue;
                    }
                    int before = q.Length; var beforeText = q.ToString();
                    switch (k)
                    {
                        case "<Esc>": case "<C-c>": LastQuery = q.ToString(); return null;
                        case "<CR>":
                            Settle();
                            LastQuery = q.ToString();
                            return results.Count > 0 ? results[Math.Min(sel, results.Count - 1)] : null;
                        case "<Down>": case "<C-n>": case "<C-j>": case "<Tab>": if (results == null) Compute(); if (results.Count > 0) sel = (sel + 1) % results.Count; break;
                        case "<Up>": case "<C-p>": case "<C-k>": case "<S-Tab>": if (results == null) Compute(); if (results.Count > 0) sel = (sel - 1 + results.Count) % results.Count; break;
                        case "<PageDown>": case "<C-d>": sel = Math.Min(Math.Max(0, (results?.Count ?? 1) - 1), sel + 10); break;
                        case "<PageUp>": case "<C-u>": sel = Math.Max(0, sel - 10); break;
                        case "<BS>": if (qc > 0) { q.Remove(qc - 1, 1); qc--; } break;
                        case "<Del>": if (qc < q.Length) q.Remove(qc, 1); break;
                        case "<C-w>":
                            {
                                int s = qc; while (s > 0 && q[s - 1] == ' ') s--; while (s > 0 && q[s - 1] != ' ') s--;
                                q.Remove(s, qc - s); qc = s; break;
                            }
                        case "<Left>": if (qc > 0) qc--; break;
                        case "<Right>": if (qc < q.Length) qc++; break;
                        case "<Home>": case "<C-a>": qc = 0; break;
                        case "<End>": case "<C-e>": qc = q.Length; break;
                        default:
                            if (Keys.IsPrintable(k)) { q.Insert(qc, k); qc += k.Length; }
                            break;
                    }
                    if (q.ToString() != beforeText)
                    {
                        dirty = true; sel = 0; scroll = 0;
                        lastEdit = Environment.TickCount64;
                    }
                }
            }
        }
        finally { liveCts?.Cancel(); }
    }

    private static void MarkLiteral(PickerItem it, string query)
    {
        if (string.IsNullOrEmpty(query)) return;
        int idx = it.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        int start = it.Text.IndexOf(':'); // ime:linija: tekst — traži posle prefiksa
        if (start >= 0)
        {
            int second = it.Text.IndexOf(':', start + 1);
            if (second > 0) { int i2 = it.Text.IndexOf(query, second, StringComparison.OrdinalIgnoreCase); if (i2 >= 0) idx = i2; }
        }
        if (idx >= 0) for (int k = 0; k < query.Length; k++) it.Hits.Add(idx + k);
    }

    public static List<PickerItem> Filter(List<PickerItem> items, string query, bool hits = true)
    {
        for (int i = 0; i < items.Count; i++) items[i].Order = i;
        return FilterSubset(items, query);
    }

    /// <summary>Filtrira bez računanja pozicija (one se računaju samo za vidljive redove).</summary>
    private static List<PickerItem> FilterSubset(List<PickerItem> items, string query)
    {
        var q = Fuzzy.Compile(query);
        if (q.Empty) return items.ToList();
        var res = new List<PickerItem>();
        if (items.Count > 20000)
        {
            // velike liste (ceo disk): bodovanje paralelno
            var scores = new int[items.Count];
            Parallel.For(0, items.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
                scores[i] = Fuzzy.Score(items[i].Text, q, null));
            for (int i = 0; i < items.Count; i++)
                if (scores[i] != int.MinValue) { items[i].Score = scores[i]; res.Add(items[i]); }
        }
        else
            foreach (var it in items)
            {
                int s = Fuzzy.Score(it.Text, q, null);
                if (s == int.MinValue) continue;
                it.Score = s;
                res.Add(it);
            }
        res.Sort((a, b) => a.Score != b.Score ? b.Score.CompareTo(a.Score) : a.Order.CompareTo(b.Order));
        return res;
    }

    private string _fq; private List<PickerItem> _fres; private int _fcount = -1;

    /// <summary>Inkrementalno: ako se upit samo produžio, filtriraj prethodne rezultate.</summary>
    private List<PickerItem> FilterItems(string query)
    {
        if (_fcount != Items.Count) { for (int i = Math.Max(0, _fcount); i < Items.Count; i++) Items[i].Order = i; }
        // isti upit, samo su stigle nove stavke (indeks se puni): filtriraj samo nove
        if (_fres != null && query == _fq && _fcount >= 0 && Items.Count > _fcount)
        {
            var added = FilterSubset(Items.GetRange(_fcount, Items.Count - _fcount), query);
            var merged = new List<PickerItem>(_fres.Count + added.Count);
            merged.AddRange(_fres); merged.AddRange(added);
            if (!string.IsNullOrWhiteSpace(query))
                merged.Sort((a, b) => a.Score != b.Score ? b.Score.CompareTo(a.Score) : a.Order.CompareTo(b.Order));
            _fres = merged; _fcount = Items.Count;
            return merged;
        }
        List<PickerItem> source = Items;
        if (_fres != null && _fcount == Items.Count && !string.IsNullOrWhiteSpace(_fq) && query.StartsWith(_fq, StringComparison.Ordinal) && !query.Contains('!'))
            source = _fres;
        var res = FilterSubset(source, query);
        _fq = query; _fres = res; _fcount = Items.Count;
        return res;
    }

    private void DrawPicker(Ui ui, string query, int qc, List<PickerItem> results, int sel, ref int scroll, string status)
    {
        var sc = ui.Screen; var th = ui.Theme;
        int W = sc.W, H = sc.H;
        int bw = Math.Min(W - 2, Math.Max(50, W * 9 / 10));
        int bh = Math.Min(H - 2, Math.Max(10, H * 8 / 10));
        int bx = (W - bw) / 2, by = (H - bh) / 2;
        bool preview = Preview && bw >= 90 && results.Any(r => r.Path != null);
        int lw = preview ? bw * 45 / 100 : bw;
        var bg = th.PopupBg;
        var border = new Style(th.Border, bg);
        var title = new Style(th.Bg, th.Orange, Attr.Bold);
        var normal = new Style(th.Fg, bg);

        // lista + upit
        sc.Box(bx, by, lw, bh, border, normal, Title, title);
        // red za upit
        int py = by + 1;
        sc.Put(bx + 2, py, "❯ ", new Style(th.Orange, bg, Attr.Bold));
        var cnt = Live != null || LiveAsync != null ? results.Count.ToString() : $"{results.Count}/{Items.Count}";
        if (status != null) cnt = status + "  " + cnt;
        if (cnt.Length > lw / 2) cnt = cnt.Substring(cnt.Length - lw / 2);
        int qEnd = bx + lw - 3 - cnt.Length;
        int maxQ = Math.Max(4, qEnd - (bx + 4) - 1);
        int qOff = Math.Max(0, qc - maxQ);
        sc.Put(bx + 4, py, query.Substring(Math.Min(qOff, query.Length)), normal, qEnd);
        sc.Put(bx + lw - 2 - cnt.Length, py, cnt, new Style(status != null ? th.Yellow : th.Comment, bg));
        for (int x = bx + 1; x < bx + lw - 1; x++) sc.Set(x, py + 1, "─", new Style(th.FgGutter, bg));
        sc.CursorX = bx + 4 + qc - qOff; sc.CursorY = py;
        sc.Shape = CursorShape.Bar;

        int listTop = py + 2, listH = by + bh - 1 - listTop;
        if (sel < scroll) scroll = sel;
        if (sel >= scroll + listH) scroll = sel - listH + 1;
        for (int r = 0; r < listH; r++)
        {
            int idx = scroll + r;
            if (idx >= results.Count) break;
            var it = results[idx];
            int y = listTop + r;
            bool isSel = idx == sel;
            var rowBg = isSel ? th.PopupSel : bg;
            sc.Fill(bx + 1, y, lw - 2, 1, new Style(th.Fg, rowBg));
            sc.Put(bx + 1, y, isSel ? "▌" : " ", new Style(th.Orange, rowBg));
            int x = bx + 3;
            if (it.Icon != null) x = sc.Put(x, y, it.Icon + " ", new Style(th.Blue, rowBg), bx + lw - 1);
            var baseStyle = it.Color == Tok.Default ? new Style(isSel ? th.Fg : th.FgDark, rowBg) : th.Tok(it.Color).WithBg(rowBg);
            int maxX = bx + lw - 2;
            string text = it.Text;
            // za putanje: skrati sa leve strane ako ne staje
            int avail = maxX - x - (it.Detail != null ? it.Detail.Length + 1 : 0);
            int cut = 0;
            if (text.Length > avail && avail > 4) { cut = text.Length - avail + 1; }
            if (cut > 0) x = sc.Put(x, y, "…", new Style(th.Comment, rowBg), maxX);
            if (MatchHighlight && Live == null && LiveAsync == null)
            {
                if (string.IsNullOrWhiteSpace(query)) it.Hits = null;
                else if (it.HitsFor != query) { it.Hits = new List<int>(); Fuzzy.Score(it.Text, query, it.Hits); it.HitsFor = query; }
            }
            var hitSet = it.Hits != null ? new HashSet<int>(it.Hits) : null;
            for (int i = cut; i < text.Length && x < maxX; i++)
            {
                var st = baseStyle;
                if (hitSet != null && hitSet.Contains(i)) st = new Style(th.Orange, rowBg, Attr.Bold);
                sc.Set(x++, y, text[i] < ' ' ? " " : text[i].ToString(), st);
            }
            if (it.Detail != null && x < maxX - 1)
                sc.Put(Math.Max(x + 1, maxX - it.Detail.Length), y, it.Detail, new Style(th.Comment, rowBg), maxX);
        }
        if (results.Count == 0)
            sc.Put(bx + 3, listTop, (Live != null || LiveAsync != null) && query.Length < LiveMinChars ? "Kucaj za pretragu (bar " + LiveMinChars + " znaka)…" : status != null ? "Tražim…" : "Nema rezultata", new Style(th.Comment, bg));

        if (preview)
        {
            int px = bx + lw, pw = bw - lw;
            var it = results.Count > 0 ? results[Math.Min(sel, results.Count - 1)] : null;
            string ptitle = it?.Path != null ? System.IO.Path.GetFileName(it.Path) : "Pregled";
            sc.Box(px, by, pw, bh, border, new Style(th.Fg, th.Bg), ptitle, new Style(th.Bg, th.Blue, Attr.Bold));
            if (it?.Path != null) DrawPreview(ui, it, px + 1, by + 1, pw - 2, bh - 2);
        }
    }

    private void DrawPreview(Ui ui, PickerItem it, int x, int y, int w, int h)
    {
        var sc = ui.Screen; var th = ui.Theme;
        if (Directory.Exists(it.Path))
        {
            try
            {
                var entries = Directory.EnumerateFileSystemEntries(it.Path).Take(h).ToList();
                for (int i = 0; i < entries.Count; i++)
                {
                    bool d = Directory.Exists(entries[i]);
                    sc.Put(x + 1, y + i, (d ? "▸ " : "  ") + System.IO.Path.GetFileName(entries[i]) + (d ? "/" : ""), new Style(d ? th.Blue : th.Fg, th.Bg), x + w);
                }
            }
            catch { }
            return;
        }
        if (!_fileCache.TryGetValue(it.Path, out var lines))
        {
            try
            {
                var fi = new FileInfo(it.Path);
                if (!fi.Exists) lines = new[] { "(fajl ne postoji)" };
                else if (fi.Length > 2_000_000) lines = new[] { "(fajl je prevelik za pregled)" };
                else
                {
                    var text = File.ReadAllText(it.Path);
                    lines = text.Contains('\0') ? new[] { "(binarni fajl)" } : text.Replace("\r\n", "\n").Split('\n');
                }
            }
            catch (Exception e) { lines = new[] { "(greška: " + e.Message + ")" }; }
            _fileCache[it.Path] = lines;
        }
        int target = Math.Max(0, it.Line);
        int top = Math.Max(0, target - h / 3);
        if (it.Line < 0) top = 0;
        var buf = new Buffer { Lines = new List<string>(lines), FileType = Syntax.DetectFileType(it.Path) };
        var hl = new HighlightCache();
        int numW = Math.Max(3, (top + h).ToString().Length);
        for (int r = 0; r < h; r++)
        {
            int l = top + r;
            if (l >= lines.Length) break;
            bool cur = l == it.Line;
            int rowBg = cur ? th.BgVisual : th.Bg;
            sc.Fill(x, y + r, w, 1, new Style(th.Fg, rowBg));
            sc.Put(x, y + r, (l + 1).ToString().PadLeft(numW) + " ", new Style(cur ? th.CursorLineNr : th.LineNr, rowBg), x + w);
            var line = lines[l].Replace('\t', ' ');
            var spans = Syntax.For(buf.FileType) != null ? hl.Get(buf, l) : null;
            var styles = new Tok[line.Length];
            if (spans != null) foreach (var s in spans) for (int k = s.Start; k < s.Start + s.Len && k < styles.Length; k++) styles[k] = s.Kind;
            int cx = x + numW + 1;
            for (int k = 0; k < line.Length && cx < x + w; k++, cx++)
                sc.Set(cx, y + r, line[k].ToString(), th.Tok(styles[k]).WithBg(rowBg));
        }
    }
}

// =====================================================================
//  Which-key
// =====================================================================
public sealed class KeyNode
{
    public string Desc;
    public Action Action;
    public readonly List<(string Key, KeyNode Node)> Children = new();
    public bool IsGroup => Children.Count > 0 && Action == null;

    public KeyNode Get(string key) => Children.FirstOrDefault(c => c.Key == key).Node;

    public KeyNode Ensure(string key, string desc = null)
    {
        var n = Get(key);
        if (n == null) { n = new KeyNode { Desc = desc }; Children.Add((key, n)); }
        else if (desc != null && n.Desc == null) n.Desc = desc;
        return n;
    }

    /// <summary>Dodaje mapiranje po putanji tastera ("ff").</summary>
    public void Map(string keys, string desc, Action action)
    {
        var parts = Keys.Parse(keys);
        var node = this;
        for (int i = 0; i < parts.Count - 1; i++) node = node.Ensure(parts[i]);
        var leaf = node.Ensure(parts[^1]);
        leaf.Desc = desc; leaf.Action = action;
    }

    public void Group(string keys, string desc)
    {
        var parts = Keys.Parse(keys);
        var node = this;
        foreach (var p in parts) node = node.Ensure(p);
        node.Desc = desc;
    }

    public IEnumerable<(string Path, KeyNode Node)> Walk(string prefix = "")
    {
        foreach (var (k, n) in Children)
        {
            var p = prefix + (k == " " ? "<Space>" : k);
            if (n.Action != null) yield return (p, n);
            foreach (var x in n.Walk(p)) yield return x;
        }
    }
}

public static class WhichKey
{
    /// <summary>Vodi korisnika kroz stablo tastera. Vraća izabranu akciju ili null.</summary>
    public static Action Run(Ui ui, KeyNode root, string prefixLabel, Action drawBackground, int delayMs = 250)
    {
        var node = root;
        var path = new List<string>();
        var stack = new Stack<KeyNode>();
        bool showPopup = !ui.Input.KeyWaiting(delayMs);
        while (true)
        {
            void Draw()
            {
                ui.BeginFrame();
                drawBackground?.Invoke();
                if (showPopup) DrawPopup(ui, node, prefixLabel + string.Concat(path.Select(Keys.Display)));
                ui.Screen.Flush();
            }
            string k;
            using (ui.PushRenderer(Draw)) k = ui.Input.Next();
            if (k == "<Esc>" || k == "<C-c>") return null;
            if (k == "<BS>")
            {
                if (stack.Count == 0) return null;
                node = stack.Pop(); path.RemoveAt(path.Count - 1); showPopup = true; continue;
            }
            var next = node.Get(k);
            if (next == null) return null;
            if (next.Action != null && next.Children.Count == 0) return next.Action;
            stack.Push(node); node = next; path.Add(k);
            if (!showPopup) showPopup = !ui.Input.KeyWaiting(delayMs);
        }
    }

    private static void DrawPopup(Ui ui, KeyNode node, string label)
    {
        var sc = ui.Screen; var th = ui.Theme;
        var items = node.Children.Where(c => c.Node.Desc != null || c.Node.Action != null).ToList();
        if (items.Count == 0) return;
        int colW = Math.Min(38, Math.Max(20, items.Max(i => Keys.Display(i.Key).Length + (i.Node.Desc?.Length ?? 0) + 6)));
        int W = sc.W, H = sc.H;
        int cols = Math.Max(1, (W - 4) / colW);
        int rows = (items.Count + cols - 1) / cols;
        cols = (items.Count + rows - 1) / rows;
        int bw = Math.Min(W, cols * colW + 4);
        int bh = rows + 2;
        int bx = W - bw, by = Math.Max(0, H - 2 - bh);
        var bg = th.PopupBg;
        sc.Box(bx, by, bw, bh, new Style(th.Border, bg), new Style(th.Fg, bg), label, new Style(th.Orange, bg, Attr.Bold));
        for (int i = 0; i < items.Count; i++)
        {
            int c = i / rows, r = i % rows;
            int x = bx + 2 + c * colW, y = by + 1 + r;
            var (key, n) = items[i];
            x = sc.Put(x, y, Keys.Display(key), new Style(th.Orange, bg, Attr.Bold));
            x = sc.Put(x + 1, y, "➜ ", new Style(th.Comment, bg));
            bool group = n.Children.Count > 0 && n.Action == null;
            var d = group ? "+" + (n.Desc ?? "grupa") : (n.Desc ?? "");
            sc.Put(x, y, d, new Style(group ? th.Magenta : th.Blue1, bg), bx + 2 + (c + 1) * colW - 1);
        }
    }
}
