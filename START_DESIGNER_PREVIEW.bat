@echo off
cd /d "%~dp0"
if not exist "artifacts\set-creator\Release\net10.0-windows\AnoWowItemEditor.dll" (
  dotnet build -c Release -p:BaseOutputPath=artifacts/set-creator/
  if errorlevel 1 exit /b 1
)
dotnet "artifacts\set-creator\Release\net10.0-windows\AnoWowItemEditor.dll"
