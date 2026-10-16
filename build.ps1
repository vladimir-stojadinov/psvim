#requires -Version 7.4
<#
.SYNOPSIS
    Build za psvim: kompajlira PsVim.Core.dll i kopira ga u modul.
.EXAMPLE
    ./build.ps1               # build
.EXAMPLE
    ./build.ps1 -Test         # build + testovi
.EXAMPLE
    ./build.ps1 -Install      # build + instalacija modula za trenutnog korisnika
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [switch]$Test,
    [switch]$Install
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK 8 nije pronađen. Instaliraj: winget install Microsoft.DotNet.SDK.8'
}

Write-Host '» Build PsVim.Core' -ForegroundColor Cyan
dotnet build (Join-Path $root 'src/PsVim.Core/PsVim.Core.csproj') -c $Configuration --nologo -v quiet
if ($LASTEXITCODE) { throw 'Build nije uspeo' }

$bin = Join-Path $root 'module/PsVim/bin'
New-Item -ItemType Directory -Force -Path $bin | Out-Null
Copy-Item (Join-Path $root "src/PsVim.Core/bin/$Configuration/net8.0/PsVim.Core.dll") $bin -Force
Write-Host "  ✓ $bin/PsVim.Core.dll" -ForegroundColor Green

if ($Test) {
    Write-Host '» Testovi' -ForegroundColor Cyan
    dotnet run --project (Join-Path $root 'tests/PsVim.Tests') -c $Configuration --nologo
    if ($LASTEXITCODE) { throw 'Testovi nisu prošli' }
}

if ($Install) {
    $modules = ($env:PSModulePath -split [IO.Path]::PathSeparator) |
        Where-Object { $_ -like "$HOME*" } | Select-Object -First 1
    if (-not $modules) { $modules = Join-Path $HOME 'Documents/PowerShell/Modules' }
    $dest = Join-Path $modules 'PsVim'
    Write-Host "» Instalacija u $dest" -ForegroundColor Cyan
    if (Test-Path $dest) {
        # DLL može biti zaključan ako je modul učitan u nekoj sesiji
        try { Remove-Item $dest -Recurse -Force } catch { throw "Ne mogu da obrišem $dest — zatvori PowerShell sesije koje koriste PsVim pa pokušaj ponovo." }
    }
    Copy-Item (Join-Path $root 'module/PsVim') $dest -Recurse -Force
    Write-Host '  ✓ Instalirano. Dodaj u $PROFILE:' -ForegroundColor Green
    Write-Host '      Import-Module PsVim'
    Write-Host '      Enable-PsVimPrompt'
}
