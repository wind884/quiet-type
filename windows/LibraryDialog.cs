using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace QuietType
{
    public sealed class LibraryDialog : Form
    {
        private readonly UserStore store;
        private readonly bool english;
        private readonly float scale;
        private readonly ListBox texts = new ListBox();
        private readonly TextBox title = new TextBox();
        private readonly ComboBox language = new ComboBox();
        private readonly TextBox content = new TextBox();
        private readonly Label count = new Label();
        private readonly Label status = new Label();
        private bool loading;
        private bool dirty;
        private string editingId = "";
        private string originalLanguage = "中文";
        private string originalLanguageLabel = "中文";

        private sealed class LanguageOption
        {
            public string Raw, Label;
            public override string ToString() { return Label; }
        }

        public LibraryDialog(UserStore store) : this(store, false) { }

        public LibraryDialog(UserStore store, bool english)
        {
            if (store == null) throw new ArgumentNullException("store");
            this.store = store;
            this.english = english;
            using (Graphics graphics = CreateGraphics()) scale = graphics.DpiX / 96F;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Microsoft YaHei UI", 9F);
            Text = T("自定义文本", "My texts");
            BackColor = Color.White;
            ForeColor = Color.FromArgb(35, 35, 35);
            ClientSize = new Size(S(920), S(590));
            MinimumSize = new Size(S(760), S(470));
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            Padding = new Padding(S(14));

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill; root.ColumnCount = 1; root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, S(31)));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, S(39)));
            Controls.Add(root);

            TableLayoutPanel work = new TableLayoutPanel();
            work.Dock = DockStyle.Fill; work.ColumnCount = 2; work.RowCount = 1;
            work.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(english ? 280 : 245)));
            work.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.Controls.Add(work, 0, 0);

            TableLayoutPanel library = new TableLayoutPanel();
            library.Dock = DockStyle.Fill; library.ColumnCount = 1; library.RowCount = 3;
            library.RowStyles.Add(new RowStyle(SizeType.Absolute, S(27)));
            library.RowStyles.Add(new RowStyle(SizeType.Absolute, S(38)));
            library.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            library.Margin = new Padding(0, 0, S(14), 0);
            library.Controls.Add(Caption(T("文本库", "Text library")), 0, 0);
            FlowLayoutPanel tools = new FlowLayoutPanel();
            tools.Dock = DockStyle.Fill; tools.WrapContents = false; tools.Margin = Padding.Empty;
            tools.Controls.Add(Button(T("新建", "New"), delegate { NewText(); }, 62));
            tools.Controls.Add(Button(T("导入 TXT", "Import TXT"), delegate { ImportText(); }, english ? 100 : 82));
            tools.Controls.Add(Button(T("删除", "Delete"), delegate { DeleteText(); }, english ? 72 : 62));
            library.Controls.Add(tools, 0, 1);
            texts.Dock = DockStyle.Fill; texts.IntegralHeight = false;
            texts.BorderStyle = BorderStyle.FixedSingle;
            texts.HorizontalScrollbar = true;
            texts.FormattingEnabled = true;
            texts.Format += delegate(object sender, ListControlConvertEventArgs e) {
                SavedText item = e.ListItem as SavedText;
                if (item != null) e.Value = Ui.Language(item.Language, english) + " / " + item.Title;
            };
            texts.SelectedIndexChanged += SelectionChanged;
            library.Controls.Add(texts, 0, 2);
            work.Controls.Add(library, 0, 0);

            TableLayoutPanel editor = new TableLayoutPanel();
            editor.Dock = DockStyle.Fill; editor.ColumnCount = 1; editor.RowCount = 7;
            editor.Margin = Padding.Empty;
            editor.RowStyles.Add(new RowStyle(SizeType.Absolute, S(27)));
            editor.RowStyles.Add(new RowStyle(SizeType.Absolute, S(38)));
            editor.RowStyles.Add(new RowStyle(SizeType.Absolute, S(27)));
            editor.RowStyles.Add(new RowStyle(SizeType.Absolute, S(38)));
            editor.RowStyles.Add(new RowStyle(SizeType.Absolute, S(27)));
            editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            editor.RowStyles.Add(new RowStyle(SizeType.Absolute, S(27)));
            editor.Controls.Add(Caption(T("标题", "Title")), 0, 0);
            title.Dock = DockStyle.Top; title.MaxLength = 120;
            title.TextChanged += Edited;
            editor.Controls.Add(title, 0, 1);
            editor.Controls.Add(Caption(T("语言，可直接输入自定义语言", "Language (or type your own)")), 0, 2);
            language.Dock = DockStyle.Top; language.DropDownStyle = ComboBoxStyle.DropDown;
            language.MaxLength = 60;
            foreach (string raw in new string[] { "中文", "英语", "日本語", "한국어", "Español", "Français", "Deutsch", "Русский" })
                language.Items.Add(new LanguageOption { Raw = raw, Label = Ui.Language(raw, english) });
            language.TextChanged += Edited;
            editor.Controls.Add(language, 0, 3);
            editor.Controls.Add(Caption(T("正文", "Text")), 0, 4);
            content.Multiline = true; content.Dock = DockStyle.Fill;
            content.AcceptsReturn = true; content.AcceptsTab = true;
            content.ScrollBars = ScrollBars.Vertical; content.WordWrap = true;
            content.BorderStyle = BorderStyle.FixedSingle;
            content.Font = new Font("Microsoft YaHei UI", 10F);
            content.MaxLength = 0;
            content.TextChanged += Edited;
            editor.Controls.Add(content, 0, 5);
            count.Dock = DockStyle.Fill; count.TextAlign = ContentAlignment.MiddleRight;
            count.ForeColor = Color.DimGray;
            editor.Controls.Add(count, 0, 6);
            work.Controls.Add(editor, 1, 0);

            status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft;
            status.AutoEllipsis = true; status.ForeColor = Color.DimGray;
            root.Controls.Add(status, 0, 1);
            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Fill; actions.FlowDirection = FlowDirection.RightToLeft;
            actions.WrapContents = false; actions.Margin = Padding.Empty;
            Button cancel = Button(T("取消", "Cancel"), null, 80);
            cancel.DialogResult = DialogResult.Cancel;
            Button use = Button(T("保存并使用", "Save and use"), delegate {
                if (SaveEditor()) { SelectedId = editingId; DialogResult = DialogResult.OK; Close(); }
            }, english ? 132 : 108);
            Button save = Button(T("保存", "Save"), delegate { SaveEditor(); }, 80);
            actions.Controls.Add(cancel); actions.Controls.Add(use); actions.Controls.Add(save);
            root.Controls.Add(actions, 0, 2);
            CancelButton = cancel;
            FormClosing += ClosingEditor;

            RefreshList(null);
            if (store.Data.Texts.Count > 0) SetEditor(store.Data.Texts[0]);
            else SetEditor(null);
        }

        public string SelectedId { get; private set; }

        private string T(string chinese, string translated) { return Ui.Pick(english, chinese, translated); }
        private int S(int value) { return (int)Math.Round(value * scale); }
        private Label Caption(string text)
        {
            return new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(85, 85, 85), Margin = Padding.Empty };
        }
        private Button Button(string text, EventHandler click, int width)
        {
            Button button = new Button { Text = text, Width = S(width), Height = S(29), UseVisualStyleBackColor = true,
                Margin = new Padding(0, 0, S(5), 0) };
            if (click != null) button.Click += click;
            return button;
        }
        private void Edited(object sender, EventArgs e)
        {
            if (loading) return;
            dirty = true; UpdateCount(); status.Text = T("有未保存的修改", "Unsaved changes");
        }
        private void UpdateCount()
        {
            int elements = StringInfo.ParseCombiningCharacters(content.Text).Length;
            count.Text = elements.ToString("N0", CultureInfo.CurrentCulture) + T(" / 40,000 字符", " / 40,000 characters");
            count.ForeColor = elements > 40000 ? Color.Firebrick : Color.DimGray;
        }
        private void RefreshList(string selectedId)
        {
            loading = true;
            try
            {
                texts.BeginUpdate(); texts.Items.Clear();
                foreach (SavedText item in store.Data.Texts) texts.Items.Add(item);
                texts.SelectedIndex = -1;
                for (int i = 0; i < texts.Items.Count; i++)
                {
                    SavedText item = (SavedText)texts.Items[i];
                    if (item.Id == selectedId) { texts.SelectedIndex = i; break; }
                }
                texts.EndUpdate();
            }
            finally { loading = false; }
        }
        private void SetEditor(SavedText item)
        {
            loading = true;
            try
            {
                editingId = item == null ? "" : item.Id;
                title.Text = item == null ? "" : item.Title;
                originalLanguage = item == null ? "中文" : item.Language;
                originalLanguageLabel = Ui.Language(originalLanguage, english);
                language.Text = originalLanguageLabel;
                content.Text = item == null ? "" : item.Text;
            }
            finally { loading = false; }
            RefreshList(editingId); dirty = false; UpdateCount();
            status.Text = item == null ? T("新建文本后保存即可用于练习", "Save a new text to use it for practice")
                : T("编辑后保存，或直接保存并使用", "Edit and save, or choose Save and use");
        }
        private void SelectionChanged(object sender, EventArgs e)
        {
            if (loading || texts.SelectedItem == null) return;
            SavedText target = (SavedText)texts.SelectedItem;
            if (target.Id == editingId) return;
            if (!ResolveEdits()) { RefreshList(editingId); return; }
            SetEditor(target);
        }
        private bool ResolveEdits()
        {
            if (!dirty) return true;
            DialogResult answer = Prompt(T("保存对当前文本的修改", "Save changes to this text?"),
                T("未保存的修改", "Unsaved changes"), T("保存", "Save"), true, true);
            if (answer == DialogResult.Cancel) return false;
            return answer != DialogResult.Yes || SaveEditor();
        }
        private void NewText()
        {
            if (!ResolveEdits()) return;
            SetEditor(null); title.Focus();
        }
        private bool SaveEditor()
        {
            string normalized;
            try { normalized = PracticeText.Normalize(content.Text, false); }
            catch (ArgumentException ex) { status.Text = Ui.Error(ex.Message, english); return false; }
            if (String.IsNullOrWhiteSpace(title.Text) || String.IsNullOrWhiteSpace(language.Text)
                || String.IsNullOrWhiteSpace(normalized))
            {
                ShowError(T("请填写标题、语言和正文", "Enter a title, language and text")); return false;
            }
            if (StringInfo.ParseCombiningCharacters(normalized).Length > 40000)
            {
                ShowError(T("正文最多 40000 个字符，请缩短后保存", "Shorten the text to 40,000 characters before saving")); return false;
            }
            SavedText saved = new SavedText { Id = editingId, Title = title.Text.Trim(), Language = CanonicalLanguage(), Text = normalized };
            bool success = store.SaveText(saved);
            editingId = saved.Id;
            RefreshList(editingId);
            if (!success)
            {
                dirty = true; ShowError(Ui.Error(store.LastError, english)); return false;
            }
            SetEditor(saved); status.Text = T("已保存到本机", "Saved on this computer");
            return true;
        }
        private string CanonicalLanguage()
        {
            string value = language.Text.Trim();
            if (value == originalLanguageLabel) return originalLanguage;
            foreach (LanguageOption option in language.Items)
                if (value == option.Raw || String.Equals(value, option.Label, StringComparison.OrdinalIgnoreCase)) return option.Raw;
            return value;
        }
        private void DeleteText()
        {
            SavedText selected = texts.SelectedItem as SavedText;
            if (selected == null) { ShowError(T("请先选择一篇已保存的文本", "Select a saved text first")); return; }
            string question = english ? "Delete \"" + selected.Title + "\"?" : "删除「" + selected.Title + "」";
            if (Prompt(question, T("删除文本", "Delete text"), T("删除", "Delete"), false, true) != DialogResult.Yes) return;
            bool success = store.DeleteText(selected.Id);
            SetEditor(store.Data.Texts.Count == 0 ? null : store.Data.Texts[0]);
            if (!success) ShowError(Ui.Error(store.LastError, english)); else status.Text = T("已删除文本", "Text deleted");
        }
        private void ImportText()
        {
            using (OpenFileDialog picker = new OpenFileDialog())
            {
                picker.Title = T("导入文本", "Import text"); picker.Filter = T("文本文件 (*.txt)|*.txt", "Text files (*.txt)|*.txt");
                picker.CheckFileExists = true; picker.Multiselect = false;
                picker.RestoreDirectory = true;
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                string imported;
                try
                {
                    using (FileStream stream = new FileStream(picker.FileName, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        if (stream.Length > 128L * 1024) { ShowError(T("文本文件最多 128 KB", "Text files can be up to 128 KB")); return; }
                        using (StreamReader reader = new StreamReader(stream, new UTF8Encoding(false, true), true)) imported = reader.ReadToEnd();
                    }
                }
                catch (DecoderFallbackException) { ShowError(T("请使用 UTF-8 或带编码标记的 Unicode 文本文件", "Use UTF-8 or a Unicode text file with an encoding marker")); return; }
                catch (IOException) { ShowError(T("无法读取文本文件，请检查文件是否被占用", "Cannot read the text file; check whether another app is using it")); return; }
                catch (UnauthorizedAccessException) { ShowError(T("没有读取该文本文件的权限", "You do not have permission to read this text file")); return; }
                string normalized;
                try { normalized = PracticeText.Normalize(imported, false); }
                catch (ArgumentException ex) { status.Text = Ui.Error(ex.Message, english); return; }
                if (String.IsNullOrWhiteSpace(normalized)) { ShowError(T("文本文件中没有可练习的内容", "The file contains no text to practise")); return; }
                if (StringInfo.ParseCombiningCharacters(normalized).Length > 40000) { ShowError(T("正文最多 40000 个字符", "Text can contain up to 40,000 characters")); return; }
                if (!ResolveEdits()) return;
                SetEditor(null);
                loading = true;
                try { title.Text = Path.GetFileNameWithoutExtension(picker.FileName); content.Text = imported; }
                finally { loading = false; }
                dirty = true; UpdateCount(); status.Text = T("文本已导入，保存后可用于练习", "Text imported; save it to use it for practice");
            }
        }
        private void ShowError(string text)
        {
            status.Text = text;
            Prompt(text, T("自定义文本", "My texts"), T("确定", "OK"), false, false);
        }
        private DialogResult Prompt(string message, string caption, string affirmative, bool offerDiscard, bool offerCancel)
        {
            using (Form prompt = new Form())
            {
                prompt.Text = caption; prompt.Font = Font; prompt.BackColor = Color.White; prompt.ForeColor = ForeColor;
                prompt.AutoScaleMode = AutoScaleMode.None; prompt.StartPosition = FormStartPosition.CenterParent;
                prompt.FormBorderStyle = FormBorderStyle.FixedDialog; prompt.MinimizeBox = false; prompt.MaximizeBox = false;
                prompt.ShowInTaskbar = false; prompt.ClientSize = new Size(S(470), S(164)); prompt.Padding = new Padding(S(16));
                TableLayoutPanel panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
                panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                panel.RowStyles.Add(new RowStyle(SizeType.Absolute, S(34)));
                panel.Controls.Add(new Label { Text = message, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                    AutoEllipsis = true, Margin = Padding.Empty }, 0, 0);
                FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
                    WrapContents = false, Margin = Padding.Empty };
                if (offerCancel)
                {
                    Button cancel = Button(T("取消", "Cancel"), null, 94); cancel.DialogResult = DialogResult.Cancel;
                    buttons.Controls.Add(cancel); prompt.CancelButton = cancel;
                }
                if (offerDiscard)
                {
                    Button discard = Button(T("不保存", "Discard"), null, 94); discard.DialogResult = DialogResult.No;
                    buttons.Controls.Add(discard);
                }
                Button accept = Button(affirmative, null, 94); accept.DialogResult = DialogResult.Yes;
                buttons.Controls.Add(accept); prompt.AcceptButton = accept;
                if (!offerCancel) prompt.CancelButton = accept;
                panel.Controls.Add(buttons, 0, 1); prompt.Controls.Add(panel);
                return prompt.ShowDialog(this);
            }
        }
        private void ClosingEditor(object sender, FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.OK) return;
            if (!ResolveEdits()) { e.Cancel = true; DialogResult = DialogResult.None; }
        }
    }
}
