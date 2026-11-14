@echo off
REM ============================================================
REM  Civil 3D MCP - one-step installer.
REM  Double-click this file. Close Civil 3D first.
REM  Fewer tools:  install.bat -Mode lean   (or -Mode code)
REM ============================================================
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\install.ps1" %*
set "RC=%ERRORLEVEL%"
echo.
pause
exit /b %RC%
