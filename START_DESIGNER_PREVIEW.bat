@echo off
cd /d "%~dp0"
if not exist "artifacts\character-ready-20261025\Release\net10.0-windows\AnoWowItemEditor.dll" (
  dotnet build -c Release -p:BaseOutputPath=artifacts/character-ready-20261025/
  if errorlevel 1 exit /b 1
)
dotnet "artifacts\character-ready-20261025\Release\net10.0-windows\AnoWowItemEditor.dll"
