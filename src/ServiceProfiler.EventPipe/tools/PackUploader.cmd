SETLOCAL
@ECHO OFF
SET CONFIG=%1
SET REBUILD=%2
IF '%CONFIG%' == '' GOTO HELP
IF '%REBUILD%' == '' SET REBUILD=TRUE

ECHO Target Configuration: %CONFIG%. ReBuild: %REBUILD%
SET TEMP_OUT=%~dp0..\Out
mkdir %TEMP_OUT%
ECHO Output Folder: %TEMP_OUT%

pushd %~dp0

IF '%REBUILD%' == 'TRUE' (
    dotnet restore %~dp0..\ServiceProfiler.EventPipe.Upload
    dotnet build -c %CONFIG% --no-restore %~dp0..\ServiceProfiler.EventPipe.Upload
    dotnet publish --no-restore --no-build %~dp0..\ServiceProfiler.EventPipe.Upload -c %CONFIG% -f net8.0
)

del %TEMP_OUT%\TraceUpload*.zip /Q
7za a -tzip %TEMP_OUT%\TraceUpload30.zip ..\ServiceProfiler.EventPipe.Upload\bin\%CONFIG%\net8.0\publish\*
dir %TEMP_OUT%\TraceUpload*.zip
popd

GOTO EXIT

:HELP
ECHO Usage
SET HELPMSG="PackUploader <Debug|Release> [TRUE|FALSE]"
ECHO %HELPMSG%
GOTO EXIT

:EXIT
ENDLOCAL
