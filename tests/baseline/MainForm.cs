using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace UnifiedHydroLauncher
{
    public sealed class MainForm : Form
    {
        private TextBox txtExe;
        private TextBox txtUnitRoot;
        private TextBox txtProject;
        private ComboBox cmbUnit;
        private TextBox txtStation;
        private TextBox txtRainfall;
        private DateTimePicker dtStart;
        private DateTimePicker dtEnd;
        private DateTimePicker dtSourceStart;
        private NumericUpDown numInterval;
        private ComboBox cmbMode;

        private Button btnBrowseExe;
        private Button btnBrowseRoot;
        private Button btnScan;
        private Button btnRun;
        private Button btnCancel;
        private Button btnClear;
        private Button btnOpenOutput;

        private DataGridView gridUnits;
        private RichTextBox txtLog;
        private ProgressBar progressUnits;
        private Label lblProgress;
        private Label lblCurrentUnit;
        private Label lblStatus;

        private Process _process;
        private string _currentUnit = "";
        private int _expectedProgressUnits = 0;
        private readonly HashSet<string> _completedUnits =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _unitsWithSourceLog =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _unitRows =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private string _tableStateContextKey = "";

        public MainForm()
        {
            Text = "Unified Hydro Workflow V4.2.1";
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            MinimumSize = new Size(1180, 800);
            Size = new Size(1320, 920);
            Font = new Font("Microsoft YaHei UI", 9F);

            BuildUi();
            LoadDefaults();
        }

        private void BuildUi()
        {
            var rootPanel = new TableLayoutPanel();
            rootPanel.Dock = DockStyle.Fill;
            rootPanel.ColumnCount = 1;
            rootPanel.RowCount = 5;
            rootPanel.Padding = new Padding(12);
            rootPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rootPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));
            rootPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 44F));
            rootPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 56F));
            rootPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            Controls.Add(rootPanel);

            BuildSettings(rootPanel);
            BuildActionAndProgress(rootPanel);
            BuildUnitTable(rootPanel);
            BuildLog(rootPanel);

            lblStatus = new Label();
            lblStatus.Dock = DockStyle.Fill;
            lblStatus.Text = "就绪";
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            rootPanel.Controls.Add(lblStatus, 0, 4);
        }

        private void BuildSettings(TableLayoutPanel rootPanel)
        {
            var settingsGroup = new GroupBox();
            settingsGroup.Text = "运行参数";
            settingsGroup.Dock = DockStyle.Top;
            settingsGroup.AutoSize = true;
            settingsGroup.Padding = new Padding(10, 12, 10, 10);
            rootPanel.Controls.Add(settingsGroup, 0, 0);

            var settings = new TableLayoutPanel();
            settings.Dock = DockStyle.Top;
            settings.AutoSize = true;
            settings.ColumnCount = 5;
            settings.RowCount = 8;
            settings.Padding = new Padding(4);
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115F));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115F));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105F));
            settingsGroup.Controls.Add(settings);

            for (int index = 0; index < 8; index++)
                settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));

            int row = 0;

            AddLabel(settings, "核心程序", row);
            txtExe = NewTextBox();
            settings.Controls.Add(txtExe, 1, row);
            settings.SetColumnSpan(txtExe, 3);
            btnBrowseExe = NewButton("浏览...");
            btnBrowseExe.Click += BtnBrowseExe_Click;
            settings.Controls.Add(btnBrowseExe, 4, row++);

            AddLabel(settings, "计算单元根目录", row);
            txtUnitRoot = NewTextBox();
            settings.Controls.Add(txtUnitRoot, 1, row);
            settings.SetColumnSpan(txtUnitRoot, 3);
            btnBrowseRoot = NewButton("浏览...");
            btnBrowseRoot.Click += BtnBrowseRoot_Click;
            settings.Controls.Add(btnBrowseRoot, 4, row++);

            AddLabel(settings, "工程名称", row);
            txtProject = NewTextBox();
            settings.Controls.Add(txtProject, 1, row);

            AddSmallLabel(settings, "运行模式", 2, row);
            cmbMode = new ComboBox();
            cmbMode.Dock = DockStyle.Fill;
            cmbMode.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbMode.Items.AddRange(new object[]
            {
                "计算",
                "仅准备",
                "仅预检"
            });
            settings.Controls.Add(cmbMode, 3, row);

            btnScan = NewButton("扫描单元");
            btnScan.Click += BtnScan_Click;
            settings.Controls.Add(btnScan, 4, row++);

            AddLabel(settings, "计算单元", row);
            cmbUnit = new ComboBox();
            cmbUnit.Dock = DockStyle.Fill;
            cmbUnit.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbUnit.SelectedIndexChanged += CmbUnit_SelectedIndexChanged;
            settings.Controls.Add(cmbUnit, 1, row);
            settings.SetColumnSpan(cmbUnit, 4);
            row++;

            AddLabel(settings, "站点文件名", row);
            txtStation = NewTextBox();
            settings.Controls.Add(txtStation, 1, row);

            AddSmallLabel(settings, "降雨文件名", 2, row);
            txtRainfall = NewTextBox();
            settings.Controls.Add(txtRainfall, 3, row);
            settings.SetColumnSpan(txtRainfall, 2);
            row++;

            AddLabel(settings, "场次开始", row);
            dtStart = NewDateTimePicker();
            settings.Controls.Add(dtStart, 1, row);

            AddSmallLabel(settings, "场次结束", 2, row);
            dtEnd = NewDateTimePicker();
            settings.Controls.Add(dtEnd, 3, row);
            settings.SetColumnSpan(dtEnd, 2);
            row++;

            AddLabel(settings, "上游水源开始", row);
            dtSourceStart = NewDateTimePicker();
            settings.Controls.Add(dtSourceStart, 1, row);

            AddSmallLabel(settings, "时间步长(min)", 2, row);
            numInterval = new NumericUpDown();
            numInterval.Dock = DockStyle.Fill;
            numInterval.Minimum = 1;
            numInterval.Maximum = 1440;
            settings.Controls.Add(numInterval, 3, row);
            settings.SetColumnSpan(numInterval, 2);
            row++;

            var tips = new Label();
            tips.AutoSize = true;
            tips.Text =
                "说明：核心程序与 project.db 应位于同一模型目录；建议模型目录路径不要包含空格。";
            tips.ForeColor = Color.DimGray;
            tips.Anchor = AnchorStyles.Left;
            settings.Controls.Add(tips, 1, row);
            settings.SetColumnSpan(tips, 4);
        }

        private void BuildActionAndProgress(TableLayoutPanel rootPanel)
        {
            var actionRow = new TableLayoutPanel();
            actionRow.Dock = DockStyle.Fill;
            actionRow.ColumnCount = 2;
            actionRow.RowCount = 1;
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            rootPanel.Controls.Add(actionRow, 0, 1);

            var actionPanel = new FlowLayoutPanel();
            actionPanel.Dock = DockStyle.Fill;
            actionPanel.AutoSize = true;
            actionPanel.FlowDirection = FlowDirection.LeftToRight;
            actionPanel.WrapContents = false;
            actionPanel.Padding = new Padding(0, 8, 0, 5);
            actionRow.Controls.Add(actionPanel, 0, 0);

            btnRun = NewActionButton("开始运行", 120);
            btnRun.Click += BtnRun_Click;
            actionPanel.Controls.Add(btnRun);

            btnCancel = NewActionButton("停止", 85);
            btnCancel.Enabled = false;
            btnCancel.Click += BtnCancel_Click;
            actionPanel.Controls.Add(btnCancel);

            btnOpenOutput = NewActionButton("打开输出目录", 125);
            btnOpenOutput.Click += BtnOpenOutput_Click;
            actionPanel.Controls.Add(btnOpenOutput);

            btnClear = NewActionButton("清空日志", 100);
            btnClear.Click += delegate { txtLog.Clear(); };
            actionPanel.Controls.Add(btnClear);

            var progressPanel = new TableLayoutPanel();
            progressPanel.Dock = DockStyle.Fill;
            progressPanel.ColumnCount = 2;
            progressPanel.RowCount = 2;
            progressPanel.Padding = new Padding(14, 7, 0, 2);
            progressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            progressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180F));
            progressPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 23F));
            progressPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            actionRow.Controls.Add(progressPanel, 1, 0);

            lblCurrentUnit = new Label();
            lblCurrentUnit.Dock = DockStyle.Fill;
            lblCurrentUnit.Text = "当前单元：—";
            lblCurrentUnit.TextAlign = ContentAlignment.MiddleLeft;
            progressPanel.Controls.Add(lblCurrentUnit, 0, 0);

            lblProgress = new Label();
            lblProgress.Dock = DockStyle.Fill;
            lblProgress.Text = "进度：0 / 0";
            lblProgress.TextAlign = ContentAlignment.MiddleRight;
            progressPanel.Controls.Add(lblProgress, 1, 0);

            progressUnits = new ProgressBar();
            progressUnits.Dock = DockStyle.Fill;
            progressUnits.Minimum = 0;
            progressUnits.Maximum = 1;
            progressUnits.Value = 0;
            progressPanel.Controls.Add(progressUnits, 0, 1);
            progressPanel.SetColumnSpan(progressUnits, 2);
        }

        private void BuildUnitTable(TableLayoutPanel rootPanel)
        {
            var group = new GroupBox();
            group.Text = "计算单元状态";
            group.Dock = DockStyle.Fill;
            group.Padding = new Padding(8);
            rootPanel.Controls.Add(group, 0, 2);

            gridUnits = new DataGridView();
            gridUnits.Dock = DockStyle.Fill;
            gridUnits.ReadOnly = true;
            gridUnits.AllowUserToAddRows = false;
            gridUnits.AllowUserToDeleteRows = false;
            gridUnits.AllowUserToResizeRows = false;
            gridUnits.MultiSelect = false;
            gridUnits.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            gridUnits.RowHeadersVisible = false;
            gridUnits.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            gridUnits.BackgroundColor = SystemColors.Window;
            gridUnits.BorderStyle = BorderStyle.Fixed3D;
            gridUnits.CellDoubleClick += GridUnits_CellDoubleClick;

            AddGridColumn("Unit", "计算单元", 170);
            AddGridColumn("Shapes", "5类SHP", 85);
            AddGridColumn("Station", "站点", 80);
            AddGridColumn("Rainfall", "降雨", 80);
            AddGridColumn("Source", "上游水源表", 125);
            AddGridColumn("Coverage", "覆盖率", 175);
            AddGridColumn("Status", "状态", 135);
            AddGridColumn("Note", "说明", 210);

            group.Controls.Add(gridUnits);
        }

        private void BuildLog(TableLayoutPanel rootPanel)
        {
            var logGroup = new GroupBox();
            logGroup.Text = "运行日志";
            logGroup.Dock = DockStyle.Fill;
            logGroup.Padding = new Padding(8);
            rootPanel.Controls.Add(logGroup, 0, 3);

            txtLog = new RichTextBox();
            txtLog.Dock = DockStyle.Fill;
            txtLog.ReadOnly = true;
            txtLog.WordWrap = false;
            txtLog.Font = new Font("Consolas", 9F);
            logGroup.Controls.Add(txtLog);
        }

        private void AddGridColumn(
            string name,
            string header,
            float fillWeight)
        {
            var column = new DataGridViewTextBoxColumn();
            column.Name = name;
            column.HeaderText = header;
            column.FillWeight = fillWeight;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            gridUnits.Columns.Add(column);
        }

        private static TextBox NewTextBox()
        {
            var box = new TextBox();
            box.Dock = DockStyle.Fill;
            box.Margin = new Padding(3, 5, 3, 5);
            return box;
        }

        private static Button NewButton(string text)
        {
            var button = new Button();
            button.Text = text;
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(3);
            return button;
        }

        private static Button NewActionButton(string text, int width)
        {
            var button = new Button();
            button.Text = text;
            button.Width = width;
            button.Height = 32;
            button.Margin = new Padding(0, 0, 10, 0);
            return button;
        }

        private static DateTimePicker NewDateTimePicker()
        {
            var picker = new DateTimePicker();
            picker.Dock = DockStyle.Fill;
            picker.Format = DateTimePickerFormat.Custom;
            picker.CustomFormat = "yyyy/MM/dd HH:mm:ss";
            picker.ShowUpDown = true;
            picker.Margin = new Padding(3, 5, 3, 5);
            return picker;
        }

        private static void AddLabel(
            TableLayoutPanel panel,
            string text,
            int row)
        {
            var label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Anchor = AnchorStyles.Left;
            panel.Controls.Add(label, 0, row);
        }

        private static void AddSmallLabel(
            TableLayoutPanel panel,
            string text,
            int column,
            int row)
        {
            var label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Anchor = AnchorStyles.Left;
            panel.Controls.Add(label, column, row);
        }

        private void LoadDefaults()
        {
            txtExe.Text = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "UnifiedHydroWorkflow.exe");

            txtStation.Text = "站点信息.xlsx";
            txtRainfall.Text = "20060803111降雨.xlsx";

            dtStart.Value = new DateTime(2006, 8, 1, 1, 0, 0);
            dtEnd.Value = new DateTime(2006, 8, 6, 0, 0, 0);
            dtSourceStart.Value = new DateTime(2006, 8, 3, 1, 0, 0);

            numInterval.Value = 60;
            cmbMode.SelectedIndex = 0;

            cmbUnit.Items.Clear();
            cmbUnit.Items.Add("全部单元 (--all)");
            cmbUnit.SelectedIndex = 0;

            ResetProgress(0);
        }

        private void BtnBrowseExe_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "Executable (*.exe)|*.exe|All files (*.*)|*.*";
                dialog.Title = "选择 UnifiedHydroWorkflow.exe";

                if (File.Exists(txtExe.Text))
                    dialog.InitialDirectory =
                        Path.GetDirectoryName(txtExe.Text);

                if (dialog.ShowDialog(this) == DialogResult.OK)
                    txtExe.Text = dialog.FileName;
            }
        }

        private void BtnBrowseRoot_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择包含各计算单元子文件夹的根目录";
                dialog.SelectedPath = txtUnitRoot.Text;

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    txtUnitRoot.Text = dialog.SelectedPath;
                    ScanUnits();
                }
            }
        }

        private void BtnScan_Click(object sender, EventArgs e)
        {
            ScanUnits();
        }

        private void ScanUnits()
        {
            string newContextKey = BuildTableStateContextKey();
            bool preserveRuntimeState =
                string.Equals(
                    _tableStateContextKey,
                    newContextKey,
                    StringComparison.Ordinal);

            Dictionary<string, UnitRowState> previousStates =
                preserveRuntimeState
                ? CaptureUnitRowStates()
                : new Dictionary<string, UnitRowState>(
                    StringComparer.OrdinalIgnoreCase);

            cmbUnit.Items.Clear();
            cmbUnit.Items.Add("全部单元 (--all)");
            gridUnits.Rows.Clear();
            _unitRows.Clear();

            string root = txtUnitRoot.Text.Trim();

            if (!Directory.Exists(root))
            {
                _tableStateContextKey = "";
                cmbUnit.SelectedIndex = 0;
                MessageBox.Show(
                    this,
                    "计算单元根目录不存在。",
                    "扫描失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            string[] directories = Directory.GetDirectories(root)
                .OrderBy(path => NaturalUnitNumber(Path.GetFileName(path)))
                .ThenBy(path => Path.GetFileName(path),
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (string directory in directories)
            {
                string unit = Path.GetFileName(directory);
                cmbUnit.Items.Add(unit);
                AddUnitRow(directory, unit);
            }

            if (preserveRuntimeState)
                RestoreUnitRowStates(previousStates);

            _tableStateContextKey = newContextKey;

            cmbUnit.SelectedIndex = 0;
            gridUnits.ClearSelection();
            lblStatus.Text =
                "扫描完成：发现 " + directories.Length + " 个计算单元";
            ResetProgress(directories.Length);
        }

        private string BuildTableStateContextKey()
        {
            string exe = txtExe == null ? "" : txtExe.Text.Trim();
            string root =
                txtUnitRoot == null ? "" : txtUnitRoot.Text.Trim();
            string project =
                txtProject == null ? "" : txtProject.Text.Trim();
            string station =
                txtStation == null ? "" : txtStation.Text.Trim();
            string rainfall =
                txtRainfall == null ? "" : txtRainfall.Text.Trim();

            string start =
                dtStart == null ? "" : FormatTime(dtStart.Value);
            string end =
                dtEnd == null ? "" : FormatTime(dtEnd.Value);
            string sourceStart =
                dtSourceStart == null
                ? ""
                : FormatTime(dtSourceStart.Value);

            string interval =
                numInterval == null
                ? ""
                : Decimal.ToInt32(numInterval.Value)
                    .ToString(CultureInfo.InvariantCulture);

            return string.Join(
                "\n",
                new[]
                {
                    NormalizeContextPath(exe),
                    NormalizeContextPath(root),
                    project,
                    station,
                    rainfall,
                    start,
                    end,
                    sourceStart,
                    interval
                });
        }

        private static string NormalizeContextPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            try
            {
                return Path.GetFullPath(value)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar)
                    .ToUpperInvariant();
            }
            catch
            {
                return value.Trim().ToUpperInvariant();
            }
        }

        private Dictionary<string, UnitRowState> CaptureUnitRowStates()
        {
            var states = new Dictionary<string, UnitRowState>(
                StringComparer.OrdinalIgnoreCase);

            foreach (DataGridViewRow row in gridUnits.Rows)
            {
                string unit =
                    Convert.ToString(row.Cells["Unit"].Value);

                if (string.IsNullOrWhiteSpace(unit))
                    continue;

                states[unit] = new UnitRowState(
                    Convert.ToString(row.Cells["Source"].Value),
                    Convert.ToString(row.Cells["Coverage"].Value),
                    Convert.ToString(row.Cells["Status"].Value),
                    Convert.ToString(row.Cells["Note"].Value));
            }

            return states;
        }

        private void RestoreUnitRowStates(
            Dictionary<string, UnitRowState> states)
        {
            foreach (KeyValuePair<string, UnitRowState> pair in states)
            {
                int rowIndex;
                if (!_unitRows.TryGetValue(pair.Key, out rowIndex))
                    continue;

                DataGridViewRow row = gridUnits.Rows[rowIndex];

                string freshlyScannedStatus =
                    Convert.ToString(row.Cells["Status"].Value);

                // 输入文件现在已经缺失时，不保留旧的“成功”等运行状态。
                if (freshlyScannedStatus == "输入缺失")
                    continue;

                UnitRowState previous = pair.Value;

                if (!string.IsNullOrWhiteSpace(previous.Coverage) &&
                    previous.Coverage != "—")
                    row.Cells["Coverage"].Value = previous.Coverage;

                if (!string.IsNullOrWhiteSpace(previous.Status))
                {
                    row.Cells["Status"].Value = previous.Status;
                    ApplyStatusStyle(row, previous.Status);
                }

                RestoreSourceStateWhenStillCompatible(
                    row,
                    previous.Source);

                if (ShouldRestoreRuntimeNote(previous.Status) &&
                    !string.IsNullOrWhiteSpace(previous.Note))
                    row.Cells["Note"].Value = previous.Note;
            }
        }

        private static void RestoreSourceStateWhenStillCompatible(
            DataGridViewRow row,
            string previousSource)
        {
            if (string.IsNullOrWhiteSpace(previousSource))
                return;

            string freshSource =
                Convert.ToString(row.Cells["Source"].Value);

            // 重新扫描确认水源表仍然存在时，保留更详细的运行期状态。
            if (freshSource == "已有" &&
                (previousSource == "已有/有效" ||
                 previousSource == "已生成" ||
                 previousSource == "已重建"))
            {
                row.Cells["Source"].Value = previousSource;
                return;
            }

            if (freshSource == "无需外部水源" &&
                previousSource == "无需外部水源")
            {
                row.Cells["Source"].Value = previousSource;
            }
        }

        private static bool ShouldRestoreRuntimeNote(string status)
        {
            return status == "失败" ||
                   status == "预检失败" ||
                   status == "覆盖不足" ||
                   status == "已停止";
        }

        private void AddUnitRow(string folder, string unit)
        {
            string[] shapes =
            {
                "wata.shp",
                "rivl.shp",
                "node.shp",
                "土地利用.shp",
                "土壤类型.shp"
            };

            int shapeCount = shapes.Count(
                name => File.Exists(Path.Combine(folder, name)));

            string stationName = txtStation.Text.Trim();
            string rainfallName = txtRainfall.Text.Trim();

            bool stationOk =
                stationName.Length > 0 &&
                File.Exists(Path.Combine(folder, stationName));

            bool rainfallOk =
                rainfallName.Length > 0 &&
                File.Exists(Path.Combine(folder, rainfallName));

            int sourceCount = 0;
            try
            {
                sourceCount =
                    Directory.GetFiles(folder, "*上游水源数据表.xls").Length;
            }
            catch
            {
                sourceCount = 0;
            }

            ExternalSourceScan sourceScan =
                DetectExternalSourceTopology(folder);

            string sourceStatus;
            if (sourceScan.Determined)
            {
                if (!sourceScan.RequiresExternalSource)
                    sourceStatus = "无需外部水源";
                else if (sourceCount > 0)
                    sourceStatus = "已有";
                else
                    sourceStatus = "需要/待生成";
            }
            else
            {
                sourceStatus =
                    sourceCount > 0 ? "已有（拓扑未判定）" : "未判定";
            }

            bool requiredInputsOk =
                shapeCount == 5 && stationOk && rainfallOk;

            string status =
                requiredInputsOk ? "待预检" : "输入缺失";

            string note = "";
            if (shapeCount != 5)
                note += "SHP缺" + (5 - shapeCount) + "；";
            if (!stationOk)
                note += "缺站点；";
            if (!rainfallOk)
                note += "缺降雨；";

            if (sourceScan.Determined)
            {
                if (sourceScan.RequiresExternalSource)
                {
                    note += string.Format(
                        "外部汇入河段{0}，外部上游编码{1}；",
                        sourceScan.SourceSegmentCount,
                        sourceScan.ExternalUpstreamCodeCount);
                }
                else if (sourceCount > 0)
                {
                    note += "无外部汇入但检测到已有水源表；";
                }
            }
            else if (!string.IsNullOrWhiteSpace(sourceScan.Error))
            {
                note += "水源拓扑未判定：" + sourceScan.Error + "；";
            }

            int index = gridUnits.Rows.Add(
                unit,
                shapeCount + "/5",
                stationOk ? "✓" : "×",
                rainfallOk ? "✓" : "×",
                sourceStatus,
                "—",
                status,
                note.TrimEnd('；'));

            _unitRows[unit] = index;
            ApplyStatusStyle(gridUnits.Rows[index], status);
        }

        private static ExternalSourceScan DetectExternalSourceTopology(
            string unitFolder)
        {
            string dbf = Path.Combine(unitFolder, "rivl.dbf");

            if (!File.Exists(dbf))
                return ExternalSourceScan.Unknown("缺少 rivl.dbf");

            try
            {
                List<DbfRecord> records = ReadRivlTopologyFromDbf(dbf);

                if (records.Count == 0)
                    return ExternalSourceScan.Unknown("rivl.dbf 无有效记录");

                var riverCodes = new HashSet<string>(
                    records
                        .Select(record => record.Rvcd)
                        .Where(code => !string.IsNullOrWhiteSpace(code)),
                    StringComparer.Ordinal);

                int sourceSegmentCount = 0;
                var externalCodes = new HashSet<string>(
                    StringComparer.Ordinal);

                foreach (DbfRecord record in records)
                {
                    List<string> outside = SplitTopologyCodes(record.Frvcd)
                        .Where(code =>
                            code != "-1" &&
                            !riverCodes.Contains(code))
                        .Distinct(StringComparer.Ordinal)
                        .ToList();

                    if (outside.Count == 0)
                        continue;

                    sourceSegmentCount++;

                    foreach (string code in outside)
                        externalCodes.Add(code);
                }

                return ExternalSourceScan.Known(
                    sourceSegmentCount > 0,
                    sourceSegmentCount,
                    externalCodes.Count);
            }
            catch (Exception exception)
            {
                return ExternalSourceScan.Unknown(exception.Message);
            }
        }

        private static List<DbfRecord> ReadRivlTopologyFromDbf(
            string dbfFile)
        {
            var result = new List<DbfRecord>();

            using (var stream = new FileStream(
                dbfFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite))
            using (var reader = new BinaryReader(stream))
            {
                if (stream.Length < 33)
                    throw new InvalidDataException("rivl.dbf 文件过小");

                stream.Seek(4, SeekOrigin.Begin);
                uint recordCount = reader.ReadUInt32();
                ushort headerLength = reader.ReadUInt16();
                ushort recordLength = reader.ReadUInt16();

                if (headerLength < 33 || recordLength < 2)
                    throw new InvalidDataException("rivl.dbf 头信息无效");

                var fields = new List<DbfField>();
                stream.Seek(32, SeekOrigin.Begin);

                int fieldOffset = 1; // 第 1 字节是删除标记

                while (stream.Position < headerLength)
                {
                    byte first = reader.ReadByte();
                    if (first == 0x0D)
                        break;

                    byte[] rest = reader.ReadBytes(31);
                    if (rest.Length != 31)
                        throw new EndOfStreamException(
                            "rivl.dbf 字段描述不完整");

                    byte[] descriptor = new byte[32];
                    descriptor[0] = first;
                    Buffer.BlockCopy(rest, 0, descriptor, 1, 31);

                    int nameLength = 0;
                    while (nameLength < 11 &&
                           descriptor[nameLength] != 0)
                        nameLength++;

                    string name = Encoding.ASCII
                        .GetString(descriptor, 0, nameLength)
                        .Trim();

                    int length = descriptor[16];

                    fields.Add(
                        new DbfField(name, fieldOffset, length));

                    fieldOffset += length;
                }

                DbfField rvcdField = fields.FirstOrDefault(
                    field => string.Equals(
                        field.Name,
                        "RVCD",
                        StringComparison.OrdinalIgnoreCase));

                DbfField frvcdField = fields.FirstOrDefault(
                    field => string.Equals(
                        field.Name,
                        "FRVCD",
                        StringComparison.OrdinalIgnoreCase));

                if (rvcdField == null || frvcdField == null)
                    throw new InvalidDataException(
                        "rivl.dbf 中找不到 RVCD / FRVCD 字段");

                stream.Seek(headerLength, SeekOrigin.Begin);

                for (uint index = 0; index < recordCount; index++)
                {
                    byte[] row = reader.ReadBytes(recordLength);
                    if (row.Length != recordLength)
                        break;

                    if (row[0] == (byte)'*')
                        continue;

                    string rvcd =
                        ReadDbfText(row, rvcdField).Trim();
                    string frvcd =
                        ReadDbfText(row, frvcdField).Trim();

                    if (rvcd.Length == 0 && frvcd.Length == 0)
                        continue;

                    result.Add(new DbfRecord(rvcd, frvcd));
                }
            }

            return result;
        }

        private static string ReadDbfText(
            byte[] row,
            DbfField field)
        {
            if (field.Offset < 0 ||
                field.Length <= 0 ||
                field.Offset + field.Length > row.Length)
                return "";

            // RVCD / FRVCD 是编码字段，内容为 ASCII 字符。
            return Encoding.ASCII.GetString(
                row,
                field.Offset,
                field.Length).Trim('\0', ' ');
        }

        private static IEnumerable<string> SplitTopologyCodes(
            string value)
        {
            return (value ?? "")
                .Split(
                    new[] { ',', ';' },
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(code => code.Trim())
                .Where(code => code.Length != 0);
        }

        private void RefreshInputTable()
        {
            if (!Directory.Exists(txtUnitRoot.Text.Trim()))
                return;

            // Preserve the user's current unit selection.
            // ScanUnits() rebuilds the ComboBox and defaults it to --all,
            // so without this the selected single unit would silently become --all
            // immediately before BuildArguments().
            string selectedUnit =
                cmbUnit.SelectedIndex > 0
                ? Convert.ToString(cmbUnit.SelectedItem)
                : "";

            bool selectedAll = cmbUnit.SelectedIndex <= 0;

            ScanUnits();

            if (selectedAll)
            {
                cmbUnit.SelectedIndex = 0;
                return;
            }

            for (int index = 1; index < cmbUnit.Items.Count; index++)
            {
                if (string.Equals(
                    Convert.ToString(cmbUnit.Items[index]),
                    selectedUnit,
                    StringComparison.OrdinalIgnoreCase))
                {
                    cmbUnit.SelectedIndex = index;
                    return;
                }
            }

            throw new InvalidOperationException(
                "The selected calculation unit no longer exists after refreshing: " +
                selectedUnit);
        }

        private void CmbUnit_SelectedIndexChanged(
            object sender,
            EventArgs e)
        {
            if (cmbUnit.SelectedIndex <= 0)
            {
                if (gridUnits != null)
                    gridUnits.ClearSelection();
                return;
            }

            string unit = Convert.ToString(cmbUnit.SelectedItem);
            SelectGridUnit(unit);
        }

        private void GridUnits_CellDoubleClick(
            object sender,
            DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
                return;

            string unit =
                Convert.ToString(gridUnits.Rows[e.RowIndex].Cells["Unit"].Value);

            for (int index = 1; index < cmbUnit.Items.Count; index++)
            {
                if (string.Equals(
                    Convert.ToString(cmbUnit.Items[index]),
                    unit,
                    StringComparison.OrdinalIgnoreCase))
                {
                    cmbUnit.SelectedIndex = index;
                    break;
                }
            }
        }

        private void SelectGridUnit(string unit)
        {
            int rowIndex;
            if (!_unitRows.TryGetValue(unit, out rowIndex))
                return;

            gridUnits.ClearSelection();
            DataGridViewRow row = gridUnits.Rows[rowIndex];
            row.Selected = true;

            if (row.Cells.Count > 0)
                gridUnits.CurrentCell = row.Cells[0];
        }

        private void BtnRun_Click(object sender, EventArgs e)
        {
            string error;
            if (!ValidateInputs(out error))
            {
                MessageBox.Show(
                    this,
                    error,
                    "参数检查",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            RefreshInputTable();

            string exe = Path.GetFullPath(txtExe.Text.Trim());
            string workingDirectory = Path.GetDirectoryName(exe);

            if (workingDirectory.IndexOf(' ') >= 0)
            {
                DialogResult answer = MessageBox.Show(
                    this,
                    "模型程序所在目录包含空格。旧版 FFMSV4 的模型运行器可能无法正确处理含空格路径。\r\n\r\n仍要继续吗？",
                    "路径警告",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (answer != DialogResult.Yes)
                    return;
            }

            PrepareRunTracking();

            string arguments = BuildArguments();

            AppendLog("GUI_COMMAND " + Quote(exe) + " " + arguments);
            AppendLog("");

            var startInfo = new ProcessStartInfo();
            startInfo.FileName = exe;
            startInfo.Arguments = arguments;
            startInfo.WorkingDirectory = workingDirectory;
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.CreateNoWindow = true;

            _process = new Process();
            _process.StartInfo = startInfo;
            _process.EnableRaisingEvents = true;
            _process.OutputDataReceived += Process_OutputDataReceived;
            _process.ErrorDataReceived += Process_ErrorDataReceived;
            _process.Exited += Process_Exited;

            try
            {
                SetRunning(true);
                lblStatus.Text = "运行中...";

                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
            }
            catch (Exception exception)
            {
                SetRunning(false);
                lblStatus.Text = "启动失败";
                MessageBox.Show(
                    this,
                    exception.Message,
                    "无法启动核心程序",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void PrepareRunTracking()
        {
            _completedUnits.Clear();
            _unitsWithSourceLog.Clear();
            _currentUnit = "";

            int selectedCount =
                cmbUnit.SelectedIndex <= 0 ? gridUnits.Rows.Count : 1;

            ResetProgress(selectedCount);

            foreach (DataGridViewRow row in gridUnits.Rows)
            {
                string unit =
                    Convert.ToString(row.Cells["Unit"].Value);

                bool selected =
                    cmbUnit.SelectedIndex <= 0 ||
                    string.Equals(
                        unit,
                        Convert.ToString(cmbUnit.SelectedItem),
                        StringComparison.OrdinalIgnoreCase);

                if (!selected)
                    continue;

                row.Cells["Coverage"].Value = "—";

                string status =
                    Convert.ToString(row.Cells["Status"].Value);

                if (status != "输入缺失")
                    SetUnitStatus(unit, "等待预检", "");
            }

            lblCurrentUnit.Text = "当前单元：—";
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            Process process = _process;
            if (process == null)
                return;

            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    AppendLog("GUI_CANCELLED");
                    lblStatus.Text = "已停止";

                    if (_currentUnit.Length > 0)
                        SetUnitStatus(
                            _currentUnit,
                            "已停止",
                            "用户终止运行");
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    "停止失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void BtnOpenOutput_Click(object sender, EventArgs e)
        {
            string exe = txtExe.Text.Trim();

            if (!File.Exists(exe))
            {
                MessageBox.Show(
                    this,
                    "请先选择有效的 UnifiedHydroWorkflow.exe。",
                    "打开输出目录",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            string modelRoot = Path.GetDirectoryName(
                Path.GetFullPath(exe));
            string outputRoot =
                Path.Combine(modelRoot, "SKBYEXE", "output");

            string unit = GetPreferredOutputUnit();
            string target = outputRoot;

            if (!string.IsNullOrWhiteSpace(unit))
            {
                string outFolder =
                    Path.Combine(outputRoot, unit, "out");
                string unitFolder =
                    Path.Combine(outputRoot, unit);

                if (Directory.Exists(outFolder))
                    target = outFolder;
                else if (Directory.Exists(unitFolder))
                    target = unitFolder;
            }

            if (!Directory.Exists(target))
            {
                MessageBox.Show(
                    this,
                    "输出目录尚不存在：\r\n" + target,
                    "打开输出目录",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            try
            {
                Process.Start("explorer.exe", Quote(target));
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    "无法打开输出目录",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private string GetPreferredOutputUnit()
        {
            if (gridUnits.SelectedRows.Count > 0)
            {
                string unit = Convert.ToString(
                    gridUnits.SelectedRows[0].Cells["Unit"].Value);

                if (!string.IsNullOrWhiteSpace(unit))
                    return unit;
            }

            if (cmbUnit.SelectedIndex > 0)
                return Convert.ToString(cmbUnit.SelectedItem);

            if (!string.IsNullOrWhiteSpace(_currentUnit))
                return _currentUnit;

            return "";
        }

        private void Process_OutputDataReceived(
            object sender,
            DataReceivedEventArgs e)
        {
            if (e.Data != null)
                HandleProcessLine(e.Data);
        }

        private void Process_ErrorDataReceived(
            object sender,
            DataReceivedEventArgs e)
        {
            if (e.Data != null)
                HandleProcessLine(e.Data);
        }

        private void HandleProcessLine(string line)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(HandleProcessLine), line);
                return;
            }

            AppendLog(line);
            UpdateRunStateFromLine(line);
        }

        private void UpdateRunStateFromLine(string line)
        {
            if (line.StartsWith("PREFLIGHT_BEGIN total="))
            {
                int total;
                if (TryParseIntValue(line, "total=", out total))
                    ResetProgress(total);

                lblStatus.Text = "正在预检...";
                return;
            }

            if (line.StartsWith("INPUT_FILES unit="))
            {
                string unit = ExtractValue(line, "unit=");
                if (unit.Length > 0)
                    SetUnitStatus(unit, "预检中", "");
                return;
            }

            if (line.StartsWith("COVERAGE unit="))
            {
                string unit = ExtractValue(line, "unit=");
                string landText = ExtractValue(line, "land_min=");
                string soilText = ExtractValue(line, "soil_min=");

                double land;
                double soil;
                if (double.TryParse(
                        landText,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out land) &&
                    double.TryParse(
                        soilText,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out soil))
                {
                    SetCoverage(
                        unit,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "L {0:F2}% / S {1:F2}%",
                            land * 100.0,
                            soil * 100.0));
                }

                SetUnitStatus(unit, "预检通过", "");
                return;
            }

            if (line.StartsWith("SKIPPED_COVERAGE unit="))
            {
                string unit = ExtractValue(line, "unit=");
                SetUnitStatus(unit, "覆盖不足", "低于核心程序覆盖率阈值");
                return;
            }

            if (line.StartsWith("PREFLIGHT_FAILED unit="))
            {
                string unit = ExtractValue(line, "unit=");
                SetUnitStatus(unit, "预检失败", ExtractMessageAfterColon(line));
                return;
            }

            if (line.StartsWith("PREFLIGHT_SUMMARY "))
            {
                int selected;
                int valid;
                int skipped;

                TryParseIntValue(line, "selected=", out selected);
                TryParseIntValue(line, "valid=", out valid);
                TryParseIntValue(line, "skipped_coverage=", out skipped);

                if (cmbMode.SelectedIndex == 2)
                {
                    _expectedProgressUnits = Math.Max(1, selected);
                    progressUnits.Maximum = Math.Max(1, selected);
                    progressUnits.Value =
                        Math.Min(progressUnits.Maximum, Math.Max(0, selected));
                    lblProgress.Text =
                        "进度：" + selected + " / " + selected;

                    foreach (DataGridViewRow row in TargetRows())
                    {
                        string status =
                            Convert.ToString(row.Cells["Status"].Value);

                        if (status == "预检中" ||
                            status == "预检通过" ||
                            status == "等待预检")
                        {
                            string unit =
                                Convert.ToString(row.Cells["Unit"].Value);
                            SetUnitStatus(unit, "预检通过", "");
                        }
                    }
                }
                else
                {
                    _completedUnits.Clear();
                    _expectedProgressUnits = Math.Max(1, valid);
                    progressUnits.Maximum = Math.Max(1, valid);
                    progressUnits.Value = 0;
                    lblProgress.Text = "进度：0 / " + valid;

                    foreach (DataGridViewRow row in TargetRows())
                    {
                        string status =
                            Convert.ToString(row.Cells["Status"].Value);

                        if (status == "预检通过" ||
                            status == "预检中")
                        {
                            string unit =
                                Convert.ToString(row.Cells["Unit"].Value);
                            SetUnitStatus(unit, "等待运行", "");
                        }
                    }
                }

                lblStatus.Text =
                    cmbMode.SelectedIndex == 2
                    ? "预检完成"
                    : "预检完成，准备运行";
                return;
            }

            if (line.StartsWith("BEGIN unit="))
            {
                string unit = ExtractValue(line, "unit=");
                _currentUnit = unit;
                lblCurrentUnit.Text = "当前单元：" + unit;
                SetUnitStatus(unit, "运行中", "");
                lblStatus.Text = "正在处理 " + unit;
                return;
            }

            if (line.StartsWith("SOURCE_FILE_VALID "))
            {
                MarkCurrentUnitHasSource();
                SetSourceStatus(_currentUnit, "已有/有效");
                return;
            }

            if (line.StartsWith("SOURCE_FILE_CREATED "))
            {
                MarkCurrentUnitHasSource();
                SetSourceStatus(_currentUnit, "已生成");
                return;
            }

            if (line.StartsWith("SOURCE_FILE_INVALID "))
            {
                MarkCurrentUnitHasSource();
                SetSourceStatus(_currentUnit, "异常");
                return;
            }

            if (line.StartsWith("SOURCE_FILE_RECREATED "))
            {
                MarkCurrentUnitHasSource();
                SetSourceStatus(_currentUnit, "已重建");
                return;
            }

            if (line.StartsWith("PREPARED unit="))
            {
                string unit = ExtractValue(line, "unit=");

                if (!_unitsWithSourceLog.Contains(unit))
                    SetSourceStatus(unit, "无需外部水源");

                if (cmbMode.SelectedIndex == 1)
                {
                    SetUnitStatus(unit, "准备完成", "");
                    MarkUnitCompleted(unit);
                }
                else
                {
                    SetUnitStatus(unit, "已准备", "");
                }

                return;
            }

            if (line.StartsWith("CALCULATED unit="))
            {
                string unit = ExtractValue(line, "unit=");

                if (!_unitsWithSourceLog.Contains(unit))
                    SetSourceStatus(unit, "无需外部水源");

                SetUnitStatus(unit, "成功", "");
                MarkUnitCompleted(unit);
                return;
            }

            if (line.StartsWith("FAILED unit="))
            {
                string unit = ExtractValue(line, "unit=");

                if (_unitsWithSourceLog.Contains(unit))
                {
                    string sourceStatus = GetSourceStatus(unit);
                    if (sourceStatus == "已有" ||
                        sourceStatus == "未判定" ||
                        sourceStatus == "异常")
                        SetSourceStatus(unit, "异常");
                }

                SetUnitStatus(unit, "失败", "详见运行日志");
                MarkUnitCompleted(unit);
                return;
            }

            if (line.StartsWith("FINAL_SUMMARY "))
            {
                int attempted;
                int success;
                int failed;

                TryParseIntValue(line, "attempted=", out attempted);
                TryParseIntValue(line, "success=", out success);
                TryParseIntValue(line, "failed=", out failed);

                if (attempted > 0)
                {
                    _expectedProgressUnits = attempted;
                    progressUnits.Maximum = Math.Max(1, attempted);
                    progressUnits.Value =
                        Math.Min(progressUnits.Maximum, attempted);
                    lblProgress.Text =
                        "进度：" + attempted + " / " + attempted;
                }

                lblStatus.Text = string.Format(
                    "完成：成功 {0}，失败 {1}",
                    success,
                    failed);
                return;
            }
        }

        private IEnumerable<DataGridViewRow> TargetRows()
        {
            string selectedUnit =
                cmbUnit.SelectedIndex > 0
                ? Convert.ToString(cmbUnit.SelectedItem)
                : "";

            foreach (DataGridViewRow row in gridUnits.Rows)
            {
                string unit =
                    Convert.ToString(row.Cells["Unit"].Value);

                if (selectedUnit.Length == 0 ||
                    string.Equals(
                        unit,
                        selectedUnit,
                        StringComparison.OrdinalIgnoreCase))
                    yield return row;
            }
        }

        private void MarkUnitCompleted(string unit)
        {
            if (string.IsNullOrWhiteSpace(unit))
                return;

            if (!_completedUnits.Add(unit))
                return;

            int total =
                _expectedProgressUnits > 0
                ? _expectedProgressUnits
                : progressUnits.Maximum;

            int completed = _completedUnits.Count;

            progressUnits.Maximum = Math.Max(1, total);
            progressUnits.Value =
                Math.Min(progressUnits.Maximum, completed);

            lblProgress.Text =
                "进度：" + completed + " / " + total;
        }

        private void MarkCurrentUnitHasSource()
        {
            if (!string.IsNullOrWhiteSpace(_currentUnit))
                _unitsWithSourceLog.Add(_currentUnit);
        }

        private void SetSourceStatus(string unit, string status)
        {
            if (string.IsNullOrWhiteSpace(unit))
                return;

            int rowIndex;
            if (!_unitRows.TryGetValue(unit, out rowIndex))
                return;

            gridUnits.Rows[rowIndex]
                .Cells["Source"].Value = status;
        }

        private string GetSourceStatus(string unit)
        {
            int rowIndex;
            if (!_unitRows.TryGetValue(unit, out rowIndex))
                return "";

            return Convert.ToString(
                gridUnits.Rows[rowIndex].Cells["Source"].Value);
        }

        private void SetCoverage(string unit, string coverage)
        {
            int rowIndex;
            if (!_unitRows.TryGetValue(unit, out rowIndex))
                return;

            gridUnits.Rows[rowIndex]
                .Cells["Coverage"].Value = coverage;
        }

        private void SetUnitStatus(
            string unit,
            string status,
            string note)
        {
            if (string.IsNullOrWhiteSpace(unit))
                return;

            int rowIndex;
            if (!_unitRows.TryGetValue(unit, out rowIndex))
                return;

            DataGridViewRow row = gridUnits.Rows[rowIndex];
            row.Cells["Status"].Value = status;

            if (!string.IsNullOrWhiteSpace(note))
                row.Cells["Note"].Value = note;

            ApplyStatusStyle(row, status);
        }

        private static void ApplyStatusStyle(
            DataGridViewRow row,
            string status)
        {
            Color background = SystemColors.Window;

            if (status == "成功" ||
                status == "准备完成" ||
                status == "预检通过")
                background = Color.Honeydew;
            else if (status == "失败" ||
                     status == "预检失败" ||
                     status == "输入缺失")
                background = Color.MistyRose;
            else if (status == "运行中" ||
                     status == "预检中" ||
                     status == "已准备")
                background = Color.LightYellow;
            else if (status == "覆盖不足" ||
                     status == "已停止")
                background = Color.PeachPuff;

            row.DefaultCellStyle.BackColor = background;
        }

        private void Process_Exited(object sender, EventArgs e)
        {
            int exitCode = -1;
            try
            {
                exitCode = ((Process)sender).ExitCode;
            }
            catch
            {
            }

            BeginInvoke(new Action(delegate
            {
                AppendLog("");
                AppendLog("GUI_EXIT_CODE " + exitCode);

                SetRunning(false);

                if (exitCode == 0)
                {
                    if (cmbMode.SelectedIndex == 2)
                        lblStatus.Text = "预检完成";
                    else if (!lblStatus.Text.StartsWith("完成："))
                        lblStatus.Text = "运行完成";
                }
                else
                {
                    lblStatus.Text =
                        "运行结束，退出码 " + exitCode;
                }
            }));
        }

        private bool ValidateInputs(out string error)
        {
            error = "";

            if (!File.Exists(txtExe.Text.Trim()))
            {
                error = "找不到 UnifiedHydroWorkflow.exe。";
                return false;
            }

            if (!Directory.Exists(txtUnitRoot.Text.Trim()))
            {
                error = "计算单元根目录不存在。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(txtProject.Text))
            {
                error = "工程名称不能为空。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(txtStation.Text))
            {
                error = "站点文件名不能为空。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(txtRainfall.Text))
            {
                error = "降雨文件名不能为空。";
                return false;
            }

            if (Path.GetFileName(txtStation.Text) != txtStation.Text.Trim())
            {
                error = "站点文件请输入文件名，不要输入完整路径。";
                return false;
            }

            if (Path.GetFileName(txtRainfall.Text) != txtRainfall.Text.Trim())
            {
                error = "降雨文件请输入文件名，不要输入完整路径。";
                return false;
            }

            if (dtEnd.Value <= dtStart.Value)
            {
                error = "场次结束时间必须晚于开始时间。";
                return false;
            }

            if (dtSourceStart.Value < dtStart.Value ||
                dtSourceStart.Value > dtEnd.Value)
            {
                error = "上游水源开始时间必须位于洪水场次时间范围内。";
                return false;
            }

            double eventMinutes =
                (dtEnd.Value - dtStart.Value).TotalMinutes;
            double sourceMinutes =
                (dtEnd.Value - dtSourceStart.Value).TotalMinutes;
            int interval = Decimal.ToInt32(numInterval.Value);

            if (eventMinutes % interval != 0)
            {
                error = "洪水场次总时长必须能被时间步长整除。";
                return false;
            }

            if (sourceMinutes % interval != 0)
            {
                error =
                    "上游水源开始至场次结束的时长必须能被时间步长整除。";
                return false;
            }

            return true;
        }

        private string BuildArguments()
        {
            var builder = new StringBuilder();

            builder.Append(
                Quote(Path.GetFullPath(txtUnitRoot.Text.Trim())));
            builder.Append(" ");
            builder.Append(Quote(txtProject.Text.Trim()));
            builder.Append(" ");

            if (cmbUnit.SelectedIndex <= 0)
                builder.Append("--all");
            else
                builder.Append(
                    Quote(Convert.ToString(cmbUnit.SelectedItem)));

            if (cmbMode.SelectedIndex == 1)
                builder.Append(" --prepare-only");
            else if (cmbMode.SelectedIndex == 2)
                builder.Append(" --preflight-only");

            builder.Append(" --station-file ");
            builder.Append(Quote(txtStation.Text.Trim()));

            builder.Append(" --rainfall-file ");
            builder.Append(Quote(txtRainfall.Text.Trim()));

            builder.Append(" --start ");
            builder.Append(Quote(FormatTime(dtStart.Value)));

            builder.Append(" --end ");
            builder.Append(Quote(FormatTime(dtEnd.Value)));

            builder.Append(" --interval ");
            builder.Append(
                Decimal.ToInt32(numInterval.Value)
                    .ToString(CultureInfo.InvariantCulture));

            builder.Append(" --source-start ");
            builder.Append(
                Quote(FormatTime(dtSourceStart.Value)));

            return builder.ToString();
        }

        private static string FormatTime(DateTime value)
        {
            return value.ToString(
                "yyyy/M/d H:mm:ss",
                CultureInfo.InvariantCulture);
        }

        private static string Quote(string value)
        {
            if (value == null)
                return "\"\"";

            return "\"" +
                   value.Replace("\"", "\\\"") +
                   "\"";
        }

        private static string ExtractValue(
            string line,
            string key)
        {
            int start = line.IndexOf(
                key,
                StringComparison.Ordinal);

            if (start < 0)
                return "";

            start += key.Length;
            int end = line.IndexOf(' ', start);

            string value =
                end < 0
                ? line.Substring(start)
                : line.Substring(start, end - start);

            return value.Trim().TrimEnd(':', ',', ';');
        }

        private static string ExtractMessageAfterColon(string line)
        {
            int colon = line.IndexOf(": ", StringComparison.Ordinal);
            if (colon < 0)
                return "";

            return line.Substring(colon + 2).Trim();
        }

        private static bool TryParseIntValue(
            string line,
            string key,
            out int value)
        {
            return int.TryParse(
                ExtractValue(line, key),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value);
        }

        private sealed class UnitRowState
        {
            public readonly string Source;
            public readonly string Coverage;
            public readonly string Status;
            public readonly string Note;

            public UnitRowState(
                string source,
                string coverage,
                string status,
                string note)
            {
                Source = source ?? "";
                Coverage = coverage ?? "";
                Status = status ?? "";
                Note = note ?? "";
            }
        }

        private sealed class DbfField
        {
            public readonly string Name;
            public readonly int Offset;
            public readonly int Length;

            public DbfField(
                string name,
                int offset,
                int length)
            {
                Name = name;
                Offset = offset;
                Length = length;
            }
        }

        private sealed class DbfRecord
        {
            public readonly string Rvcd;
            public readonly string Frvcd;

            public DbfRecord(
                string rvcd,
                string frvcd)
            {
                Rvcd = rvcd;
                Frvcd = frvcd;
            }
        }

        private sealed class ExternalSourceScan
        {
            public readonly bool Determined;
            public readonly bool RequiresExternalSource;
            public readonly int SourceSegmentCount;
            public readonly int ExternalUpstreamCodeCount;
            public readonly string Error;

            private ExternalSourceScan(
                bool determined,
                bool requiresExternalSource,
                int sourceSegmentCount,
                int externalUpstreamCodeCount,
                string error)
            {
                Determined = determined;
                RequiresExternalSource = requiresExternalSource;
                SourceSegmentCount = sourceSegmentCount;
                ExternalUpstreamCodeCount =
                    externalUpstreamCodeCount;
                Error = error ?? "";
            }

            public static ExternalSourceScan Known(
                bool requiresExternalSource,
                int sourceSegmentCount,
                int externalUpstreamCodeCount)
            {
                return new ExternalSourceScan(
                    true,
                    requiresExternalSource,
                    sourceSegmentCount,
                    externalUpstreamCodeCount,
                    "");
            }

            public static ExternalSourceScan Unknown(string error)
            {
                return new ExternalSourceScan(
                    false,
                    false,
                    0,
                    0,
                    error);
            }
        }

        private static int NaturalUnitNumber(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return int.MaxValue;

            int underscore = name.LastIndexOf('_');
            if (underscore < 0 || underscore == name.Length - 1)
                return int.MaxValue;

            string tail = name.Substring(underscore + 1).Trim();

            if (tail.StartsWith(
                "r",
                StringComparison.OrdinalIgnoreCase))
                tail = tail.Substring(1);

            int number;
            return int.TryParse(tail, out number)
                ? number
                : int.MaxValue;
        }

        private void ResetProgress(int total)
        {
            _expectedProgressUnits = Math.Max(0, total);
            progressUnits.Minimum = 0;
            progressUnits.Maximum = Math.Max(1, total);
            progressUnits.Value = 0;
            lblProgress.Text = "进度：0 / " + total;
            lblCurrentUnit.Text = "当前单元：—";
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

        private void SetRunning(bool running)
        {
            btnRun.Enabled = !running;
            btnCancel.Enabled = running;

            btnBrowseExe.Enabled = !running;
            btnBrowseRoot.Enabled = !running;
            btnScan.Enabled = !running;

            txtExe.Enabled = !running;
            txtUnitRoot.Enabled = !running;
            txtProject.Enabled = !running;
            cmbUnit.Enabled = !running;
            txtStation.Enabled = !running;
            txtRainfall.Enabled = !running;
            dtStart.Enabled = !running;
            dtEnd.Enabled = !running;
            dtSourceStart.Enabled = !running;
            numInterval.Enabled = !running;
            cmbMode.Enabled = !running;
        }
    }
}
