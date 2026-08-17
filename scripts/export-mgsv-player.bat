@echo off
rem ---------------------------------------------------------------------------
rem  foxanimrip - MGSV: The Phantom Pain player locomotion
rem  SPDX-License-Identifier: MIT
rem
rem  Both player models, each exported twice:
rem
rem    mgsv-player\male\locomotion                 in place
rem    mgsv-player\male\locomotion-rootmotion      travelling
rem    mgsv-player\female\locomotion               in place
rem    mgsv-player\female\locomotion-rootmotion    travelling
rem
rem  In place is right for a retargetable Action library -- a walk cycle that
rem  wanders off is a nuisance to reuse. Travelling is right when the animation
rem  should actually move the character, or when you want to know the speed each
rem  clip was authored for.
rem
rem  Both models are exported separately even though they very likely share a
rem  skeleton. Clips are baked against the model that plays them, and the rig
rem  solve uses that model's bone positions, so two characters of different
rem  proportions do not necessarily produce identical output. Step 1 prints both
rem  bone counts: if they match and the clips turn out identical, keep one set
rem  and both models.
rem
rem  Put this file next to foxanimrip-cli.exe and double-click it.
rem ---------------------------------------------------------------------------

setlocal
cd /d "%~dp0"

rem == settings ===============================================================
set MALE=skl0_main0_def
set FEMALE=skl0_main0_def_f
set OUT=C:\rips\mgsv-player
set PACK=50

rem  Which clips count as locomotion:
rem    --grid        exact: only clips forming a detected movement grid.
rem    --locomotion  looser: any clip whose name contains a movement fragment.
rem
rem  --grid is verified against The Phantom Pain's player archive: it finds four
rem  complete 8-direction grids (walk and run, standing and crouched). Step 1
rem  prints them. Leave --locomotion for the wider sweep, or blank for everything.
set SELECT=--grid

rem  Animation archives. player2_resident is the campaign player set;
rem  mgoplayer_resident is Metal Gear Online's, which is larger.
set SETS=--mtar player2_resident --mtar mgoplayer_resident
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
if not exist "%REPORTS%" mkdir "%REPORTS%" 2>nul

echo.
echo ===========================================================
echo   1 of 5   Checks
echo ===========================================================
echo.
echo   The first run for each character searches every archive for its
echo   rig. That takes a few minutes once per model, then it is cached.
echo.

echo   Locomotion grids in player2_resident ...
"%TOOL%" --game tpp --list-grids player2_resident   > "%REPORTS%\grids-player2.txt" 2>&1
type "%REPORTS%\grids-player2.txt"

echo.
echo   Locomotion grids in mgoplayer_resident ...
"%TOOL%" --game tpp --list-grids mgoplayer_resident > "%REPORTS%\grids-mgo.txt" 2>&1
type "%REPORTS%\grids-mgo.txt"

echo.
echo   Checking %MALE% ...
"%TOOL%" --game tpp --character %MALE%   --why-mtar player2_resident   > "%REPORTS%\why-male-player2.txt" 2>&1
"%TOOL%" --game tpp --character %MALE%   --why-mtar mgoplayer_resident > "%REPORTS%\why-male-mgo.txt" 2>&1

echo   Checking %FEMALE% ...
"%TOOL%" --game tpp --character %FEMALE% --why-mtar player2_resident   > "%REPORTS%\why-female-player2.txt" 2>&1
"%TOOL%" --game tpp --character %FEMALE% --why-mtar mgoplayer_resident > "%REPORTS%\why-female-mgo.txt" 2>&1

echo.
echo   -- bones matched, male then female --
findstr /C:"bone(s) matched" "%REPORTS%\why-male-player2.txt"
findstr /C:"bone(s) matched" "%REPORTS%\why-female-player2.txt"

echo.
echo   If both lines show the same totals, the two models share a skeleton
echo   and their exported clips will be identical or nearly so -- in which
echo   case one clip set plus both models is enough, and you can skip half
echo   of what follows.
echo.
echo   If either says 0 bones matched, that model cannot play that archive
echo   and the model name at the top of this file is the thing to change.
echo.
echo   If the grid lists above are empty, set SELECT=--locomotion instead;
echo   it is currently %SELECT%.
echo.
pause

echo.
echo ===========================================================
echo   2 of 5   Male, in place
echo ===========================================================
echo.
"%TOOL%" --game tpp --character %MALE% %SETS% %SELECT% ^
         --export-model --dedupe --pack %PACK% ^
         --out "%OUT%\male\locomotion"
if errorlevel 1 goto :failed

echo.
echo ===========================================================
echo   3 of 5   Male, with root motion
echo ===========================================================
echo.
"%TOOL%" --game tpp --character %MALE% %SETS% %SELECT% --root-motion ^
         --export-model --dedupe --pack %PACK% ^
         --out "%OUT%\male\locomotion-rootmotion"
if errorlevel 1 goto :failed

echo.
echo ===========================================================
echo   4 of 5   Female, in place
echo ===========================================================
echo.
"%TOOL%" --game tpp --character %FEMALE% %SETS% %SELECT% ^
         --export-model --dedupe --pack %PACK% ^
         --out "%OUT%\female\locomotion"
if errorlevel 1 goto :failed

echo.
echo ===========================================================
echo   5 of 5   Female, with root motion
echo ===========================================================
echo.
"%TOOL%" --game tpp --character %FEMALE% %SETS% %SELECT% --root-motion ^
         --export-model --dedupe --pack %PACK% ^
         --out "%OUT%\female\locomotion-rootmotion"
if errorlevel 1 goto :failed

echo.
echo ===========================================================
echo   Done.  Everything is under %OUT%
echo ===========================================================
echo.
echo   male\locomotion                plays on the spot
echo   male\locomotion-rootmotion     travels
echo   female\locomotion              plays on the spot
echo   female\locomotion-rootmotion   travels
echo.
echo   Each folder has the model, its textures, one FBX per %PACK% clips and
echo   an index.tsv listing every clip with its frame and bone counts.
echo.
echo   Check one in-place folder against its root-motion twin in Blender:
echo   the same clip should stand still in one and move in the other. If
echo   both stand still, --root-motion did not take effect.
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
