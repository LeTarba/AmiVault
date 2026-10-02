@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "PROJECT_DIR=%SCRIPT_DIR%Acr122NfcReader"
set "PROJECT_FILE=%PROJECT_DIR%\Acr122NfcReader.csproj"
set "DEBUG_EXE=%PROJECT_DIR%\bin\Debug\net8.0-windows\AmiVault.exe"
set "RELEASE_EXE=%PROJECT_DIR%\bin\Release\net8.0-windows\AmiVault.exe"

if exist "%DEBUG_EXE%" (
    start "" "%DEBUG_EXE%"
    exit /b 0
)

if exist "%RELEASE_EXE%" (
    start "" "%RELEASE_EXE%"
    exit /b 0
)

where dotnet >nul 2>&1
if errorlevel 1 (
    echo .NET SDK introuvable. Compile d'abord le projet ou installe dotnet.
    pause
    exit /b 1
)

if not exist "%PROJECT_FILE%" (
    echo Projet introuvable : "%PROJECT_FILE%"
    pause
    exit /b 1
)

pushd "%PROJECT_DIR%"
dotnet run --project "%PROJECT_FILE%"
set "EXITCODE=%ERRORLEVEL%"
popd

if not "%EXITCODE%"=="0" (
    echo.
    echo Le programme s'est termine avec le code %EXITCODE%.
    pause
)

exit /b %EXITCODE%
