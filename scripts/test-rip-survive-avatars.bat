@echo off
setlocal EnableExtensions EnableDelayedExpansion
rem ===========================================================================
rem  foxanimrip - Survive-native avatar heads/bodies.
rem  SPDX-License-Identifier: MIT
rem
rem  The avatar face presets (avf0/avm0/avm1 type0-7), base bodies (av*0_body0)
rem  and horn accessory exist in BOTH games: MGSV/MGO under Assets/tpp/chara/avm,
rem  and Metal Gear Survive under Assets/ssd/chara/avm. The MGO copies are on the
rem  MGO (skl0) skeleton and were ripped to rips\mgo-avatars; the Survive copies
rem  are on the SURVIVE skeleton -- so they line up with the Survive bodies
rem  (bsm0/bsf0) instead of drifting off at the neck.
rem
rem  test-rip-survive-chars.bat named 419 Survive models but never these, so this
rem  fills the gap: it rips the Survive avatar set into rips\survive-chars\models
rem  next to the rest of the Survive characters.
rem
rem  Each model is ripped on its own so one unknown name only skips itself; the
rem  count of names not found in Survive is reported at the end (some, like the
rem  second male head set avm1_*, may simply not exist in Survive).
rem
rem  Put this file next to foxanimrip-cli.exe and double-click it.
rem ===========================================================================

cd /d "%~dp0"
set "HERE=%~dp0"
set "TOOL=%HERE%foxanimrip-cli.exe"
if not exist "%TOOL%" set "TOOL=%HERE%..\foxanimrip-cli.exe"
set "LOGS=%HERE%test-logs"
set "OUT=%HERE%rips\survive-chars\models"
set MISS=0

rem  Edit if your Survive install lives elsewhere.
set "SURVIVE=E:\SteamLibrary\steamapps\common\METAL GEAR SURVIVE"

if not exist "%TOOL%" (
    echo cannot find foxanimrip-cli.exe beside this script
    pause
    exit /b 66
)
if not exist "%LOGS%" mkdir "%LOGS%"
set "LOG=%LOGS%\92-survchar-avatars.log"

"%TOOL%" --version > "%LOG%" 2>&1
echo rip Survive-native avatar heads/bodies -> rips\survive-chars\models
echo (detail in test-logs\92-survchar-avatars.log)
echo.

for %%N in (
  avf0_body0_def avf0_type0_def avf0_type1_def avf0_type2_def avf0_type3_def avf0_type4_def avf0_type5_def avf0_type6_def avf0_type7_def
  avm0_body0_def avm0_main4_arm_cov avm0_type0_def avm0_type1_def avm0_type2_def avm0_type3_def avm0_type4_def avm0_type5_def avm0_type6_def avm0_type7_def
  avm1_type0_def avm1_type1_def avm1_type2_def avm1_type3_def avm1_type4_def avm1_type5_def avm1_type6_def avm1_type7_def
  avm_hone_v00_cov avm_hone_v01_cov avm_hone_v02_cov
  avm_hair_a0_v0_cov avm_hair_a0_v1_cov avm_hair_b0_v0_cov avm_hair_b0_v1_cov avm_hair_c0_v0_cov avm_hair_c0_v1_cov
) do (
    echo [rip] %%N
    echo ===== %%N ===== >> "%LOG%"
    "%TOOL%" --game survive --root "%SURVIVE%" --character %%N --export-model --no-rig --skip-existing --out "%OUT%" >> "%LOG%" 2>&1
    if errorlevel 1 (
        set /a MISS+=1
        echo   ^(not present in Survive, or failed^) >> "%LOG%"
    )
)

echo finished, %MISS% name^(s^) not found/failed >> "%LOG%"
echo.
echo done - %MISS% name(s) not found in Survive (that's expected for any the
echo creator doesn't include). In the Model Browser: re-Scan, set Game to
echo "Metal Gear Survive" and Category to "Head".
echo tell Claude "read" to have the log checked.
pause
exit /b 0
