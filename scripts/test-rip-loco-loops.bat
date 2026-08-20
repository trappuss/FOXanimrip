@echo off
setlocal EnableExtensions EnableDelayedExpansion
rem ===========================================================================
rem  foxanimrip - rip the player gait loops for the Blender Locomotion Lab.
rem  SPDX-License-Identifier: MIT
rem
rem  Rips every walk/run/jog/dash LOOP clip against the real player models so
rem  the IK legs are solved (the all-anims tree exported these through a stub
rem  skeleton, which left the legs unanimated -- those copies are junk).
rem  Loose one-clip FBXs land in rips\loco-loops\<who>\<set>\<clip>.fbx; point
rem  the add-on's Locomotion Lab "Clips" field at rips\loco-loops.
rem
rem  Put this file next to foxanimrip-cli.exe and double-click it.
rem ===========================================================================
cd /d "%~dp0"
set "TOOL=%~dp0foxanimrip-cli.exe"
if not exist "%TOOL%" set "TOOL=%~dp0..\foxanimrip-cli.exe"
set "OUT=%~dp0rips\loco-loops"
set "LOGS=%~dp0test-logs"
set ERRORS=0
if not exist "%TOOL%" ( echo cannot find foxanimrip-cli.exe & pause & exit /b 66 )
if not exist "%LOGS%" mkdir "%LOGS%"

set "TPP=E:\SteamLibrary\steamapps\common\MGS_TPP"
set "SURVIVE=E:\SteamLibrary\steamapps\common\METAL GEAR SURVIVE"

rem The loop clips only: every *_wk_lp / _rn_lp / _jg_lp / _dh_lp variant,
rem including the eight-direction and slope sets for the 2D blendspace later.
set "LOOPS=_wk_lp,_rn_lp,_jg_lp,_dh_lp"

rem Broken copies from the all-anims tree (legs unanimated) - retire them so
rem the Lab cannot pick them up by mistake.
if exist "%OUT%\snapnon_s_wk_lp_vr1_l.fbx" (
  if not exist "%~dp0_to_delete\broken-loco-loops" mkdir "%~dp0_to_delete\broken-loco-loops"
  move /y "%OUT%\snapnon_*.fbx" "%~dp0_to_delete\broken-loco-loops\" >nul 2>&1
)

"%TOOL%" --version > "%LOGS%\94-loco-loops.log" 2>&1

echo [1/3] TPP player loops (skl0_main0_def)...
"%TOOL%" --root "%TPP%" --game tpp --character skl0_main0_def --mtar player2_resident --mtar mgoplayer_resident --filter-any "%LOOPS%" --out "%OUT%\tpp-player" >> "%LOGS%\94-loco-loops.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [2/3] MGO avatar loops (avf0_body0_def)...
"%TOOL%" --root "%TPP%" --game tpp --character avf0_body0_def --mtar mgoplayer_resident --filter-any "%LOOPS%" --out "%OUT%\mgo-avatar" >> "%LOGS%\94-loco-loops.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [3/3] Survive player loops (bsf0_main0_def)...
"%TOOL%" --root "%SURVIVE%" --game survive --character bsf0_main0_def --mtar SsdPlayer_layers --filter-any "%LOOPS%" --out "%OUT%\survive-player" >> "%LOGS%\94-loco-loops.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo finished, %ERRORS% problem step(s) >> "%LOGS%\94-loco-loops.log"
echo.
echo done - %ERRORS% problem step(s). Loops in %OUT%\
echo In Blender: FoxBrowser sidebar, Locomotion Lab panel --
echo   Clips = rips\loco-loops     Measured params = rips\locomotion-params
echo   select the player armature, Build Locomotion Lab, pick a gait, play.
echo tell Claude "read" if anything looks off.
pause
exit /b %ERRORS%
