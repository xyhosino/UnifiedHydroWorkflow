using System;
using System.IO;

namespace UnifiedHydroLauncher
{
    internal sealed class DeploymentLayout
    {
        internal string ProgramDirectory = "";
        internal string ModelRoot = "";
        internal string WorkflowSourcePath = "";
        internal string WorkflowConfigSourcePath = "";
        internal bool IsNestedDeployment;
        internal bool IsDetected;
        internal string Error = "";

        internal static DeploymentLayout Detect(string baseDirectory)
        {
            var result = new DeploymentLayout();
            string programDirectory;
            try
            {
                programDirectory = Path.GetFullPath(baseDirectory ?? "").TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            }
            catch (Exception ex)
            {
                result.Error = "程序目录无效：" + ex.Message;
                return result;
            }

            result.ProgramDirectory = programDirectory;

            // 兼容旧版平铺部署：Launcher 直接放在原模型根目录。
            if (LooksLikeModelRoot(programDirectory))
            {
                result.ModelRoot = programDirectory;
                result.IsNestedDeployment = false;
            }
            else
            {
                DirectoryInfo parent = Directory.GetParent(programDirectory);
                string parentPath = parent == null ? "" : parent.FullName;
                if (parentPath.Length > 0 && LooksLikeModelRoot(parentPath))
                {
                    result.ModelRoot = parentPath;
                    result.IsNestedDeployment = true;
                }
            }

            string localWorkflow = Path.Combine(programDirectory, "UnifiedHydroWorkflow.exe");
            string localConfig = Path.Combine(programDirectory, "UnifiedHydroWorkflow.exe.config");

            if (File.Exists(localWorkflow))
            {
                result.WorkflowSourcePath = localWorkflow;
                result.WorkflowConfigSourcePath = localConfig;
            }
            else if (result.ModelRoot.Length > 0)
            {
                // 兼容 V5.0.3 及更早版本：Workflow 仍位于模型根目录。
                string legacyWorkflow = Path.Combine(result.ModelRoot, "UnifiedHydroWorkflow.exe");
                string legacyConfig = Path.Combine(result.ModelRoot, "UnifiedHydroWorkflow.exe.config");
                result.WorkflowSourcePath = legacyWorkflow;
                result.WorkflowConfigSourcePath = legacyConfig;
            }
            else
            {
                result.WorkflowSourcePath = localWorkflow;
                result.WorkflowConfigSourcePath = localConfig;
            }

            result.IsDetected = result.ModelRoot.Length > 0;
            if (!result.IsDetected)
            {
                result.Error =
                    "未识别到原水文模型根目录。请将整个 UnifiedHydroWorkflow 文件夹放到 " +
                    "FloodAnalysisSYS.exe 所在目录下。";
            }
            return result;
        }

        internal static bool LooksLikeModelRoot(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return false;

            return File.Exists(Path.Combine(directory, "FloodAnalysisSYS.exe")) &&
                   File.Exists(Path.Combine(directory, "project.db")) &&
                   File.Exists(Path.Combine(directory, "SysDAL.dll")) &&
                   Directory.Exists(Path.Combine(directory, "SKBYEXE"));
        }
    }
}
