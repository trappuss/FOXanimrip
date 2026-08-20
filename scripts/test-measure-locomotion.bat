@echo off
setlocal EnableExtensions EnableDelayedExpansion
rem ===========================================================================
rem  foxanimrip - measure authored locomotion parameters (no FBX).
rem  SPDX-License-Identifier: MIT
rem
rem  Writes locomotion-params.tsv per game: for every player locomotion clip,
rem  the root's travel distance (m), speed (m/s), net turn (deg) and turn rate
rem  (deg/s). These are the real numbers MGSV's movement was authored with --
rem  measured off the animation data, not guessed -- for a 1:1 movement rebuild.
rem
rem  Put this file next to foxanimrip-cli.exe and double-click it.
rem ===========================================================================
cd /d "%~dp0"
set "TOOL=%~dp0foxanimrip-cli.exe"
if not exist "%TOOL%" set "TOOL=%~dp0..\foxanimrip-cli.exe"
set "OUT=%~dp0rips\locomotion-params"
set "LOGS=%~dp0test-logs"
set ERRORS=0
if not exist "%TOOL%" ( echo cannot find foxanimrip-cli.exe & pause & exit /b 66 )
if not exist "%LOGS%" mkdir "%LOGS%"

set "TPP=E:\SteamLibrary\steamapps\common\MGS_TPP"
set "GZ=D:\D Games\Metal Gear Solid V - Ground Zeroes"
set "SURVIVE=E:\SteamLibrary\steamapps\common\METAL GEAR SURVIVE"

"%TOOL%" --version > "%LOGS%\95-measure.log" 2>&1

echo [1/4] TPP player (male base) locomotion...
"%TOOL%" --root "%TPP%" --game tpp --character skl0_main0_def --mtar player2_resident --mtar mgoplayer_resident --locomotion --measure --out "%OUT%\tpp-male" >> "%LOGS%\95-measure.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [2/4] TPP player (female base) locomotion...
"%TOOL%" --root "%TPP%" --game tpp --character skl0_main0_def_f --mtar player2_resident --mtar mgoplayer_resident --locomotion --measure --out "%OUT%\tpp-female" >> "%LOGS%\95-measure.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [3/4] Ground Zeroes player locomotion...
"%TOOL%" --root "%GZ%" --game gz --character sna2_main0_def --all --locomotion --measure --out "%OUT%\gz" >> "%LOGS%\95-measure.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [4/4] Survive player locomotion...
"%TOOL%" --root "%SURVIVE%" --game survive --character bsf0_main0_def --mtar SsdPlayer_layers --locomotion --measure --out "%OUT%\survive" >> "%LOGS%\95-measure.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo finished, %ERRORS% problem step(s) >> "%LOGS%\95-measure.log"
echo.
echo done - %ERRORS% problem step(s). Tables in %OUT%\*\locomotion-params.tsv
echo tell Claude "read" to have them analysed.
pause
exit /b %ERRORS%
