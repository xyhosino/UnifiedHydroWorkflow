@echo off
setlocal EnableExtensions
cd /d "%~dp0"

if /i "%~1"=="--refresh" goto REFRESH

set "MODELROOT=%~1"
if "%MODELROOT%"=="" (
  echo Usage example:
  echo   prepare_deployment_v510.bat "E:\hydrological_modeling\ModelRoot"
  echo   prepare_deployment_v510.bat --refresh
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
  echo V5.1.0 reuses the verified core configuration file intentionally.
  pause
  exit /b 4
)

echo [INFO] Workflow config source:
echo        %OLDCFG%

set "COREOUT=%~dp0WorkflowCore\bin"
call "%~dp0WorkflowCore\build_workflow_v35.bat" "%MODELROOT%" "%COREOUT%"
if errorlevel 1 (
  echo [ERROR] Workflow core build failed. Deployment stopped.
  pause
  exit /b 5
)

goto PACKAGE

:REFRESH
set "REFRESH=1"
set "COREOUT=%~dp0WorkflowCore\bin"
set "DIST=%~dp0dist\UnifiedHydroWorkflow"

if not exist "bin\Release\UnifiedHydroLauncher.exe" (
  echo [ERROR] bin\Release\UnifiedHydroLauncher.exe not found. Run build_release.bat first.
  exit /b 3
)

if not exist "%COREOUT%\UnifiedHydroWorkflow.exe" (
  echo [ERROR] Prebuilt Workflow core not found: %COREOUT%\UnifiedHydroWorkflow.exe
  exit /b 5
)

if not exist "%DIST%\UnifiedHydroWorkflow.exe.config" (
  echo [ERROR] Existing dist Workflow config not found:
  echo         %DIST%\UnifiedHydroWorkflow.exe.config
  exit /b 4
)

echo [INFO] Refreshing dist from existing compiled outputs.
echo [INFO] Preserving existing dist structure and Workflow config.

:PACKAGE
if not defined DIST set "DIST=%~dp0dist\UnifiedHydroWorkflow"

if not exist "bin\Release\Unified_Hydro_Workflow_V5.1.0_*.txt" (
  echo [ERROR] V5.1.0 help file not found in bin\Release. Run build_release.bat first.
  exit /b 6
)

if not exist "README_V5_1_0.txt" (
  echo [ERROR] README_V5_1_0.txt not found.
  exit /b 6
)

if not defined REFRESH if exist "%DIST%" rmdir /s /q "%DIST%"
if not exist "%DIST%" mkdir "%DIST%"

copy /y "bin\Release\UnifiedHydroLauncher.exe" "%DIST%\" >nul
if exist "bin\Release\UnifiedHydroLauncher.exe.config" copy /y "bin\Release\UnifiedHydroLauncher.exe.config" "%DIST%\" >nul
if exist "bin\Release\UnifiedHydroLauncher.pdb" copy /y "bin\Release\UnifiedHydroLauncher.pdb" "%DIST%\" >nul
copy /y "UnifiedHydroPreprocess_ArcGISPro.py" "%DIST%\" >nul
copy /y "UnifiedHydroPreprocess_ArcMap.py" "%DIST%\" >nul
copy /y "UnifiedHydroPreprocess_QGIS.py" "%DIST%\" >nul
xcopy /e /i /y "PreprocessTemplates" "%DIST%\PreprocessTemplates" >nul
copy /y "%COREOUT%\UnifiedHydroWorkflow.exe" "%DIST%\UnifiedHydroWorkflow.exe" >nul
if exist "%COREOUT%\UnifiedHydroWorkflow.pdb" copy /y "%COREOUT%\UnifiedHydroWorkflow.pdb" "%DIST%\UnifiedHydroWorkflow.pdb" >nul
if not defined REFRESH copy /y "%OLDCFG%" "%DIST%\UnifiedHydroWorkflow.exe.config" >nul
copy /y "bin\Release\Unified_Hydro_Workflow_V5.1.0_*.txt" "%DIST%\" >nul
copy /y "README_V5_1_0.txt" "%DIST%\" >nul
if exist "VERSION.txt" copy /y "VERSION.txt" "%DIST%\" >nul

if not defined REFRESH if exist "%MODELROOT%\UnifiedHydroWorkflow.exe" (
  echo.
  echo [WARNING] A root-level UnifiedHydroWorkflow.exe exists:
  echo   %MODELROOT%\UnifiedHydroWorkflow.exe
  echo V5.1.0 uses the maintained nested V3.5 core. If the launcher reports a stale-core warning,
  echo back up or remove the old root-level copy, then run the nested launcher again.
)

echo.
echo [OK] V5.1.0 deployment folder created:
echo %DIST%
echo.
echo Now back up/rename the old model-root UnifiedHydroWorkflow folder, then copy this
echo whole dist\UnifiedHydroWorkflow folder into the original model root.
echo V5.1.0 does not use external capability marker files.
if defined REFRESH exit /b 0
pause
