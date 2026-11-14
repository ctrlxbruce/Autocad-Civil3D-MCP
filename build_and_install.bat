@echo off
REM ============================================================
REM  Builds the Civil 3D plugin and installs it so Civil 3D
REM  loads it automatically. Also installs the Python package.
REM  Run this by double-clicking. Close Civil 3D first.
REM ============================================================
setlocal
cd /d "%~dp0"

echo.
echo [1/3] Building the Civil 3D plugin...
dotnet build "civil3d-plugin\Civil3DMcp.csproj" -c Release
if errorlevel 1 (
  echo.
  echo BUILD FAILED. Copy the red error lines above and send them to Claude.
  pause & exit /b 1
)

echo.
echo [2/3] Installing plugin into Civil 3D autoloader...
set BUNDLE=%APPDATA%\Autodesk\ApplicationPlugins\Civil3DMcp.bundle
if not exist "%BUNDLE%\Contents" mkdir "%BUNDLE%\Contents"
copy /Y "civil3d-plugin\Bundle\PackageContents.xml" "%BUNDLE%\" >nul
copy /Y "civil3d-plugin\bin\Release\net8.0\Civil3DMcp.dll" "%BUNDLE%\Contents\" >nul
echo Installed to %BUNDLE%

echo.
echo [3/3] Installing Python requirements...
python -m pip install -r "mcp-server\requirements.txt"
if errorlevel 1 (
  echo Python not found. Install Python 3.10+ from python.org and tick "Add to PATH".
  pause & exit /b 1
)

echo.
echo DONE. Next: add the Claude Desktop config (see README step 4), then start Civil 3D.
pause
