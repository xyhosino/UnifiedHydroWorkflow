using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Microsoft.Win32;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Xml;

namespace UnifiedHydroLauncher
{
    public sealed partial class MainForm
    {
        private MenuStrip mainMenu;
        private ToolStripMenuItem menuCheckUpdates;
        private bool _updateCheckInProgress;
        private const int UpdateCheckCacheMinutes = 30;

        private sealed class UpdateCheckResult
        {
            internal bool Success;
            internal bool NoPublishedRelease;
            internal bool FromCache;
            internal bool UsedFallback;
            internal string LatestTag = "";
            internal string ReleaseUrl = "";
            internal string Error = "";
            internal string RateLimitResetLocal = "";
            internal DateTime CheckedAtLocal = DateTime.MinValue;
        }

        private sealed class CoreDiagnosticResult
        {
            internal bool Success;
            internal string ExecutionPath = "";
            internal string StageMessage = "";
            internal string CleanupMessage = "";
            internal string CoreVersion = "";
            internal string StandardError = "";
            internal string Error = "";
            internal int ExitCode = int.MinValue;
            internal readonly HashSet<string> Capabilities =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class TimeoutWebClient : WebClient
        {
            private readonly string _accept;

            internal TimeoutWebClient()
                : this("application/vnd.github+json")
            {
            }

            internal TimeoutWebClient(string accept)
            {
                _accept = string.IsNullOrWhiteSpace(accept) ? "*/*" : accept;
            }

            protected override WebRequest GetWebRequest(Uri address)
            {
                WebRequest request = base.GetWebRequest(address);
                if (request != null)
                    request.Timeout = 10000;

                HttpWebRequest http = request as HttpWebRequest;
                if (http != null)
                {
                    http.ReadWriteTimeout = 10000;
                    http.UserAgent = "UnifiedHydroWorkflow/" + AppInfo.Version;
                    http.Accept = _accept;
                    http.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                    if (address != null &&
                        string.Equals(address.Host, "api.github.com", StringComparison.OrdinalIgnoreCase))
                    {
                        try { http.Headers["X-GitHub-Api-Version"] = "2022-11-28"; } catch { }
                    }
                }
                return request;
            }
        }

        private MenuStrip BuildMainMenu()
        {
            mainMenu = new MenuStrip();
            mainMenu.Dock = DockStyle.Fill;
            mainMenu.Font = Font;
            mainMenu.Padding = new Padding(8, 2, 0, 2);

            var help = new ToolStripMenuItem("帮助");

            var guide = new ToolStripMenuItem("使用说明");
            guide.Click += ShowUserGuide;
            help.DropDownItems.Add(guide);

            var diagnostics = new ToolStripMenuItem("运行诊断");
            diagnostics.Click += ShowDiagnosticsFromMenu;
            help.DropDownItems.Add(diagnostics);

            help.DropDownItems.Add(new ToolStripSeparator());

            menuCheckUpdates = new ToolStripMenuItem("检查更新");
            menuCheckUpdates.Click += CheckForUpdatesFromMenu;
            help.DropDownItems.Add(menuCheckUpdates);

            var about = new ToolStripMenuItem("关于");
            about.Click += ShowAboutDialog;
            help.DropDownItems.Add(about);

            mainMenu.Items.Add(help);
            MainMenuStrip = mainMenu;
            return mainMenu;
        }

        private string GetUserGuidePath()
        {
            return Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                AppInfo.HelpFileName);
        }

        private void ShowUserGuide(object sender, EventArgs e)
        {
            string guidePath = GetUserGuidePath();
            string content;
            try
            {
                content = File.Exists(guidePath)
                    ? File.ReadAllText(guidePath, Encoding.UTF8)
                    : BuildFallbackGuide();
            }
            catch (Exception ex)
            {
                content = BuildFallbackGuide() +
                    Environment.NewLine + Environment.NewLine +
                    "说明文件读取失败：" + ex.Message;
            }

            using (var dialog = new Form())
            {
                dialog.Text = "使用说明 - " + AppInfo.WindowTitle;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.Size = new Size(900, 700);
                dialog.MinimumSize = new Size(720, 520);
                dialog.Font = Font;

                var root = new TableLayoutPanel();
                root.Dock = DockStyle.Fill;
                root.ColumnCount = 1;
                root.RowCount = 3;
                root.Padding = new Padding(12);
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
                dialog.Controls.Add(root);

                var title = new Label();
                title.AutoSize = true;
                title.Font = new Font(Font.FontFamily, 13F, FontStyle.Bold);
                title.Text = AppInfo.ProductName + " " + AppInfo.DisplayVersion + "  简明使用说明";
                title.Margin = new Padding(2, 2, 2, 10);
                root.Controls.Add(title, 0, 0);

                var text = new RichTextBox();
                text.Dock = DockStyle.Fill;
                text.ReadOnly = true;
                text.BackColor = Color.White;
                text.BorderStyle = BorderStyle.FixedSingle;
                text.Font = new Font("Microsoft YaHei UI", 9.5F);
                text.Text = content;
                text.SelectionStart = 0;
                text.SelectionLength = 0;
                root.Controls.Add(text, 0, 1);

                var actions = new FlowLayoutPanel();
                actions.Dock = DockStyle.Fill;
                actions.FlowDirection = FlowDirection.RightToLeft;
                actions.WrapContents = false;
                actions.Padding = new Padding(0, 8, 0, 0);
                root.Controls.Add(actions, 0, 2);

                var close = new Button();
                close.Text = "关闭";
                close.Width = 100;
                close.Height = 30;
                close.Click += delegate { dialog.Close(); };
                actions.Controls.Add(close);

                var openFile = new Button();
                openFile.Text = "打开说明文件";
                openFile.Width = 125;
                openFile.Height = 30;
                openFile.Enabled = File.Exists(guidePath);
                openFile.Click += delegate { OpenLocalDocument(guidePath); };
                actions.Controls.Add(openFile);

                dialog.ShowDialog(this);
            }
        }

        private static string BuildFallbackGuide()
        {
            return string.Join(
                Environment.NewLine,
                new[]
                {
                    "推荐使用流程",
                    "",
                    "1. 选择计算单元根目录并点击“扫描单元”。",
                    "2. 使用“智能映射（推荐）”，必要时打开“输入数据映射...”人工确认。",
                    "3. 设置工程名称、场次开始/结束时间、上游水源开始时间和计算时间间隔。",
                    "4. 先选择“仅预检”运行，确认全部计算单元通过。",
                    "5. 不修改关键输入和参数，切换到“计算”后正式运行。",
                    "6. 运行结束后检查状态表、运行日志和 SKBYEXE\\output 输出。",
                    "",
                    "注意：当前版本的“停止”会立即终止当前 Workflow 进程，仅在确实需要中断时使用。",
                    "如需完整说明，请确认程序目录中存在：",
                    AppInfo.HelpFileName
                });
        }

        private void ShowDiagnosticsFromMenu(object sender, EventArgs e)
        {
            Cursor previous = Cursor.Current;
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                CheckEnvironment(true);

                using (var dialog = new Form())
                {
                    dialog.Text = "运行诊断 - " + AppInfo.WindowTitle;
                    dialog.StartPosition = FormStartPosition.CenterParent;
                    dialog.Size = new Size(900, 680);
                    dialog.MinimumSize = new Size(720, 520);
                    dialog.Font = Font;

                    var root = new TableLayoutPanel();
                    root.Dock = DockStyle.Fill;
                    root.ColumnCount = 1;
                    root.RowCount = 2;
                    root.Padding = new Padding(12);
                    root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                    root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
                    dialog.Controls.Add(root);

                    var text = new TextBox();
                    text.Dock = DockStyle.Fill;
                    text.Multiline = true;
                    text.ReadOnly = true;
                    text.ScrollBars = ScrollBars.Both;
                    text.WordWrap = false;
                    text.Font = new Font("Consolas", 9.5F);
                    text.Text = BuildDiagnosticReport();
                    root.Controls.Add(text, 0, 0);

                    var actions = new FlowLayoutPanel();
                    actions.Dock = DockStyle.Fill;
                    actions.FlowDirection = FlowDirection.RightToLeft;
                    actions.WrapContents = false;
                    actions.Padding = new Padding(0, 8, 0, 0);
                    root.Controls.Add(actions, 0, 1);

                    var close = new Button();
                    close.Text = "关闭";
                    close.Width = 100;
                    close.Height = 30;
                    close.Click += delegate { dialog.Close(); };
                    actions.Controls.Add(close);

                    var copy = new Button();
                    copy.Text = "复制诊断信息";
                    copy.Width = 125;
                    copy.Height = 30;
                    copy.Click += delegate
                    {
                        try
                        {
                            Clipboard.SetText(text.Text);
                            MessageBox.Show(dialog, "诊断信息已复制到剪贴板。", "运行诊断",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(dialog, ex.Message, "无法复制诊断信息",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    };
                    actions.Controls.Add(copy);

                    var refresh = new Button();
                    refresh.Text = "重新诊断";
                    refresh.Width = 105;
                    refresh.Height = 30;
                    refresh.Click += delegate
                    {
                        try
                        {
                            dialog.UseWaitCursor = true;
                            CheckEnvironment(true);
                            text.Text = BuildDiagnosticReport();
                            text.SelectionStart = 0;
                            text.SelectionLength = 0;
                        }
                        finally
                        {
                            dialog.UseWaitCursor = false;
                        }
                    };
                    actions.Controls.Add(refresh);

                    dialog.ShowDialog(this);
                }
            }
            finally
            {
                Cursor.Current = previous;
            }
        }

        private string BuildDiagnosticReport()
        {
            var builder = new StringBuilder();
            builder.AppendLine(AppInfo.ProductName + " " + AppInfo.DisplayVersion);
            builder.AppendLine("Workflow Core（期望版本）：V" + AppInfo.WorkflowCoreVersion);
            builder.AppendLine("诊断时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine();

            builder.AppendLine("=== 程序与部署 ===");
            builder.AppendLine("程序目录：" + AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'));
            builder.AppendLine("进程架构：" + (IntPtr.Size == 4 ? "x86" : "x64"));
            builder.AppendLine("Windows：" + GetWindowsDisplayName());
            builder.AppendLine("操作系统架构：" + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit"));
            builder.AppendLine(".NET CLR：" + Environment.Version);

            if (_deployment == null)
                _deployment = DeploymentLayout.Detect(AppDomain.CurrentDomain.BaseDirectory);

            if (_deployment != null && _deployment.IsDetected)
            {
                builder.AppendLine("部署模式：" +
                    (_deployment.IsNestedDeployment ? "nested（独立程序文件夹）" : "legacy_flat（兼容旧版平铺）"));
                builder.AppendLine("模型根目录：" + _deployment.ModelRoot);
                builder.AppendLine("Workflow Core 文件：" + FormatFileStatus(_deployment.WorkflowSourcePath));
                builder.AppendLine("Workflow 配置：" + FormatFileStatus(_deployment.WorkflowConfigSourcePath));
            }
            else
            {
                builder.AppendLine("部署模式：未识别");
                builder.AppendLine("模型根目录：未识别");
                if (_deployment != null && !string.IsNullOrWhiteSpace(_deployment.Error))
                    builder.AppendLine("部署错误：" + _deployment.Error);
            }

            string inputRoot = txtUnitRoot == null ? "" : txtUnitRoot.Text.Trim();
            builder.AppendLine("输入数据目录：" +
                (inputRoot.Length == 0 ? "未设置" :
                    (Directory.Exists(inputRoot) ? inputRoot : inputRoot + "（不存在）")));
            builder.AppendLine();

            builder.AppendLine("=== GIS 自动检测 ===");
            builder.AppendLine("优先级：ArcGIS Pro → ArcMap → QGIS");
            string arcPro = PreprocessingForm.FindArcGisPython();
            string arcMap = PreprocessingForm.FindArcGisDesktopPython();
            string qgis = PreprocessingForm.FindQgisPythonLauncher();
            builder.AppendLine("ArcGIS Pro：" + FormatRuntimeStatus(arcPro));
            builder.AppendLine("ArcMap：" + FormatRuntimeStatus(arcMap));
            builder.AppendLine("QGIS：" + FormatRuntimeStatus(qgis));
            builder.AppendLine("自动选择：" +
                (!string.IsNullOrWhiteSpace(arcPro) ? "ArcGIS Pro" :
                 !string.IsNullOrWhiteSpace(arcMap) ? "ArcMap" :
                 !string.IsNullOrWhiteSpace(qgis) ? "QGIS" : "未检测到可用 GIS 引擎"));
            builder.AppendLine();

            builder.AppendLine("=== Workflow Core 能力 ===");
            if (_isRunning)
            {
                builder.AppendLine("能力查询：已跳过（当前计算正在运行，避免干扰 Workflow Core）");
            }
            else if (_deployment == null || !_deployment.IsDetected ||
                string.IsNullOrWhiteSpace(_deployment.WorkflowSourcePath) ||
                !File.Exists(_deployment.WorkflowSourcePath))
            {
                builder.AppendLine("能力查询：无法执行（Workflow Core 不存在或部署结构未识别）");
            }
            else
            {
                CoreDiagnosticResult core = RunWorkflowCoreDiagnostic();
                builder.AppendLine("维护副本：" + _deployment.WorkflowSourcePath);
                if (!string.IsNullOrWhiteSpace(core.ExecutionPath))
                    builder.AppendLine("诊断执行位置：" + core.ExecutionPath);
                if (!string.IsNullOrWhiteSpace(core.StageMessage))
                    builder.AppendLine("运行时准备：" + core.StageMessage);

                if (!string.IsNullOrWhiteSpace(core.CoreVersion))
                {
                    builder.AppendLine("Core 自报版本：V" + core.CoreVersion);
                    if (!string.Equals(core.CoreVersion, AppInfo.WorkflowCoreVersion,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        builder.AppendLine("版本一致性：不一致（期望 V" +
                            AppInfo.WorkflowCoreVersion + "）");
                    }
                    else
                    {
                        builder.AppendLine("版本一致性：OK");
                    }
                }
                else
                {
                    builder.AppendLine("Core 自报版本：无返回");
                }

                if (core.ExitCode != int.MinValue)
                    builder.AppendLine("能力查询退出码：" + core.ExitCode);

                if (core.Success)
                {
                    var capabilityList = new List<string>();
                    foreach (string capability in core.Capabilities)
                        capabilityList.Add(capability);
                    capabilityList.Sort(StringComparer.OrdinalIgnoreCase);
                    builder.AppendLine("能力查询：OK");
                    builder.AppendLine("Capabilities：" + string.Join(", ", capabilityList.ToArray()));
                }
                else
                {
                    builder.AppendLine("能力查询：失败");
                    if (!string.IsNullOrWhiteSpace(core.Error))
                        builder.AppendLine("失败原因：" + LimitDiagnosticText(core.Error));
                    if (!string.IsNullOrWhiteSpace(core.StandardError))
                        builder.AppendLine("stderr：" + LimitDiagnosticText(core.StandardError));
                }

                if (!string.IsNullOrWhiteSpace(core.CleanupMessage))
                    builder.AppendLine("运行时清理：" + core.CleanupMessage);
            }
            builder.AppendLine();

            builder.AppendLine("=== 运行环境检查详情 ===");
            builder.AppendLine(string.IsNullOrWhiteSpace(_environmentDetails)
                ? "暂无环境检查结果。"
                : _environmentDetails);

            return builder.ToString();
        }

        private CoreDiagnosticResult RunWorkflowCoreDiagnostic()
        {
            var result = new CoreDiagnosticResult();
            WorkflowRuntimeStager stager = null;
            try
            {
                if (_deployment == null)
                    _deployment = DeploymentLayout.Detect(AppDomain.CurrentDomain.BaseDirectory);
                if (_deployment == null || !_deployment.IsDetected)
                {
                    result.Error = _deployment == null ? "无法确定部署结构。" : _deployment.Error;
                    return result;
                }

                stager = new WorkflowRuntimeStager(_deployment);
                string executionPath;
                string stageMessage;
                if (!stager.Ensure(out executionPath, out stageMessage))
                {
                    result.StageMessage = stageMessage;
                    result.Error = stageMessage;
                    return result;
                }

                result.ExecutionPath = executionPath;
                result.StageMessage = stageMessage;

                var startInfo = new ProcessStartInfo();
                startInfo.FileName = executionPath;
                startInfo.Arguments = "--capabilities";
                startInfo.WorkingDirectory = _deployment.ModelRoot;
                startInfo.UseShellExecute = false;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                startInfo.CreateNoWindow = true;

                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        result.Error = "无法启动 Workflow Core。";
                        return result;
                    }

                    if (!process.WaitForExit(8000))
                    {
                        try { process.Kill(); } catch { }
                        result.Error = "Workflow Core 能力查询超时（8 秒）。";
                        return result;
                    }

                    string output = process.StandardOutput.ReadToEnd();
                    result.StandardError = process.StandardError.ReadToEnd().Trim();
                    result.ExitCode = process.ExitCode;

                    foreach (string rawLine in output.Split(
                        new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string line = rawLine.Trim();
                        const string versionPrefix = "WORKFLOW_CORE_VERSION ";
                        const string capabilityPrefix = "CAPABILITY ";
                        if (line.StartsWith(versionPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            result.CoreVersion = line.Substring(versionPrefix.Length).Trim();
                        }
                        else if (line.StartsWith(capabilityPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            string capability = line.Substring(capabilityPrefix.Length).Trim();
                            if (capability.Length > 0)
                                result.Capabilities.Add(capability);
                        }
                    }

                    result.Success = result.ExitCode == 0 &&
                        !string.IsNullOrWhiteSpace(result.CoreVersion) &&
                        result.Capabilities.Contains("capability-query");
                    if (!result.Success && string.IsNullOrWhiteSpace(result.Error))
                    {
                        result.Error = result.ExitCode == 0
                            ? "Workflow Core 已运行，但没有返回完整的版本/能力信息。"
                            : "Workflow Core 返回非零退出码。";
                    }
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }
            finally
            {
                if (stager != null)
                {
                    try { result.CleanupMessage = stager.Cleanup(); }
                    catch (Exception ex) { result.CleanupMessage = "WORKFLOW_RUNTIME_CLEANUP WARN " + ex.Message; }
                }
            }
            return result;
        }

        private static string GetWindowsDisplayName()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key != null)
                    {
                        string product = Convert.ToString(key.GetValue("ProductName"));
                        string display = Convert.ToString(key.GetValue("DisplayVersion"));
                        if (string.IsNullOrWhiteSpace(display))
                            display = Convert.ToString(key.GetValue("ReleaseId"));
                        string build = Convert.ToString(key.GetValue("CurrentBuildNumber"));
                        string ubr = Convert.ToString(key.GetValue("UBR"));

                        int buildNumber;
                        if (int.TryParse(build, out buildNumber) && buildNumber >= 22000 &&
                            !string.IsNullOrWhiteSpace(product) &&
                            product.IndexOf("Windows 10", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            product = Regex.Replace(product, "Windows 10", "Windows 11",
                                RegexOptions.IgnoreCase);
                        }

                        var parts = new List<string>();
                        if (!string.IsNullOrWhiteSpace(product)) parts.Add(product.Trim());
                        if (!string.IsNullOrWhiteSpace(display)) parts.Add(display.Trim());
                        string name = string.Join(" ", parts.ToArray()).Trim();
                        if (!string.IsNullOrWhiteSpace(build))
                        {
                            string fullBuild = build.Trim();
                            if (!string.IsNullOrWhiteSpace(ubr))
                                fullBuild += "." + ubr.Trim();
                            name += (name.Length == 0 ? "" : " ") + "(build " + fullBuild + ")";
                        }
                        if (name.Length > 0) return name;
                    }
                }
            }
            catch { }
            return Environment.OSVersion.VersionString;
        }

        private static string LimitDiagnosticText(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            string text = value.Trim().Replace("\r\n", " | ").Replace("\n", " | ").Replace("\r", " | ");
            const int maxLength = 1600;
            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
        }

        private static string FormatFileStatus(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "未设置";
            try
            {
                if (!File.Exists(path)) return "缺失（" + path + "）";
                var info = new FileInfo(path);
                return "OK（" + path + "，" + info.Length + " bytes，" +
                    info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") + "）";
            }
            catch (Exception ex)
            {
                return "检查失败（" + ex.Message + "）";
            }
        }

        private static string FormatRuntimeStatus(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? "未检测到" : "OK（" + path + "）";
        }

        private void CheckForUpdatesFromMenu(object sender, EventArgs e)
        {
            if (_updateCheckInProgress)
            {
                MessageBox.Show(this, "正在检查更新，请稍候。", "检查更新",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _updateCheckInProgress = true;
            UseWaitCursor = true;
            if (menuCheckUpdates != null) menuCheckUpdates.Enabled = false;
            AppendLog("UPDATE_CHECK_BEGIN source=github_releases");

            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateCheckResult result = QueryLatestGitHubRelease();
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        ShowUpdateCheckResult(result);
                    }));
                }
                catch
                {
                    _updateCheckInProgress = false;
                }
            });
        }

        private static UpdateCheckResult QueryLatestGitHubRelease()
        {
            UpdateCheckResult cached;
            if (TryReadUpdateCheckCache(out cached))
                return cached;

            var result = new UpdateCheckResult();
            try
            {
                EnableTls12ForGitHub();

                using (var client = new TimeoutWebClient())
                {
                    string json = client.DownloadString(AppInfo.GitHubLatestReleaseApiUrl);
                    Match match = Regex.Match(
                        json,
                        "\\\"tag_name\\\"\\s*:\\s*\\\"(?<tag>[^\\\"]+)\\\"",
                        RegexOptions.IgnoreCase);
                    if (!match.Success)
                    {
                        result.Error = "GitHub 返回了 Release 信息，但未找到可识别的版本标签。";
                        return result;
                    }

                    result.LatestTag = match.Groups["tag"].Value.Trim();
                    result.ReleaseUrl = AppInfo.GitHubReleasesUrl + "/tag/" +
                        Uri.EscapeDataString(result.LatestTag);
                    result.Success = true;
                    result.CheckedAtLocal = DateTime.Now;
                    WriteUpdateCheckCache(result);
                    return result;
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse response = ex.Response as HttpWebResponse;
                if (response != null)
                {
                    try
                    {
                        if (response.StatusCode == HttpStatusCode.NotFound)
                        {
                            result.NoPublishedRelease = true;
                            result.Error = "GitHub 仓库目前还没有已发布的 Release。";
                            result.CheckedAtLocal = DateTime.Now;
                            WriteUpdateCheckCache(result);
                            return result;
                        }

                        if (response.StatusCode == HttpStatusCode.Forbidden)
                        {
                            string remaining = response.Headers["X-RateLimit-Remaining"] ?? "";
                            string reset = response.Headers["X-RateLimit-Reset"] ?? "";
                            bool rateLimited =
                                string.Equals(remaining, "0", StringComparison.OrdinalIgnoreCase);

                            if (rateLimited)
                            {
                                UpdateCheckResult fallback = QueryLatestReleaseFallback();
                                if (fallback.Success || fallback.NoPublishedRelease)
                                {
                                    fallback.UsedFallback = true;
                                    fallback.CheckedAtLocal = DateTime.Now;
                                    WriteUpdateCheckCache(fallback);
                                    return fallback;
                                }

                                result.RateLimitResetLocal = FormatRateLimitResetLocal(reset);
                                result.Error = "GitHub API 暂时达到匿名访问频率限制（HTTP 403）。";
                                if (!string.IsNullOrWhiteSpace(result.RateLimitResetLocal))
                                    result.Error += "\r\n预计可在 " + result.RateLimitResetLocal + " 后重新检查。";
                                result.Error += "\r\n备用 Release 查询也暂时不可用，请稍后重试；" +
                                    "这不会影响程序其他功能。";
                                return result;
                            }

                            result.Error = "GitHub 拒绝了更新检查（HTTP 403）。" +
                                "\r\n请检查代理、防火墙或网络访问策略后重试。";
                            return result;
                        }

                        result.Error = "GitHub Releases 返回 HTTP " +
                            ((int)response.StatusCode) + " " + response.StatusDescription + "。";
                        return result;
                    }
                    finally
                    {
                        try { response.Close(); } catch { }
                    }
                }

                result.Error = "无法连接 GitHub Releases：" + ex.Message;
                return result;
            }
            catch (Exception ex)
            {
                result.Error = "检查更新失败：" + ex.Message;
                return result;
            }
        }

        private static void EnableTls12ForGitHub()
        {
            try
            {
                // SecurityProtocolType.Tls12 is not named by .NET Framework 4.0,
                // but its numeric value is 3072.
                ServicePointManager.SecurityProtocol =
                    ServicePointManager.SecurityProtocol | (SecurityProtocolType)3072;
            }
            catch { }
        }

        private static UpdateCheckResult QueryLatestReleaseFallback()
        {
            var result = new UpdateCheckResult();
            try
            {
                EnableTls12ForGitHub();
                string atomUrl = AppInfo.GitHubReleasesUrl + ".atom";
                string xml;
                using (var client = new TimeoutWebClient(
                    "application/atom+xml, application/xml;q=0.9, text/xml;q=0.8, */*;q=0.5"))
                {
                    xml = client.DownloadString(atomUrl);
                }

                var document = new XmlDocument();
                document.XmlResolver = null;
                document.LoadXml(xml);

                var ns = new XmlNamespaceManager(document.NameTable);
                ns.AddNamespace("a", "http://www.w3.org/2005/Atom");
                XmlNode entry = document.SelectSingleNode("/a:feed/a:entry[1]", ns);
                if (entry == null)
                {
                    result.NoPublishedRelease = true;
                    result.Error = "GitHub 仓库目前还没有已发布的 Release。";
                    return result;
                }

                XmlNode linkNode = entry.SelectSingleNode("a:link[@href]", ns);
                string href = linkNode == null || linkNode.Attributes == null ||
                    linkNode.Attributes["href"] == null
                    ? "" : linkNode.Attributes["href"].Value;

                string tag = ExtractReleaseTagFromUrl(href);
                if (string.IsNullOrWhiteSpace(tag))
                {
                    XmlNode titleNode = entry.SelectSingleNode("a:title", ns);
                    string title = titleNode == null ? "" : titleNode.InnerText;
                    Match versionMatch = Regex.Match(
                        title ?? "",
                        "(?i)(?<tag>v?\\d+\\.\\d+\\.\\d+)");
                    if (versionMatch.Success)
                        tag = versionMatch.Groups["tag"].Value;
                }

                if (string.IsNullOrWhiteSpace(tag))
                {
                    result.Error = "备用 Release 查询成功，但未找到可识别的版本标签。";
                    return result;
                }

                result.LatestTag = tag.Trim();
                result.ReleaseUrl = string.IsNullOrWhiteSpace(href)
                    ? AppInfo.GitHubReleasesUrl + "/tag/" + Uri.EscapeDataString(result.LatestTag)
                    : href.Trim();
                result.Success = true;
                return result;
            }
            catch (WebException ex)
            {
                HttpWebResponse response = ex.Response as HttpWebResponse;
                if (response != null)
                {
                    try
                    {
                        if (response.StatusCode == HttpStatusCode.NotFound)
                        {
                            result.NoPublishedRelease = true;
                            result.Error = "GitHub 仓库目前还没有已发布的 Release。";
                            return result;
                        }
                    }
                    finally
                    {
                        try { response.Close(); } catch { }
                    }
                }

                result.Error = "备用 Release 查询失败：" + ex.Message;
                return result;
            }
            catch (Exception ex)
            {
                result.Error = "备用 Release 查询失败：" + ex.Message;
                return result;
            }
        }

        private static string ExtractReleaseTagFromUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            const string marker = "/releases/tag/";
            int index = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return "";
            string tag = url.Substring(index + marker.Length);
            int query = tag.IndexOfAny(new char[] { '?', '#', '/' });
            if (query >= 0) tag = tag.Substring(0, query);
            try { tag = Uri.UnescapeDataString(tag); } catch { }
            return tag.Trim();
        }

        private static string FormatRateLimitResetLocal(string unixSeconds)
        {
            long seconds;
            if (!long.TryParse(unixSeconds, out seconds) || seconds <= 0)
                return "";

            try
            {
                DateTime utc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    .AddSeconds(seconds);
                return utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            }
            catch
            {
                return "";
            }
        }

        private static string GetUpdateCheckCachePath()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root))
                root = Path.GetTempPath();
            return Path.Combine(root, "UnifiedHydroWorkflow", "update_check_cache.txt");
        }

        private static bool TryReadUpdateCheckCache(out UpdateCheckResult result)
        {
            result = null;
            try
            {
                string path = GetUpdateCheckCachePath();
                if (!File.Exists(path)) return false;

                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                if (lines.Length < 4) return false;

                long ticks;
                if (!long.TryParse(lines[0], out ticks)) return false;
                DateTime checkedUtc = new DateTime(ticks, DateTimeKind.Utc);
                TimeSpan age = DateTime.UtcNow - checkedUtc;
                if (age < TimeSpan.Zero || age.TotalMinutes > UpdateCheckCacheMinutes)
                    return false;

                var cached = new UpdateCheckResult();
                cached.FromCache = true;
                cached.CheckedAtLocal = checkedUtc.ToLocalTime();
                cached.NoPublishedRelease = string.Equals(lines[1], "NO_RELEASE",
                    StringComparison.OrdinalIgnoreCase);
                cached.LatestTag = lines[2] ?? "";
                cached.ReleaseUrl = lines[3] ?? "";

                if (cached.NoPublishedRelease)
                {
                    cached.Error = "GitHub 仓库目前还没有已发布的 Release。";
                    result = cached;
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(cached.LatestTag))
                {
                    cached.Success = true;
                    result = cached;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static void WriteUpdateCheckCache(UpdateCheckResult result)
        {
            if (result == null || (!result.Success && !result.NoPublishedRelease))
                return;

            try
            {
                string path = GetUpdateCheckCachePath();
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                string[] lines = new string[]
                {
                    DateTime.UtcNow.Ticks.ToString(),
                    result.NoPublishedRelease ? "NO_RELEASE" : "RELEASE",
                    result.LatestTag ?? "",
                    result.ReleaseUrl ?? ""
                };
                File.WriteAllLines(path, lines, Encoding.UTF8);
            }
            catch { }
        }

        private void ShowUpdateCheckResult(UpdateCheckResult result)
        {
            _updateCheckInProgress = false;
            UseWaitCursor = false;
            if (menuCheckUpdates != null) menuCheckUpdates.Enabled = true;

            if (result.NoPublishedRelease)
            {
                AppendLog("UPDATE_CHECK no_release" +
                    (result.FromCache ? " source=cache" :
                     result.UsedFallback ? " source=fallback" : " source=api"));
                string sourceNote = result.FromCache && result.CheckedAtLocal != DateTime.MinValue
                    ? "\r\n\r\n使用缓存结果（" +
                        result.CheckedAtLocal.ToString("yyyy-MM-dd HH:mm") + " 检查）。"
                    : result.UsedFallback ? "\r\n\r\n已使用备用 Release 查询。" : "";
                MessageBox.Show(this,
                    result.Error + "\r\n\r\n当前版本：" + AppInfo.DisplayVersion + sourceNote,
                    "检查更新",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (!result.Success)
            {
                AppendLog("UPDATE_CHECK failed " + result.Error);
                MessageBox.Show(this,
                    result.Error + "\r\n\r\n你也可以在“关于”中打开 GitHub 仓库手动查看。",
                    "检查更新",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            Version currentVersion;
            Version latestVersion;
            if (!TryParseThreePartVersion(AppInfo.Version, out currentVersion) ||
                !TryParseThreePartVersion(result.LatestTag, out latestVersion))
            {
                AppendLog("UPDATE_CHECK invalid_tag latest=" + result.LatestTag);
                MessageBox.Show(this,
                    "无法识别 GitHub Release 版本标签：" + result.LatestTag +
                    "\r\n\r\n版本标签应使用三段格式，例如 v5.1.0。",
                    "检查更新",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            AppendLog("UPDATE_CHECK current=" + AppInfo.Version + " latest=" + result.LatestTag +
                (result.FromCache ? " source=cache" :
                 result.UsedFallback ? " source=fallback" : " source=api"));
            string resultSourceNote = "";
            if (result.FromCache && result.CheckedAtLocal != DateTime.MinValue)
            {
                resultSourceNote = "\r\n\r\n使用 30 分钟内缓存结果（" +
                    result.CheckedAtLocal.ToString("yyyy-MM-dd HH:mm") + " 检查）。";
            }
            else if (result.UsedFallback)
            {
                resultSourceNote = "\r\n\r\nGitHub API 受限，已使用备用 Release 查询。";
            }

            int compare = latestVersion.CompareTo(currentVersion);
            if (compare > 0)
            {
                DialogResult answer = MessageBox.Show(this,
                    "发现新版本。\r\n\r\n" +
                    "当前版本：" + AppInfo.DisplayVersion + "\r\n" +
                    "最新版本：" + result.LatestTag + "\r\n\r\n" +
                    "是否打开 GitHub Release 页面查看更新？\r\n\r\n" +
                    "程序只检查版本，不会自动下载或覆盖现有文件。" + resultSourceNote,
                    "发现新版本",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);
                if (answer == DialogResult.Yes)
                    OpenUrl(result.ReleaseUrl, "无法打开 GitHub Release");
                return;
            }

            MessageBox.Show(this,
                "当前版本已是最新版本。\r\n\r\n" +
                "当前版本：" + AppInfo.DisplayVersion + "\r\n" +
                "GitHub 最新 Release：" + result.LatestTag + resultSourceNote,
                "检查更新",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private static bool TryParseThreePartVersion(string value, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(value)) return false;

            string normalized = value.Trim();
            if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(1);

            string[] parts = normalized.Split('.');
            if (parts.Length < 2 || parts.Length > 3) return false;

            int major;
            int minor;
            int patch = 0;
            if (!int.TryParse(parts[0], out major) || major < 0) return false;
            if (!int.TryParse(parts[1], out minor) || minor < 0) return false;
            if (parts.Length == 3 && (!int.TryParse(parts[2], out patch) || patch < 0))
                return false;

            version = new Version(major, minor, patch);
            return true;
        }

        private void ShowAboutDialog(object sender, EventArgs e)
        {
            using (var dialog = new Form())
            {
                dialog.Text = "关于 " + AppInfo.ProductName;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.ClientSize = new Size(580, 340);
                dialog.Font = Font;

                var root = new TableLayoutPanel();
                root.Dock = DockStyle.Fill;
                root.ColumnCount = 1;
                root.RowCount = 7;
                root.Padding = new Padding(28, 24, 28, 20);
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
                dialog.Controls.Add(root);

                var title = new Label();
                title.AutoSize = true;
                title.Font = new Font(Font.FontFamily, 16F, FontStyle.Bold);
                title.Text = AppInfo.ProductName;
                root.Controls.Add(title, 0, 0);

                var subtitle = new Label();
                subtitle.AutoSize = true;
                subtitle.ForeColor = Color.DimGray;
                subtitle.Margin = new Padding(1, 8, 1, 0);
                subtitle.Text = "水文模型数据预处理、输入检查与批量计算辅助工具";
                root.Controls.Add(subtitle, 0, 1);

                var version = new Label();
                version.AutoSize = true;
                version.Text = "版本：" + AppInfo.DisplayVersion +
                    "    Workflow Core：V" + AppInfo.WorkflowCoreVersion;
                root.Controls.Add(version, 0, 3);

                var support = new Label();
                support.AutoSize = true;
                support.Margin = new Padding(0, 10, 0, 0);
                support.Text = "GIS 支持：ArcGIS Pro / ArcMap / QGIS 3.x";
                root.Controls.Add(support, 0, 4);

                var linkPanel = new FlowLayoutPanel();
                linkPanel.AutoSize = true;
                linkPanel.FlowDirection = FlowDirection.TopDown;
                linkPanel.WrapContents = false;
                linkPanel.Margin = new Padding(0, 16, 0, 0);

                var githubLabel = new Label();
                githubLabel.AutoSize = true;
                githubLabel.Text = "GitHub 仓库";
                linkPanel.Controls.Add(githubLabel);

                var githubLink = new LinkLabel();
                githubLink.AutoSize = true;
                githubLink.Text = AppInfo.GitHubUrl;
                githubLink.LinkClicked += delegate
                {
                    OpenUrl(AppInfo.GitHubUrl, "无法打开 GitHub 仓库");
                };
                linkPanel.Controls.Add(githubLink);

                var note = new Label();
                note.AutoSize = true;
                note.MaximumSize = new Size(500, 0);
                note.ForeColor = Color.DimGray;
                note.Margin = new Padding(0, 12, 0, 0);
                note.Text = "本程序为原水文模型的辅助工作流，不包含原水文模型程序、模型 DLL、数据库或实际业务数据。";
                linkPanel.Controls.Add(note);
                root.Controls.Add(linkPanel, 0, 5);

                var closePanel = new FlowLayoutPanel();
                closePanel.Dock = DockStyle.Fill;
                closePanel.FlowDirection = FlowDirection.RightToLeft;
                closePanel.WrapContents = false;
                root.Controls.Add(closePanel, 0, 6);

                var close = new Button();
                close.Text = "关闭";
                close.Width = 100;
                close.Height = 30;
                close.Click += delegate { dialog.Close(); };
                closePanel.Controls.Add(close);

                dialog.ShowDialog(this);
            }
        }

        private void OpenLocalDocument(string path)
        {
            if (!File.Exists(path))
            {
                MessageBox.Show(
                    this,
                    "未找到说明文件：\r\n" + path,
                    "文件不存在",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            try
            {
                var start = new ProcessStartInfo();
                start.FileName = path;
                start.UseShellExecute = true;
                Process.Start(start);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "无法打开说明文件",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void OpenUrl(string url, string title)
        {
            try
            {
                var start = new ProcessStartInfo();
                start.FileName = url;
                start.UseShellExecute = true;
                Process.Start(start);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message + "\r\n\r\n" + url,
                    title,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
    }
}
