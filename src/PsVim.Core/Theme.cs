namespace PsVim;

public enum Tok : byte
{
    Default, Comment, String, Variable, Keyword, Command, Function, Parameter, Number, Operator, Type, Member,
    Constant, Tag, Attribute, Heading, Punct, Escape
}

/// <summary>Paleta boja. Podrazumevana je tokyonight (kao u LazyVim-u).</summary>
public sealed class Theme
{
    public string Name;
    public int Bg, BgDark, BgHighlight, BgVisual, BgSearch, BgCurSearch, Fg, FgDark, FgGutter, Comment, Border;
    public int Blue, Cyan, Magenta, Purple, Orange, Yellow, Green, Teal, Red, Blue1, Green1;
    public int LineNr, CursorLineNr, IndentGuide, PopupBg, PopupSel;

    public Style Normal => new(Fg, Bg);
    public Style Tok(Tok t) => t switch
    {
        PsVim.Tok.Comment => new(Comment, -1, Attr.Italic),
        PsVim.Tok.String => new(Green, -1),
        PsVim.Tok.Variable => new(Cyan, -1),
        PsVim.Tok.Keyword => new(Magenta, -1, Attr.Italic),
        PsVim.Tok.Command => new(Blue, -1),
        PsVim.Tok.Function => new(Blue, -1, Attr.Bold),
        PsVim.Tok.Parameter => new(Yellow, -1),
        PsVim.Tok.Number => new(Orange, -1),
        PsVim.Tok.Operator => new(Blue1, -1),
        PsVim.Tok.Type => new(Teal, -1),
        PsVim.Tok.Member => new(Green1, -1),
        PsVim.Tok.Constant => new(Orange, -1),
        PsVim.Tok.Tag => new(Red, -1),
        PsVim.Tok.Attribute => new(Yellow, -1),
        PsVim.Tok.Heading => new(Blue, -1, Attr.Bold),
        PsVim.Tok.Punct => new(Blue1, -1),
        PsVim.Tok.Escape => new(Magenta, -1),
        _ => new(Fg, -1)
    };

    public static readonly string[] Names = { "tokyonight", "catppuccin", "gruvbox", "kanagawa" };

    public static Theme Get(string name) => (name ?? "").ToLowerInvariant() switch
    {
        "catppuccin" => Catppuccin(),
        "gruvbox" => Gruvbox(),
        "kanagawa" => Kanagawa(),
        _ => TokyoNight()
    };

    public static Theme TokyoNight() => new()
    {
        Name = "tokyonight",
        Bg = 0x1a1b26, BgDark = 0x16161e, BgHighlight = 0x292e42, BgVisual = 0x283457, BgSearch = 0x3d59a1, BgCurSearch = 0xff9e64,
        Fg = 0xc0caf5, FgDark = 0xa9b1d6, FgGutter = 0x3b4261, Comment = 0x565f89, Border = 0x27a1b9,
        Blue = 0x7aa2f7, Cyan = 0x7dcfff, Magenta = 0xbb9af7, Purple = 0x9d7cd8, Orange = 0xff9e64, Yellow = 0xe0af68,
        Green = 0x9ece6a, Teal = 0x2ac3de, Red = 0xf7768e, Blue1 = 0x89ddff, Green1 = 0x73daca,
        LineNr = 0x3b4261, CursorLineNr = 0xff9e64, IndentGuide = 0x292e42, PopupBg = 0x16161e, PopupSel = 0x283457
    };

    public static Theme Catppuccin() => new()
    {
        Name = "catppuccin",
        Bg = 0x1e1e2e, BgDark = 0x181825, BgHighlight = 0x2a2b3c, BgVisual = 0x45475a, BgSearch = 0x3e5767, BgCurSearch = 0xfab387,
        Fg = 0xcdd6f4, FgDark = 0xbac2de, FgGutter = 0x45475a, Comment = 0x6c7086, Border = 0x89b4fa,
        Blue = 0x89b4fa, Cyan = 0x89dceb, Magenta = 0xcba6f7, Purple = 0xb4befe, Orange = 0xfab387, Yellow = 0xf9e2af,
        Green = 0xa6e3a1, Teal = 0x94e2d5, Red = 0xf38ba8, Blue1 = 0x94e2d5, Green1 = 0x74c7ec,
        LineNr = 0x45475a, CursorLineNr = 0xb4befe, IndentGuide = 0x313244, PopupBg = 0x181825, PopupSel = 0x313244
    };

    public static Theme Gruvbox() => new()
    {
        Name = "gruvbox",
        Bg = 0x282828, BgDark = 0x1d2021, BgHighlight = 0x3c3836, BgVisual = 0x504945, BgSearch = 0x665c54, BgCurSearch = 0xfe8019,
        Fg = 0xebdbb2, FgDark = 0xd5c4a1, FgGutter = 0x504945, Comment = 0x928374, Border = 0x83a598,
        Blue = 0x83a598, Cyan = 0x8ec07c, Magenta = 0xd3869b, Purple = 0xd3869b, Orange = 0xfe8019, Yellow = 0xfabd2f,
        Green = 0xb8bb26, Teal = 0x8ec07c, Red = 0xfb4934, Blue1 = 0x8ec07c, Green1 = 0x8ec07c,
        LineNr = 0x665c54, CursorLineNr = 0xfabd2f, IndentGuide = 0x3c3836, PopupBg = 0x1d2021, PopupSel = 0x504945
    };

    public static Theme Kanagawa() => new()
    {
        Name = "kanagawa",
        Bg = 0x1f1f28, BgDark = 0x16161d, BgHighlight = 0x2a2a37, BgVisual = 0x223249, BgSearch = 0x2d4f67, BgCurSearch = 0xffa066,
        Fg = 0xdcd7ba, FgDark = 0xc8c093, FgGutter = 0x54546d, Comment = 0x727169, Border = 0x7e9cd8,
        Blue = 0x7e9cd8, Cyan = 0x7fb4ca, Magenta = 0x957fb8, Purple = 0x938aa9, Orange = 0xffa066, Yellow = 0xe6c384,
        Green = 0x98bb6c, Teal = 0x6a9589, Red = 0xe46876, Blue1 = 0x7fb4ca, Green1 = 0x7aa89f,
        LineNr = 0x54546d, CursorLineNr = 0xffa066, IndentGuide = 0x2a2a37, PopupBg = 0x16161d, PopupSel = 0x223249
    };
}
