@echo off
cd /d "%~dp0"
if not exist "artifacts\set-review\Release\net10.0-windows\AnoWowItemEditor.dll" (
  dotnet build -c Release -p:BaseOutputPath=artifacts/set-review/
  if errorlevel 1 exit /b 1
)
dotnet "artifacts\set-review\Release\net10.0-windows\AnoWowItemEditor.dll"
