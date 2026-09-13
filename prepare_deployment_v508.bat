@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "MODELROOT=%~1"
if "%MODELROOT%"=="" (
  echo Usage example:
  echo   prepare_deployment_v508.bat "E:\hydrological_modeling\ModelRoot"
  echo.
  set /p MODELROOT=Original model root: 
)

if not exist "%MODELROOT%\FloodAnalysisSYS.exe" (
  echo [ERROR] FloodAnalysisSYS.exe not found in: %MODELROOT%
  pause
  exit /b 2
)

if not exist "bin\Release\UnifiedHydroLauncher.exe" (
  echo [ERROR] bin\Release\UnifiedHydroLauncher.exe not found. Run build_release.bat first.
  pause
  exit /b 3
)

rem Reuse the verified Workflow config. Search active layout first, then common backup folders.
set "OLDCFG="
if exist "%MODELROOT%\UnifiedHydroWorkflow\UnifiedHydroWorkflow.exe.config" set "OLDCFG=%MODELROOT%\UnifiedHydroWorkflow\UnifiedHydroWorkflow.exe.config"
if not defined OLDCFG if exist "%MODELROOT%\UnifiedHydroWorkflow.exe.config" set "OLDCFG=%MODELROOT%\UnifiedHydroWorkflow.exe.config"
if not defined OLDCFG if exist "%MODELROOT%\UnifiedHydroWorkflow_V507_backup\UnifiedHydroWorkflow.exe.config" set "OLDCFG=%MODELROOT%\UnifiedHydroWorkflow_V507_backup\UnifiedHydroWorkflow.exe.config"
if not defined OLDCFG if exist "%MODELROOT%\UnifiedHydroWorkflow_backup\UnifiedHydroWorkflow.exe.config" set "OLDCFG=%MODELROOT%\UnifiedHydroWorkflow_backup\UnifiedHydroWorkflow.exe.config"
if not defined OLDCFG (
  for /d %%D in ("%MODELROOT%\UnifiedHydroWorkflow*_backup") do (
    if exist "%%~fD\UnifiedHydroWorkflow.exe.config" if not defined OLDCFG set "OLDCFG=%%~fD\UnifiedHydroWorkflow.exe.config"
  )
)

if not defined OLDCFG (
  echo [ERROR] Existing UnifiedHydroWorkflow.exe.config was not found.
  echo.
  echo Keep the old UnifiedHydroWorkflow folder until this deployment package has been created,
  echo or place its UnifiedHydroWorkflow.exe.config back under the model root temporarily.
  echo V5.0.8 reuses the verified core configuration file intentionally.
  pause
  exit /b 4
)

echo [INFO] Workflow config source:
echo        %OLDCFG%

set "COREOUT=%~dp0WorkflowCore\bin"
call "%~dp0WorkflowCore\build_workflow_v34.bat" "%MODELROOT%" "%COREOUT%"
if errorlevel 1 (
  echo [ERROR] Workflow core build failed. Deployment stopped.
  pause
  exit /b 5
)

set "DIST=%~dp0dist\UnifiedHydroWorkflow"
if exist "%DIST%" rmdir /s /q "%DIST%"
mkdir "%DIST%"

copy /y "bin\Release\UnifiedHydroLauncher.exe" "%DIST%\" >nul
if exist "bin\Release\UnifiedHydroLauncher.exe.config" copy /y "bin\Release\UnifiedHydroLauncher.exe.config" "%DIST%\" >nul
if exist "bin\Release\UnifiedHydroLauncher.pdb" copy /y "bin\Release\UnifiedHydroLauncher.pdb" "%DIST%\" >nul
copy /y "UnifiedHydroPreprocess_ArcGISPro.py" "%DIST%\" >nul
copy /y "UnifiedHydroPreprocess_ArcMap.py" "%DIST%\" >nul
copy /y "UnifiedHydroPreprocess_QGIS.py" "%DIST%\" >nul
xcopy /e /i /y "PreprocessTemplates" "%DIST%\PreprocessTemplates" >nul
copy /y "%COREOUT%\UnifiedHydroWorkflow.exe" "%DIST%\UnifiedHydroWorkflow.exe" >nul
if exist "%COREOUT%\UnifiedHydroWorkflow.pdb" copy /y "%COREOUT%\UnifiedHydroWorkflow.pdb" "%DIST%\UnifiedHydroWorkflow.pdb" >nul
copy /y "%OLDCFG%" "%DIST%\UnifiedHydroWorkflow.exe.config" >nul
if exist "bin\Release\*.txt" copy /y "bin\Release\*.txt" "%DIST%\" >nul
if exist "README_V5_0_8.txt" copy /y "README_V5_0_8.txt" "%DIST%\" >nul
if exist "VERSION.txt" copy /y "VERSION.txt" "%DIST%\" >nul

if exist "%MODELROOT%\UnifiedHydroWorkflow.exe" (
  echo.
  echo [WARNING] A root-level UnifiedHydroWorkflow.exe exists:
  echo   %MODELROOT%\UnifiedHydroWorkflow.exe
  echo V5.0.8 uses the maintained nested V3.4 core. If the launcher reports a stale-core warning,
  echo back up or remove the old root-level copy, then run the nested launcher again.
)

echo.
echo [OK] V5.0.8 deployment folder created:
echo %DIST%
echo.
echo Now back up/rename the old model-root UnifiedHydroWorkflow folder, then copy this
echo whole dist\UnifiedHydroWorkflow folder into the original model root.
echo V5.0.8 does not use external capability marker files.
pause
