
@echo off
copy *.nupkg %HOMEPATH%\dropbox\nuget\ /y

nuget push .\Plugin.SpeechRecognition\bin\Release\*.nupkg -Source https://www.nuget.org/api/v2/package
del .\Plugin.SpeechRecognition\bin\Release\*.nupkg

rem nuget push .\MvvmCross.Plugin.BluetoothLE\bin\Release\*.nupkg -Source https://www.nuget.org/api/v2/package
rem del .\MvvmCross.Plugin.BluetoothLE\bin\Release\*.nupkg
pause