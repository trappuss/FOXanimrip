@echo off
setlocal EnableExtensions
rem ===========================================================================
rem  Visual QA: headless Blender turntable render of assembled models.
rem  SPDX-License-Identifier: MIT
rem
rem  Renders angles of a model FBX (or one thumbnail per model in a folder) so
rem  material / rig / texture regressions are obvious without opening Blender.
rem
rem  Usage:
rem    test-qa-turntable.bat "path\to\model.fbx"          (6 angles)
rem    test-qa-turntable.bat "path\to\rips\some-folder"   (contact sheet)
rem
rem  Set BLENDER below to your blender.exe if it is not on PATH.
rem ===========================================================================
cd /d "%~dp0"
set "BLENDER=blender"
where %BLENDER% >nul 2>&1 || set "BLENDER=C:\Program Files\Blender Foundation\Blender 4.4\blender.exe"

set "TARGET=%~1"
if "%TARGET%"=="" set "TARGET=%~dp0rips"
set "OUT=%~dp0rips\_qa"

if not exist "%BLENDER%" (
    echo Could not find blender.exe. Edit BLENDER at the top of this bat.
    pause & exit /b 66
)

echo Rendering QA views of "%TARGET%" -> "%OUT%"
"%BLENDER%" --background --factory-startup --python "%~dp0qa-turntable.py" -- "%TARGET%" "%OUT%" 6
echo.
echo done - images in %OUT%
pause
