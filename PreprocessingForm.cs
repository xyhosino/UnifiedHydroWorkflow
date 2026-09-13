using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace UnifiedHydroLauncher
{
    internal sealed class PreprocessingForm : Form
    {
        private ComboBox cmbEngine;
        private TextBox txtPython;
        private ComboBox txtWata;
        private ComboBox txtRiver;
        private ComboBox txtNode;
        private ComboBox txtSoil;
        private ComboBox txtLanduse;
        private TextBox txtOutput;
        private RichTextBox txtLog;
        private Button btnRun;
        private Button btnCancel;
        private Button btnClose;
        private Process process;
        private bool running;
        private bool cancelled;
        private readonly List<Button> browseButtons = new List<Button>();
        private PreprocessEngine autoDetectedEngine = PreprocessEngine.ArcGisPro;
        private bool arcMapBackgroundDetectionInProgress;
        private string backgroundDetectedArcMapPython = "";
        private static bool arcGisDesktopPythonCacheInitialized;
        private static string arcGisDesktopPythonCache = "";

        internal bool Succeeded { get; private set; }
        internal string OutputRoot
        {
            get { return txtOutput == null ? "" : txtOutput.Text.Trim(); }
        }

        internal PreprocessingForm()
        {
            Text = AppInfo.DisplayVersion + " 数据预处理（多 GIS 引擎）";
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 9F);
            MinimumSize = new Size(930, 650);
            Size = new Size(1050, 720);
            BuildUi();
            cmbEngine.SelectedIndex = 0;
            UpdateRuntimeForEngine();
            Shown += PreprocessingForm_Shown;
            FormClosing += PreprocessingForm_FormClosing;
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(12);
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            Controls.Add(root);

            var intro = new Label();
            intro.AutoSize = true;
            intro.MaximumSize = new Size(990, 0);
            intro.Text =
                "支持 ArcGIS Pro、ArcGIS Desktop / ArcMap 与 QGIS 3.x；自动检测优先使用 ArcGIS Pro。" +
                "按流域shp中的 WSCU_Name 字段拆分计算单元，WSCD 用于河道归属匹配；" +
                "三种引擎最终均使用内置标准模板保证原水文模型兼容性。";
            intro.ForeColor = Color.DimGray;
            root.Controls.Add(intro, 0, 0);

            var fields = new TableLayoutPanel();
            fields.Dock = DockStyle.Top;
            fields.AutoSize = true;
            fields.ColumnCount = 3;
            fields.RowCount = 8;
            fields.Padding = new Padding(0, 8, 0, 6);
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135F));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95F));
            for (int index = 0; index < 8; index++)
                fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            root.Controls.Add(fields, 0, 1);

            cmbEngine = AddEngineRow(fields, 0);
            txtPython = AddFileRow(fields, 1, "Python / 启动器", "GIS Python / QGIS launcher (*.exe;*.bat)|*.exe;*.bat|Executable (*.exe)|*.exe|Batch (*.bat)|*.bat|All files (*.*)|*.*");
            txtWata = AddShapeRow(fields, 2, "流域shp");
            txtRiver = AddShapeRow(fields, 3, "河道shp");
            txtNode = AddShapeRow(fields, 4, "节点shp");
            txtLanduse = AddShapeRow(fields, 5, "土地利用shp");
            txtSoil = AddShapeRow(fields, 6, "土壤地质shp");
            txtOutput = AddFolderRow(fields, 7, "输出根目录");

            var logGroup = new GroupBox();
            logGroup.Text = "预处理日志";
            logGroup.Dock = DockStyle.Fill;
            logGroup.Padding = new Padding(8);
            root.Controls.Add(logGroup, 0, 2);

            txtLog = new RichTextBox();
            txtLog.Dock = DockStyle.Fill;
            txtLog.ReadOnly = true;
            txtLog.BackColor = Color.White;
            txtLog.Font = new Font("Consolas", 9F);
            logGroup.Controls.Add(txtLog);

            var actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.FlowDirection = FlowDirection.RightToLeft;
            actions.WrapContents = false;
            actions.Padding = new Padding(0, 8, 0, 0);
            root.Controls.Add(actions, 0, 3);

            btnClose = NewButton("关闭", 105);
            btnClose.Click += BtnClose_Click;
            actions.Controls.Add(btnClose);

            btnCancel = NewButton("停止预处理", 115);
            btnCancel.Enabled = false;
            btnCancel.Click += BtnCancel_Click;
            actions.Controls.Add(btnCancel);

            btnRun = NewButton("开始预处理", 125);
            btnRun.Click += BtnRun_Click;
            actions.Controls.Add(btnRun);
        }


        private ComboBox AddEngineRow(TableLayoutPanel panel, int row)
        {
            var label = NewLabel("处理引擎");
            panel.Controls.Add(label, 0, row);
            var combo = new ComboBox();
            combo.Dock = DockStyle.Fill;
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.Items.Add("自动检测（推荐）");
            combo.Items.Add("ArcGIS Pro (arcpy)");
            combo.Items.Add("ArcGIS Desktop / ArcMap (arcpy)");
            combo.Items.Add("QGIS 3.x");
            combo.SelectedIndexChanged += Engine_SelectedIndexChanged;
            panel.Controls.Add(combo, 1, row);
            panel.SetColumnSpan(combo, 2);
            return combo;
        }

        private void PreprocessingForm_Shown(object sender, EventArgs e)
        {
            BeginArcMapBackgroundDetection();
        }

        private static string GetCachedArcGisDesktopPython()
        {
            try
            {
                if (arcGisDesktopPythonCacheInitialized &&
                    !string.IsNullOrWhiteSpace(arcGisDesktopPythonCache) &&
                    File.Exists(arcGisDesktopPythonCache))
                {
                    return arcGisDesktopPythonCache;
                }
            }
            catch { }
            return "";
        }

        private void BeginArcMapBackgroundDetection()
        {
            if (arcMapBackgroundDetectionInProgress || IsDisposed)
                return;

            string cached = GetCachedArcGisDesktopPython();
            if (!string.IsNullOrWhiteSpace(cached))
            {
                backgroundDetectedArcMapPython = cached;
                if (SelectedEngine == PreprocessEngine.ArcGisDesktop && txtPython != null)
                    txtPython.Text = cached;
                return;
            }

            arcMapBackgroundDetectionInProgress = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string detected = FindArcGisDesktopPython();
                try
                {
                    if (IsDisposed || !IsHandleCreated)
                        return;

                    BeginInvoke(new Action(delegate
                    {
                        arcMapBackgroundDetectionInProgress = false;
                        backgroundDetectedArcMapPython = detected ?? "";

                        if (SelectedEngine == PreprocessEngine.ArcGisDesktop)
                        {
                            txtPython.Text = backgroundDetectedArcMapPython;
                        }
                        else if (SelectedEngine == PreprocessEngine.Auto &&
                                 string.IsNullOrWhiteSpace(FindArcGisPython()) &&
                                 !string.IsNullOrWhiteSpace(backgroundDetectedArcMapPython))
                        {
                            autoDetectedEngine = PreprocessEngine.ArcGisDesktop;
                            txtPython.Text = backgroundDetectedArcMapPython;
                        }
                    }));
                }
                catch
                {
                    arcMapBackgroundDetectionInProgress = false;
                }
            });
        }

        private void Engine_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (running || txtPython == null) return;
            UpdateRuntimeForEngine();
        }

        private void UpdateRuntimeForEngine()
        {
            if (cmbEngine == null || txtPython == null) return;
            PreprocessEngine selected = SelectedEngine;
            if (selected == PreprocessEngine.ArcGisPro)
            {
                txtPython.Text = FindArcGisPython();
                return;
            }
            if (selected == PreprocessEngine.ArcGisDesktop)
            {
                string cachedArcMap = GetCachedArcGisDesktopPython();
                if (string.IsNullOrWhiteSpace(cachedArcMap))
                    cachedArcMap = backgroundDetectedArcMapPython;

                // Clear the previous engine path immediately. Never leave an ArcGIS Pro
                // path visible while ArcMap is being detected in the background.
                txtPython.Text = cachedArcMap ?? "";
                if (string.IsNullOrWhiteSpace(txtPython.Text))
                    BeginArcMapBackgroundDetection();
                return;
            }
            if (selected == PreprocessEngine.Qgis)
            {
                txtPython.Text = FindQgisPythonLauncher();
                return;
            }

            string arcgisPro = FindArcGisPython();
            if (!string.IsNullOrWhiteSpace(arcgisPro))
            {
                autoDetectedEngine = PreprocessEngine.ArcGisPro;
                txtPython.Text = arcgisPro;
                BeginArcMapBackgroundDetection();
                return;
            }

            string arcgisDesktop = GetCachedArcGisDesktopPython();
            if (string.IsNullOrWhiteSpace(arcgisDesktop))
                arcgisDesktop = backgroundDetectedArcMapPython;
            if (!string.IsNullOrWhiteSpace(arcgisDesktop))
            {
                autoDetectedEngine = PreprocessEngine.ArcGisDesktop;
                txtPython.Text = arcgisDesktop;
                return;
            }

            string qgis = FindQgisPythonLauncher();
            if (!string.IsNullOrWhiteSpace(qgis))
            {
                autoDetectedEngine = PreprocessEngine.Qgis;
                txtPython.Text = qgis;
                BeginArcMapBackgroundDetection();
                return;
            }

            txtPython.Text = "";
            BeginArcMapBackgroundDetection();
        }

        private PreprocessEngine SelectedEngine
        {
            get
            {
                if (cmbEngine == null) return PreprocessEngine.Auto;
                if (cmbEngine.SelectedIndex == 1) return PreprocessEngine.ArcGisPro;
                if (cmbEngine.SelectedIndex == 2) return PreprocessEngine.ArcGisDesktop;
                if (cmbEngine.SelectedIndex == 3) return PreprocessEngine.Qgis;
                return PreprocessEngine.Auto;
            }
        }

        private PreprocessEngine ResolveEngine()
        {
            PreprocessEngine selected = SelectedEngine;
            if (selected != PreprocessEngine.Auto) return selected;
            string runtime = txtPython == null ? "" : txtPython.Text.Trim();
            if (LooksLikeQgisRuntime(runtime)) return PreprocessEngine.Qgis;
            if (LooksLikeArcGisDesktopRuntime(runtime)) return PreprocessEngine.ArcGisDesktop;
            if (LooksLikeArcGisProRuntime(runtime)) return PreprocessEngine.ArcGisPro;
            return autoDetectedEngine;
        }

        private static bool LooksLikeQgisRuntime(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string lower = path.ToLowerInvariant();
            return lower.Contains("qgis") || lower.EndsWith("python-qgis.bat") || lower.EndsWith("python-qgis-ltr.bat");
        }

        private static bool LooksLikeArcGisDesktopRuntime(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string lower = path.ToLowerInvariant();
            return (lower.Contains("python27") && lower.Contains("arcgis")) ||
                   lower.Contains("desktop10") ||
                   lower.Contains("arcgisx6410") ||
                   (lower.Contains("arcgis10") && !lower.Contains("arcgispro"));
        }

        private static bool LooksLikeArcGisProRuntime(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string lower = path.ToLowerInvariant();
            return lower.Contains("arcgispro-py3") ||
                   (lower.Contains("arcgis") && lower.Contains("\\pro\\"));
        }

        private TextBox AddFileRow(TableLayoutPanel panel, int row, string caption, string filter)
        {
            var label = NewLabel(caption);
            panel.Controls.Add(label, 0, row);
            var box = NewTextBox();
            panel.Controls.Add(box, 1, row);
            var button = NewButton("浏览...", 85);
            button.Tag = new BrowseFileTarget(box, filter);
            button.Click += BrowseFile_Click;
            browseButtons.Add(button);
            panel.Controls.Add(button, 2, row);
            return box;
        }

        private ComboBox AddShapeRow(TableLayoutPanel panel, int row, string caption)
        {
            var label = NewLabel(caption);
            panel.Controls.Add(label, 0, row);
            var combo = NewPathCombo();
            combo.DropDown += ShapeCombo_DropDown;
            panel.Controls.Add(combo, 1, row);
            var button = NewButton("浏览...", 85);
            button.Tag = new BrowseFileTarget(combo, "Shapefile (*.shp)|*.shp|All files (*.*)|*.*");
            button.Click += BrowseFile_Click;
            browseButtons.Add(button);
            panel.Controls.Add(button, 2, row);
            return combo;
        }

        private TextBox AddFolderRow(TableLayoutPanel panel, int row, string caption)
        {
            var label = NewLabel(caption);
            panel.Controls.Add(label, 0, row);
            var box = NewTextBox();
            panel.Controls.Add(box, 1, row);
            var button = NewButton("浏览...", 85);
            button.Tag = box;
            button.Click += BrowseFolder_Click;
            browseButtons.Add(button);
            panel.Controls.Add(button, 2, row);
            return box;
        }

        private static Label NewLabel(string text)
        {
            var label = new Label();
            label.Text = text;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        private static TextBox NewTextBox()
        {
            var box = new TextBox();
            box.Dock = DockStyle.Fill;
            return box;
        }

        private static ComboBox NewPathCombo()
        {
            var combo = new ComboBox();
            combo.Dock = DockStyle.Fill;
            combo.DropDownStyle = ComboBoxStyle.DropDown;
            combo.IntegralHeight = false;
            combo.DropDownHeight = 180;
            return combo;
        }

        private static Button NewButton(string text, int width)
        {
            var button = new Button();
            button.Text = text;
            button.Width = width;
            button.Height = 29;
            button.Margin = new Padding(5, 2, 0, 2);
            return button;
        }

        private void BrowseFile_Click(object sender, EventArgs e)
        {
            Button button = sender as Button;
            BrowseFileTarget target = button == null ? null : button.Tag as BrowseFileTarget;
            if (target == null) return;
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = target.Filter;
                dialog.CheckFileExists = true;
                string current = target.Control.Text.Trim();
                if (File.Exists(current))
                {
                    dialog.FileName = current;
                }
                else
                {
                    string currentDirectory = SafeGetDirectoryName(current);
                    if (Directory.Exists(currentDirectory))
                        dialog.InitialDirectory = currentDirectory;
                }
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    target.Control.Text = dialog.FileName;
                    if (target.Control is ComboBox)
                        PopulateShapeChoices(SafeGetDirectoryName(dialog.FileName));
                }
            }
        }

        private void ShapeCombo_DropDown(object sender, EventArgs e)
        {
            ComboBox combo = sender as ComboBox;
            if (combo == null) return;
            string current = combo.Text.Trim();
            string directory = SafeGetDirectoryName(current);
            if (Directory.Exists(directory))
                PopulateShapeChoices(directory);
        }

        private static string SafeGetDirectoryName(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "";
            try
            {
                return Path.GetDirectoryName(path) ?? "";
            }
            catch (ArgumentException)
            {
                return "";
            }
            catch (NotSupportedException)
            {
                return "";
            }
            catch (PathTooLongException)
            {
                return "";
            }
        }

        private void PopulateShapeChoices(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return;
            string[] files;
            try
            {
                files = Directory.GetFiles(directory, "*.shp");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return;
            }

            foreach (ComboBox combo in new[] { txtWata, txtRiver, txtNode, txtLanduse, txtSoil })
            {
                if (combo == null) continue;
                string selected = combo.Text;
                combo.BeginUpdate();
                combo.Items.Clear();
                foreach (string file in files)
                    combo.Items.Add(file);
                combo.Text = selected;
                combo.EndUpdate();
            }
        }

        private void BrowseFolder_Click(object sender, EventArgs e)
        {
            Button button = sender as Button;
            TextBox target = button == null ? null : button.Tag as TextBox;
            if (target == null) return;
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.ShowNewFolderButton = true;
                string current = target.Text.Trim();
                if (Directory.Exists(current))
                    dialog.SelectedPath = current;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    target.Text = dialog.SelectedPath;
            }
        }

        private void BtnRun_Click(object sender, EventArgs e)
        {
            if (running) return;
            string error;
            if (!ValidateInputs(out error))
            {
                MessageBox.Show(this, error, "预处理参数检查", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            PreprocessEngine engine = ResolveEngine();
            string scriptName;
            if (engine == PreprocessEngine.Qgis)
                scriptName = "UnifiedHydroPreprocess_QGIS.py";
            else if (engine == PreprocessEngine.ArcGisDesktop)
                scriptName = "UnifiedHydroPreprocess_ArcMap.py";
            else
                scriptName = "UnifiedHydroPreprocess_ArcGISPro.py";
            string scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, scriptName);
            if (!File.Exists(scriptPath))
            {
                MessageBox.Show(
                    this,
                    "未找到预处理脚本：\r\n" + scriptPath + "\r\n请保留完整的多 GIS 预处理脚本。",
                    "缺少预处理脚本",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            string templateDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PreprocessTemplates");
            string[] requiredTemplates = new string[]
            {
                "wata.shp", "wata.shx", "wata.dbf", "wata.prj", "wata.cpg",
                "rivl.shp", "rivl.shx", "rivl.dbf", "rivl.prj", "rivl.cpg",
                "node.shp", "node.shx", "node.dbf", "node.prj", "node.cpg"
            };
            foreach (string templateFile in requiredTemplates)
            {
                string fullTemplatePath = Path.Combine(templateDirectory, templateFile);
                if (!File.Exists(fullTemplatePath))
                {
                    MessageBox.Show(
                        this,
                        "缺少内置预处理模板：\r\n" + fullTemplatePath +
                        "\r\n请保留 Launcher 同目录下完整的 PreprocessTemplates 文件夹。",
                        "缺少内置模板",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }
            }

            string runtimePath = Path.GetFullPath(txtPython.Text.Trim());
            string scriptArguments = BuildArguments(scriptPath);
            var startInfo = new ProcessStartInfo();
            if (engine == PreprocessEngine.Qgis &&
                string.Equals(Path.GetExtension(runtimePath), ".bat", StringComparison.OrdinalIgnoreCase))
            {
                string commandProcessor = Environment.GetEnvironmentVariable("ComSpec");
                startInfo.FileName = string.IsNullOrWhiteSpace(commandProcessor) ? "cmd.exe" : commandProcessor;
                startInfo.Arguments = "/d /s /c \"\"" + runtimePath + "\" " + scriptArguments + "\"";
            }
            else
            {
                startInfo.FileName = runtimePath;
                startInfo.Arguments = scriptArguments;
            }
            startInfo.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.CreateNoWindow = true;

            startInfo.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";

            // 强制 GIS Python 日志使用 UTF-8 输出，避免中文乱码
            startInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

            // Launcher 同样按 UTF-8 读取 stdout / stderr
            TrySetProcessOutputEncodingUtf8(startInfo);

            process = new Process();
            process.StartInfo = startInfo;
            process.EnableRaisingEvents = true;
            process.OutputDataReceived += Process_OutputDataReceived;
            process.ErrorDataReceived += Process_ErrorDataReceived;
            process.Exited += Process_Exited;

            Succeeded = false;
            cancelled = false;
            txtLog.Clear();
            AppendLog("PREPROCESS_GUI_ENGINE " + EngineName(engine));
            AppendLog("PREPROCESS_GUI_RUNTIME " + runtimePath);
            AppendLog("PREPROCESS_GUI_COMMAND " + Quote(startInfo.FileName) + " " + startInfo.Arguments);
            AppendLog("");
            try
            {
                SetRunning(true);
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch (Exception exception)
            {
                SetRunning(false);
                AppendLog("PREPROCESS_GUI_START_FAILED " + exception.Message);
                MessageBox.Show(this, exception.Message, "无法启动 GIS 预处理环境", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void TrySetProcessOutputEncodingUtf8(ProcessStartInfo startInfo)
        {
            try
            {
                Encoding utf8 = new UTF8Encoding(false);

                // 项目目标仍是 .NET Framework 4.0，
                // 因此通过反射调用较新运行时提供的编码属性，
                // 避免源码直接引用导致旧 Targeting Pack 编译失败。
                var outputProperty = typeof(ProcessStartInfo).GetProperty(
                    "StandardOutputEncoding"
                );

                if (outputProperty != null && outputProperty.CanWrite)
                {
                    outputProperty.SetValue(
                        startInfo,
                        utf8,
                        null
                    );
                }

                var errorProperty = typeof(ProcessStartInfo).GetProperty(
                    "StandardErrorEncoding"
                );

                if (errorProperty != null && errorProperty.CanWrite)
                {
                    errorProperty.SetValue(
                        startInfo,
                        utf8,
                        null
                    );
                }
            }
            catch
            {
                // 编码设置失败不影响预处理本身启动
            }
        }

        private static string EngineName(PreprocessEngine engine)
        {
            if (engine == PreprocessEngine.Qgis) return "QGIS";
            if (engine == PreprocessEngine.ArcGisDesktop) return "ArcGISDesktop";
            if (engine == PreprocessEngine.ArcGisPro) return "ArcGISPro";
            return "Auto";
        }

        private string BuildArguments(string scriptPath)
        {
            var builder = new StringBuilder();
            builder.Append(Quote(scriptPath));
            AppendArgument(builder, "--wata", txtWata.Text);
            AppendArgument(builder, "--river", txtRiver.Text);
            AppendArgument(builder, "--node", txtNode.Text);
            AppendArgument(builder, "--landuse", txtLanduse.Text);
            AppendArgument(builder, "--soil", txtSoil.Text);
            AppendArgument(builder, "--output-dir", txtOutput.Text);
            return builder.ToString();
        }

        private static void AppendArgument(StringBuilder builder, string name, string value)
        {
            builder.Append(" ");
            builder.Append(name);
            builder.Append(" ");
            builder.Append(Quote(Path.GetFullPath(value.Trim())));
        }

        private bool ValidateInputs(out string error)
        {
            error = "";
            if (!File.Exists(txtPython.Text.Trim()))
            {
                error = "请选择有效的 GIS Python / QGIS 启动器。ArcGIS Pro 请选择其 Python 3；ArcGIS Desktop / ArcMap 请选择 Python 2.7；QGIS 推荐选择 python-qgis.bat。";
                return false;
            }
            if (!ValidateShape(txtWata.Text, "流域shp", out error) ||
                !ValidateShape(txtRiver.Text, "河道shp", out error) ||
                !ValidateShape(txtNode.Text, "节点shp", out error) ||
                !ValidateShape(txtLanduse.Text, "土地利用shp", out error) ||
                !ValidateShape(txtSoil.Text, "土壤地质shp", out error))
                return false;
            string output = txtOutput.Text.Trim();
            if (output.Length == 0)
            {
                error = "请选择输出根目录。";
                return false;
            }
            if (output.IndexOf(' ') >= 0)
            {
                error = "输出根目录不能包含普通空格；核心 UnifiedHydroWorkflow 不接受带空格的输入根目录。";
                return false;
            }
            try
            {
                if (!Directory.Exists(output))
                    Directory.CreateDirectory(output);
            }
            catch (Exception exception)
            {
                error = "无法创建输出目录：" + exception.Message;
                return false;
            }
            return true;
        }

        private static bool ValidateShape(string value, string caption, out string error)
        {
            error = "";
            string path = value.Trim();
            if (!File.Exists(path) || !string.Equals(Path.GetExtension(path), ".shp", StringComparison.OrdinalIgnoreCase))
            {
                error = caption + " 必须选择有效的 .shp 文件。";
                return false;
            }
            foreach (string extension in new string[] { ".shx", ".dbf" })
            {
                if (!File.Exists(Path.ChangeExtension(path, extension)))
                {
                    error = caption + " 缺少 " + extension + " 配套文件。";
                    return false;
                }
            }
            return true;
        }

        private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null) AppendLog(e.Data);
        }

        private void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null) AppendLog(e.Data);
        }

        private void Process_Exited(object sender, EventArgs e)
        {
            int exitCode = -1;
            try { exitCode = process.ExitCode; }
            catch { }
            BeginInvoke(new Action<int>(FinishProcess), exitCode);
        }

        private void FinishProcess(int exitCode)
        {
            if (IsDisposed) return;
            Succeeded = exitCode == 0;
            AppendLog("");
            AppendLog("PREPROCESS_GUI_EXIT_CODE " + exitCode);
            SetRunning(false);
            if (Succeeded)
            {
                btnClose.Text = "完成并返回";
                AppendLog("PREPROCESS_GUI_SUCCESS output=" + Path.GetFullPath(OutputRoot));
            }
            else if (cancelled)
            {
                AppendLog("PREPROCESS_GUI_STOPPED");
            }
            else
            {
                MessageBox.Show(this, "预处理未成功完成，请查看日志中的 PREPROCESS_FAILED / PREPROCESS_EXCEPTION / ARCPY_ERROR / QGIS_ERROR。",
                    "预处理失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            if (!running || process == null) return;
            try
            {
                if (!process.HasExited)
                {
                    cancelled = true;
                    process.Kill();
                    AppendLog("PREPROCESS_GUI_CANCELLED");
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "停止预处理失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void BtnClose_Click(object sender, EventArgs e)
        {
            if (running) return;
            DialogResult = Succeeded ? DialogResult.OK : DialogResult.Cancel;
            Close();
        }

        private void PreprocessingForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!running) return;
            e.Cancel = true;
            MessageBox.Show(this, "预处理仍在运行，请先点击“停止预处理”。", "预处理运行中",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void SetRunning(bool value)
        {
            running = value;
            btnRun.Enabled = !value;
            btnCancel.Enabled = value;
            btnClose.Enabled = !value;
            cmbEngine.Enabled = !value;
            txtPython.Enabled = !value;
            txtWata.Enabled = !value;
            txtRiver.Enabled = !value;
            txtNode.Enabled = !value;
            txtSoil.Enabled = !value;
            txtLanduse.Enabled = !value;
            txtOutput.Enabled = !value;
            foreach (Button button in browseButtons)
                button.Enabled = !value;
        }

        private void AppendLog(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(AppendLog), text);
                return;
            }
            txtLog.AppendText(text + Environment.NewLine);
            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.ScrollToCaret();
        }

        private static void AddUniquePath(List<string> paths, string path)
        {
            if (paths == null || string.IsNullOrWhiteSpace(path)) return;
            foreach (string existing in paths)
            {
                if (string.Equals(existing, path, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            paths.Add(path);
        }

        private static IEnumerable<RegistryKey> OpenRegistryRoots(RegistryHive hive)
        {
            var roots = new List<RegistryKey>();
            try
            {
                roots.Add(RegistryKey.OpenBaseKey(hive, RegistryView.Registry64));
            }
            catch { }
            try
            {
                roots.Add(RegistryKey.OpenBaseKey(hive, RegistryView.Registry32));
            }
            catch { }
            if (roots.Count == 0)
            {
                try
                {
                    roots.Add(hive == RegistryHive.CurrentUser
                        ? Registry.CurrentUser
                        : Registry.LocalMachine);
                }
                catch { }
            }
            return roots;
        }

        private static string ReadRegistryString(
            RegistryHive hive, string subKeyPath, string valueName)
        {
            foreach (RegistryKey root in OpenRegistryRoots(hive))
            {
                try
                {
                    using (root)
                    using (RegistryKey key = root.OpenSubKey(subKeyPath))
                    {
                        if (key == null) continue;
                        object value = key.GetValue(valueName);
                        string text = value == null ? "" : Convert.ToString(value);
                        if (!string.IsNullOrWhiteSpace(text))
                            return text.Trim().Trim('"');
                    }
                }
                catch { }
            }
            return "";
        }

        private static List<string> GetArcGisProInstallDirectoriesFromRegistry()
        {
            var result = new List<string>();
            RegistryHive[] hives = new RegistryHive[]
            {
                RegistryHive.LocalMachine,
                RegistryHive.CurrentUser
            };

            foreach (RegistryHive hive in hives)
            {
                foreach (RegistryKey root in OpenRegistryRoots(hive))
                {
                    try
                    {
                        using (root)
                        using (RegistryKey key = root.OpenSubKey(@"SOFTWARE\ESRI\ArcGISPro"))
                        {
                            if (key == null) continue;
                            string installDir = Convert.ToString(key.GetValue("InstallDir")) ?? "";
                            if (string.IsNullOrWhiteSpace(installDir))
                                installDir = Convert.ToString(key.GetValue("InstallLocation")) ?? "";
                            if (!string.IsNullOrWhiteSpace(installDir))
                                AddUniquePath(result, installDir.Trim().Trim('"'));
                        }
                    }
                    catch { }
                }
            }

            // Windows App Paths is another reliable source for non-default installations.
            foreach (RegistryHive hive in hives)
            {
                string exe = ReadRegistryString(
                    hive,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\ArcGISPro.exe",
                    "");
                if (!string.IsNullOrWhiteSpace(exe))
                {
                    try
                    {
                        string bin = Path.GetDirectoryName(exe);
                        string installDir = string.IsNullOrWhiteSpace(bin)
                            ? "" : Directory.GetParent(bin).FullName;
                        if (!string.IsNullOrWhiteSpace(installDir))
                            AddUniquePath(result, installDir);
                    }
                    catch { }
                }
            }
            return result;
        }

        private static void AddArcMapRegistryCandidates(List<string> candidates)
        {
            RegistryHive[] hives = new RegistryHive[]
            {
                RegistryHive.LocalMachine,
                RegistryHive.CurrentUser
            };

            foreach (RegistryHive hive in hives)
            {
                foreach (RegistryKey root in OpenRegistryRoots(hive))
                {
                    try
                    {
                        using (root)
                        using (RegistryKey esri = root.OpenSubKey(@"SOFTWARE\ESRI"))
                        {
                            if (esri == null) continue;
                            foreach (string subName in esri.GetSubKeyNames())
                            {
                                if (!subName.StartsWith("Desktop10",
                                    StringComparison.OrdinalIgnoreCase))
                                    continue;

                                using (RegistryKey desktop = esri.OpenSubKey(subName))
                                {
                                    if (desktop == null) continue;
                                    string installDir =
                                        Convert.ToString(desktop.GetValue("InstallDir")) ?? "";
                                    string version = subName.Substring("Desktop".Length);

                                    // ArcMap's default Python is normally X:\Python27\ArcGIS10.x.
                                    foreach (DriveInfo drive in DriveInfo.GetDrives())
                                    {
                                        try
                                        {
                                            if (!drive.IsReady ||
                                                drive.DriveType != DriveType.Fixed) continue;
                                            AddUniquePath(candidates,
                                                Path.Combine(drive.RootDirectory.FullName,
                                                    "Python27", "ArcGIS" + version, "python.exe"));
                                        }
                                        catch { }
                                    }

                                    // Also cover custom layouts near the Desktop install root.
                                    if (!string.IsNullOrWhiteSpace(installDir))
                                    {
                                        installDir = installDir.Trim().Trim('"');
                                        AddUniquePath(candidates,
                                            Path.Combine(installDir, "Python27", "python.exe"));
                                        string parent = "";
                                        try { parent = Directory.GetParent(installDir).FullName; }
                                        catch { }
                                        if (!string.IsNullOrWhiteSpace(parent))
                                            AddArcMapCommonBaseCandidates(candidates, parent);
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        private static void AddArcMapCommonBaseCandidates(
            List<string> candidates, string baseRoot)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(baseRoot) ||
                    !Directory.Exists(baseRoot)) return;

                string[] dirs = Directory.GetDirectories(baseRoot, "ArcGIS*");
                Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
                Array.Reverse(dirs);
                foreach (string dir in dirs)
                {
                    string name = Path.GetFileName(dir) ?? "";
                    if (name.IndexOf("ArcGISPro", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;

                    AddUniquePath(candidates, Path.Combine(dir, "python.exe"));
                    AddUniquePath(candidates, Path.Combine(dir, "Python27", "python.exe"));

                    try
                    {
                        foreach (string child in Directory.GetDirectories(dir))
                        {
                            string childName = Path.GetFileName(child) ?? "";
                            if (childName.IndexOf("pro", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                childName.IndexOf("py3", StringComparison.OrdinalIgnoreCase) >= 0)
                                continue;
                            AddUniquePath(candidates, Path.Combine(child, "python.exe"));
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static List<string> GetQgisInstallDirectoriesFromRegistry()
        {
            var result = new List<string>();
            RegistryHive[] hives = new RegistryHive[]
            {
                RegistryHive.LocalMachine,
                RegistryHive.CurrentUser
            };

            // Installed-program entries.
            foreach (RegistryHive hive in hives)
            {
                foreach (RegistryKey root in OpenRegistryRoots(hive))
                {
                    try
                    {
                        using (root)
                        using (RegistryKey uninstall = root.OpenSubKey(
                            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                        {
                            if (uninstall == null) continue;
                            foreach (string subName in uninstall.GetSubKeyNames())
                            {
                                using (RegistryKey app = uninstall.OpenSubKey(subName))
                                {
                                    if (app == null) continue;
                                    string displayName =
                                        Convert.ToString(app.GetValue("DisplayName")) ?? "";
                                    if (displayName.IndexOf("QGIS",
                                        StringComparison.OrdinalIgnoreCase) < 0)
                                        continue;

                                    string location =
                                        Convert.ToString(app.GetValue("InstallLocation")) ?? "";
                                    if (!string.IsNullOrWhiteSpace(location))
                                        AddUniquePath(result, location.Trim().Trim('"'));

                                    string icon =
                                        Convert.ToString(app.GetValue("DisplayIcon")) ?? "";
                                    if (!string.IsNullOrWhiteSpace(icon))
                                    {
                                        icon = icon.Trim().Trim('"');
                                        int comma = icon.IndexOf(',');
                                        if (comma > 0) icon = icon.Substring(0, comma);
                                        try
                                        {
                                            string bin = Path.GetDirectoryName(icon);
                                            string installDir = string.IsNullOrWhiteSpace(bin)
                                                ? "" : Directory.GetParent(bin).FullName;
                                            if (!string.IsNullOrWhiteSpace(installDir))
                                                AddUniquePath(result, installDir);
                                        }
                                        catch { }
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            // App Paths for common QGIS executables.
            string[] appNames = new string[]
            {
                "qgis-bin.exe",
                "qgis-ltr-bin.exe",
                "qgis.exe"
            };
            foreach (RegistryHive hive in hives)
            {
                foreach (string appName in appNames)
                {
                    string exe = ReadRegistryString(
                        hive,
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + appName,
                        "");
                    if (string.IsNullOrWhiteSpace(exe)) continue;
                    try
                    {
                        string bin = Path.GetDirectoryName(exe);
                        string installDir = string.IsNullOrWhiteSpace(bin)
                            ? "" : Directory.GetParent(bin).FullName;
                        if (!string.IsNullOrWhiteSpace(installDir))
                            AddUniquePath(result, installDir);
                    }
                    catch { }
                }
            }

            return result;
        }

        private static void AddQgisCandidatesFromInstallDirectory(
            string installDir, List<string> candidates)
        {
            if (string.IsNullOrWhiteSpace(installDir)) return;
            installDir = installDir.Trim().Trim('"');
            AddUniquePath(candidates,
                Path.Combine(installDir, "bin", "python-qgis.bat"));
            AddUniquePath(candidates,
                Path.Combine(installDir, "bin", "python-qgis-ltr.bat"));

            // Some uninstall entries point to a parent bundle directory.
            AddQgisCandidatesFromDirectory(installDir, candidates);
        }

        internal static string FindArcGisPython()
        {
            var envRoots = new List<string>();

            // 1) Windows registry: most reliable for normal ArcGIS Pro installations.
            foreach (string installDir in GetArcGisProInstallDirectoriesFromRegistry())
            {
                AddUniquePath(envRoots,
                    Path.Combine(installDir, "bin", "Python", "envs"));
            }

            // 2) Official/default Program Files locations.
            var programRoots = new List<string>();
            string programW6432 = Environment.GetEnvironmentVariable("ProgramW6432");
            if (!string.IsNullOrWhiteSpace(programW6432))
                AddUniquePath(programRoots, programW6432);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFiles))
                AddUniquePath(programRoots, programFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrWhiteSpace(programFilesX86))
                AddUniquePath(programRoots, programFilesX86);

            foreach (string root in programRoots)
                AddUniquePath(envRoots,
                    Path.Combine(root, "ArcGIS", "Pro", "bin", "Python", "envs"));

            // 3) Common custom installation layouts on fixed drives.
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
                    string root = drive.RootDirectory.FullName;
                    AddUniquePath(envRoots,
                        Path.Combine(root, "Apps", "GIS_RS", "ArcGIS", "Pro", "bin", "Python", "envs"));
                    AddUniquePath(envRoots,
                        Path.Combine(root, "Apps", "GIS_RS", "ArcGIS", "bin", "Python", "envs"));
                    AddUniquePath(envRoots,
                        Path.Combine(root, "Apps", "ArcGIS", "Pro", "bin", "Python", "envs"));
                    AddUniquePath(envRoots,
                        Path.Combine(root, "GIS", "ArcGIS", "Pro", "bin", "Python", "envs"));
                    AddUniquePath(envRoots,
                        Path.Combine(root, "Software", "ArcGIS", "Pro", "bin", "Python", "envs"));
                }
                catch { }
            }

            // Prefer the standard Pro environment; fall back to any environment under envs.
            foreach (string envRoot in envRoots)
            {
                string standard = Path.Combine(envRoot, "arcgispro-py3", "python.exe");
                if (File.Exists(standard)) return standard;

                try
                {
                    if (!Directory.Exists(envRoot)) continue;
                    string[] directories = Directory.GetDirectories(envRoot);
                    Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
                    foreach (string directory in directories)
                    {
                        string candidate = Path.Combine(directory, "python.exe");
                        if (File.Exists(candidate) && LooksLikeArcGisProRuntime(candidate))
                            return candidate;
                    }
                }
                catch { }
            }

            // 4) PATH fallback.
            string pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string part in pathValue.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    string directory = part.Trim().Trim('"');
                    if (directory.Length == 0) continue;
                    string candidate = Path.Combine(directory, "python.exe");
                    if (File.Exists(candidate) && LooksLikeArcGisProRuntime(candidate))
                        return candidate;
                }
                catch { }
            }

            // Manual browsing remains available in the UI when nothing is detected.
            return "";
        }

        internal static string FindArcGisDesktopPython()
        {
            // Only reuse a positive cache entry.  A failed ArcMap probe may be caused by a
            // slow first arcpy import, so an empty result must be allowed to retry later.
            if (arcGisDesktopPythonCacheInitialized &&
                !string.IsNullOrWhiteSpace(arcGisDesktopPythonCache) &&
                File.Exists(arcGisDesktopPythonCache))
            {
                return arcGisDesktopPythonCache;
            }

            var candidates = new List<string>();

            // 1) Windows registry: ArcGIS Desktop/ArcMap installation information.
            AddArcMapRegistryCandidates(candidates);

            // 2) Official/default Python27 and common custom layouts.
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
                    string root = drive.RootDirectory.FullName;

                    // ArcMap / ArcGIS Desktop default Python 2.7 layout.
                    string python27 = Path.Combine(root, "Python27");
                    if (Directory.Exists(python27))
                    {
                        string[] arcgisDirs = Directory.GetDirectories(python27, "ArcGIS*");
                        Array.Sort(arcgisDirs, StringComparer.OrdinalIgnoreCase);
                        Array.Reverse(arcgisDirs);
                        foreach (string directory in arcgisDirs)
                            candidates.Add(Path.Combine(directory, "python.exe"));
                    }

                    candidates.Add(Path.Combine(root, "Apps", "GIS_RS", "ArcGIS", "Python27", "python.exe"));
                    candidates.Add(Path.Combine(root, "Apps", "ArcGIS", "Python27", "python.exe"));

                    // Common custom ArcMap environments used by this workflow, e.g.
                    // ...\Apps\Development\ArcGIS10.x_envs\ArcGIS10.x\python.exe.
                    // ArcGISPro_envs is deliberately excluded below.
                    AddArcMapDevelopmentCandidates(candidates,
                        Path.Combine(root, "Apps", "Development"));
                    AddArcMapCommonBaseCandidates(candidates,
                        Path.Combine(root, "GIS"));
                    AddArcMapCommonBaseCandidates(candidates,
                        Path.Combine(root, "Software"));
                    AddArcMapCommonBaseCandidates(candidates,
                        Path.Combine(root, "Apps"));
                }
                catch { }
            }

            // Custom installations may add the ArcGIS Python directory to PATH.
            string pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string part in pathValue.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string directory = part.Trim().Trim('"');
                if (directory.Length == 0) continue;
                if (directory.IndexOf("ArcGIS", StringComparison.OrdinalIgnoreCase) >= 0)
                    candidates.Add(Path.Combine(directory, "python.exe"));
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string candidate in candidates)
            {
                try
                {
                    string full = Path.GetFullPath(candidate);
                    if (!seen.Add(full)) continue;
                    if (!File.Exists(full)) continue;

                    // Never accept an ArcGIS Pro / conda clone as ArcMap merely because
                    // its path contains the word "ArcGIS".
                    if (LooksLikeArcGisProRuntime(full)) continue;

                    // ArcMap must be a Python 2 runtime with a working arcpy installation
                    // whose product identifies itself as ArcGIS Desktop.
                    if (!IsVerifiedArcMapPython(full)) continue;

                    arcGisDesktopPythonCache = full;
                    arcGisDesktopPythonCacheInitialized = true;
                    return full;
                }
                catch { }
            }

            arcGisDesktopPythonCache = "";
            arcGisDesktopPythonCacheInitialized = true;
            return "";
        }

        private static void AddArcMapDevelopmentCandidates(
            List<string> candidates, string developmentRoot)
        {
            try
            {
                if (!Directory.Exists(developmentRoot)) return;

                string[] environmentRoots = Directory.GetDirectories(
                    developmentRoot, "ArcGIS*_envs", SearchOption.TopDirectoryOnly);
                Array.Sort(environmentRoots, StringComparer.OrdinalIgnoreCase);
                Array.Reverse(environmentRoots);

                foreach (string environmentRoot in environmentRoots)
                {
                    string rootName = Path.GetFileName(environmentRoot) ?? "";
                    if (rootName.IndexOf("ArcGISPro", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;

                    candidates.Add(Path.Combine(environmentRoot, "python.exe"));

                    string[] environments = Directory.GetDirectories(environmentRoot);
                    Array.Sort(environments, StringComparer.OrdinalIgnoreCase);
                    Array.Reverse(environments);
                    foreach (string environment in environments)
                    {
                        string environmentName = Path.GetFileName(environment) ?? "";
                        if (environmentName.IndexOf("arcgispro", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            environmentName.IndexOf("py3", StringComparison.OrdinalIgnoreCase) >= 0)
                            continue;
                        candidates.Add(Path.Combine(environment, "python.exe"));
                    }
                }
            }
            catch { }
        }

        private static bool IsVerifiedArcMapPython(string pythonPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(pythonPath) || !File.Exists(pythonPath))
                    return false;

                string lower = pythonPath.ToLowerInvariant();
                if (lower.Contains("arcgispro") || lower.Contains("arcgispro-py3") ||
                    lower.Contains("py3-clone") || LooksLikeArcGisProRuntime(pythonPath))
                    return false;

                // ArcMap / ArcGIS Desktop 10.x uses Python 2.7.  ArcGIS Pro uses Python 3,
                // so this is the strongest simple discriminator and prevents Pro clones
                // from being accepted as ArcMap.
                string majorVersion = RunPythonProbe(
                    pythonPath,
                    "import sys;sys.stdout.write(str(sys.version_info[0]))",
                    10000);
                if (!string.Equals((majorVersion ?? "").Trim(), "2", StringComparison.Ordinal))
                    return false;

                // Verify that the selected Python can really import ArcPy.  Do not make
                // GetInstallInfo/ProductName mandatory: some ArcMap custom environments
                // can import ArcPy correctly while GetInstallInfo is unavailable or slow.
                string arcpyProbe = RunPythonProbe(
                    pythonPath,
                    "import arcpy,sys;sys.stdout.write('ARCPY_OK')",
                    45000);
                if (!string.Equals((arcpyProbe ?? "").Trim(), "ARCPY_OK",
                                   StringComparison.OrdinalIgnoreCase))
                    return false;

                // Best-effort product check.  Reject Pro if it is explicitly reported;
                // otherwise Python 2.7 + working ArcPy is sufficient for ArcMap.
                string productName = RunPythonProbe(
                    pythonPath,
                    "import arcpy,sys;info=arcpy.GetInstallInfo();sys.stdout.write(str(info.get('ProductName','')))",
                    30000);
                string product = (productName ?? "").Trim().ToLowerInvariant();
                if (product.Contains("pro"))
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string RunPythonProbe(string pythonPath, string code, int timeoutMilliseconds)
        {
            Process probe = null;
            try
            {
                var startInfo = new ProcessStartInfo();
                startInfo.FileName = pythonPath;
                startInfo.Arguments = "-c \"" + code.Replace("\"", "\\\"") + "\"";
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                startInfo.WorkingDirectory = Path.GetDirectoryName(pythonPath) ?? "";

                probe = Process.Start(startInfo);
                if (probe == null) return "";
                if (!probe.WaitForExit(timeoutMilliseconds))
                {
                    try { probe.Kill(); }
                    catch { }
                    return "";
                }

                string stdout = probe.StandardOutput.ReadToEnd();
                string stderr = probe.StandardError.ReadToEnd();
                if (probe.ExitCode != 0) return "";
                if (!string.IsNullOrWhiteSpace(stderr) && string.IsNullOrWhiteSpace(stdout))
                    return "";
                return stdout ?? "";
            }
            catch
            {
                return "";
            }
            finally
            {
                if (probe != null) probe.Dispose();
            }
        }

        internal static string FindQgisPythonLauncher()
        {
            var candidates = new List<string>();

            // 1) PATH.
            string pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string part in pathValue.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string directory = part.Trim().Trim('"');
                if (directory.Length == 0) continue;
                AddUniquePath(candidates, Path.Combine(directory, "python-qgis.bat"));
                AddUniquePath(candidates, Path.Combine(directory, "python-qgis-ltr.bat"));
            }

            // 2) OSGeo4W standard roots.
            string systemDrive = Environment.GetEnvironmentVariable("SystemDrive");
            if (string.IsNullOrWhiteSpace(systemDrive)) systemDrive = "C:";
            string systemRoot = systemDrive + Path.DirectorySeparatorChar;
            AddUniquePath(candidates, Path.Combine(systemRoot, "OSGeo4W", "bin", "python-qgis.bat"));
            AddUniquePath(candidates, Path.Combine(systemRoot, "OSGeo4W", "bin", "python-qgis-ltr.bat"));
            AddUniquePath(candidates, Path.Combine(systemRoot, "OSGeo4W64", "bin", "python-qgis.bat"));
            AddUniquePath(candidates, Path.Combine(systemRoot, "OSGeo4W64", "bin", "python-qgis-ltr.bat"));

            // 3) Windows installed-program information / registry App Paths.
            foreach (string installDir in GetQgisInstallDirectoriesFromRegistry())
                AddQgisCandidatesFromInstallDirectory(installDir, candidates);

            // 4) Program Files.
            var roots = new List<string>();
            string programW6432 = Environment.GetEnvironmentVariable("ProgramW6432");
            if (!string.IsNullOrWhiteSpace(programW6432)) AddUniquePath(roots, programW6432);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFiles)) AddUniquePath(roots, programFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrWhiteSpace(programFilesX86)) AddUniquePath(roots, programFilesX86);

            foreach (string root in roots)
                AddQgisCandidatesFromDirectory(root, candidates);

            // 5) Common custom installation layouts.
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
                    string root = drive.RootDirectory.FullName;
                    AddQgisCandidatesFromDirectory(Path.Combine(root, "Apps"), candidates);
                    AddQgisCandidatesFromDirectory(Path.Combine(root, "Apps", "GIS_RS"), candidates);
                    AddQgisCandidatesFromDirectory(Path.Combine(root, "GIS"), candidates);
                    AddQgisCandidatesFromDirectory(Path.Combine(root, "Software"), candidates);
                }
                catch { }
            }

            foreach (string candidate in candidates)
            {
                try
                {
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }

            // Manual browsing remains available in the UI when nothing is detected.
            return "";
        }

        private static void AddQgisCandidatesFromDirectory(string root, List<string> candidates)
        {
            try
            {
                if (!Directory.Exists(root)) return;
                string[] qgisDirectories = Directory.GetDirectories(root, "QGIS*");
                Array.Sort(qgisDirectories, StringComparer.OrdinalIgnoreCase);
                Array.Reverse(qgisDirectories);
                foreach (string directory in qgisDirectories)
                {
                    AddUniquePath(candidates, Path.Combine(directory, "bin", "python-qgis.bat"));
                    AddUniquePath(candidates, Path.Combine(directory, "bin", "python-qgis-ltr.bat"));
                }
            }
            catch { }
        }

        private enum PreprocessEngine
        {
            Auto = 0,
            ArcGisPro = 1,
            ArcGisDesktop = 2,
            Qgis = 3
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private sealed class BrowseFileTarget
        {
            internal readonly Control Control;
            internal readonly string Filter;
            internal BrowseFileTarget(Control control, string filter)
            {
                Control = control;
                Filter = filter;
            }
        }
    }
}
