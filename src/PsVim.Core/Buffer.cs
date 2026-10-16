using System.Text;

namespace PsVim;

public struct Pos : IComparable<Pos>, IEquatable<Pos>
{
    public int L, C;
    public Pos(int l, int c) { L = l; C = c; }
    public int CompareTo(Pos o) => L != o.L ? L.CompareTo(o.L) : C.CompareTo(o.C);
    public bool Equals(Pos o) => L == o.L && C == o.C;
    public override bool Equals(object obj) => obj is Pos p && Equals(p);
    public override int GetHashCode() => HashCode.Combine(L, C);
    public static bool operator <(Pos a, Pos b) => a.CompareTo(b) < 0;
    public static bool operator >(Pos a, Pos b) => a.CompareTo(b) > 0;
    public static bool operator <=(Pos a, Pos b) => a.CompareTo(b) <= 0;
    public static bool operator >=(Pos a, Pos b) => a.CompareTo(b) >= 0;
    public static bool operator ==(Pos a, Pos b) => a.Equals(b);
    public static bool operator !=(Pos a, Pos b) => !a.Equals(b);
    public override string ToString() => $"{L + 1}:{C + 1}";
}

public sealed class Diagnostic
{
    public int Line, Col;
    public string Severity; // Error, Warning, Information
    public string Message;
    public string Source;
}

public sealed class Snapshot
{
    public string[] Lines;
    public Pos Cursor;
    public long ChangeId;
}

public sealed class Buffer
{
    private static long _ids;
    private static int _bufNums;

    public List<string> Lines = new() { "" };
    public string Path;           // pun put ili null
    public string DisplayName => Path == null ? (ScratchName ?? "[Bez imena]") : System.IO.Path.GetFileName(Path);
    public string ScratchName;
    public int Number = ++_bufNums;
    public int Version;           // raste sa svakom izmenom (keš za bojenje)
    public long ChangeId;         // identitet stanja za undo
    public long SavedChangeId;
    public bool Modified => ChangeId != SavedChangeId;
    public bool ReadOnly;
    public bool IsBinary;
    public bool IsScratch;
    public string FileType = "text";
    public string Eol = "\r\n";
    public bool Bom;

    // stanje prozora po baferu
    public Pos Cursor;
    public int WantCol;
    public int Top, Left;

    public readonly Stack<Snapshot> Undo = new();
    public readonly Stack<Snapshot> Redo = new();
    public List<Diagnostic> Diagnostics = new();
    public int DiagVersion = -1;
    public Dictionary<char, Pos> Marks = new();

    public int Count => Lines.Count;
    public string this[int i] => Lines[i];

    public static Buffer FromText(string text, string path = null)
    {
        var b = new Buffer { Path = path };
        b.SetText(text ?? "");
        b.SavedChangeId = b.ChangeId;
        b.FileType = Syntax.DetectFileType(path);
        return b;
    }

    public static Buffer Load(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        var b = new Buffer { Path = path, FileType = Syntax.DetectFileType(path) };
        if (File.Exists(path))
        {
            var bytes = File.ReadAllBytes(path);
            int off = 0;
            Encoding enc = new UTF8Encoding(false);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) { b.Bom = true; off = 3; }
            else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) { enc = Encoding.Unicode; off = 2; b.Bom = true; }
            var text = enc.GetString(bytes, off, bytes.Length - off);
            // binarni fajl (exe, dll, zip...): prikaži, ali zabrani izmene
            if (Array.IndexOf(bytes, (byte)0, off, Math.Min(8000, bytes.Length - off)) >= 0)
            {
                b.IsBinary = true;
                b.ReadOnly = true;
            }
            b.Eol = text.Contains("\r\n") ? "\r\n" : (text.Contains('\n') ? "\n" : (OperatingSystem.IsWindows() ? "\r\n" : "\n"));
            b.SetText(text);
            // kao vim: poslednji \n ne pravi dodatnu praznu liniju
            if (b.Lines.Count > 1 && b.Lines[^1].Length == 0) b.Lines.RemoveAt(b.Lines.Count - 1);
        }
        else
        {
            b.Eol = OperatingSystem.IsWindows() ? "\r\n" : "\n";
        }
        b.SavedChangeId = b.ChangeId;
        return b;
    }

    public void SetText(string text)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        Lines = new List<string>(text.Split('\n'));
        if (Lines.Count == 0) Lines.Add("");
        Touch();
    }

    public string GetAllText(string eol = "\n") => string.Join(eol, Lines);

    public void Save(string path = null)
    {
        if (path != null) { Path = System.IO.Path.GetFullPath(path); FileType = Syntax.DetectFileType(Path); }
        if (Path == null) throw new InvalidOperationException("Nema imena fajla (koristi :w ime)");
        var dir = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var text = string.Join(Eol, Lines) + Eol;
        var enc = new UTF8Encoding(Bom);
        File.WriteAllText(Path, text, enc);
        SavedChangeId = ChangeId;
    }

    private void Touch()
    {
        Version++;
        ChangeId = Interlocked.Increment(ref _ids) + 1_000_000;
    }

    // ---------- undo ----------
    public Snapshot TakeSnapshot() => new() { Lines = Lines.ToArray(), Cursor = Cursor, ChangeId = ChangeId };

    public void Restore(Snapshot s)
    {
        Lines = new List<string>(s.Lines);
        Cursor = s.Cursor;
        ChangeId = s.ChangeId;
        Version++;
    }

    public void CommitUndo(Snapshot before)
    {
        Undo.Push(before);
        Redo.Clear();
        if (Undo.Count > 2000)
        {
            var arr = Undo.ToArray();
            Undo.Clear();
            for (int i = 999; i >= 0; i--) Undo.Push(arr[i]);
        }
    }

    // ---------- primitivne izmene ----------
    public int LineLen(int l) => Lines[l].Length;

    public Pos Clamp(Pos p)
    {
        int l = Math.Clamp(p.L, 0, Lines.Count - 1);
        int c = Math.Clamp(p.C, 0, Lines[l].Length);
        return new Pos(l, c);
    }

    /// <summary>Ubacuje tekst (može sadržati \n) na poziciju. Vraća poziciju iza ubačenog teksta.</summary>
    public Pos Insert(Pos p, string text)
    {
        if (string.IsNullOrEmpty(text)) return p;
        p = Clamp(p);
        var line = Lines[p.L];
        var before = line.Substring(0, p.C);
        var after = line.Substring(p.C);
        var parts = text.Split('\n');
        if (parts.Length == 1)
        {
            Lines[p.L] = before + text + after;
            Touch();
            return new Pos(p.L, p.C + text.Length);
        }
        Lines[p.L] = before + parts[0];
        var newLines = new List<string>();
        for (int i = 1; i < parts.Length - 1; i++) newLines.Add(parts[i]);
        newLines.Add(parts[^1] + after);
        Lines.InsertRange(p.L + 1, newLines);
        Touch();
        return new Pos(p.L + parts.Length - 1, parts[^1].Length);
    }

    /// <summary>Briše [a, b) gde b može biti na drugoj liniji. Vraća obrisani tekst.</summary>
    public string Delete(Pos a, Pos b)
    {
        if (b < a) (a, b) = (b, a);
        a = Clamp(a);
        // b može biti (L, len+1) što znači "uključujući kraj linije"
        if (b.L >= Lines.Count) b = new Pos(Lines.Count - 1, Lines[^1].Length);
        if (b.C > Lines[b.L].Length)
        {
            if (b.L + 1 < Lines.Count) b = new Pos(b.L + 1, 0);
            else b = new Pos(b.L, Lines[b.L].Length);
        }
        if (a == b) return "";
        var text = GetText(a, b);
        var head = Lines[a.L].Substring(0, a.C);
        var tail = Lines[b.L].Substring(b.C);
        Lines[a.L] = head + tail;
        if (b.L > a.L) Lines.RemoveRange(a.L + 1, b.L - a.L);
        Touch();
        return text;
    }

    public string GetText(Pos a, Pos b)
    {
        if (b < a) (a, b) = (b, a);
        a = Clamp(a);
        if (b.L >= Lines.Count) b = new Pos(Lines.Count - 1, Lines[^1].Length);
        if (b.C > Lines[b.L].Length)
        {
            if (b.L + 1 < Lines.Count) b = new Pos(b.L + 1, 0);
            else b = new Pos(b.L, Lines[b.L].Length);
        }
        if (a.L == b.L) return Lines[a.L].Substring(a.C, b.C - a.C);
        var sb = new StringBuilder();
        sb.Append(Lines[a.L].AsSpan(a.C)).Append('\n');
        for (int l = a.L + 1; l < b.L; l++) sb.Append(Lines[l]).Append('\n');
        sb.Append(Lines[b.L].AsSpan(0, b.C));
        return sb.ToString();
    }

    public List<string> GetLines(int from, int to) => Lines.GetRange(from, to - from + 1);

    public void DeleteLines(int from, int to)
    {
        from = Math.Max(0, from); to = Math.Min(Lines.Count - 1, to);
        if (to < from) return;
        Lines.RemoveRange(from, to - from + 1);
        if (Lines.Count == 0) Lines.Add("");
        Touch();
    }

    public void InsertLines(int at, IEnumerable<string> lines)
    {
        at = Math.Clamp(at, 0, Lines.Count);
        Lines.InsertRange(at, lines);
        Touch();
    }

    public void SetLine(int l, string text)
    {
        if (Lines[l] == text) return;
        Lines[l] = text;
        Touch();
    }

    public void ReplaceAll(List<string> lines)
    {
        Lines = lines.Count == 0 ? new List<string> { "" } : lines;
        Touch();
    }

    public static string IndentOf(string s)
    {
        int i = 0;
        while (i < s.Length && (s[i] == ' ' || s[i] == '\t')) i++;
        return s.Substring(0, i);
    }

    public int FirstNonBlank(int l)
    {
        var s = Lines[l];
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        return i >= s.Length ? Math.Max(0, s.Length - 1) : i;
    }
}
