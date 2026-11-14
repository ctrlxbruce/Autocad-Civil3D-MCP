@echo off
REM Installs the ready-made plugin (no build needed). Close Civil 3D first.
set BUNDLE=%APPDATA%\Autodesk\ApplicationPlugins\Civil3DMcp.bundle
if not exist "%BUNDLE%\Contents" mkdir "%BUNDLE%\Contents"
copy /Y "%~dp0Civil3DMcp.bundle\PackageContents.xml" "%BUNDLE%\" >nul
copy /Y "%~dp0Civil3DMcp.bundle\Contents\Civil3DMcp.dll" "%BUNDLE%\Contents\" >nul
echo Installed to %BUNDLE%
python -m pip install -r "%~dp0..\mcp-server\requirements.txt"
echo DONE. Start Civil 3D - it will load the plugin automatically.
pause
