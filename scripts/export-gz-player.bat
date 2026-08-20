@echo off
rem ---------------------------------------------------------------------------
rem  foxanimrip - Ground Zeroes player animations for sna2_main0_def
rem  SPDX-License-Identifier: MIT
rem
rem  Exports the same locomotion twice, once on the spot and once travelling,
rem  plus the facial set on its own:
rem
rem    gz-player\locomotion              in place  - for a retargetable library
rem    gz-player\locomotion-rootmotion   travelling - real displacement
rem    gz-player\facial                  facial, exported once
rem
rem  Facial is not duplicated across the two: face bones carry no root travel,
rem  so both copies would be identical and one of them a waste of disk.
rem
rem  Put this file next to foxanimrip-cli.exe and double-click it.
rem ---------------------------------------------------------------------------

setlocal
cd /d "%~dp0"

rem == settings ===============================================================
set GAME_ROOT=D:\D Games\Metal Gear Solid V - Ground Zeroes
set MODEL=sna2_main0_def
set OUT=C:\rips\gz-player
set PACK=50

rem  How locomotion clips are chosen. Two options:
rem    --grid        exact: only clips forming a detected movement grid.
rem    --locomotion  looser: any clip whose name contains a movement fragment.
rem
rem  Step 1 below prints which grids exist. Ground Zeroes names its clips
rem  differently from The Phantom Pain, so if the grid report comes back empty,
rem  change this to --locomotion. --grid selecting nothing is reported, not
rem  silently obeyed, so you will see it either way.
set SELECT=--grid
rem ===========================================================================

rem  Works whether this sits beside the tool or one folder below it.
set TOOL=%~dp0foxanimrip-cli.exe
if not exist "%TOOL%" set TOOL=%~dp0..\foxanimrip-cli.exe
set REPORTS=%OUT%\_reports

if not exist "%TOOL%" (
    echo.
    echo   Cannot find foxanimrip-cli.exe next to this script.
    echo   Put this .bat beside foxanimrip-cli.exe, or in a folder
    echo   directly inside the one holding it.
    goto :end
)
if not exist "%GAME_ROOT%" (
    echo.
    echo   Ground Zeroes not found at:
    echo     %GAME_ROOT%
    echo   Fix GAME_ROOT at the top of this file.
    goto :end
)
if not exist "%REPORTS%" mkdir "%REPORTS%" 2>nul

echo.
echo ===========================================================
echo   1 of 4   Checks
echo ===========================================================
echo.
echo   The first run searches the archives for this character's rig.
echo   That takes a minute or two once, then it is cached.
echo.

echo   Listing the player animation sets ...
"%TOOL%" --game gz --root "%GAME_ROOT%" --list-sets TppGzPlayer > "%REPORTS%\sets.txt" 2>&1

echo   Looking for locomotion grids ...
"%TOOL%" --game gz --root "%GAME_ROOT%" --list-grids TppGzPlayer_layers > "%REPORTS%\grids.txt" 2>&1
type "%REPORTS%\grids.txt"

echo.
echo   Checking %MODEL% against both sets ...
"%TOOL%" --game gz --root "%GAME_ROOT%" --character %MODEL% --why-mtar TppGzPlayer_layers > "%REPORTS%\why-layers.txt" 2>&1
"%TOOL%" --game gz --root "%GAME_ROOT%" --character %MODEL% --why-mtar TppGzPlayerFacial  > "%REPORTS%\why-facial.txt" 2>&1

echo.
echo   Reports in %REPORTS%
echo.
echo   Read the grid list printed above. If it lists no grids, stop, set
echo   SELECT=--locomotion at the top of this file, and run again -- SELECT is
echo   currently %SELECT%.
echo.
echo   Also check why-layers.txt and why-facial.txt. If either says 0 bones
echo   matched, that set will contribute nothing and the model name is the
echo   thing to change.
echo.
pause

echo.
echo ===========================================================
echo   2 of 4   Locomotion, in place
echo ===========================================================
echo.
"%TOOL%" --game gz --root "%GAME_ROOT%" --character %MODEL% ^
         --mtar TppGzPlayer_layers %SELECT% ^
         --export-model --dedupe --pack %PACK% ^
         --out "%OUT%\locomotion"
if errorlevel 1 goto :failed

echo.
echo ===========================================================
echo   3 of 4   Locomotion, with root motion
echo ===========================================================
echo.
"%TOOL%" --game gz --root "%GAME_ROOT%" --character %MODEL% ^
         --mtar TppGzPlayer_layers %SELECT% --root-motion ^
         --export-model --dedupe --pack %PACK% ^
         --out "%OUT%\locomotion-rootmotion"
if errorlevel 1 goto :failed

echo.
echo ===========================================================
echo   4 of 4   Facial
echo ===========================================================
echo.
echo   Every clip in the set, not just locomotion -- a movement filter would
echo   match almost nothing in a facial archive.
echo.
"%TOOL%" --game gz --root "%GAME_ROOT%" --character %MODEL% ^
         --mtar TppGzPlayerFacial ^
         --export-model --dedupe --pack %PACK% ^
         --out "%OUT%\facial"
if errorlevel 1 goto :failed

echo.
echo ===========================================================
echo   Done.  Everything is under %OUT%
echo ===========================================================
echo.
echo   locomotion              plays on the spot
echo   locomotion-rootmotion   travels
echo   facial                  the whole facial set
echo.
echo   Each folder has the model, its textures, one FBX per %PACK% clips and
echo   an index.tsv listing every clip with its frame and bone counts.
echo.
echo   Check the two locomotion folders against each other in Blender: the
echo   same clip should stand still in one and move in the other. If both
echo   stand still, --root-motion did not take effect.
echo.
goto :end

:failed
echo.
echo   ** An export failed. Nothing after that point ran.
echo   ** The reports in %REPORTS% usually say why more plainly than
echo   ** the messages above.
echo.

:end
echo.
pause
endlocal
