SETLOCAL
@ECHO OFF
SET CONFIG=%1
SET REBUILD=%2

IF '%CONFIG%' == '' GOTO HELP
IF '%REBUILD%' == '' SET REBUILD=TRUE

ECHO Target Configuration: %CONFIG%. ReBuild: %REBUILD%

FOR /F "TOKENS=1* DELIMS= " %%A IN ('DATE/T') DO SET CDATE=%%B
FOR /F "TOKENS=1,2 eol=/ DELIMS=/ " %%A IN ('DATE/T') DO SET mm=%%B
FOR /F "TOKENS=1,2 DELIMS=/ eol=/" %%A IN ('echo %CDATE%') DO SET dd=%%B
FOR /F "TOKENS=2,3 DELIMS=/ " %%A IN ('echo %CDATE%') DO SET yyyy=%%B
FOR /F "TOKENS=1-2 delims=/:" %%a in ('echo %TIME: =0%') DO SET mytime=%%a%%b
SET CURRENT_DATE_TIME=%yyyy%%mm%%dd%%mytime%
SET ASSEMBLY_VERSION=99.%yyyy%.%mm%%dd%.%mytime%
ECHO Version:%CURRENT_DATE_TIME%
ECHO Assembly Version: %ASSEMBLY_VERSION%

pushd %~dp0

IF '%REBUILD%' == 'TRUE' (
    dotnet restore %~dp0..\..\Azure.Monitor.OpenTelemetry.Profiler.sln
    dotnet build -c %CONFIG% --no-restore %~dp0..\..\Azure.Monitor.OpenTelemetry.Profiler.sln /p:AssemblyVersion=%ASSEMBLY_VERSION%
)

popd

GOTO EXIT

:HELP
ECHO Usage
SET HELPMSG="BuildSolution <Debug|Release> [TRUE|FALSE]"
ECHO %HELPMSG%
GOTO EXIT

:EXIT
ENDLOCAL
