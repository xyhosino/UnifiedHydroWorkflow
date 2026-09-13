from pathlib import Path
import hashlib
import re
import runpy

ROOT = Path(__file__).resolve().parents[1]


def need(condition, message):
    if not condition:
        raise AssertionError(message)


# First run the deeper project/static regression checks.
runpy.run_path(str(ROOT / 'tests' / 'V5_0_8_STATIC_CHECK.py'), run_name='__release_static__')

# Required source/release inputs.
required = [
    'AppInfo.cs',
    'MainForm.cs',
    'MainForm.Help.cs',
    'PreprocessingForm.cs',
    'WorkflowRuntimeStager.cs',
    'WorkflowCore/UnifiedHydroWorkflow.cs',
    'WorkflowCore/build_workflow_v34.bat',
    'prepare_deployment_v508.bat',
    'build_release.bat',
    'UnifiedHydroPreprocess_ArcGISPro.py',
    'UnifiedHydroPreprocess_ArcMap.py',
    'UnifiedHydroPreprocess_QGIS.py',
    'Unified_Hydro_Workflow_V5.0.8_简明使用说明.txt',
]
for rel in required:
    need((ROOT / rel).exists(), 'required file missing: ' + rel)

# No compiled/proprietary model artifacts should be present in the source package.
for path in ROOT.rglob('*'):
    if not path.is_file():
        continue
    rel = path.relative_to(ROOT).as_posix()
    lower = path.name.lower()
    need(lower not in {'floodanalysissys.exe', 'project.db', 'sysdal.dll', 'dbsupport.dll',
                       'sysmodel.dll', 'dataimport.dll', 'writedataparams.dll'},
         'proprietary/runtime model artifact found: ' + rel)
    need(path.suffix.lower() not in {'.exe', '.dll'},
         'compiled binary unexpectedly included in source package: ' + rel)

# Active source must not contain model-root/dev-container hardcoding.
active_suffixes = {'.cs', '.py', '.bat', '.ps1'}
for path in ROOT.rglob('*'):
    if not path.is_file() or path.suffix.lower() not in active_suffixes:
        continue
    rel = path.relative_to(ROOT).as_posix()
    if rel.startswith('tests/baseline/') or rel == 'tests/V5_0_8_RELEASE_CHECK.py':
        continue
    text = path.read_text(encoding='utf-8-sig', errors='ignore')
    need('/mnt/data/' not in text, 'container path leaked into source: ' + rel)
    need('FFMSV4_DEV' not in text, 'specific model-root folder hardcoded in active source: ' + rel)

# User-facing V5 version numbers are limited to three numeric components.
for path in ROOT.rglob('*'):
    if not path.is_file() or path.suffix.lower() not in {'.cs', '.py', '.bat', '.txt'}:
        continue
    rel = path.relative_to(ROOT).as_posix()
    if rel.startswith('tests/baseline/'):
        continue
    text = path.read_text(encoding='utf-8-sig', errors='ignore')
    need(re.search(r'\bV5\.\d+\.\d+\.\d+\b', text) is None,
         'four-part user-facing V5 version found: ' + rel)

# Windows CMD files: pure ASCII, no BOM, CRLF only.
for rel in ['build_release.bat', 'prepare_deployment_v508.bat', 'WorkflowCore/build_workflow_v34.bat']:
    raw = (ROOT / rel).read_bytes()
    need(not raw.startswith(b'\xef\xbb\xbf'), rel + ': UTF-8 BOM found')
    need(all(byte < 128 for byte in raw), rel + ': non-ASCII byte found')
    need(raw.count(b'\n') == raw.count(b'\r\n'), rel + ': LF-only newline found')

# Regression signatures for this V5.0.8 corrective build.
help_cs = (ROOT / 'MainForm.Help.cs').read_text(encoding='utf-8-sig')
pre = (ROOT / 'PreprocessingForm.cs').read_text(encoding='utf-8-sig')
core = (ROOT / 'WorkflowCore' / 'UnifiedHydroWorkflow.cs').read_text(encoding='utf-8-sig')
for token in [
    'RunWorkflowCoreDiagnostic()',
    'WorkflowRuntimeStager(_deployment)',
    'RedirectStandardError = true',
    'Core 自报版本：V',
    'GetWindowsDisplayName()',
    'http.UserAgent = "UnifiedHydroWorkflow/" + AppInfo.Version',
    'http.Accept = _accept',
    'application/vnd.github+json',
    'X-GitHub-Api-Version',
    'HttpStatusCode.NotFound',
    'HttpStatusCode.Forbidden',
]:
    need(token in help_cs, 'corrective diagnostic/update feature missing: ' + token)
for token in [
    'ArcGIS*_envs', 'AddArcMapDevelopmentCandidates',
    'IsVerifiedArcMapPython', 'sys.version_info[0]', 'arcpy.GetInstallInfo()',
    'product.Contains("pro")', 'LooksLikeArcGisProRuntime(full)',
    'rootName.IndexOf("ArcGISPro"'
]:
    need(token in pre, 'ArcMap verified-detection feature missing: ' + token)
need('_projectName, _projectFolder, "");' in core,
     'new project description is not blank')

print('V5.0.8 release check: PASS')

# portable GIS detection is checked by V5_0_8_GIS_PORTABLE_DETECTION_CHECK.py
