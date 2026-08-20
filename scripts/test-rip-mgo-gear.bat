@echo off
setlocal EnableExtensions
rem SPDX-License-Identifier: MIT
rem
rem test-rip-mgo-gear.bat -- every MGO equipment model, unattended.
rem
rem All 177 equipment models (hats, chest gear, heads, glasses, suits,
rem outfits, DLC gear), generated from the inventory tables. Safe to stop
rem and re-run: models already exported are skipped, so an interrupted
rem batch resumes where it left off instead of repeating.
rem
rem Gear has no rig of its own, so the rig search -- the slowest step of
rem the first version of this script -- is skipped outright, and each
rem batch opens the game archives once rather than once per model.

set "HERE=%~dp0"
set "TOOL=%HERE%foxanimrip-cli.exe"

rem If an update was delivered while the tool was running, swap it in now --
rem or run the new copy directly if the old one is still locked.
if exist "%HERE%foxanimrip-cli-new.exe" (
    del "%HERE%foxanimrip-cli.exe" 2>nul
    if not exist "%HERE%foxanimrip-cli.exe" (
        ren "%HERE%foxanimrip-cli-new.exe" "foxanimrip-cli.exe"
    ) else (
        set "TOOL=%HERE%foxanimrip-cli-new.exe"
    )
)
set "LOGS=%HERE%test-logs"
set "OUT=%HERE%rips\mgo-gear"
set ERRORS=0

if not exist "%TOOL%" (
    echo cannot find foxanimrip-cli.exe beside this script
    pause
    exit /b 66
)
if not exist "%LOGS%" mkdir "%LOGS%"
del /q "%LOGS%\5*-gear-*.log" "%LOGS%\60-gear-*.log" 2>nul

echo test-rip-mgo-gear: 10 steps, already-exported models are skipped
"%TOOL%" --version > "%LOGS%\50-gear-run.log" 2>&1

echo [1/10] gear models Inf0_main0_def0 .. gls1_main0_def...
echo [cmd] batch 1: Inf0_main0_def0 .. gls1_main0_def > "%LOGS%\51-gear-models.log"
"%TOOL%" --game tpp --character Inf0_main0_def0,Inf0_main0_def_f,Inf1_main0_def0,Inf1_main0_def_f,cmn0_chst0_def_f,cmn0_main0_def,cmn0_main0_def_f,cmn1_chst1_def,cmn1_chst1_def_f,cmn1_main0_def,cmn1_main0_def_f,dlf0_main0_def_f,dlf1_main0_def,dlg0_main0_def_f,dlg1_main0_def,dlh0_main0_def_f,dlh1_main0_def,gls0_main1_def,gls0_main1_def_f,gls1_main0_def --export-model --no-rig --skip-existing --out "%OUT%\models" >> "%LOGS%\51-gear-models.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [2/10] gear models gls1_main0_def_f .. hat15_main0_def_f...
echo [cmd] batch 2: gls1_main0_def_f .. hat15_main0_def_f > "%LOGS%\52-gear-models.log"
"%TOOL%" --game tpp --character gls1_main0_def_f,gls2_main0_def,gls2_main0_def_f,gls3_main0_def,gls3_main0_def_f,gls4_main0_def,gls4_main0_def_f,gls5_main0_def,gls5_main0_def_f,hat0_main0_def,hat0_main0_def_f,hat10_main0_def,hat10_main0_def_f,hat11_main0_def,hat12_main0_def,hat13_main0_def,hat13_main0_def_f,hat14_main0_def,hat15_main0_def,hat15_main0_def_f --export-model --no-rig --skip-existing --out "%OUT%\models" >> "%LOGS%\52-gear-models.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [3/10] gear models hat16_main0_def .. hat2_main0_def_f...
echo [cmd] batch 3: hat16_main0_def .. hat2_main0_def_f > "%LOGS%\53-gear-models.log"
"%TOOL%" --game tpp --character hat16_main0_def,hat16_main0_def_f,hat17_main0_def,hat17_main0_def_f,hat18_main0_def,hat18_main0_def_f,hat19_main0_def,hat19_main0_def_f,hat1_main0_def,hat1_main0_def_f,hat20_main0_def,hat20_main0_def_f,hat21_main0_def,hat21_main0_def_f,hat22_main0_def,hat22_main0_def_f,hat23_main0_def,hat23_main0_def_f,hat2_main0_def,hat2_main0_def_f --export-model --no-rig --skip-existing --out "%OUT%\models" >> "%LOGS%\53-gear-models.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [4/10] gear models hat3_main0_def .. inf4_main0_def_f...
echo [cmd] batch 4: hat3_main0_def .. inf4_main0_def_f > "%LOGS%\54-gear-models.log"
"%TOOL%" --game tpp --character hat3_main0_def,hat3_main0_def_f,hat4_main0_def,hat4_main0_def_f,hat5_main1_def,hat6_main0_def,hat7_main0_def,hat7_main0_def_f,hat8_main0_def,hat9_main1_def,icl0_main0_def,icl0_main0_def_f,icl1_main0_def,icl1_main0_def_f,inf2_main0_def,inf2_main0_def_f,inf3_main0_def,inf3_main0_def_f,inf4_main0_def,inf4_main0_def_f --export-model --no-rig --skip-existing --out "%OUT%\models" >> "%LOGS%\54-gear-models.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [5/10] gear models inf5_main0_def .. ins0_mask0_def_f...
echo [cmd] batch 5: inf5_main0_def .. ins0_mask0_def_f > "%LOGS%\55-gear-models.log"
"%TOOL%" --game tpp --character inf5_main0_def,inf5_main0_def_f,inf6_main0_def,inf6_main0_def_f,inf7_main0_def,inf7_main0_def_f,inh0_main0_def,inh0_main0_def_f,inh1_main0_def,inh1_main0_def_f,inh2_main0_def,inh2_main0_def_f,inh3_main0_def,inh3_main0_def_f,inh4_main0_def,inh4_main0_def_f,ins0_main0_def,ins0_main0_def_f,ins0_mask0_def,ins0_mask0_def_f --export-model --no-rig --skip-existing --out "%OUT%\models" >> "%LOGS%\55-gear-models.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [6/10] gear models ins1_main0_def .. rec5_main0_def_f...
echo [cmd] batch 6: ins1_main0_def .. rec5_main0_def_f > "%LOGS%\56-gear-models.log"
"%TOOL%" --game tpp --character ins1_main0_def,ins1_main0_def_f,oce0_main1_def,qui0_main0_mgo,rcl0_main0_def,rcl0_main0_def_f,rcl1_main0_def,rcl1_main0_def_f,rec0_main0_def,rec0_main0_def_f,rec1_main0_def,rec1_main0_def_f,rec2_main0_def,rec2_main0_def_f,rec3_main0_def,rec3_main0_def_f,rec4_main0_def,rec4_main0_def_f,rec5_main0_def,rec5_main0_def_f --export-model --no-rig --skip-existing --out "%OUT%\models" >> "%LOGS%\56-gear-models.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [7/10] gear models rec6_main0_def .. res1_main0_def_f...
echo [cmd] batch 7: rec6_main0_def .. res1_main0_def_f > "%LOGS%\57-gear-models.log"
"%TOOL%" --game tpp --character rec6_main0_def,rec6_main0_def_f,rec7_main0_def,rec7_main0_def_f,reh0_main0_def,reh0_main0_def_f,reh1_main0_def,reh1_main0_def_f,reh2_main0_def,reh2_main0_def_f,reh3_main0_def,reh3_main0_def_f,reh4_main0_def,reh4_main0_def_f,res0_main0_def,res0_main0_def_f,res0_mask0_def,res0_mask0_def_f,res1_main0_def,res1_main0_def_f --export-model --no-rig --skip-existing --out "%OUT%\models" >> "%LOGS%\57-gear-models.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [8/10] gear models sna0_main4_def .. tec7_main0_def...
echo [cmd] batch 8: sna0_main4_def .. tec7_main0_def > "%LOGS%\58-gear-models.log"
"%TOOL%" --game tpp --character sna0_main4_def,tcl0_main0_def,tcl0_main0_def_f,tcl1_main0_def,tcl1_main0_def_f,tec0_main0_def0,tec0_main0_def_f,tec1_main0_def0,tec1_main0_def_f,tec2_main0_def,tec2_main0_def_f,tec3_main0_def,tec3_main0_def_f,tec4_main0_def,tec4_main0_def_f,tec5_main0_def,tec5_main0_def_f,tec6_main0_def,tec6_main0_def_f,tec7_main0_def --export-model --no-rig --skip-existing --out "%OUT%\models" >> "%LOGS%\58-gear-models.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [9/10] gear models tec7_main0_def_f .. tes1_main0_def_f...
echo [cmd] batch 9: tec7_main0_def_f .. tes1_main0_def_f > "%LOGS%\59-gear-models.log"
"%TOOL%" --game tpp --character tec7_main0_def_f,teh0_main0_def,teh0_main0_def_f,teh1_main0_def,teh1_main0_def_f,teh2_main0_def,teh2_main0_def_f,teh3_main0_def,teh3_main0_def_f,teh4_main0_def,teh4_main0_def_f,tes0_helm0_def,tes0_helm0_def_f,tes0_main0_def,tes0_main0_def_f,tes1_main0_def,tes1_main0_def_f --export-model --no-rig --skip-existing --out "%OUT%\models" >> "%LOGS%\59-gear-models.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [10/10] DLC gear customisation textures...
echo [cmd] --rip-variations tpp/fova/chara/dl > "%LOGS%\60-gear-fova.log"
"%TOOL%" --game tpp --rip-variations tpp/fova/chara/dl --out "%OUT%\fova" >> "%LOGS%\60-gear-fova.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo finished %date% %time%, %ERRORS% problem(s) >> "%LOGS%\50-gear-run.log"
echo.
echo done - %ERRORS% problem(s). Output in rips\mgo-gear\, logs in test-logs\
echo tell Claude "read" to have the results checked
pause
