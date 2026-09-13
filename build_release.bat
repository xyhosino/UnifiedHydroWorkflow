@echo off
setlocal
cd /d "%~dp0"

"C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe" ^
  "UnifiedHydroLauncher.csproj" ^
  /p:Configuration=Release ^
  /p:Platform=x86

echo.
pause
