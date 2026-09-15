@echo off
setlocal
where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET 10 SDK was not found on PATH.
  exit /b 1
)

set "FKNRTD_ROOT=%~dp0.."
set "FKNRTD_ARTIFACTS=%FKNRTD_ROOT%\artifacts"
dotnet pack "%FKNRTD_ROOT%\src\FKNRTD.Cli\FKNRTD.Cli.csproj" -c Release -o "%FKNRTD_ARTIFACTS%"
if errorlevel 1 exit /b 1

dotnet tool list --global | findstr /I /B "fknrtd.cli " >nul
if errorlevel 1 (
  dotnet tool install --global --add-source "%FKNRTD_ARTIFACTS%" FKNRTD.CLI
) else (
  dotnet tool update --global --add-source "%FKNRTD_ARTIFACTS%" FKNRTD.CLI
)
if errorlevel 1 exit /b 1

echo FKNRTD.CLI installed. Run: fknrtd help
endlocal
