@echo off
setlocal EnableExtensions
rem SPDX-License-Identifier: MIT
rem
rem test-survive-locomotion.bat -- the base player's locomotion, both genders,
rem in place and travelling. Four folders under rips\survive-locomotion\:
rem
rem   male\locomotion              bsm0, baked on the spot (Action library)
rem   male\locomotion-rootmotion   bsm0, keeping the root's travel
rem   female\locomotion            bsf0, on the spot
rem   female\locomotion-rootmotion bsf0, travelling
rem
rem The base skeletons are bsm0_main0_def (male, 127 bones) and bsf0_main0_def
rem (female, 126 bones), found under Assets/ssd/chara/base. The player's motion
rem archive is SsdPlayer_layers (3220 clips) -- Survive's equivalent of TPP's
rem player2_resident. Each export also rips the base model itself (rigged), so
rem the clips have a character to sit on.
rem
rem "In place" bakes every clip at the origin: right for a retargetable Action
rem library, but a character animated from it never leaves the spot. The
rem root-motion twin keeps the travel. Check a walk in both to see the
rem difference.
rem
rem SELECT trims the set. --locomotion keeps walk/run/turn/idle/dash and the
rem like; blank exports the whole player set (all 3220 clips, much larger).
rem Priority here is locomotion, so --locomotion is the default; delete it on
rem the line below for everything.

set "HERE=%~dp0"
set "TOOL=%HERE%foxanimrip-cli.exe"
set "SURVIVE=E:\SteamLibrary\steamapps\common\METAL GEAR SURVIVE"
set "OUT=%HERE%rips\survive-locomotion"
set "LOGS=%HERE%test-logs"

set "MALE=bsm0_main0_def"
set "FEMALE=bsf0_main0_def"
set "SET=--mtar SsdPlayer_layers"
set "SELECT=--locomotion"
set "COMMON=--export-model --dedupe --pack 50"
set ERRORS=0

if exist "%HERE%foxanimrip-cli-new.exe" (
    del "%HERE%foxanimrip-cli.exe" 2>nul
    if not exist "%HERE%foxanimrip-cli.exe" ( ren "%HERE%foxanimrip-cli-new.exe" "foxanimrip-cli.exe" ) else ( set "TOOL=%HERE%foxanimrip-cli-new.exe" )
)
if not exist "%TOOL%" ( echo cannot find foxanimrip-cli.exe beside this script & pause & exit /b 66 )
if not exist "%LOGS%" mkdir "%LOGS%"
del /q "%LOGS%\6*-surviveloco-*.log" 2>nul

echo test-survive-locomotion: 4 exports, detail in test-logs\
"%TOOL%" --version > "%LOGS%\65-surviveloco-run.log" 2>&1

echo [1/4] male, in place...
"%TOOL%" --game survive --root "%SURVIVE%" --character %MALE% %SET% %SELECT% %COMMON% --out "%OUT%\male\locomotion" > "%LOGS%\66-surviveloco-male.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [2/4] male, with root motion...
"%TOOL%" --game survive --root "%SURVIVE%" --character %MALE% %SET% %SELECT% --root-motion %COMMON% --out "%OUT%\male\locomotion-rootmotion" >> "%LOGS%\66-surviveloco-male.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [3/4] female, in place...
"%TOOL%" --game survive --root "%SURVIVE%" --character %FEMALE% %SET% %SELECT% %COMMON% --out "%OUT%\female\locomotion" > "%LOGS%\67-surviveloco-female.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [4/4] female, with root motion...
"%TOOL%" --game survive --root "%SURVIVE%" --character %FEMALE% %SET% %SELECT% --root-motion %COMMON% --out "%OUT%\female\locomotion-rootmotion" >> "%LOGS%\67-surviveloco-female.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo finished %date% %time%, %ERRORS% problem(s) >> "%LOGS%\65-surviveloco-run.log"
echo.
echo done - %ERRORS% problem(s). Output in rips\survive-locomotion\, logs in test-logs\
echo tell Claude "read" to have the results checked
pause
