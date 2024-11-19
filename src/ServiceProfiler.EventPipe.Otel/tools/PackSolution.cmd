SETLOCAL
@ECHO OFF
SET CONFIG=%1
SET PKG_TYPE=%2
SET REBUILD=%3
IF '%CONFIG%' == '' GOTO HELP
IF '%PKG_TYPE%' == '' SET PKG_TYPE=private
IF '%REBUILD%' == '' SET REBUILD=TRUE
IF '%TARGET_FX%' == '' SET TARGET_FX=netstandard2.1

pushd %~dp0

CLS
ECHO Target Configuration: %CONFIG%. ReBuild: %REBUILD%. Package Type: %PKG_TYPE%.

SET BASE_DIR=%~dp0..
SET SLN_DIR=%BASE_DIR%
SET TEMP_OUT=%BASE_DIR%\Out

ECHO Prepare output folder: %TEMP_OUT%
mkdir %TEMP_OUT% > NUL
mkdir %TEMP_OUT%\Nugets > NUL
del %TEMP_OUT%\Nugets\*.nupkg /S /Q /F > NUL

DEL %BASE_DIR%\Azure.Monitor.OpenTelemetry.Profiler.Core\bin\%CONFIG%\%TARGET_FX%\*.nupkg /Q > NUL
DEL %BASE_DIR%\Azure.Monitor.OpenTelemetry.Profiler.AspNetCore\bin\%CONFIG%\%TARGET_FX%\*.nupkg /Q > NUL

ECHO Build the solution

call .\BuildSolution.cmd %CONFIG% %REBUILD%

IF '%ERRORLEVEL%' NEQ '0' GOTO ERR

dotnet pack %BASE_DIR%\Azure.Monitor.OpenTelemetry.Profiler.Core --include-symbols --no-build --no-restore --version-suffix -%PKG_TYPE%-%CURRENT_DATE_TIME% -c %CONFIG%
dotnet pack %BASE_DIR%\Azure.Monitor.OpenTelemetry.Profiler.AspNetCore --include-symbols --no-build --no-restore --version-suffix -%PKG_TYPE%-%CURRENT_DATE_TIME% -c %CONFIG%

ECHO COPY %BASE_DIR%\Azure.Monitor.OpenTelemetry.Profiler.Core\bin\%CONFIG%\*.nupkg %TEMP_OUT%\Nugets\
COPY %BASE_DIR%\Azure.Monitor.OpenTelemetry.Profiler.Core\bin\%CONFIG%\*.nupkg %TEMP_OUT%\Nugets\
IF '%ERRORLEVEL%' NEQ '0' GOTO ERR

ECHO COPY %BASE_DIR%\Azure.Monitor.OpenTelemetry.Profiler.AspNetCore\bin\%CONFIG%\*.nupkg %TEMP_OUT%\Nugets\
COPY %BASE_DIR%\Azure.Monitor.OpenTelemetry.Profiler.AspNetCore\bin\%CONFIG%\*.nupkg %TEMP_OUT%\Nugets\
IF '%ERRORLEVEL%' NEQ '0' GOTO ERR

popd

IF '%ERRORLEVEL%' == '0' ECHO Package succeeded :-)
GOTO EXIT

:HELP
ECHO Usage
SET "HELPMSG=PackNugetPackage <Debug|Release> [PackageType] [TRUE|FALSE]"
SETLOCAL EnableDelayedExpansion
(
    ECHO !HELPMSG!
    ECHO By default: PackageType=private, REBUILD=TRUE
)
GOTO EXIT

:ERR
ECHO Package failed :-(
GOTO EXIT

:EXIT
ENDLOCAL
