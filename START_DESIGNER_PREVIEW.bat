@echo off
cd /d "%~dp0"
if not exist "artifacts\modify-set-012\Release\net10.0-windows\WoWItemEditor.dll" (
  dotnet build src\WoWItemEditor\WoWItemEditor.csproj -c Release -o artifacts/modify-set-012/Release/net10.0-windows
  if errorlevel 1 exit /b 1
)
dotnet "artifacts\modify-set-012\Release\net10.0-windows\WoWItemEditor.dll"
