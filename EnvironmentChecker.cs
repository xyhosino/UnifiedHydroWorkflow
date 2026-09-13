using System;
using System.Collections.Generic;
using System.IO;

namespace UnifiedHydroLauncher
{
    internal sealed class EnvironmentCheckResult
    {
        internal readonly List<string> Details = new List<string>();
        internal readonly List<string> Log = new List<string>();
        internal int FatalCount;
        internal int PendingCount;
        internal string RuntimeDirectory = "";
        internal bool Passed { get { return FatalCount == 0 && PendingCount == 0; } }
        internal bool Pending { get { return FatalCount == 0 && PendingCount > 0; } }
    }

    // Read-only checks. File-system delegates permit safe tests without touching Windows DLLs.
    internal sealed class EnvironmentChecker
    {
        private readonly Func<string, bool> fileExists;
        private readonly Func<string, bool> directoryExists;
        private readonly string windows;
        private readonly bool is64BitWindows;

        internal EnvironmentChecker()
            : this(File.Exists, Directory.Exists,
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.Is64BitOperatingSystem) { }

        internal EnvironmentChecker(Func<string, bool> files, Func<string, bool> directories,
            string windowsDirectory, bool is64Bit)
        {
            fileExists = files;
            directoryExists = directories;
            windows = windowsDirectory;
            is64BitWindows = is64Bit;
        }

        internal bool HasVc2010X86(out string detail, out string log)
        {
            string systemDir = Path.Combine(windows, is64BitWindows ? "SysWOW64" : "System32");
            bool cr = fileExists(Path.Combine(systemDir, "msvcr100.dll"));
            bool cp = fileExists(Path.Combine(systemDir, "msvcp100.dll"));
            log = "ENV_VC2010_X86 " + (cr && cp ? "OK" : "FAIL")
                + " msvcr100.dll=" + (cr ? "exists" : "missing")
                + " msvcp100.dll=" + (cp ? "exists" : "missing");
            detail = cr && cp ? "Microsoft Visual C++ 2010 x86：正常"
                : "缺少 Microsoft Visual C++ 2010 SP1 Redistributable (x86)"
                + Environment.NewLine + "msvcr100.dll：" + (cr ? "存在" : "缺失")
                + Environment.NewLine + "msvcp100.dll：" + (cp ? "存在" : "缺失")
                + Environment.NewLine + "该组件是水文模型 GDAL/OGR 模块导入 SHP 数据所必需的。"
                + "即使 Windows 为 64 位，也必须安装 x86 版本。"
                + Environment.NewLine + "检查目录：" + systemDir;
            return cr && cp;
        }

        // V5.0.4 recommended deployment check.
        internal EnvironmentCheckResult Check(DeploymentLayout layout, string inputPath)
        {
            var result = new EnvironmentCheckResult();
            result.Log.Add("ENV_CHECK_BEGIN");

            string detail, log;
            bool hasVc = HasVc2010X86(out detail, out log);
            result.Details.Add(detail);
            result.Log.Add(log);
            if (!hasVc) result.FatalCount++;

            if (layout == null || !layout.IsDetected)
            {
                result.FatalCount++;
                result.Log.Add("ENV_MODEL_ROOT FAIL not_detected");
                result.Details.Add(layout == null
                    ? "未识别到原水文模型根目录。"
                    : layout.Error);
            }

            string model = layout == null ? "" : CheckDirectory(
                result, layout.ModelRoot, "MODEL", "模型根目录", false);
            string program = layout == null ? "" : CheckDirectory(
                result, layout.ProgramDirectory, "PROGRAM", "工作流程序目录", false);
            CheckDirectory(result, inputPath, "INPUT", "输入数据目录", true);

            if (model.Length > 0)
            {
                string[] modelFiles = {
                    "FloodAnalysisSYS.exe", "project.db", "SysDAL.dll", "DBSupport.dll",
                    "System.Data.SQLite.dll", "ogr_wrap.dll", "gdal_wrap.dll", "gdal110.dll"
                };
                foreach (string name in modelFiles)
                    CheckFile(result, model, name, "MODEL");

                string skby = Path.Combine(model, "SKBYEXE");
                bool skbyExists = directoryExists(skby);
                result.Log.Add("ENV_DIR " + (skbyExists ? "OK " : "FAIL ") + "SKBYEXE");
                result.Details.Add("SKBYEXE" + (skbyExists ? "：正常" : "：缺失"));
                if (!skbyExists) result.FatalCount++;
            }

            if (program.Length > 0)
            {
                string workflowPath = layout == null ? "" : layout.WorkflowSourcePath;
                bool workflowExists = workflowPath.Length > 0 && fileExists(workflowPath);
                result.Log.Add("ENV_WORKFLOW " + (workflowExists ? "OK" : "FAIL")
                    + " path=" + workflowPath);
                result.Details.Add("UnifiedHydroWorkflow.exe"
                    + (workflowExists ? "：正常（" + workflowPath + "）" : "：缺失"));
                if (!workflowExists) result.FatalCount++;

                string configPath = layout == null ? "" : layout.WorkflowConfigSourcePath;
                bool configExists = configPath.Length > 0 && fileExists(configPath);
                result.Log.Add("ENV_WORKFLOW_CONFIG " + (configExists ? "OK" : "FAIL")
                    + " path=" + configPath);
                result.Details.Add("UnifiedHydroWorkflow.exe.config"
                    + (configExists ? "：正常" : "：缺失"));
                if (!configExists) result.FatalCount++;
            }

            if (!hasVc)
            {
                result.RuntimeDirectory = FindRuntimeDirectory(model, program);
                result.Details.Add(result.RuntimeDirectory.Length > 0
                    ? "可打开 Runtime 文件夹，手动运行 vcredist_x86.exe；安装后请重新检查环境。"
                    : "请手动安装 VC++ 2010 SP1 x86；安装后请重新检查环境。");
            }

            if (result.Pending)
                result.Details.Insert(0, "参数未完整配置，请指定输入数据目录。");

            if (layout != null && layout.IsDetected)
            {
                result.Details.Insert(0,
                    "部署模式：" + (layout.IsNestedDeployment ? "独立程序文件夹" : "兼容旧版平铺")
                    + Environment.NewLine + "模型根目录：" + layout.ModelRoot
                    + Environment.NewLine + "程序目录：" + layout.ProgramDirectory);
                result.Log.Add("ENV_DEPLOYMENT mode="
                    + (layout.IsNestedDeployment ? "nested" : "legacy_flat")
                    + " model=" + layout.ModelRoot
                    + " program=" + layout.ProgramDirectory);
            }

            result.Log.Add("ENV_CHECK_SUMMARY fatal=" + result.FatalCount
                + " pending=" + result.PendingCount + " warning=0");
            return result;
        }

        // Keep the V4/V5.0.3 overload for the existing isolation tests.
        internal EnvironmentCheckResult Check(string modelDirectory, string inputPath)
        {
            var result = new EnvironmentCheckResult();
            result.Log.Add("ENV_CHECK_BEGIN");
            string detail, log;
            bool hasVc = HasVc2010X86(out detail, out log);
            result.Details.Add(detail);
            result.Log.Add(log);
            if (!hasVc) result.FatalCount++;

            string model = CheckDirectory(result, modelDirectory, "MODEL", "模型目录", false);
            CheckDirectory(result, inputPath, "INPUT", "输入数据目录", true);

            string[] files = { "UnifiedHydroWorkflow.exe", "UnifiedHydroWorkflow.exe.config",
                "project.db", "SysDAL.dll", "DBSupport.dll", "System.Data.SQLite.dll",
                "ogr_wrap.dll", "gdal_wrap.dll", "gdal110.dll" };
            if (model.Length == 0)
            {
                result.Log.Add("ENV_FILES SKIP invalid_model_path");
            }
            else
            {
                foreach (string name in files)
                {
                    bool exists = fileExists(Path.Combine(model, name));
                    result.Log.Add("ENV_FILE " + (exists ? "OK " : "FAIL ") + name);
                    result.Details.Add(name + (exists ? "：正常" : "：缺失"));
                    if (!exists) result.FatalCount++;
                    if (!exists && name == "UnifiedHydroWorkflow.exe")
                        result.Details.Add("请将 Launcher 与 UnifiedHydroWorkflow.exe 放在水文模型根目录");
                }
            }

            if (!hasVc)
            {
                result.RuntimeDirectory = FindRuntimeDirectory(model);
                result.Details.Add(result.RuntimeDirectory.Length > 0
                    ? "可打开 Runtime 文件夹，手动运行 vcredist_x86.exe；安装后请重新检查环境。"
                    : "请手动安装 VC++ 2010 SP1 x86；安装后请重新检查环境。");
            }
            if (result.Pending)
                result.Details.Insert(0, "参数未完整配置，请指定输入数据目录。");
            result.Log.Add("ENV_CHECK_SUMMARY fatal=" + result.FatalCount
                + " pending=" + result.PendingCount + " warning=0");
            return result;
        }

        private void CheckFile(EnvironmentCheckResult result, string directory,
            string name, string code)
        {
            bool exists = fileExists(Path.Combine(directory, name));
            result.Log.Add("ENV_" + code + " " + (exists ? "OK " : "FAIL ") + name);
            result.Details.Add(name + (exists ? "：正常" : "：缺失"));
            if (!exists) result.FatalCount++;
        }

        private string CheckDirectory(EnvironmentCheckResult result, string path, string code,
            string label, bool pendingWhenEmpty)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                if (pendingWhenEmpty)
                {
                    result.PendingCount++;
                    result.Log.Add("ENV_PATH_" + code + " PENDING not_configured");
                    result.Details.Add(label + "：待指定");
                }
                else
                {
                    result.FatalCount++;
                    result.Log.Add("ENV_PATH_" + code + " FAIL unavailable");
                    result.Details.Add(label + "：无法确定");
                }
                return "";
            }

            string full = "";
            try
            {
                full = Path.GetFullPath(path.Trim());
            }
            catch (Exception ex)
            {
                result.FatalCount++;
                result.Log.Add("ENV_PATH_" + code + " FAIL invalid_path");
                result.Details.Add(label + "路径无效：" + ex.Message);
                return "";
            }

            bool exists = full.Length > 0 && directoryExists(full);
            bool space = (path ?? "").IndexOf(' ') >= 0 || full.IndexOf(' ') >= 0;
            if (!exists)
            {
                result.FatalCount++;
                result.Log.Add("ENV_PATH_" + code + " FAIL missing_or_invalid");
                result.Details.Add(label + "不存在或路径无效：" + path);
            }
            if (space)
            {
                result.FatalCount++;
                result.Log.Add("ENV_PATH_" + code + " FAIL contains_space");
                result.Details.Add(label + "包含空格：" + path + Environment.NewLine
                    + "水文模型旧版运行组件不支持带空格路径，请移动到不含空格的目录。");
            }
            if (exists && !space)
            {
                result.Log.Add("ENV_PATH_" + code + " OK");
                result.Details.Add(label + "：正常（" + full + "）");
            }
            return full;
        }

        private string FindRuntimeDirectory(params string[] roots)
        {
            foreach (string root in roots)
            {
                if (string.IsNullOrEmpty(root)) continue;
                string directory = Path.Combine(root, "Runtime");
                if (fileExists(Path.Combine(directory, "vcredist_x86.exe"))) return directory;
            }
            return "";
        }
    }
}
