@echo off
setlocal EnableExtensions EnableDelayedExpansion
rem ===========================================================================
rem  foxanimrip - build, release, and publish to GitHub in one step.
rem  SPDX-License-Identifier: MIT
rem
rem  Does four things, in order, stopping at the first failure:
rem    1. builds the CLI, the GUI and the Blender add-on zip
rem    2. commits and pushes main (README and docs included)
rem    3. creates/updates the GitHub release for this version and uploads the
rem       release zip, the repo zip and the add-on zip
rem    4. syncs wiki\ into the repository's GitHub wiki
rem
rem  Needs: git, the .NET 10 SDK, and the GitHub CLI (gh) already authenticated
rem  (`gh auth login`). Run it from the repository root.
rem
rem    push-github.bat                 build, push, release, wiki
rem    push-github.bat --no-build      skip the build (use what is in dist\)
rem    push-github.bat --wiki-only     only sync the wiki
rem    push-github.bat --dry-run       show what would happen, change nothing
rem ===========================================================================
cd /d "%~dp0.."
set "ROOT=%CD%"

set DRY=0
set NOBUILD=0
set WIKIONLY=0
for %%A in (%*) do (
  if /I "%%A"=="--dry-run"   set DRY=1
  if /I "%%A"=="--no-build"  set NOBUILD=1
  if /I "%%A"=="--wiki-only" set WIKIONLY=1
)

rem ---- preflight -----------------------------------------------------------
where git >nul 2>&1 || (echo [x] git not found on PATH & exit /b 66)
where gh  >nul 2>&1 || (echo [x] GitHub CLI ^(gh^) not found - install it and run: gh auth login & exit /b 66)
gh auth status >nul 2>&1 || (echo [x] gh is not authenticated - run: gh auth login & exit /b 66)
if not exist "src\Directory.Build.props" (echo [x] run this from the repository root & exit /b 66)

rem ---- version, straight from the single source ----------------------------
set "VERSION="
for /f "tokens=2 delims=<>" %%V in ('findstr /R "<Version>" "src\Directory.Build.props"') do set "VERSION=%%V"
if "%VERSION%"=="" (echo [x] could not read ^<Version^> from src\Directory.Build.props & exit /b 65)
set "TAG=v%VERSION%"

rem ---- add-on version, for the asset name ----------------------------------
set "ADDON="
for /f "tokens=2 delims==" %%V in ('findstr /R "^version" "blender\io_foxbrowser\blender_manifest.toml"') do set "ADDON=%%V"
set "ADDON=%ADDON: =%"
set "ADDON=%ADDON:"=%"

echo.
echo   foxanimrip %VERSION%   (add-on %ADDON%)   tag %TAG%
if %DRY%==1 echo   DRY RUN - nothing will be pushed
echo.

if %WIKIONLY%==1 goto :wiki

rem ---- 1. build ------------------------------------------------------------
if %NOBUILD%==1 goto :afterbuild
echo [1/4] building...
if "%FoxBrowserRefDir%"=="" (
  echo     ! FoxBrowserRefDir is not set. The Core project needs FoxBrowser's
  echo       assemblies to compile against. Set it, e.g.:
  echo           set FoxBrowserRefDir=C:\path\to\foxbrowser-refs
  echo       ^(tools\extract-refs.py makes that folder^)
  exit /b 65
)
if %DRY%==0 (
  dotnet publish src\FoxAnimRip.Headless -c Release -r win-x64 --self-contained false ^
      -p:PublishSingleFile=true -p:FoxBrowserRefDir="%FoxBrowserRefDir%" -o "dist\cli" || exit /b 1
  dotnet publish src\FoxAnimRip -c Release ^
      -p:FoxBrowserRefDir="%FoxBrowserRefDir%" -o "dist\gui" || exit /b 1
  powershell -NoProfile -Command ^
    "Remove-Item -Force 'blender\io_foxbrowser-%ADDON%.zip' -ErrorAction SilentlyContinue;" ^
    "Get-ChildItem 'blender\io_foxbrowser' -Recurse -Include __pycache__ | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue;" ^
    "Compress-Archive -Path 'blender\io_foxbrowser' -DestinationPath 'blender\io_foxbrowser-%ADDON%.zip'" || exit /b 1
)
:afterbuild

rem ---- assemble the release payload ---------------------------------------
set "REL=dist\foxanimrip-%VERSION%-win-x64"
if %DRY%==0 (
  if exist "%REL%" rmdir /s /q "%REL%"
  mkdir "%REL%\docs" 2>nul
  copy /y "dist\cli\foxanimrip-cli.exe" "%REL%\" >nul 2>&1
  copy /y "dist\gui\foxanimrip.exe"     "%REL%\" >nul 2>&1
  copy /y "blender\io_foxbrowser-%ADDON%.zip" "%REL%\" >nul
  copy /y "CHANGELOG.md" "%REL%\" >nul
  copy /y "README.md"    "%REL%\" >nul
  copy /y "docs\*.md"    "%REL%\docs\" >nul
  copy /y "docs\*.png"   "%REL%\docs\" >nul 2>&1
  copy /y "scripts\*.bat" "%REL%\" >nul
  copy /y "scripts\*.py"  "%REL%\" >nul 2>&1
  del /q "%REL%\push-github.bat" 2>nul
  powershell -NoProfile -Command ^
    "Remove-Item -Force 'dist\foxanimrip-%VERSION%-win-x64.zip' -ErrorAction SilentlyContinue;" ^
    "Compress-Archive -Path '%REL%' -DestinationPath 'dist\foxanimrip-%VERSION%-win-x64.zip'" || exit /b 1
  powershell -NoProfile -Command ^
    "Remove-Item -Force 'dist\foxanimrip-repo-%VERSION%.zip' -ErrorAction SilentlyContinue;" ^
    "Compress-Archive -Path 'src','blender','docs','wiki','scripts','tools','tests','README.md','CHANGELOG.md','LICENSE','build.ps1','build.sh' -DestinationPath 'dist\foxanimrip-repo-%VERSION%.zip'" || exit /b 1
)

rem ---- 2. commit and push main --------------------------------------------
echo [2/4] pushing main...
git rev-parse --abbrev-ref HEAD > "%TEMP%\fx_branch.txt"
set /p BRANCH=<"%TEMP%\fx_branch.txt"
if not "%BRANCH%"=="main" echo     ^(on branch %BRANCH%, not main^)
if %DRY%==0 (
  git add -A
  git diff --cached --quiet && (
    echo     nothing to commit
  ) || (
    git commit -m "foxanimrip %VERSION%" -m "See CHANGELOG.md for details." || exit /b 1
  )
  git push origin %BRANCH% || exit /b 1
)

rem ---- 3. release ----------------------------------------------------------
echo [3/4] release %TAG%...
if %DRY%==0 (
  gh release view %TAG% >nul 2>&1
  if errorlevel 1 (
    gh release create %TAG% ^
       "dist\foxanimrip-%VERSION%-win-x64.zip" ^
       "dist\foxanimrip-repo-%VERSION%.zip" ^
       "blender\io_foxbrowser-%ADDON%.zip" ^
       --title "foxanimrip %VERSION%" --notes-file "CHANGELOG.md" || exit /b 1
  ) else (
    echo     release exists - replacing assets
    gh release upload %TAG% ^
       "dist\foxanimrip-%VERSION%-win-x64.zip" ^
       "dist\foxanimrip-repo-%VERSION%.zip" ^
       "blender\io_foxbrowser-%ADDON%.zip" --clobber || exit /b 1
    gh release edit %TAG% --notes-file "CHANGELOG.md" >nul || exit /b 1
  )
)

rem ---- 4. wiki -------------------------------------------------------------
:wiki
echo [4/4] wiki...
if not exist "wiki" (echo     no wiki\ folder - skipping & goto :done)
for /f "tokens=*" %%R in ('gh repo view --json nameWithOwner -q .nameWithOwner') do set "REPO=%%R"
if "%REPO%"=="" (echo [x] could not determine the repository from gh & exit /b 1)
if %DRY%==1 (
  echo     would sync wiki\*.md to https://github.com/%REPO%.wiki.git
  goto :done
)
if exist "%TEMP%\fxwiki" rmdir /s /q "%TEMP%\fxwiki"
git clone "https://github.com/%REPO%.wiki.git" "%TEMP%\fxwiki" 2>nul
if not exist "%TEMP%\fxwiki\.git" (
  echo     ! could not clone the wiki.
  echo       GitHub creates the wiki repository only after its first page exists.
  echo       Open https://github.com/%REPO%/wiki and save any page once, then
  echo       run:  push-github.bat --wiki-only
  goto :done
)
copy /y "wiki\*.md" "%TEMP%\fxwiki\" >nul
pushd "%TEMP%\fxwiki"
git add -A
git diff --cached --quiet && (
  echo     wiki already up to date
) || (
  git commit -m "Handbook update for %VERSION%" >nul || (popd & exit /b 1)
  git push || (popd & exit /b 1)
  echo     wiki pushed
)
popd

:done
echo.
echo   done.
echo     release   https://github.com/%REPO%/releases/tag/%TAG%
echo     wiki      https://github.com/%REPO%/wiki
echo.
pause
exit /b 0
