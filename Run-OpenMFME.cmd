@echo off
cd /d "%~dp0"
dotnet run --project "src\OpenMFME.Desktop\OpenMFME.Desktop.csproj"
if errorlevel 1 pause
