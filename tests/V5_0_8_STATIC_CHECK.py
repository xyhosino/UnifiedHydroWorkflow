from pathlib import Path
import hashlib
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]

def need(cond, msg):
    if not cond:
        raise AssertionError(msg)


def text(name):
    return (ROOT / name).read_text(encoding='utf-8-sig')


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def scan_csharp(path):
    """Small lexical sanity scan: comments/strings/chars + (), {}, [] balance."""
    s = Path(path).read_text(encoding='utf-8-sig')
    stack = []
    pairs = {')': '(', '}': '{', ']': '['}
    i = 0
    n = len(s)
    state = 'code'
    line = 1
    while i < n:
        c = s[i]
        nxt = s[i + 1] if i + 1 < n else ''
        if c == '\n':
            line += 1
        if state == 'code':
            if c == '/' and nxt == '/':
                state = 'linecomment'; i += 2; continue
            if c == '/' and nxt == '*':
                state = 'blockcomment'; i += 2; continue
            if c == '@' and nxt == '"':
                state = 'verbatim'; i += 2; continue
            if c == '"':
                state = 'string'; i += 1; continue
            if c == "'":
                state = 'char'; i += 1; continue
            if c in '({[':
                stack.append((c, line))
            elif c in ')}]':
                need(stack and stack[-1][0] == pairs[c], f'{path.name}:{line}: unbalanced {c}')
                stack.pop()
            i += 1; continue
        if state == 'linecomment':
            if c == '\n':
                state = 'code'
            i += 1; continue
        if state == 'blockcomment':
            if c == '*' and nxt == '/':
                state = 'code'; i += 2; continue
            i += 1; continue
        if state == 'string':
            need(c != '\n', f'{path.name}:{line}: newline inside regular string literal')
            if c == '\\':
                i += 2; continue
            if c == '"':
                state = 'code'
            i += 1; continue
        if state == 'char':
            need(c != '\n', f'{path.name}:{line}: newline inside char literal')
            if c == '\\':
                i += 2; continue
            if c == "'":
                state = 'code'
            i += 1; continue
        if state == 'verbatim':
            if c == '"' and nxt == '"':
                i += 2; continue
            if c == '"':
                state = 'code'
            i += 1; continue
    need(state in ('code', 'linecomment'), f'{path.name}: unfinished lexical state {state}')
    need(not stack, f'{path.name}: unclosed delimiter {stack[-1] if stack else ""}')


# Release identity and centralized application metadata.
app = text('AppInfo.cs')
for required in [
    'internal const string Version = "5.0.8"',
    'internal const string DisplayVersion = "V" + Version',
    'internal const string WorkflowCoreVersion = "3.4"',
    'https://github.com/xyhosino/UnifiedHydroWorkflow',
    'internal const string GitHubReleasesUrl = GitHubUrl + "/releases"',
    'releases/latest',
    'Unified_Hydro_Workflow_V5.0.8_简明使用说明.txt'
]:
    need(required in app, 'AppInfo missing: ' + required)
need('Text = AppInfo.WindowTitle;' in text('MainForm.cs'), 'main title does not use AppInfo')
need('AppInfo.DisplayVersion + " 数据预处理（多 GIS 引擎）"' in text('PreprocessingForm.cs'),
     'preprocess title does not use AppInfo')
need('AppInfo.LauncherProduct' in text('AssemblyInfo.cs') and
     'AppInfo.AssemblyVersion' in text('AssemblyInfo.cs'),
     'assembly version does not use AppInfo')
need(not re.search(r'V5\.0\.\d+\.\d+', '\n'.join(
    p.read_text(encoding='utf-8-sig', errors='ignore')
    for p in ROOT.glob('*') if p.is_file() and p.suffix.lower() in {'.cs', '.py', '.txt', '.bat'}
)), 'four-part user-facing V5 release number remains in active top-level files')

# Help/About UI, deep diagnostics and GitHub Release update check.
help_cs = text('MainForm.Help.cs')
for required in [
    'new ToolStripMenuItem("帮助")',
    'new ToolStripMenuItem("使用说明")',
    'new ToolStripMenuItem("运行诊断")',
    'new ToolStripMenuItem("检查更新")',
    'new ToolStripMenuItem("关于")',
    'BuildDiagnosticReport()',
    '复制诊断信息',
    '优先级：ArcGIS Pro → ArcMap → QGIS',
    'RunWorkflowCoreDiagnostic()',
    'WorkflowRuntimeStager',
    'RedirectStandardError = true',
    'Core 自报版本：V',
    'GetWindowsDisplayName()',
    'QueryLatestGitHubRelease()',
    'GitHubLatestReleaseApiUrl',
    'http.UserAgent = "UnifiedHydroWorkflow/" + AppInfo.Version',
    'http.Accept = _accept',
    'application/vnd.github+json',
    'X-GitHub-Api-Version',
    'HttpStatusCode.Forbidden',
    'HttpStatusCode.NotFound',
    'TryParseThreePartVersion',
    '程序只检查版本，不会自动下载或覆盖现有文件。',
    'AppInfo.GitHubUrl',
    'Workflow Core：V',
    'AppInfo.HelpFileName'
]:
    need(required in help_cs, 'help/update UI missing: ' + required)
need('new ToolStripMenuItem("GitHub 仓库")' not in help_cs,
     'duplicate GitHub repository menu remains; repository link should stay in About only')
need('shell.Controls.Add(BuildMainMenu(), 0, 0);' in text('MainForm.cs'),
     'main menu not mounted in main window')

need('QueryWorkflowCapabilities(workflowPath)' not in help_cs,
     'diagnostics still query nested Workflow Core directly instead of staging it')
need('Environment.OSVersion.VersionString' in help_cs and 'GetWindowsDisplayName()' in help_cs,
     'Windows diagnostic fallback/display logic missing')
need('(SecurityProtocolType)3072' in help_cs,
     'GitHub update check does not enable TLS 1.2 on .NET 4.0')

# Project includes new source/help content.
proj = text('UnifiedHydroLauncher.csproj')
for required in [
    '<Compile Include="AppInfo.cs" />',
    '<Compile Include="MainForm.Help.cs" />',
    '<Content Include="Unified_Hydro_Workflow_V5.0.8_简明使用说明.txt">'
]:
    need(required in proj, 'project include missing: ' + required)
ET.parse(str(ROOT / 'UnifiedHydroLauncher.csproj'))

# GIS automatic selection priority is intentionally ArcGIS Pro -> ArcMap -> QGIS.
pre = text('PreprocessingForm.cs')
pro_pos = pre.find('string arcgisPro = FindArcGisPython();')
arcmap_pos = pre.find('string arcgisDesktop = GetCachedArcGisDesktopPython();')
qgis_pos = pre.find('string qgis = FindQgisPythonLauncher();')
need(pro_pos >= 0 and arcmap_pos > pro_pos and qgis_pos > arcmap_pos,
     'GIS auto-detection priority regressed; expected ArcGIS Pro -> ArcMap -> QGIS')
for required in [
    'internal static string FindArcGisPython()',
    'internal static string FindArcGisDesktopPython()',
    'internal static string FindQgisPythonLauncher()',
    'ArcGIS*_envs',
    'AddArcMapDevelopmentCandidates',
    'IsVerifiedArcMapPython',
    'sys.version_info[0]',
    'arcpy.GetInstallInfo()',
    'product.Contains("pro")',
    'LooksLikeArcGisProRuntime(full)',
    'rootName.IndexOf("ArcGISPro"'
]:
    need(required in pre, 'ArcMap detector regression/missing validation: ' + required)
need('if (File.Exists(full)) return full;' not in pre[pre.find('internal static string FindArcGisDesktopPython()'):pre.find('internal static string FindQgisPythonLauncher()')],
     'ArcMap detector accepts first existing python.exe without validation')

# Preserve V5.0.7 stable core behavior.
main = text('MainForm.cs')
for required in [
    'BuildPreflightFingerprint()', 'PREFLIGHT_CACHE reuse=1', '--skip-preflight',
    'PREFLIGHT_REUSED total=', 'QueryWorkflowCapabilities', 'CAPABILITY ',
    'CaptureSuccessfulPreflightStates()', 'RestoreSuccessfulPreflightStates()',
    'SOURCE_EXTERNAL.', '.prj=', '.cpg='
]:
    need(required in main, 'stable preflight behavior missing: ' + required)
need('UnifiedHydroWorkflow.preflight-reuse.v33' not in main,
     'launcher depends on legacy capability marker')

inp = text('InputDiscovery.cs')
for required in ['ReadShapeType', 'IsPolygon', 'IsPolyline', 'IsPoint',
                 'candidate.Fields.Contains("WSCD")',
                 'candidate.Fields.Contains("RVCD")',
                 'candidate.Fields.Contains("NDCD")']:
    need(required in inp, 'input mapping feature missing: ' + required)

maint = text('MaintenanceManager.cs')
for required in ['project_rollback_current.db', 'project_last_good.db',
                 'RecoverInterruptedRollback', 'RestoreFailedRun',
                 'CleanupKnownTemporaryFiles']:
    need(required in maint, 'maintenance feature missing: ' + required)

core = text('WorkflowCore/UnifiedHydroWorkflow.cs')
for required in [
    '--skip-preflight', '--input-map', 'PREFLIGHT_REUSED total=',
    'DATABASE_ROLLBACK_READY', 'project_rollback_current.db', '--capabilities',
    'WORKFLOW_CORE_VERSION 3.4', 'CAPABILITY skip-preflight',
    'Path.GetFileNameWithoutExtension(unit.Wata)',
    'SHPImport.ReadWata(unit.Wata, wataLayerName, 1)',
    'SHPImport.ReadRivl(unit.River, riverLayerName, 1)',
    'SHPImport.ReadNode(unit.Node, nodeLayerName, 1)',
    'CopyShape(unit.Wata, targetFolder, "Wata")'
]:
    need(required in core, 'workflow core stable feature missing: ' + required)
need('CopyShape(unit.Folder, "wata", targetFolder, "Wata")' not in core,
     'mapped SHP filename hotfix regressed')
need('project_before_unified_' not in core,
     'workflow core creates legacy timestamp backups')
need('Unified bottom-level workflow' not in core,
     'workflow core still writes custom project description')
need('_projectName, _projectFolder, "");' in core,
     'new project description is not blank')
need('Workflow V3.2' not in main,
     'stale Workflow V3.2 launcher message remains')
need('AppInfo.WorkflowCoreVersion' in main,
     'launcher stale-core message is not centralized')

# Deployment V5.0.8.
deploy = text('prepare_deployment_v508.bat')
for required in [
    'build_workflow_v34.bat',
    'if exist "bin\\Release\\*.txt"',
    'README_V5_0_8.txt',
    '[OK] V5.0.8 deployment folder created:'
]:
    need(required in deploy, 'deployment missing: ' + required)
need('UnifiedHydroWorkflow.preflight-reuse.v33' not in deploy,
     'deployment creates legacy capability marker')

# C# lexical sanity.
for p in list(ROOT.glob('*.cs')) + [ROOT / 'WorkflowCore' / 'UnifiedHydroWorkflow.cs']:
    scan_csharp(p)

# Preserve validated preprocessing templates by fixed SHA-256.
EXPECTED_TEMPLATES = {
    'node.cpg':'3ad3031f5503a4404af825262ee8232cc04d4ea6683d42c5dd0a2f2a27ac9824',
    'node.dbf':'49c8852769026c3e07153aee72bec6780a61bd2775305accbb6fbd2bf76e728b',
    'node.prj':'a02a27b1d1982c8516d83398e85a3c8b1aef1713c13ef4d84d7bde17430c07c4',
    'node.shp':'b3972d142bfe06c9ccef01cd9d6c7a491d129df48c68c998b5ac7e84558acd8e',
    'node.shx':'5b276376407d216cf398267ec7e4bbc7bb553c3d1960345cc2d229c6fb8d20c6',
    'rivl.cpg':'3ad3031f5503a4404af825262ee8232cc04d4ea6683d42c5dd0a2f2a27ac9824',
    'rivl.dbf':'a9488299e9bed0c32b52c519d4aa9360579aff129666333bfbc87db917a108b6',
    'rivl.prj':'a02a27b1d1982c8516d83398e85a3c8b1aef1713c13ef4d84d7bde17430c07c4',
    'rivl.shp':'6cb451d78fec75c2d3669705924fc4a3241ebf5f0bee55b561d96ccaaec25e89',
    'rivl.shx':'4652c4852b73ca539b11aecbb3c29bb5ff265c6da4ce4c59c822c3f984026fb8',
    'wata.cpg':'3ad3031f5503a4404af825262ee8232cc04d4ea6683d42c5dd0a2f2a27ac9824',
    'wata.dbf':'1f4107f58e63b5d8fdab98f2e8fb7c8582d205205914b38586d269a065149216',
    'wata.prj':'a02a27b1d1982c8516d83398e85a3c8b1aef1713c13ef4d84d7bde17430c07c4',
    'wata.shp':'432f7ebde5f9a1442ff804d427880637d6e3d303b7431cf9a8b7f3336c82b666',
    'wata.shx':'ad4481392f3e17cafb6a6f733077538082a112dc434ab7d10351386a359954a0',
}
for name, expected in EXPECTED_TEMPLATES.items():
    path = ROOT / 'PreprocessTemplates' / name
    need(path.exists() and sha(path) == expected, 'template changed: ' + name)

# Validated worker logic must remain unchanged apart from version labels.
def normalized_worker_hash(path):
    value = Path(path).read_text(encoding='utf-8-sig')
    value = re.sub(r'V5\.0\.\d+(?:\.\d+)?', 'V5.X', value)
    return hashlib.sha256(value.encode('utf-8')).hexdigest()

EXPECTED_WORKERS = {
    'UnifiedHydroPreprocess_ArcGISPro.py':'4536ba8244fd5c83f2932576a599f70e678a90d32557e5acd485bdbba7abc76c',
    'UnifiedHydroPreprocess_ArcMap.py':'2542fe39cf22612b472e815ce7c214153614bf06b15a1ee16a0a8d5f0770571c',
    'UnifiedHydroPreprocess_QGIS.py':'5f3c5ac4540fbb661ddb83110d9a91feb3a1472f4c3f705d133c28820a785449',
}
for name, expected in EXPECTED_WORKERS.items():
    need(normalized_worker_hash(ROOT / name) == expected,
         'worker logic changed unexpectedly: ' + name)

need((ROOT / 'Unified_Hydro_Workflow_V5.0.8_简明使用说明.txt').exists(),
     'user guide file missing')
need(text('VERSION.txt').strip() == 'Unified Hydro Workflow V5.0.8',
     'VERSION.txt mismatch')

# Windows batch packaging checks: CMD scripts must be BOM-free CRLF.
for rel in [
    'build_release.bat',
    'prepare_deployment_v508.bat',
    'WorkflowCore/build_workflow_v34.bat',
]:
    raw = (ROOT / rel).read_bytes()
    need(not raw.startswith(b'\xef\xbb\xbf'), rel + ' must not contain UTF-8 BOM')
    need(b'\r\n' in raw, rel + ' must use Windows CRLF line endings')
    need(raw.count(b'\n') == raw.count(b'\r\n'), rel + ' contains LF-only line endings')


print('V5.0.8 static check: PASS')
