@echo off
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo .NET Framework C# compiler was not found.
    exit /b 1
)
if not exist dist mkdir dist
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /out:"dist\SpeedtestPortable.exe" /resource:"bin\windows\speedtest.exe",speedtest.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll SpeedtestBackstageGui.cs
if errorlevel 1 exit /b %errorlevel%
echo Built dist\SpeedtestPortable.exe
