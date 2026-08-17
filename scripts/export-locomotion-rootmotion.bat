@echo off
rem ---------------------------------------------------------------------------
rem  foxanimrip - player locomotion export, WITH root motion
rem  SPDX-License-Identifier: MIT
rem
rem  Exports the player locomotion grids for MGSV: The Phantom Pain (male and
rem  female) and Ground Zeroes, each into its own folder, with the character
rem  model and textures beside the clips.
rem
rem  ROOT MOTION IS ON. Clips keep the distance the character actually travels,
rem  so a walk cycle walks forward instead of on the spot. That is what you want
rem  when the animation should move the character, or when you want to know the
rem  speed each clip was authored for.
rem
rem  It is NOT what you want for a retargetable Action library -- a walk that
rem  wanders off is a nuisance to reuse. For that, remove --root-motion from the
rem  three commands below.
rem
rem  Put this file next to foxanimrip-cli.exe and double-click it.
rem  It is meant to be edited: the settings are at the top and each export is
rem  written out in full rather than hidden in a subroutine.
rem ---------------------------------------------------------------------------

setlocal
cd /d "%~dp0"

rem == settings ===============================================================
set OUT=C:\rips
set TPP_FEMALE=skl0_main0_def_f
set TPP_MALE=skl0_main0_def
set GZ_MODEL=sna2_main0_def
set PACK=50
rem ===========================================================================

set TOOL=%~dp0foxanimrip-cli.exe
set REPORTS=%OUT%\_reports

if not exist "%TOOL%" (
    echo.
    echo   Cannot find foxanimrip-cli.exe next to this script.
    echo   Move this .bat into the same folder as the tool.
    goto :end
)
if not exist "%REPORTS%" mkdir "%REPORTS%" 2>nul

echo.
echo ===========================================================
echo   1 of 3   Checks, before exporting anything
echo ===========================================================
echo.
echo   The first run for each character searches every archive for
echo   its rig. That takes a few minutes once, then it is cached.
echo.

echo   Finding the locomotion grids ...
"%TOOL%" --game tpp --list-grids player2_resident  > "%REPORTS%\grids-player2.txt" 2>&1

echo   Checking %TPP_FEMALE% ...
"%TOOL%" --game tpp --character %TPP_FEMALE% --why-mtar player2_resident      > "%REPORTS%\why-female-player2.txt" 2>&1
"%TOOL%" --game tpp --character %TPP_FEMALE% --why-mtar mgoplayer_resident    > "%REPORTS%\why-female-mgo.txt" 2>&1

echo   Checking %TPP_MALE% ...
"%TOOL%" --game tpp --character %TPP_MALE% --why-mtar player2_resident        > "%REPORTS%\why-male-player2.txt" 2>&1
"%TOOL%" --game tpp --character %TPP_MALE% --why-mtar mgoplayer_resident      > "%REPORTS%\why-male-mgo.txt" 2>&1

echo.
echo   Reports are in %REPORTS%
echo.
echo   READ THE why-*.txt FILES NOW.
echo   Each says how many bones matched. If one says 0, that character
echo   cannot play that archive and the export will write an empty
echo   folder -- change the model name at the top of this file rather
echo   than letting it run.
echo.
pause

echo.
echo ===========================================================
echo   2 of 3   The Phantom Pain
echo ===========================================================

echo.
echo   -- female: %TPP_FEMALE%
echo.
"%TOOL%" --game tpp --character %TPP_FEMALE% ^
         --mtar player2_resident --mtar mgoplayer_resident ^
         --grid --root-motion ^
         --export-model --dedupe --pack %PACK% ^
         --out "%OUT%\tpp-female-rootmotion"
if errorlevel 1 goto :failed

echo.
echo   -- male: %TPP_MALE%
echo.
"%TOOL%" --game tpp --character %TPP_MALE% ^
         --mtar player2_resident --mtar mgoplayer_resident ^
         --grid --root-motion ^
         --export-model --dedupe --pack %PACK% ^
         --out "%OUT%\tpp-male-rootmotion"
if errorlevel 1 goto :failed

echo.
echo ===========================================================
echo   3 of 3   Ground Zeroes
echo ===========================================================
echo.
echo   -- %GZ_MODEL%
echo.
"%TOOL%" --game gz --character %GZ_MODEL% ^
         --mtar TppGzPlayer_layers ^
         --grid --root-motion ^
         --export-model --dedupe --pack %PACK% ^
         --out "%OUT%\gz-player-rootmotion"
if errorlevel 1 goto :failed

echo.
echo ===========================================================
echo   Done.  Everything is under %OUT%
echo ===========================================================
echo.
echo   Each folder holds the model, its textures, one FBX per %PACK%
echo   clips, and an index.tsv listing every clip with its frame and
echo   bone counts.
echo.
echo   Import ONE folder into Blender and look at it before trusting
echo   the rest. With root motion on the character should travel
echo   across the scene rather than walking on the spot -- if it walks
echo   on the spot, --root-motion did not take effect.
echo.
goto :end

:failed
echo.
echo   ** An export failed. Nothing after that point ran.
echo   ** The messages above say why; the reports in %REPORTS%
echo   ** usually say it more plainly.
echo.

:end
echo.
pause
endlocal
