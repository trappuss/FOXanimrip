@echo off
setlocal EnableExtensions
rem SPDX-License-Identifier: MIT
rem
rem test-convert-png.bat -- a PNG beside every DDS, without touching the DDS.
rem
rem   test-convert-png.bat                   converts everything under rips\
rem   test-convert-png.bat "C:\some\folder"  converts everything under that
rem
rem The DDS files stay exactly as ripped -- they are the authentic game data.
rem The PNGs are viewing copies written next to them, and a DDS that already
rem has a PNG is skipped, so re-running only picks up new rips.
rem
rem Conversion is done with Microsoft's texconv (DirectXTex), which decodes
rem every format Fox Engine uses (BC1/BC3/BC4/BC5/BC7, sRGB and linear alike)
rem correctly rather than guessing. If texconv.exe is not beside this script
rem it is downloaded once from Microsoft's official GitHub releases.
rem
rem Normal maps need one special step: Fox stores them two-channel (BC5), with
rem the blue channel dropped because it is derivable. Files whose name contains
rem "nrm" get that channel reconstructed (-reconstructz) so the PNG looks like
rem a normal map instead of a red-green ghost. Everything else converts plainly,
rem in its own colour space -- no format is forced, so nothing gets re-graded.

set "HERE=%~dp0"
set "TEXCONV=%HERE%texconv.exe"
set "TARGET=%~1"
if "%TARGET%"=="" set "TARGET=%HERE%rips"

if not exist "%TARGET%" (
    echo nothing to convert: "%TARGET%" does not exist
    pause
    exit /b 66
)

if not exist "%TEXCONV%" (
    echo texconv.exe not found beside this script - downloading it once from
    echo Microsoft's DirectXTex releases...
    curl -L -o "%TEXCONV%" https://github.com/microsoft/DirectXTex/releases/latest/download/texconv.exe
    if not exist "%TEXCONV%" (
        echo download failed - get texconv.exe from
        echo https://github.com/microsoft/DirectXTex/releases and put it beside this script
        pause
        exit /b 1
    )
)

set /a DONE=0
set /a SKIPPED=0
set /a FAILED=0

for /r "%TARGET%" %%f in (*.dds) do call :one "%%f"

echo.
echo done - %DONE% converted, %SKIPPED% already had a PNG, %FAILED% failed
pause
exit /b 0

:one
if exist "%~dpn1.png" (
    set /a SKIPPED+=1
    goto :eof
)
echo %~nx1
set "FLAGS="
rem Two-channel normal maps get their blue channel rebuilt; everything else
rem converts as-is in its own colour space.
echo %~n1 | findstr /i "nrm" >nul && set "FLAGS=-f R8G8B8A8_UNORM -reconstructz"
"%TEXCONV%" -nologo -y -ft png %FLAGS% -o "%~dp1." "%~1" >nul 2>&1
if exist "%~dpn1.png" (set /a DONE+=1) else (
    set /a FAILED+=1
    echo   ! failed: %~1
)
goto :eof
