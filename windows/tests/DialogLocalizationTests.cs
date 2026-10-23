using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using QuietType;

public static class DialogLocalizationTests
{
    private static int checks;
    [STAThread]
    public static int Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dialog-tests", Guid.NewGuid().ToString("N"));
            UserStore store = new UserStore(directory); store.Load();
            SavedText saved = new SavedText { Id = "dialog-text", Title = "标题保持原样", Language = "英语", Text = "Keep this exact article body." };
            store.Data.Texts.Add(saved);
            using (LibraryDialog dialog = new LibraryDialog(store, true))
            {
                Require(dialog.Text == "My texts", "English library heading");
                ComboBox language = Field<ComboBox>(dialog, "language");
                Require(language.Text == "English", "Canonical English has an English display label");
                foreach (object item in language.Items) Require(!Cjk(item.ToString()), "English language choice: " + item);
                foreach (Control control in Descendants(dialog))
                    if (control is Label || control is Button) Require(!Cjk(control.Text), "English library UI: " + control.Text);
                Require(Field<ListBox>(dialog, "texts").GetItemText(saved) == "English / 标题保持原样", "Only metadata in list is translated");
                Field<TextBox>(dialog, "title").Text = "修改后的标题";
                Require((bool)Call(dialog, "SaveEditor"), "English editor saves");
                Require(store.Data.Texts[0].Language == "英语", "Saving English label retains canonical metadata");
                Require(store.Data.Texts[0].Title == "修改后的标题" && store.Data.Texts[0].Text == saved.Text, "Article title and body remain content");
                Require(Field<Label>(dialog, "status").Text == "Saved on this computer", "Saved status is English");
                language.SelectedIndex = 0;
                Require((bool)Call(dialog, "SaveEditor"), "Chinese choice saves");
                Require(store.Data.Texts[0].Language == "中文", "Chinese display label is not persisted as English metadata");
                language.SelectedIndex = 2;
                Require((bool)Call(dialog, "SaveEditor"), "Japanese choice saves");
                Require(store.Data.Texts[0].Language == "日本語", "Japanese display label retains original canonical value");
                saved = store.Data.Texts[0]; saved.Language = "Custom language / 自定义";
                Call(dialog, "SetEditor", saved);
                Field<TextBox>(dialog, "title").Text = "Another title";
                Require((bool)Call(dialog, "SaveEditor"), "Custom language saves");
                Require(store.Data.Texts[0].Language == "Custom language / 自定义", "Custom language is retained exactly");
                saved = store.Data.Texts[0]; saved.Language = "English";
                Call(dialog, "SetEditor", saved);
                Require((bool)Call(dialog, "SaveEditor"), "Existing English literal saves");
                Require(store.Data.Texts[0].Language == "English", "Existing user metadata is not silently normalised");
                Field<TextBox>(dialog, "content").Text = "\uD800";
                Require(!(bool)Call(dialog, "SaveEditor"), "Invalid Unicode is rejected");
                Require(!Cjk(Field<Label>(dialog, "status").Text), "Engine error is translated");
                Call(dialog, "SetEditor", saved);
                CheckPrompt(dialog);
                CheckButtonsFit(dialog);
                Render(dialog, "LibraryEnglish.png");
                dialog.Size = dialog.MinimumSize; dialog.PerformLayout();
                foreach (Control control in Descendants(dialog))
                    if (control is TextBox && ((TextBox)control).Multiline) Require(control.Width > 250, "Minimum layout leaves useful text width");
                Render(dialog, "LibraryEnglishMin.png");
            }
            store.Data.Results.Add(new ScoreRecord { Section = "趣味挑战", LengthMode = "短篇速练", Result = "挑战完成", Title = "中文文章题目", WhenIso = "2026-10-22T12:00:00Z" });
            using (HistoryDialog dialog = new HistoryDialog(store, true))
            {
                Require(dialog.Text == "Practice history", "English history heading");
                foreach (Control control in Descendants(dialog))
                    if (control is Label || control is Button) Require(!Cjk(control.Text), "English history UI");
                DataGridView grid = Find<DataGridView>(dialog);
                foreach (DataGridViewColumn column in grid.Columns) Require(!Cjk(column.HeaderText), "English history column");
                Require((string)grid.Rows[0].Cells[1].Value == "Challenges / Short practice", "History mode translated");
                Require((string)grid.Rows[0].Cells[2].Value == "中文文章题目", "History article title preserved");
                Require((string)grid.Rows[0].Cells[6].Value == "Challenge complete", "History result translated");
                Require(TextRenderer.MeasureText((string)grid.Rows[0].Cells[6].Value, grid.Font).Width + 12 <= grid.Columns[6].Width, "Full English result fits the result column");
                Render(dialog, "HistoryEnglish.png");
            }
            using (LibraryDialog dialog = new LibraryDialog(store)) Require(dialog.Text == "自定义文本", "Original library constructor remains Chinese");
            using (HistoryDialog dialog = new HistoryDialog(store)) Require(dialog.Text == "练习记录", "Original history constructor remains Chinese");
            UserStore reloaded = new UserStore(directory); Require(reloaded.Load(), "Reload saved metadata");
            Require(reloaded.Data.Texts[0].Language == "English", "Metadata survives persistence unchanged");
            Console.WriteLine("Dialog localisation tests passed: " + checks);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void CheckPrompt(LibraryDialog owner)
    {
        Exception failure = null;
        using (Timer timer = new Timer())
        {
            timer.Interval = 120;
            timer.Tick += delegate {
                timer.Stop();
                try
                {
                    Form active = Form.ActiveForm;
                    Require(active != null && active != owner, "Localised prompt is shown");
                    List<string> labels = new List<string>();
                    foreach (Control c in Descendants(active)) if (c is Button) labels.Add(c.Text);
                    Require(labels.Contains("Save") && labels.Contains("Discard") && labels.Contains("Cancel"), "Modal buttons are English on any OS locale");
                    CheckButtonsFit(active);
                    active.DialogResult = DialogResult.Cancel;
                }
                catch (Exception ex) { failure = ex; foreach (Form form in Application.OpenForms) if (form != owner) { form.DialogResult = DialogResult.Cancel; break; } }
            };
            timer.Start();
            Require((DialogResult)Call(owner, "Prompt", "Save changes?", "Unsaved changes", "Save", true, true) == DialogResult.Cancel, "Prompt cancellation preserved");
            if (failure != null) throw failure;
        }
    }
    private static void CheckButtonsFit(Control root)
    {
        foreach (Control c in Descendants(root)) if (c is Button)
            Require(TextRenderer.MeasureText(c.Text, c.Font).Width + 12 < c.Width, "Button label fits: " + c.Text);
    }
    private static void Render(Form form, string name)
    {
        bool visible = form.Visible;
        form.Show(); form.PerformLayout(); form.Refresh(); Application.DoEvents();
        using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name), System.Drawing.Imaging.ImageFormat.Png);
        }
        if (!visible) form.Hide();
    }
    private static bool Cjk(string value) { return Regex.IsMatch(value ?? "", "[\u3400-\u9fff]"); }
    private static T Field<T>(object instance, string name) { return (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance); }
    private static object Call(object instance, string name, params object[] args) { return instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, args); }
    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control c in root.Controls) { yield return c; foreach (Control child in Descendants(c)) yield return child; }
    }
    private static T Find<T>(Control root) where T : Control { foreach (Control c in Descendants(root)) if (c is T) return (T)c; throw new InvalidOperationException("Missing control"); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks++; }
}
