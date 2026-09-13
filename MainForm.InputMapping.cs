using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace UnifiedHydroLauncher
{
    public sealed partial class MainForm
    {
        private const string SmartInputMode = "智能映射（推荐）";
        private const string LegacyInputMode = "标准命名兼容模式";

        private ComboBox cmbInputMode;
        private ComboBox cmbStationMode;
        private ComboBox cmbRainfallMode;
        private TextBox txtStationExternalRoot;
        private TextBox txtRainfallExternalRoot;
        private TextBox txtSourceExternalRoot;
        private Button btnBrowseStationExternalRoot;
        private Button btnBrowseRainfallExternalRoot;
        private Button btnBrowseSourceExternalRoot;
        private Label lblInputData;
        private Button btnToggleExternalSources;
        private Panel pnlExternalSources;
        private ToolTip externalPathToolTip;
        private Button btnInputMapping;
        private Button btnRecognitionRules;
        private ToolTip recognitionRulesToolTip;
        private LauncherInputMappingDocument _inputMappingDocument;
        private readonly Dictionary<string, LauncherUnitInput> _resolvedUnitInputs =
            new Dictionary<string, LauncherUnitInput>(StringComparer.OrdinalIgnoreCase);
        private string _inputMappingPath = "";
        private string _inputMappingLoadError = "";
        private bool _buildingInputUi;
        private TableLayoutPanel _inputSettingsLayout;
        private int _unifiedFileNameRow = -1;
        private Label lblStationUnifiedName;
        private Label lblRainfallUnifiedName;

        private void BuildInputModeRow(TableLayoutPanel settings, int row)
        {
            AddLabel(settings, "输入方式", row);
            cmbInputMode = new ComboBox();
            cmbInputMode.Dock = DockStyle.Fill;
            cmbInputMode.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbInputMode.Items.AddRange(new object[] { SmartInputMode, LegacyInputMode });
            cmbInputMode.SelectedIndexChanged += InputModeChanged;
            settings.Controls.Add(cmbInputMode, 1, row);

            AddSmallLabel(settings, "计算单元输入", 2, row);
            lblInputData = new Label();
            lblInputData.Dock = DockStyle.Fill;
            lblInputData.Text = "待扫描";
            lblInputData.TextAlign = ContentAlignment.MiddleLeft;
            settings.Controls.Add(lblInputData, 3, row);

            btnInputMapping = NewButton("输入数据映射...");
            btnInputMapping.Click += BtnInputMapping_Click;
            settings.Controls.Add(btnInputMapping, 4, row);
        }

        private void ShowRecognitionRules_Click(object sender, EventArgs e)
        {
            MessageBox.Show(
                this,
                "自动识别规则：\r\n\r\n" +
                "1. 计算单元文件夹内的数据优先；本地缺失时，才搜索当前设置的外部数据目录。\r\n\r\n" +
                "2. 站点数据可识别：\r\n" +
                "   · 名称含“站点”的文件，例如 站点信息.xlsx；\r\n" +
                "   · 与计算单元名称对应的文件，例如 WFC17_2_3_1.xlsx、17_2_3_1.xlsx。\r\n\r\n" +
                "3. 降雨数据可识别：\r\n" +
                "   · 名称含“降雨”的文件，例如 20060803111降雨.xlsx、降雨.xlsx；\r\n" +
                "   · 排除站点文件后只剩一个候选时，可识别其他名称。\r\n\r\n" +
                "4. 不建议使用 1.xlsx、2.xlsx 等无明显含义的文件名。\r\n\r\n" +
                "5. 所有计算单元使用同一文件名时，推荐选择“统一文件名”；\r\n" +
                "   文件名特殊或存在多个候选时，请使用“输入数据映射”。",
                "站点/降雨自动识别规则",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void BuildStationRainfallModeRow(TableLayoutPanel settings, int row)
        {
            AddLabel(settings, "站点数据", row);
            cmbStationMode = NewInputFileModeCombo();
            cmbStationMode.SelectedIndexChanged += InputFileModeChanged;
            settings.Controls.Add(cmbStationMode, 1, row);

            AddSmallLabel(settings, "降雨数据", 2, row);
            cmbRainfallMode = NewInputFileModeCombo();
            cmbRainfallMode.SelectedIndexChanged += InputFileModeChanged;
            settings.Controls.Add(cmbRainfallMode, 3, row);

            // Recognition help belongs to station/rainfall recognition.
            // Add the small button directly to the table cell so Windows DPI scaling
            // cannot collapse a nested FlowLayoutPanel and hide it.
            btnRecognitionRules = new Button();
            btnRecognitionRules.Text = "?";
            btnRecognitionRules.Width = 36;
            btnRecognitionRules.Height = 26;
            btnRecognitionRules.Anchor = AnchorStyles.Left;
            btnRecognitionRules.Margin = new Padding(8, 4, 0, 4);
            btnRecognitionRules.TabStop = false;
            btnRecognitionRules.Click += ShowRecognitionRules_Click;
            recognitionRulesToolTip = new ToolTip();
            recognitionRulesToolTip.SetToolTip(btnRecognitionRules, "站点/降雨自动识别规则");
            settings.Controls.Add(btnRecognitionRules, 4, row);
        }

        private void BuildUnifiedFileNameRow(TableLayoutPanel settings, int row)
        {
            _inputSettingsLayout = settings;
            _unifiedFileNameRow = row;

            lblStationUnifiedName = new Label();
            lblStationUnifiedName.Text = "站点文件名";
            lblStationUnifiedName.Dock = DockStyle.Fill;
            lblStationUnifiedName.TextAlign = ContentAlignment.MiddleLeft;
            settings.Controls.Add(lblStationUnifiedName, 0, row);

            txtStation = NewTextBox();
            txtStation.Leave += InputFileNameChanged;
            settings.Controls.Add(txtStation, 1, row);

            lblRainfallUnifiedName = new Label();
            lblRainfallUnifiedName.Text = "降雨文件名";
            lblRainfallUnifiedName.Dock = DockStyle.Fill;
            lblRainfallUnifiedName.TextAlign = ContentAlignment.MiddleLeft;
            settings.Controls.Add(lblRainfallUnifiedName, 2, row);

            txtRainfall = NewTextBox();
            txtRainfall.Leave += InputFileNameChanged;
            settings.Controls.Add(txtRainfall, 3, row);
        }

        private void BuildStationModeRow(TableLayoutPanel settings, int row)
        {
            AddLabel(settings, "站点数据", row);
            cmbStationMode = NewInputFileModeCombo();
            cmbStationMode.SelectedIndexChanged += InputFileModeChanged;
            settings.Controls.Add(cmbStationMode, 1, row);
            AddSmallLabel(settings, "统一文件名", 2, row);
            txtStation = NewTextBox();
            txtStation.Leave += InputFileNameChanged;
            settings.Controls.Add(txtStation, 3, row);
            settings.SetColumnSpan(txtStation, 2);
        }

        private void BuildRainfallModeRow(TableLayoutPanel settings, int row)
        {
            AddLabel(settings, "降雨数据", row);
            cmbRainfallMode = NewInputFileModeCombo();
            cmbRainfallMode.SelectedIndexChanged += InputFileModeChanged;
            settings.Controls.Add(cmbRainfallMode, 1, row);
            AddSmallLabel(settings, "统一文件名", 2, row);
            txtRainfall = NewTextBox();
            txtRainfall.Leave += InputFileNameChanged;
            settings.Controls.Add(txtRainfall, 3, row);
            settings.SetColumnSpan(txtRainfall, 2);
        }

        private void BuildExternalSourcesSection(TableLayoutPanel settings, int row)
        {
            var host = new Panel();
            host.Dock = DockStyle.Top;
            host.AutoSize = true;
            host.Margin = new Padding(0, 2, 0, 2);
            settings.Controls.Add(host, 1, row);
            settings.SetColumnSpan(host, 4);

            var wrapper = new TableLayoutPanel();
            wrapper.Dock = DockStyle.Top;
            wrapper.AutoSize = true;
            wrapper.ColumnCount = 1;
            wrapper.RowCount = 2;
            wrapper.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            wrapper.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            host.Controls.Add(wrapper);

            btnToggleExternalSources = new Button();
            btnToggleExternalSources.Dock = DockStyle.Fill;
            btnToggleExternalSources.Height = 30;
            btnToggleExternalSources.FlatStyle = FlatStyle.Flat;
            btnToggleExternalSources.FlatAppearance.BorderSize = 0;
            btnToggleExternalSources.TextAlign = ContentAlignment.MiddleLeft;
            btnToggleExternalSources.Text = "▶ 外部数据源（未设置，本地优先）";
            btnToggleExternalSources.Click += ToggleExternalSources_Click;
            wrapper.Controls.Add(btnToggleExternalSources, 0, 0);

            pnlExternalSources = new Panel();
            pnlExternalSources.Dock = DockStyle.Top;
            pnlExternalSources.AutoSize = true;
            pnlExternalSources.Visible = false;
            wrapper.Controls.Add(pnlExternalSources, 0, 1);

            var external = new TableLayoutPanel();
            external.Dock = DockStyle.Top;
            external.AutoSize = true;
            external.ColumnCount = 3;
            external.RowCount = 3;
            external.Padding = new Padding(8, 2, 0, 4);
            external.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125F));
            external.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            external.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95F));
            for (int index = 0; index < 3; index++)
                external.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            pnlExternalSources.Controls.Add(external);

            externalPathToolTip = new ToolTip();
            externalPathToolTip.AutoPopDelay = 12000;
            externalPathToolTip.InitialDelay = 350;
            externalPathToolTip.ReshowDelay = 150;

            AddExternalDirectoryRow(external, 0, "站点数据目录", out txtStationExternalRoot,
                out btnBrowseStationExternalRoot);
            AddExternalDirectoryRow(external, 1, "降雨数据目录", out txtRainfallExternalRoot,
                out btnBrowseRainfallExternalRoot);
            AddExternalDirectoryRow(external, 2, "上游水源数据目录", out txtSourceExternalRoot,
                out btnBrowseSourceExternalRoot);
        }

        private void AddExternalDirectoryRow(
            TableLayoutPanel panel,
            int row,
            string caption,
            out TextBox box,
            out Button browseButton)
        {
            var label = new Label();
            label.Text = caption;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            panel.Controls.Add(label, 0, row);

            box = NewTextBox();
            box.Leave += ExternalInputDirectoryChanged;
            box.TextChanged += ExternalPathTextChanged;
            panel.Controls.Add(box, 1, row);

            browseButton = NewButton("浏览...");
            browseButton.Tag = box;
            browseButton.Click += BrowseExternalDirectory_Click;
            panel.Controls.Add(browseButton, 2, row);
        }

        private void ToggleExternalSources_Click(object sender, EventArgs e)
        {
            if (pnlExternalSources == null) return;
            pnlExternalSources.Visible = !pnlExternalSources.Visible;
            UpdateExternalSourcesHeader();
        }

        private void UpdateExternalSourcesHeader()
        {
            if (btnToggleExternalSources == null) return;
            var configured = new List<string>();
            if (txtStationExternalRoot != null &&
                !string.IsNullOrWhiteSpace(txtStationExternalRoot.Text))
                configured.Add("站点");
            if (txtRainfallExternalRoot != null &&
                !string.IsNullOrWhiteSpace(txtRainfallExternalRoot.Text))
                configured.Add("降雨");
            if (txtSourceExternalRoot != null &&
                !string.IsNullOrWhiteSpace(txtSourceExternalRoot.Text))
                configured.Add("上游水源");

            string arrow = pnlExternalSources != null && pnlExternalSources.Visible
                ? "▼ " : "▶ ";
            string state = configured.Count == 0
                ? "未设置"
                : "已设置：" + string.Join("、", configured.ToArray());
            btnToggleExternalSources.Text =
                arrow + "外部数据源（" + state + "，本地优先）";
        }

        private void ExternalPathTextChanged(object sender, EventArgs e)
        {
            TextBox box = sender as TextBox;
            if (box != null && externalPathToolTip != null)
                externalPathToolTip.SetToolTip(box, box.Text.Trim());
            UpdateExternalSourcesHeader();
        }

        private void BrowseExternalDirectory_Click(object sender, EventArgs e)
        {
            Button button = sender as Button;
            TextBox target = button == null ? null : button.Tag as TextBox;
            if (target == null) return;
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择外部数据目录（可留空，计算单元文件夹始终优先）";
                string current = target.Text.Trim();
                if (Directory.Exists(current))
                    dialog.SelectedPath = current;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    target.Text = dialog.SelectedPath;
                    RescanForInputConfigurationChange();
                }
            }
        }

        private void ExternalInputDirectoryChanged(object sender, EventArgs e)
        {
            RescanForInputConfigurationChange();
        }

        private static ComboBox NewInputFileModeCombo()
        {
            var combo = new ComboBox();
            combo.Dock = DockStyle.Fill;
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.Items.AddRange(new object[]
            {
                InputDiscovery.AutomaticMode,
                InputDiscovery.UnifiedNameMode
            });
            return combo;
        }

        private bool IsSmartInputMapping
        {
            get
            {
                return cmbInputMode != null &&
                       Convert.ToString(cmbInputMode.SelectedItem) == SmartInputMode;
            }
        }

        private void InputModeChanged(object sender, EventArgs e)
        {
            UpdateInputControlState();
            RescanForInputConfigurationChange();
        }

        private void InputFileModeChanged(object sender, EventArgs e)
        {
            UpdateInputControlState();
            RescanForInputConfigurationChange();
        }

        private void InputFileNameChanged(object sender, EventArgs e)
        {
            RescanForInputConfigurationChange();
        }

        private void UpdateInputControlState()
        {
            if (cmbInputMode == null || txtStation == null || txtRainfall == null)
                return;
            bool smart = IsSmartInputMapping;
            btnInputMapping.Enabled = !_isRunning && smart;
            if (btnRecognitionRules != null)
                btnRecognitionRules.Enabled = !_isRunning;
            txtStation.Enabled = !_isRunning && (!smart ||
                Convert.ToString(cmbStationMode.SelectedItem) ==
                    InputDiscovery.UnifiedNameMode);
            txtRainfall.Enabled = !_isRunning && (!smart ||
                Convert.ToString(cmbRainfallMode.SelectedItem) ==
                    InputDiscovery.UnifiedNameMode);
            if (txtStationExternalRoot != null)
                txtStationExternalRoot.Enabled = !_isRunning && smart;
            if (txtRainfallExternalRoot != null)
                txtRainfallExternalRoot.Enabled = !_isRunning && smart;
            if (txtSourceExternalRoot != null)
                txtSourceExternalRoot.Enabled = !_isRunning;
            if (btnBrowseStationExternalRoot != null)
                btnBrowseStationExternalRoot.Enabled = !_isRunning && smart;
            if (btnBrowseRainfallExternalRoot != null)
                btnBrowseRainfallExternalRoot.Enabled = !_isRunning && smart;
            if (btnBrowseSourceExternalRoot != null)
                btnBrowseSourceExternalRoot.Enabled = !_isRunning;
            UpdateUnifiedFileNameRowVisibility();
            UpdateExternalSourcesHeader();
        }

        private void UpdateUnifiedFileNameRowVisibility()
        {
            if (_inputSettingsLayout == null || _unifiedFileNameRow < 0 ||
                cmbStationMode == null || cmbRainfallMode == null)
                return;

            bool stationUnified = !IsSmartInputMapping ||
                Convert.ToString(cmbStationMode.SelectedItem) ==
                    InputDiscovery.UnifiedNameMode;
            bool rainfallUnified = !IsSmartInputMapping ||
                Convert.ToString(cmbRainfallMode.SelectedItem) ==
                    InputDiscovery.UnifiedNameMode;
            bool visible = stationUnified || rainfallUnified;

            _inputSettingsLayout.RowStyles[_unifiedFileNameRow].SizeType =
                SizeType.Absolute;
            _inputSettingsLayout.RowStyles[_unifiedFileNameRow].Height =
                visible ? 34F : 0F;

            if (lblStationUnifiedName != null)
                lblStationUnifiedName.Visible = stationUnified;
            if (txtStation != null)
                txtStation.Visible = stationUnified;
            if (lblRainfallUnifiedName != null)
                lblRainfallUnifiedName.Visible = rainfallUnified;
            if (txtRainfall != null)
                txtRainfall.Visible = rainfallUnified;
        }

        private void RescanForInputConfigurationChange()
        {
            if (_buildingInputUi || txtUnitRoot == null ||
                !Directory.Exists(txtUnitRoot.Text.Trim()))
            {
                UpdateRunButtonAvailability();
                return;
            }
            ScanUnits();
        }

        private void PrepareResolvedInputs(string[] directories)
        {
            _resolvedUnitInputs.Clear();
            _inputMappingDocument = new LauncherInputMappingDocument();
            _inputMappingLoadError = "";
            string root = Path.GetFullPath(txtUnitRoot.Text.Trim());
            _inputMappingPath = Path.Combine(root, "UnifiedHydroInputMapping.xml");

            LauncherInputMappingDocument saved = null;
            if (IsSmartInputMapping && File.Exists(_inputMappingPath))
            {
                try
                {
                    saved = LauncherInputMappingDocument.Load(_inputMappingPath, root);
                }
                catch (Exception exception)
                {
                    _inputMappingLoadError = exception.Message;
                }
            }

            foreach (string directory in directories)
            {
                LauncherUnitInput discovered = InputDiscovery.Discover(
                    directory,
                    Convert.ToString(cmbStationMode.SelectedItem),
                    txtStation.Text.Trim(),
                    Convert.ToString(cmbRainfallMode.SelectedItem),
                    txtRainfall.Text.Trim(),
                    GetExternalRoot(txtStationExternalRoot),
                    GetExternalRoot(txtRainfallExternalRoot));

                LauncherUnitInput effective = discovered;
                if (IsSmartInputMapping && saved != null)
                {
                    LauncherUnitInput mapped;
                    if (saved.TryGet(discovered.UnitName, out mapped))
                    {
                        effective = discovered.Clone();
                        foreach (string role in InputRoles.All)
                        {
                            string mappedPath = mapped.GetPath(role);
                            if (role == InputRoles.Station || role == InputRoles.Rainfall)
                            {
                                string discoveredPath = discovered.GetPath(role);
                                string externalRoot = role == InputRoles.Station
                                    ? GetExternalRoot(txtStationExternalRoot)
                                    : GetExternalRoot(txtRainfallExternalRoot);

                                // Saved local choices remain valid. Saved external event
                                // files are reused only when the user has explicitly
                                // configured the corresponding current external root.
                                // This prevents a previous flood event from silently
                                // restoring an old station/rainfall file.
                                if (mappedPath.Length > 0 && File.Exists(mappedPath) &&
                                    IsPathInsideUnitFolder(mappedPath, directory))
                                {
                                    effective.SetPath(role, mappedPath);
                                }
                                else if (mappedPath.Length > 0 && File.Exists(mappedPath) &&
                                    externalRoot.Length > 0 &&
                                    IsPathInsideFolder(mappedPath, externalRoot))
                                {
                                    effective.SetPath(role, mappedPath);
                                }
                                else if (discoveredPath.Length > 0 && File.Exists(discoveredPath))
                                {
                                    effective.SetPath(role, discoveredPath);
                                }
                                else
                                {
                                    effective.SetPath(role, "");
                                }
                            }
                            else
                            {
                                effective.SetPath(role, mappedPath);
                            }
                        }
                    }
                    else
                    {
                        effective = discovered.Clone();
                        effective.RecognitionNote = JoinNotes(
                            discovered.RecognitionNote,
                            "现有 Mapping 缺少该单元，已使用当前自动识别结果");
                    }
                }
                else if (IsSmartInputMapping && _inputMappingLoadError.Length > 0)
                {
                    effective = discovered.Clone();
                    effective.RecognitionNote = JoinNotes(
                        discovered.RecognitionNote,
                        "Mapping XML 无效，请重新打开映射并保存：" +
                        _inputMappingLoadError);
                }
                else if (!IsSmartInputMapping)
                {
                    effective = CreateLegacyUnitInput(directory);
                }

                _resolvedUnitInputs[effective.UnitName] = effective;
                _inputMappingDocument.Add(effective.Clone());
            }
        }

        private LauncherUnitInput CreateLegacyUnitInput(string folder)
        {
            var unit = new LauncherUnitInput(Path.GetFileName(folder), folder);
            unit.SetPath(InputRoles.Watershed, Path.Combine(folder, "wata.shp"));
            unit.SetPath(InputRoles.River, Path.Combine(folder, "rivl.shp"));
            unit.SetPath(InputRoles.Node, Path.Combine(folder, "node.shp"));
            unit.SetPath(InputRoles.Land, Path.Combine(folder, "土地利用.shp"));
            unit.SetPath(InputRoles.Soil, Path.Combine(folder, "土壤类型.shp"));
            unit.SetPath(InputRoles.Station,
                Path.Combine(folder, txtStation.Text.Trim()));
            unit.SetPath(InputRoles.Rainfall,
                Path.Combine(folder, txtRainfall.Text.Trim()));
            return unit;
        }

        private LauncherUnitInput GetResolvedUnitInput(string folder, string unit)
        {
            LauncherUnitInput resolved;
            return _resolvedUnitInputs.TryGetValue(unit, out resolved)
                ? resolved
                : CreateLegacyUnitInput(folder);
        }

        private void UpdateInputSummary()
        {
            int ready = 0;
            int ambiguous = 0;
            int missing = 0;
            foreach (LauncherUnitInput unit in _resolvedUnitInputs.Values)
            {
                string note;
                if (unit.Validate(out note))
                    ready++;
                else if (InputRoles.All.Any(role =>
                    unit.GetPath(role).Length == 0 &&
                    unit.GetCandidates(role).Count > 1))
                    ambiguous++;
                else
                    missing++;
            }
            int total = _resolvedUnitInputs.Count;
            if (_inputMappingLoadError.Length > 0)
                lblInputData.Text = "0/" + total + " 有效，Mapping XML 异常";
            else if (ambiguous == 0 && missing == 0)
                lblInputData.Text = ready + "/" + total + " 有效";
            else
                lblInputData.Text = string.Format(
                    "{0}/{1} 有效，{2} 待确认，{3} 缺失",
                    ready, total, ambiguous, missing);
            lblInputData.ForeColor = ready == total && total > 0
                ? Color.DarkGreen
                : Color.Firebrick;
            UpdateRunButtonAvailability();
        }

        private void BtnInputMapping_Click(object sender, EventArgs e)
        {
            string root = txtUnitRoot.Text.Trim();
            if (!Directory.Exists(root))
            {
                MessageBox.Show(this, "请先选择有效的计算单元根目录。",
                    "输入数据映射", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_resolvedUnitInputs.Count == 0)
                ScanUnits();
            IEnumerable<LauncherUnitInput> editUnits = _resolvedUnitInputs.Values;
            if (_inputMappingLoadError.Length > 0)
            {
                editUnits = Directory.GetDirectories(root).Select(folder =>
                    InputDiscovery.Discover(
                        folder,
                        Convert.ToString(cmbStationMode.SelectedItem),
                        txtStation.Text.Trim(),
                        Convert.ToString(cmbRainfallMode.SelectedItem),
                        txtRainfall.Text.Trim(),
                        GetExternalRoot(txtStationExternalRoot),
                        GetExternalRoot(txtRainfallExternalRoot)));
            }
            using (var dialog = new InputMappingForm(
                root,
                editUnits,
                Convert.ToString(cmbStationMode.SelectedItem),
                txtStation.Text.Trim(),
                Convert.ToString(cmbRainfallMode.SelectedItem),
                txtRainfall.Text.Trim(),
                GetExternalRoot(txtStationExternalRoot),
                GetExternalRoot(txtRainfallExternalRoot)))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                _inputMappingDocument = dialog.Document;
                _inputMappingPath = dialog.MappingPath;
                _inputMappingLoadError = "";
                ScanUnits();
            }
        }

        private static bool IsPathInsideUnitFolder(string path, string unitFolder)
        {
            try
            {
                string folder = Path.GetFullPath(unitFolder).TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                    Path.DirectorySeparatorChar;
                string full = Path.GetFullPath(path);
                return full.StartsWith(folder, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsPathInsideFolder(string path, string folder)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(folder))
                return false;
            try
            {
                string root = Path.GetFullPath(folder).TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                    Path.DirectorySeparatorChar;
                string full = Path.GetFullPath(path);
                return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string JoinNotes(params string[] values)
        {
            var parts = new List<string>();
            foreach (string value in values)
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                foreach (string part in value.Split(new[] { '；' },
                    StringSplitOptions.RemoveEmptyEntries))
                {
                    string trimmed = part.Trim();
                    if (trimmed.Length > 0 &&
                        !parts.Any(existing => string.Equals(
                            existing, trimmed, StringComparison.OrdinalIgnoreCase)))
                        parts.Add(trimmed);
                }
            }
            return string.Join("；", parts.ToArray());
        }

        private static string GetExternalRoot(TextBox box)
        {
            if (box == null || string.IsNullOrWhiteSpace(box.Text))
                return "";
            string value = box.Text.Trim();
            return Directory.Exists(value) ? Path.GetFullPath(value) : value;
        }

        private bool ValidateInputMappingForRun(out string error)
        {
            error = "";
            if (!IsSmartInputMapping)
                return true;
            if (_inputMappingLoadError.Length > 0)
            {
                error = "输入映射 XML 无效：" + _inputMappingLoadError;
                return false;
            }
            List<string> targetUnits = GetTargetUnitNames();
            if (targetUnits.Count == 0)
            {
                error = "没有待运行的计算单元。";
                return false;
            }
            foreach (string name in targetUnits)
            {
                LauncherUnitInput unit;
                if (!_resolvedUnitInputs.TryGetValue(name, out unit))
                {
                    error = "INPUT_MAP_UNIT_MISSING unit=" + name;
                    return false;
                }
                string note;
                if (!unit.Validate(out note))
                {
                    error = "输入数据尚未就绪：" + name + "：" + note;
                    return false;
                }
            }

            try
            {
                var runnableDocument = new LauncherInputMappingDocument();
                foreach (LauncherUnitInput unit in _resolvedUnitInputs.Values)
                {
                    string validationNote;
                    if (unit.Validate(out validationNote))
                        runnableDocument.Add(unit.Clone());
                }
                runnableDocument.Save(_inputMappingPath);
                LauncherInputMappingDocument saved =
                    LauncherInputMappingDocument.Load(
                        _inputMappingPath,
                        Path.GetFullPath(txtUnitRoot.Text.Trim()));
                foreach (string name in targetUnits)
                {
                    string note;
                    if (!saved.GetRequired(name).Validate(out note))
                        throw new InvalidDataException(
                            "输入映射快速验证失败：" + name + "：" + note);
                }
                _inputMappingDocument = runnableDocument;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
            return true;
        }

        private List<string> GetTargetUnitNames()
        {
            if (cmbUnit.SelectedIndex > 0)
                return new List<string>
                {
                    Convert.ToString(cmbUnit.SelectedItem)
                };
            return _resolvedUnitInputs.Keys
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private bool AreTargetInputsReady()
        {
            List<string> targets = GetTargetUnitNames();
            if (targets.Count == 0) return false;
            foreach (string name in targets)
            {
                LauncherUnitInput unit;
                string note;
                if (!_resolvedUnitInputs.TryGetValue(name, out unit) ||
                    !unit.Validate(out note))
                    return false;
            }
            return _inputMappingLoadError.Length == 0;
        }

        private void UpdateRunButtonAvailability()
        {
            if (btnRun == null) return;
            btnRun.Enabled = !_isRunning && _environmentPassed &&
                txtProject != null &&
                !string.IsNullOrWhiteSpace(txtProject.Text) &&
                AreTargetInputsReady();
        }

        private string BuildInputMappingContextKey()
        {
            if (!IsSmartInputMapping || _inputMappingDocument == null)
                return "";
            return "INPUT_MAP\n" + _inputMappingPath.ToUpperInvariant() +
                "\n" + _inputMappingDocument.BuildFingerprint();
        }

        private void AppendInputMappingArgument(StringBuilder builder)
        {
            if (!IsSmartInputMapping) return;
            builder.Append(" --input-map ");
            builder.Append(Quote(Path.GetFullPath(_inputMappingPath)));
        }

        private void AppendInputMappingLog()
        {
            if (IsSmartInputMapping)
                AppendLog("INPUT_MAP enabled path=" +
                    Path.GetFullPath(_inputMappingPath));
            else
                AppendLog("INPUT_MAP disabled legacy_input=true");
        }
    }
}
