#requires -Version 5.1
<#
    psvim — vim/LazyVim za PowerShell.
    Editor je u PsVim.Core.dll (C#); ovaj modul ga povezuje sa PowerShell-om:
      * psvim / v           — editor (alias vim/vi samo ako pravi vim nije instaliran)
      * Enable-PsVimPrompt  — vim mod u samom promptu (PSReadLine) sa LazyVim prečicama
#>

# PowerShell 7 (Core, .NET 8) koristi bin/PsVim.Core.dll; Windows PowerShell 5.1 (Desktop, .NET Framework) bin/net472/PsVim.Core.dll
$script:Bin = Join-Path $PSScriptRoot 'bin'
if ($PSVersionTable.PSEdition -eq 'Desktop') { $script:Bin = Join-Path $script:Bin 'net472' }
$script:Dll = Join-Path $script:Bin 'PsVim.Core.dll'
$script:Esc = [char]27
if (-not ('PsVim.Editor' -as [type])) {
    if (-not (Test-Path $script:Dll)) {
        throw "PsVim.Core.dll nije pronađen ($script:Dll). Pokreni setup.ps1 ili build.ps1 iz korena projekta."
    }
    Add-Type -Path $script:Dll
}

# --------------------------------------------------------------------------------------------
#  Kuke (hooks) koje editor poziva
# --------------------------------------------------------------------------------------------
$script:KnownCommands = $null
$script:HasAnalyzer = $null

function script:Test-Analyzer {
    if ($null -eq $script:HasAnalyzer) {
        $script:HasAnalyzer = [bool](Get-Module -ListAvailable -Name PSScriptAnalyzer -ErrorAction SilentlyContinue)
    }
    $script:HasAnalyzer
}

function script:Get-KnownCommands {
    if ($null -eq $script:KnownCommands) {
        $set = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($c in (Get-Command -CommandType Alias, Function, Cmdlet, Filter, ExternalScript, Application -ErrorAction SilentlyContinue)) {
            [void]$set.Add($c.Name)
            if ($c.CommandType -eq 'Application') { [void]$set.Add([IO.Path]::GetFileNameWithoutExtension($c.Name)) }
        }
        $script:KnownCommands = $set
    }
    $script:KnownCommands
}

function New-PsVimHooks {
    <#
    .SYNOPSIS
        Pravi objekat sa kukama koje editor koristi (clipboard, shell, dijagnostika, pomoć, dopuna).
    #>
    [CmdletBinding()]
    param()

    $h = [PsVim.Hooks]::new()

    $h.ClipboardGet = [Func[string]] {
        try { $t = Get-Clipboard -Raw -ErrorAction Stop; if ($null -eq $t) { '' } else { [string]$t } } catch { $null }
    }
    $h.ClipboardSet = [Action[string]] {
        param($text)
        try { Set-Clipboard -Value $text -ErrorAction Stop } catch { }
    }

    $h.Shell = [Func[string, string, string]] {
        param($cmd, $inputText)
        try {
            if (-not [string]::IsNullOrEmpty($inputText)) {
                $sb = [scriptblock]::Create("`$input | $cmd")
                $out = ($inputText -split "`n") | & $sb *>&1
            }
            else {
                $sb = [scriptblock]::Create($cmd)
                $out = & $sb *>&1
            }
            ($out | Out-String -Width 220)
        }
        catch { "Greška: $($_.Exception.Message)" }
    }

    # Brza dijagnostika: PowerShell parser + nepoznate komande
    $h.Diagnose = [Func[string, string, string[]]] {
        param($text, $path)
        $tokens = $null; $errors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseInput($text, [ref]$tokens, [ref]$errors)
        $res = [System.Collections.Generic.List[string]]::new()
        foreach ($e in $errors) {
            $res.Add(('{0}{4}{1}{4}Error{4}{2}' -f $e.Extent.StartLineNumber, $e.Extent.StartColumnNumber, $e.Message, $null, "`t"))
        }
        if ($errors.Count -eq 0 -and $global:PsVimOptions.CheckCommands) {
            $known = Get-KnownCommands
            $defined = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach ($f in $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true)) { [void]$defined.Add($f.Name) }
            foreach ($c in $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true)) {
                $first = $c.CommandElements[0]
                if ($first -isnot [System.Management.Automation.Language.StringConstantExpressionAst]) { continue }
                if ($first.StringConstantType -ne 'BareWord') { continue }
                $name = $first.Value
                if ($name -match '[\\/.:]' -or $defined.Contains($name) -or $known.Contains($name)) { continue }
                $res.Add(('{0}{3}{1}{3}Warning{3}Komanda ''{2}'' nije pronađena' -f $first.Extent.StartLineNumber, $first.Extent.StartColumnNumber, $name, "`t"))
            }
        }
        [string[]]$res
    }

    $h.Help = [Func[string, string]] {
        param($word)
        $txt = $null
        try { $txt = Get-Help -Name $word -ErrorAction Stop | Out-String -Width 110 } catch { }
        if ([string]::IsNullOrWhiteSpace($txt)) {
            $cmd = Get-Command -Name $word -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($cmd) { $txt = ($cmd | Format-List Name, CommandType, Source, Version, Definition | Out-String -Width 110) }
        }
        $txt
    }

    $h.Complete = [Func[string, int, string[]]] {
        param($text, $offset)
        try {
            $r = TabExpansion2 -inputScript $text -cursorColumn $offset
            [string[]]@($r.CompletionMatches | Select-Object -First 200 | ForEach-Object {
                    '{0}{5}{1}{5}{2}{5}{3}{5}{4}' -f $r.ReplacementIndex, $r.ReplacementLength, $_.CompletionText, $_.ListItemText, $_.ResultType, "`t"
                })
        }
        catch { [string[]]@() }
    }

    if (Test-Analyzer) {
        $h.Lint = [Func[string, string, string[]]] {
            param($text, $path)
            try {
                [string[]]@(Invoke-ScriptAnalyzer -ScriptDefinition $text -ErrorAction Stop | ForEach-Object {
                        $sev = switch ($_.Severity.ToString()) { 'ParseError' { 'Error' } 'Error' { 'Error' } 'Warning' { 'Warning' } default { 'Information' } }
                        '{0}{4}{1}{4}{2}{4}{3}' -f $_.Line, $_.Column, $sev, ("$($_.RuleName): $($_.Message)" -replace "`r?`n", ' '), "`t"
                    })
            }
            catch { [string[]]@() }
        }
        $h.Format = [Func[string, string, string]] {
            param($text, $path)
            try { Invoke-Formatter -ScriptDefinition $text -ErrorAction Stop } catch { $null }
        }
    }
    $h
}

if (-not $global:PsVimOptions) {
    $global:PsVimOptions = [pscustomobject]@{
        CheckCommands = $true   # upozorenje za nepoznate komande u .ps1 fajlovima
    }
}

# --------------------------------------------------------------------------------------------
#  Editor
# --------------------------------------------------------------------------------------------
function Invoke-PsVim {
    <#
    .SYNOPSIS
        Otvara psvim editor.
    .EXAMPLE
        psvim skripta.ps1
    .EXAMPLE
        psvim +42 skripta.ps1      # otvori na liniji 42
    .EXAMPLE
        psvim .                    # stablo fajlova trenutnog foldera
    .EXAMPLE
        Get-Process | psvim        # otvori izlaz komande u novom baferu
    #>
    [CmdletBinding()]
    param(
        [Parameter(Position = 0, ValueFromRemainingArguments)]
        [string[]]$Path,
        [Parameter(ValueFromPipeline)]
        [object]$InputObject
    )
    begin { $piped = [System.Collections.Generic.List[object]]::new() }
    process { if ($null -ne $InputObject) { $piped.Add($InputObject) } }
    end {
        $files = @($Path | Where-Object { $_ })
        $tmp = $null
        if ($piped.Count -gt 0) {
            $tmp = Join-Path ([IO.Path]::GetTempPath()) ("psvim-{0}.txt" -f ([guid]::NewGuid().ToString('N').Substring(0, 6)))
            $piped | Out-String -Width 250 | Set-Content -LiteralPath $tmp -Encoding utf8
            $files += $tmp
        }
        $cwd = (Get-Location -PSProvider FileSystem).ProviderPath
        $hooks = New-PsVimHooks
        try {
            [void][PsVim.Editor]::Start([string[]]$files, $cwd, $hooks, $null)
        }
        finally {
            if ($tmp) { Remove-Item -LiteralPath $tmp -ErrorAction SilentlyContinue }
        }
    }
}

Set-Alias -Name psvim -Value Invoke-PsVim
Set-Alias -Name v -Value Invoke-PsVim
foreach ($name in 'vim', 'vi') {
    if (-not (Get-Command $name -CommandType Application -ErrorAction SilentlyContinue)) {
        Set-Alias -Name $name -Value Invoke-PsVim
    }
}

function Edit-PsVimConfig {
    <# .SYNOPSIS Otvara konfiguraciju (~/.config/psvim/config.json). #>
    $p = [PsVim.Options]::DefaultPath()
    if (-not (Test-Path $p)) { [PsVim.Options]::WriteDefault($p) }
    Invoke-PsVim $p
}

# --------------------------------------------------------------------------------------------
#  Prompt: vim mod + LazyVim prečice u PSReadLine-u
# --------------------------------------------------------------------------------------------
$script:PromptLeader = @(
    "f`t+fajl/traži"
    "ff`tOtvori fajl u psvim-u"
    "fi`tUbaci putanju fajla"
    "fr`tNedavni fajlovi"
    "fd`tPromeni direktorijum (cd)"
    "fc`tKonfiguracija psvim-a"
    " `tOtvori fajl u psvim-u"
    "/`tGrep i otvori"
    ":`tIstorija komandi"
    "e`tStablo fajlova (psvim .)"
    "v`tUredi komandu u psvim-u"
    "s`t+pretraga"
    "sc`tIstorija komandi"
    "sh`tGet-Help za reč"
    "sk`tPrečice prompta"
    "g`t+git"
    "gg`tLazygit"
    "gs`tGit status"
    "u`t+ui"
    "ui`tUključi/isključi predloge (inline)"
)

function script:Get-PromptState {
    $line = $null; $cursor = $null
    [Microsoft.PowerShell.PSConsoleReadLine]::GetBufferState([ref]$line, [ref]$cursor)
    [pscustomobject]@{ Line = $line; Cursor = $cursor }
}

function script:Set-PromptLine([string]$text, [switch]$Accept) {
    $s = Get-PromptState
    [Microsoft.PowerShell.PSConsoleReadLine]::Replace(0, $s.Line.Length, $text)
    if ($Accept) { [Microsoft.PowerShell.PSConsoleReadLine]::AcceptLine() }
}

function script:Format-PathArg([string]$p) {
    $cwd = (Get-Location -PSProvider FileSystem).ProviderPath
    $rel = [PsVim.Compat]::RelativePath($cwd, $p)
    if (-not $rel.StartsWith('..')) { $p = ".$([IO.Path]::DirectorySeparatorChar)$rel" }
    if ($p -match "[\s'`$`(`)\{\}&;,]") { "'" + ($p -replace "'", "''") + "'" } else { $p }
}

function script:Get-HistoryLines {
    $items = [Microsoft.PowerShell.PSConsoleReadLine]::GetHistoryItems() | ForEach-Object CommandLine
    [array]::Reverse($items)
    [string[]]($items | Select-Object -Unique)
}

function script:Invoke-PromptAction([string]$key) {
    $cwd = (Get-Location -PSProvider FileSystem).ProviderPath
    switch -CaseSensitive ($key) {
        { $_ -in 'ff', ' ' } {
            $f = [PsVim.PromptUi]::PickFile($cwd, 'Fajlovi')
            if ($f) { Set-PromptLine "psvim $(Format-PathArg $f)" -Accept }
        }
        'fi' {
            $f = [PsVim.PromptUi]::PickFile($cwd, 'Ubaci putanju')
            if ($f) { [Microsoft.PowerShell.PSConsoleReadLine]::Insert((Format-PathArg $f)) }
        }
        'fr' {
            $f = [PsVim.PromptUi]::PickRecent()
            if ($f) { Set-PromptLine "psvim $(Format-PathArg $f)" -Accept }
        }
        'fd' {
            $d = [PsVim.PromptUi]::PickDirectory($cwd)
            if ($d) { Set-PromptLine "Set-Location $(Format-PathArg $d)" -Accept }
        }
        'fc' { Set-PromptLine 'Edit-PsVimConfig' -Accept }
        '/' {
            $r = [PsVim.PromptUi]::PickGrep($cwd)
            if ($r) { $p, $l = $r -split "`t"; Set-PromptLine "psvim +$l $(Format-PathArg $p)" -Accept }
        }
        { $_ -in ':', 'sc' } {
            $s = Get-PromptState
            $c = [PsVim.PromptUi]::PickFromList((Get-HistoryLines), 'Istorija komandi', $s.Line)
            if ($c) { Set-PromptLine $c }
        }
        'e' { Set-PromptLine 'psvim .' -Accept }
        'v' { Edit-PsVimPromptLine }
        'sh' { Show-PsVimHelpForWord }
        'sk' { [PsVim.PromptUi]::ShowText('Prečice prompta', (Get-PsVimPromptKeys)) }
        'gg' {
            if (Get-Command lazygit -ErrorAction SilentlyContinue) { Set-PromptLine 'lazygit' -Accept }
            else { [PsVim.PromptUi]::ShowText('lazygit', "lazygit nije instaliran.`n`nwinget install JesseDuffield.lazygit") }
        }
        'gs' {
            $files = git status --porcelain 2>$null | ForEach-Object { $_.Substring(3).Trim('"') }
            if ($files) {
                $f = [PsVim.PromptUi]::PickFromList([string[]]$files, 'Git status', '')
                if ($f) { [Microsoft.PowerShell.PSConsoleReadLine]::Insert((Format-PathArg (Join-Path $cwd $f))) }
            }
        }
        'ui' {
            $o = Get-PSReadLineOption
            try {
                if ($o.PredictionSource -eq 'None') { Set-PSReadLineOption -PredictionSource History } else { Set-PSReadLineOption -PredictionSource None }
            }
            catch { [PsVim.PromptUi]::ShowText('Predlozi', "Inline predlozi traže PSReadLine 2.1+.`n`nInstall-Module PSReadLine -Scope CurrentUser -Force") }
        }
    }
}

function Edit-PsVimPromptLine {
    <# .SYNOPSIS Otvara trenutnu komandu u psvim-u (kao 'v' u vi modu bash-a). #>
    $s = Get-PromptState
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("psvim-cmd-{0}.ps1" -f ([guid]::NewGuid().ToString('N').Substring(0, 6)))
    Set-Content -LiteralPath $tmp -Value $s.Line -Encoding utf8 -NoNewline
    try {
        [void][PsVim.Editor]::Start([string[]]@($tmp), (Get-Location -PSProvider FileSystem).ProviderPath, (New-PsVimHooks), $null)
        $new = (Get-Content -LiteralPath $tmp -Raw) -replace "(\r?\n)+$", ''
        Set-PromptLine $new
    }
    finally { Remove-Item -LiteralPath $tmp -ErrorAction SilentlyContinue }
    [Microsoft.PowerShell.PSConsoleReadLine]::InvokePrompt()
}

function Show-PsVimHelpForWord {
    $s = Get-PromptState
    $line = $s.Line; $c = [Math]::Min($s.Cursor, [Math]::Max(0, $line.Length - 1))
    if ($line.Length -eq 0) { return }
    $st = $c; while ($st -gt 0 -and $line[$st - 1] -match '[\w-]') { $st-- }
    $en = $c; while ($en -lt $line.Length -and $line[$en] -match '[\w-]') { $en++ }
    $word = $line.Substring($st, $en - $st)
    if (-not $word) { return }
    $h = New-PsVimHooks
    $txt = $h.Help.Invoke($word)
    if (-not $txt) { $txt = "Nema pomoći za '$word'." }
    [PsVim.PromptUi]::ShowText("Get-Help $word", $txt)
}

function Get-PsVimPromptKeys {
    @"
psvim u promptu (PSReadLine vi mod)

Normal mod (Esc):
  ␣           leader meni (which-key) — sačekaj ili kucaj dalje
  ␣␣ / ␣ff    pronađi fajl i otvori u psvim-u
  ␣fi         ubaci putanju fajla u komandu
  ␣fr         nedavni fajlovi
  ␣fd         promeni direktorijum (fuzzy)
  ␣/          grep kroz projekat i otvori na liniji
  ␣: / ␣sc    istorija komandi (fuzzy)
  ␣e          stablo fajlova
  ␣v / v      uredi komandu u psvim-u
  ␣sh / K     Get-Help za reč pod kursorom
  ␣gg         lazygit      ␣gs  git status
  ␣ui         uključi/isključi inline predloge
  standardni vi tasteri: h j k l w b e 0 $ x dd cw ciw ... u . i a A I

Insert mod:
  Tab / Ctrl+Space   meni za dopunu
  Ctrl+t             ubaci putanju fajla (fzf stil)
  Ctrl+r             istorija komandi (fzf stil)
  Ctrl+y / →         prihvati predlog
  ↑ / ↓              pretraga istorije po početku linije
"@
}

function Enable-PsVimPrompt {
    <#
    .SYNOPSIS
        Uključuje vim mod u PowerShell promptu sa LazyVim prečicama (leader = razmak u normal modu).
    .DESCRIPTION
        Dodaj u $PROFILE:
            Import-Module PsVim
            Enable-PsVimPrompt
    #>
    [CmdletBinding()]
    param([switch]$NoPredictions)

    if (-not (Get-Module PSReadLine)) { Import-Module PSReadLine -ErrorAction Stop }

    Set-PSReadLineOption -EditMode Vi
    # oblik kursora: blok u normal modu, crta u insert modu (kao u LazyVim-u)
    try {
        Set-PSReadLineOption -ViModeIndicator Script -ViModeChangeHandler {
            param($mode)
            if ($mode -eq 'Command') { [Console]::Write("$([char]27)[2 q") } else { [Console]::Write("$([char]27)[6 q") }
        }
    }
    catch { try { Set-PSReadLineOption -ViModeIndicator Cursor } catch { } }
    if (-not $NoPredictions) {
        # HistoryAndPlugin traži PowerShell 7.2+; u 5.1 sa PSReadLine 2.1+ radi History; PSReadLine 2.0 nema predloge
        try { Set-PSReadLineOption -PredictionSource HistoryAndPlugin -PredictionViewStyle InlineView }
        catch { try { Set-PSReadLineOption -PredictionSource History } catch { } }
    }
    # boje kao ANSI sekvence (radi i u PSReadLine 2.0 iz Windows PowerShell-a 5.1)
    $c = @{}
    $palette = @{
        Command = '7aa2f7'; Parameter = 'e0af68'; Variable = '7dcfff'; String = '9ece6a'; Number = 'ff9e64'
        Operator = '89ddff'; Keyword = 'bb9af7'; Type = '2ac3de'; Member = '73daca'; Comment = '565f89'
    }
    foreach ($k in $palette.Keys) {
        $hex = $palette[$k]
        $c[$k] = "$script:Esc[38;2;{0};{1};{2}m" -f [Convert]::ToInt32($hex.Substring(0, 2), 16), [Convert]::ToInt32($hex.Substring(2, 2), 16), [Convert]::ToInt32($hex.Substring(4, 2), 16)
    }
    try { Set-PSReadLineOption -Colors $c } catch { }
    try { Set-PSReadLineOption -Colors @{ InlinePrediction = "$script:Esc[38;2;86;95;137m" } } catch { }

    # ---- normal (command) mod ----
    Set-PSReadLineKeyHandler -ViMode Command -Chord 'Spacebar' -BriefDescription 'psvim leader' -ScriptBlock {
        $s = Get-PromptState
        $key = [PsVim.PromptUi]::WhichKey([string[]]$script:PromptLeader, $s.Line)
        [Microsoft.PowerShell.PSConsoleReadLine]::InvokePrompt()
        if ($key) { Invoke-PromptAction $key }
    }
    Set-PSReadLineKeyHandler -ViMode Command -Chord 'v' -BriefDescription 'Uredi u psvim-u' -ScriptBlock { Edit-PsVimPromptLine }
    Set-PSReadLineKeyHandler -ViMode Command -Chord 'K' -BriefDescription 'Get-Help' -ScriptBlock {
        Show-PsVimHelpForWord
        [Microsoft.PowerShell.PSConsoleReadLine]::InvokePrompt()
    }

    # ---- insert mod ----
    Set-PSReadLineKeyHandler -ViMode Insert -Chord 'Tab' -Function MenuComplete
    Set-PSReadLineKeyHandler -ViMode Insert -Chord 'Ctrl+Spacebar' -Function MenuComplete
    Set-PSReadLineKeyHandler -ViMode Insert -Chord 'UpArrow' -Function HistorySearchBackward
    Set-PSReadLineKeyHandler -ViMode Insert -Chord 'DownArrow' -Function HistorySearchForward
    try { Set-PSReadLineKeyHandler -ViMode Insert -Chord 'Ctrl+y' -Function AcceptSuggestion } catch { }  # PSReadLine 2.1+
    Set-PSReadLineKeyHandler -ViMode Insert -Chord 'Ctrl+t' -BriefDescription 'Ubaci putanju fajla' -ScriptBlock {
        $f = [PsVim.PromptUi]::PickFile((Get-Location -PSProvider FileSystem).ProviderPath, 'Ubaci putanju')
        [Microsoft.PowerShell.PSConsoleReadLine]::InvokePrompt()
        if ($f) { [Microsoft.PowerShell.PSConsoleReadLine]::Insert((Format-PathArg $f)) }
    }
    Set-PSReadLineKeyHandler -ViMode Insert -Chord 'Ctrl+r' -BriefDescription 'Istorija komandi' -ScriptBlock {
        $s = Get-PromptState
        $c = [PsVim.PromptUi]::PickFromList((Get-HistoryLines), 'Istorija komandi', $s.Line)
        [Microsoft.PowerShell.PSConsoleReadLine]::InvokePrompt()
        if ($c) { Set-PromptLine $c }
    }
    Write-Verbose 'psvim prompt uključen. Esc za normal mod, razmak za leader meni.'
}

function Disable-PsVimPrompt {
    <# .SYNOPSIS Vraća Windows/Emacs način uređivanja u promptu. #>
    Set-PSReadLineOption -EditMode Windows
    [Console]::Write("$([char]27)[0 q")
}

Export-ModuleMember -Function Invoke-PsVim, Edit-PsVimConfig, New-PsVimHooks, Enable-PsVimPrompt, Disable-PsVimPrompt, Edit-PsVimPromptLine, Show-PsVimHelpForWord, Get-PsVimPromptKeys `
    -Alias psvim, v, vim, vi
