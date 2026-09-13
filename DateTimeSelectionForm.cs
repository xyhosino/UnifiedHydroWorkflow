using System;
using System.Drawing;
using System.Windows.Forms;

namespace UnifiedHydroLauncher
{
    internal sealed class DateTimeSelectionForm : Form
    {
        private readonly MonthCalendar calendar;
        private readonly NumericUpDown minute;
        private readonly NumericUpDown second;
        private readonly Button[] hourButtons = new Button[24];
        private int selectedHour;

        internal DateTime SelectedDateTime
        {
            get
            {
                DateTime date = calendar.SelectionStart.Date;
                return date.AddHours(selectedHour)
                    .AddMinutes(Decimal.ToInt32(minute.Value))
                    .AddSeconds(Decimal.ToInt32(second.Value));
            }
        }

        internal DateTimeSelectionForm(DateTime initial)
        {
            Text = "选择日期和时间";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(610, 330);

            selectedHour = initial.Hour;

            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(10);
            root.ColumnCount = 2;
            root.RowCount = 2;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 255F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            Controls.Add(root);

            calendar = new MonthCalendar();
            calendar.MaxSelectionCount = 1;
            calendar.ShowTodayCircle = true;
            calendar.SelectionStart = initial.Date;
            calendar.SelectionEnd = initial.Date;
            calendar.Dock = DockStyle.Fill;
            root.Controls.Add(calendar, 0, 0);

            var timeHost = new TableLayoutPanel();
            timeHost.Dock = DockStyle.Fill;
            timeHost.ColumnCount = 1;
            timeHost.RowCount = 3;
            timeHost.Padding = new Padding(8, 0, 0, 0);
            timeHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            timeHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            timeHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            root.Controls.Add(timeHost, 1, 0);

            var title = new Label();
            title.Text = "小时";
            title.Dock = DockStyle.Fill;
            title.TextAlign = ContentAlignment.MiddleLeft;
            title.Font = new Font(Font, FontStyle.Bold);
            timeHost.Controls.Add(title, 0, 0);

            var hours = new TableLayoutPanel();
            hours.Dock = DockStyle.Fill;
            hours.ColumnCount = 4;
            hours.RowCount = 6;
            for (int column = 0; column < 4; column++)
                hours.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            for (int row = 0; row < 6; row++)
                hours.RowStyles.Add(new RowStyle(SizeType.Percent, 16.666F));
            timeHost.Controls.Add(hours, 0, 1);

            for (int hour = 0; hour < 24; hour++)
            {
                var button = new Button();
                button.Text = hour.ToString("00") + ":00";
                button.Dock = DockStyle.Fill;
                button.Margin = new Padding(2);
                button.Tag = hour;
                button.Click += Hour_Click;
                hourButtons[hour] = button;
                hours.Controls.Add(button, hour % 4, hour / 4);
            }

            var minuteSecond = new TableLayoutPanel();
            minuteSecond.Dock = DockStyle.Fill;
            minuteSecond.ColumnCount = 4;
            minuteSecond.RowCount = 1;
            minuteSecond.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 45F));
            minuteSecond.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            minuteSecond.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 45F));
            minuteSecond.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            timeHost.Controls.Add(minuteSecond, 0, 2);

            var minuteLabel = new Label();
            minuteLabel.Text = "分钟";
            minuteLabel.Dock = DockStyle.Fill;
            minuteLabel.TextAlign = ContentAlignment.MiddleLeft;
            minuteSecond.Controls.Add(minuteLabel, 0, 0);

            minute = new NumericUpDown();
            minute.Dock = DockStyle.Fill;
            minute.Minimum = 0;
            minute.Maximum = 59;
            minute.Value = initial.Minute;
            minuteSecond.Controls.Add(minute, 1, 0);

            var secondLabel = new Label();
            secondLabel.Text = "秒";
            secondLabel.Dock = DockStyle.Fill;
            secondLabel.TextAlign = ContentAlignment.MiddleCenter;
            minuteSecond.Controls.Add(secondLabel, 2, 0);

            second = new NumericUpDown();
            second.Dock = DockStyle.Fill;
            second.Minimum = 0;
            second.Maximum = 59;
            second.Value = initial.Second;
            minuteSecond.Controls.Add(second, 3, 0);

            var actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.FlowDirection = FlowDirection.RightToLeft;
            actions.WrapContents = false;
            actions.Padding = new Padding(0, 7, 0, 0);
            root.Controls.Add(actions, 0, 1);
            root.SetColumnSpan(actions, 2);

            var cancel = new Button();
            cancel.Text = "取消";
            cancel.Width = 90;
            cancel.Height = 30;
            cancel.DialogResult = DialogResult.Cancel;
            actions.Controls.Add(cancel);

            var ok = new Button();
            ok.Text = "确定";
            ok.Width = 90;
            ok.Height = 30;
            ok.DialogResult = DialogResult.OK;
            actions.Controls.Add(ok);

            AcceptButton = ok;
            CancelButton = cancel;
            UpdateHourHighlight();
        }

        private void Hour_Click(object sender, EventArgs e)
        {
            Button button = sender as Button;
            if (button == null || !(button.Tag is int)) return;
            selectedHour = (int)button.Tag;
            UpdateHourHighlight();
        }

        private void UpdateHourHighlight()
        {
            for (int hour = 0; hour < hourButtons.Length; hour++)
            {
                Button button = hourButtons[hour];
                if (button == null) continue;
                if (hour == selectedHour)
                {
                    button.UseVisualStyleBackColor = false;
                    button.BackColor = SystemColors.Highlight;
                    button.ForeColor = SystemColors.HighlightText;
                }
                else
                {
                    button.BackColor = SystemColors.Control;
                    button.UseVisualStyleBackColor = true;
                    button.ForeColor = SystemColors.ControlText;
                }
            }
        }
    }
}
