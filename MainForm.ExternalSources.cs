using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UnifiedHydroLauncher
{
    public sealed partial class MainForm
    {
        private SourceAvailability EvaluateSourceAvailability(
            string unitFolder,
            ExternalSourceScan scan)
        {
            if (scan == null || !scan.Determined)
                return SourceAvailability.Unknown();
            if (!scan.RequiresExternalSource)
                return SourceAvailability.NotRequired();

            var result = new SourceAvailability();
            result.Required = scan.SourceRiverCodes.Count;
            if (result.Required == 0)
            {
                result.Required = scan.SourceSegmentCount;
                result.Missing = result.Required;
                return result;
            }

            foreach (string riverCode in scan.SourceRiverCodes)
            {
                string fileName = riverCode + "上游水源数据表.xls";
                string local = Path.Combine(unitFolder, fileName);
                if (File.Exists(local))
                {
                    result.Local++;
                    continue;
                }

                List<string> external = FindExternalSourceFiles(fileName);
                if (external.Count == 1)
                    result.External++;
                else if (external.Count > 1)
                    result.Ambiguous++;
                else
                    result.Missing++;
            }
            return result;
        }

        private List<string> FindExternalSourceFiles(string fileName)
        {
            var result = new List<string>();
            if (txtSourceExternalRoot == null ||
                string.IsNullOrWhiteSpace(txtSourceExternalRoot.Text))
                return result;

            string root = txtSourceExternalRoot.Text.Trim();
            if (!Directory.Exists(root))
                return result;

            try
            {
                result.AddRange(
                    Directory.GetFiles(root, "*.xls", SearchOption.AllDirectories)
                        .Where(path => string.Equals(
                            Path.GetFileName(path),
                            fileName,
                            StringComparison.OrdinalIgnoreCase)));
            }
            catch
            {
                return new List<string>();
            }

            return result
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private bool StageExternalSourceFilesForTargets(out string error)
        {
            error = "";
            if (txtSourceExternalRoot == null ||
                string.IsNullOrWhiteSpace(txtSourceExternalRoot.Text))
                return true;

            string root = txtSourceExternalRoot.Text.Trim();
            if (!Directory.Exists(root))
            {
                error = "上游水源外部目录不存在：\r\n" + root;
                return false;
            }

            foreach (string unitName in GetTargetUnitNames())
            {
                LauncherUnitInput input;
                if (!_resolvedUnitInputs.TryGetValue(unitName, out input))
                    continue;

                ExternalSourceScan scan = DetectExternalSourceTopology(
                    input.GetPath(InputRoles.River));
                if (!scan.Determined || !scan.RequiresExternalSource)
                    continue;

                foreach (string riverCode in scan.SourceRiverCodes)
                {
                    string fileName = riverCode + "上游水源数据表.xls";
                    string target = Path.Combine(input.UnitFolder, fileName);
                    if (File.Exists(target))
                        continue;

                    List<string> candidates = FindExternalSourceFiles(fileName);
                    if (candidates.Count == 0)
                    {
                        AppendLog("SOURCE_EXTERNAL_MISSING unit=" + unitName +
                            " file=" + fileName + " action=core_fallback");
                        continue;
                    }
                    if (candidates.Count > 1)
                    {
                        error = "上游水源外部目录中存在多个同名文件，无法自动判断：\r\n" +
                            fileName + "\r\n\r\n" +
                            string.Join("\r\n", candidates.Take(8).ToArray()) +
                            "\r\n\r\n请保留唯一文件，或将正确文件直接放入计算单元文件夹。";
                        return false;
                    }

                    try
                    {
                        File.Copy(candidates[0], target, false);
                        AppendLog("SOURCE_STAGED unit=" + unitName +
                            " file=" + fileName +
                            " from=" + candidates[0]);
                    }
                    catch (Exception exception)
                    {
                        error = "复制上游水源数据失败：\r\n" +
                            candidates[0] + "\r\n→\r\n" + target +
                            "\r\n\r\n" + exception.Message;
                        return false;
                    }
                }
            }

            RefreshInputTable();
            return true;
        }

        private sealed class SourceAvailability
        {
            internal bool Determined = true;
            internal bool RequiredByTopology = true;
            internal int Required;
            internal int Local;
            internal int External;
            internal int Missing;
            internal int Ambiguous;

            internal static SourceAvailability Unknown()
            {
                return new SourceAvailability
                {
                    Determined = false,
                    RequiredByTopology = false
                };
            }

            internal static SourceAvailability NotRequired()
            {
                return new SourceAvailability
                {
                    Determined = true,
                    RequiredByTopology = false
                };
            }

            internal int Available
            {
                get { return Local + External; }
            }

            internal string StatusText
            {
                get
                {
                    if (!Determined) return "未判定";
                    if (!RequiredByTopology) return "无需外部水源";
                    if (Required <= 0) return "需要/待生成";
                    if (Ambiguous > 0)
                        return "待确认 " + Available + "/" + Required;
                    if (Available >= Required)
                    {
                        if (External > 0 && Local > 0)
                            return "混合 " + Available + "/" + Required;
                        if (External > 0)
                            return "外部 " + Available + "/" + Required;
                        return "本地 " + Available + "/" + Required;
                    }
                    return "待生成 " + Available + "/" + Required;
                }
            }

            internal string NoteText
            {
                get
                {
                    var parts = new List<string>();
                    if (External > 0)
                        parts.Add("外部目录可补齐" + External + "张水源表");
                    if (Ambiguous > 0)
                        parts.Add("外部目录有" + Ambiguous + "张水源表存在同名歧义");
                    if (Missing > 0)
                        parts.Add("所需" + Missing + "张水源表将在运行时自动生成");
                    return string.Join("；", parts.ToArray());
                }
            }
        }
    }
}
