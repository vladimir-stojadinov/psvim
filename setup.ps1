<#
.SYNOPSIS
    Instalacija i početno podešavanje psvim-a (vim/LazyVim za PowerShell).

.DESCRIPTION
    Radi sve korake:
      1. proverava verziju: radi i u Windows PowerShell 5.1 i u PowerShell 7.4+ (instalira za onaj iz kog je pokrenut,
         a ako na računaru postoji i drugi, nudi da instalira i za njega)
      2. preuzima psvim sa GitHub-a ako skripta nije pokrenuta iz kloniranog repozitorijuma
      3. (opciono) builduje iz izvornog koda ako je instaliran .NET 8 SDK, inače koristi gotov DLL
      4. instalira modul za trenutnog korisnika
      5. dodaje psvim u $PROFILE (Import-Module + vim mod u promptu)
      6. pravi konfiguraciju (~/.config/psvim/config.json) prema tvojim odgovorima
      7. (opciono) instalira PSScriptAnalyzer i lazygit
      8. proverava da sve radi

    Skripta je bezbedna za ponovno pokretanje: menja samo svoj blok u $PROFILE i pravi rezervnu kopiju.

.EXAMPLE
    # bez kloniranja:
    irm https://raw.githubusercontent.com/vladimir-stojadinov/psvim/main/setup.ps1 | iex
    # sa opcijama:
    & ([scriptblock]::Create((irm https://raw.githubusercontent.com/vladimir-stojadinov/psvim/main/setup.ps1))) -Yes -Theme gruvbox

.EXAMPLE
    ./setup.ps1                 # interaktivno
    ./setup.ps1 -Yes            # sve podrazumevano, bez pitanja
    ./setup.ps1 -Yes -Theme catppuccin -NoPrompt
    ./setup.ps1 -Uninstall      # ukloni modul i blok iz $PROFILE (konfiguracija ostaje)
#>
[CmdletBinding()]
param(
    [switch]$Yes,
    [string]$Theme,
    [switch]$NoPrompt,
    [switch]$NoAnalyzer,
    [switch]$NoLazygit,
    [switch]$Build,
    [switch]$ResetConfig,
    [switch]$Uninstall,
    [string]$Source,
    [switch]$NoOtherEdition
)

# NAPOMENA: ovaj fajl mora da se parsira i u Windows PowerShell 5.1 (zbog irm | iex),
# zato ne koristi sintaksu samo iz PowerShell 7 (??, ?:, &&, Join-Path sa 3 argumenta...).

$ErrorActionPreference = 'Stop'
if ($Theme -and ($Theme -notin @('tokyonight', 'catppuccin', 'gruvbox', 'kanagawa'))) {
    throw "Nepoznata tema '$Theme' (dostupne: tokyonight, catppuccin, gruvbox, kanagawa)"
}
$Repo = 'vladimir-stojadinov/psvim'
$RawUrl = "https://raw.githubusercontent.com/$Repo/main/setup.ps1"
$ZipUrl = "https://github.com/$Repo/archive/refs/heads/main.zip"

# Windows PowerShell 5.1 čita .ps1 bez BOM-a kao ANSI (cp1250/1252), pa UTF-8 slova postanu nečitljiva.
# BOM ne smemo da dodamo (lomi irm | iex), zato proveravamo jedan znak i po potrebi ispravljamo tekst.
$script:Canary = 'č'
$script:Misread = $script:Canary.Length -ne 1
function Fix([string]$text) {
    if (-not $script:Misread -or -not $text) { return $text }
    if ($text -notmatch '[\u00C2-\u00C5\u00E2]') { return $text }
    try { return [Text.Encoding]::UTF8.GetString([Text.Encoding]::Default.GetBytes($text)) } catch { return $text }
}
function Say([string]$text, [string]$color = 'Gray') { Write-Host (Fix $text) -ForegroundColor $color }
function Write-Step([string]$text) { Write-Host ''; Say "» $text" Cyan }
function Write-Ok([string]$text) { Say "  √ $text" Green }
function Write-Info([string]$text) { Say "  $text" Gray }
function Write-Warn2([string]$text) { Say "  ! $text" Yellow }

function Ask-YesNo([string]$question, [bool]$default) {
    if ($Yes) { return $default }
    $hint = if ($default) { '[D/n]' } else { '[d/N]' }
    $a = Read-Host (Fix "  $question $hint")
    if ([string]::IsNullOrWhiteSpace($a)) { return $default }
    return ($a.Trim().ToLower() -in @('d', 'da', 'y', 'yes'))
}

function Ask-Choice([string]$question, [string[]]$options, [string]$default) {
    if ($Yes) { return $default }
    Say "  $question" Gray
    for ($i = 0; $i -lt $options.Count; $i++) {
        $mark = if ($options[$i] -eq $default) { ' (podrazumevano)' } else { '' }
        Say ("    {0}) {1}{2}" -f ($i + 1), $options[$i], $mark) Gray
    }
    $a = Read-Host '  Izbor'
    $n = 0
    if ([int]::TryParse($a, [ref]$n) -and $n -ge 1 -and $n -le $options.Count) { return $options[$n - 1] }
    return $default
}

Write-Host ''
Say '  psvim - vim i LazyVim za PowerShell' Blue
Say '  ===================================' DarkBlue

# ---------------------------------------------------------------------------------------------
# 1. Verzija PowerShell-a
# ---------------------------------------------------------------------------------------------
$ver = $PSVersionTable.PSVersion
$isDesktop = $PSVersionTable.PSEdition -ne 'Core'
$onWindows = [IO.Path]::DirectorySeparatorChar -eq '\'
$selfExe = (Get-Process -Id $PID).Path
if ($isDesktop) {
    $editionName = "Windows PowerShell $($ver.Major).$($ver.Minor)"
    if ($ver.Major -lt 5 -or ($ver.Major -eq 5 -and $ver.Minor -lt 1)) {
        Write-Warn2 "Potreban je Windows PowerShell 5.1 ili PowerShell 7.4+ (trenutno: $ver)."
        return
    }
    try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 } catch { }
    # .NET Framework 4.7.2+ (release 461808)
    $release = 0
    try { $release = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction Stop).Release } catch { }
    if ($release -lt 461808) {
        Write-Warn2 'Potreban je .NET Framework 4.7.2 ili noviji: https://dotnet.microsoft.com/download/dotnet-framework'
        Write-Warn2 'Ili koristi PowerShell 7: winget install Microsoft.PowerShell'
        return
    }
}
else {
    $editionName = "PowerShell $ver"
    if ($ver.Major -lt 7 -or ($ver.Major -eq 7 -and $ver.Minor -lt 4)) {
        Write-Warn2 "Potreban je PowerShell 7.4 ili noviji (trenutno: $ver): winget install Microsoft.PowerShell"
        return
    }
}
Write-Info "Instaliram za: $editionName"

# ---------------------------------------------------------------------------------------------
# Putanje
# ---------------------------------------------------------------------------------------------
$userModules = ($env:PSModulePath -split [IO.Path]::PathSeparator) |
    Where-Object { $_ -and $_.StartsWith($HOME, [StringComparison]::OrdinalIgnoreCase) } |
    Select-Object -First 1
if (-not $userModules) {
    if ($isDesktop) { $userModules = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'WindowsPowerShell\Modules' }
    elseif ($onWindows) { $userModules = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'PowerShell\Modules' }
    else { $userModules = Join-Path $HOME '.local/share/powershell/Modules' }
}
$moduleDest = Join-Path $userModules 'PsVim'
$profilePath = $PROFILE.CurrentUserAllHosts
$configDir = if ($env:PSVIM_CONFIG) { Split-Path $env:PSVIM_CONFIG } else { Join-Path (Join-Path $HOME '.config') 'psvim' }
$configPath = if ($env:PSVIM_CONFIG) { $env:PSVIM_CONFIG } else { Join-Path $configDir 'config.json' }
$beginMark = '# >>> psvim >>>'
$endMark = '# <<< psvim <<<'

function Remove-ProfileBlock([string]$text) {
    $pattern = '(?ms)^' + [regex]::Escape($beginMark) + '.*?' + [regex]::Escape($endMark) + '\r?\n?'
    return [regex]::Replace($text, $pattern, '')
}

$script:ModuleWasLocked = $false

# Učitan DLL (npr. jer ga $PROFILE učitava u ovom prozoru) Windows ne da da se obriše,
# ali dozvoljava preimenovanje. Zato zaključane fajlove sklanjamo pod imenom *.psvim-old
# i brišemo ih pri sledećem pokretanju setup-a.
function Remove-InstalledModule {
    if (-not (Test-Path $moduleDest)) { return $true }
    try { Remove-Item $moduleDest -Recurse -Force -ErrorAction Stop; return $true } catch { }
    foreach ($f in @(Get-ChildItem $moduleDest -Recurse -File -ErrorAction SilentlyContinue)) {
        if ($f.Name -like '*.psvim-old*') { continue }
        try { Remove-Item $f.FullName -Force -ErrorAction Stop }
        catch {
            try {
                Rename-Item $f.FullName ($f.Name + '.psvim-old' + [guid]::NewGuid().ToString('N').Substring(0, 6)) -ErrorAction Stop
                $script:ModuleWasLocked = $true
            }
            catch {
                Write-Warn2 "Ne mogu ni da obrišem ni da sklonim $($f.FullName)."
                Write-Warn2 'Zatvori PowerShell prozore u kojima je psvim učitan i pokreni setup ponovo.'
                return $false
            }
        }
    }
    return $true
}

function Remove-StaleFiles {
    if (-not (Test-Path $moduleDest)) { return }
    Get-ChildItem $moduleDest -Recurse -File -Filter '*.psvim-old*' -ErrorAction SilentlyContinue |
        ForEach-Object { Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue }
}

# ---------------------------------------------------------------------------------------------
# Deinstalacija
# ---------------------------------------------------------------------------------------------
if ($Uninstall) {
    Write-Step 'Uklanjam psvim'
    if (Remove-InstalledModule) {
        if ($script:ModuleWasLocked) { Write-Ok "Modul uklonjen ($moduleDest); zaključani fajlovi se brišu kad zatvoriš PowerShell i ponovo pokreneš setup -Uninstall" }
        else { Write-Ok "Modul uklonjen ($moduleDest)" }
    }
    if (Test-Path $profilePath) {
        $text = Get-Content -LiteralPath $profilePath -Raw
        $new = Remove-ProfileBlock $text
        if ($new -ne $text) {
            Copy-Item $profilePath "$profilePath.psvim-bak" -Force
            Set-Content -LiteralPath $profilePath -Value $new -NoNewline -Encoding utf8
            Write-Ok "Blok uklonjen iz $profilePath (rezervna kopija: .psvim-bak)"
        }
    }
    Write-Info "Konfiguracija je ostavljena: $configPath"
    return
}

# ---------------------------------------------------------------------------------------------
# 2. Izvorni fajlovi
# ---------------------------------------------------------------------------------------------
Write-Step 'Izvorni fajlovi'
$root = $null
if ($Source) { $root = (Resolve-Path $Source).Path }
elseif ($PSScriptRoot -and (Test-Path (Join-Path $PSScriptRoot 'module/PsVim/PsVim.psd1'))) { $root = $PSScriptRoot }
if ($root) {
    Write-Ok "Koristim lokalni repozitorijum: $root"
}
else {
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ('psvim-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Path $tmp | Out-Null
    $zip = Join-Path $tmp 'psvim.zip'
    Write-Info "Preuzimam $ZipUrl"
    Invoke-WebRequest -UseBasicParsing -Uri $ZipUrl -OutFile $zip
    Expand-Archive -Path $zip -DestinationPath $tmp -Force
    $root = (Get-ChildItem $tmp -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'module/PsVim/PsVim.psd1') } | Select-Object -First 1).FullName
    if (-not $root) { throw 'Preuzeta arhiva ne sadrži module/PsVim.' }
    Write-Ok "Preuzeto u $root"
}

# ---------------------------------------------------------------------------------------------
# 3. Build (opciono)
# ---------------------------------------------------------------------------------------------
$dll = Join-Path $root 'module/PsVim/bin/PsVim.Core.dll'
if ($isDesktop) { $dll = Join-Path $root 'module/PsVim/bin/net472/PsVim.Core.dll' }
$hasDotnet = [bool](Get-Command dotnet -ErrorAction SilentlyContinue) -and ((dotnet --list-sdks 2>$null) -match '^(8|9|10)\.')
if ($Build -or (-not (Test-Path $dll))) {
    Write-Step 'Build iz izvornog koda'
    if (-not $hasDotnet) {
        if (-not (Test-Path $dll)) { throw 'Nema gotovog DLL-a, a .NET 8 SDK nije instaliran (winget install Microsoft.DotNet.SDK.8).' }
        Write-Warn2 '.NET 8 SDK nije instaliran - koristim gotov DLL.'
    }
    else {
        dotnet build (Join-Path $root 'src/PsVim.Core/PsVim.Core.csproj') -c Release --nologo -v quiet | Out-Host
        if ($LASTEXITCODE) { throw 'Build nije uspeo.' }
        $out = Join-Path $root 'src/PsVim.Core/bin/Release'
        New-Item -ItemType Directory -Force -Path (Join-Path $root 'module/PsVim/bin/net472') | Out-Null
        Copy-Item (Join-Path $out 'net8.0/PsVim.Core.dll') (Join-Path $root 'module/PsVim/bin') -Force
        Copy-Item (Join-Path $out 'net472/PsVim.Core.dll') (Join-Path $root 'module/PsVim/bin/net472') -Force
        Write-Ok 'PsVim.Core.dll napravljen (net8.0 + net472)'
    }
}
else {
    Write-Info 'Koristim gotov PsVim.Core.dll (za build iz koda: ./setup.ps1 -Build)'
}

# ---------------------------------------------------------------------------------------------
# 4. Modul
# ---------------------------------------------------------------------------------------------
Write-Step 'Instalacija modula'
Remove-StaleFiles
if (-not (Remove-InstalledModule)) { return }
New-Item -ItemType Directory -Path $moduleDest -Force | Out-Null
# kopiraj sadržaj (ne folder), da radi i kad su u $moduleDest ostali sklonjeni fajlovi
Copy-Item (Join-Path (Join-Path $root 'module/PsVim') '*') $moduleDest -Recurse -Force
Get-ChildItem $moduleDest -Recurse -Filter '*.new.dll' | Remove-Item -Force -ErrorAction SilentlyContinue
if ($onWindows) { Get-ChildItem $moduleDest -Recurse -File | Unblock-File -ErrorAction SilentlyContinue }
Write-Ok "Instalirano u $moduleDest"
if ($script:ModuleWasLocked) { Write-Info 'Stara verzija je učitana u ovom prozoru - nova važi od sledećeg PowerShell prozora.' }

# ---------------------------------------------------------------------------------------------
# 5. Podešavanja (pitanja)
# ---------------------------------------------------------------------------------------------
Write-Step 'Početno podešavanje'
$enablePrompt = if ($NoPrompt) { $false } else { Ask-YesNo 'Ukljuciti vim mod u PowerShell promptu (Esc + razmak = meni)?' $true }

$writeConfig = $true
if ((Test-Path $configPath) -and -not $ResetConfig) {
    $writeConfig = -not (Ask-YesNo "Konfiguracija već postoji ($configPath). Zadržati je?" $true)
}
if ($writeConfig) {
    $themeChoice = if ($Theme) { $Theme } else { Ask-Choice 'Tema:' @('tokyonight', 'catppuccin', 'gruvbox', 'kanagawa') 'tokyonight' }
    $relative = Ask-YesNo 'Relativni brojevi linija (kao u LazyVim-u)?' $true
    $nerd = Ask-YesNo 'Imaš li Nerd Font u terminalu (za ikonice)?' $false
    $tab = Ask-Choice 'Uvlačenje:' @('4', '2') '4'
    $cfg = [ordered]@{
        theme          = $themeChoice
        number         = $true
        relativeNumber = $relative
        tabStop        = [int]$tab
        shiftWidth     = [int]$tab
        expandTab      = $true
        nerdFont       = $nerd
        autoPairs      = $true
        clipboard      = 'unnamedplus'
        whichKeyDelay  = 300
        keymaps        = @(
            [ordered]@{ mode = 'n'; lhs = '<leader>td'; rhs = ':!Get-Date<CR>'; desc = 'Primer: prikaži datum' }
        )
    }
    New-Item -ItemType Directory -Path (Split-Path $configPath) -Force | Out-Null
    if (Test-Path $configPath) { Copy-Item $configPath "$configPath.bak" -Force }
    $cfg | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $configPath -Encoding utf8
    Write-Ok "Konfiguracija: $configPath"
}
else { Write-Ok "Zadržana postojeća konfiguracija" }

# ---------------------------------------------------------------------------------------------
# 6. $PROFILE
# ---------------------------------------------------------------------------------------------
Write-Step 'PowerShell profil'
$block = @($beginMark, 'Import-Module PsVim')
if ($enablePrompt) { $block += 'Enable-PsVimPrompt' }
$block += $endMark
$blockText = ($block -join [Environment]::NewLine) + [Environment]::NewLine
New-Item -ItemType Directory -Path (Split-Path $profilePath) -Force | Out-Null
$existing = ''
if (Test-Path $profilePath) {
    $existing = Get-Content -LiteralPath $profilePath -Raw
    if ($null -eq $existing) { $existing = '' }
    Copy-Item $profilePath "$profilePath.psvim-bak" -Force
}
$clean = Remove-ProfileBlock $existing
if ($clean.Length -gt 0 -and -not $clean.EndsWith("`n")) { $clean += [Environment]::NewLine }
Set-Content -LiteralPath $profilePath -Value ($clean + $blockText) -NoNewline -Encoding utf8
Write-Ok "Dodato u $profilePath"
if ($existing) { Write-Info "Rezervna kopija: $profilePath.psvim-bak" }

# ---------------------------------------------------------------------------------------------
# 7. Dodaci
# ---------------------------------------------------------------------------------------------
Write-Step 'Dodaci'
if (Get-Module -ListAvailable -Name PSScriptAnalyzer) { Write-Ok 'PSScriptAnalyzer je već instaliran' }
elseif (-not $NoAnalyzer -and (Ask-YesNo 'Instalirati PSScriptAnalyzer (upozorenja i formatiranje koda)?' $true)) {
    try {
        if ($isDesktop -and -not (Get-PackageProvider -ListAvailable -Name NuGet -ErrorAction SilentlyContinue | Where-Object { $_.Version -ge [version]'2.8.5.201' })) {
            Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Scope CurrentUser -Force | Out-Null
        }
        Install-Module PSScriptAnalyzer -Scope CurrentUser -Force -AllowClobber -ErrorAction Stop
        Write-Ok 'PSScriptAnalyzer instaliran'
    }
    catch { Write-Warn2 "PSScriptAnalyzer nije instaliran: $($_.Exception.Message)" }
}
if (Get-Command lazygit -ErrorAction SilentlyContinue) { Write-Ok 'lazygit je već instaliran' }
elseif (-not $NoLazygit -and $onWindows -and (Get-Command winget -ErrorAction SilentlyContinue) -and (Ask-YesNo 'Instalirati lazygit (␣gg u editoru)?' $false)) {
    winget install --id JesseDuffield.lazygit -e --accept-source-agreements --accept-package-agreements | Out-Host
}
if (-not (Get-Command git -ErrorAction SilentlyContinue)) { Write-Warn2 'git nije instaliran - git prečice (␣gs, ␣gl, ␣gd) neće raditi.' }

# ---------------------------------------------------------------------------------------------
# 8. Provera
# ---------------------------------------------------------------------------------------------
Write-Step 'Provera'
$checkScript = @'
try {
    Import-Module PsVim -ErrorAction Stop
    $h = New-PsVimHooks
    $d = $h.Diagnose.Invoke("function x {", "t.ps1")
    $o = [PsVim.Options]::Load()
    "OK|$((Get-Module PsVim).Version)|$($d.Count)|$($o.Theme)"
} catch { "ERR|$($_.Exception.Message)" }
'@
# -EncodedCommand: Windows PowerShell 5.1 kvari navodnike kad višelinijsku komandu šalje drugom procesu
$encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($checkScript))
$check = & $selfExe -NoProfile -NoLogo -NonInteractive -EncodedCommand $encoded
$check = ($check | Select-Object -Last 1)
if ($check -like 'OK|*') {
    $p = $check -split '\|'
    Write-Ok "Modul PsVim $($p[1]) se učitava, dijagnostika radi, tema: $($p[3])"
}
else { Write-Warn2 "Provera nije uspela: $check" }

# ---------------------------------------------------------------------------------------------
# 9. Druga verzija PowerShell-a na istom računaru
# ---------------------------------------------------------------------------------------------
if (-not $NoOtherEdition -and $onWindows) {
    $other = $null; $otherName = $null
    if ($isDesktop) {
        $p7 = Get-Command pwsh -ErrorAction SilentlyContinue
        if ($p7) { $other = $p7.Source }
        elseif (Test-Path (Join-Path $env:ProgramFiles 'PowerShell\7\pwsh.exe')) { $other = Join-Path $env:ProgramFiles 'PowerShell\7\pwsh.exe' }
        $otherName = 'PowerShell 7'
        if ($other) {
            $ov = & $other -NoProfile -NoLogo -Command '$PSVersionTable.PSVersion.Major * 100 + $PSVersionTable.PSVersion.Minor'
            if ([int]($ov | Select-Object -Last 1) -lt 704) { $other = $null }
        }
    }
    else {
        $ps51 = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        if (Test-Path $ps51) { $other = $ps51 }
        $otherName = 'Windows PowerShell 5.1'
    }
    if ($other) {
        Write-Step "Na racunaru postoji i $otherName"
        if (Ask-YesNo "Instalirati psvim i za ${otherName}?" $true) {
            $self = $PSCommandPath
            if (-not $self) {
                $self = Join-Path ([IO.Path]::GetTempPath()) 'psvim-setup.ps1'
                Invoke-WebRequest -UseBasicParsing -Uri $RawUrl -OutFile $self
            }
            $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $self, '-Yes', '-NoOtherEdition', '-NoLazygit', '-Source', $root)
            if (-not $enablePrompt) { $argList += '-NoPrompt' }
            if ($NoAnalyzer) { $argList += '-NoAnalyzer' }
            & $other @argList
        }
    }
    elseif ($isDesktop) {
        Write-Info 'Savet: PowerShell 7 je brži i ima bolji prompt (winget install Microsoft.PowerShell), pa ponovo pokreni setup.'
    }
}

Write-Host ''
Say '  Gotovo!' Green
Say '  Otvori novi PowerShell prozor (ili ukucaj: . $PROFILE), pa:' Gray
Say '      psvim              početni ekran' White
Say '      psvim fajl.ps1     otvori fajl' White
Say '      :help              pomoć u editoru' White
if ($enablePrompt) { Write-Host '  U promptu: Esc pa razmak otvara meni.' -ForegroundColor Gray }
Say "  Uputstvo: https://github.com/$Repo/blob/main/UPUTSTVO.md" Gray
Write-Host ''
