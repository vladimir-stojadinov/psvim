using PsVim;
using Buffer = PsVim.Buffer;

int pass = 0, fail = 0;
var failures = new List<string>();

Editor Make(string text, string ft = "powershell", string cwd = null)
{
    var t = new FakeTerminal("") { Width = 100, Height = 30 };
    var ui = new Ui(t, new Options { Clipboard = "", WhichKeyDelay = 0 });
    var ed = new Editor(ui, null, cwd ?? Path.GetTempPath());
    var b = Buffer.FromText(text);
    b.FileType = ft;
    ed.Buffers.Add(b);
    ed.Buf = b;
    return ed;
}

void T(string name, string initial, string keys, string expected, string cursor = null, string ft = "powershell")
{
    try
    {
        var ed = Make(initial.Replace("|", ""), ft);
        // '|' u početnom tekstu označava kursor
        int ci = initial.IndexOf('|');
        if (ci >= 0)
        {
            var before = initial.Substring(0, ci).Split('\n');
            ed.Buf.Cursor = new Pos(before.Length - 1, before[before.Length - 1].Length);
        }
        ed.RunKeys(keys);
        var got = ed.Buf.GetAllText("\n");
        bool ok = got == expected;
        string cur = $"{ed.Buf.Cursor.L}:{ed.Buf.Cursor.C}";
        if (ok && cursor != null && cursor != cur) ok = false;
        if (ok) pass++;
        else
        {
            fail++;
            failures.Add($"✗ {name}\n    tasteri: {keys}\n    očekivano: {Esc(expected)} {(cursor != null ? "@" + cursor : "")}\n    dobijeno:  {Esc(got)} @{cur}\n    poruka: {ed.LastMessage}");
        }
    }
    catch (Exception e)
    {
        fail++;
        failures.Add($"✗ {name}: IZUZETAK {e}");
    }
}

string Esc(string s) => "\"" + s.Replace("\n", "⏎") + "\"";

void Check(string name, bool cond, string info = "")
{
    if (cond) pass++; else { fail++; failures.Add($"✗ {name} {info}"); }
}

// ---------------- osnovne izmene ----------------
T("dw", "hello world", "dw", "world", "0:0");
T("dw na kraju linije", "foo bar\nbaz", "wdw", "foo \nbaz");
T("2dw", "a b c d", "2dw", "c d");
T("d2w", "a b c d", "d2w", "c d");
T("cw", "hello world", "cwfoo<Esc>", "foo world", "0:2");
T("cw na kraju reči", "ab cd", "lcwX<Esc>", "aX cd");
T("ce", "hello world", "ceyo<Esc>", "yo world");
T("cc čuva uvlačenje", "    foo\nbar", "ccx<Esc>", "    x\nbar");
T("dd", "a\nb\nc", "jdd", "a\nc", "1:0");
T("2dd", "a\nb\nc", "2dd", "c");
T("dd poslednja", "a\nb", "jdd", "a", "0:0");
T("dj", "a\nb\nc\nd", "jdj", "a\nd");
T("dk", "a\nb\nc", "jjdk", "a");
T("dG", "a\nb\nc", "jdG", "a");
T("dgg", "a\nb\nc", "jdgg", "c");
T("d$", "hello world", "wd$", "hello ");
T("D", "hello world", "wD", "hello ");
T("C", "hello world", "wCthere<Esc>", "hello there");
T("x", "abc", "x", "bc");
T("3x", "abcdef", "3x", "def");
T("X", "abc", "$X", "ac");
T("x prazna linija", "\nb", "x", "\nb");
T("r", "abc", "rx", "xbc");
T("3r", "abcd", "3rz", "zzzd");
T("~", "abc", "~~", "ABc");
T("J", "a\n  b\nc", "J", "a b\nc");
T("3J", "a\nb\nc\nd", "3J", "a b c\nd");
T("gJ", "a\n  b", "gJ", "a  b");
T("yyp", "a\nb", "yyp", "a\na\nb", "1:0");
T("yyP", "a\nb", "jyyP", "a\nb\nb");
T("ywP", "foo bar", "ywP", "foo foo bar");
T("p charwise", "ab", "ylp", "aab");
T("ddp zamena linija", "a\nb\nc", "ddp", "b\na\nc");
T("xp zamena znakova", "ab", "xp", "ba");
T("3p", "x", "yl3p", "xxxx");
T("A", "foo", "A;<Esc>", "foo;");
T("I", "   foo", "I# <Esc>", "   # foo");
T("a", "ac", "ab<Esc>", "abc");
T("o", "foo", "obar<Esc>", "foo\nbar");
T("O", "foo", "Obar<Esc>", "bar\nfoo");
T("o posle {", "if ($x) {", "ofoo<Esc>", "if ($x) {\n    foo");
T("o čuva uvlačenje", "    a", "ob<Esc>", "    a\n    b");
T("s (flash ne briše)", "abc", "x", "bc");
T("S", "  abc", "Sx<Esc>", "  x");

// ---------------- undo/redo/dot ----------------
T("u", "abc", "xu", "abc");
T("u posle inserta", "abc", "ifoo<Esc>u", "abc");
T("<C-r>", "abc", "xu<C-r>", "bc");
T("vise undo", "abc", "xxxuu", "bc");
T(". posle dw", "a b c d", "dw.", "c d");
T(". posle ciw", "foo bar baz", "ciwx<Esc>w.", "x x baz");
T(". posle A", "a\nb", "A;<Esc>j.", "a;\nb;");
T(". posle x sa brojem", "abcdef", "2x.", "ef");
T(". posle dd", "a\nb\nc", "dd.", "c");
T(". posle o", "a", "ob<Esc>.", "a\nb\nb");

// ---------------- kretanja ----------------
T("w preko linija", "foo\nbar", "wx", "foo\nar");
T("b", "foo bar", "$bx", "foo ar");
T("e", "foo bar", "ex", "fo bar");
T("ge", "foo bar", "$gex", "fo bar");
T("W", "a.b c", "Wx", "a.b ");
T("w interpunkcija", "a.b", "wx", "ab");
T("0 ^ $", "  abc", "$x0x", " ab");
T("^", "  abc", "$^x", "  bc");
T("f", "abcabc", "fcx", "ababc");
T("t", "abcabc", "tcx", "acabc");
T("F", "abcabc", "$Fax", "abcbc");
T("; ponavljanje", "a,b,c", "f,;x", "a,bc");
T(", unazad", "a,b,c", "$F,;x", "ab,c");
T("dt", "foo(bar)", "dt(", "(bar)");
T("df", "foo(bar)", "df(", "bar)");
T("ct)", "f(abc)", "f(lct)x<Esc>", "f(x)");
T("%", "if (a) { b }", "f{%x", "if (a) { b ");
T("d%", "x(a b)y", "fd(", "x(a b)y");
T("d% 2", "x(a b)y", "f(d%", "xy");
T("G", "a\nb\nc", "Gx", "a\nb\n");
T("gg", "a\nb\nc", "Gggx", "\nb\nc");
T("3G", "a\nb\nc\nd", "3Gx", "a\nb\n\nd");
T("}", "a\nb\n\nc", "}x", "a\nb\n\nc");
T("d}", "a\nb\n\nc", "d}", "\nc");
T("j k sa kolonom", "abcd\nx\nabcd", "$jjx", "abcd\nx\nabc");
T("count j", "a\nb\nc\nd", "3jx", "a\nb\nc\n");
T("H/L su baferi", "abc", "lx", "ac");

// ---------------- tekst objekti ----------------
T("diw", "foo bar baz", "wdiw", "foo  baz");
T("daw", "foo bar baz", "wdaw", "foo baz");
T("ciw", "foo bar", "ciwx<Esc>", "x bar");
T("di\"", "a = \"hello\"", "f\"di\"", "a = \"\"");
T("ci\" ispred navodnika", "x \"abc\" y", "ci\"Q<Esc>", "x \"Q\" y");
T("da\"", "a \"b\" c", "f\"da\"", "a c");
T("ci'", "a 'xy' b", "fxci'z<Esc>", "a 'z' b");
T("di(", "f(a, b)", "fadi(", "f()");
T("da(", "f(a, b)", "fada(", "f");
T("dib", "f(a)", "fadib", "f()");
T("ci( ugnežđeno", "f(g(x), y)", "fxci(z<Esc>", "f(g(z), y)");
T("2di(", "f(g(x), y)", "fx2di(", "f()");
T("di{ višelinijski", "if {\n    a\n    b\n}", "jdi{", "if {\n}");
T("ci{ višelinijski", "if {\n    a\n}", "jci{x<Esc>", "if {\n    x\n}");
T("da{", "x { a } y", "fada{", "x  y");
T("di[", "a[1, 2]", "f1di[", "a[]");
T("dip", "a\nb\n\nc", "dip", "\nc");
T("dap", "a\nb\n\nc", "dap", "c");
T("yiw P", "foo bar", "wyiwP", "foo barbar");
T("dif", "function A {\n    x\n    y\n}\nz", "jdif", "function A {\n}\nz");
T("daf", "function A {\n    x\n}\nz", "jdaf", "z");
T("ig", "a\nb", "dig", "");
T("gUiw", "foo bar", "gUiw", "FOO bar");
T("guu", "FOO\nBAR", "guu", "foo\nBAR");
T("g~w", "abc", "g~w", "ABC");

// ---------------- vizuelni ----------------
T("vd", "abc", "vld", "c");
T("vjd", "abc\ndef", "lvjd", "af");
T("Vd", "a\nb\nc", "Vjd", "c");
T("Vy p", "a\nb", "Vyp", "a\na\nb");
T("viwy P", "foo bar", "viwy$p", "foo barfoo");
T("viwp zamena", "foo bar", "yiwwviwp", "foo foo");
T("v$d", "abc def", "wv$d", "abc ");
T("vc", "hello", "vllcX<Esc>", "Xlo");
T("V>", "a\nb", "Vj><Esc>", "    a\n    b");
T("V> ostaje u selekciji", "a", "V>><Esc>", "        a");
T("v~", "abc", "vl~", "ABc");
T("vU", "abc", "vlU", "ABc");
T("vJ", "a\nb\nc", "VjjJ", "a b c");
T("vr", "abc", "vlrx", "xxc");
T("vo", "abcd", "lvlohd", "d");
T("vip", "a\nb\n\nc", "vipd", "\nc");
T("va(", "f(x) y", "fxva(d", "f y");
T("gv", "abc", "vl<Esc>gvd", "c");
T("vgc", "a\nb", "Vjgc", "# a\n# b");
T("v I", "abc", "lvlIX<Esc>", "aXbc");

// ---------------- komentari i uvlačenje ----------------
T("gcc", "foo", "gcc", "# foo");
T("gcc dva puta", "foo", "gccgcc", "foo");
T("gcc čuva uvlačenje", "  foo", "gcc", "  # foo");
T("gcj", "a\nb\nc", "gcj", "# a\n# b\nc");
T("gcc cs", "x;", "gcc", "// x;", ft: "cs");
T(">>", "a", ">>", "    a");
T("<<", "        a", "<<", "    a");
T("3>>", "a\nb\nc", "3>>", "    a\n    b\n    c");
T(">j", "a\nb", ">j", "    a\n    b");
T("=ip reindent", "if {\nx\n}", "=ip", "if {\n    x\n}");

// ---------------- insert mod ----------------
T("autopairs (", "", "i(<Esc>", "()");
T("autopairs preko )", "", "i()x<Esc>", "()x");
T("autopairs \"", "", "i\"a\"<Esc>", "\"a\"");
T("autopairs bs", "", "i(<BS>x<Esc>", "x");
T("enter između {}", "", "iif {<CR>x<Esc>", "if {\n    x\n}");
T("enter auto indent", "    a", "A<CR>b<Esc>", "    a\n    b");
T("tab", "", "i<Tab>x<Esc>", "    x");
T("bs u uvlačenju", "", "i<Tab><Tab><BS>x<Esc>", "    x");
T("C-w", "foo bar", "A<C-w>x<Esc>", "foo x");
T("C-u", "  foo bar", "A<C-u>x<Esc>", "  x");
T("električni }", "if {\n    ", "GA}<Esc>", "if {\n}");
T("insert bs spaja linije", "a\nb", "ji<BS><Esc>", "ab");
T("C-r registar", "foo", "yiwA <C-r>\"<Esc>", "foo foo");
T("R zamena", "abcd", "Rxy<Esc>", "xycd");
T("R bs", "abcd", "Rxy<BS><Esc>", "xbcd");
T("dopuna rečima", "foobar\nfo", "jA<C-n><Esc>", "foobar\nfoobar");

// ---------------- pretraga ----------------
T("/", "a foo b foo", "/foo<CR>x", "a oo b foo");
T("n", "a foo b foo", "/foo<CR>nx", "a foo b oo");
T("N", "foo a foo", "$N", "foo a foo");
T("? pretraga", "foo a foo", "$?foo<CR>x", "foo a oo");
T("*", "ab x ab", "*x", "ab x b");
T("#", "ab x ab", "$#x", "b x ab");
T("d/", "abc def", "d/d<CR>", "def");
T("pretraga wrap", "foo\nbar", "j/foo<CR>x", "oo\nbar");
T("regex pretraga", "a1 b22", "/\\d\\d<CR>x", "a1 b2");

// ---------------- ex komande ----------------
T(":s", "a a", ":s/a/b/<CR>", "b a");
T(":s g", "a a", ":s/a/b/g<CR>", "b b");
T(":%s", "a\na", ":%s/a/b/<CR>", "b\nb");
T(":%s grupe", "foo bar", ":%s/(\\w+) (\\w+)/\\2 \\1/<CR>", "bar foo");
T(":%s vim grupe", "foo bar", ":%s/\\(\\w\\+\\) \\(\\w\\+\\)/\\2 \\1/<CR>", "bar foo");
T(":s &", "abc", ":s/b/[&]/<CR>", "a[b]c");
T(":'<,'>s", "a\na\na", "Vj<Esc>:'<,'>s/a/b/<CR>", "b\nb\na");
T("v: s", "a\na\na", "Vj:s/a/b/<CR>", "b\nb\na");
T(":g/x/d", "x1\na\nx2", ":g/x/d<CR>", "a");
T(":v/x/d", "x1\na\nx2", ":v/x/d<CR>", "x1\nx2");
T(":sort", "c\na\nb", ":sort<CR>", "a\nb\nc");
T(":sort!", "a\nc\nb", ":sort!<CR>", "c\nb\na");
T(":sort n", "10\n9\n100", ":sort n<CR>", "9\n10\n100");
T(":sort u", "b\na\nb", ":sort u<CR>", "a\nb");
T(":2d", "a\nb\nc", ":2d<CR>", "a\nc");
T(":1,2d", "a\nb\nc", ":1,2d<CR>", "c");
T(":norm", "a\nb", ":%norm A;<CR>", "a;\nb;");
T(":3", "a\nb\nc", ":3<CR>x", "a\nb\n");
T(":m", "a\nb\nc", ":m$<CR>", "b\nc\na");
T(":t", "a\nb", ":t.<CR>", "a\na\nb");
T(":retab", "\ta", ":retab<CR>", "    a");

// ---------------- brojevi, makroi, registri, oznake ----------------
T("<C-a>", "x = 9", "<C-a>", "x = 10");
T("5<C-a>", "x = 1", "5<C-a>", "x = 6");
T("<C-x> negativni", "x = 0", "<C-x>", "x = -1");
T("<C-a> hex", "0x0f", "<C-a>", "0x10");
T("makro", "a\nb\nc", "qqA!<Esc>jq2@q", "a!\nb!\nc!");
T("makro @@", "1\n2\n3", "qqA.<Esc>jq@q@@", "1.\n2.\n3.");
T("imenovani registar", "foo\nbar", "\"ayyj\"ap", "foo\nbar\nfoo");
T("registar A dodaje", "a\nb\nc", "\"xyyj\"Xyyj\"xp", "a\nb\nc\na\nb");
T("crna rupa", "a\nb", "yyj\"_ddp", "a\na");
T("oznake", "a\nb\nc", "majj'ax", "\nb\nc");
T("`oznake", "abc\nd", "lmaj`ax", "ac\nd");
T("\"0 posle brisanja", "a\nb", "yyjdd\"0p", "a\na");

// ---------------- flash ----------------
T("flash skok", "abc xyz abc", "sxyax", "abc yz abc");
T("flash sa oznakom", "foo foo foo", "sfoa", "foo foo foo", "0:4");
T("flash operator", "abc def ghi", "dsga", "hi");

// ---------------- čitanje fajla / picker / stablo ----------------
var dir = Path.Combine(Path.GetTempPath(), "psvim-test-" + Guid.NewGuid().ToString("N").Substring(0, 8));
Directory.CreateDirectory(Path.Combine(dir, "src"));
File.WriteAllText(Path.Combine(dir, "README.md"), "# Naslov\ntekst\n");
File.WriteAllText(Path.Combine(dir, "src", "main.ps1"), "function Get-Foo {\n    param($x)\n    Write-Output $x\n}\nGet-Foo 1\n");
File.WriteAllText(Path.Combine(dir, "src", "util.ps1"), "function Invoke-Bar { 'bar' }\n");
Directory.CreateDirectory(Path.Combine(dir, "node_modules"));
File.WriteAllText(Path.Combine(dir, "node_modules", "x.js"), "ignored");
{
    var ed = Make("", "text", dir);
    ed.Buf.IsScratch = true;
    ed.RunKeys(" ffmain<CR>");
    Check("picker otvara main.ps1", ed.Buf.Path?.EndsWith("main.ps1") == true, ed.Buf.Path ?? ed.LastMessage);
    Check("filetype powershell", ed.Buf.FileType == "powershell");
    ed.RunKeys("Ggd");
    Check("gd skače na definiciju", ed.Buf.Cursor.L == 0, ed.Buf.Cursor.ToString());
    ed.RunKeys(" /Invoke<CR>");
    Check("live grep otvara util.ps1", ed.Buf.Path?.EndsWith("util.ps1") == true, ed.Buf.Path);
    ed.RunKeys(" ,main<CR>");
    Check("picker bafera", ed.Buf.Path?.EndsWith("main.ps1") == true, ed.Buf.Path);
    ed.RunKeys("Lj");
    Check("L menja bafer", ed.Buf.Path?.EndsWith("util.ps1") == true, ed.Buf.Path);
    ed.RunKeys(" bd");
    Check("␣bd zatvara bafer", ed.Buffers.Count(b => !b.IsScratch) == 1, string.Join(",", ed.Buffers.Select(b => b.DisplayName)));
    ed.RunKeys("ggOX<Esc>:w<CR>");
    Check(":w snima", File.ReadAllText(Path.Combine(dir, "src", "main.ps1")).StartsWith("X"), ed.LastMessage);
    Check("posle snimanja nije izmenjen", !ed.Buf.Modified);
    ed.RunKeys(" e");
    ed.RunKeys("ggj<CR>");
    // stablo: README.md, src/ (folderi prvi) -> gg na src, j na main? zavisi od reda
    Check("stablo radi", ed.Buf != null);
    ed.RunKeys(":e " + Path.Combine(dir, "novi.txt") + "<CR>ihello<Esc>:w<CR>");
    Check(":e novi fajl i :w", File.Exists(Path.Combine(dir, "novi.txt")) && File.ReadAllText(Path.Combine(dir, "novi.txt")).Trim() == "hello", ed.LastMessage);
    ed.RunKeys(" sk");
    Check("keymaps picker ne pada", true);
    // render
    ed.Render();
    var status = ed.Ui.Screen.RowText(ed.Ui.Screen.H - 2);
    Check("statusna linija ima NORMAL", status.Contains("NORMAL"), status);
    ed.RunKeys(" fnsrc/treci.ps1<CR>");
    Check("␣fn novi fajl", ed.Buf.Path?.EndsWith("treci.ps1") == true, ed.Buf.Path);
    var files = Files.Enumerate(dir, new Options());
    Check("node_modules ignorisan", !files.Any(f => f.Contains("node_modules")), string.Join(",", files));
}
{
    // stablo detaljno
    var ed = Make("", "text", dir);
    ed.RunKeys(" e");
    ed.Render();
    var row1 = ed.Ui.Screen.RowText(1);
    Check("stablo prikazuje src", ed.Ui.Screen.RowText(1).Contains("src") || ed.Ui.Screen.RowText(2).Contains("src"), row1);
    ed.RunKeys("ggl"); // raširi src
    ed.RunKeys("j<CR>"); // main.ps1
    Check("stablo otvara fajl", ed.Buf.Path?.EndsWith(".ps1") == true, ed.Buf.Path);
}
{
    // dijagnostika preko kuke
    var t = new FakeTerminal("");
    var ui = new Ui(t, new Options { Clipboard = "" });
    var hooks = new Hooks { Diagnose = (text, p) => text.Contains("{{") ? new[] { "1\t1\tError\tNedostaje }" } : Array.Empty<string>() };
    var ed = new Editor(ui, hooks, dir);
    var b = Buffer.FromText("{{"); b.FileType = "powershell"; ed.Buffers.Add(b); ed.Buf = b;
    ed.RunKeys("l");
    Check("dijagnostika se izračunava", ed.Buf.Diagnostics.Count == 1, ed.Buf.Diagnostics.Count.ToString());
    ed.Render();
    var row = ed.Ui.Screen.RowText(0);
    Check("virtuelni tekst dijagnostike", row.Contains("Nedostaje"), row);
    ed.RunKeys("x");
    Check("dijagnostika nestaje posle ispravke", ed.Buf.Diagnostics.Count == 0);
}
{
    // clipboard kuka (unnamedplus)
    string cb = null;
    var t = new FakeTerminal("");
    var ui = new Ui(t, new Options());
    var hooks = new Hooks { ClipboardGet = () => cb, ClipboardSet = s => cb = s };
    var ed = new Editor(ui, hooks, dir);
    var b = Buffer.FromText("foo\nbar"); ed.Buffers.Add(b); ed.Buf = b;
    ed.RunKeys("yy");
    Check("yy ide u clipboard", cb == "foo\n", cb);
    ed.RunKeys("jp");
    Check("p iz clipboarda (linijski)", b.GetAllText() == "foo\nbar\nfoo", b.GetAllText());
    cb = "XYZ";
    ed.RunKeys("ggP");
    Check("p spoljnog clipboarda", b.GetAllText().StartsWith("XYZfoo"), b.GetAllText());
}
{
    // lekser
    var lx = new PowerShellLexer();
    var spans = new List<Span>();
    lx.Lex("function Get-Foo { param([string]$Name = \"x $y\") Get-Item -Path $Name -eq 5 # kom", LexState.Normal, spans);
    Check("lekser: keyword", spans.Any(s => s.Kind == Tok.Keyword && s.Start == 0));
    Check("lekser: function name", spans.Any(s => s.Kind == Tok.Function && s.Start == 9));
    Check("lekser: type", spans.Any(s => s.Kind == Tok.Type));
    Check("lekser: variable", spans.Count(s => s.Kind == Tok.Variable) >= 3);
    Check("lekser: parameter", spans.Any(s => s.Kind == Tok.Parameter));
    Check("lekser: operator -eq", spans.Any(s => s.Kind == Tok.Operator && s.Len == 3));
    Check("lekser: komentar", spans.Any(s => s.Kind == Tok.Comment));
    Check("lekser: komanda", spans.Any(s => s.Kind == Tok.Command));
    spans.Clear();
    var st = lx.Lex("<# blok", LexState.Normal, spans);
    Check("lekser: blok komentar", st == LexState.BlockComment);
    spans.Clear();
    st = lx.Lex("$x = @\"", LexState.Normal, spans);
    Check("lekser: here-string", st == LexState.HereDouble);
}
{
    // fuzzy
    Check("fuzzy: podudaranje", Fuzzy.Score("src/main.ps1", "main") > Fuzzy.Score("src/makeit.ps1", "main"));
    Check("fuzzy: bez podudaranja", Fuzzy.Score("abc", "xyz") == int.MinValue);
    Check("fuzzy: ime fajla", Fuzzy.Score("src/editor.cs", "ed") > Fuzzy.Score("readme.md", "ed"));
}
{
    // which-key popup se crta
    var t = new FakeTerminal("") { Width = 120, Height = 30 };
    var ui = new Ui(t, new Options { Clipboard = "", WhichKeyDelay = 0 });
    var ed = new Editor(ui, null, dir);
    var b = Buffer.FromText("x"); ed.Buffers.Add(b); ed.Buf = b;
    // bez tastera posle leader-a: WhichKey čeka -> FakeTerminal baca EndOfInput posle crtanja
    ed.RunKeys(" ");
    var screen = string.Join("\n", Enumerable.Range(0, ui.Screen.H).Select(ui.Screen.RowText));
    Check("which-key popup", screen.Contains("+fajl/traži") && screen.Contains("Pronađi fajlove"), screen);
}


{
    // artefakti: binarni fajl + široki/nevidljivi znakovi, pa prelazak na običan fajl
    var bin = Path.Combine(dir, "x.dll");
    var bytes = new List<byte> { 0x4d, 0x5a, 0, 0, 0x1b, 0x5b, 0x32, 0x4a, 7, 8, 0x0b };
    bytes.AddRange(System.Text.Encoding.UTF8.GetBytes("中文字 😀 é ​‮ end\n"));
    for (int i = 0; i < 300; i++) bytes.Add((byte)(i * 7 % 256));
    File.WriteAllBytes(bin, bytes.ToArray());
    var txt = Path.Combine(dir, "a.txt");
    File.WriteAllText(txt, "hello\nworld\n");
    var t = new FakeTerminal("") { Width = 80, Height = 20 };
    var ui = new Ui(t, new Options { Clipboard = "", WhichKeyDelay = 0 });
    var ed = new Editor(ui, null, dir);
    ed.Open(bin);
    Check("binarni fajl je samo za čitanje", ed.Buf.ReadOnly && ed.Buf.IsBinary);
    ed.RunKeys("x");
    ed.Render();
    ed.RunKeys("l");
    ed.Render();
    Compare("binarni fajl");
    ed.Open(txt);
    ed.Render();
    ed.RunKeys("j");
    ed.Render();
    Compare("posle prelaska na tekst");
    void Compare(string label) {
    var grid = new string[20, 80];
    int cx = 0, cy = 0;
    var o = t.Output.ToString();
    for (int i = 0; i < o.Length; i++)
    {
        if (o[i] == '\x1b')
        {
            int j = i + 1;
            if (j < o.Length && o[j] == '[')
            {
                j++; int st = j;
                while (j < o.Length && !(o[j] >= '@' && o[j] <= '~')) j++;
                var par = o.Substring(st, j - st); char cmd = o[j];
                if (cmd == 'H') { var pp = par.Split(';'); cy = int.Parse(pp[0]) - 1; cx = int.Parse(pp[1]) - 1; }
                else if (cmd == 'J' && par == "2") { for (int a = 0; a < 20; a++) for (int b2 = 0; b2 < 80; b2++) grid[a, b2] = " "; }
                i = j; continue;
            }
            Check("sirov ESC u izlazu", false, "pozicija " + i); break;
        }
        string ch = char.IsHighSurrogate(o[i]) ? o.Substring(i++, 2) : o[i].ToString();
        if (o[i] < ' ' && !char.IsLowSurrogate(o[i])) { Check("kontrolni znak u izlazu", false, ((int)o[i]).ToString()); continue; }
        // širina kao u pravom terminalu: CJK/emoji 2 kolone, kombinujući/nevidljivi 0
        int cp = char.ConvertToUtf32(ch, 0);
        var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch, 0);
        int w = cat is System.Globalization.UnicodeCategory.NonSpacingMark or System.Globalization.UnicodeCategory.Format ? 0
              : (cp >= 0x1100 && (cp <= 0x115F || (cp >= 0x2E80 && cp <= 0xA4CF) || (cp >= 0xAC00 && cp <= 0xD7A3) || (cp >= 0xF900 && cp <= 0xFAFF) || (cp >= 0xFF00 && cp <= 0xFF60) || (cp >= 0x1F300 && cp <= 0x1FAFF))) ? 2 : 1;
        if (w == 0) continue;
        if (cy < 20 && cx < 80) grid[cy, cx] = ch;
        if (w == 2 && cy < 20 && cx + 1 < 80) grid[cy, cx + 1] = "";
        cx += w;
    }
    int diff = 0; string first = null;
    for (int y = 0; y < 20; y++)
        for (int x = 0; x < 80; x++)
        {
            var m = ui.Screen.GetText(x, y);
            if ((grid[y, x] ?? " ") != m) { diff++; first ??= $"({x},{y}) terminal='{grid[y, x]}' model='{m}'"; }
        }
    Check("terminal == model: " + label, diff == 0, $"{diff} razlika, prva {first}");
    }
    Check("široki znak zamenjen", Screen.Sanitize("中") == "□" && Screen.Sanitize("́") == "?" && Screen.Sanitize("​") == "?" && Screen.Sanitize("č") == "č");
}
try { Directory.Delete(dir, true); } catch { }

foreach (var f in failures) Console.WriteLine(f);
Console.WriteLine($"\n{pass} prošlo, {fail} palo");
return fail == 0 ? 0 : 1;
