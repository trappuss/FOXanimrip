@echo off
setlocal EnableExtensions EnableDelayedExpansion
rem ===========================================================================
rem  foxanimrip - EVERY animation in all three games, organised by origin.
rem  SPDX-License-Identifier: MIT
rem
rem  For each game this runs the "rip every set" sweep (--all-sets): every
rem  animation archive in the game is bound to the model whose skeleton best
rem  fits it, so nothing is left out for want of a hand-picked base model.
rem  --all-models widens the candidate skeletons past characters to everything
rem  the game ships (vehicles, gear, creatures) -- no stone unturned.
rem
rem  Each clip lands mirroring where it lives in the game archives:
rem
rem    rips\all-anims\<Game>\<motion>\Assets\...\<mtar>\<clip>.fbx
rem
rem  ...e.g.  ...\in-place\Assets\tpp\chara\player\player2_resident\run.fbx
rem
rem  Every clip is exported twice, once baked on the spot (in-place, for a
rem  retargetable Action library) and once with its real travel (root-motion).
rem  One FBX per clip, so the tree reads like the game's own folders.
rem
rem  Reports, per game and motion, next to the clips:
rem    all-sets-report.tsv   every set: the model used, bones matched, coverage,
rem                          and any set that could not be placed (UNCOVERED).
rem    index.tsv             every exported clip -> its file and origin path.
rem  A roll-up lands in rips\all-anims\_SUMMARY.txt.
rem
rem  This is a BIG job: reading every model skeleton and every archive in three
rem  games, twice. Expect it to run for a long time. Everything is logged to
rem  test-logs\8*-allanims-*.log for unattended reading -- when it finishes,
rem  tell Claude "read".
rem
rem  Put this file next to foxanimrip-cli.exe and double-click it.
rem ===========================================================================

cd /d "%~dp0"

set "TOOL=%~dp0foxanimrip-cli.exe"
if not exist "%TOOL%" set "TOOL=%~dp0..\foxanimrip-cli.exe"
set "OUT=%~dp0rips\all-anims"
set "LOGS=%~dp0test-logs"
set ERRORS=0

if not exist "%TOOL%" (
    echo cannot find foxanimrip-cli.exe beside this script
    pause
    exit /b 66
)
if not exist "%LOGS%" mkdir "%LOGS%"
if not exist "%OUT%"  mkdir "%OUT%"
del /q "%LOGS%\8*-allanims-*.log" 2>nul

rem == game roots ==============================================================
rem  Explicit so the right install is used even with more than one on disk.
rem  Edit these if your games live elsewhere.
set "TPP_ROOT=E:\SteamLibrary\steamapps\common\MGS_TPP"
set "GZ_ROOT=D:\D Games\Metal Gear Solid V - Ground Zeroes"
set "SURVIVE_ROOT=E:\SteamLibrary\steamapps\common\METAL GEAR SURVIVE"
rem ===========================================================================

"%TOOL%" --version > "%LOGS%\80-allanims-run.log" 2>&1
echo test-rip-all-anims: 3 games x 2 motions, detail in test-logs\
echo output tree: %OUT%
echo.

call :ripgame tpp     "%TPP_ROOT%"     "Metal Gear Solid V - The Phantom Pain" 81
call :ripgame gz      "%GZ_ROOT%"      "Metal Gear Solid V - Ground Zeroes"    83
call :ripgame survive "%SURVIVE_ROOT%" "Metal Gear Survive"                    85

call :summary

echo finished %date% %time%, %ERRORS% problem step(s) >> "%LOGS%\80-allanims-run.log"
echo.
echo done - %ERRORS% problem step(s). Tree in %OUT%
echo roll-up: %OUT%\_SUMMARY.txt
echo tell Claude "read" to have the results checked
pause
exit /b %ERRORS%

rem ---------------------------------------------------------------------------
rem  :ripgame  <game-id>  <root>  <friendly name>  <log-number-base>
rem  Rips one game both ways. Skips a game whose root is missing.
rem ---------------------------------------------------------------------------
:ripgame
set "GID=%~1"
set "GROOT=%~2"
set "GNAME=%~3"
set "N=%~4"
set /a N2=%N%+1

if not exist "%GROOT%" (
    echo [skip] %GNAME%: root not found - %GROOT%
    echo [skip] root not found: %GROOT% > "%LOGS%\%N%-allanims-%GID%.log"
    exit /b 0
)

echo [%GNAME%] in-place ...
"%TOOL%" --root "%GROOT%" --game %GID% --all-sets --all-models --out "%OUT%\%GNAME%\in-place" > "%LOGS%\%N%-allanims-%GID%-inplace.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [%GNAME%] root-motion ...
"%TOOL%" --root "%GROOT%" --game %GID% --all-sets --all-models --root-motion --out "%OUT%\%GNAME%\root-motion" > "%LOGS%\%N2%-allanims-%GID%-rootmotion.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

exit /b 0

rem ---------------------------------------------------------------------------
rem  :summary  -- roll up every all-sets-report.tsv into one plain-text summary.
rem  Pure cmd: counts assigned vs total sets per report with find /c.
rem ---------------------------------------------------------------------------
:summary
set "SUM=%OUT%\_SUMMARY.txt"
> "%SUM%" echo foxanimrip - all animations, coverage summary
>> "%SUM%" echo generated %date% %time%
>> "%SUM%" echo.
for /f "delims=" %%R in ('dir /b /s "%OUT%\all-sets-report.tsv" 2^>nul') do (
    rem  Piping into find /c prints just the number (no filename header).
    for /f %%A in ('type "%%R" ^| find /c "assigned"') do set "ASG=%%A"
    for /f %%T in ('type "%%R" ^| find /c /v ""') do set "TOT=%%T"
    set /a SETS=!TOT!-1
    >> "%SUM%" echo %%R
    >> "%SUM%" echo     sets: !SETS!   assigned: !ASG!   ^(UNCOVERED rows are in the report^)
    >> "%SUM%" echo.
)
>> "%SUM%" echo Per-clip origin map is index.tsv in each motion folder.
exit /b 0
