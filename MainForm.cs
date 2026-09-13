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
    public sealed partial class MainForm : Form
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
        private ComboBox cmbInterval;
        private ComboBox cmbMode;

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
        private Label lblModelEnvironment;
        private SplitContainer resultsSplit;

        private Process _process;
        private DeploymentLayout _deployment;
        private WorkflowRuntimeStager _workflowRuntimeStager;
        private string _currentUnit = "";
        private int _expectedProgressUnits = 0;
        private readonly HashSet<string> _completedUnits =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _unitsWithSourceLog =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _unitRows =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private string _tableStateContextKey = "";
        private bool _scenarioMismatchDetected;
        private string _successfulPreflightFingerprint = "";
        private string _currentRunFingerprint = "";
        private bool _preflightSummaryAllValid;
        private bool _reusePreflightForCurrentRun;
        private string _databaseBackupPath = "";
        private readonly Dictionary<string, UnitRowState> _successfulPreflightUnitStates =
            new Dictionary<string, UnitRowState>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _unitSourceValidFiles =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _unitSourceGeneratedFiles =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public MainForm()
        {
            Text = AppInfo.WindowTitle;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            MinimumSize = new Size(1180, 860);
            Size = new Size(1360, 960);
            Font = new Font("Microsoft YaHei UI", 9F);

            BuildUi();
            LoadDefaults();
            txtUnitRoot.TextChanged += EnvironmentPathChanged;
            CheckEnvironment();
        }

        private void BuildUi()
        {
            var shell = new TableLayoutPanel();
            shell.Dock = DockStyle.Fill;
            shell.ColumnCount = 1;
            shell.RowCount = 2;
            shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Controls.Add(shell);

            shell.Controls.Add(BuildMainMenu(), 0, 0);

            var rootPanel = new TableLayoutPanel();
            rootPanel.Dock = DockStyle.Fill;
            rootPanel.ColumnCount = 1;
            rootPanel.RowCount = 6;
            rootPanel.Padding = new Padding(12);
            rootPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rootPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rootPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            rootPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            rootPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            rootPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            shell.Controls.Add(rootPanel, 0, 1);

            BuildSettings(rootPanel);
            BuildEventSettings(rootPanel);
            BuildEnvironmentPanel(rootPanel);
            BuildActionAndProgress(rootPanel);
            BuildResultsSplit(rootPanel);

            lblStatus = new Label();
            lblStatus.Dock = DockStyle.Fill;
            lblStatus.Text = "就绪";
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            rootPanel.Controls.Add(lblStatus, 0, 5);

            Shown += delegate
            {
                if (resultsSplit != null && resultsSplit.Height > 300)
                    resultsSplit.SplitterDistance = (int)(resultsSplit.Height * 0.60);
            };
        }

        private void BuildSettings(TableLayoutPanel rootPanel)
        {
            var settingsGroup = new GroupBox();
            settingsGroup.Text = "数据与工程";
            settingsGroup.Dock = DockStyle.Top;
            settingsGroup.AutoSize = true;
            settingsGroup.Padding = new Padding(10, 10, 10, 8);
            rootPanel.Controls.Add(settingsGroup, 0, 0);

            var settings = new TableLayoutPanel();
            settings.Dock = DockStyle.Top;
            settings.AutoSize = true;
            settings.ColumnCount = 5;
            settings.RowCount = 9;
            settings.Padding = new Padding(4);
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140F));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115F));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250F));
            settingsGroup.Controls.Add(settings);

            for (int index = 0; index < 6; index++)
                settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 0F));
            settings.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));

            int row = 0;

            // Keep the workflow path internally for launching, but show users the
            // detected original-model environment instead of another long path.
            txtExe = NewTextBox();
            txtExe.ReadOnly = true;
            txtExe.TabStop = false;
            txtExe.Visible = false;

            AddLabel(settings, "模型环境", row);
            lblModelEnvironment = new Label();
            lblModelEnvironment.Dock = DockStyle.Fill;
            lblModelEnvironment.TextAlign = ContentAlignment.MiddleLeft;
            lblModelEnvironment.AutoEllipsis = true;
            lblModelEnvironment.Text = "○ 待识别";
            settings.Controls.Add(lblModelEnvironment, 1, row);
            settings.SetColumnSpan(lblModelEnvironment, 4);
            row++;

            AddLabel(settings, "计算单元根目录", row);
            txtUnitRoot = NewTextBox();
            settings.Controls.Add(txtUnitRoot, 1, row);
            settings.SetColumnSpan(txtUnitRoot, 3);

            var rootButtons = new TableLayoutPanel();
            rootButtons.Dock = DockStyle.Fill;
            rootButtons.ColumnCount = 2;
            rootButtons.RowCount = 1;
            rootButtons.Margin = new Padding(0);
            rootButtons.Padding = new Padding(0);
            rootButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
            rootButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            rootButtons.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            btnBrowseRoot = NewButton("浏览...");
            btnBrowseRoot.Margin = new Padding(3);
            btnBrowseRoot.Click += BtnBrowseRoot_Click;
            rootButtons.Controls.Add(btnBrowseRoot, 0, 0);

            btnPreprocess = NewButton("数据预处理...");
            btnPreprocess.Margin = new Padding(3);
            btnPreprocess.Click += BtnPreprocess_Click;
            rootButtons.Controls.Add(btnPreprocess, 1, 0);

            settings.Controls.Add(rootButtons, 4, row++);

            BuildInputModeRow(settings, row++);

            AddLabel(settings, "工程名称", row);
            txtProject = NewTextBox();
            txtProject.TextChanged += delegate { UpdateRunButtonAvailability(); };
            settings.Controls.Add(txtProject, 1, row);

            AddSmallLabel(settings, "运行模式", 2, row);
            cmbMode = new ComboBox();
            cmbMode.Dock = DockStyle.Fill;
            cmbMode.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbMode.Items.AddRange(new object[] { "计算", "仅准备", "仅预检" });
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
            // Unit names are short; keep the selector compact instead of stretching
            // across the whole window.  This also leaves the right half visually calm.
            row++;

            BuildStationRainfallModeRow(settings, row++);
            BuildUnifiedFileNameRow(settings, row++);
            BuildExternalSourcesSection(settings, row++);

            var tips = new Label();
            tips.AutoSize = true;
            tips.Text = "说明：计算单元内数据优先，外部目录仅用于缺失数据补充。";
            tips.ForeColor = Color.DimGray;
            tips.Anchor = AnchorStyles.Left;
            settings.Controls.Add(tips, 1, row);
            settings.SetColumnSpan(tips, 4);
        }

        private void BuildEventSettings(TableLayoutPanel rootPanel)
        {
            var group = new GroupBox();
            group.Text = "洪水场次";
            group.Dock = DockStyle.Top;
            group.AutoSize = true;
            group.Padding = new Padding(10, 8, 10, 6);
            rootPanel.Controls.Add(group, 0, 1);

            var panel = new TableLayoutPanel();
            panel.Dock = DockStyle.Top;
            panel.AutoSize = true;
            panel.ColumnCount = 4;
            panel.RowCount = 2;
            panel.Padding = new Padding(4);
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            group.Controls.Add(panel);

            AddLabel(panel, "场次开始", 0);
            panel.Controls.Add(NewDateTimeEditor(out dtStart), 1, 0);
            AddSmallLabel(panel, "场次结束", 2, 0);
            panel.Controls.Add(NewDateTimeEditor(out dtEnd), 3, 0);

            AddLabel(panel, "上游水源开始", 1);
            panel.Controls.Add(NewDateTimeEditor(out dtSourceStart), 1, 1);
            AddSmallLabel(panel, "计算时间间隔", 2, 1);
            cmbInterval = new ComboBox();
            cmbInterval.Dock = DockStyle.Fill;
            cmbInterval.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbInterval.Margin = new Padding(3, 4, 3, 4);
            AddIntervalOption("5分钟", 5);
            AddIntervalOption("10分钟", 10);
            AddIntervalOption("15分钟", 15);
            AddIntervalOption("20分钟", 20);
            AddIntervalOption("30分钟", 30);
            AddIntervalOption("1小时", 60);
            AddIntervalOption("2小时", 120);
            AddIntervalOption("3小时", 180);
            AddIntervalOption("4小时", 240);
            AddIntervalOption("6小时", 360);
            AddIntervalOption("8小时", 480);
            AddIntervalOption("10小时", 600);
            AddIntervalOption("12小时", 720);
            AddIntervalOption("1天", 1440);
            panel.Controls.Add(cmbInterval, 3, 1);
        }

        private void BuildActionAndProgress(TableLayoutPanel rootPanel)
        {
            var actionRow = new TableLayoutPanel();
            actionRow.Dock = DockStyle.Fill;
            actionRow.ColumnCount = 2;
            actionRow.RowCount = 1;
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            rootPanel.Controls.Add(actionRow, 0, 3);

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

        private void BuildResultsSplit(TableLayoutPanel rootPanel)
        {
            resultsSplit = new SplitContainer();
            resultsSplit.Dock = DockStyle.Fill;
            resultsSplit.Orientation = Orientation.Horizontal;
            resultsSplit.Panel1MinSize = 150;
            resultsSplit.Panel2MinSize = 100;
            resultsSplit.SplitterWidth = 6;
            rootPanel.Controls.Add(resultsSplit, 0, 4);

            var unitGroup = new GroupBox();
            unitGroup.Text = "计算单元状态";
            unitGroup.Dock = DockStyle.Fill;
            unitGroup.Padding = new Padding(8);
            resultsSplit.Panel1.Controls.Add(unitGroup);

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
            AddGridColumn("Station", "站点", 95);
            AddGridColumn("Rainfall", "降雨", 95);
            AddGridColumn("Source", "上游水源表", 125);
            AddGridColumn("Coverage", "覆盖率", 160);
            AddGridColumn("Status", "状态", 130);
            AddGridColumn("Note", "说明", 220);
            unitGroup.Controls.Add(gridUnits);

            var logGroup = new GroupBox();
            logGroup.Text = "运行日志";
            logGroup.Dock = DockStyle.Fill;
            logGroup.Padding = new Padding(8);
            resultsSplit.Panel2.Controls.Add(logGroup);

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
            picker.Margin = new Padding(0);
            return picker;
        }

        private Control NewDateTimeEditor(out DateTimePicker picker)
        {
            var host = new TableLayoutPanel();
            host.Dock = DockStyle.Fill;
            host.ColumnCount = 2;
            host.RowCount = 1;
            host.Margin = new Padding(3, 4, 3, 4);
            host.Padding = new Padding(0);
            host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            host.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34F));
            host.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            DateTimePicker localPicker = NewDateTimePicker();
            picker = localPicker;
            host.Controls.Add(localPicker, 0, 0);

            var select = new Button();
            select.Text = "▼";
            select.Dock = DockStyle.Fill;
            select.Margin = new Padding(2, 0, 0, 0);
            select.Tag = localPicker;
            select.Click += OpenDateTimeSelector_Click;
            select.Enabled = localPicker.Enabled;
            localPicker.EnabledChanged += delegate { select.Enabled = localPicker.Enabled; };
            host.Controls.Add(select, 1, 0);
            return host;
        }

        private void OpenDateTimeSelector_Click(object sender, EventArgs e)
        {
            Button button = sender as Button;
            DateTimePicker picker = button == null ? null : button.Tag as DateTimePicker;
            if (picker == null) return;

            using (var dialog = new DateTimeSelectionForm(picker.Value))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    picker.Value = dialog.SelectedDateTime;
            }
        }

        private static void AddLabel(
            TableLayoutPanel panel,
            string text,
            int row)
        {
            var label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.AutoEllipsis = true;
            label.Margin = new Padding(3, 0, 3, 0);
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
            label.AutoSize = false;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.AutoEllipsis = true;
            label.Margin = new Padding(3, 0, 3, 0);
            panel.Controls.Add(label, column, row);
        }

        private void LoadDefaults()
        {
            _deployment = DeploymentLayout.Detect(AppDomain.CurrentDomain.BaseDirectory);
            txtExe.Text = _deployment.WorkflowSourcePath;

            txtStation.Text = "站点信息.xlsx";
            txtRainfall.Text = "20060803111降雨.xlsx";
            if (txtStationExternalRoot != null) txtStationExternalRoot.Text = "";
            if (txtRainfallExternalRoot != null) txtRainfallExternalRoot.Text = "";
            if (txtSourceExternalRoot != null) txtSourceExternalRoot.Text = "";

            _buildingInputUi = true;
            cmbInputMode.SelectedIndex = 0;
            cmbStationMode.SelectedIndex = 0;
            cmbRainfallMode.SelectedIndex = 0;
            _buildingInputUi = false;
            UpdateInputControlState();

            dtStart.Value = new DateTime(2006, 8, 1, 1, 0, 0);
            dtEnd.Value = new DateTime(2006, 8, 6, 0, 0, 0);
            dtSourceStart.Value = new DateTime(2006, 8, 3, 1, 0, 0);

            SelectIntervalMinutes(60);
            cmbMode.SelectedIndex = 0;

            cmbUnit.Items.Clear();
            cmbUnit.Items.Add("全部单元 (--all)");
            cmbUnit.SelectedIndex = 0;

            ResetProgress(0);
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
            CheckEnvironment(false);
            string cleanup = MaintenanceManager.CleanupKnownTemporaryFiles(
                txtUnitRoot == null ? "" : txtUnitRoot.Text.Trim());
            if (!string.IsNullOrWhiteSpace(cleanup) && cleanup != "CACHE_CLEANUP clean")
                AppendLog(cleanup);
            ScanUnits();
        }

        private void ScanUnits()
        {
            string root = txtUnitRoot.Text.Trim();

            if (!Directory.Exists(root))
            {
                cmbUnit.Items.Clear();
                cmbUnit.Items.Add("全部单元 (--all)");
                gridUnits.Rows.Clear();
                _unitRows.Clear();
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

            PrepareResolvedInputs(directories);

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
            UpdateInputSummary();
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
            string stationExternal =
                txtStationExternalRoot == null ? "" : txtStationExternalRoot.Text.Trim();
            string rainfallExternal =
                txtRainfallExternalRoot == null ? "" : txtRainfallExternalRoot.Text.Trim();
            string sourceExternal =
                txtSourceExternalRoot == null ? "" : txtSourceExternalRoot.Text.Trim();

            string start =
                dtStart == null ? "" : FormatTime(dtStart.Value);
            string end =
                dtEnd == null ? "" : FormatTime(dtEnd.Value);
            string sourceStart =
                dtSourceStart == null
                ? ""
                : FormatTime(dtSourceStart.Value);

            string interval =
                cmbInterval == null
                ? ""
                : GetSelectedIntervalMinutes()
                    .ToString(CultureInfo.InvariantCulture);

            string legacyContext = string.Join(
                "\n",
                new[]
                {
                    NormalizeContextPath(exe),
                    NormalizeContextPath(root),
                    project,
                    station,
                    rainfall,
                    NormalizeContextPath(stationExternal),
                    NormalizeContextPath(rainfallExternal),
                    NormalizeContextPath(sourceExternal),
                    start,
                    end,
                    sourceStart,
                    interval
                });
            string mappingContext = BuildInputMappingContextKey();
            return mappingContext.Length == 0
                ? legacyContext
                : legacyContext + "\n" + mappingContext;
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

                states[unit] = CaptureUnitRowState(row);
            }

            return states;
        }

        private static UnitRowState CaptureUnitRowState(DataGridViewRow row)
        {
            return new UnitRowState(
                Convert.ToString(row.Cells["Shapes"].Value),
                Convert.ToString(row.Cells["Station"].Value),
                Convert.ToString(row.Cells["Rainfall"].Value),
                Convert.ToString(row.Cells["Source"].Value),
                Convert.ToString(row.Cells["Coverage"].Value),
                Convert.ToString(row.Cells["Status"].Value),
                Convert.ToString(row.Cells["Note"].Value));
        }

        private void CaptureSuccessfulPreflightStates()
        {
            _successfulPreflightUnitStates.Clear();
            foreach (DataGridViewRow row in TargetRows())
            {
                string unit = Convert.ToString(row.Cells["Unit"].Value);
                if (!string.IsNullOrWhiteSpace(unit))
                    _successfulPreflightUnitStates[unit] = CaptureUnitRowState(row);
            }
        }

        private void RestoreSuccessfulPreflightStates()
        {
            foreach (KeyValuePair<string, UnitRowState> pair in
                _successfulPreflightUnitStates)
            {
                int rowIndex;
                if (!_unitRows.TryGetValue(pair.Key, out rowIndex))
                    continue;

                DataGridViewRow row = gridUnits.Rows[rowIndex];
                if (Convert.ToString(row.Cells["Status"].Value) == "输入缺失")
                    continue;

                UnitRowState cached = pair.Value;
                row.Cells["Shapes"].Value = cached.Shapes;
                row.Cells["Station"].Value = cached.Station;
                row.Cells["Rainfall"].Value = cached.Rainfall;
                row.Cells["Source"].Value = cached.Source;
                row.Cells["Coverage"].Value = cached.Coverage;
                row.Cells["Note"].Value = cached.Note;
            }
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

                // Source availability is recalculated from current files on every
                // scan. Do not resurrect legacy runtime labels such as
                // “已有/有效” or “已生成”. Successful preflight reuse restores the
                // exact cached source state separately after fingerprint validation.

                if (ShouldRestoreRuntimeNote(previous.Status) &&
                    !string.IsNullOrWhiteSpace(previous.Note))
                    row.Cells["Note"].Value = previous.Note;
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
            LauncherUnitInput input = GetResolvedUnitInput(folder, unit);
            string[] shapes =
            {
                input.GetPath(InputRoles.Watershed),
                input.GetPath(InputRoles.River),
                input.GetPath(InputRoles.Node),
                input.GetPath(InputRoles.Land),
                input.GetPath(InputRoles.Soil)
            };

            int shapeCount = shapes.Count(path => File.Exists(path));
            bool stationOk = File.Exists(input.GetPath(InputRoles.Station));
            bool rainfallOk = File.Exists(input.GetPath(InputRoles.Rainfall));

            int sourceCount = 0;
            try
            {
                sourceCount = Directory.GetFiles(
                    folder, "*上游水源数据表.xls").Length;
            }
            catch
            {
                sourceCount = 0;
            }

            ExternalSourceScan sourceScan =
                DetectExternalSourceTopology(input.GetPath(InputRoles.River));
            SourceAvailability sourceAvailability =
                EvaluateSourceAvailability(folder, sourceScan);

            string sourceStatus = sourceAvailability.Determined
                ? sourceAvailability.StatusText
                : (sourceCount > 0 ? "已有（拓扑未判定）" : "未判定");

            string inputValidationNote;
            bool requiredInputsOk = input.Validate(out inputValidationNote);
            string status = requiredInputsOk ? "待预检" : "输入缺失";

            var noteParts = new List<string>();
            if (shapeCount != 5)
                noteParts.Add("SHP缺" + (5 - shapeCount));
            if (!stationOk)
                noteParts.Add("缺站点");
            if (!rainfallOk)
                noteParts.Add("缺降雨");
            if (!requiredInputsOk && inputValidationNote.Length > 0)
                noteParts.Add(inputValidationNote);
            if (!requiredInputsOk &&
                !string.IsNullOrWhiteSpace(input.RecognitionNote))
                noteParts.Add(input.RecognitionNote);

            if (sourceScan.Determined)
            {
                if (sourceScan.RequiresExternalSource)
                {
                    noteParts.Add(string.Format(
                        "外部汇入河段{0}，外部上游编码{1}",
                        sourceScan.SourceSegmentCount,
                        sourceScan.ExternalUpstreamCodeCount));
                }
                else if (sourceCount > 0)
                {
                    noteParts.Add("无外部汇入但检测到已有水源表");
                }
            }
            else if (!string.IsNullOrWhiteSpace(sourceScan.Error))
            {
                noteParts.Add("水源拓扑未判定：" + sourceScan.Error);
            }

            if (!string.IsNullOrWhiteSpace(sourceAvailability.NoteText))
                noteParts.Add(sourceAvailability.NoteText);

            string note = JoinNotes(noteParts.ToArray());
            int index = gridUnits.Rows.Add(
                unit,
                shapeCount + "/5",
                FormatEventInputStatus(
                    input, InputRoles.Station, txtStationExternalRoot),
                FormatEventInputStatus(
                    input, InputRoles.Rainfall, txtRainfallExternalRoot),
                sourceStatus,
                "—",
                status,
                note);

            _unitRows[unit] = index;
            ApplyStatusStyle(gridUnits.Rows[index], status);
        }

        private string FormatEventInputStatus(
            LauncherUnitInput input, string role, TextBox externalRootBox)
        {
            string path = input.GetPath(role);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return "×";
            if (IsPathInsideUnitFolder(path, input.UnitFolder))
                return "本地✓";

            string externalRoot = GetExternalRoot(externalRootBox);
            if (externalRoot.Length > 0 &&
                IsPathInsideFolder(path, externalRoot))
                return "外部✓";
            return "映射✓";
        }

        private static ExternalSourceScan DetectExternalSourceTopology(
            string riverShpPath)
        {
            if (string.IsNullOrWhiteSpace(riverShpPath))
                return ExternalSourceScan.Unknown("河道文件尚未确定");
            string dbf = Path.ChangeExtension(riverShpPath, ".dbf");

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
                var sourceRiverCodes = new HashSet<string>(
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
                    if (!string.IsNullOrWhiteSpace(record.Rvcd))
                        sourceRiverCodes.Add(record.Rvcd.Trim());

                    foreach (string code in outside)
                        externalCodes.Add(code);
                }

                return ExternalSourceScan.Known(
                    sourceSegmentCount > 0,
                    sourceSegmentCount,
                    externalCodes.Count,
                    sourceRiverCodes.OrderBy(value => value, StringComparer.Ordinal).ToList());
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
                UpdateRunButtonAvailability();
                return;
            }

            string unit = Convert.ToString(cmbUnit.SelectedItem);
            SelectGridUnit(unit);
            UpdateRunButtonAvailability();
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

        private string BuildPreflightFingerprint()
        {
            var builder = new StringBuilder();
            builder.AppendLine(BuildTableStateContextKey());
            builder.AppendLine("SELECTOR=" +
                (cmbUnit.SelectedIndex <= 0
                    ? "--all"
                    : Convert.ToString(cmbUnit.SelectedItem)));

            foreach (string unitName in GetTargetUnitNames()
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                LauncherUnitInput unit;
                if (!_resolvedUnitInputs.TryGetValue(unitName, out unit))
                {
                    builder.AppendLine("MISSING_UNIT=" + unitName);
                    continue;
                }

                builder.AppendLine("UNIT=" + unitName);
                foreach (string role in InputRoles.All)
                {
                    string path = unit.GetPath(role);
                    builder.AppendLine(role + "=" + BuildFileStamp(path));
                    if (InputRoles.IsShape(role))
                    {
                        builder.AppendLine(role + ".shx=" +
                            BuildFileStamp(Path.ChangeExtension(path, ".shx")));
                        builder.AppendLine(role + ".dbf=" +
                            BuildFileStamp(Path.ChangeExtension(path, ".dbf")));
                        builder.AppendLine(role + ".prj=" +
                            BuildFileStamp(Path.ChangeExtension(path, ".prj")));
                        builder.AppendLine(role + ".cpg=" +
                            BuildFileStamp(Path.ChangeExtension(path, ".cpg")));
                    }
                }

                ExternalSourceScan sourceScan = DetectExternalSourceTopology(
                    unit.GetPath(InputRoles.River));
                if (sourceScan.Determined && sourceScan.RequiresExternalSource)
                {
                    foreach (string riverCode in sourceScan.SourceRiverCodes
                        .OrderBy(value => value, StringComparer.Ordinal))
                    {
                        string fileName = riverCode + "上游水源数据表.xls";
                        string localPath = Path.Combine(unit.UnitFolder, fileName);
                        builder.AppendLine("SOURCE." + riverCode + "=" +
                            BuildFileStamp(localPath));

                        if (!File.Exists(localPath))
                        {
                            List<string> external = FindExternalSourceFiles(fileName);
                            foreach (string externalPath in external)
                                builder.AppendLine("SOURCE_EXTERNAL." + riverCode + "=" +
                                    BuildFileStamp(externalPath));
                        }
                    }
                }
            }

            // ValidateInputMappingForRun rewrites the XML even when its content is
            // unchanged. Do not use its timestamp in the preflight fingerprint; the
            // resolved role paths and their file stamps above already represent all
            // selected-unit mapping content relevant to preflight.
            if (!string.IsNullOrWhiteSpace(_inputMappingPath))
                builder.AppendLine("MAPFILE=" + NormalizeContextPath(_inputMappingPath));

            return builder.ToString();
        }

        private static string BuildFileStamp(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "<empty>";
            try
            {
                string full = Path.GetFullPath(path);
                if (!File.Exists(full)) return full.ToUpperInvariant() + "|MISSING";
                FileInfo info = new FileInfo(full);
                return full.ToUpperInvariant() + "|" +
                    info.Length.ToString(CultureInfo.InvariantCulture) + "|" +
                    info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
            }
            catch
            {
                return path.Trim().ToUpperInvariant() + "|INVALID";
            }
        }

        private bool CoreSupportsPreflightReuse(string workflowExe)
        {
            HashSet<string> capabilities = QueryWorkflowCapabilities(workflowExe);
            bool supported = capabilities.Contains("skip-preflight");
            AppendLog("CORE_CAPABILITIES " +
                (capabilities.Count == 0
                    ? "unavailable"
                    : string.Join(",", capabilities.OrderBy(value => value).ToArray())));
            return supported;
        }

        private HashSet<string> QueryWorkflowCapabilities(string workflowExe)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(workflowExe) || !File.Exists(workflowExe))
                return result;

            try
            {
                var startInfo = new ProcessStartInfo();
                startInfo.FileName = workflowExe;
                startInfo.Arguments = "--capabilities";
                startInfo.WorkingDirectory =
                    _deployment == null ? AppDomain.CurrentDomain.BaseDirectory : _deployment.ModelRoot;
                startInfo.UseShellExecute = false;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = false;
                startInfo.CreateNoWindow = true;

                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                        return result;

                    if (!process.WaitForExit(5000))
                    {
                        try { process.Kill(); } catch { }
                        return result;
                    }

                    string output = process.StandardOutput.ReadToEnd();
                    foreach (string rawLine in output.Split(
                        new[] { '\r', '\n' },
                        StringSplitOptions.RemoveEmptyEntries))
                    {
                        string line = rawLine.Trim();
                        const string prefix = "CAPABILITY ";
                        if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            string capability = line.Substring(prefix.Length).Trim();
                            if (capability.Length > 0)
                                result.Add(capability);
                        }
                    }
                }
            }
            catch
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            return result;
        }

        private void BtnRun_Click(object sender, EventArgs e)
        {
            if (_isRunning || !CheckEnvironment(false))
                return;

            string error;
            if (!ValidateInputs(out error))
            {
                MessageBox.Show(this, error, "参数检查",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            RefreshInputTable();

            if (!ValidateInputMappingForRun(out error))
            {
                MessageBox.Show(this, error, "输入数据检查",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!StageExternalSourceFilesForTargets(out error))
            {
                MessageBox.Show(this, error, "上游水源数据准备",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string cacheCleanup = MaintenanceManager.CleanupKnownTemporaryFiles(
                txtUnitRoot.Text.Trim());
            if (!string.IsNullOrWhiteSpace(cacheCleanup) && cacheCleanup != "CACHE_CLEANUP clean")
                AppendLog(cacheCleanup);

            _currentRunFingerprint = BuildPreflightFingerprint();
            _preflightSummaryAllValid = false;
            _databaseBackupPath = "";

            _deployment = DeploymentLayout.Detect(AppDomain.CurrentDomain.BaseDirectory);
            _workflowRuntimeStager = new WorkflowRuntimeStager(_deployment);
            string exe;
            string stageMessage;
            if (!_workflowRuntimeStager.Ensure(out exe, out stageMessage))
            {
                MessageBox.Show(this, stageMessage, "工作流核心准备",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                _workflowRuntimeStager = null;
                return;
            }
            AppendLog(stageMessage);

            string workingDirectory = _deployment.ModelRoot;
            bool isPreflightOnly = cmbMode.SelectedIndex == 2;
            _reusePreflightForCurrentRun = !isPreflightOnly &&
                CoreSupportsPreflightReuse(exe) &&
                !string.IsNullOrWhiteSpace(_successfulPreflightFingerprint) &&
                string.Equals(
                    _successfulPreflightFingerprint,
                    _currentRunFingerprint,
                    StringComparison.Ordinal);

            if (_reusePreflightForCurrentRun)
                AppendLog("PREFLIGHT_CACHE reuse=1");
            else if (!isPreflightOnly && !string.IsNullOrWhiteSpace(_successfulPreflightFingerprint))
                AppendLog("PREFLIGHT_CACHE reuse=0 reason=context_changed_or_core_unsupported");

            if (!isPreflightOnly)
            {
                string interruptedRecovery = MaintenanceManager.RecoverInterruptedRollback(
                    workingDirectory);
                if (!string.IsNullOrWhiteSpace(interruptedRecovery))
                    AppendLog(interruptedRecovery);
                if (interruptedRecovery.StartsWith(
                    "DATABASE_INTERRUPTED_ROLLBACK_FAILED",
                    StringComparison.Ordinal))
                {
                    MessageBox.Show(
                        this,
                        "检测到上一次异常中断留下的数据库回滚文件，但自动恢复失败。\r\n\r\n" +
                        interruptedRecovery + "\r\n\r\n" +
                        "为避免覆盖恢复文件，本次计算已取消。",
                        "数据库恢复失败",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                string backupMaintenance = MaintenanceManager.NormalizeDatabaseBackups(
                    workingDirectory);
                if (!string.IsNullOrWhiteSpace(backupMaintenance))
                    AppendLog(backupMaintenance);
            }

            if (_reusePreflightForCurrentRun)
                RestoreSuccessfulPreflightStates();

            PrepareRunTracking();
            string arguments = BuildArguments();

            AppendInputMappingLog();
            AppendLog("GUI_COMMAND " + Quote(exe) + " " + arguments);
            AppendLog("GUI_WORKING_DIRECTORY " + workingDirectory);
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
                string cleanup = _workflowRuntimeStager == null
                    ? ""
                    : _workflowRuntimeStager.Cleanup();
                if (cleanup.Length > 0) AppendLog(cleanup);
                _workflowRuntimeStager = null;
                SetRunning(false);
                lblStatus.Text = "启动失败";
                MessageBox.Show(this, exception.Message, "无法启动核心程序",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PrepareRunTracking()
        {
            _scenarioMismatchDetected = false;
            _completedUnits.Clear();
            _unitsWithSourceLog.Clear();
            _unitSourceValidFiles.Clear();
            _unitSourceGeneratedFiles.Clear();
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

                if (!_reusePreflightForCurrentRun)
                    row.Cells["Coverage"].Value = "—";

                string status =
                    Convert.ToString(row.Cells["Status"].Value);

                if (status != "输入缺失")
                    SetUnitStatus(
                        unit,
                        _reusePreflightForCurrentRun ? "等待运行" : "等待预检",
                        "");
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
            _deployment = DeploymentLayout.Detect(AppDomain.CurrentDomain.BaseDirectory);
            if (_deployment == null || !_deployment.IsDetected)
            {
                MessageBox.Show(this,
                    _deployment == null ? "未识别到原水文模型根目录。" : _deployment.Error,
                    "打开输出目录",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            string outputRoot = Path.Combine(_deployment.ModelRoot, "SKBYEXE", "output");
            string unit = GetPreferredOutputUnit();
            string target = outputRoot;

            if (!string.IsNullOrWhiteSpace(unit))
            {
                string outFolder = Path.Combine(outputRoot, unit, "out");
                string unitFolder = Path.Combine(outputRoot, unit);
                if (Directory.Exists(outFolder))
                    target = outFolder;
                else if (Directory.Exists(unitFolder))
                    target = unitFolder;
            }

            if (!Directory.Exists(target))
            {
                MessageBox.Show(this,
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
                MessageBox.Show(this, exception.Message, "无法打开输出目录",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            if (line.IndexOf(
                    "Existing flood event does not match the requested start/end time or interval",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _scenarioMismatchDetected = true;
                if (!string.IsNullOrWhiteSpace(_currentUnit))
                    SetUnitStatus(_currentUnit, "失败", "已有工程场次参数与当前设置不一致");
            }

            if (line.StartsWith("DATABASE_BACKUP "))
            {
                _databaseBackupPath = line.Substring("DATABASE_BACKUP ".Length).Trim();
                return;
            }

            if (line.StartsWith("DATABASE_ROLLBACK_READY "))
            {
                _databaseBackupPath = line.Substring("DATABASE_ROLLBACK_READY ".Length).Trim();
                return;
            }

            if (line.StartsWith("PREFLIGHT_REUSED total="))
            {
                int total;
                TryParseIntValue(line, "total=", out total);
                _expectedProgressUnits = Math.Max(1, total);
                progressUnits.Maximum = Math.Max(1, total);
                progressUnits.Value = 0;
                lblProgress.Text = "进度：0 / " + total;
                foreach (DataGridViewRow row in TargetRows())
                {
                    string unit = Convert.ToString(row.Cells["Unit"].Value);
                    SetUnitStatus(unit, "等待运行", "");
                    AppendLog("PREFLIGHT_REUSED unit=" + unit);
                }
                lblStatus.Text = "已复用预检结果，准备运行";
                return;
            }

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
                _preflightSummaryAllValid = selected > 0 && valid == selected && skipped == 0;

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
                IncrementSourceCounter(_unitSourceValidFiles, _currentUnit);
                UpdateRuntimeSourceStatus(_currentUnit);
                return;
            }

            if (line.StartsWith("SOURCE_FILE_CREATED "))
            {
                MarkCurrentUnitHasSource();
                IncrementSourceCounter(_unitSourceGeneratedFiles, _currentUnit);
                UpdateRuntimeSourceStatus(_currentUnit);
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
                IncrementSourceCounter(_unitSourceGeneratedFiles, _currentUnit);
                UpdateRuntimeSourceStatus(_currentUnit);
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

        private static void IncrementSourceCounter(
            Dictionary<string, int> counters,
            string unit)
        {
            if (string.IsNullOrWhiteSpace(unit))
                return;
            int value;
            counters.TryGetValue(unit, out value);
            counters[unit] = value + 1;
        }

        private void UpdateRuntimeSourceStatus(string unit)
        {
            if (string.IsNullOrWhiteSpace(unit))
                return;

            int valid = 0;
            int generated = 0;
            _unitSourceValidFiles.TryGetValue(unit, out valid);
            _unitSourceGeneratedFiles.TryGetValue(unit, out generated);
            int available = valid + generated;

            int required = available;
            LauncherUnitInput input;
            if (_resolvedUnitInputs.TryGetValue(unit, out input))
            {
                ExternalSourceScan scan = DetectExternalSourceTopology(
                    input.GetPath(InputRoles.River));
                if (scan.Determined && scan.RequiresExternalSource)
                    required = Math.Max(required, scan.SourceRiverCodes.Count);
            }

            if (required <= 0)
            {
                SetSourceStatus(unit, "无需外部水源");
                return;
            }

            string status;
            if (generated == 0)
                status = "本地 " + available + "/" + required;
            else if (valid == 0)
                status = "自动生成 " + available + "/" + required;
            else
                status = "混合 " + available + "/" + required;

            SetSourceStatus(unit, status);
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

                if (_workflowRuntimeStager != null)
                {
                    AppendLog(_workflowRuntimeStager.Cleanup());
                    _workflowRuntimeStager = null;
                }

                bool wasPreflightOnly = cmbMode.SelectedIndex == 2;
                if (wasPreflightOnly)
                {
                    if (exitCode == 0 && _preflightSummaryAllValid)
                    {
                        _successfulPreflightFingerprint = _currentRunFingerprint;
                        CaptureSuccessfulPreflightStates();
                        AppendLog("PREFLIGHT_CACHE stored=1 rows=" +
                            _successfulPreflightUnitStates.Count);
                    }
                    else
                    {
                        _successfulPreflightFingerprint = "";
                        _successfulPreflightUnitStates.Clear();
                        AppendLog("PREFLIGHT_CACHE stored=0");
                    }
                }
                else
                {
                    string dbResult = exitCode == 0
                        ? MaintenanceManager.FinalizeSuccessfulRun(
                            _deployment.ModelRoot,
                            _databaseBackupPath)
                        : MaintenanceManager.RestoreFailedRun(
                            _deployment.ModelRoot,
                            _databaseBackupPath);
                    if (!string.IsNullOrWhiteSpace(dbResult))
                        AppendLog(dbResult);
                    if (dbResult.StartsWith(
                        "DATABASE_ROLLBACK_FAILED",
                        StringComparison.Ordinal))
                    {
                        MessageBox.Show(
                            this,
                            "本次运行失败，并且 project.db 自动回滚失败。\r\n\r\n" +
                            dbResult + "\r\n\r\n" +
                            "请暂时不要再次运行，先保留 unified_workflow_backups 中的回滚文件。",
                            "数据库自动回滚失败",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                }

                SetRunning(false);

                if (exitCode == 0)
                {
                    if (wasPreflightOnly)
                        lblStatus.Text = "预检完成";
                    else if (!lblStatus.Text.StartsWith("完成："))
                        lblStatus.Text = "运行完成";
                }
                else
                {
                    lblStatus.Text = "运行结束，退出码 " + exitCode;
                    if (_scenarioMismatchDetected)
                    {
                        MessageBox.Show(
                            this,
                            "工程 “" + txtProject.Text.Trim() +
                            "” 已存在，但其中的洪水场次参数与当前设置不一致。\r\n\r\n" +
                            "当前设置：" + FormatTime(dtStart.Value) + " ～ " +
                            FormatTime(dtEnd.Value) + "，计算时间间隔 " +
                            GetSelectedIntervalMinutes() + " min。\r\n\r\n" +
                            "请使用新的工程名称，或恢复该工程原有的场次参数。\r\n" +
                            "程序不会覆盖已有洪水场次。",
                            "已有工程场次参数不一致",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                }
            }));
        }

        private bool ValidateInputs(out string error)
        {
            error = "";

            _deployment = DeploymentLayout.Detect(AppDomain.CurrentDomain.BaseDirectory);
            if (_deployment == null || !_deployment.IsDetected)
            {
                error = _deployment == null
                    ? "未识别到原水文模型根目录。"
                    : _deployment.Error;
                return false;
            }

            if (!File.Exists(_deployment.WorkflowSourcePath))
            {
                error = "未找到 UnifiedHydroWorkflow.exe：\r\n"
                    + _deployment.WorkflowSourcePath
                    + "\r\n\r\n请将 Workflow Core V" + AppInfo.WorkflowCoreVersion +
                    " 与 Launcher 放在 UnifiedHydroWorkflow 程序文件夹内。";
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

            bool stationNameRequired = !IsSmartInputMapping ||
                Convert.ToString(cmbStationMode.SelectedItem) ==
                    InputDiscovery.UnifiedNameMode;
            bool rainfallNameRequired = !IsSmartInputMapping ||
                Convert.ToString(cmbRainfallMode.SelectedItem) ==
                    InputDiscovery.UnifiedNameMode;

            if (stationNameRequired && string.IsNullOrWhiteSpace(txtStation.Text))
            {
                error = "站点文件名不能为空。";
                return false;
            }

            if (rainfallNameRequired && string.IsNullOrWhiteSpace(txtRainfall.Text))
            {
                error = "降雨文件名不能为空。";
                return false;
            }

            if (stationNameRequired &&
                Path.GetFileName(txtStation.Text) != txtStation.Text.Trim())
            {
                error = "站点文件请输入文件名，不要输入完整路径。";
                return false;
            }

            if (rainfallNameRequired &&
                Path.GetFileName(txtRainfall.Text) != txtRainfall.Text.Trim())
            {
                error = "降雨文件请输入文件名，不要输入完整路径。";
                return false;
            }

            if (txtStationExternalRoot != null &&
                !string.IsNullOrWhiteSpace(txtStationExternalRoot.Text) &&
                !Directory.Exists(txtStationExternalRoot.Text.Trim()))
            {
                error = "站点外部目录不存在。";
                return false;
            }

            if (txtRainfallExternalRoot != null &&
                !string.IsNullOrWhiteSpace(txtRainfallExternalRoot.Text) &&
                !Directory.Exists(txtRainfallExternalRoot.Text.Trim()))
            {
                error = "降雨外部目录不存在。";
                return false;
            }

            if (txtSourceExternalRoot != null &&
                !string.IsNullOrWhiteSpace(txtSourceExternalRoot.Text) &&
                !Directory.Exists(txtSourceExternalRoot.Text.Trim()))
            {
                error = "上游水源外部目录不存在。";
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
            int interval = GetSelectedIntervalMinutes();
            if (interval <= 0)
            {
                error = "请选择计算时间间隔。";
                return false;
            }

            int eventWholeMinutes = Convert.ToInt32(Math.Round(eventMinutes));
            int sourceWholeMinutes = Convert.ToInt32(Math.Round(sourceMinutes));
            int eventRemainder = eventWholeMinutes % interval;
            if (eventRemainder != 0)
            {
                error = string.Format(
                    CultureInfo.InvariantCulture,
                    "场次总时长为 {0} min，不能被当前计算时间间隔 {1} min 整除（余 {2} min）。\r\n请调整计算时间间隔或场次起止时间。",
                    eventWholeMinutes, interval, eventRemainder);
                return false;
            }

            int sourceRemainder = sourceWholeMinutes % interval;
            if (sourceRemainder != 0)
            {
                error = string.Format(
                    CultureInfo.InvariantCulture,
                    "上游水源开始至场次结束共 {0} min，不能被当前计算时间间隔 {1} min 整除（余 {2} min）。\r\n请调整上游水源开始时间或计算时间间隔。",
                    sourceWholeMinutes, interval, sourceRemainder);
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

            if (_reusePreflightForCurrentRun)
                builder.Append(" --skip-preflight");

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
                GetSelectedIntervalMinutes()
                    .ToString(CultureInfo.InvariantCulture));

            builder.Append(" --source-start ");
            builder.Append(
                Quote(FormatTime(dtSourceStart.Value)));

            AppendInputMappingArgument(builder);

            return builder.ToString();
        }

        private sealed class IntervalOption
        {
            internal readonly string Label;
            internal readonly int Minutes;

            internal IntervalOption(string label, int minutes)
            {
                Label = label;
                Minutes = minutes;
            }

            public override string ToString()
            {
                return Label;
            }
        }

        private void AddIntervalOption(string label, int minutes)
        {
            cmbInterval.Items.Add(new IntervalOption(label, minutes));
        }

        private int GetSelectedIntervalMinutes()
        {
            IntervalOption option = cmbInterval == null
                ? null
                : cmbInterval.SelectedItem as IntervalOption;
            return option == null ? 0 : option.Minutes;
        }

        private void SelectIntervalMinutes(int minutes)
        {
            if (cmbInterval == null) return;
            for (int index = 0; index < cmbInterval.Items.Count; index++)
            {
                IntervalOption option = cmbInterval.Items[index] as IntervalOption;
                if (option != null && option.Minutes == minutes)
                {
                    cmbInterval.SelectedIndex = index;
                    return;
                }
            }
            if (cmbInterval.Items.Count > 0)
                cmbInterval.SelectedIndex = 0;
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
            public readonly string Shapes;
            public readonly string Station;
            public readonly string Rainfall;
            public readonly string Source;
            public readonly string Coverage;
            public readonly string Status;
            public readonly string Note;

            public UnitRowState(
                string shapes,
                string station,
                string rainfall,
                string source,
                string coverage,
                string status,
                string note)
            {
                Shapes = shapes ?? "";
                Station = station ?? "";
                Rainfall = rainfall ?? "";
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
            public readonly List<string> SourceRiverCodes;
            public readonly string Error;

            private ExternalSourceScan(
                bool determined,
                bool requiresExternalSource,
                int sourceSegmentCount,
                int externalUpstreamCodeCount,
                IEnumerable<string> sourceRiverCodes,
                string error)
            {
                Determined = determined;
                RequiresExternalSource = requiresExternalSource;
                SourceSegmentCount = sourceSegmentCount;
                ExternalUpstreamCodeCount =
                    externalUpstreamCodeCount;
                SourceRiverCodes = (sourceRiverCodes ?? new string[0])
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                Error = error ?? "";
            }

            public static ExternalSourceScan Known(
                bool requiresExternalSource,
                int sourceSegmentCount,
                int externalUpstreamCodeCount,
                IEnumerable<string> sourceRiverCodes)
            {
                return new ExternalSourceScan(
                    true,
                    requiresExternalSource,
                    sourceSegmentCount,
                    externalUpstreamCodeCount,
                    sourceRiverCodes,
                    "");
            }

            public static ExternalSourceScan Unknown(string error)
            {
                return new ExternalSourceScan(
                    false,
                    false,
                    0,
                    0,
                    new string[0],
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
            _isRunning = running;
            if (!running)
                CheckEnvironment(false);
            UpdateRunButtonAvailability();
            btnCancel.Enabled = running;

            btnBrowseRoot.Enabled = !running;
            btnScan.Enabled = !running;
            if (btnPreprocess != null) btnPreprocess.Enabled = !running;

            txtExe.Enabled = !running;
            txtUnitRoot.Enabled = !running;
            txtProject.Enabled = !running;
            cmbUnit.Enabled = !running;
            txtStation.Enabled = !running;
            txtRainfall.Enabled = !running;
            dtStart.Enabled = !running;
            dtEnd.Enabled = !running;
            dtSourceStart.Enabled = !running;
            cmbInterval.Enabled = !running;
            cmbMode.Enabled = !running;
            cmbInputMode.Enabled = !running;
            cmbStationMode.Enabled = !running;
            cmbRainfallMode.Enabled = !running;
            UpdateInputControlState();
        }
    }
}
