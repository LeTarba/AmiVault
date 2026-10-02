@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "PROJECT_DIR=%SCRIPT_DIR%Acr122NfcReader"
set "PROJECT_FILE=%PROJECT_DIR%\Acr122NfcReader.csproj"
set "PUBLISH_DIR=%SCRIPT_DIR%dist\AmiVault-win-x64-selfcontained"

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
    echo Nettoyage de l'ancienne version standalone...
    rmdir /s /q "%PUBLISH_DIR%"
)

pushd "%PROJECT_DIR%"
echo Publication standalone Win-x64 en cours...
dotnet publish "%PROJECT_FILE%" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%PUBLISH_DIR%"
set "EXITCODE=%ERRORLEVEL%"
popd

if not "%EXITCODE%"=="0" (
    echo.
    echo La publication standalone a echoue avec le code %EXITCODE%.
    pause
    exit /b %EXITCODE%
)

echo.
echo Publication standalone terminee :
echo %PUBLISH_DIR%
echo.
exit /b 0
