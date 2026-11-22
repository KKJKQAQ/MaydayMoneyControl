@echo off
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0Run.ps1" LaunchGame
if errorlevel 1 pause
