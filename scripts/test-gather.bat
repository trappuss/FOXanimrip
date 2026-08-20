@echo off
setlocal EnableExtensions
rem SPDX-License-Identifier: MIT
rem
rem test-gather.bat -- collect everything Claude needs to read, in one go.
rem
rem Double-click it, wait, done. It writes numbered logs into test-logs\
rem beside this file; nothing here modifies the games or exports anything.
rem Re-running overwrites the previous logs, so a run is always a snapshot.
rem
rem What it answers, log by log:
rem   00  which build ran, what was copied, what failed
rem   12  which player models exist (skl0 / dlf0 / mgo stems)
rem   20-22  which animation sets each player model matches
rem   23-25  why player2_resident matches (or does not) per model
rem   26-29  clip names and locomotion grids in player2 / mgoplayer
rem   30-34  the same questions for Ground Zeroes, plus its inventory
rem   99  file list and error count
rem It also copies the Phantom Pain inventory tables from E:\ into
rem test-logs\tpp-inventory so they are readable from this folder.

set "HERE=%~dp0"
set "TOOL=%HERE%foxanimrip-cli.exe"
set "LOGS=%HERE%test-logs"
set "TPPINV=E:\E\Torrents_inventory"
set "GZROOT=D:\D Games\Metal Gear Solid V - Ground Zeroes"
set ERRORS=0

if not exist "%TOOL%" (
    echo cannot find foxanimrip-cli.exe beside this script
    pause
    exit /b 66
)
if not exist "%LOGS%" mkdir "%LOGS%"
del /q "%LOGS%\*.log" 2>nul

echo test-gather: 16 steps, each announced here, detail in test-logs\
echo test-gather started %date% %time% > "%LOGS%\00-run.log"
"%TOOL%" --version >> "%LOGS%\00-run.log" 2>&1

rem ---- The Phantom Pain inventory tables, copied whole -------------------
if exist "%TPPINV%\variations.tsv" (
    if not exist "%LOGS%\tpp-inventory" mkdir "%LOGS%\tpp-inventory"
    copy /y "%TPPINV%\models.tsv"     "%LOGS%\tpp-inventory\" >nul
    copy /y "%TPPINV%\textures.tsv"   "%LOGS%\tpp-inventory\" >nul
    copy /y "%TPPINV%\variations.tsv" "%LOGS%\tpp-inventory\" >nul
    copy /y "%TPPINV%\rip-all-models.bat" "%LOGS%\tpp-inventory\" >nul
    echo copied TPP inventory tables from %TPPINV% >> "%LOGS%\00-run.log"
) else (
    echo MISSING %TPPINV%\variations.tsv - run the TPP inventory first >> "%LOGS%\00-run.log"
    set /a ERRORS+=1
)

rem ---- Which player models exist -----------------------------------------
echo [1/16] listing player models...
echo [cmd] --game tpp --list-models skl0 > "%LOGS%\12-tpp-models.log"
"%TOOL%" --game tpp --list-models skl0 >> "%LOGS%\12-tpp-models.log" 2>&1
if errorlevel 1 set /a ERRORS+=1
echo [cmd] --game tpp --list-models dlf0 >> "%LOGS%\12-tpp-models.log"
"%TOOL%" --game tpp --list-models dlf0 >> "%LOGS%\12-tpp-models.log" 2>&1
if errorlevel 1 set /a ERRORS+=1
echo [cmd] --game tpp --list-models mgo >> "%LOGS%\12-tpp-models.log"
"%TOOL%" --game tpp --list-models mgo >> "%LOGS%\12-tpp-models.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

rem ---- Animation sets per player model -----------------------------------
echo [2/16] TPP animation sets for skl0_main0_def...
echo [cmd] --game tpp --character skl0_main0_def --list-sets resident > "%LOGS%\20-tpp-sets-male.log"
"%TOOL%" --game tpp --character skl0_main0_def --list-sets resident >> "%LOGS%\20-tpp-sets-male.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [3/16] TPP animation sets for skl0_main0_def_f...
echo [cmd] --game tpp --character skl0_main0_def_f --list-sets resident > "%LOGS%\21-tpp-sets-female.log"
"%TOOL%" --game tpp --character skl0_main0_def_f --list-sets resident >> "%LOGS%\21-tpp-sets-female.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [4/16] TPP animation sets for dlf0_main0_def_f...
echo [cmd] --game tpp --character dlf0_main0_def_f --list-sets resident > "%LOGS%\22-tpp-sets-dlf0.log"
"%TOOL%" --game tpp --character dlf0_main0_def_f --list-sets resident >> "%LOGS%\22-tpp-sets-dlf0.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

rem ---- Do male and female share player2_resident -------------------------
echo [5/16] why player2_resident matches the male model...
echo [cmd] --game tpp --character skl0_main0_def --why-mtar player2_resident > "%LOGS%\23-tpp-why-player2-male.log"
"%TOOL%" --game tpp --character skl0_main0_def --why-mtar player2_resident >> "%LOGS%\23-tpp-why-player2-male.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [6/16] why player2_resident matches the female model...
echo [cmd] --game tpp --character skl0_main0_def_f --why-mtar player2_resident > "%LOGS%\24-tpp-why-player2-female.log"
"%TOOL%" --game tpp --character skl0_main0_def_f --why-mtar player2_resident >> "%LOGS%\24-tpp-why-player2-female.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [7/16] which skl0 models fit player2_resident...
echo [cmd] --game tpp --for-mtar player2_resident --model-filter skl0 > "%LOGS%\25-tpp-for-player2.log"
"%TOOL%" --game tpp --for-mtar player2_resident --model-filter skl0 >> "%LOGS%\25-tpp-for-player2.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

rem ---- Clip names and locomotion grids -----------------------------------
echo [8/16] clip names in player2_resident...
echo [cmd] --game tpp --list-clips player2_resident > "%LOGS%\26-tpp-clips-player2.log"
"%TOOL%" --game tpp --list-clips player2_resident >> "%LOGS%\26-tpp-clips-player2.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [9/16] locomotion grids in player2_resident...
echo [cmd] --game tpp --list-grids player2_resident > "%LOGS%\27-tpp-grids-player2.log"
"%TOOL%" --game tpp --list-grids player2_resident >> "%LOGS%\27-tpp-grids-player2.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [10/16] clip names in mgoplayer_resident...
echo [cmd] --game tpp --list-clips mgoplayer_resident > "%LOGS%\28-tpp-clips-mgoplayer.log"
"%TOOL%" --game tpp --list-clips mgoplayer_resident >> "%LOGS%\28-tpp-clips-mgoplayer.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [11/16] locomotion grids in mgoplayer_resident...
echo [cmd] --game tpp --list-grids mgoplayer_resident > "%LOGS%\29-tpp-grids-mgoplayer.log"
"%TOOL%" --game tpp --list-grids mgoplayer_resident >> "%LOGS%\29-tpp-grids-mgoplayer.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

rem ---- Ground Zeroes, same questions -------------------------------------
echo [12/16] Ground Zeroes inventory (slowest step)...
echo [cmd] --game gz --inventory test-logs\gz-inventory > "%LOGS%\30-gz-inventory.log"
"%TOOL%" --game gz --root "%GZROOT%" --inventory "%LOGS%\gz-inventory" >> "%LOGS%\30-gz-inventory.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [13/16] GZ animation sets for sna2_main0_def...
echo [cmd] --game gz --character sna2_main0_def --list-sets > "%LOGS%\31-gz-sets.log"
"%TOOL%" --game gz --root "%GZROOT%" --character sna2_main0_def --list-sets >> "%LOGS%\31-gz-sets.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [14/16] GZ clip names in TppGzPlayer_layers...
echo [cmd] --game gz --list-clips TppGzPlayer_layers > "%LOGS%\32-gz-clips-layers.log"
"%TOOL%" --game gz --root "%GZROOT%" --list-clips TppGzPlayer_layers >> "%LOGS%\32-gz-clips-layers.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [15/16] GZ locomotion grids in TppGzPlayer_layers...
echo [cmd] --game gz --list-grids TppGzPlayer_layers > "%LOGS%\33-gz-grids-layers.log"
"%TOOL%" --game gz --root "%GZROOT%" --list-grids TppGzPlayer_layers >> "%LOGS%\33-gz-grids-layers.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [16/16] GZ clip names in TppGzPlayerFacial...
echo [cmd] --game gz --list-clips TppGzPlayerFacial > "%LOGS%\34-gz-clips-facial.log"
"%TOOL%" --game gz --root "%GZROOT%" --list-clips TppGzPlayerFacial >> "%LOGS%\34-gz-clips-facial.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

rem ---- Wrap up ------------------------------------------------------------
echo finished %date% %time%, %ERRORS% problem(s) > "%LOGS%\99-summary.log"
dir /s /-c "%LOGS%" >> "%LOGS%\99-summary.log"

echo.
echo done - %ERRORS% problem(s), logs in test-logs\
echo tell Claude "read" and it will take it from there
pause
