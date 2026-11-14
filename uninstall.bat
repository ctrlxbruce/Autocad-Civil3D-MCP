@echo off
REM ============================================================
REM  Civil 3D MCP - uninstaller.
REM  Close Civil 3D and Claude first, then double-click this file.
REM ============================================================
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\uninstall.ps1" %*
set "RC=%ERRORLEVEL%"
echo.
pause
exit /b %RC%
