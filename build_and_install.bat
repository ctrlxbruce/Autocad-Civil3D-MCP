@echo off
REM ============================================================
REM  For developers: builds the Civil 3D plugin from source,
REM  then runs the normal installer with the new build
REM  (plugin + Python environment + Claude connections).
REM  Needs the .NET 8 SDK. Close Civil 3D first.
REM ============================================================
setlocal
cd /d "%~dp0"

echo.
echo Building the Civil 3D plugin...
dotnet build "civil3d-plugin\Civil3DMcp.csproj" -c Release
if errorlevel 1 (
  echo.
  echo BUILD FAILED. Copy the red error lines above and send them to Claude.
  echo.
  pause
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\install.ps1" -PluginDll "%~dp0civil3d-plugin\bin\Release\net8.0\Civil3DMcp.dll" %*
set "RC=%ERRORLEVEL%"
echo.
pause
exit /b %RC%
