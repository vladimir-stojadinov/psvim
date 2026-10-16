using System.Text.RegularExpressions;

namespace PsVim;

/// <summary>Pomoćne funkcije za fajlove: nabrajanje, grep, nedavni fajlovi, git grana.</summary>
public static class Files
{
    public static List<string> Enumerate(string root, Options o, int max = 50000)
    {
        var res = new List<string>();
        Walk(root, o, CancellationToken.None, f => { res.Add(f); return res.Count < max; });
        return res;
    }

    /// <summary>
    /// Obilazi stablo i za svaki fajl poziva onFile(relativna putanja). Preskače ignorisane,
    /// skrivene/sistemske foldere i "junction"/symlink foldere (sprečava petlje na C:\).
    /// onFile vraća false za prekid.
    /// </summary>
    public static void Walk(string root, Options o, CancellationToken ct, Func<string, bool> onFile)
    {
        if (!Directory.Exists(root)) return;
        var ignore = new HashSet<string>(o.Ignore ?? new List<string>(), StringComparer.OrdinalIgnoreCase)
        {
            "$Recycle.Bin", "System Volume Information", "$WinREAgent", "Config.Msi", "Recovery", "WinSxS"
        };
        var globs = LoadGitIgnore(root);
        var eo = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = o.ShowHidden ? FileAttributes.System : (FileAttributes.System | FileAttributes.Hidden),
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false
        };
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            if (ct.IsCancellationRequested) return;
            var dir = stack.Pop();
            var subdirs = new List<string>();
            var files = new List<string>();
            try
            {
                var di = new DirectoryInfo(dir);
                foreach (var fsi in di.EnumerateFileSystemInfos("*", eo))
                {
                    var name = fsi.Name;
                    if (ignore.Contains(name)) continue;
                    if (!o.ShowHidden && name.StartsWith('.')) continue;
                    if (globs.Count > 0 && globs.Any(g => g.IsMatch(name))) continue;
                    if ((fsi.Attributes & FileAttributes.Directory) != 0)
                    {
                        if ((fsi.Attributes & FileAttributes.ReparsePoint) != 0) continue; // junction/symlink
                        subdirs.Add(fsi.FullName);
                    }
                    else files.Add(fsi.FullName);
                }
            }
            catch { continue; }
            files.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
                if (!onFile(Path.GetRelativePath(root, f))) return;
            subdirs.Sort(StringComparer.OrdinalIgnoreCase);
            for (int i = subdirs.Count - 1; i >= 0; i--) stack.Push(subdirs[i]);
        }
    }

    public static List<string> EnumerateDirs(string root, Options o, int max = 20000)
    {
        var res = new List<string>();
        var ignore = new HashSet<string>(o.Ignore ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        var q = new Queue<(string, int)>();
        q.Enqueue((root, 0));
        while (q.Count > 0 && res.Count < max)
        {
            var (d, depth) = q.Dequeue();
            if (depth > 8) continue;
            IEnumerable<string> subs;
            try { subs = Directory.EnumerateDirectories(d); } catch { continue; }
            foreach (var s in subs)
            {
                var name = Path.GetFileName(s);
                if (ignore.Contains(name) || (!o.ShowHidden && name.StartsWith('.'))) continue;
                res.Add(Path.GetRelativePath(root, s));
                q.Enqueue((s, depth + 1));
            }
        }
        return res;
    }

    private static List<Regex> LoadGitIgnore(string root)
    {
        var list = new List<Regex>();
        try
        {
            var gi = Path.Combine(root, ".gitignore");
            if (!File.Exists(gi)) return list;
            foreach (var raw in File.ReadAllLines(gi))
            {
                var l = raw.Trim();
                if (l.Length == 0 || l.StartsWith('#') || l.StartsWith('!')) continue;
                l = l.Trim('/');
                if (l.Contains('/')) continue; // podržavamo samo jednostavne obrasce po imenu
                var rx = "^" + Regex.Escape(l).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                list.Add(new Regex(rx, RegexOptions.IgnoreCase | RegexOptions.Compiled));
            }
        }
        catch { }
        return list;
    }

    // ---------------- grep ----------------
    private static readonly Dictionary<string, (DateTime, string[])> _contentCache = new();

    /// <summary>Čita linije fajla; keš samo za male fajlove (gd, pregled). Null za binarne/prevelike.</summary>
    public static string[] ReadLinesCached(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length > 3_000_000) return null;
            lock (_contentCache)
                if (_contentCache.TryGetValue(path, out var c) && c.Item1 == fi.LastWriteTimeUtc) return c.Item2;
            var lines = ReadTextLines(path, 3_000_000);
            if (fi.Length < 256_000)
                lock (_contentCache)
                {
                    if (_contentCache.Count > 3000) _contentCache.Clear();
                    _contentCache[path] = (fi.LastWriteTimeUtc, lines);
                }
            return lines;
        }
        catch { return null; }
    }

    /// <summary>Čita tekstualni fajl; null ako je binaran ili veći od maxBytes.</summary>
    public static string[] ReadTextLines(string path, long maxBytes)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
            if (fs.Length > maxBytes) return null;
            var buf = new byte[fs.Length];
            int read = 0;
            while (read < buf.Length) { int n = fs.Read(buf, read, buf.Length - read); if (n <= 0) break; read += n; }
            if (Array.IndexOf(buf, (byte)0, 0, Math.Min(read, 8000)) >= 0) return null;
            var text = System.Text.Encoding.UTF8.GetString(buf, 0, read);
            return text.Replace("\r\n", "\n").Split('\n');
        }
        catch { return null; }
    }

    public static Regex MakeRegex(string query, bool ignoreCase, bool smartCase, bool literal = false)
    {
        bool ic = ignoreCase && !(smartCase && query.Any(char.IsUpper));
        var opts = RegexOptions.CultureInvariant | (ic ? RegexOptions.IgnoreCase : 0);
        if (!literal)
        {
            try { return new Regex(query, opts, TimeSpan.FromMilliseconds(200)); } catch { }
        }
        return new Regex(Regex.Escape(query), opts, TimeSpan.FromMilliseconds(200));
    }

    /// <summary>Sinhroni grep (za testove i male skupove fajlova).</summary>
    public static List<PickerItem> Grep(string root, IEnumerable<string> relFiles, string query, Options o, int max = 2000, bool literal = false)
    {
        var res = new List<PickerItem>();
        if (string.IsNullOrEmpty(query)) return res;
        var rx = MakeRegex(query, true, true, literal);
        foreach (var rel in relFiles)
        {
            GrepFile(root, rel, rx, it => { res.Add(it); return res.Count < max; });
            if (res.Count >= max) break;
        }
        return res;
    }

    /// <summary>
    /// Paralelni grep u pozadini nad indeksom koji se možda još puni.
    /// Rezultati se prijavljuju preko onHit (poziva se sa više niti!).
    /// </summary>
    public static void GrepIndex(FileIndex index, string query, CancellationToken ct, Action<PickerItem> onHit, int max = 2000)
    {
        if (string.IsNullOrEmpty(query)) return;
        var rx = MakeRegex(query, true, true);
        Volatile.Write(ref index.Scanned, 0);
        int next = -1, hits = 0;
        void Worker()
        {
            while (!ct.IsCancellationRequested && Volatile.Read(ref hits) < max)
            {
                int i = Interlocked.Increment(ref next);
                string rel;
                while (true)
                {
                    if (ct.IsCancellationRequested) return;
                    if (i < index.Count) { rel = index[i]; break; }
                    if (index.Done) return;
                    Thread.Sleep(5);
                }
                GrepFile(index.Root, rel, rx, it =>
                {
                    if (ct.IsCancellationRequested) return false;
                    if (Interlocked.Increment(ref hits) > max) return false;
                    onHit(it);
                    return true;
                }, 1_000_000);
                Interlocked.Increment(ref index.Scanned);
            }
        }
        // jedno jezgro uvek ostaje slobodno za UI; niže prioritet da tasteri ne kasne
        int n = Math.Clamp(Environment.ProcessorCount - 1, 1, 6);
        var threads = new Thread[n];
        for (int t = 0; t < n; t++)
        {
            threads[t] = new Thread(() => { try { Worker(); } catch { } }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "psvim-grep" };
            threads[t].Start();
        }
        foreach (var th in threads) th.Join();
    }

    private static void GrepFile(string root, string rel, Regex rx, Func<PickerItem, bool> onHit, long maxBytes = 3_000_000)
    {
        var full = Path.Combine(root, rel);
        var lines = ReadTextLines(full, maxBytes);
        if (lines == null) return;
        for (int i = 0; i < lines.Length; i++)
        {
            Match m;
            try { m = rx.Match(lines[i]); } catch { return; }
            if (!m.Success) continue;
            var text = lines[i].Trim();
            if (text.Length > 200) text = text.Substring(0, 200);
            if (!onHit(new PickerItem { Text = $"{rel}:{i + 1}: {text}", Path = full, Line = i, Col = m.Index })) return;
        }
    }

    // ---------------- nedavni fajlovi i pozicije ----------------
    private static string RecentPath => Path.Combine(Options.DataDir(), "recent.txt");

    public static List<(string Path, int Line, int Col)> LoadRecent()
    {
        var res = new List<(string, int, int)>();
        try
        {
            if (!File.Exists(RecentPath)) return res;
            foreach (var l in File.ReadAllLines(RecentPath))
            {
                var p = l.Split('\t');
                if (p.Length < 3) continue;
                res.Add((p[0], int.TryParse(p[1], out var a) ? a : 0, int.TryParse(p[2], out var b) ? b : 0));
            }
        }
        catch { }
        return res;
    }

    public static void SaveRecent(IEnumerable<(string Path, int Line, int Col)> updates)
    {
        try
        {
            var list = LoadRecent();
            foreach (var u in updates.Reverse())
            {
                list.RemoveAll(x => string.Equals(x.Path, u.Path, StringComparison.OrdinalIgnoreCase));
                list.Insert(0, u);
            }
            if (list.Count > 300) list = list.Take(300).ToList();
            File.WriteAllLines(RecentPath, list.Select(x => $"{x.Path}\t{x.Line}\t{x.Col}"));
        }
        catch { }
    }

    private static readonly Dictionary<string, (long At, string Branch)> _branchCache = new();

    public static string GitBranch(string start)
    {
        if (start == null) return null;
        var now = Environment.TickCount64;
        if (_branchCache.TryGetValue(start, out var c) && now - c.At < 3000) return c.Branch;
        var b = GitBranchUncached(start);
        _branchCache[start] = (now, b);
        return b;
    }

    private static string GitBranchUncached(string start)
    {
        try
        {
            var dir = start;
            if (File.Exists(dir)) dir = Path.GetDirectoryName(dir);
            while (!string.IsNullOrEmpty(dir))
            {
                var g = Path.Combine(dir, ".git");
                string head = null;
                if (Directory.Exists(g)) head = Path.Combine(g, "HEAD");
                else if (File.Exists(g))
                {
                    var t = File.ReadAllText(g).Trim();
                    if (t.StartsWith("gitdir:")) head = Path.Combine(Path.GetFullPath(Path.Combine(dir, t.Substring(7).Trim())), "HEAD");
                }
                if (head != null && File.Exists(head))
                {
                    var h = File.ReadAllText(head).Trim();
                    const string pre = "ref: refs/heads/";
                    return h.StartsWith(pre) ? h.Substring(pre.Length) : h.Substring(0, Math.Min(7, h.Length));
                }
                dir = Path.GetDirectoryName(dir);
            }
        }
        catch { }
        return null;
    }

    public static string FindInPath(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var exts = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';')
            : new[] { "" };
        foreach (var d in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(d)) continue;
            foreach (var e in exts)
            {
                try
                {
                    var f = Path.Combine(d.Trim(), exe + e.ToLowerInvariant());
                    if (File.Exists(f)) return f;
                    f = Path.Combine(d.Trim(), exe + e);
                    if (File.Exists(f)) return f;
                }
                catch { }
            }
        }
        return null;
    }
}

/// <summary>Indeks fajlova koji se puni u pozadini (može da se koristi dok još nije završen).</summary>
public sealed class FileIndex
{
    public readonly string Root;
    private readonly List<string> _files = new();
    private readonly CancellationTokenSource _cts = new();
    private volatile bool _done;
    public int Scanned; // koliko fajlova je grep pregledao (za status)
    public readonly DateTime Created = DateTime.UtcNow;
    public bool Done => _done;
    public int Count { get { lock (_files) return _files.Count; } }
    public string this[int i] { get { lock (_files) return _files[i]; } }

    public FileIndex(string root, Options o, int max = 300_000)
    {
        Root = root;
        var th = new Thread(() =>
        {
            try
            {
                Files.Walk(root, o, _cts.Token, f =>
                {
                    lock (_files) _files.Add(f);
                    return _files.Count < max;
                });
            }
            catch { }
            finally { _done = true; }
        }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "psvim-index" };
        th.Start();
    }

    public List<string> Snapshot(int from = 0)
    {
        lock (_files) return from >= _files.Count ? new List<string>() : _files.GetRange(from, _files.Count - from);
    }

    /// <summary>Čeka da se indeks završi najviše ms milisekundi.</summary>
    public bool Wait(int ms)
    {
        var until = Environment.TickCount64 + ms;
        while (!_done && Environment.TickCount64 < until) Thread.Sleep(10);
        return _done;
    }

    public void Cancel() => _cts.Cancel();
}

/// <summary>Stablo fajlova (neo-tree).</summary>
public sealed class FileTree
{
    public sealed class Node
    {
        public string Path, Name; public bool IsDir; public List<Node> Children; public Node Parent;
    }

    public string Root { get; private set; }
    public readonly HashSet<string> Expanded = new(StringComparer.OrdinalIgnoreCase);
    public List<(Node Node, int Depth)> Visible = new();
    public int Sel, Scroll;
    private Node _root;
    private readonly Options _o;

    public FileTree(string root, Options o) { _o = o; SetRoot(root); }

    public void SetRoot(string root)
    {
        Root = System.IO.Path.GetFullPath(root);
        Expanded.Add(Root);
        Refresh();
    }

    public void Refresh()
    {
        _root = new Node { Path = Root, Name = System.IO.Path.GetFileName(Root.TrimEnd('\\', '/')), IsDir = true };
        if (string.IsNullOrEmpty(_root.Name)) _root.Name = Root;
        Rebuild();
    }

    private void Load(Node n)
    {
        n.Children = new List<Node>();
        try
        {
            var ignore = new HashSet<string>(_o.Ignore ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            ignore.Remove("bin"); ignore.Remove("obj"); ignore.Remove("dist"); ignore.Remove("packages");
            var dirs = new List<Node>(); var files = new List<Node>();
            foreach (var e in Directory.EnumerateFileSystemEntries(n.Path))
            {
                var name = System.IO.Path.GetFileName(e);
                if (!_o.ShowHidden && (name.StartsWith('.') || ignore.Contains(name))) continue;
                bool d = Directory.Exists(e);
                (d ? dirs : files).Add(new Node { Path = e, Name = name, IsDir = d, Parent = n });
            }
            dirs.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            files.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            n.Children.AddRange(dirs); n.Children.AddRange(files);
        }
        catch { }
    }

    public void Rebuild()
    {
        var selPath = Sel < Visible.Count && Sel >= 0 ? Visible[Sel].Node.Path : null;
        Visible.Clear();
        void Walk(Node n, int depth)
        {
            if (n.Children == null) Load(n);
            foreach (var c in n.Children)
            {
                Visible.Add((c, depth));
                if (c.IsDir && Expanded.Contains(c.Path)) Walk(c, depth + 1);
            }
        }
        Walk(_root, 0);
        if (selPath != null)
        {
            int i = Visible.FindIndex(v => string.Equals(v.Node.Path, selPath, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) Sel = i;
        }
        Sel = Math.Clamp(Sel, 0, Math.Max(0, Visible.Count - 1));
    }

    public Node Current => Sel >= 0 && Sel < Visible.Count ? Visible[Sel].Node : null;

    public void Toggle(Node n)
    {
        if (!n.IsDir) return;
        if (!Expanded.Remove(n.Path)) { Expanded.Add(n.Path); n.Children = null; }
        Rebuild();
    }

    public void Collapse()
    {
        var n = Current; if (n == null) return;
        if (n.IsDir && Expanded.Contains(n.Path)) { Expanded.Remove(n.Path); Rebuild(); return; }
        var p = n.Parent;
        if (p != null && p != _root)
        {
            Sel = Visible.FindIndex(v => v.Node == p);
            Expanded.Remove(p.Path);
            Rebuild();
        }
    }

    public void Reveal(string path)
    {
        if (path == null) return;
        var full = System.IO.Path.GetFullPath(path);
        if (!full.StartsWith(Root, StringComparison.OrdinalIgnoreCase)) return;
        var dir = System.IO.Path.GetDirectoryName(full);
        while (dir != null && dir.Length >= Root.Length)
        {
            Expanded.Add(dir);
            dir = System.IO.Path.GetDirectoryName(dir);
        }
        Refresh();
        int i = Visible.FindIndex(v => string.Equals(v.Node.Path, full, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) Sel = i;
    }

    public string TargetDir()
    {
        var n = Current;
        if (n == null) return Root;
        if (n.IsDir && Expanded.Contains(n.Path)) return n.Path;
        return System.IO.Path.GetDirectoryName(n.Path);
    }
}
