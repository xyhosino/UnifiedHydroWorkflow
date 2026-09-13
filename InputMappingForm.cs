using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace UnifiedHydroLauncher
{
    internal sealed class InputMappingForm : Form
    {
        private readonly string unitRoot;
        private readonly string stationMode;
        private readonly string stationName;
        private readonly string rainfallMode;
        private readonly string rainfallName;
        private readonly string stationExternalRoot;
        private readonly string rainfallExternalRoot;
        private List<LauncherUnitInput> units;
        private DataGridView grid;
        private bool _loadingGrid;

        internal LauncherInputMappingDocument Document { get; private set; }
        internal string MappingPath { get; private set; }

        internal InputMappingForm(
            string root,
            IEnumerable<LauncherUnitInput> inputUnits,
            string selectedStationMode,
            string selectedStationName,
            string selectedRainfallMode,
            string selectedRainfallName,
            string selectedStationExternalRoot,
            string selectedRainfallExternalRoot)
        {
            unitRoot = Path.GetFullPath(root);
            stationMode = selectedStationMode;
            stationName = selectedStationName;
            rainfallMode = selectedRainfallMode;
            rainfallName = selectedRainfallName;
            stationExternalRoot = selectedStationExternalRoot ?? "";
            rainfallExternalRoot = selectedRainfallExternalRoot ?? "";
            units = inputUnits.Select(unit => unit.Clone()).ToList();
            MappingPath = Path.Combine(unitRoot, "UnifiedHydroInputMapping.xml");

            Text = "输入数据映射";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1450, 720);
            MinimumSize = new Size(1100, 560);
            Font = new Font("Microsoft YaHei UI", 9F);
            BuildUi();
            PopulateGrid();
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(10);
            root.RowCount = 3;
            root.ColumnCount = 1;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));
            Controls.Add(root);

            var tip = new Label();
            tip.AutoSize = true;
            tip.Text = "自动识别以 DBF 字段为主；有歧义时选择单元和字段，再点击“浏览当前字段”。";
            root.Controls.Add(tip, 0, 0);

            grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.RowHeadersVisible = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.DataError += delegate { };
            grid.CellValueChanged += Grid_CellValueChanged;
            root.Controls.Add(grid, 0, 1);

            AddTextColumn("Unit", "计算单元", 150, true);
            AddTextColumn(InputRoles.Watershed, "流域", 145, false);
            AddTextColumn(InputRoles.River, "河道", 145, false);
            AddTextColumn(InputRoles.Node, "节点", 145, false);
            AddTextColumn(InputRoles.Land, "土地利用", 145, false);
            AddTextColumn(InputRoles.Soil, "土壤", 145, false);
            AddTextColumn(InputRoles.Station, "站点", 155, false);
            AddTextColumn(InputRoles.Rainfall, "降雨", 155, false);
            AddTextColumn("Status", "状态", 85, true);
            AddTextColumn("Note", "说明", 260, true);

            var buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.Padding = new Padding(0, 7, 0, 0);
            root.Controls.Add(buttons, 0, 2);

            var cancel = NewButton("取消", 90);
            cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(cancel);
            var save = NewButton("保存并启用", 120);
            save.Click += Save_Click;
            buttons.Controls.Add(save);
            var browse = NewButton("浏览当前字段...", 135);
            browse.Click += Browse_Click;
            buttons.Controls.Add(browse);
            var rediscover = NewButton("重新自动识别", 120);
            rediscover.Click += Rediscover_Click;
            buttons.Controls.Add(rediscover);
            AcceptButton = save;
            CancelButton = cancel;
        }

        private static Button NewButton(string text, int width)
        {
            var button = new Button();
            button.Text = text;
            button.Width = width;
            button.Height = 30;
            button.Margin = new Padding(8, 0, 0, 0);
            return button;
        }

        private void AddTextColumn(
            string name, string header, int width, bool readOnly)
        {
            var column = new DataGridViewTextBoxColumn();
            column.Name = name;
            column.HeaderText = header;
            column.Width = width;
            column.ReadOnly = readOnly;
            grid.Columns.Add(column);
        }

        private void PopulateGrid()
        {
            _loadingGrid = true;
            try
            {
                grid.Rows.Clear();
                foreach (LauncherUnitInput unit in units
                    .OrderBy(value => value.UnitName, StringComparer.OrdinalIgnoreCase))
                {
                    int index = grid.Rows.Add();
                    DataGridViewRow row = grid.Rows[index];
                    row.Tag = unit;
                    row.Cells["Unit"].Value = unit.UnitName;
                    foreach (string role in InputRoles.All)
                    {
                        var cell = new DataGridViewComboBoxCell();
                        cell.FlatStyle = FlatStyle.Flat;
                        cell.Items.Add("");
                        foreach (string candidate in unit.GetCandidates(role))
                            AddChoice(cell, ToDisplayValue(unit, candidate));
                        string selected = ToDisplayValue(unit, unit.GetPath(role));
                        AddChoice(cell, selected);
                        cell.Value = selected;
                        row.Cells[role] = cell;
                    }
                }
            }
            finally
            {
                _loadingGrid = false;
            }
            UpdateAllStatuses();
        }

        private void Grid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_loadingGrid) return;
            UpdateAllStatuses();
        }

        private static void AddChoice(DataGridViewComboBoxCell cell, string value)
        {
            if (value.Length > 0 && !cell.Items.Contains(value))
                cell.Items.Add(value);
        }

        private static string ToDisplayValue(
            LauncherUnitInput unit, string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return "";
            return LauncherInputMappingDocument.ToPortablePath(
                unit.UnitFolder, fullPath);
        }

        private static string ResolveChoice(
            LauncherUnitInput unit, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            return Path.GetFullPath(Path.IsPathRooted(value)
                ? value
                : Path.Combine(unit.UnitFolder, value));
        }

        private void ApplyRow(DataGridViewRow row)
        {
            var unit = (LauncherUnitInput)row.Tag;
            foreach (string role in InputRoles.All)
                unit.SetPath(role, ResolveChoice(
                    unit,
                    Convert.ToString(row.Cells[role].Value)));
        }

        private void UpdateAllStatuses()
        {
            if (_loadingGrid || grid == null || grid.IsCurrentCellInEditMode) return;
            foreach (DataGridViewRow row in grid.Rows)
            {
                ApplyRow(row);
                var unit = (LauncherUnitInput)row.Tag;
                string note;
                bool ready = unit.Validate(out note);
                bool ambiguous = !ready && InputRoles.All.Any(role =>
                    unit.GetPath(role).Length == 0 &&
                    unit.GetCandidates(role).Count > 1);
                row.Cells["Status"].Value = ready
                    ? "就绪"
                    : ambiguous ? "待确认" : "缺失";
                row.Cells["Note"].Value = ready
                    ? ""
                    : CombineNotes(note, unit.RecognitionNote);
                row.DefaultCellStyle.BackColor = ready
                    ? Color.Honeydew
                    : ambiguous ? Color.LightYellow : Color.MistyRose;
            }
        }

        private static string CombineNotes(params string[] values)
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

        private void Browse_Click(object sender, EventArgs e)
        {
            if (grid.CurrentCell == null) return;
            string role = grid.Columns[grid.CurrentCell.ColumnIndex].Name;
            if (!InputRoles.All.Contains(role))
            {
                MessageBox.Show(this, "请先选择一个输入文件字段。",
                    "输入数据映射", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var unit = (LauncherUnitInput)grid.Rows[grid.CurrentCell.RowIndex].Tag;
            using (var dialog = new OpenFileDialog())
            {
                string currentChoice = Convert.ToString(grid.CurrentCell.Value);
                string currentPath = ResolveChoice(unit, currentChoice);
                string initialDirectory = unit.UnitFolder;

                if (!string.IsNullOrWhiteSpace(currentPath))
                {
                    try
                    {
                        string currentDirectory = Path.GetDirectoryName(currentPath);
                        if (!string.IsNullOrWhiteSpace(currentDirectory) &&
                            Directory.Exists(currentDirectory))
                        {
                            initialDirectory = currentDirectory;
                            if (File.Exists(currentPath))
                                dialog.FileName = Path.GetFileName(currentPath);
                        }
                    }
                    catch
                    {
                    }
                }

                dialog.InitialDirectory = initialDirectory;
                dialog.Filter = InputRoles.IsShape(role)
                    ? "Shapefile (*.shp)|*.shp"
                    : "Excel (*.xls;*.xlsx)|*.xls;*.xlsx";
                dialog.Title = "选择" + LauncherUnitInput.RoleCaption(role) + "文件";
                dialog.CheckFileExists = true;
                dialog.RestoreDirectory = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var cell = grid.CurrentCell as DataGridViewComboBoxCell;
                string value = ToDisplayValue(unit, dialog.FileName);
                AddChoice(cell, value);
                cell.Value = value;
                UpdateAllStatuses();
            }
        }

        private void Rediscover_Click(object sender, EventArgs e)
        {
            units = Directory.GetDirectories(unitRoot)
                .Select(folder => InputDiscovery.Discover(
                    folder,
                    stationMode,
                    stationName,
                    rainfallMode,
                    rainfallName,
                    stationExternalRoot,
                    rainfallExternalRoot))
                .ToList();
            PopulateGrid();
        }

        private void Save_Click(object sender, EventArgs e)
        {
            grid.EndEdit();
            UpdateAllStatuses();
            var invalid = new List<string>();
            var document = new LauncherInputMappingDocument();
            foreach (DataGridViewRow row in grid.Rows)
            {
                ApplyRow(row);
                var unit = (LauncherUnitInput)row.Tag;
                string note;
                if (!unit.Validate(out note))
                    invalid.Add(unit.UnitName + "：" + note);
                document.Add(unit.Clone());
            }
            if (invalid.Count > 0)
            {
                MessageBox.Show(this,
                    "仍有未完成或无效的输入：\r\n" +
                    string.Join("\r\n", invalid.Take(12).ToArray()),
                    "无法启用输入映射",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
            try
            {
                document.Save(MappingPath);
                Document = document;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "保存输入映射失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
