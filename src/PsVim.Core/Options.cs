using System.Text.Json;
using System.Text.Json.Serialization;

namespace PsVim;

public sealed class KeymapConfig
{
    public string Mode { get; set; } = "n";
    public string Lhs { get; set; }
    public string Rhs { get; set; }
    public string Desc { get; set; }
}

/// <summary>Podešavanja. Učitavaju se iz ~/.config/psvim/config.json (ili $env:PSVIM_CONFIG).</summary>
public sealed class Options
{
    public string Theme { get; set; } = "tokyonight";
    public bool Number { get; set; } = true;
    public bool RelativeNumber { get; set; } = true;
    public bool CursorLine { get; set; } = true;
    public int TabStop { get; set; } = 4;
    public int ShiftWidth { get; set; } = 4;
    public bool ExpandTab { get; set; } = true;
    public bool IgnoreCase { get; set; } = true;
    public bool SmartCase { get; set; } = true;
    public bool HlSearch { get; set; } = true;
    public bool IncSearch { get; set; } = true;
    public int ScrollOff { get; set; } = 4;
    public bool AutoPairs { get; set; } = true;
    public bool IndentGuides { get; set; } = true;
    public bool NerdFont { get; set; } = false;
    public bool Transparent { get; set; } = false;
    /// <summary>"unnamedplus" = yank/paste ide i u sistemski clipboard (kao LazyVim).</summary>
    public string Clipboard { get; set; } = "unnamedplus";
    public string Leader { get; set; } = " ";
    public int TreeWidth { get; set; } = 32;
    public bool ShowHidden { get; set; } = false;
    public int WhichKeyDelay { get; set; } = 300;
    public bool Diagnostics { get; set; } = true;
    public bool VirtualText { get; set; } = true;
    public bool AutoFormatOnSave { get; set; } = false;
    public List<string> Ignore { get; set; } = new() { ".git", "node_modules", "bin", "obj", ".vs", ".idea", "__pycache__", ".venv", "dist", "packages" };
    public List<KeymapConfig> Keymaps { get; set; } = new();

    [JsonIgnore] public string LoadedFrom;

    public static string DefaultPath()
    {
        var env = Environment.GetEnvironmentVariable("PSVIM_CONFIG");
        if (!string.IsNullOrEmpty(env)) return env;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", "psvim", "config.json");
    }

    public static string DataDir()
    {
        var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "psvim");
        try { Directory.CreateDirectory(d); } catch { }
        return d;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Options Load(string path = null)
    {
        path ??= DefaultPath();
        try
        {
            if (File.Exists(path))
            {
                var o = JsonSerializer.Deserialize<Options>(File.ReadAllText(path), JsonOpts) ?? new Options();
                o.LoadedFrom = path;
                return o;
            }
        }
        catch (Exception e)
        {
            var o = new Options { LoadedFrom = "GREŠKA: " + e.Message };
            return o;
        }
        return new Options();
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

    public static void WriteDefault(string path = null)
    {
        path ??= DefaultPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var o = new Options();
        o.Keymaps.Add(new KeymapConfig { Mode = "n", Lhs = "<leader>td", Rhs = ":!Get-Date<CR>", Desc = "Primer: prikaži datum" });
        File.WriteAllText(path, o.ToJson());
    }

    /// <summary>:set opcija</summary>
    public string Set(string expr)
    {
        expr = expr.Trim();
        if (expr.Length == 0) return null;
        bool? val = true;
        string name = expr, value = null;
        int eq = expr.IndexOf('=');
        if (eq > 0) { name = expr.Substring(0, eq); value = expr.Substring(eq + 1); val = null; }
        else if (expr.EndsWith("!")) { name = expr.TrimEnd('!'); val = null; value = "!"; }
        else if (expr.EndsWith("?")) { name = expr.TrimEnd('?'); return name + "=" + Get(name); }
        else if (expr.StartsWith("no")) { name = expr.Substring(2); val = false; }
        string Toggle(bool cur) => value == "!" ? (!cur).ToString() : (val ?? false).ToString();
        switch (name.ToLowerInvariant())
        {
            case "nu": case "number": Number = bool.Parse(Toggle(Number)); break;
            case "rnu": case "relativenumber": RelativeNumber = bool.Parse(Toggle(RelativeNumber)); break;
            case "cul": case "cursorline": CursorLine = bool.Parse(Toggle(CursorLine)); break;
            case "et": case "expandtab": ExpandTab = bool.Parse(Toggle(ExpandTab)); break;
            case "ic": case "ignorecase": IgnoreCase = bool.Parse(Toggle(IgnoreCase)); break;
            case "scs": case "smartcase": SmartCase = bool.Parse(Toggle(SmartCase)); break;
            case "hls": case "hlsearch": HlSearch = bool.Parse(Toggle(HlSearch)); break;
            case "is": case "incsearch": IncSearch = bool.Parse(Toggle(IncSearch)); break;
            case "list": case "ig": case "indentguides": IndentGuides = bool.Parse(Toggle(IndentGuides)); break;
            case "autopairs": AutoPairs = bool.Parse(Toggle(AutoPairs)); break;
            case "nerdfont": NerdFont = bool.Parse(Toggle(NerdFont)); break;
            case "hidden": ShowHidden = bool.Parse(Toggle(ShowHidden)); break;
            case "ts": case "tabstop": TabStop = Math.Clamp(int.Parse(value ?? "4"), 1, 16); break;
            case "sw": case "shiftwidth": ShiftWidth = Math.Clamp(int.Parse(value ?? "4"), 1, 16); break;
            case "so": case "scrolloff": ScrollOff = Math.Clamp(int.Parse(value ?? "4"), 0, 100); break;
            case "theme": case "colorscheme": Theme = value; break;
            case "clipboard": Clipboard = value ?? ""; break;
            default: return "Nepoznata opcija: " + name;
        }
        return null;
    }

    public string Get(string name) => name.ToLowerInvariant() switch
    {
        "nu" or "number" => Number.ToString(),
        "rnu" or "relativenumber" => RelativeNumber.ToString(),
        "ts" or "tabstop" => TabStop.ToString(),
        "sw" or "shiftwidth" => ShiftWidth.ToString(),
        "et" or "expandtab" => ExpandTab.ToString(),
        "theme" or "colorscheme" => Theme,
        _ => "?"
    };
}
