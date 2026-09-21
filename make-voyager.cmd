@echo off
setlocal
cd /d "%~dp0"
set "LOG=%~dp0make-log.txt"
set "STAGE=%~dp0msi-stage"

echo === Voyager: build + MSI + install === > "%LOG%"
taskkill /im Voyager.exe /f >> "%LOG%" 2>&1
timeout /t 2 /nobreak >nul

echo. >> "%LOG%"
echo --- publish (self-contained) --- >> "%LOG%"
dotnet publish "%~dp0Voyager.csproj" -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=false -p:DebugType=none ^
  -o "%~dp0publish" >> "%LOG%" 2>&1
if errorlevel 1 (
  echo [NG] publish failed. >> "%LOG%"
  goto :end
)
rmdir /s /q "%~dp0publish\runtimes" 2>nul

mkdir "%STAGE%" 2>nul
copy /y "%~dp0publish\Voyager.exe"        "%STAGE%\" >> "%LOG%" 2>&1
copy /y "%~dp0publish\WebView2Loader.dll" "%STAGE%\" >> "%LOG%" 2>&1
copy /y "%~dp0assets\Voyager.ico"         "%STAGE%\" >> "%LOG%" 2>&1
copy /y "%~dp0installer\Voyager.v6.wxs"   "%STAGE%\" >> "%LOG%" 2>&1

echo. >> "%LOG%"
echo --- wix build --- >> "%LOG%"
set "PATH=%PATH%;%USERPROFILE%\.dotnet\tools"
pushd "%STAGE%"
wix build -arch x64 -o "%~dp0Voyager-1.0.0-x64-wix.msi" Voyager.v6.wxs >> "%LOG%" 2>&1
echo wix exit=%ERRORLEVEL% >> "%LOG%"
popd

if not exist "%~dp0Voyager-1.0.0-x64-wix.msi" goto :end

echo. >> "%LOG%"
echo --- install --- >> "%LOG%"
del "%~dp0voyager-debug.log" 2>nul
msiexec /i "%~dp0Voyager-1.0.0-x64-wix.msi" /qn /l*v "%~dp0msi-install-log.txt"
echo msiexec exit=%ERRORLEVEL% >> "%LOG%"
dir "%LOCALAPPDATA%\Programs\Voyager" >> "%LOG%" 2>&1
copy /y "%~dp0debug.on" "%LOCALAPPDATA%\Programs\Voyager\debug.on" >> "%LOG%" 2>&1
start "" "%LOCALAPPDATA%\Programs\Voyager\Voyager.exe"

:end
echo DONE >> "%LOG%"
endlocal
