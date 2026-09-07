@echo off
setlocal enabledelayedexpansion

echo ===================================================
echo   Building OmniHID (Core Library and CLI)
echo ===================================================

set CSC_PATH=
for /d %%D in ("%WINDIR%\Microsoft.NET\Framework64\v4.0.30319", "%WINDIR%\Microsoft.NET\Framework\v4.0.30319") do (
    if exist "%%~D\csc.exe" set "CSC_PATH=%%~D\csc.exe"
)

if "%CSC_PATH%"=="" (
    echo [ERROR] csc.exe not found!
    exit /b 1
)

if not exist "bin" mkdir bin

echo.
echo [1/2] Compiling OmniHid.Core.dll...
"%CSC_PATH%" /nologo /target:library /optimize+ /out:bin\OmniHid.Core.dll ^
    /reference:System.IO.Compression.dll,System.IO.Compression.FileSystem.dll ^
    /recurse:src\OmniHid.Core\*.cs

if errorlevel 1 (
    echo [ERROR] OmniHid.Core compilation failed.
    exit /b 1
)

echo [2/2] Compiling omni-hid.exe (CLI standalone)...
"%CSC_PATH%" /nologo /target:exe /optimize+ /out:bin\omni-hid.exe ^
    /reference:bin\OmniHid.Core.dll ^
    /resource:bin\OmniHid.Core.dll,OmniHid.Core.dll ^
    /recurse:src\OmniHid.Cli\*.cs

if errorlevel 1 (
    echo [ERROR] omni-hid CLI compilation failed.
    exit /b 1
)

echo.
echo Deploying local devices directory to bin\devices...
if not exist "bin\devices" mkdir "bin\devices"
xcopy /s /e /y /i "devices" "bin\devices" >nul

echo.
echo [SUCCESS] OmniHID build succeeded!
echo   - bin\OmniHid.Core.dll
echo   - bin\omni-hid.exe
echo   - bin\devices\ (verified and unverified)
exit /b 0