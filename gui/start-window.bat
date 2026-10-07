@echo off
rem WinHealthAudit - open the window. Ctrl+C (or closing this window) stops it.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0app.ps1" %*
