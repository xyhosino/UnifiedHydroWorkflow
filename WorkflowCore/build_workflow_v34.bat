@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "MODELROOT=%~1"
set "OUTDIR=%~2"
if "%MODELROOT%"=="" (
  echo [ERROR] Missing original model root.
  echo Usage: build_workflow_v34.bat "E:\hydrological_modeling\ModelRoot" [output-folder]
  exit /b 2
)
if "%OUTDIR%"=="" set "OUTDIR=%~dp0bin"

if not exist "%MODELROOT%\FloodAnalysisSYS.exe" (
  echo [ERROR] FloodAnalysisSYS.exe not found: %MODELROOT%
  exit /b 3
)

set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [ERROR] .NET Framework 4.0 C# compiler not found: %CSC%
  exit /b 4
)

for %%F in (
  Aspose.Cells.dll
  D8WataExtract.dll
  DataImport.dll
  hydrowatershed_csharp.dll
  PublicControl.dll
  SysDAL.dll
  SysModel.dll
  WriteDataParams.dll
) do (
  if not exist "%MODELROOT%\%%F" (
    echo [ERROR] Required model assembly not found: %MODELROOT%\%%F
    exit /b 5
  )
)

if not exist "%OUTDIR%" mkdir "%OUTDIR%"
if exist "%OUTDIR%\UnifiedHydroWorkflow.exe" del /q "%OUTDIR%\UnifiedHydroWorkflow.exe"
if exist "%OUTDIR%\UnifiedHydroWorkflow.pdb" del /q "%OUTDIR%\UnifiedHydroWorkflow.pdb"

"%CSC%" /nologo /target:exe /platform:x86 /optimize+ /debug:pdbonly /utf8output ^
  /out:"%OUTDIR%\UnifiedHydroWorkflow.exe" ^
  /reference:"%MODELROOT%\Aspose.Cells.dll" ^
  /reference:"%MODELROOT%\D8WataExtract.dll" ^
  /reference:"%MODELROOT%\DataImport.dll" ^
  /reference:"%MODELROOT%\hydrowatershed_csharp.dll" ^
  /reference:"%MODELROOT%\PublicControl.dll" ^
  /reference:"%MODELROOT%\SysDAL.dll" ^
  /reference:"%MODELROOT%\SysModel.dll" ^
  /reference:"%MODELROOT%\WriteDataParams.dll" ^
  /reference:System.Data.dll ^
  /reference:System.Data.DataSetExtensions.dll ^
  /reference:System.Xml.dll ^
  /reference:System.Configuration.dll ^
  "%~dp0UnifiedHydroWorkflow.cs"

if errorlevel 1 (
  echo [ERROR] UnifiedHydroWorkflow V3.4 build failed.
  exit /b 6
)

if not exist "%OUTDIR%\UnifiedHydroWorkflow.exe" (
  echo [ERROR] Compiler returned success but executable is missing.
  exit /b 7
)

echo [OK] UnifiedHydroWorkflow V3.4 built:
echo      %OUTDIR%\UnifiedHydroWorkflow.exe
exit /b 0
