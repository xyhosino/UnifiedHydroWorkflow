using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace UnifiedHydroLauncher
{
    public sealed partial class MainForm
    {
        private Label lblEnvironment;
        // Kept as a non-visible compatibility field for the historical V4/V5 tests.
        private TextBox txtEnvironmentDetails;
        private Button btnRuntime;
        private Button btnEnvironmentDetails;
        private string _environmentDetails = "";
        private string _runtimeDirectory = "";
        private bool _environmentPassed;
        private bool _isRunning;
        private readonly EnvironmentChecker _environmentChecker = new EnvironmentChecker();

        private void BuildEnvironmentPanel(TableLayoutPanel rootPanel)
        {
            var panel = new TableLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.ColumnCount = 4;
            panel.RowCount = 1;
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165F));
            rootPanel.Controls.Add(panel, 0, 2);

            txtEnvironmentDetails = new TextBox();
            txtEnvironmentDetails.ReadOnly = true;

            lblEnvironment = new Label();
            lblEnvironment.Dock = DockStyle.Fill;
            lblEnvironment.TextAlign = ContentAlignment.MiddleLeft;
            lblEnvironment.AutoEllipsis = true;
            panel.Controls.Add(lblEnvironment, 0, 0);

            var recheck = NewButton("重新检查");
            recheck.Click += delegate { CheckEnvironment(true); };
            panel.Controls.Add(recheck, 1, 0);

            btnEnvironmentDetails = NewButton("查看详情");
            btnEnvironmentDetails.Click += ShowEnvironmentDetails;
            panel.Controls.Add(btnEnvironmentDetails, 2, 0);

            btnRuntime = NewButton("打开 Runtime 文件夹");
            btnRuntime.Visible = false;
            btnRuntime.Click += OpenRuntimeFolder;
            panel.Controls.Add(btnRuntime, 3, 0);
        }

        private void EnvironmentPathChanged(object sender, EventArgs e)
        {
            CheckEnvironment(false);
        }

        private bool CheckEnvironment()
        {
            return CheckEnvironment(true);
        }

        private bool CheckEnvironment(bool logDetails)
        {
            _deployment = DeploymentLayout.Detect(AppDomain.CurrentDomain.BaseDirectory);
            if (txtExe != null)
            {
                string automaticCorePath = _deployment == null
                    ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UnifiedHydroWorkflow.exe")
                    : _deployment.WorkflowSourcePath;
                if (!string.Equals(txtExe.Text, automaticCorePath,
                        StringComparison.OrdinalIgnoreCase))
                    txtExe.Text = automaticCorePath;
            }

            if (lblModelEnvironment != null)
            {
                if (_deployment != null && _deployment.IsDetected)
                {
                    lblModelEnvironment.Text = "✓ 已识别  " + _deployment.ModelRoot;
                    lblModelEnvironment.ForeColor = Color.DarkGreen;
                }
                else
                {
                    lblModelEnvironment.Text = "✕ 未识别原水文模型根目录";
                    lblModelEnvironment.ForeColor = Color.Firebrick;
                }
            }

            EnvironmentCheckResult result = _environmentChecker.Check(
                _deployment,
                txtUnitRoot == null ? "" : txtUnitRoot.Text);
            _environmentPassed = result.Passed;
            _runtimeDirectory = result.RuntimeDirectory;

            if (result.Passed)
            {
                lblEnvironment.Text = "运行环境：✓ 正常" +
                    (_deployment != null && _deployment.IsNestedDeployment
                        ? "    模型根目录：" + _deployment.ModelRoot
                        : "");
                lblEnvironment.ForeColor = Color.DarkGreen;
            }
            else if (result.Pending)
            {
                lblEnvironment.Text = "运行环境：○ 待配置";
                lblEnvironment.ForeColor = Color.DarkGoldenrod;
            }
            else
            {
                lblEnvironment.Text = "运行环境：✕ " + result.FatalCount + " 项异常";
                lblEnvironment.ForeColor = Color.Firebrick;
            }

            _environmentDetails = string.Join(Environment.NewLine, result.Details.ToArray());
            txtEnvironmentDetails.Text = _environmentDetails;
            btnRuntime.Visible = _runtimeDirectory.Length > 0;
            UpdateRunButtonAvailability();
            if (logDetails || !result.Passed)
            {
                foreach (string line in result.Log) AppendLog(line);
            }
            else
            {
                AppendLog(result.Passed ? "ENV_RECHECK OK" : "ENV_RECHECK FAILED");
            }
            return result.Passed;
        }

        private void ShowEnvironmentDetails(object sender, EventArgs e)
        {
            using (var dialog = new Form())
            {
                dialog.Text = "运行环境详情";
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.Size = new Size(760, 520);
                dialog.MinimumSize = new Size(620, 400);
                dialog.Font = Font;

                var text = new TextBox();
                text.Dock = DockStyle.Fill;
                text.Multiline = true;
                text.ReadOnly = true;
                text.ScrollBars = ScrollBars.Both;
                text.WordWrap = false;
                text.Text = _environmentDetails;
                dialog.Controls.Add(text);

                var close = new Button();
                close.Text = "关闭";
                close.Dock = DockStyle.Bottom;
                close.Height = 38;
                close.Click += delegate { dialog.Close(); };
                dialog.Controls.Add(close);

                dialog.ShowDialog(this);
            }
        }

        private void OpenRuntimeFolder(object sender, EventArgs e)
        {
            CheckEnvironment(false);
            if (_runtimeDirectory.Length == 0) return;
            try { Process.Start("explorer.exe", Quote(_runtimeDirectory)); }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "无法打开 Runtime 文件夹",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
