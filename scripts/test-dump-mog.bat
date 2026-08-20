@echo off
setlocal EnableExtensions EnableDelayedExpansion
rem ===========================================================================
rem  foxanimrip - extract motion-graph (.mog) files from all three games.
rem  SPDX-License-Identifier: MIT
rem
rem  .mog files hold the blend/state logic behind locomotion. They are
rem  hash-named (in no dictionary), so the tool finds them by extension code and
rem  saves the raw bytes plus a mogs.tsv header summary. This gives us the real
rem  files to build the graph parser against -- the flagship piece for a 1:1
rem  movement rebuild.
rem
rem  Put next to foxanimrip-cli.exe and double-click.
rem ===========================================================================
cd /d "%~dp0"
set "TOOL=%~dp0foxanimrip-cli.exe"
if not exist "%TOOL%" set "TOOL=%~dp0..\foxanimrip-cli.exe"
set "OUT=%~dp0rips\mogs"
set "LOGS=%~dp0test-logs"
set ERRORS=0
if not exist "%TOOL%" ( echo cannot find foxanimrip-cli.exe & pause & exit /b 66 )
if not exist "%LOGS%" mkdir "%LOGS%"

set "TPP=E:\SteamLibrary\steamapps\common\MGS_TPP"
set "GZ=D:\D Games\Metal Gear Solid V - Ground Zeroes"
set "SURVIVE=E:\SteamLibrary\steamapps\common\METAL GEAR SURVIVE"

"%TOOL%" --version > "%LOGS%\96-dump-mog.log" 2>&1

echo [1/3] TPP motion graphs...  (first run rescans the archive index)
"%TOOL%" --root "%TPP%" --game tpp --rescan --dump-mog "%OUT%\tpp" >> "%LOGS%\96-dump-mog.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [2/3] Ground Zeroes motion graphs...
"%TOOL%" --root "%GZ%" --game gz --rescan --dump-mog "%OUT%\gz" >> "%LOGS%\96-dump-mog.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [3/3] Survive motion graphs...
"%TOOL%" --root "%SURVIVE%" --game survive --rescan --dump-mog "%OUT%\survive" >> "%LOGS%\96-dump-mog.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo finished, %ERRORS% problem step(s) >> "%LOGS%\96-dump-mog.log"
echo.
echo done - raw .mog files in %OUT%\*\raw\, index in %OUT%\*\mogs.tsv
echo tell Claude "read" so the graph parser can be built against the real files.
pause
exit /b %ERRORS%
