using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace QuietType
{
    public sealed class HistoryDialog : Form
    {
        private readonly float scale;
        private readonly bool english;

        public HistoryDialog(UserStore store) : this(store, false) { }

        public HistoryDialog(UserStore store, bool english)
        {
            if (store == null) throw new ArgumentNullException("store");
            this.english = english;
            using (Graphics graphics = CreateGraphics()) scale = graphics.DpiX / 96F;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Microsoft YaHei UI", 9F);
            Text = T("练习记录", "Practice history");
            BackColor = Color.White;
            ForeColor = Color.FromArgb(35, 35, 35);
            ClientSize = new Size(S(1000), S(520));
            MinimumSize = new Size(S(800), S(380));
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false; MinimizeBox = false;
            Padding = new Padding(S(14));

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill; root.ColumnCount = 1; root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, S(31)));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, S(42)));
            Controls.Add(root);
            Label summary = new Label();
            summary.Dock = DockStyle.Fill; summary.TextAlign = ContentAlignment.MiddleLeft;
            summary.ForeColor = Color.DimGray;
            string number = store.Data.Results.Count.ToString(CultureInfo.CurrentCulture);
            summary.Text = store.Data.Results.Count == 0 ? T("还没有练习记录", "No practice history yet")
                : T("最近 " + number + " 次练习", "Recent sessions: " + number);
            root.Controls.Add(summary, 0, 0);

            DataGridView grid = new DataGridView();
            grid.Name = "HistoryRecords";
            grid.Dock = DockStyle.Fill;
            grid.ReadOnly = true; grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false; grid.AutoGenerateColumns = false;
            grid.MultiSelect = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.RowHeadersVisible = false; grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.GridColor = Color.FromArgb(225, 225, 225);
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersHeight = S(32); grid.RowTemplate.Height = S(29);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(232, 237, 242);
            grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(25, 25, 25);
            grid.DefaultCellStyle.Padding = new Padding(S(4), 0, S(4), 0);
            AddColumn(grid, T("日期", "Date"), 148, false);
            AddColumn(grid, T("模式", "Mode"), english ? 180 : 143, false);
            AddColumn(grid, T("文本", "Text"), 180, true);
            AddColumn(grid, T("速度 · 字/分", "Chars/min"), 105, false);
            AddColumn(grid, T("准确率", "Accuracy"), english ? 95 : 85, false);
            AddColumn(grid, T("得分", "Score"), 75, false);
            AddColumn(grid, T("结果", "Result"), english ? 170 : 112, false);
            foreach (ScoreRecord item in store.Data.Results)
            {
                if (item == null) continue;
                string mode = Ui.RecordText(item.Section ?? "", english);
                if (!String.IsNullOrEmpty(item.LengthMode)) mode += (mode.Length > 0 ? " / " : "") + Ui.RecordText(item.LengthMode, english);
                string title = english && !String.IsNullOrEmpty(item.TitleEn) ? item.TitleEn : Ui.RecordText(item.Title, english);
                grid.Rows.Add(FormatDate(item.WhenIso), mode, title, item.Cpm.ToString("0.0", CultureInfo.CurrentCulture),
                    item.Accuracy.ToString("0.0", CultureInfo.CurrentCulture) + "%", item.Score.ToString(CultureInfo.CurrentCulture), Ui.RecordText(item.Result ?? "", english));
            }
            if (grid.Rows.Count > 0) grid.ClearSelection();
            root.Controls.Add(grid, 0, 1);

            FlowLayoutPanel footer = new FlowLayoutPanel();
            footer.Dock = DockStyle.Fill; footer.FlowDirection = FlowDirection.RightToLeft;
            footer.Padding = new Padding(0, S(10), 0, 0); footer.Margin = Padding.Empty;
            Button close = new Button { Text = T("关闭", "Close"), Width = S(84), Height = S(29), DialogResult = DialogResult.Cancel,
                UseVisualStyleBackColor = true, Margin = Padding.Empty };
            footer.Controls.Add(close); root.Controls.Add(footer, 0, 2);
            CancelButton = close;
        }

        private string T(string chinese, string translated) { return Ui.Pick(english, chinese, translated); }
        private int S(int value) { return (int)Math.Round(value * scale); }
        private void AddColumn(DataGridView grid, string label, int width, bool fill)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
            column.HeaderText = label; column.Name = "column" + grid.Columns.Count.ToString(CultureInfo.InvariantCulture);
            column.Width = S(width); column.MinimumWidth = S(fill ? 100 : 65);
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            if (fill) column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            grid.Columns.Add(column);
        }
        private static string FormatDate(string value)
        {
            DateTime date;
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out date)
                ? date.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture) : (value ?? "");
        }
    }
}
