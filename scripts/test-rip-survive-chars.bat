@echo off
setlocal EnableExtensions
rem SPDX-License-Identifier: MIT
rem
rem test-rip-survive-chars.bat -- every Survive character asset, unattended.
rem
rem All 419 models under Assets/ssd/chara -- the body/arm/leg/head/up_armor/
rem chest_rig/body/hats customisation parts, plus bosses, zombies, kaiju,
rem walker gear and NPCs -- generated from the inventory, not hand-picked.
rem The two base player skeletons (bsm0/bsf0) are left to the locomotion
rem script, which exports them rigged.
rem
rem Gear and parts carry no rig, so the rig search is skipped (--no-rig) and
rem already-exported models are skipped (--skip-existing): safe to stop and
rem re-run. The last step rips the ssd customisation textures -- every skin
rem tone per part and the rest of the far deeper Survive character creator.

set "HERE=%~dp0"
set "TOOL=%HERE%foxanimrip-cli.exe"
set "SURVIVE=E:\SteamLibrary\steamapps\common\METAL GEAR SURVIVE"
set "LOGS=%HERE%test-logs"
set "OUT=%HERE%rips\survive-chars"
set ERRORS=0

if exist "%HERE%foxanimrip-cli-new.exe" (
    del "%HERE%foxanimrip-cli.exe" 2>nul
    if not exist "%HERE%foxanimrip-cli.exe" ( ren "%HERE%foxanimrip-cli-new.exe" "foxanimrip-cli.exe" ) else ( set "TOOL=%HERE%foxanimrip-cli-new.exe" )
)
if not exist "%TOOL%" ( echo cannot find foxanimrip-cli.exe beside this script & pause & exit /b 66 )
if not exist "%LOGS%" mkdir "%LOGS%"
del /q "%LOGS%\8*-survchar-*.log" "%LOGS%\90-survchar-*.log" 2>nul

echo test-rip-survive-chars: 22 steps, already-exported models are skipped
"%TOOL%" --version > "%LOGS%\80-survchar-run.log" 2>&1

echo [1/22] models arf0_main0_def .. arf26_main0_def...
echo [cmd] batch 1: arf0_main0_def .. arf26_main0_def > "%LOGS%\801-survchar-models.log"
"%TOOL%" --game survive --root "%SURVIVE%" --character arf0_main0_def,arf10_main0_def,arf11_main0_def,arf12_main0_def,arf13_main0_def,arf14_main0_def,arf15_main0_def,arf16_main0_def,arf17_main0_def,arf18_main0_def,arf18_main1_def,arf1_main0_def,arf21_main0_def,arf22_main0_def,arf22_main1_def,arf23_main0_def,arf23_main1_def,arf24_main0_def,arf24_main1_def,arf26_main0_def --export-model --no-rig --skip-existing --out "%OUT%\models" >> "%LOGS%\801-survchar-models.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [2/22] models arf2_main0_def .. arm18_main0_def...
echo [cmd] batch 2: arf2_main0_def .. arm18_main0_def > "%LOGS%\802-survchar-models.log"
"%TOOL%" --game survive --root "%SURVIVE%" --character arf2_main0_def,arf3_main0_def,arf4_main0_def,arf5_main0_def,arf6_main0_def,arf7_main0_def,arf8_main0_def,arf90_main0_def,arf9_main0_def,arf9_main1_def,arm0_main0_def,arm10_main0_def,arm11_main0_def,arm12_main0_def,arm13_main0_def,arm14_main0_def,arm15_main0_def,arm16_main0_def,arm17_main0_def,arm18_main0_def --export-model --no-rig --skip-existing --out "%OUT%\models" >> "%LOGS%\802-survchar-models.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [3/22] models arm18_main1_def .. arm9_main0_def...
echo [cmd] batch 3: arm18_main1_def .. arm9_main0_def > "%LOGS%\803-survchar-models.log"
"%TOOL%" --game survive --root "%SURVIVE%" --character arm18_main1_def,arm1_main0_def,arm21_main0_def,arm22_main0_def,arm22_main1_def,arm23_main0_def,arm23_main1_def,arm24_main0_def,arm24_main1_def,arm26_main0_def,arm2_main0_def,arm3_main0_def,arm4_main0_def,arm5_main0_def,arm6_main0_def,arm7_main0_def,arm8_main0_def,arm90_main0_def,arm91_main0_def,arm9_main0_def --export-model --no-rig --skip-existing --out "%OUT%\models" >> "%LOGS%\803-survchar-models.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [4/22] models arm9_main1_def .. bdf3_main1_def...
echo [cmd] batch 4: arm9_main1_def .. bdf3_main1_def > "%LOGS%\804-survchar-models.log"
"%TOOL%" --game survive --root "%SURVIVE%" --character arm9_main1_def,avf_hair_a0_v0_cov,avf_hair_b0_v0_cov,avf_hair_c0_v0_cov,avf_hair_d0_v0_cov,bdf0_main0_def,bdf10_main0_def,bdf11_main0_def,bdf12_main0_def,bdf13_main0_def,bdf14_main0_def,bdf15_main0_def,bdf1_main0_def,bdf1_main1_def,bdf1_main2_def,bdf2_main0_def,bdf2_main1_def,bdf2_main2_def,bdf3_main0_def,bdf3_main1_def --export-model --no-rig --skip-existing --out "%OUT%\models" >> "%LOGS%\804-survchar-models.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [5/22] models bdf3_main2_def .. bdm2_main0_def...
echo [cmd] batch 5: bdf3_main2_def .. bdm2_main0_def > "%LOGS%\805-survchar-models.log"
"%TOOL%" --game survive --root "%SURVIVE%" --character bdf3_main2_def,bdf4_main0_def,bdf4_main1_def,bdf4_main2_def,bdf5_main0_def,bdf6_main0_def,bdf7_main0_def,bdf8_main0_def,bdf9_main0_def,bdm0_main0_def,bdm10_main0_def,bdm11_main0_def,bdm12_main0_def,bdm13_main0_def,bdm14_main0_def,bdm15_main0_def,bdm1_main0_def,bdm1_main1_def,bdm1_main2_def,bdm2_main0_def --export-model --no-rig --skip-existing --out "%OUT%\models" >> "%LOGS%\805-survchar-models.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [6/22] models bdm2_main1_def .. bss0_rckt0_cov...
echo [cmd] batch 6: bdm2_main1_def .. bss0_rckt0_cov > "%LOGS%\806-survchar-models.log"
"%TOOL%" --game survive --root "%SURVIVE%" --character bdm2_main1_def,bdm2_main2_def,bdm3_main0_def,bdm3_main1_def,bdm3_main2_def,bdm4_main0_def,bdm4_main1_def,bdm4_main2_def,bdm5_main0_def,bdm6_main0_def,bdm7_main0_def,bdm8_main0_def,bdm9_main0_def,bss0_hand4_def,bss0_main0_def,bss0_main0_sta,bss0_main1_sta,bss0_main2_sta,bss0_main3_sta,bss0_rckt0_cov --export-model --no-rig --skip-existing --out "%OUT%\models" >> "%LOGS%\806-survchar-models.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [7/22] models bss1_main0_def .. hat30_main0_def...
echo [cmd] batch 7: bss1_main0_def .. hat30_main0_def > "%LOGS%\807-survchar-models.log"
"%TOOL%" --game survive --root "%SURVIVE%" --character bss1_main0_def,bss1_main0_sta,bss1_main1_sta,bss1_main2_sta,bss1_main3_sta,bss1_main4_sta,bss1_main5_sta,bss1_main6_sta,bss1_main7_sta,dmc0_main0_def,dmc1_main1_def,eng0_main0_def,gls4_main0_def,gls4_main0_def_f,gnt0_main0_def,hat13_main0_def,hat13_main0_def_f,hat21_main0_def,hat21_main0_def_f,hat30_main0_def --export-model --no-rig --skip-existing --out "%OUT%\models" >> "%LOGS%\807-survchar-models.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [8/22] models hat31_main0_def .. hdf25_main0_def...
echo [cmd] batch 8: hat31_main0_def .. hdf25_main0_def > "%LOGS%\808-survchar-models.log"
"%TOOL%" --game survive --root "%SURVIVE%" --character hat31_main0_def,hat31_main0_def_f,hat9_main1_def,hdf0_main0_def,hdf10_main0_def,hdf11_main0_def,hdf12_main0_def,hdf13_main0_def,hdf14_main0_def,hdf15_main0_def,hdf16_main0_def,hdf17_main0_def,hdf18_main0_def,hdf19_main0_def,hdf1_main0_def,hdf20_main0_def,hdf20_main1_def,hdf21_main0_def,hdf24_main0_def,hdf25_main0_def --export-model --no-rig --skip-existing --out "%OUT%\models" >> "%LOGS%\808-survchar-models.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [9/22] models hdf26_main0_def .. hdm11_main0_def...
echo [cmd] batch 9: hdf26_main0_def .. hdm11_main0_def > "%LOGS%\809-survchar-models.log"
"%TOOL%" --game survive --root "%SURVIVE%" --character hdf26_main0_def,hdf26_main1_def,hdf27_main0_def,hdf27_main1_def,hdf28_main0_def,hdf28_main1_def,hdf2_main0_def,hdf30_main0_def,hdf31_main0_def,hdf3_main0_def,hdf4_main0_def,hdf5_main0_def,hdf6_main0_def,hdf7_main0_def,hdf7_main1_def,hdf8_main0_def,hdf9_main0_def,hdm0_main0_def,hdm10_main0_def,hdm11_main0_def --export-model --no-rig --skip-existing --out "%OUT%\models" >> "%LOGS%\809-survchar-models.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [10/22] models hdm12_main0_def .. hdm28_main1_def...
echo [cmd] batch 10: hdm12_main0_def .. hdm28_main1_def > "%LOGS%\810-survchar-models.log"
"%TOOL%" --game survive --root "%SURVIVE%" --character hdm12_main0_def,hdm13_main0_def,hdm14_main0_def,hdm15_main0_def,hdm16_main0_def,hdm17_main0_def,hdm18_main0_def,hdm19_main0_def,hdm1_main0_def,hdm20_main0_def,hdm20_main1_def,hdm21_main0_def,hdm24_main0_def,hdm25_main0_def,hdm26_main0_def,hdm26_main1_def,hdm27_main0_def,hdm27_main1_def,hdm28_main0_def,hdm28_main1_def --export-model --no-rig --skip-existing --out "%OUT%\models" >> "%LOGS%\810-survchar-models.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [11/22] models hdm2_main0_def .. kij0_main0_cov...
echo [cmd] batch 11: hdm2_main0_def .. kij0_main0_cov > "%LOGS%\811-survchar-models.log"
"%TOOL%" --game survive --root "%SURVIVE%" --character hdm2_main0_def,hdm30_main0_def,hdm31_main0_def,hdm3_main0_def,hdm4_main0_def,hdm5_main0_def,hdm6_main0_def,hdm7_main0_def,hdm7_main1_def,hdm8_main0_def,hdm90_main0_def,hdm91_main0_def,hdm92_main0_def,hdm9_main0_def,isc0_main0_def,isc1_main1_def,isc2_main0_def,isc3_main0_def,kij0_head0_def,kij0_main0_cov --export-model --no-rig --skip-existing --out "%OUT%\models" >> "%LOGS%\811-survchar-models.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [12/22] models kij0_main0_def .. lgf22_main0_def...
echo [cmd] batch 12: kij0_main0_def .. lgf22_main0_def > "%LOGS%\812-survchar-models.log"
"%TOOL%" --game survive --root "%SURVIVE%" --character kij0_main0_def,kij0_main0_sta,kij0_main1_def,kij0_main1_sta,kij0_main2_sta,kij0_main3_def,lgf0_main0_def,lgf10_main0_def,lgf11_main0_def,lgf12_main0_def,lgf13_main0_def,lgf14_main0_def,lgf15_main0_def,lgf16_main0_def,lgf17_main0_def,lgf18_main0_def,lgf19_main0_def,lgf19_main1_def,lgf1_main0_def,lgf22_main0_def --export-model --no-rig --skip-existing --out "%OUT%\models" >> "%LOGS%\812-survchar-models.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [13/22] models lgf22_main1_def .. lgm13_main0_def...
echo [cmd] batch 13: lgf22_main1_def .. lgm13_main0_def > "%LOGS%\813-survchar-models.log"
"%TOOL%" --game survive --root "%SURVIVE%" --character lgf22_main1_def,lgf23_main0_def,lgf23_main1_def,lgf24_main0_def,lgf24_main1_def,lgf26_main0_def,lgf2_main0_def,lgf3_main0_def,lgf4_main0_def,lgf5_main0_def,lgf5_main1_def,lgf6_main0_def,lgf7_main0_def,lgf8_main0_def,lgf9_main0_def,lgm0_main0_def,lgm10_main0_def,lgm11_main0_def,lgm12_main0_def,lgm13_main0_def --export-model --no-rig --skip-existing --out "%OUT%\models" >> "%LOGS%\813-survchar-models.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [14/22] models lgm14_main0_def .. lgm5_main1_def...
echo [cmd] batch 14: lgm14_main0_def .. lgm5_main1_def > "%LOGS%\814-survchar-models.log"
"%TOOL%" --game survive --root "%SURVIVE%" --character lgm14_main0_def,lgm15_main0_def,lgm16_main0_def,lgm17_main0_def,lgm18_main0_def,lgm19_main0_def,lgm19_main1_def,lgm1_main0_def,lgm22_main0_def,lgm22_main1_def,lgm23_main0_def,lgm23_main1_def,lgm24_main0_def,lgm24_main1_def,lgm26_main0_def,lgm2_main0_def,lgm3_main0_def,lgm4_main0_def,lgm5_main0_def,lgm5_main1_def --export-model --no-rig --skip-existing --out "%OUT%\models" >> "%LOGS%\814-survchar-models.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [15/22] models lgm6_main0_def .. rgf13_main0_def...
echo [cmd] batch 15: lgm6_main0_def .. rgf13_main0_def > "%LOGS%\815-survchar-models.log"
"%TOOL%" --game survive --root "%SURVIVE%" --character lgm6_main0_def,lgm7_main0_def,lgm8_main0_def,lgm90_main0_def,lgm91_main0_def,lgm9_main0_def,mbs0_main0_def,mbs1_main0_def,mbs2_main0_def,mlt0_main0_def,mlt0_main1_def,npc16_main0_def,npc17_main0_def,nrs0_main0_def,plc0_main0_def,rgf0_main0_def,rgf10_main0_def,rgf11_main0_def,rgf12_main0_def,rgf13_main0_def --export-model --no-rig --skip-existing --out "%OUT%\models" >> "%LOGS%\815-survchar-models.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [16/22] models rgf14_main0_def .. rgm13_main0_def...
echo [cmd] batch 16: rgf14_main0_def .. rgm13_main0_def > "%LOGS%\816-survchar-models.log"
"%TOOL%" --game survive --root "%SURVIVE%" --character rgf14_main0_def,rgf15_main0_def,rgf16_main0_def,rgf17_main0_def,rgf18_main0_def,rgf19_main0_def,rgf1_main0_def,rgf2_main0_def,rgf3_main0_def,rgf4_main0_def,rgf5_main0_def,rgf6_main0_def,rgf7_main0_def,rgf8_main0_def,rgf9_main0_def,rgm0_main0_def,rgm10_main0_def,rgm11_main0_def,rgm12_main0_def,rgm13_main0_def --export-model --no-rig --skip-existing --out "%OUT%\models" >> "%LOGS%\816-survchar-models.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [17/22] models rgm14_main0_def .. uaf11_main1_def...
echo [cmd] batch 17: rgm14_main0_def .. uaf11_main1_def > "%LOGS%\817-survchar-models.log"
"%TOOL%" --game survive --root "%SURVIVE%" --character rgm14_main0_def,rgm15_main0_def,rgm16_main0_def,rgm17_main0_def,rgm18_main0_def,rgm19_main0_def,rgm1_main0_def,rgm2_main0_def,rgm3_main0_def,rgm4_main0_def,rgm5_main0_def,rgm6_main0_def,rgm7_main0_def,rgm8_main0_def,rgm9_main0_def,tnk0_main0_def,tnk0_main1_def,uaf0_main0_def,uaf11_main0_def,uaf11_main1_def --export-model --no-rig --skip-existing --out "%OUT%\models" >> "%LOGS%\817-survchar-models.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [18/22] models uaf12_main0_def .. uam12_main1_def...
echo [cmd] batch 18: uaf12_main0_def .. uam12_main1_def > "%LOGS%\818-survchar-models.log"
"%TOOL%" --game survive --root "%SURVIVE%" --character uaf12_main0_def,uaf12_main1_def,uaf13_main0_def,uaf13_main1_def,uaf15_main0_def,uaf1_main0_def,uaf2_main0_def,uaf2_main1_def,uaf3_main0_def,uaf4_main0_def,uaf5_main0_def,uaf6_main0_def,uaf7_main0_def,uaf8_main0_def,uaf8_main1_def,uam0_main0_def,uam11_main0_def,uam11_main1_def,uam12_main0_def,uam12_main1_def --export-model --no-rig --skip-existing --out "%OUT%\models" >> "%LOGS%\818-survchar-models.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [19/22] models uam13_main0_def .. zmb12_main0_def...
echo [cmd] batch 19: uam13_main0_def .. zmb12_main0_def > "%LOGS%\819-survchar-models.log"
"%TOOL%" --game survive --root "%SURVIVE%" --character uam13_main0_def,uam13_main1_def,uam15_main0_def,uam1_main0_def,uam2_main0_def,uam2_main1_def,uam3_main0_def,uam4_main0_def,uam5_main0_def,uam6_main0_def,uam7_main0_def,uam8_main0_def,uam8_main1_def,zmb10_main0_def,zmb11_main0_def,zmb11_main0_sta,zmb11_main1_sta,zmb11_main2_sta,zmb12_eqhd0_def,zmb12_main0_def --export-model --no-rig --skip-existing --out "%OUT%\models" >> "%LOGS%\819-survchar-models.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [20/22] models zmb1_main1_def .. zmb4_main5_sta...
echo [cmd] batch 20: zmb1_main1_def .. zmb4_main5_sta > "%LOGS%\820-survchar-models.log"
"%TOOL%" --game survive --root "%SURVIVE%" --character zmb1_main1_def,zmb1_main1_sta,zmb1_main2_def,zmb1_main2_sta,zmb1_main3_def,zmb1_main3_sta,zmb2_main0_def,zmb2_main0_sta,zmb3_main0_def,zmb3_main0_sta,zmb3_main1_sta,zmb3_main2_sta,zmb4_main0_def,zmb4_main0_sta,zmb4_main1_sta,zmb4_main2_sta,zmb4_main3_def,zmb4_main3_sta,zmb4_main4_sta,zmb4_main5_sta --export-model --no-rig --skip-existing --out "%OUT%\models" >> "%LOGS%\820-survchar-models.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [21/22] models zmb4_main7_sta .. zmb7_main8_sta...
echo [cmd] batch 21: zmb4_main7_sta .. zmb7_main8_sta > "%LOGS%\821-survchar-models.log"
"%TOOL%" --game survive --root "%SURVIVE%" --character zmb4_main7_sta,zmb4_main8_sta,zmb4_rock0_sta,zmb4_rock1_sta,zmb5_main0_def,zmb5_main0_sta,zmb5_main1_sta,zmb5_main2_sta,zmb5_main3_sta,zmb7_main0_def,zmb7_main0_sta,zmb7_main1_sta,zmb7_main2_sta,zmb7_main3_sta,zmb7_main4_sta,zmb7_main5_sta,zmb7_main6_sta,zmb7_main7_sta,zmb7_main8_sta --export-model --no-rig --skip-existing --out "%OUT%\models" >> "%LOGS%\821-survchar-models.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo [22/22] Survive customisation textures (skins, faces, parts)...
echo [cmd] --rip-variations ssd/fova/chara > "%LOGS%\90-survchar-fova.log"
"%TOOL%" --game survive --root "%SURVIVE%" --rip-variations ssd/fova/chara --out "%OUT%\fova" >> "%LOGS%\90-survchar-fova.log" 2>&1
if errorlevel 1 set /a ERRORS+=1

echo finished %date% %time%, %ERRORS% problem(s) >> "%LOGS%\80-survchar-run.log"
echo.
echo done - %ERRORS% problem(s). Output in rips\survive-chars\, logs in test-logs\
echo tell Claude "read" to have the results checked
pause
