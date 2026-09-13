using System;
using System.IO;

namespace UnifiedHydroLauncher
{
    // V5.0.8 keeps the maintained Workflow core in the recommended nested
    // deployment beside the Launcher, and stages it into
    // the original model root only while it is executed. This preserves the
    // original model assembly/database lookup environment without scattering the
    // maintained copy through the model root.
    internal sealed class WorkflowRuntimeStager
    {
        private readonly DeploymentLayout layout;
        private readonly string markerPath;
        private readonly string legacyMarkerPath;
        private bool stagedByThisInstance;

        internal WorkflowRuntimeStager(DeploymentLayout deployment)
        {
            layout = deployment;
            markerPath = deployment == null || string.IsNullOrWhiteSpace(deployment.ModelRoot)
                ? ""
                : Path.Combine(deployment.ModelRoot, ".UnifiedHydroWorkflow.runtime");
            legacyMarkerPath = deployment == null || string.IsNullOrWhiteSpace(deployment.ModelRoot)
                ? ""
                : Path.Combine(deployment.ModelRoot, ".UnifiedHydroWorkflow.runtime.v504");
        }

        internal bool Ensure(out string executionPath, out string message)
        {
            executionPath = "";
            message = "";
            if (layout == null || !layout.IsDetected)
            {
                message = layout == null ? "无法确定部署结构。" : layout.Error;
                return false;
            }
            if (!File.Exists(layout.WorkflowSourcePath))
            {
                message = "未找到工作流核心：\r\n" + layout.WorkflowSourcePath;
                return false;
            }

            string targetExe = Path.Combine(layout.ModelRoot, "UnifiedHydroWorkflow.exe");
            string targetConfig = Path.Combine(layout.ModelRoot, "UnifiedHydroWorkflow.exe.config");

            // Launcher 与 Workflow 本来就在模型根目录：旧式平铺部署，直接使用。
            if (PathsEqual(layout.WorkflowSourcePath, targetExe))
            {
                executionPath = targetExe;
                message = "WORKFLOW_RUNTIME mode=legacy_flat path=" + targetExe;
                return true;
            }

            // 推荐的 nested 部署中，如果模型根目录还残留旧 Workflow，
            // 只有在它与子目录维护副本完全一致时才允许继续。这样可避免
            // 静默调用到旧核心。
            if (File.Exists(targetExe) && !RuntimeMarkerExists())
            {
                bool exeSame = FilesEqual(layout.WorkflowSourcePath, targetExe);
                bool configSame = ConfigFilesCompatible(
                    layout.WorkflowConfigSourcePath, targetConfig);
                if (!exeSame || !configSame)
                {
                    message =
                        "检测到模型根目录中的 UnifiedHydroWorkflow.exe 与子目录维护版不同。\r\n\r\n" +
                        "模型根目录：" + layout.ModelRoot + "\r\n" +
                        "维护版目录：" + layout.ProgramDirectory + "\r\n\r\n" +
                        "为避免误用旧核心，请先把模型根目录中的 UnifiedHydroWorkflow.exe" +
                        "（以及对应 config）改名备份或删除，再重新运行。";
                    return false;
                }

                executionPath = targetExe;
                message = "WORKFLOW_RUNTIME mode=legacy_flat_same_copy path=" + targetExe;
                return true;
            }

            try
            {
                // 清理上次异常退出遗留的临时副本。只有 marker 存在时才删除，
                // 绝不删除用户原本放在根目录的文件。
                if (RuntimeMarkerExists())
                {
                    SafeDelete(targetExe);
                    SafeDelete(targetConfig);
                    SafeDelete(markerPath);
                    SafeDelete(legacyMarkerPath);
                }

                File.Copy(layout.WorkflowSourcePath, targetExe, false);
                if (File.Exists(layout.WorkflowConfigSourcePath))
                    File.Copy(layout.WorkflowConfigSourcePath, targetConfig, false);

                File.WriteAllText(markerPath,
                    "V5.0.8 runtime staging\r\nsource=" + layout.WorkflowSourcePath +
                    "\r\ntime=" + DateTime.Now.ToString("s"));
                stagedByThisInstance = true;
                executionPath = targetExe;
                message = "WORKFLOW_RUNTIME mode=staged source=" + layout.WorkflowSourcePath +
                    " target=" + targetExe;
                return true;
            }
            catch (Exception ex)
            {
                // The target did not exist before staging in this branch; remove any
                // partial copy so the model root is not left in an ambiguous state.
                try { SafeDelete(targetExe); } catch { }
                try { SafeDelete(targetConfig); } catch { }
                try { SafeDelete(markerPath); } catch { }
                try { SafeDelete(legacyMarkerPath); } catch { }
                stagedByThisInstance = false;
                message = "工作流核心临时部署失败：\r\n" + ex.Message;
                return false;
            }
        }

        internal string Cleanup()
        {
            if (!stagedByThisInstance || string.IsNullOrEmpty(markerPath))
                return "WORKFLOW_RUNTIME_CLEANUP skipped";

            string targetExe = Path.Combine(layout.ModelRoot, "UnifiedHydroWorkflow.exe");
            string targetConfig = Path.Combine(layout.ModelRoot, "UnifiedHydroWorkflow.exe.config");
            try
            {
                SafeDelete(targetExe);
                SafeDelete(targetConfig);
                SafeDelete(markerPath);
                SafeDelete(legacyMarkerPath);
                stagedByThisInstance = false;
                return "WORKFLOW_RUNTIME_CLEANUP OK";
            }
            catch (Exception ex)
            {
                return "WORKFLOW_RUNTIME_CLEANUP WARN " + ex.Message;
            }
        }

        private bool RuntimeMarkerExists()
        {
            return (!string.IsNullOrEmpty(markerPath) && File.Exists(markerPath)) ||
                   (!string.IsNullOrEmpty(legacyMarkerPath) && File.Exists(legacyMarkerPath));
        }

        private static bool ConfigFilesCompatible(string source, string target)
        {
            bool sourceExists = File.Exists(source);
            bool targetExists = File.Exists(target);
            if (!sourceExists && !targetExists) return true;
            if (sourceExists != targetExists) return false;
            return FilesEqual(source, target);
        }

        private static bool FilesEqual(string first, string second)
        {
            try
            {
                var firstInfo = new FileInfo(first);
                var secondInfo = new FileInfo(second);
                if (!firstInfo.Exists || !secondInfo.Exists) return false;
                if (firstInfo.Length != secondInfo.Length) return false;

                const int bufferSize = 8192;
                byte[] left = new byte[bufferSize];
                byte[] right = new byte[bufferSize];
                using (FileStream a = File.OpenRead(first))
                using (FileStream b = File.OpenRead(second))
                {
                    while (true)
                    {
                        int aRead = a.Read(left, 0, left.Length);
                        int bRead = b.Read(right, 0, right.Length);
                        if (aRead != bRead) return false;
                        if (aRead == 0) return true;
                        for (int index = 0; index < aRead; index++)
                        {
                            if (left[index] != right[index]) return false;
                        }
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool PathsEqual(string a, string b)
        {
            try
            {
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static void SafeDelete(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
