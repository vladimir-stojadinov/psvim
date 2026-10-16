namespace PsVim;

public struct Span
{
    public int Start, Len; public Tok Kind;
    public Span(int s, int l, Tok k) { Start = s; Len = l; Kind = k; }
}

/// <summary>Stanje leksera na početku linije (za višelinijske komentare i stringove).</summary>
public enum LexState : byte { Normal, BlockComment, HereDouble, HereSingle, CodeFence, TripleDouble, TripleSingle }

public interface ILexer
{
    LexState Lex(string line, LexState state, List<Span> spans);
}

public static class Syntax
{
    public static string DetectFileType(string path)
    {
        if (path == null) return "text";
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        var name = System.IO.Path.GetFileName(path).ToLowerInvariant();
        if (name == "dockerfile") return "sh";
        if (name == "makefile") return "sh";
        return ext switch
        {
            ".ps1" or ".psm1" or ".psd1" or ".ps1xml" => "powershell",
            ".cs" or ".csx" => "cs",
            ".js" or ".mjs" or ".cjs" or ".jsx" => "javascript",
            ".ts" or ".tsx" => "typescript",
            ".json" or ".jsonc" => "json",
            ".py" => "python",
            ".sh" or ".bash" or ".zsh" => "sh",
            ".md" or ".markdown" => "markdown",
            ".yml" or ".yaml" => "yaml",
            ".toml" or ".ini" or ".cfg" => "toml",
            ".xml" or ".csproj" or ".props" or ".targets" or ".html" or ".htm" or ".xaml" or ".svg" or ".config" => "xml",
            ".c" or ".h" or ".cpp" or ".hpp" or ".cc" => "c",
            ".java" or ".kt" => "java",
            ".go" => "go",
            ".rs" => "rust",
            ".lua" => "lua",
            ".sql" => "sql",
            ".css" or ".scss" => "css",
            ".bat" or ".cmd" => "bat",
            _ => "text"
        };
    }

    private static readonly Dictionary<string, ILexer> _lexers = new();

    public static ILexer For(string ft)
    {
        if (_lexers.TryGetValue(ft, out var l)) return l;
        l = ft switch
        {
            "powershell" => new PowerShellLexer(),
            "markdown" => new MarkdownLexer(),
            "xml" => new XmlLexer(),
            "json" => new CLikeLexer(null, null, null, new[] { "true", "false", "null" }, jsonKeys: true),
            "cs" => new CLikeLexer("//", "/*", "*/", CsKeywords),
            "javascript" or "typescript" => new CLikeLexer("//", "/*", "*/", JsKeywords),
            "c" => new CLikeLexer("//", "/*", "*/", CKeywords),
            "java" => new CLikeLexer("//", "/*", "*/", JavaKeywords),
            "go" => new CLikeLexer("//", "/*", "*/", GoKeywords),
            "rust" => new CLikeLexer("//", "/*", "*/", RustKeywords),
            "css" => new CLikeLexer(null, "/*", "*/", Array.Empty<string>()),
            "python" => new CLikeLexer("#", null, null, PyKeywords, tripleQuotes: true),
            "sh" => new CLikeLexer("#", null, null, ShKeywords, dollarVars: true),
            "yaml" => new CLikeLexer("#", null, null, new[] { "true", "false", "null", "yes", "no" }, yamlKeys: true),
            "toml" => new CLikeLexer("#", null, null, new[] { "true", "false" }, yamlKeys: true),
            "lua" => new CLikeLexer("--", null, null, LuaKeywords),
            "sql" => new CLikeLexer("--", "/*", "*/", SqlKeywords, ignoreCase: true),
            "bat" => new CLikeLexer("::", null, null, BatKeywords, ignoreCase: true, dollarVars: false),
            _ => null
        };
        _lexers[ft] = l;
        return l;
    }

    /// <summary>Simbol za komentarisanje linije (gcc).</summary>
    public static string CommentString(string ft) => ft switch
    {
        "powershell" or "python" or "sh" or "yaml" or "toml" => "#",
        "lua" or "sql" => "--",
        "bat" => "::",
        "xml" or "markdown" => "<!--",
        "text" => "#",
        _ => "//"
    };

    static readonly string[] CsKeywords = "abstract as async await base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null object operator out override params private protected public readonly record ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using var virtual void volatile while yield get set init value when where nameof".Split(' ');
    static readonly string[] JsKeywords = "async await break case catch class const continue debugger default delete do else export extends false finally for from function if import in instanceof let new null of return static super switch this throw true try typeof undefined var void while with yield interface type enum implements private public protected readonly as any number string boolean".Split(' ');
    static readonly string[] CKeywords = "auto break case char const continue default do double else enum extern float for goto if inline int long register return short signed sizeof static struct switch typedef union unsigned void volatile while class namespace public private protected template typename virtual new delete this true false nullptr include define".Split(' ');
    static readonly string[] JavaKeywords = "abstract boolean break byte case catch char class const continue default do double else enum extends final finally float for if implements import instanceof int interface long native new null package private protected public return short static super switch synchronized this throw throws true false try void volatile while fun val var when object data".Split(' ');
    static readonly string[] GoKeywords = "break case chan const continue default defer else fallthrough for func go goto if import interface map package range return select struct switch type var nil true false string int error bool byte".Split(' ');
    static readonly string[] RustKeywords = "as async await break const continue crate else enum extern false fn for if impl in let loop match mod move mut pub ref return self Self static struct super trait true type unsafe use where while Some None Ok Err".Split(' ');
    static readonly string[] PyKeywords = "and as assert async await break class continue def del elif else except False finally for from global if import in is lambda None nonlocal not or pass raise return True try while with yield self".Split(' ');
    static readonly string[] ShKeywords = "if then else elif fi case esac for while until do done in function return local export echo exit set unset source alias".Split(' ');
    static readonly string[] LuaKeywords = "and break do else elseif end false for function goto if in local nil not or repeat return then true until while".Split(' ');
    static readonly string[] SqlKeywords = "select from where insert into update delete create table drop alter index join left right inner outer on and or not null is as group by order having limit values set primary key foreign references distinct union all case when then else end begin commit".Split(' ');
    static readonly string[] BatKeywords = "echo set if else goto call exit for in do not exist defined errorlevel rem setlocal endlocal".Split(' ');
}

/// <summary>Lekser za PowerShell.</summary>
public sealed class PowerShellLexer : ILexer
{
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "begin","break","catch","class","continue","data","define","do","dynamicparam","else","elseif","end","enum","exit",
        "filter","finally","for","foreach","from","function","hidden","if","in","param","process","return","static","switch",
        "throw","trap","try","until","using","var","while","workflow","parallel","sequence","inlinescript","configuration",
        "clean","default"
    };
    private static readonly HashSet<string> Operators = new(StringComparer.OrdinalIgnoreCase)
    {
        "eq","ne","gt","ge","lt","le","like","notlike","match","notmatch","contains","notcontains","in","notin","replace",
        "split","join","is","isnot","as","and","or","not","xor","band","bor","bxor","bnot","shl","shr","f",
        "ceq","cne","cgt","cge","clt","cle","clike","cnotlike","cmatch","cnotmatch","ccontains","cnotcontains","cin","cnotin","creplace","csplit",
        "ieq","ine","igt","ige","ilt","ile","ilike","inotlike","imatch","inotmatch","icontains","inotcontains","iin","inotin","ireplace","isplit"
    };
    private static readonly HashSet<string> Constants = new(StringComparer.OrdinalIgnoreCase) { "$true", "$false", "$null" };

    public LexState Lex(string s, LexState state, List<Span> spans)
    {
        int i = 0, n = s.Length;
        if (state == LexState.BlockComment)
        {
            int e = s.IndexOf("#>", StringComparison.Ordinal);
            if (e < 0) { spans.Add(new Span(0, n, Tok.Comment)); return state; }
            spans.Add(new Span(0, e + 2, Tok.Comment));
            i = e + 2; state = LexState.Normal;
        }
        else if (state == LexState.HereDouble || state == LexState.HereSingle)
        {
            var end = state == LexState.HereDouble ? "\"@" : "'@";
            if (s.StartsWith(end, StringComparison.Ordinal))
            {
                spans.Add(new Span(0, 2, Tok.String));
                i = 2; state = LexState.Normal;
            }
            else
            {
                if (state == LexState.HereDouble) LexDoubleBody(s, 0, n, spans); else spans.Add(new Span(0, n, Tok.String));
                return state;
            }
        }

        bool afterFunction = false;
        char prevSig = ' ';
        while (i < n)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '<' && i + 1 < n && s[i + 1] == '#')
            {
                int e = s.IndexOf("#>", i + 2, StringComparison.Ordinal);
                if (e < 0) { spans.Add(new Span(i, n - i, Tok.Comment)); return LexState.BlockComment; }
                spans.Add(new Span(i, e + 2 - i, Tok.Comment)); i = e + 2; continue;
            }
            if (c == '#') { spans.Add(new Span(i, n - i, Tok.Comment)); return LexState.Normal; }
            if (c == '@' && i + 1 < n && (s[i + 1] == '"' || s[i + 1] == '\'') && s.Substring(i + 2).Trim().Length == 0)
            {
                spans.Add(new Span(i, n - i, Tok.String));
                return s[i + 1] == '"' ? LexState.HereDouble : LexState.HereSingle;
            }
            if (c == '"')
            {
                int j = i + 1;
                while (j < n)
                {
                    if (s[j] == '`' && j + 1 < n) { j += 2; continue; }
                    if (s[j] == '"') { if (j + 1 < n && s[j + 1] == '"') { j += 2; continue; } break; }
                    j++;
                }
                int end = Math.Min(n, j + 1);
                spans.Add(new Span(i, 1, Tok.String));
                LexDoubleBody(s, i + 1, Math.Min(j, n), spans);
                if (j < n) spans.Add(new Span(j, 1, Tok.String));
                i = end; prevSig = '"'; continue;
            }
            if (c == '\'')
            {
                int j = i + 1;
                while (j < n)
                {
                    if (s[j] == '\'') { if (j + 1 < n && s[j + 1] == '\'') { j += 2; continue; } break; }
                    j++;
                }
                int end = Math.Min(n, j + 1);
                spans.Add(new Span(i, end - i, Tok.String)); i = end; prevSig = '\''; continue;
            }
            if (c == '$')
            {
                int j = ScanVariable(s, i);
                var name = s.Substring(i, j - i);
                spans.Add(new Span(i, j - i, Constants.Contains(name) ? Tok.Constant : Tok.Variable));
                i = j; prevSig = 'v'; continue;
            }
            if (c == '[' && i + 1 < n && (char.IsLetter(s[i + 1]) || s[i + 1] == '_'))
            {
                int j = i + 1, depth = 1;
                while (j < n && depth > 0)
                {
                    char d = s[j];
                    if (d == '[') depth++;
                    else if (d == ']') depth--;
                    else if (!(char.IsLetterOrDigit(d) || d == '.' || d == '_' || d == ',' || d == '`' || d == ' ' || d == '+' || d == '(' || d == ')' || d == '=' || d == '\'' || d == '"' || d == '$' || d == '-')) break;
                    j++;
                }
                if (depth == 0 && prevSig != 'v' && prevSig != ']' && prevSig != ')')
                {
                    var inner = s.Substring(i + 1, j - i - 2);
                    bool attr = inner.IndexOf('(') >= 0;
                    spans.Add(new Span(i, j - i, attr ? Tok.Attribute : Tok.Type));
                    i = j; prevSig = 't'; continue;
                }
                spans.Add(new Span(i, 1, Tok.Punct)); i++; prevSig = '['; continue;
            }
            if (c == ':' && i + 1 < n && s[i + 1] == ':')
            {
                spans.Add(new Span(i, 2, Tok.Operator)); i += 2;
                int j = i; while (j < n && (char.IsLetterOrDigit(s[j]) || s[j] == '_')) j++;
                if (j > i) spans.Add(new Span(i, j - i, Tok.Member));
                i = j; prevSig = 'm'; continue;
            }
            if (c == '-' && i + 1 < n && (char.IsLetter(s[i + 1]) || s[i + 1] == '_') && (i == 0 || !char.IsLetterOrDigit(s[i - 1])))
            {
                int j = i + 1; while (j < n && (char.IsLetterOrDigit(s[j]) || s[j] == '_' || s[j] == '-' && false)) j++;
                var w = s.Substring(i + 1, j - i - 1);
                spans.Add(new Span(i, j - i, Operators.Contains(w) ? Tok.Operator : Tok.Parameter));
                // -Param:value
                i = j; prevSig = '-'; continue;
            }
            if (char.IsDigit(c) && (i == 0 || !char.IsLetter(s[i - 1])))
            {
                int j = i;
                if (c == '0' && i + 1 < n && (s[i + 1] == 'x' || s[i + 1] == 'X')) { j += 2; while (j < n && Uri.IsHexDigit(s[j])) j++; }
                else { while (j < n && (char.IsDigit(s[j]) || s[j] == '.' || s[j] == '_')) j++; }
                // sufiksi kb, mb, gb, tb, pb, d, l
                int k = j; while (k < n && char.IsLetter(s[k])) k++;
                var suf = s.Substring(j, k - j).ToLowerInvariant();
                if (suf is "kb" or "mb" or "gb" or "tb" or "pb" or "d" or "l" or "u" or "ul" or "y" or "e") j = k;
                if (j < n && (char.IsLetter(s[j]) || s[j] == '-')) { /* deo identifikatora */ }
                else { spans.Add(new Span(i, j - i, Tok.Number)); i = j; prevSig = '0'; continue; }
            }
            if (char.IsLetter(c) || c == '_')
            {
                int j = i;
                while (j < n && (char.IsLetterOrDigit(s[j]) || s[j] == '_' || s[j] == '-' || s[j] == '.' && false)) j++;
                // ne uključuj završni '-'
                while (j > i + 1 && s[j - 1] == '-') j--;
                var w = s.Substring(i, j - i);
                Tok t = Tok.Default;
                bool prevDot = i > 0 && s[i - 1] == '.';
                if (afterFunction) { t = Tok.Function; afterFunction = false; }
                else if (prevDot) t = (j < n && s[j] == '(') ? Tok.Function : Tok.Member;
                else if (Keywords.Contains(w) && !w.Contains('-'))
                {
                    t = Tok.Keyword;
                    if (w.Equals("function", StringComparison.OrdinalIgnoreCase) || w.Equals("filter", StringComparison.OrdinalIgnoreCase)
                        || w.Equals("class", StringComparison.OrdinalIgnoreCase) || w.Equals("enum", StringComparison.OrdinalIgnoreCase)
                        || w.Equals("configuration", StringComparison.OrdinalIgnoreCase))
                        afterFunction = true;
                }
                else if (IsVerbNoun(w)) t = Tok.Command;
                else if (prevSig == ' ' || prevSig == '|' || prevSig == '{' || prevSig == '(' || prevSig == ';' || prevSig == '&' || prevSig == '=')
                {
                    // prva reč u naredbi: verovatno komanda (git, ls, dotnet...)
                    if (prevSig != '=' && prevSig != '(') t = Tok.Command;
                }
                if (t != Tok.Default) spans.Add(new Span(i, j - i, t));
                i = j; prevSig = 'w'; continue;
            }
            if (c == '`' && i + 1 < n) { spans.Add(new Span(i, 2, Tok.Escape)); i += 2; continue; }
            if ("|=+*/%!><&,;.(){}]".IndexOf(c) >= 0)
            {
                Tok t = (c == '|' || c == '=' || c == '+' || c == '*' || c == '/' || c == '%' || c == '!' || c == '>' || c == '<' || c == '&') ? Tok.Operator : Tok.Punct;
                if (c == '.' && i + 1 < n && s[i + 1] == '.') { spans.Add(new Span(i, 2, Tok.Operator)); i += 2; prevSig = 'o'; continue; }
                spans.Add(new Span(i, 1, t));
                prevSig = c switch { '|' => '|', '{' => '{', '(' => '(', ';' => ';', '&' => '&', '=' => '=', ')' => ')', ']' => ']', '}' => '{', _ => 'o' };
                i++; continue;
            }
            i++; prevSig = 'o';
        }
        return LexState.Normal;
    }

    private static bool IsVerbNoun(string w)
    {
        int d = w.IndexOf('-');
        if (d <= 0 || d >= w.Length - 1) return false;
        for (int k = 0; k < d; k++) if (!char.IsLetter(w[k])) return false;
        return char.IsLetter(w[d + 1]);
    }

    public static int ScanVariable(string s, int i)
    {
        int n = s.Length, j = i + 1;
        if (j < n && s[j] == '{')
        {
            int e = s.IndexOf('}', j);
            return e < 0 ? n : e + 1;
        }
        if (j < n && (s[j] == '_' || s[j] == '?' || s[j] == '$' || s[j] == '^') && (j + 1 >= n || !char.IsLetterOrDigit(s[j + 1]))) return j + 1;
        if (j < n && s[j] == '(') return j; // podizraz $( — samo $
        while (j < n && (char.IsLetterOrDigit(s[j]) || s[j] == '_' || s[j] == ':' && j + 1 < n && char.IsLetter(s[j + 1]))) j++;
        return j;
    }

    private static void LexDoubleBody(string s, int from, int to, List<Span> spans)
    {
        int i = from, start = from;
        while (i < to)
        {
            if (s[i] == '`' && i + 1 < to)
            {
                if (i > start) spans.Add(new Span(start, i - start, Tok.String));
                spans.Add(new Span(i, 2, Tok.Escape)); i += 2; start = i; continue;
            }
            if (s[i] == '$' && i + 1 < to && (char.IsLetter(s[i + 1]) || s[i + 1] == '_' || s[i + 1] == '{' || s[i + 1] == '('))
            {
                if (i > start) spans.Add(new Span(start, i - start, Tok.String));
                int j;
                if (s[i + 1] == '(')
                {
                    int depth = 0; j = i + 1;
                    while (j < to) { if (s[j] == '(') depth++; else if (s[j] == ')') { depth--; if (depth == 0) { j++; break; } } j++; }
                }
                else j = Math.Min(to, ScanVariable(s, i));
                spans.Add(new Span(i, j - i, Tok.Variable)); i = j; start = i; continue;
            }
            i++;
        }
        if (to > start) spans.Add(new Span(start, to - start, Tok.String));
    }
}

/// <summary>Opšti lekser za jezike sa C-sintaksom, Python, shell, YAML, JSON...</summary>
public sealed class CLikeLexer : ILexer
{
    private readonly string _line, _bs, _be;
    private readonly HashSet<string> _kw;
    private readonly bool _jsonKeys, _yamlKeys, _triple, _dollar;

    public CLikeLexer(string line, string blockStart, string blockEnd, string[] kw, bool jsonKeys = false, bool yamlKeys = false,
        bool tripleQuotes = false, bool ignoreCase = false, bool dollarVars = false)
    {
        _line = line; _bs = blockStart; _be = blockEnd;
        _kw = new HashSet<string>(kw, ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        _jsonKeys = jsonKeys; _yamlKeys = yamlKeys; _triple = tripleQuotes; _dollar = dollarVars;
    }

    public LexState Lex(string s, LexState state, List<Span> spans)
    {
        int i = 0, n = s.Length;
        if (state == LexState.BlockComment)
        {
            int e = s.IndexOf(_be, StringComparison.Ordinal);
            if (e < 0) { spans.Add(new Span(0, n, Tok.Comment)); return state; }
            spans.Add(new Span(0, e + _be.Length, Tok.Comment)); i = e + _be.Length; state = LexState.Normal;
        }
        else if (state == LexState.TripleDouble || state == LexState.TripleSingle)
        {
            var q = state == LexState.TripleDouble ? "\"\"\"" : "'''";
            int e = s.IndexOf(q, StringComparison.Ordinal);
            if (e < 0) { spans.Add(new Span(0, n, Tok.String)); return state; }
            spans.Add(new Span(0, e + 3, Tok.String)); i = e + 3; state = LexState.Normal;
        }
        if (_yamlKeys)
        {
            // ključ: vrednost
            int k = i; while (k < n && char.IsWhiteSpace(s[k])) k++;
            if (k < n && s[k] == '-') { k++; while (k < n && s[k] == ' ') k++; }
            int st = k; while (k < n && (char.IsLetterOrDigit(s[k]) || s[k] == '_' || s[k] == '-' || s[k] == '.')) k++;
            if (k > st && k < n && (s[k] == ':' || s[k] == '=')) { spans.Add(new Span(st, k - st, Tok.Member)); i = k; }
            if (st < n && s[st] == '[') { int e = s.IndexOf(']', st); if (e > 0) { spans.Add(new Span(st, e - st + 1, Tok.Heading)); i = e + 1; } }
        }
        while (i < n)
        {
            char c = s[i];
            if (_line != null && string.CompareOrdinal(s, i, _line, 0, _line.Length) == 0)
            {
                spans.Add(new Span(i, n - i, Tok.Comment)); return LexState.Normal;
            }
            if (_bs != null && string.CompareOrdinal(s, i, _bs, 0, _bs.Length) == 0)
            {
                int e = s.IndexOf(_be, i + _bs.Length, StringComparison.Ordinal);
                if (e < 0) { spans.Add(new Span(i, n - i, Tok.Comment)); return LexState.BlockComment; }
                spans.Add(new Span(i, e + _be.Length - i, Tok.Comment)); i = e + _be.Length; continue;
            }
            if (_triple && (c == '"' || c == '\'') && i + 2 < n && s[i + 1] == c && s[i + 2] == c)
            {
                var q = new string(c, 3);
                int e = s.IndexOf(q, i + 3, StringComparison.Ordinal);
                if (e < 0) { spans.Add(new Span(i, n - i, Tok.String)); return c == '"' ? LexState.TripleDouble : LexState.TripleSingle; }
                spans.Add(new Span(i, e + 3 - i, Tok.String)); i = e + 3; continue;
            }
            if (c == '"' || c == '\'' || c == '`')
            {
                int j = i + 1;
                while (j < n && s[j] != c) { if (s[j] == '\\') j++; j++; }
                int end = Math.Min(n, j + 1);
                bool isKey = false;
                if (_jsonKeys) { int k = end; while (k < n && s[k] == ' ') k++; isKey = k < n && s[k] == ':'; }
                spans.Add(new Span(i, end - i, isKey ? Tok.Member : Tok.String)); i = end; continue;
            }
            if (_dollar && c == '$')
            {
                int j = i + 1;
                if (j < n && s[j] == '{') { int e = s.IndexOf('}', j); j = e < 0 ? n : e + 1; }
                else while (j < n && (char.IsLetterOrDigit(s[j]) || s[j] == '_')) j++;
                spans.Add(new Span(i, j - i, Tok.Variable)); i = j; continue;
            }
            if (char.IsDigit(c) && (i == 0 || !char.IsLetterOrDigit(s[i - 1]) && s[i - 1] != '_'))
            {
                int j = i; while (j < n && (char.IsLetterOrDigit(s[j]) || s[j] == '.' || s[j] == '_')) j++;
                spans.Add(new Span(i, j - i, Tok.Number)); i = j; continue;
            }
            if (char.IsLetter(c) || c == '_' || c == '@' || c == '#')
            {
                int j = i + 1; while (j < n && (char.IsLetterOrDigit(s[j]) || s[j] == '_')) j++;
                var w = s.Substring(i, j - i);
                if (_kw.Contains(w)) spans.Add(new Span(i, j - i, Tok.Keyword));
                else if (j < n && s[j] == '(') spans.Add(new Span(i, j - i, Tok.Function));
                else if (char.IsUpper(c) && w.Length > 1 && !_yamlKeys) spans.Add(new Span(i, j - i, Tok.Type));
                else if (c == '@' || c == '#') spans.Add(new Span(i, j - i, Tok.Attribute));
                i = j; continue;
            }
            if ("+-*/%=<>!&|^~?:".IndexOf(c) >= 0) { spans.Add(new Span(i, 1, Tok.Operator)); i++; continue; }
            if ("(){}[];,.".IndexOf(c) >= 0) { spans.Add(new Span(i, 1, Tok.Punct)); i++; continue; }
            i++;
        }
        return LexState.Normal;
    }
}

public sealed class MarkdownLexer : ILexer
{
    public LexState Lex(string s, LexState state, List<Span> spans)
    {
        var t = s.TrimStart();
        if (t.StartsWith("```"))
        {
            spans.Add(new Span(0, s.Length, Tok.Comment));
            return state == LexState.CodeFence ? LexState.Normal : LexState.CodeFence;
        }
        if (state == LexState.CodeFence) { spans.Add(new Span(0, s.Length, Tok.String)); return state; }
        if (t.StartsWith("#")) { spans.Add(new Span(0, s.Length, Tok.Heading)); return LexState.Normal; }
        int lead = s.Length - t.Length;
        if (t.StartsWith("- ") || t.StartsWith("* ") || t.StartsWith("+ ")) spans.Add(new Span(lead, 1, Tok.Keyword));
        else if (t.StartsWith(">")) { spans.Add(new Span(0, s.Length, Tok.Comment)); return LexState.Normal; }
        else
        {
            int k = 0; while (k < t.Length && char.IsDigit(t[k])) k++;
            if (k > 0 && k < t.Length && t[k] == '.') spans.Add(new Span(lead, k + 1, Tok.Keyword));
        }
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '`')
            {
                int e = s.IndexOf('`', i + 1);
                if (e > i) { spans.Add(new Span(i, e - i + 1, Tok.String)); i = e; }
            }
            else if (s[i] == '*' && i + 1 < s.Length && s[i + 1] == '*')
            {
                int e = s.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (e > i) { spans.Add(new Span(i, e - i + 2, Tok.Constant)); i = e + 1; }
            }
            else if (s[i] == '[')
            {
                int e = s.IndexOf("](", i, StringComparison.Ordinal);
                int e2 = e > 0 ? s.IndexOf(')', e) : -1;
                if (e > 0 && e2 > 0) { spans.Add(new Span(i, e - i + 1, Tok.Function)); spans.Add(new Span(e + 1, e2 - e, Tok.Comment)); i = e2; }
            }
        }
        return LexState.Normal;
    }
}

public sealed class XmlLexer : ILexer
{
    public LexState Lex(string s, LexState state, List<Span> spans)
    {
        int i = 0, n = s.Length;
        if (state == LexState.BlockComment)
        {
            int e = s.IndexOf("-->", StringComparison.Ordinal);
            if (e < 0) { spans.Add(new Span(0, n, Tok.Comment)); return state; }
            spans.Add(new Span(0, e + 3, Tok.Comment)); i = e + 3;
        }
        while (i < n)
        {
            if (string.CompareOrdinal(s, i, "<!--", 0, 4) == 0)
            {
                int e = s.IndexOf("-->", i + 4, StringComparison.Ordinal);
                if (e < 0) { spans.Add(new Span(i, n - i, Tok.Comment)); return LexState.BlockComment; }
                spans.Add(new Span(i, e + 3 - i, Tok.Comment)); i = e + 3; continue;
            }
            if (s[i] == '<')
            {
                int j = i + 1;
                if (j < n && (s[j] == '/' || s[j] == '?' || s[j] == '!')) j++;
                while (j < n && (char.IsLetterOrDigit(s[j]) || s[j] == ':' || s[j] == '-' || s[j] == '_' || s[j] == '.')) j++;
                spans.Add(new Span(i, j - i, Tok.Tag)); i = j;
                // atributi do '>'
                while (i < n && s[i] != '>')
                {
                    if (s[i] == '"' || s[i] == '\'')
                    {
                        int e = s.IndexOf(s[i], i + 1); e = e < 0 ? n - 1 : e;
                        spans.Add(new Span(i, e - i + 1, Tok.String)); i = e + 1; continue;
                    }
                    if (char.IsLetter(s[i]))
                    {
                        int k = i; while (k < n && (char.IsLetterOrDigit(s[k]) || s[k] == ':' || s[k] == '-' || s[k] == '_')) k++;
                        spans.Add(new Span(i, k - i, Tok.Attribute)); i = k; continue;
                    }
                    i++;
                }
                if (i < n) { spans.Add(new Span(i, 1, Tok.Tag)); i++; }
                continue;
            }
            if (s[i] == '&')
            {
                int e = s.IndexOf(';', i);
                if (e > i && e - i < 10) { spans.Add(new Span(i, e - i + 1, Tok.Escape)); i = e + 1; continue; }
            }
            i++;
        }
        return LexState.Normal;
    }
}

/// <summary>Keš stanja leksera po liniji za jedan bafer.</summary>
public sealed class HighlightCache
{
    private int _version = -1;
    private string _ft;
    private readonly List<LexState> _states = new();
    private readonly Dictionary<int, List<Span>> _spans = new();

    public List<Span> Get(Buffer b, int line)
    {
        var lexer = Syntax.For(b.FileType);
        if (lexer == null) return null;
        if (b.Version != _version || b.FileType != _ft)
        {
            _version = b.Version; _ft = b.FileType;
            _states.Clear(); _spans.Clear();
            _states.Add(LexState.Normal);
        }
        if (_spans.TryGetValue(line, out var sp)) return sp;
        while (_states.Count <= line)
        {
            int l = _states.Count - 1;
            var tmp = new List<Span>();
            var next = lexer.Lex(b.Lines[l], _states[l], tmp);
            _spans[l] = tmp;
            _states.Add(next);
        }
        sp = new List<Span>();
        lexer.Lex(b.Lines[line], _states[line], sp);
        _spans[line] = sp;
        return sp;
    }
}
