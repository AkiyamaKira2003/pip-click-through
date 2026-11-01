@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0pipctl.ps1" %*
exit /b %errorlevel%
