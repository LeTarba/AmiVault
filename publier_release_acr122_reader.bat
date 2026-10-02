@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "PROJECT_DIR=%SCRIPT_DIR%Acr122NfcReader"
set "PROJECT_FILE=%PROJECT_DIR%\Acr122NfcReader.csproj"
set "PUBLISH_DIR=%SCRIPT_DIR%dist\AmiVault-Release"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo .NET SDK introuvable. Installe dotnet pour publier le projet.
    pause
    exit /b 1
)

if not exist "%PROJECT_FILE%" (
    echo Projet introuvable : "%PROJECT_FILE%"
    pause
    exit /b 1
)

if exist "%PUBLISH_DIR%" (
    echo Nettoyage de l'ancienne version...
    rmdir /s /q "%PUBLISH_DIR%"
)

pushd "%PROJECT_DIR%"
echo Publication Release en cours...
dotnet publish "%PROJECT_FILE%" -c Release -o "%PUBLISH_DIR%"
set "EXITCODE=%ERRORLEVEL%"
popd

if not "%EXITCODE%"=="0" (
    echo.
    echo La publication a echoue avec le code %EXITCODE%.
    pause
    exit /b %EXITCODE%
)

echo.
echo Publication terminee :
echo %PUBLISH_DIR%
echo.
echo Lancement de l'application publiee...
if exist "%PUBLISH_DIR%\AmiVault.exe" (
    start "" "%PUBLISH_DIR%\AmiVault.exe"
) else (
    echo Executable introuvable dans le dossier publie.
    pause
    exit /b 1
)

exit /b 0
