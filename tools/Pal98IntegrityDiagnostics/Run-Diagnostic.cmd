@echo off
setlocal
set "PalDiagPS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if exist "%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe" set "PalDiagPS=%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe"
"%PalDiagPS%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-Diagnostic.ps1"
endlocal
