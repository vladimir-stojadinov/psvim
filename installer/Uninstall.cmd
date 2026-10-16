@echo off
rem psvim - uklanjanje (konfiguracija u %USERPROFILE%\.config\psvim ostaje)
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup.ps1" -Uninstall
where pwsh >nul 2>nul && pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup.ps1" -Uninstall
echo.
pause
