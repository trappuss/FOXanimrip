@echo off
setlocal EnableExtensions EnableDelayedExpansion
title foxanimrip - set up and publish to GitHub
rem ===========================================================================
rem  foxanimrip - the one-click publisher.  SPDX-License-Identifier: MIT
rem
rem  Double-click this. It does everything:
rem     installs Git and the GitHub CLI if missing   (via winget)
rem     signs you in to GitHub
rem     turns this folder into a repository and uploads it
rem     creates the release with the zips attached
rem     creates and fills the wiki
rem
rem  Safe to run again later - it skips whatever is already done.
rem ===========================================================================

rem ---- find the repository root, wherever this file was run from -----------
set "ROOT="
if exist "%~dp0src\Directory.Build.props" set "ROOT=%~dp0"
if not defined ROOT if exist "%~dp0..\src\Directory.Build.props" pushd "%~dp0.." & set "ROOT=!CD!\" & popd
if not defined ROOT for /d %%D in ("%~dp0foxanimrip-repo-*") do if exist "%%D\src\Directory.Build.props" set "ROOT=%%D\"
if not defined ROOT (
  echo.
  echo   Could not find the foxanimrip repository.
  echo   Put this file inside the repo folder ^(the one containing src\^) and run it again.
  echo.
  pause & exit /b 66
)
cd /d "%ROOT%"

echo.
echo   ============================================================
echo     foxanimrip - publish to GitHub
echo     %ROOT%
echo   ============================================================
echo.

rem ---- 1. Git and the GitHub CLI -------------------------------------------
echo   [1/7] checking Git and the GitHub CLI...
set "PATH=%PATH%;%ProgramFiles%\Git\cmd;%ProgramFiles%\GitHub CLI"

where git >nul 2>&1
if errorlevel 1 (
  echo         Git is not installed - installing it now...
  winget install --id Git.Git -e --source winget --accept-package-agreements --accept-source-agreements
  set "PATH=!PATH!;%ProgramFiles%\Git\cmd"
)
where gh >nul 2>&1
if errorlevel 1 (
  echo         GitHub CLI is not installed - installing it now...
  winget install --id GitHub.cli -e --source winget --accept-package-agreements --accept-source-agreements
  set "PATH=!PATH!;%ProgramFiles%\GitHub CLI"
)

where git >nul 2>&1 || goto :needrestart
where gh  >nul 2>&1 || goto :needrestart
echo         ok
goto :identity

:needrestart
echo.
echo   Git and/or the GitHub CLI were just installed, but this window cannot
echo   see them yet. Close this window and double-click the file again.
echo.
echo   If that still fails, install them by hand:
echo       https://git-scm.com/download/win
echo       https://cli.github.com
echo.
pause & exit /b 1

rem ---- 2. who you are ------------------------------------------------------
:identity
echo   [2/7] checking your Git identity...
for /f "delims=" %%N in ('git config --global user.name 2^>nul') do set "GNAME=%%N"
for /f "delims=" %%M in ('git config --global user.email 2^>nul') do set "GMAIL=%%M"
if "!GNAME!"=="" (
  echo.
  set /p GNAME="        Your name (shown on commits): "
  git config --global user.name "!GNAME!"
)
if "!GMAIL!"=="" (
  set /p GMAIL="        Your email (the one on your GitHub account): "
  git config --global user.email "!GMAIL!"
)
echo         !GNAME! ^<!GMAIL!^>

rem ---- 3. sign in ----------------------------------------------------------
echo   [3/7] checking your GitHub sign-in...
gh auth status >nul 2>&1
if errorlevel 1 (
  echo.
  echo         A browser will open. Choose:  GitHub.com  /  HTTPS  /  Yes  /  browser
  echo.
  gh auth login || (echo         sign-in failed & pause & exit /b 1)
)
for /f "delims=" %%U in ('gh api user -q .login 2^>nul') do set "USERNAME=%%U"
echo         signed in as !USERNAME!

rem ---- 4. version ----------------------------------------------------------
set "VERSION="
for /f "tokens=2 delims=<>" %%V in ('findstr /R "<Version>" "src\Directory.Build.props"') do set "VERSION=%%V"
set "TAG=v!VERSION!"
set "ADDON="
for /f "tokens=2 delims==" %%V in ('findstr /R "^version" "blender\io_foxbrowser\blender_manifest.toml"') do set "ADDON=%%V"
set "ADDON=!ADDON: =!"
set "ADDON=!ADDON:"=!"

rem ---- 5. repository -------------------------------------------------------
echo   [4/7] repository...

rem GitHub rejects pushes that expose a private email (error GH007), so commit
rem as the noreply address it issues for exactly this purpose.
for /f "delims=" %%I in ('gh api user -q .id 2^>nul') do set "UID=%%I"
set "NOREPLY=!UID!+!USERNAME!@users.noreply.github.com"

if not exist ".git" (
  echo         creating a new local repository
  git init -q || (pause & exit /b 1)
  git branch -M main
)
git config user.name "!USERNAME!"
git config user.email "!NOREPLY!"

rem Any commit already made with the private address has to be rewritten, or
rem the push is refused again on that old commit.
git rev-parse HEAD >nul 2>&1 && git commit -q --amend --reset-author --no-edit 2>nul

echo.
set "REPONAME=foxanimrip"
set /p REPONAME="        Repository to publish to [foxanimrip]: "
if "!REPONAME!"=="" set "REPONAME=foxanimrip"

set "EXISTS=0"
gh repo view "!USERNAME!/!REPONAME!" >nul 2>&1 && set "EXISTS=1"

if "!EXISTS!"=="1" (
  for /f "delims=" %%B in ('gh repo view "!USERNAME!/!REPONAME!" --json defaultBranchRef -q .defaultBranchRef.name 2^>nul') do set "BRANCH=%%B"
  if "!BRANCH!"=="" set "BRANCH=main"
  echo.
  echo         !USERNAME!/!REPONAME! already exists ^(branch !BRANCH!^).
  echo         Publishing REPLACES its contents with this folder.
  choice /C YN /N /M "        Replace it? (Y/N) "
  if errorlevel 2 (echo         cancelled & pause & exit /b 0)

  git remote get-url origin >nul 2>&1 && git remote remove origin
  git remote add origin "https://github.com/!USERNAME!/!REPONAME!.git"
  git add -A
  git diff --cached --quiet || git commit -q -m "foxanimrip !VERSION!"
  git branch -M !BRANCH!
  echo         replacing !BRANCH! ...
  git push -u origin !BRANCH! --force || (
     echo         push failed & pause & exit /b 1)
) else (
  set "VIS=--public"
  set /p PRIV="        Make it private? (y/N): "
  if /I "!PRIV!"=="y" set "VIS=--private"
  set "BRANCH=main"
  git add -A
  git diff --cached --quiet || git commit -q -m "foxanimrip !VERSION!"
  echo         creating github.com/!USERNAME!/!REPONAME! ...
  gh repo create "!REPONAME!" !VIS! --source . --remote origin --push || (
     echo         could not create the repository & pause & exit /b 1)
)
for /f "delims=" %%R in ('gh repo view --json nameWithOwner -q .nameWithOwner 2^>nul') do set "REPO=%%R"
echo         !REPO!

rem ---- 6. release ----------------------------------------------------------
echo   [5/7] packaging the release...
if not exist "dist\cli\foxanimrip-cli.exe" (
  echo         ! dist\cli\foxanimrip-cli.exe is missing.
  echo           Copy foxanimrip-cli.exe into dist\cli\ and foxanimrip.exe into
  echo           dist\gui\, then run this again.
  pause & exit /b 65
)
set "REL=dist\foxanimrip-!VERSION!-win-x64"
if exist "!REL!" rmdir /s /q "!REL!"
mkdir "!REL!\docs" 2>nul
copy /y "dist\cli\foxanimrip-cli.exe" "!REL!\" >nul
copy /y "dist\gui\foxanimrip.exe"     "!REL!\" >nul 2>&1
copy /y "blender\io_foxbrowser-!ADDON!.zip" "!REL!\" >nul
copy /y "CHANGELOG.md" "!REL!\" >nul
copy /y "README.md"    "!REL!\" >nul
copy /y "docs\*.md"    "!REL!\docs\" >nul
copy /y "docs\*.png"   "!REL!\docs\" >nul 2>&1
copy /y "scripts\test-*.bat"   "!REL!\" >nul 2>&1
copy /y "scripts\export-*.bat" "!REL!\" >nul 2>&1
copy /y "scripts\*.py"         "!REL!\" >nul 2>&1
powershell -NoProfile -Command ^
  "Remove-Item -Force 'dist\foxanimrip-!VERSION!-win-x64.zip' -EA SilentlyContinue;" ^
  "Compress-Archive -Path '!REL!' -DestinationPath 'dist\foxanimrip-!VERSION!-win-x64.zip'" || (pause & exit /b 1)
powershell -NoProfile -Command ^
  "Remove-Item -Force 'dist\foxanimrip-repo-!VERSION!.zip' -EA SilentlyContinue;" ^
  "Compress-Archive -Path 'src','blender','docs','wiki','scripts','tools','tests','README.md','CHANGELOG.md','LICENSE' -DestinationPath 'dist\foxanimrip-repo-!VERSION!.zip'" || (pause & exit /b 1)

echo   [6/7] release !TAG!...
gh release view !TAG! >nul 2>&1
if errorlevel 1 (
  gh release create !TAG! ^
     "dist\foxanimrip-!VERSION!-win-x64.zip" ^
     "dist\foxanimrip-repo-!VERSION!.zip" ^
     "blender\io_foxbrowser-!ADDON!.zip" ^
     --title "foxanimrip !VERSION!" --notes-file "CHANGELOG.md" || (pause & exit /b 1)
  echo         created
) else (
  gh release upload !TAG! ^
     "dist\foxanimrip-!VERSION!-win-x64.zip" ^
     "dist\foxanimrip-repo-!VERSION!.zip" ^
     "blender\io_foxbrowser-!ADDON!.zip" --clobber >nul || (pause & exit /b 1)
  gh release edit !TAG! --notes-file "CHANGELOG.md" >nul
  echo         updated
)

rem ---- 7. wiki -------------------------------------------------------------
echo   [7/7] wiki...
if not exist "wiki" (echo         no wiki\ folder - skipping & goto :cleanup)
gh api -X PATCH "repos/!REPO!" -f has_wiki=true >nul 2>&1

set "W=%TEMP%\fxwiki"
if exist "%W%" rmdir /s /q "%W%"
git clone -q "https://github.com/!REPO!.wiki.git" "%W%" 2>nul
if exist "%W%\.git" goto :wikipush

echo         wiki not created yet - trying to create it...
mkdir "%W%" 2>nul
pushd "%W%"
git init -q
copy /y "!ROOT!wiki\*.md" . >nul
git -c user.name="!USERNAME!" -c user.email="!NOREPLY!" commit -q -m "Fox Engine Asset Handbook" --allow-empty 2>nul
git add -A
git -c user.name="!USERNAME!" -c user.email="!NOREPLY!" commit -q -m "Fox Engine Asset Handbook" 2>nul
git branch -M master
git remote add origin "https://github.com/!REPO!.wiki.git"
git push -q -u origin master 2>nul
if not errorlevel 1 (popd & echo         wiki created and pushed & goto :cleanup)
git branch -M main
git push -q -u origin main 2>nul
if not errorlevel 1 (popd & echo         wiki created and pushed & goto :cleanup)
popd

echo.
echo         GitHub needs the very first wiki page to be made on the website.
echo         Opening it now - type any character, click "Save page", then
echo         come back here and press a key.
echo.
start "" "https://github.com/!REPO!/wiki"
pause
if exist "%W%" rmdir /s /q "%W%"
git clone -q "https://github.com/!REPO!.wiki.git" "%W%" 2>nul
if not exist "%W%\.git" (
   echo         still cannot reach the wiki - run this file again later.
   goto :cleanup
)

:wikipush
copy /y "!ROOT!wiki\*.md" "%W%\" >nul
pushd "%W%"
git add -A
git diff --cached --quiet && (
  echo         wiki already up to date
) || (
  git -c user.name="!USERNAME!" -c user.email="!NOREPLY!" commit -q -m "Handbook update for !VERSION!"
  git push -q && echo         wiki pushed
)
popd

:cleanup
rem ---- offer to remove a repository created by mistake ---------------------
gh repo view "!USERNAME!/FOXENGINErip" >nul 2>&1
if errorlevel 1 goto :done
if /I "!REPONAME!"=="FOXENGINErip" goto :done
echo.
echo         You also have an empty repository called FOXENGINErip.
choice /C YN /N /M "        Delete it? (Y/N) "
if errorlevel 2 goto :done
gh repo delete "!USERNAME!/FOXENGINErip" --yes 2>nul
if not errorlevel 1 (echo         deleted & goto :done)
echo         needs one extra permission - approve it in the browser...
gh auth refresh -h github.com -s delete_repo
gh repo delete "!USERNAME!/FOXENGINErip" --yes 2>nul
if errorlevel 1 echo         could not delete it - remove it at https://github.com/!USERNAME!/FOXENGINErip/settings

:done
echo.
echo   ============================================================
echo     Done.
echo.
echo     Code     https://github.com/!REPO!
echo     Release  https://github.com/!REPO!/releases/tag/!TAG!
echo     Wiki     https://github.com/!REPO!/wiki
echo   ============================================================
echo.
choice /C YN /N /M "   Open them in your browser now? (Y/N) "
if errorlevel 2 goto :end
start "" "https://github.com/!REPO!"
start "" "https://github.com/!REPO!/releases/tag/!TAG!"
start "" "https://github.com/!REPO!/wiki"
:end
echo.
pause
exit /b 0
