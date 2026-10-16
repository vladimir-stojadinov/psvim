using System.Diagnostics;

namespace PsVim;

/// <summary>
/// Sloj kompatibilnosti: isti kod radi na .NET 8 (PowerShell 7) i .NET Framework 4.7.2 (Windows PowerShell 5.1).
/// </summary>
public static class Compat
{
    public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
    public static long Clamp(long v, long min, long max) => v < min ? min : (v > max ? max : v);

    private static readonly Stopwatch _clock = Stopwatch.StartNew();
    /// <summary>Monotono vreme u milisekundama (zamena za Environment.TickCount64).</summary>
    public static long Ticks => _clock.ElapsedMilliseconds;

    public static bool IsWindows => Path.DirectorySeparatorChar == '\\';

    public static string ProcessPath
    {
        get
        {
#if NETFRAMEWORK
            try { return Process.GetCurrentProcess().MainModule?.FileName; } catch { return null; }
#else
            return Environment.ProcessPath;
#endif
        }
    }

    public static int Hash(int a, int b) { unchecked { return a * 397 ^ b; } }
    public static int Hash(int a, int b, int c) { unchecked { return (a * 397 ^ b) * 397 ^ c; } }

    public static string RelativePath(string relativeTo, string path)
    {
#if NETFRAMEWORK
        var from = Path.GetFullPath(relativeTo).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        var to = Path.GetFullPath(path);
        var cmp = IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (to.StartsWith(from, cmp)) return to.Substring(from.Length);
        if (string.Equals(to.TrimEnd('\\', '/'), from.TrimEnd('\\', '/'), cmp)) return ".";
        try
        {
            var u1 = new Uri(from); var u2 = new Uri(to);
            if (!string.Equals(u1.Scheme, u2.Scheme, StringComparison.OrdinalIgnoreCase)) return to;
            var rel = Uri.UnescapeDataString(u1.MakeRelativeUri(u2).ToString()).Replace('/', Path.DirectorySeparatorChar);
            return string.IsNullOrEmpty(rel) ? "." : rel;
        }
        catch { return to; }
#else
        return Path.GetRelativePath(relativeTo, path);
#endif
    }

    /// <summary>Nabraja stavke foldera ne prekidajući se na nedostupnim fajlovima.</summary>
    public static IEnumerable<FileSystemInfo> EnumerateEntries(DirectoryInfo di)
    {
#if NETFRAMEWORK
        return di.EnumerateFileSystemInfos("*");
#else
        return di.EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false, ReturnSpecialDirectories = false, AttributesToSkip = 0 });
#endif
    }
}

#if NETFRAMEWORK
/// <summary>Metode koje postoje u .NET Core-u, a nedostaju u .NET Framework-u.</summary>
public static class FrameworkExtensions
{
    public static bool Contains(this string s, char c) => s.IndexOf(c) >= 0;
    public static bool Contains(this string s, string v, StringComparison cmp) => s.IndexOf(v, cmp) >= 0;
    public static bool StartsWith(this string s, char c) => s.Length > 0 && s[0] == c;
    public static bool EndsWith(this string s, char c) => s.Length > 0 && s[s.Length - 1] == c;
    public static string[] Split(this string s, char c, StringSplitOptions o) => s.Split(new[] { c }, o);
    public static string[] Split(this string s, char c, int count) => s.Split(new[] { c }, count);
    public static string Replace(this string s, string oldValue, string newValue, StringComparison cmp)
    {
        if (string.IsNullOrEmpty(oldValue)) return s;
        var sb = new System.Text.StringBuilder();
        int i = 0, j;
        while ((j = s.IndexOf(oldValue, i, cmp)) >= 0) { sb.Append(s, i, j - i).Append(newValue); i = j + oldValue.Length; }
        return sb.Append(s, i, s.Length - i).ToString();
    }
    public static TV GetValueOrDefault<TK, TV>(this Dictionary<TK, TV> d, TK key) => d.TryGetValue(key, out var v) ? v : default;
}
#endif
