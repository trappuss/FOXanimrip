@echo off
setlocal EnableExtensions
rem SPDX-License-Identifier: MIT
rem
rem test-rip-avatars.bat -- every MGO avatar asset, unattended.
rem
rem What comes out, under rips\mgo-avatars\ beside this file:
rem   models\<name>\   45 models: both avatar genders (bodies, all 8+8+8 heads,
rem                    every hair piece), the skl0 base skeletons, the DLC
rem                    characters, and the customisation stage -- each as FBX
rem                    with textures, rig manifest and source fmdl.
rem   fova\textures\   the customisation textures the inventory could only
rem                    name: all five skin tones per garment, hair colours,
rem                    eyes, chest gear -- pulled out of the archives by code.
rem   fova\ripped-files.tsv  which variation owns which file, failures included.
rem
rem Logs go to test-logs\4x-*.log for Claude to read. Re-run any time; models
rem are re-exported, so delete rips\mgo-avatars first if you want it clean.

set "HERE=%~dp0"
set "TOOL=%HERE%foxanimrip-cli.exe"
set "LOGS=%HERE%test-logs"
set "OUT=%HERE%rips\mgo-avatars"
set ERRORS=0

if not exist "%TOOL%" (
    echo cannot find foxanimrip-cli.exe beside this script
    pause
    exit /b 66
)
if not exist "%LOGS%" mkdir "%LOGS%"
del /q "%LOGS%\4*-rip-*.log" 2>nul

echo test-rip-avatars: 6 steps, detail in test-logs\
"%TOOL%" --version > "%LOGS%\40-rip-run.log" 2>&1

echo [1/6] female avatar: body and heads...
echo [cmd] avf0 body + type0-7 > "%LOGS%\41-rip-avf0.log"
"%TOOL%" --game tpp --character avf0_body0_def,avf0_type0_def,avf0_type1_def,avf0_type2_def,avf0_type3_def,avf0_type4_def,avf0_type5_def,avf0_type6_def,avf0_type7_def --export-model --out "%OUT%\models" >> "%LOGS%\41-rip-avf0.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [2/6] male avatar: bodies and heads...
echo [cmd] avm0 body/arm + type0-7 > "%LOGS%\42-rip-avm0.log"
"%TOOL%" --game tpp --character avm0_body0_def,avm0_main4_arm_cov,avm0_type0_def,avm0_type1_def,avm0_type2_def,avm0_type3_def,avm0_type4_def,avm0_type5_def,avm0_type6_def,avm0_type7_def --export-model --out "%OUT%\models" >> "%LOGS%\42-rip-avm0.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [3/6] male avatar second set of heads...
echo [cmd] avm1 type0-7 > "%LOGS%\43-rip-avm1.log"
"%TOOL%" --game tpp --character avm1_type0_def,avm1_type1_def,avm1_type2_def,avm1_type3_def,avm1_type4_def,avm1_type5_def,avm1_type6_def,avm1_type7_def --export-model --out "%OUT%\models" >> "%LOGS%\43-rip-avm1.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [4/6] hair pieces...
echo [cmd] all hair + hone > "%LOGS%\44-rip-hair.log"
"%TOOL%" --game tpp --character avf_hair_a0_v0_cov,avf_hair_b0_v0_cov,avf_hair_c0_v0_cov,avf_hair_d0_v0_cov,avm_hair_a0_v0_cov,avm_hair_a0_v1_cov,avm_hair_b0_v0_cov,avm_hair_b0_v1_cov,avm_hair_c0_v0_cov,avm_hair_c0_v1_cov,avm_hone_v00_cov,avm_hone_v01_cov,avm_hone_v02_cov --export-model --out "%OUT%\models" >> "%LOGS%\44-rip-hair.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [5/6] base skeletons, DLC characters, stage...
echo [cmd] skl0 both + dlf0 + qui0 + avr_stage > "%LOGS%\45-rip-base.log"
"%TOOL%" --game tpp --character skl0_main0_def,skl0_main0_def_f,dlf0_main0_def_f,qui0_main0_mgo,avr_stage --export-model --out "%OUT%\models" >> "%LOGS%\45-rip-base.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [6/6] customisation textures: skins, hair colours, eyes, gear...
echo [cmd] --rip-variations mgo/fova/chara > "%LOGS%\46-rip-fova.log"
"%TOOL%" --game tpp --rip-variations mgo/fova/chara --out "%OUT%\fova" >> "%LOGS%\46-rip-fova.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo finished %date% %time%, %ERRORS% problem(s) >> "%LOGS%\40-rip-run.log"
echo.
echo done - %ERRORS% problem(s). Output in rips\mgo-avatars\, logs in test-logs\
echo tell Claude "read" to have the results checked
pause
