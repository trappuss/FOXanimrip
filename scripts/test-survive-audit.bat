@echo off
setlocal EnableExtensions
rem SPDX-License-Identifier: MIT
rem
rem test-survive-audit.bat -- prove the tool reads Metal Gear Survive, and
rem gather everything needed to write its rip scripts. Read-only: nothing here
rem exports or changes a file. Logs land in test-logs\, and the inventory tables
rem in test-logs\survive-inventory\ so they can be read from this folder.
rem
rem Why an audit before the rip scripts: Survive's base player skeleton and its
rem locomotion archive are not guessable from outside the files -- unlike TPP,
rem there is no wiki to name them. This finds them, so the rip scripts can name
rem the right things instead of guessing and wasting a long run.

set "HERE=%~dp0"
set "TOOL=%HERE%foxanimrip-cli.exe"
set "LOGS=%HERE%test-logs"
set "SURVIVE=E:\SteamLibrary\steamapps\common\METAL GEAR SURVIVE"
set ERRORS=0

if exist "%HERE%foxanimrip-cli-new.exe" (
    del "%HERE%foxanimrip-cli.exe" 2>nul
    if not exist "%HERE%foxanimrip-cli.exe" ( ren "%HERE%foxanimrip-cli-new.exe" "foxanimrip-cli.exe" ) else ( set "TOOL=%HERE%foxanimrip-cli-new.exe" )
)
if not exist "%TOOL%" (
    echo cannot find foxanimrip-cli.exe beside this script
    pause
    exit /b 66
)
if not exist "%LOGS%" mkdir "%LOGS%"
del /q "%LOGS%\7*-survive-*.log" 2>nul

echo test-survive-audit: 6 steps, detail in test-logs\
"%TOOL%" --version > "%LOGS%\70-survive-run.log" 2>&1

echo [1/6] which games the tool detects...
echo [cmd] --list-games > "%LOGS%\71-survive-games.log"
"%TOOL%" --list-games >> "%LOGS%\71-survive-games.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [2/6] where Survive's archives are, and how many...
echo [cmd] --game survive --where > "%LOGS%\72-survive-where.log"
"%TOOL%" --game survive --root "%SURVIVE%" --where >> "%LOGS%\72-survive-where.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [3/6] full inventory: every model, texture and variation (slow)...
echo [cmd] --game survive --inventory survive-inventory > "%LOGS%\73-survive-inventory.log"
"%TOOL%" --game survive --root "%SURVIVE%" --inventory "%LOGS%\survive-inventory" >> "%LOGS%\73-survive-inventory.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [4/6] every animation archive in the game, with clip and bone counts...
echo [cmd] --game survive --list-sets > "%LOGS%\74-survive-sets.log"
"%TOOL%" --game survive --root "%SURVIVE%" --list-sets >> "%LOGS%\74-survive-sets.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [5/6] the customisation part models (arm/head/leg/armor/body)...
echo [cmd] --list-models for each ssd part > "%LOGS%\75-survive-models.log"
for %%p in (arf arm hdf hdm lgf lgm uaf uam bdf bdm) do (
    echo [cmd] --list-models %%p >> "%LOGS%\75-survive-models.log"
    "%TOOL%" --game survive --root "%SURVIVE%" --list-models %%p >> "%LOGS%\75-survive-models.log" 2>&1
)
if errorlevel 1 set /a ERRORS+=1

echo [6/6] candidates for the base player skeleton...
echo [cmd] --list-models for pl / main / body / base > "%LOGS%\76-survive-base.log"
for %%p in (pl_ player main0 base skl chr) do (
    echo [cmd] --list-models %%p >> "%LOGS%\76-survive-base.log"
    "%TOOL%" --game survive --root "%SURVIVE%" --list-models %%p >> "%LOGS%\76-survive-base.log" 2>&1
)
if errorlevel 1 set /a ERRORS+=1

echo finished %date% %time%, %ERRORS% problem(s) >> "%LOGS%\70-survive-run.log"
echo.
echo done - %ERRORS% problem(s). Logs in test-logs\, inventory in test-logs\survive-inventory\
echo tell Claude "read" and it will write the two rip scripts from what this found
pause
