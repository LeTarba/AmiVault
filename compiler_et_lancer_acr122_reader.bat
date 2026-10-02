@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "PROJECT_DIR=%SCRIPT_DIR%Acr122NfcReader"
set "PROJECT_FILE=%PROJECT_DIR%\Acr122NfcReader.csproj"
set "OUTPUT_EXE=%PROJECT_DIR%\bin\Debug\net8.0-windows\AmiVault.exe"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo .NET SDK introuvable. Installe dotnet pour compiler le projet.
    pause
    exit /b 1
)

if not exist "%PROJECT_FILE%" (
    echo Projet introuvable : "%PROJECT_FILE%"
    pause
    exit /b 1
)

pushd "%PROJECT_DIR%"
echo Compilation en cours...
dotnet build "%PROJECT_FILE%" -c Debug
set "EXITCODE=%ERRORLEVEL%"

if not "%EXITCODE%"=="0" (
    echo.
    echo La compilation a echoue avec le code %EXITCODE%.
    popd
    pause
    exit /b %EXITCODE%
)

if exist "%OUTPUT_EXE%" (
    echo.
    echo Lancement de l'application...
    start "" "%OUTPUT_EXE%"
    popd
    exit /b 0
)

echo.
echo Executable introuvable apres compilation : "%OUTPUT_EXE%"
popd
pause
exit /b 1
