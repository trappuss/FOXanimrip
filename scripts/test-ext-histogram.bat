@echo off
setlocal EnableExtensions EnableDelayedExpansion
rem ===========================================================================
rem  foxanimrip - what file types each game actually holds (by extension code).
rem  SPDX-License-Identifier: MIT
rem
rem  Diagnostic for the .mog hunt: --dump-mog found none, so this counts EVERY
rem  file in each install by its extension code (no dictionary needed) and prints
rem  whether .mog / .mas / .fsm are present. Writes rips\ext-histogram\<game>.tsv.
rem ===========================================================================
cd /d "%~dp0"
set "TOOL=%~dp0foxanimrip-cli.exe"
if not exist "%TOOL%" set "TOOL=%~dp0..\foxanimrip-cli.exe"
set "OUT=%~dp0rips\ext-histogram"
set "LOGS=%~dp0test-logs"
if not exist "%TOOL%" ( echo cannot find foxanimrip-cli.exe & pause & exit /b 66 )
if not exist "%LOGS%" mkdir "%LOGS%"
if not exist "%OUT%" mkdir "%OUT%"

set "TPP=E:\SteamLibrary\steamapps\common\MGS_TPP"
set "GZ=D:\D Games\Metal Gear Solid V - Ground Zeroes"
set "SURVIVE=E:\SteamLibrary\steamapps\common\METAL GEAR SURVIVE"

"%TOOL%" --version > "%LOGS%\97-ext-histogram.log" 2>&1
echo [1/3] TPP...
"%TOOL%" --root "%TPP%" --game tpp --ext-histogram "%OUT%\tpp.tsv" >> "%LOGS%\97-ext-histogram.log" 2>&1
echo [2/3] Ground Zeroes...
"%TOOL%" --root "%GZ%" --game gz --ext-histogram "%OUT%\gz.tsv" >> "%LOGS%\97-ext-histogram.log" 2>&1
echo [3/3] Survive...
"%TOOL%" --root "%SURVIVE%" --game survive --ext-histogram "%OUT%\survive.tsv" >> "%LOGS%\97-ext-histogram.log" 2>&1
echo.
echo done - tables in %OUT%\*.tsv ; say "read".
pause
