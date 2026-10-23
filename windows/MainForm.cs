using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace QuietType
{
    public sealed class MainForm : Form
    {
        readonly UserStore store;
        readonly ContentIndex index;
        readonly float scale;
        readonly ListBox navigation = new ListBox(), lessons = new ListBox();
        readonly ComboBox language = new ComboBox(), group = new ComboBox(), duration = new ComboBox(), pace = new ComboBox(), fontSize = new ComboBox();
        readonly TextBox search = new TextBox();
        readonly RadioButton shortMode = new RadioButton(), longMode = new RadioButton();
        readonly Label firstLabel = new Label(), secondLabel = new Label(), searchLabel = new Label();
        readonly Button next, shuffle, library, restart, peek, records, locale;
        readonly Timer timer = new Timer();
        readonly ToolTip tips = new ToolTip();
        readonly Stopwatch memoryClock = new Stopwatch();
        readonly Random random = new Random();
        readonly List<string> revengeUsed = new List<string>();
        readonly int[] durationValues = { 30, 60, 120, 180, 300 }, paceValues = { 90, 120, 180, 240 };
        bool loading, english, saved, memoryWaiting, memoryPreview, peeking, roundLong;
        int peeksLeft, peeksUsed, recordedMistakes;
        string roundId = "", storageStatus = "";
        Lesson current;
        Workspace page, roundPage;
        Rectangle paper;
        public PracticeControl Practice { get; private set; }
        public Workspace CurrentWorkspace { get { return page; } }
        public Lesson SelectedLesson { get { return current; } }
        public bool IsEnglish { get { return english; } }
        public bool IsLong { get { return longMode.Checked; } }
        int S(int v) { return (int)Math.Round(v * scale); }
        int Sidebar { get { return S(146); } }
        int LibraryWidth { get { return S(252); } }
        int BodyX { get { return Sidebar + LibraryWidth + S(26); } }
        int BodyWidth { get { return ClientSize.Width - BodyX - S(26); } }
        string T(string zh, string en) { return Ui.Pick(english, zh, en); }
        string Key(ComboBox box) { var value = box.SelectedItem as Choice; return value == null ? "" : value.Key; }

        public MainForm() : this(new UserStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuietType"))) { }
        public MainForm(UserStore userStore)
        {
            loading = true; store = userStore; store.Load(); storageStatus = store.LastError;
            english = store.Data.UiLanguage == "en"; index = ContentIndex.Builtin();
            using (var g = CreateGraphics()) scale = g.DpiX / 96f;
            AutoScaleMode = AutoScaleMode.None;
            var area = Screen.FromControl(this).WorkingArea;
            ClientSize = new Size(Math.Min(S(1230), area.Width - S(32)), Math.Min(S(760), area.Height - S(72)));
            MinimumSize = SizeFromClientSize(new Size(Math.Min(S(1040), ClientSize.Width), Math.Min(S(620), ClientSize.Height)));
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White; ForeColor = Color.FromArgb(35, 35, 35);
            Font = new Font("Microsoft YaHei UI", 12 * scale, GraphicsUnit.Pixel);
            DoubleBuffered = true; KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            navigation.Name = "Navigation"; navigation.BorderStyle = BorderStyle.None;
            navigation.BackColor = Color.FromArgb(246, 246, 245); navigation.DrawMode = DrawMode.OwnerDrawFixed;
            navigation.ItemHeight = S(43); navigation.IntegralHeight = false;
            foreach (Workspace value in Enum.GetValues(typeof(Workspace))) navigation.Items.Add(value);
            navigation.DrawItem += DrawNavigation;
            navigation.SelectedIndexChanged += delegate { if (!loading && navigation.SelectedIndex >= 0) SelectWorkspace((Workspace)navigation.SelectedIndex, ""); };
            lessons.Name = "Lessons"; lessons.BorderStyle = BorderStyle.None; lessons.DrawMode = DrawMode.OwnerDrawFixed;
            lessons.ItemHeight = S(61); lessons.IntegralHeight = false; lessons.BackColor = Color.White;
            lessons.DrawItem += DrawLesson;
            lessons.SelectedIndexChanged += delegate { if (!loading) StartCurrent(false); };
            language.Name = "Language"; group.Name = "Group"; duration.Name = "Duration"; pace.Name = "Pace"; fontSize.Name = "FontSize";
            foreach (var box in new[] { language, group, duration, pace, fontSize })
            { box.DropDownStyle = ComboBoxStyle.DropDownList; box.FlatStyle = FlatStyle.Flat; box.FormattingEnabled = true; }
            foreach (var box in new[] { language, group }) box.Format += delegate(object sender, ListControlConvertEventArgs e) {
                var choice = e.ListItem as Choice; if (choice != null) e.Value = choice.Text(english);
            };
            language.SelectedIndexChanged += delegate {
                if (loading) return; loading = true; PopulateGroups(Key(group)); loading = false; RefreshLessons("", false);
            };
            group.SelectedIndexChanged += delegate { if (!loading) RefreshLessons("", false); };
            search.Name = "Search"; search.BorderStyle = BorderStyle.FixedSingle;
            search.TextChanged += delegate { if (!loading) RefreshLessons("", false); };
            foreach (var label in new[] { firstLabel, secondLabel, searchLabel }) { label.ForeColor = Color.FromArgb(115,115,115); label.Font = Font; }
            SetLengthRadio(shortMode, "ShortMode"); SetLengthRadio(longMode, "LongMode"); shortMode.Checked = true;
            for (int i = 13; i <= 21; i++) fontSize.Items.Add(i.ToString());
            duration.SelectedIndexChanged += delegate { if (!loading && page == Workspace.Test) StartCurrent(false); };
            pace.SelectedIndexChanged += delegate { if (!loading && current != null && current.Game == GameKind.Race) StartCurrent(false); };
            Practice = new PracticeControl { Name = "Practice", TextSize = store.Data.FontSize, TabIndex = 12 };
            fontSize.SelectedIndex = Practice.TextSize - 13;
            fontSize.SelectedIndexChanged += delegate {
                if (fontSize.SelectedIndex < 0) return;
                Practice.TextSize = fontSize.SelectedIndex + 13; store.Data.FontSize = Practice.TextSize; Practice.FocusInput();
            };
            Practice.SessionChanged += delegate {
                if (loading) return;
                if (Practice.Session != null && !Practice.Session.IsFinished) saved = false;
                HandleFinished(); Invalidate();
            };
            next = MakeButton("Next", delegate { if (lessons.Items.Count > 0) lessons.SelectedIndex = (lessons.SelectedIndex + 1) % lessons.Items.Count; });
            shuffle = MakeButton("Shuffle", delegate {
                if (lessons.Items.Count < 2) return;
                int nextIndex = random.Next(lessons.Items.Count - 1); if (nextIndex >= lessons.SelectedIndex) nextIndex++;
                lessons.SelectedIndex = nextIndex;
            });
            library = MakeButton("Library", OpenLibrary);
            restart = MakeButton("Restart", delegate { StartCurrent(true); }); restart.BackColor = Color.FromArgb(236,239,241);
            peek = MakeButton("Peek", PeekAtText);
            records = MakeButton("Records", delegate { using (var dialog = new HistoryDialog(store, english)) dialog.ShowDialog(this); Practice.FocusInput(); });
            locale = MakeButton("Locale", delegate { SetUiLanguage(!english); });
            locale.FlatAppearance.BorderSize = 0; records.FlatAppearance.BorderSize = 0;
            Controls.AddRange(new Control[] { navigation, lessons, language, group, search, firstLabel, secondLabel, searchLabel,
                shortMode, longMode, duration, pace, fontSize, Practice, next, shuffle, library, restart, peek, records, locale });
            ApplyLocale();
            timer.Interval = 100; timer.Tick += Tick; timer.Start();
            Activated += delegate { Practice.SetHostActive(Practice.HasInputFocus); };
            Deactivate += delegate { Practice.SetHostActive(false); };
            Shown += delegate { Practice.FocusInput(); };
            FormClosing += delegate { SaveRound("中途结束"); store.Data.FontSize = Practice.TextSize; store.Data.UiLanguage = english ? "en" : "zh"; store.Save(); };
            SelectWorkspace(Workspace.Reading, ""); PerformLayout();
        }
        void SetLengthRadio(RadioButton radio, string name)
        {
            radio.Name = name; radio.Appearance = Appearance.Button; radio.FlatStyle = FlatStyle.Flat;
            radio.FlatAppearance.BorderColor = Color.FromArgb(215,215,215); radio.TextAlign = ContentAlignment.MiddleCenter;
            radio.CheckedChanged += delegate {
                radio.BackColor = radio.Checked ? Color.FromArgb(236,239,241) : Color.White;
                if (loading || !radio.Checked) return;
                loading = true; duration.SelectedIndex = IsLong ? 3 : 1; loading = false; StartCurrent(false);
            };
        }
        Button MakeButton(string name, Action action)
        {
            var button = new Button { Name = name, Font = Font, FlatStyle = FlatStyle.Flat, BackColor = Color.White,
                ForeColor = ForeColor, Cursor = Cursors.Hand };
            button.FlatAppearance.BorderColor = Color.FromArgb(207,207,207);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(243,243,243); button.Click += delegate { action(); }; return button;
        }
        public void SetUiLanguage(bool useEnglish)
        {
            english = useEnglish; bool previous = loading; loading = true;
            string langKey = Key(language), groupKey = Key(group);
            Populate(language, index.Languages(page, store.Data.Texts), langKey); PopulateGroups(groupKey);
            ApplyLocale(); loading = previous;
            store.Data.UiLanguage = english ? "en" : "zh"; store.Save(); storageStatus = store.LastError;
            navigation.Invalidate(); lessons.Invalidate(); PerformLayout(); Invalidate(); Practice.FocusInput();
        }
        void ApplyLocale()
        {
            bool previous = loading; loading = true;
            Text = T("轻打", "Quiet Type"); shortMode.Text = T("短篇速练", "Short practice"); longMode.Text = T("长篇精练", "Long practice");
            int d = duration.SelectedIndex < 0 ? 1 : duration.SelectedIndex, p = pace.SelectedIndex < 0 ? 2 : pace.SelectedIndex;
            duration.Items.Clear(); foreach (int value in durationValues) duration.Items.Add(value + T(" 秒", " sec")); duration.SelectedIndex = d;
            pace.Items.Clear(); foreach (int value in paceValues) pace.Items.Add(value + T(" 字/分", " chars/min")); pace.SelectedIndex = p;
            next.Text = T("下一篇", "Next text"); shuffle.Text = T("随机", "Shuffle"); library.Text = T("管理文本", "Manage texts");
            restart.Text = memoryWaiting ? T("开始记忆", "Start recall") : T("重来", "Restart"); peek.Text = T("偷看 ", "Peek ") + peeksLeft;
            records.Text = T("练习记录", "History"); locale.Text = english ? "中文" : "English";
            searchLabel.Text = T("搜索文章标题", "Search titles"); search.AccessibleName = searchLabel.Text;
            bool modeFirst = page == Workspace.Games || page == Workspace.Exams;
            firstLabel.Text = modeFirst ? (page == Workspace.Games ? T("玩法", "Challenge") : T("题型", "Task type")) : T("练习语言", "Text language");
            secondLabel.Text = modeFirst ? T("练习语言", "Text language") : T("文章分类", "Topic");
            language.AccessibleName = T("练习语言", "Text language"); group.AccessibleName = modeFirst ? firstLabel.Text : secondLabel.Text;
            lessons.AccessibleName = T("文章列表", "Practice texts"); navigation.AccessibleName = T("练习类型", "Practice type");
            Practice.AccessibleName = T("打字练习区", "Typing area");
            var sink = Practice.Controls.Find("InputSink",true); if (sink.Length > 0) sink[0].AccessibleName = T("输入文字", "Type here");
            tips.SetToolTip(locale,T("切换界面语言，保留当前练习", "Switch interface language and keep this round"));
            tips.SetToolTip(restart,T("重开当前练习  Ctrl+R", "Restart this round  Ctrl+R")); tips.SetToolTip(next,T("下一篇  Ctrl+N", "Next text  Ctrl+N"));
            tips.SetToolTip(fontSize,T("文字大小", "Text size")); tips.SetToolTip(library,T("粘贴或导入自己想练的文章", "Paste or import your own practice texts"));
            loading = previous;
        }
        void Populate(ComboBox box, List<Choice> choices, string preferred)
        {
            box.Items.Clear(); int selected = 0;
            foreach (var choice in choices) { if (choice.Key == preferred) selected = box.Items.Count; box.Items.Add(choice); }
            if (box.Items.Count > 0) box.SelectedIndex = selected;
        }
        void PopulateGroups(string preferred)
        {
            var values = index.Groups(page);
            if (page == Workspace.Reading || page == Workspace.Test)
                values.RemoveAll(delegate(Choice c) { return c.Key != "" && index.Query(page,Key(language),c.Key,"",store.Data.Texts).Count == 0; });
            Populate(group,values,preferred);
        }
        public void SelectWorkspace(Workspace selected, string preferredId)
        {
            SaveRound("中途结束"); loading = true; page = selected; navigation.SelectedIndex = (int)selected; search.Text = "";
            string preferredLanguage = "en";
            if (!string.IsNullOrEmpty(preferredId))
                foreach (var item in index.Query(page,"","","",store.Data.Texts)) if (item.Id == preferredId) { preferredLanguage = ContentIndex.LanguageKey(item); break; }
            Populate(language,index.Languages(page,store.Data.Texts),preferredLanguage); PopulateGroups(""); ApplyLocale(); loading = false;
            RefreshLessons(preferredId,true);
        }
        void RefreshLessons(string preferredId, bool force)
        {
            if (string.IsNullOrEmpty(preferredId) && current != null) preferredId = current.Id;
            var results = index.Query(page,Key(language),(page == Workspace.Code || page == Workspace.Library) ? "" : Key(group),search.Text,store.Data.Texts);
            loading = true; lessons.BeginUpdate(); lessons.Items.Clear(); int selected = 0;
            foreach (var item in results) { if (item.Id == preferredId) selected = lessons.Items.Count; lessons.Items.Add(item); }
            if (lessons.Items.Count > 0) lessons.SelectedIndex = selected;
            lessons.EndUpdate(); loading = false;
            Lesson candidate = lessons.SelectedItem as Lesson;
            if (force || current == null || candidate == null || current.Id != candidate.Id || current.LongText != candidate.LongText) StartCurrent(false);
            PerformLayout(); Invalidate();
        }
        string TargetFor(Lesson lesson)
        {
            revengeUsed.Clear(); string target = IsLong ? lesson.LongText : lesson.ShortText;
            if (lesson.Game == GameKind.Revenge)
            {
                string lang = ContentIndex.LanguageKey(lesson);
                foreach (var mistake in store.Data.Mistakes)
                { if (ContentIndex.TokenMatches(mistake.Text,lang)) revengeUsed.Add(mistake.Text); if (revengeUsed.Count >= (IsLong ? 35 : 10)) break; }
                if (revengeUsed.Count > 0) { target = string.Join(" ",revengeUsed.ToArray()); if (IsLong) target = target + " " + target + " " + target; }
            }
            if (page == Workspace.Test)
            {
                string seed = target; int minimum = IsLong ? 12000 : 4000;
                while (target.Length < minimum) target += " " + seed;
                if (PracticeText.Elements(target).Count > 35000) target = PracticeText.Shorten(target,35000,false);
            }
            return PracticeText.Normalize(target,lesson.IsCode);
        }
        void StartCurrent(bool startButton)
        {
            if (loading || Practice == null) return;
            SaveRound("中途结束"); loading = true; var previous = current; current = lessons.SelectedItem as Lesson;
            if (current == null) { memoryWaiting = memoryPreview = peeking = false; Practice.Visible = false; loading = false; PerformLayout(); Invalidate(); return; }
            var options = new SessionOptions { IsCode = current.IsCode, Game = current.Game, StageCount = IsLong ? 7 : 3 };
            if (current.Game == GameKind.Race && (previous == null || previous.Game != GameKind.Race || ContentIndex.LanguageKey(previous) != ContentIndex.LanguageKey(current)))
                pace.SelectedIndex = ContentIndex.LanguageKey(current) == "zh" ? 1 : 2;
            if (page == Workspace.Test) { options.TimeLimitSeconds = durationValues[Math.Max(0,duration.SelectedIndex)]; options.CountWhileUnfocused = true; }
            if (current.Game == GameKind.SuddenDeath) options.MistakeLimit = 1;
            if (current.Game == GameKind.Revenge) options.MistakeLimit = IsLong ? 5 : 3;
            string target = TargetFor(current);
            if (current.Game == GameKind.Race)
            { options.TimeLimitSeconds = Math.Max(1,(int)Math.Ceiling(PracticeText.Elements(target).Count * 60.0 / paceValues[Math.Max(0,pace.SelectedIndex)])); options.CountWhileUnfocused = true; }
            roundId = Guid.NewGuid().ToString("N"); saved = false; recordedMistakes = 0; roundLong = IsLong; roundPage = page;
            memoryWaiting = current.Game == GameKind.Memory; memoryPreview = peeking = false; peeksLeft = IsLong ? 3 : 2; peeksUsed = 0; memoryClock.Reset();
            Practice.InputAllowed = !memoryWaiting; Practice.Load(new TypingSession(target,options),current.IsCode); Practice.MaskReference = memoryWaiting;
            peek.Enabled = !memoryWaiting; loading = false; ApplyLocale();
            if (memoryWaiting && startButton) BeginMemory(); else Practice.FocusInput(); PerformLayout(); Invalidate();
        }
        void BeginMemory()
        {
            memoryWaiting = false; memoryPreview = true; peeking = false; Practice.InputAllowed = false; Practice.MaskReference = false;
            memoryClock.Restart(); restart.Text = T("重来", "Restart"); peek.Enabled = false; Invalidate();
        }
        void PeekAtText()
        {
            if (current == null || current.Game != GameKind.Memory || memoryWaiting || memoryPreview || peeking || Practice.Session.IsFinished || peeksLeft <= 0) return;
            peeksLeft--; peeksUsed++; peeking = true; Practice.MaskReference = false; memoryClock.Restart();
            peek.Text = T("偷看 ", "Peek ") + peeksLeft; peek.Enabled = false; Practice.FocusInput(); Invalidate();
        }
        void Tick(object sender, EventArgs e)
        {
            if (Practice == null || Practice.Session == null || current == null) return;
            if (memoryPreview && memoryClock.Elapsed.TotalSeconds >= (roundLong ? 8 : 4))
            { memoryPreview = false; Practice.MaskReference = true; Practice.InputAllowed = true; peek.Enabled = peeksLeft > 0; Practice.FocusInput(); Invalidate(); }
            if (peeking && memoryClock.Elapsed.TotalSeconds >= 1.8)
            { peeking = false; Practice.MaskReference = true; peek.Enabled = peeksLeft > 0; Invalidate(); }
            Practice.Session.Tick();
            if (current.Game == GameKind.Race)
            {
                Practice.GhostPosition = Math.Min(Practice.Session.TargetUnits.Count - 1,(int)(Practice.Session.ElapsedSeconds * paceValues[Math.Max(0,pace.SelectedIndex)] / 60)); Practice.Invalidate();
            }
            HandleFinished();
            if (Practice.Session.HasStarted || memoryPreview || peeking)
            { Invalidate(new Rectangle(BodyX,S(157),BodyWidth,S(98))); Invalidate(new Rectangle(BodyX,paper.Bottom + S(4),BodyWidth,S(30))); }
        }
        int FinalScore { get { return Practice.Session.Score * (current.Game == GameKind.Memory ? Math.Max(1,3-peeksUsed) : 1); } }
        void HandleFinished()
        {
            if (loading || Practice.Session == null || current == null || !Practice.Session.IsFinished || saved) return;
            SaveRound("");
            if (current.Game == GameKind.Revenge && Practice.Session.Reason == FinishReason.Completed && Practice.Session.ErrorCount == 0 && revengeUsed.Count > 0)
            { store.ClearMistakes(revengeUsed); storageStatus = store.LastError; }
            peek.Enabled = false; Practice.MaskReference = false; Invalidate();
        }
        void SaveRound(string overrideResult)
        {
            if (Practice == null || Practice.Session == null || current == null || !Practice.Session.HasStarted || saved) return;
            var s = Practice.Session; string result = s.IsFinished ? ResultTitle() : overrideResult;
            var record = new ScoreRecord { Id = roundId, WhenIso = DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture),
                Section = Ui.PageName(roundPage,false), Title = (current.Game == GameKind.None ? "" : Ui.GameName(current.Game,false) + " / ") + current.Title,
                TitleEn = (current.Game == GameKind.None ? "" : Ui.GameName(current.Game,true) + " / ") + ContentIndex.Title(current,true),
                Language = current.Language, LengthMode = roundLong ? "长篇精练" : "短篇速练", Result = result,
                Cpm = Math.Round(s.CharactersPerMinute), Accuracy = Math.Round(s.Accuracy,1), Seconds = s.ElapsedSeconds,
                Score = FinalScore, BestCombo = s.BestCombo, TypedCount = s.Position, CorrectCount = s.CorrectCount };
            store.Record(record,s.MissedTokens.GetRange(recordedMistakes,s.MissedTokens.Count-recordedMistakes));
            recordedMistakes = s.MissedTokens.Count; saved = true; storageStatus = store.LastError;
        }
        string ResultTitle()
        {
            var s = Practice.Session;
            if (s.Reason == FinishReason.TimeUp) return "测试结束";
            if (s.Reason == FinishReason.Lost)
            {
                if (current.Game == GameKind.Race) return s.Position >= s.TargetUnits.Count ? "错字未改" : "影子先到了";
                if (current.Game == GameKind.Memory) return "记忆有误";
                if (current.Game == GameKind.Combo) return "准确率未达90%";
                if (current.Game == GameKind.Revenge) return "错字未清完";
                return "挑战失败";
            }
            if (s.Reason == FinishReason.Completed) return roundPage == Workspace.Games ? "挑战完成" : "练习完成";
            return "练习中";
        }
        void OpenLibrary()
        {
            string before = LibraryFingerprint(); string selectedId = "";
            using (var dialog = new LibraryDialog(store,english)) if (dialog.ShowDialog(this) == DialogResult.OK) selectedId = "user:" + dialog.SelectedId;
            if (selectedId.Length > 0)
            {
                SelectWorkspace(Workspace.Library,selectedId);
                foreach (var value in language.Items)
                {
                    var choice = value as Choice;
                    var all = index.Query(Workspace.Library,choice.Key,"","",store.Data.Texts);
                    if (all.Exists(delegate(Lesson item) { return item.Id == selectedId; }))
                    { loading = true; language.SelectedItem = choice; loading = false; RefreshLessons(selectedId,true); break; }
                }
            }
            else if (page == Workspace.Library && before != LibraryFingerprint())
            {
                loading = true; Populate(language,index.Languages(page,store.Data.Texts),Key(language)); loading = false;
                RefreshLessons(current == null ? "" : current.Id,true);
            }
            storageStatus = store.LastError; Practice.FocusInput(); Invalidate();
        }
        string LibraryFingerprint()
        {
            var result = new StringBuilder(); foreach (var item in store.Data.Texts) result.Append(item.Id).Append('\u001f').Append(item.Title).Append('\u001f').Append(item.Language).Append('\u001f').Append(item.Text).Append('\u001e');
            return result.ToString();
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys data)
        {
            if (data == (Keys.Control | Keys.R)) { StartCurrent(true); return true; }
            if (data == (Keys.Control | Keys.N)) { next.PerformClick(); return true; }
            return base.ProcessCmdKey(ref msg,data);
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e); if (Practice == null || next == null) return;
            navigation.SetBounds(0,S(83),Sidebar,S(270));
            records.SetBounds(S(14),ClientSize.Height - S(100),Sidebar - S(28),S(32));
            locale.SetBounds(S(14),ClientSize.Height - S(58),Sidebar - S(28),S(32));
            int lx = Sidebar + S(18), lw = LibraryWidth - S(36);
            bool modeFirst = page == Workspace.Games || page == Workspace.Exams;
            bool oneFilter = page == Workspace.Code || page == Workspace.Library || page == Workspace.Exams;
            firstLabel.SetBounds(lx,S(75),lw,S(20)); firstLabel.Visible = true; secondLabel.SetBounds(lx,S(139),lw,S(20));
            var first = modeFirst ? group : language; var second = modeFirst ? language : group;
            first.SetBounds(lx,S(99),lw,S(28)); first.Visible = true;
            second.SetBounds(lx,S(163),lw,S(28)); second.Visible = !oneFilter; secondLabel.Visible = !oneFilter;
            int searchY = oneFilter ? 143 : 207;
            searchLabel.SetBounds(lx,S(searchY),lw,S(20)); search.SetBounds(lx,S(searchY+24),lw,S(26));
            lessons.SetBounds(Sidebar + S(8),S(searchY+66),LibraryWidth - S(16),ClientSize.Height-S(searchY+66)-S(70));
            library.Visible = page == Workspace.Library; library.SetBounds(lx,ClientSize.Height-S(52),lw,S(31));
            bool active = current != null;
            Practice.Visible = active;
            foreach (var item in new Control[] { shortMode,longMode,fontSize,next,shuffle,restart }) item.Visible = active;
            duration.Visible = active && page == Workspace.Test; pace.Visible = active && current.Game == GameKind.Race; peek.Visible = active && current.Game == GameKind.Memory;
            int x = BodyX, w = BodyWidth;
            shortMode.SetBounds(x,S(114),S(119),S(30)); longMode.SetBounds(x+S(126),S(114),S(119),S(30));
            fontSize.SetBounds(x+w-S(57),S(116),S(57),S(26));
            duration.SetBounds(x+w-S(124),S(222),S(124),S(27)); pace.SetBounds(x+w-S(133),S(222),S(133),S(27));
            paper = new Rectangle(x,S(263),w,Math.Max(S(150),ClientSize.Height-S(375)));
            Practice.SetBounds(paper.X+S(16),paper.Y+S(15),paper.Width-S(32),paper.Height-S(31));
            int y = ClientSize.Height-S(53);
            next.SetBounds(x,y,S(97),S(32)); shuffle.SetBounds(x+S(107),y,S(85),S(32));
            restart.SetBounds(x+w-S(114),y,S(114),S(32)); peek.SetBounds(x+w-S(218),y,S(94),S(32));
            Invalidate();
        }
        void DrawText(Graphics g, string value, Rectangle area, int pixels, Color color, bool bold, TextFormatFlags extra)
        {
            using (var font = new Font("Microsoft YaHei UI",pixels*scale,bold ? FontStyle.Bold : FontStyle.Regular,GraphicsUnit.Pixel))
                TextRenderer.DrawText(g,value ?? "",font,area,color,TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|extra);
        }
        void DrawNavigation(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return; bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var brush = new SolidBrush(selected ? Color.FromArgb(230,234,236) : navigation.BackColor)) e.Graphics.FillRectangle(brush,e.Bounds);
            if (selected) using (var brush = new SolidBrush(Color.FromArgb(76,98,112))) e.Graphics.FillRectangle(brush,0,e.Bounds.Y,S(3),e.Bounds.Height);
            DrawText(e.Graphics,Ui.PageName((Workspace)e.Index,english),new Rectangle(S(22),e.Bounds.Y,e.Bounds.Width-S(28),e.Bounds.Height),13,selected ? ForeColor : Color.FromArgb(100,100,100),selected,0);
        }
        void DrawLesson(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return; var lesson = (Lesson)lessons.Items[e.Index]; bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var brush = new SolidBrush(selected ? Color.FromArgb(239,241,242) : Color.White)) e.Graphics.FillRectangle(brush,e.Bounds);
            if (selected) using (var brush = new SolidBrush(Color.FromArgb(108,129,142))) e.Graphics.FillRectangle(brush,e.Bounds.X,e.Bounds.Y+S(9),S(2),e.Bounds.Height-S(18));
            DrawText(e.Graphics,ContentIndex.Title(lesson,english),new Rectangle(e.Bounds.X+S(12),e.Bounds.Y+S(6),e.Bounds.Width-S(22),S(25)),12,ForeColor,selected,0);
            string topic = page == Workspace.Games ? Ui.GameName(lesson.Game,english) : lesson.IsCode ? lesson.Language : T(lesson.Category,lesson.CategoryEn);
            DrawText(e.Graphics,topic+T("  ·  全文 ","  ·  Full ")+PracticeText.Elements(lesson.LongText).Count+T(" 字符"," chars"),new Rectangle(e.Bounds.X+S(12),e.Bounds.Y+S(32),e.Bounds.Width-S(22),S(20)),10,Color.FromArgb(140,140,140),false,0);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g = e.Graphics;
            Color ink = Color.FromArgb(42,42,42), dim = Color.FromArgb(120,120,120), line = Color.FromArgb(227,227,227);
            using (var brush = new SolidBrush(navigation.BackColor)) g.FillRectangle(brush,0,0,Sidebar,ClientSize.Height);
            using (var pen = new Pen(line,scale)) { g.DrawLine(pen,Sidebar,0,Sidebar,ClientSize.Height); g.DrawLine(pen,Sidebar+LibraryWidth,0,Sidebar+LibraryWidth,ClientSize.Height); }
            DrawText(g,T("轻打","Quiet Type"),new Rectangle(S(22),S(24),Sidebar-S(32),S(31)),18,ink,true,0);
            DrawText(g,T("离线打字练习","Offline typing"),new Rectangle(S(22),S(52),Sidebar-S(30),S(22)),10,dim,false,0);
            DrawText(g,"GraysonC",new Rectangle(S(22),ClientSize.Height-S(130),Sidebar-S(32),S(20)),10,Color.FromArgb(185,185,185),false,0);
            DrawText(g,Ui.PageName(page,english),new Rectangle(Sidebar+S(18),S(28),LibraryWidth-S(36),S(28)),16,ink,true,0);
            if (page != Workspace.Library) DrawText(g,lessons.Items.Count+T(" 篇可选"," texts"),new Rectangle(Sidebar+S(18),ClientSize.Height-S(47),LibraryWidth-S(36),S(24)),11,dim,false,0);
            int x = BodyX, w = BodyWidth;
            if (current == null || Practice.Session == null)
            {
                DrawText(g,page == Workspace.Library ? T("练你自己的文字","Your words, your practice") : T("没有找到文章","No matching texts"),new Rectangle(x,S(90),w,S(38)),21,ink,false,0);
                DrawText(g,page == Workspace.Library ? T("在左下角管理文本，粘贴文章或导入 TXT","Use Manage texts to paste an article or import a TXT file") : T("换一个分类，或清空搜索试试","Try a different topic or clear the search"),new Rectangle(x,S(138),w,S(30)),12,dim,false,0); return;
            }
            DrawText(g,ContentIndex.Title(current,english),new Rectangle(x,S(24),w,S(33)),20,ink,true,0);
            string description = T(current.Description,current.DescriptionEn);
            using (var font = new Font("Microsoft YaHei UI",11*scale,GraphicsUnit.Pixel)) TextRenderer.DrawText(g,description,font,new Rectangle(x,S(65),w,S(38)),dim,TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis);
            DrawText(g,T("字号","Text size"),new Rectangle(x+w-S(129),S(115),S(64),S(28)),11,dim,false,TextFormatFlags.Right);
            var s = Practice.Session; string speed = !s.HasStarted || s.ElapsedSeconds < 2 ? "—" : Math.Round(s.CharactersPerMinute).ToString();
            string time = FormatTime(Math.Floor(s.ElapsedSeconds));
            if (page == Workspace.Test) time = FormatTime(Math.Max(0,durationValues[Math.Max(0,duration.SelectedIndex)]-s.ElapsedSeconds));
            if (current.Game == GameKind.Race) time = FormatTime(Math.Max(0,Math.Ceiling(s.TargetUnits.Count*60.0/paceValues[Math.Max(0,pace.SelectedIndex)])-s.ElapsedSeconds));
            string[] names = { T("速度 · 字/分","Speed · chars/min"),T("准确率","Accuracy"),page == Workspace.Test || current.Game == GameKind.Race ? T("剩余时间","Time left") : T("用时","Time"),T("进度","Progress") };
            string[] values = { speed,s.Attempts == 0 ? "—" : Math.Round(s.Accuracy,1).ToString("0.#")+"%",time,s.Position+" / "+s.TargetUnits.Count };
            for (int i = 0; i < 4; i++)
            {
                int sx = x+i*w/4;
                DrawText(g,names[i],new Rectangle(sx,S(162),w/4-S(8),S(20)),10,dim,false,0);
                DrawText(g,values[i],new Rectangle(sx,S(185),w/4-S(8),S(27)),i == 3 ? 15 : 20,ink,false,0);
            }
            PaintMode(g,x,w,dim,ink);
            using (var pen = new Pen(Color.FromArgb(218,218,218),scale)) g.DrawRectangle(pen,paper);
            if (s.Position > 0) using (var pen = new Pen(Color.FromArgb(108,129,146),Math.Max(1,S(2))))
                g.DrawLine(pen,paper.Left+1,paper.Bottom,paper.Left+(paper.Width-2)*s.Position/s.TargetUnits.Count,paper.Bottom);
            string footer; Color footerColor = dim;
            if (!string.IsNullOrEmpty(storageStatus)) footer = Ui.Error(storageStatus,english);
            else if (s.IsFinished)
            {
                footer = Ui.RecordText(ResultTitle(),english)+"   "+Math.Round(s.CharactersPerMinute)+T(" 字/分   准确率 "," chars/min   Accuracy ")+Math.Round(s.Accuracy,1).ToString("0.#")+"%";
                if (page == Workspace.Games) footer += T("   得分 ","   Score ")+FinalScore+T("   最高连击 ","   Best combo ")+s.BestCombo;
                footer += "   "+s.ErrorCount+T(" 处未改错误"," uncorrected"); footerColor = s.Reason == FinishReason.Lost ? Color.FromArgb(160,81,81) : ink;
            }
            else if (current.IsCode) footer = T("Enter 换行    Tab 缩进    Backspace 修改    Ctrl+R 重来","Enter new line    Tab indent    Backspace edit    Ctrl+R restart");
            else if (page == Workspace.Exams) footer = T(current.SourceInfo,current.SourceInfoEn)+"    "+T("英文速度 ","Speed ")+(speed == "—" ? "—" : Math.Round(s.CharactersPerMinute/5).ToString())+" WPM";
            else if (!s.HasStarted) footer = T("点击白色区域开始    中文和其他语言使用系统输入法","Click the white area to begin    Use your system input method for other languages");
            else if (!s.IsRunning) footer = T("已暂停    点击文字区域继续","Paused    Click the typing area to continue");
            else footer = T("Backspace 修改    Ctrl+Backspace 删词    Ctrl+R 重来","Backspace edit    Ctrl+Backspace delete word    Ctrl+R restart");
            DrawText(g,footer,new Rectangle(x,paper.Bottom+S(10),w,S(25)),10,footerColor,false,0);
        }
        void PaintMode(Graphics g, int x, int width, Color dim, Color ink)
        {
            var s = Practice.Session; string value;
            if (page == Workspace.Test) value = T("首次输入开始计时    切换窗口计时继续","The first key starts the clock    Time keeps running outside this window");
            else if (current.Game == GameKind.Combo)
            {
                int stages = roundLong ? 7 : 3, stage = Math.Min(stages,s.Position*stages/s.TargetUnits.Count+1);
                value = T("第 ","Stage ")+stage+" / "+stages+"    "+s.Combo+T(" 连击"," combo")+"    ×"+Math.Min(5,1+s.Combo/10)+"    "+T("得分 ","Score ")+s.Score+T("    90%准确率过关","    Finish with 90% accuracy");
            }
            else if (current.Game == GameKind.SuddenDeath) value = T("错一个字就结束    已走过 ","One wrong key ends the run    Correct ")+s.CorrectCount+T(" 字"," chars");
            else if (current.Game == GameKind.Memory)
            {
                if (memoryWaiting) value = T("按「开始记忆」，先看原文，再藏起盲打","Press Start recall to preview the text, then type from memory");
                else if (memoryPreview) value = T("记住这一段    还剩 ","Remember this    ")+Math.Max(1,(int)Math.Ceiling((roundLong ? 8 : 4)-memoryClock.Elapsed.TotalSeconds))+T(" 秒"," sec left");
                else value = (peeking ? T("偷看中","Peeking") : T("原文已藏起","Text hidden"))+T("    剩余偷看 ","    Peeks left ")+peeksLeft+T("    完成得分 ×","    Finish score ×")+Math.Max(1,3-peeksUsed);
            }
            else if (current.Game == GameKind.Revenge)
                value = (revengeUsed.Count == 0 ? T("易错词热身","Warm-up words") : T("复仇清单 ","Mistake list ")+revengeUsed.Count)+T("    剩余 ","    Lives ")+Math.Max(0,(roundLong ? 5 : 3)-s.TotalMistakes)+T("    全部改对清掉清单","    Correct every word to clear the list");
            else if (current.Game == GameKind.Race)
            {
                double ghost = Math.Min(1,s.ElapsedSeconds*paceValues[Math.Max(0,pace.SelectedIndex)]/60/s.TargetUnits.Count);
                double actual = s.CorrectCount/(double)s.TargetUnits.Count; int barX = x+S(40), barW = Math.Max(S(75),width-S(295));
                DrawText(g,T("你","You"),new Rectangle(x,S(219),S(36),S(18)),10,ink,false,0);
                DrawText(g,T("影子","Ghost"),new Rectangle(x,S(236),S(37),S(18)),10,dim,false,0);
                using (var pen = new Pen(Color.FromArgb(233,233,233),S(3))) { g.DrawLine(pen,barX,S(228),barX+barW,S(228)); g.DrawLine(pen,barX,S(245),barX+barW,S(245)); }
                using (var pen = new Pen(ink,S(3))) g.DrawLine(pen,barX,S(228),barX+(int)(barW*actual),S(228));
                using (var pen = new Pen(Color.FromArgb(142,163,180),S(3))) g.DrawLine(pen,barX,S(245),barX+(int)(barW*ghost),S(245));
                int lead = s.CorrectCount-(int)(ghost*s.TargetUnits.Count);
                DrawText(g,(lead >= 0 ? T("领先 ","Ahead ") : T("落后 ","Behind "))+Math.Abs(lead),new Rectangle(barX+barW+S(12),S(224),S(94),S(24)),11,ink,false,0); return;
            }
            else value = Ui.Language(ContentIndex.LanguageKey(current),english)+T("    切换窗口自动暂停","    Pauses when you leave the typing area");
            DrawText(g,value,new Rectangle(x,S(222),width-S(page == Workspace.Test ? 135 : 0),S(29)),10,dim,false,0);
        }
        static string FormatTime(double seconds) { int value = Math.Max(0,(int)Math.Ceiling(seconds)); return (value/60).ToString("00")+":"+(value%60).ToString("00"); }
        protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); tips.Dispose(); } base.Dispose(disposing); }
    }
}
