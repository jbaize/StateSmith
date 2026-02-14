@echo off
setlocal EnableExtensions

REM ---- Config ----
set "REPO_ROOT=%~dp0"
set "PROJECT=%REPO_ROOT%src\StateSmith.Cli\StateSmith.Cli.csproj"
set "FRAMEWORK=net9.0"
set "RID=win-x64"
set "CONFIG=Release"
set "OUT_DIR=%REPO_ROOT%artifacts\win-x64"

REM Optional: set from CI/tag; default local value
if "%SEMVER%"=="" set "SEMVER=0.0.0-local-build"

echo Building ss.cli with release-equivalent settings...
dotnet publish "%PROJECT%" ^
  -c %CONFIG% ^
  -r %RID% ^
  --self-contained ^
  --framework %FRAMEWORK% ^
  -p:Version=%SEMVER% ^
  -p:PublishSingleFile=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:DefineConstants="SS_SINGLE_FILE_APPLICATION" ^
  -o "%OUT_DIR%"

if errorlevel 1 (
  echo Publish failed.
  exit /b 1
)

REM Release workflow keeps assembly name as StateSmith.Cli and then renames/moves output.
if not exist "%OUT_DIR%\StateSmith.Cli.exe" (
  echo Expected output not found: "%OUT_DIR%\StateSmith.Cli.exe"
  exit /b 1
)

copy /Y "%OUT_DIR%\StateSmith.Cli.exe" "%OUT_DIR%\ss.cli.exe" >nul
echo Done.
echo   Canonical binary: "%OUT_DIR%\StateSmith.Cli.exe"
echo   Friendly copy:    "%OUT_DIR%\ss.cli.exe"

REM Quick smoke test
"%OUT_DIR%\ss.cli.exe" --version
