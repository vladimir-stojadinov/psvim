@echo off
rem psvim - instalacija bez preuzimanja skripti sa interneta (iz raspakovanog ZIP-a)
rem Instalira za Windows PowerShell 5.1 i nudi instalaciju i za PowerShell 7 ako postoji.
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup.ps1" -Source "%~dp0." %*
echo.
pause
