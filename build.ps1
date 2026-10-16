<#
.SYNOPSIS
    Build za psvim: kompajlira PsVim.Core.dll za PowerShell 7 (net8.0) i Windows PowerShell 5.1 (net472).
.EXAMPLE
    ./build.ps1               # build
    ./build.ps1 -Test         # build + testovi
    ./build.ps1 -Install      # build + instalacija (poziva setup.ps1)
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [switch]$Test,
    [switch]$Install
)
# Radi i u Windows PowerShell 5.1 i u PowerShell 7.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$onWindows = [IO.Path]::DirectorySeparatorChar -eq '\'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK 8 nije instaliran. Instaliraj: winget install Microsoft.DotNet.SDK.8'
}

Write-Host '>> Build PsVim.Core (net8.0 + net472)' -ForegroundColor Cyan
dotnet build (Join-Path $root 'src/PsVim.Core/PsVim.Core.csproj') -c $Configuration --nologo -v quiet
if ($LASTEXITCODE) { throw 'Build nije uspeo' }

$bin = Join-Path $root 'module/PsVim/bin'
$bin472 = Join-Path $bin 'net472'
New-Item -ItemType Directory -Force -Path $bin472 | Out-Null
$out = Join-Path $root "src/PsVim.Core/bin/$Configuration"
Copy-Item (Join-Path $out 'net8.0/PsVim.Core.dll') $bin -Force
Copy-Item (Join-Path $out 'net472/PsVim.Core.dll') $bin472 -Force
Write-Host "  + $bin/PsVim.Core.dll (PowerShell 7)" -ForegroundColor Green
Write-Host "  + $bin472/PsVim.Core.dll (Windows PowerShell 5.1)" -ForegroundColor Green

if ($Test) {
    dotnet build (Join-Path $root 'tests/PsVim.Tests') -c $Configuration --nologo -v quiet
    if ($LASTEXITCODE) { throw 'Build testova nije uspeo' }
    Write-Host '>> Testovi (net8.0)' -ForegroundColor Cyan
    dotnet (Join-Path $root "tests/PsVim.Tests/bin/$Configuration/net8.0/PsVim.Tests.dll")
    if ($LASTEXITCODE) { throw 'Testovi (net8.0) nisu prosli' }
    $exe = Join-Path $root "tests/PsVim.Tests/bin/$Configuration/net472/PsVim.Tests.exe"
    if (Test-Path $exe) {
        Write-Host '>> Testovi (net472)' -ForegroundColor Cyan
        if ($onWindows) { & $exe } elseif (Get-Command mono -ErrorAction SilentlyContinue) { mono $exe }
        if ($LASTEXITCODE) { throw 'Testovi (net472) nisu prosli' }
    }
}

if ($Install) {
    & (Join-Path $root 'setup.ps1') -Source $root
}
