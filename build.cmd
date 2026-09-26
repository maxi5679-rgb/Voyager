@echo off
setlocal
cd /d "%~dp0"
set "LOG=%~dp0make-log.txt"
set "STAGE=%~dp0msi-stage"
set "PUB=%~dp0publish"
set "APPDIR=%LOCALAPPDATA%\Programs\Voyager"
set "DATADIR=%LOCALAPPDATA%\Voyager"

rem --- Next build number. nextbuild.ps1 looks at the registry, the installed
rem     exe, buildno.txt and the artifacts in this folder, and takes the highest
rem     one it can find plus 1, so a cleanup of this folder can no longer make
rem     the version go backwards.
set BUILDNO=
for /f "usebackq delims=" %%v in (`powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0nextbuild.ps1"`) do set BUILDNO=%%v

if "%BUILDNO%"=="" (
  echo [NG] could not determine the next build number > "%LOG%"
  goto :done
)
set "VER=1.0.%BUILDNO%"

echo === Voyager build %VER% === > "%LOG%"
powershell -NoProfile -Command ^
  "$p = Join-Path $env:LOCALAPPDATA 'Programs\Voyager\Voyager.exe';" ^
  "if (Test-Path $p) { Write-Output ('  currently installed: ' + (Get-Item $p).VersionInfo.FileVersion) } else { Write-Output '  currently installed: (none)' }" >> "%LOG%" 2>&1

taskkill /im Voyager.exe /f >> "%LOG%" 2>&1
rem Wait without 'timeout'. It aborts with "input redirection is not supported"
rem when this script runs with a redirected stdin, e.g. launched from a tool.
ping -n 3 127.0.0.1 >nul
call :arp "before"

echo. >> "%LOG%"
echo [1] publish %VER% >> "%LOG%"
if exist "%PUB%" rmdir /s /q "%PUB%"
rem Self-contained but NOT a single file. One opaque unsigned 43MB blob made
rem the resident scanner read the whole thing on first launch (45 s). As loose
rem files, almost everything is Microsoft-signed runtime and gets waved through.
dotnet publish "%~dp0Voyager.csproj" -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=false -p:DebugType=none ^
  -p:Version=%VER% -o "%PUB%" >> "%LOG%" 2>&1
if errorlevel 1 (
  echo [NG] dotnet publish failed with exit code %ERRORLEVEL% >> "%LOG%"
  goto :fail
)
if not exist "%PUB%\Voyager.exe" (
  echo [NG] publish produced no Voyager.exe >> "%LOG%"
  goto :fail
)
rem WebView2Loader.dll must sit next to the exe, not only under runtimes\.
if not exist "%PUB%\WebView2Loader.dll" (
  for /r "%PUB%\runtimes" %%f in (WebView2Loader.dll) do copy /y "%%f" "%PUB%\WebView2Loader.dll" >nul
)
rmdir /s /q "%PUB%\runtimes" 2>nul
if not exist "%PUB%\WebView2Loader.dll" (
  echo [NG] WebView2Loader.dll not found in publish output >> "%LOG%"
  goto :fail
)
powershell -NoProfile -Command "$v=(Get-Item '%PUB%\Voyager.exe').VersionInfo; Write-Output ('  built FileVersion=' + $v.FileVersion)" >> "%LOG%" 2>&1

echo. >> "%LOG%"
echo [2] stage >> "%LOG%"
if exist "%STAGE%" rmdir /s /q "%STAGE%"
mkdir "%STAGE%"
copy /y "%PUB%\Voyager.exe"             "%STAGE%\Voyager.exe"        >> "%LOG%" 2>&1 || goto :fail
copy /y "%PUB%\WebView2Loader.dll"      "%STAGE%\WebView2Loader.dll" >> "%LOG%" 2>&1 || goto :fail
xcopy /e /i /y /q "%PUB%" "%STAGE%\publish" >> "%LOG%" 2>&1 || goto :fail
powershell -NoProfile -Command ^
  "$n = (Get-ChildItem -Recurse -File '%STAGE%\publish').Count;" ^
  "Write-Output ('  staged files=' + $n)" >> "%LOG%" 2>&1
copy /y "%~dp0assets\Voyager.ico"       "%STAGE%\Voyager.ico"        >> "%LOG%" 2>&1 || goto :fail
copy /y "%~dp0installer\ui\side.bmp"    "%STAGE%\side.bmp"           >> "%LOG%" 2>&1 || goto :fail
copy /y "%~dp0installer\ui\banner.bmp"  "%STAGE%\banner.bmp"         >> "%LOG%" 2>&1 || goto :fail
copy /y "%~dp0installer\Voyager.ui.wxs" "%STAGE%\Voyager.ui.wxs"     >> "%LOG%" 2>&1 || goto :fail

echo. >> "%LOG%"
echo [3] wix build %VER% >> "%LOG%"
set "PATH=%PATH%;%USERPROFILE%\.dotnet\tools"
rem Two packages from the same source. Windows Installer bakes its own strings
rem (the ActionText that scrolls on the progress page, the Error table) into the
rem package at build time from Package/@Language, and nothing at run time can
rem change them, so the only way to get both right is to build both.
rem The plain name is English, matching README.md; -ja matches README.ja.md.
rem The wizard text we wrote ourselves follows UILANG in either package.
wix build -arch x64 -d Ver=%VER% -d Lang=1033 -d Stage=%STAGE% -b "%STAGE%" -o "%~dp0Voyager-%VER%-x64.msi" "%STAGE%\Voyager.ui.wxs" >> "%LOG%" 2>&1
if errorlevel 1 (
  echo [NG] wix build en failed with exit code %ERRORLEVEL% >> "%LOG%"
  goto :fail
)
wix build -arch x64 -d Ver=%VER% -d Lang=1041 -d Stage=%STAGE% -b "%STAGE%" -o "%~dp0Voyager-%VER%-x64-ja.msi" "%STAGE%\Voyager.ui.wxs" >> "%LOG%" 2>&1
if errorlevel 1 (
  echo [NG] wix build ja failed with exit code %ERRORLEVEL% >> "%LOG%"
  goto :fail
)
if not exist "%~dp0Voyager-%VER%-x64.msi" (
  echo [NG] wix reported success but no MSI >> "%LOG%"
  goto :fail
)
if not exist "%~dp0Voyager-%VER%-x64-ja.msi" (
  echo [NG] wix reported success but no -ja MSI >> "%LOG%"
  goto :fail
)
powershell -NoProfile -Command ^
  "Get-ChildItem '%~dp0Voyager-%VER%-x64*.msi' | ForEach-Object { '  built {0} ({1} MB)' -f $_.Name, [math]::Round($_.Length/1MB,1) }" >> "%LOG%" 2>&1

rem --- debug.on and the app log now live in the app data folder, not in the
rem     install folder: MSI cannot delete files it does not own, so a debug.on
rem     under Programs\Voyager kept that folder alive after an uninstall.
rem     The single line written into debug.on tells the app where to put the
rem     log, so it lands next to this script instead of under AppData.
if not exist "%DATADIR%" mkdir "%DATADIR%"
> "%DATADIR%\debug.on" echo %~dp0
del "%~dp0voyager-debug.log" 2>nul

rem Earlier builds dropped debug.on into the install folder. Clear that stale
rem copy out, or it keeps the folder alive after an uninstall.
del "%APPDIR%\debug.on" 2>nul

echo. >> "%LOG%"
echo [4] install %VER% (wizard) >> "%LOG%"
rem start /wait so the script really waits for msiexec to finish.
rem Plain "msiexec" can hand off to a second process and return early,
rem which is why rebuild8 never wrote its RESULT line.
rem Anything passed to this script goes on to msiexec, e.g.
rem   build.cmd UILANG=1033     shows the wizard in English on a Japanese Windows
start "" /wait msiexec /i "%~dp0Voyager-%VER%-x64.msi" /l*v "%~dp0msi-install-log.txt" %*
set "RC=%ERRORLEVEL%"

rem RESULT goes in FIRST, before anything that could stall, so the log always
rem says how it ended even if a later step is slow or the window gets closed.
echo   msiexec exit code %RC% >> "%LOG%"
if "%RC%"=="1602" (
  echo RESULT: CANCELLED by the user >> "%LOG%"
  echo === DONE === >> "%LOG%"
  goto :done
)
if not "%RC%"=="0" (
  echo RESULT: FAILED - msiexec %RC% >> "%LOG%"
  echo        1603 = downgrade or the app was still running. >> "%LOG%"
  echo === DONE === >> "%LOG%"
  goto :done
)
echo RESULT: OK (%VER%) >> "%LOG%"
echo === DONE === >> "%LOG%"
call :arp "after"

powershell -NoProfile -Command ^
  "$p = Join-Path $env:LOCALAPPDATA 'Programs\Voyager\Voyager.exe';" ^
  "$m = Join-Path $PWD 'Voyager-%VER%-x64.msi';" ^
  "Write-Output ('  installed FileVersion=' + (Get-Item $p).VersionInfo.FileVersion);" ^
  "Write-Output ('  installed files=' + (Get-ChildItem -Recurse -File (Split-Path $p)).Count);" ^
  "Write-Output ('  msi size=' + [Math]::Round((Get-Item $m).Length/1MB,1) + ' MB')" >> "%LOG%" 2>&1
goto :done

:fail
echo. >> "%LOG%"
echo RESULT: FAILED - nothing was installed >> "%LOG%"
echo === DONE === >> "%LOG%"

:done
endlocal
exit /b 0

rem --- Count the "Voyager" entries in Apps and Features.
rem     Two entries means an old product was left registered; catch it at once.
:arp
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0arpcheck.ps1" -Label "%~1" >> "%LOG%" 2>&1
goto :eof
