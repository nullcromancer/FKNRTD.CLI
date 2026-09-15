@echo off
rem FKNRTD.CLI installation manager.
rem Installs, updates, removes and diagnoses the global 'fknrtd' tool built from this checkout.
setlocal enabledelayedexpansion

set "PACKAGE_ID=FKNRTD.CLI"
set "COMMAND_NAME=fknrtd"

for %%d in ("%~dp0..") do set "ROOT=%%~fd"
set "PROJECT=%ROOT%\src\FKNRTD.Cli\FKNRTD.Cli.csproj"
set "SOLUTION=%ROOT%\FKNRTD.CLI.sln"
set "SELFTEST=%ROOT%\tests\FKNRTD.SelfTest\FKNRTD.SelfTest.csproj"
set "ARTIFACTS=%ROOT%\artifacts"
set "TOOLS_DIR=%USERPROFILE%\.dotnet\tools"

set "ACTION=%~1"
if "%ACTION%"=="" set "ACTION=help"

set "DO_VERIFY=0"
set "DO_PACK=1"
for %%o in (%*) do (
  set "ARG=%%~o"
  if "!ARG:~0,1!"=="-" (
    if /i "!ARG!"=="-verify" (
      set "DO_VERIFY=1"
    ) else if /i "!ARG!"=="-no-pack" (
      set "DO_PACK=0"
    ) else if /i "!ARG!"=="-h" (
      set "ACTION=help"
    ) else (
      echo fknrtd manage: unknown option '!ARG!'. Run 'manage.cmd help'.
      exit /b 1
    )
  )
)

if /i "%ACTION%"=="install" goto :run_install
if /i "%ACTION%"=="update" goto :run_update
if /i "%ACTION%"=="upgrade" goto :run_update
if /i "%ACTION%"=="uninstall" goto :run_uninstall
if /i "%ACTION%"=="remove" goto :run_uninstall
if /i "%ACTION%"=="doctor" goto :run_doctor
if /i "%ACTION%"=="status" goto :run_doctor
if /i "%ACTION%"=="help" goto :run_help

echo Unknown action: %ACTION%
echo.
call :usage
exit /b 2

:run_help
call :usage
exit /b 0

:run_install
call :require_dotnet || exit /b 1
call :installed_version
if not "%INSTALLED%"=="" (
  echo fknrtd manage: %PACKAGE_ID% %INSTALLED% is already installed. Run 'manage.cmd update' instead.
  exit /b 1
)
if "%DO_VERIFY%"=="1" (
  call :verify || exit /b 1
)
if "%DO_PACK%"=="1" (
  call :pack || exit /b 1
)
call :source_version
dotnet tool install --global --add-source "%ARTIFACTS%" --version "%SRC_VERSION%" "%PACKAGE_ID%" || exit /b 1
echo.
echo Installed %PACKAGE_ID% %SRC_VERSION%. Run: %COMMAND_NAME% help
call :warn_about_path
exit /b 0

:run_update
call :require_dotnet || exit /b 1
call :installed_version
if "%DO_VERIFY%"=="1" (
  call :verify || exit /b 1
)
if "%DO_PACK%"=="1" (
  call :pack || exit /b 1
)
call :source_version
if "%INSTALLED%"=="" (
  echo %PACKAGE_ID% is not installed yet; installing it instead.
  echo.
  dotnet tool install --global --add-source "%ARTIFACTS%" --version "%SRC_VERSION%" "%PACKAGE_ID%" || exit /b 1
  echo.
  echo Installed %PACKAGE_ID% %SRC_VERSION%. Run: %COMMAND_NAME% help
  call :warn_about_path
  exit /b 0
)
if /i "%INSTALLED%"=="%SRC_VERSION%" (
  rem Same version number, changed content: 'update' is a no-op, so reinstall over it.
  echo Reinstalling %PACKAGE_ID% %SRC_VERSION% over the same version.
  dotnet tool uninstall --global "%PACKAGE_ID%" || exit /b 1
  dotnet tool install --global --add-source "%ARTIFACTS%" --version "%SRC_VERSION%" "%PACKAGE_ID%" || exit /b 1
) else (
  dotnet tool update --global --add-source "%ARTIFACTS%" --version "%SRC_VERSION%" "%PACKAGE_ID%" || exit /b 1
)
echo.
echo Updated %PACKAGE_ID% %INSTALLED% -^> %SRC_VERSION%. Run: %COMMAND_NAME% help
call :warn_about_path
exit /b 0

:run_uninstall
call :require_dotnet || exit /b 1
call :installed_version
if "%INSTALLED%"=="" (
  echo %PACKAGE_ID% is not installed. Nothing to remove.
  exit /b 0
)
dotnet tool uninstall --global "%PACKAGE_ID%" || exit /b 1
echo.
echo Removed %PACKAGE_ID% %INSTALLED%.
echo Project state in .fknrtd\ directories was left untouched.
exit /b 0

:run_doctor
set "STATUS=0"
echo FKNRTD.CLI installation diagnostics
where dotnet >nul 2>nul
if errorlevel 1 (
  call :report "FAIL" "dotnet SDK" "Not found on PATH. Install the .NET 10 SDK."
  echo.
  echo Install the .NET 10 SDK and run this again.
  exit /b 1
)
set "DOTNET_VERSION="
for /f "delims=" %%v in ('dotnet --version 2^>nul') do set "DOTNET_VERSION=%%v"
call :report "OK" "dotnet SDK" "%DOTNET_VERSION%"

if not exist "%PROJECT%" (
  call :report "FAIL" "Source version" "%PROJECT% is missing."
  exit /b 1
)
call :source_version
call :report "OK" "Source version" "%SRC_VERSION% from %PROJECT%"

call :installed_version
if "%INSTALLED%"=="" (
  call :report "WARN" "Installed tool" "Not installed. Run 'manage.cmd install'."
  set "STATUS=1"
) else (
  call :report "OK" "Installed tool" "%PACKAGE_ID% %INSTALLED%"
  if /i "%INSTALLED%"=="%SRC_VERSION%" (
    call :report "OK" "Up to date" "Matches this checkout."
  ) else (
    call :report "WARN" "Up to date" "Installed %INSTALLED%, this checkout builds %SRC_VERSION%. Run 'manage.cmd update'."
    set "STATUS=1"
  )
)

set "RESOLVED="
for /f "delims=" %%p in ('where %COMMAND_NAME% 2^>nul') do if not defined RESOLVED set "RESOLVED=%%p"
if "%RESOLVED%"=="" (
  call :report "WARN" "Command on PATH" "'%COMMAND_NAME%' does not resolve. Add %TOOLS_DIR% to PATH."
  set "STATUS=1"
) else (
  call :report "OK" "Command on PATH" "%RESOLVED%"
  set "REPORTED="
  for /f "delims=" %%v in ('%COMMAND_NAME% --version 2^>nul') do set "REPORTED=%%v"
  if "!REPORTED!"=="" (
    call :report "FAIL" "Command runs" "%COMMAND_NAME% --version produced no output."
    set "STATUS=1"
  ) else (
    call :report "OK" "Command runs" "!REPORTED!"
  )
)

if exist "%ARTIFACTS%\*.nupkg" (
  call :report "OK" "Local package" "%ARTIFACTS%"
) else (
  call :report "WARN" "Local package" "No .nupkg in artifacts\. Packing happens on install or update."
)

echo.
if "%STATUS%"=="0" (
  echo The installation is healthy. For workspace diagnostics run: %COMMAND_NAME% doctor
) else (
  echo Act on the warnings above, then run this again.
)
exit /b %STATUS%

rem ------------------------------------------------------------------ helpers

:usage
echo FKNRTD.CLI installation manager
echo.
echo   manage.cmd install      Pack this checkout and install the global tool
echo   manage.cmd update       Pack this checkout and move the installed tool to it
echo   manage.cmd uninstall    Remove the global tool; project .fknrtd state is kept
echo   manage.cmd doctor       Diagnose the installation and report what to do next
echo   manage.cmd help         Show this text
echo.
echo Options for install and update
echo   -verify      Build and run the local self-test suite before installing
echo   -no-pack     Install from artifacts\ without packing again
echo.
echo 'fknrtd doctor' is a different command: it diagnoses a workspace, not the installation.
goto :eof

:require_dotnet
where dotnet >nul 2>nul
if errorlevel 1 (
  echo fknrtd manage: the .NET 10 SDK was not found on PATH.
  exit /b 1
)
goto :eof

rem <Version> from the tool project: the exact package this checkout produces.
:source_version
set "SRC_VERSION="
for /f "tokens=*" %%l in ('findstr /c:"<Version>" "%PROJECT%"') do set "VERSION_LINE=%%l"
set "SRC_VERSION=%VERSION_LINE:<Version>=%"
set "SRC_VERSION=%SRC_VERSION:</Version>=%"
goto :eof

rem Empty when the global tool is not installed.
:installed_version
set "INSTALLED="
for /f "tokens=1,2" %%a in ('dotnet tool list --global 2^>nul') do (
  if /i "%%a"=="fknrtd.cli" set "INSTALLED=%%b"
)
goto :eof

:verify
echo Verifying this checkout before installing.
dotnet build "%SOLUTION%" -c Release || exit /b 1
dotnet run --project "%SELFTEST%" -c Release --no-build || exit /b 1
goto :eof

:pack
dotnet pack "%PROJECT%" -c Release -o "%ARTIFACTS%" || exit /b 1
goto :eof

:warn_about_path
where %COMMAND_NAME% >nul 2>nul
if not errorlevel 1 goto :eof
echo.
echo %COMMAND_NAME% is not on PATH yet. Add this directory and restart the terminal:
echo   %TOOLS_DIR%
goto :eof

:report
setlocal
set "MARK=%~1     "
set "NAME=%~2                        "
echo %MARK:~0,4% %NAME:~0,24% %~3
endlocal
goto :eof
