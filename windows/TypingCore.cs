using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace QuietType
{
    public static class PracticeText
    {
        public static string Normalize(string text, bool code)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("请先输入练习文本");
            try { text = text.Normalize(NormalizationForm.FormC); }
            catch (ArgumentException) { throw new ArgumentException("文本含有不完整字符，请重新复制"); }
            text = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\ufeff", "").Replace("\u200b", "");
            var clean = new StringBuilder();
            bool pendingSpace = false;
            foreach (char c in text)
            {
                if ((c >= '\u0590' && c <= '\u08ff') || (c >= '\ufb1d' && c <= '\ufdff')
                    || (c >= '\ufe70' && c <= '\ufefc') || c == '\u200f' || c == '\u202b' || c == '\u202e' || c == '\u2067')
                    throw new ArgumentException("暂不支持从右向左排版的文本，可使用中文、日韩文及从左向右的其他语言");
                if (code && c == '\n') { clean.Append('\n'); continue; }
                if (code && c == '\t') { clean.Append("    "); continue; }
                if (!code && char.IsWhiteSpace(c)) { pendingSpace = clean.Length > 0; continue; }
                if (char.IsControl(c)) throw new ArgumentException("文本中有无法输入的控制字符");
                if (pendingSpace) clean.Append(' ');
                pendingSpace = false;
                clean.Append(c == '\u00a0' ? ' ' : c);
            }
            string result = code ? clean.ToString().Trim('\n') : clean.ToString().Trim();
            if (string.IsNullOrWhiteSpace(result)) throw new ArgumentException("请先输入练习文本");
            if (Elements(result).Count > 40000) throw new ArgumentException("文本最多支持 40,000 个字符");
            return result;
        }
        public static List<string> Elements(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(text)) return list;
            var iterator = StringInfo.GetTextElementEnumerator(text);
            while (iterator.MoveNext())
            {
                string part = iterator.GetTextElement();
                int cp = char.ConvertToUtf32(part, 0);
                bool join = list.Count > 0 && (part == "\u200d" || list[list.Count - 1].EndsWith("\u200d", StringComparison.Ordinal)
                    || (cp >= 0x1f3fb && cp <= 0x1f3ff));
                if (list.Count > 0 && cp >= 0x1f1e6 && cp <= 0x1f1ff)
                {
                    string previous = list[list.Count - 1];
                    int p = char.ConvertToUtf32(previous, 0);
                    join = previous.Length == 2 && p >= 0x1f1e6 && p <= 0x1f1ff;
                }
                if (join) list[list.Count - 1] += part;
                else list.Add(part);
            }
            return list;
        }
        public static string Shorten(string text, int maximum, bool code)
        {
            var units = Elements(Normalize(text, code));
            if (units.Count <= maximum) return string.Concat(units);
            maximum = Math.Max(1, maximum);
            if (code)
            {
                int line = maximum;
                while (line > maximum / 2 && units[line - 1] != "\n") line--;
                if (line > maximum / 2) maximum = line;
            }
            else
            {
                int word = maximum;
                while (word > maximum * 3 / 4 && IsWord(units[word])) word--;
                if (word > maximum * 3 / 4) maximum = word;
            }
            return string.Concat(units.GetRange(0, maximum)).TrimEnd();
        }
        public static int CellSpan(string element)
        {
            if (string.IsNullOrEmpty(element)) return 1;
            int c = char.ConvertToUtf32(element, 0);
            return c >= 0x1100 && (c <= 0x115f || c == 0x2329 || c == 0x232a
                || (c >= 0x2e80 && c <= 0xa4cf) || (c >= 0xac00 && c <= 0xd7a3)
                || (c >= 0xf900 && c <= 0xfaff) || (c >= 0xfe10 && c <= 0xfe6f)
                || (c >= 0xff00 && c <= 0xff60) || c >= 0x1f000) ? 2 : 1;
        }
        public static bool IsWord(string element)
        {
            if (string.IsNullOrEmpty(element) || CellSpan(element) > 1) return false;
            return char.IsLetterOrDigit(element, 0) || element == "_" || element == "'" || element == "-";
        }
        public static string MistakeToken(IList<string> units, int index)
        {
            if (units[index] == " " || units[index] == "\n") index = Math.Max(0, index - 1);
            int start = index, end = index + 1;
            if (IsWord(units[index]))
            {
                while (start > 0 && IsWord(units[start - 1])) start--;
                while (end < units.Count && IsWord(units[end])) end++;
            }
            else if (CellSpan(units[index]) > 1)
            { start = Math.Max(0, index - 1); end = Math.Min(units.Count, index + 2); }
            var b = new StringBuilder();
            for (int i = start; i < end && i < start + 32; i++) b.Append(units[i]);
            return b.ToString().Trim();
        }
    }
    sealed class MonotonicTime : ITimeSource
    { public double Now { get { return Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency; } } }

    public sealed class TypingSession
    {
        readonly SessionOptions options;
        readonly ITimeSource time;
        readonly HashSet<int> rewarded = new HashSet<int>();
        double elapsed, last;
        bool active, started, running;
        int correctAttempts;
        public readonly List<string> TargetUnits;
        public readonly List<string> InputUnits = new List<string>();
        public readonly List<string> MissedTokens = new List<string>();
        public TypingSession(string text, SessionOptions settings) : this(text, settings, new MonotonicTime()) { }
        public TypingSession(string text, SessionOptions settings, ITimeSource source)
        {
            settings = settings ?? new SessionOptions();
            options = new SessionOptions { Game = settings.Game, IsCode = settings.IsCode,
                TimeLimitSeconds = settings.TimeLimitSeconds, MistakeLimit = settings.MistakeLimit,
                CountWhileUnfocused = settings.CountWhileUnfocused, StageCount = settings.StageCount };
            time = source;
            Target = PracticeText.Normalize(text, options.IsCode);
            TargetUnits = PracticeText.Elements(Target);
        }
        public string Target { get; private set; }
        public string Typed { get { return string.Concat(InputUnits); } }
        public int Position { get { return InputUnits.Count; } }
        public int Attempts { get; private set; }
        public int TotalMistakes { get; private set; }
        public int Combo { get; private set; }
        public int BestCombo { get; private set; }
        public int Score { get; private set; }
        public int CorrectCount
        {
            get { int count = 0; for (int i = 0; i < Position; i++) if (InputUnits[i] == TargetUnits[i]) count++; return count; }
        }
        public int ErrorCount { get { return Position - CorrectCount; } }
        public double Accuracy { get { return Attempts == 0 ? 100 : correctAttempts * 100.0 / Attempts; } }
        public double ElapsedSeconds
        {
            get
            {
                double value = elapsed + (running ? Math.Max(0, time.Now - last) : 0);
                return options.TimeLimitSeconds > 0 ? Math.Min(value, options.TimeLimitSeconds) : value;
            }
        }
        public double CharactersPerMinute { get { return CorrectCount * 60.0 / Math.Max(1, ElapsedSeconds); } }
        public bool IsRunning { get { return running; } }
        public bool HasStarted { get { return started; } }
        public bool IsFinished { get { return Reason != FinishReason.None; } }
        public FinishReason Reason { get; private set; }
        void RunClock(bool run)
        {
            if (running == run) return;
            if (running) elapsed += Math.Max(0, time.Now - last);
            running = run;
            if (run) last = time.Now;
        }
        void Finish(FinishReason reason)
        {
            double at = ElapsedSeconds;
            RunClock(false);
            elapsed = at;
            Reason = reason;
        }
        public void Tick()
        {
            if (!IsFinished && started && options.TimeLimitSeconds > 0 && ElapsedSeconds >= options.TimeLimitSeconds)
                Finish(options.Game == GameKind.Race ? FinishReason.Lost : FinishReason.TimeUp);
        }
        public void SetActive(bool value)
        {
            Tick();
            active = value;
            RunClock(!IsFinished && started && (active || options.CountWhileUnfocused));
        }
        public bool Commit(string text)
        {
            Tick();
            if (IsFinished || string.IsNullOrEmpty(text)) return false;
            try { text = text.Replace("\r\n", "\n").Replace('\r', '\n').Normalize(NormalizationForm.FormC); }
            catch (ArgumentException) { return false; }
            bool changed = false;
            foreach (string unit in PracticeText.Elements(text))
            {
                if (IsFinished) break;
                if ((unit == "\n" && !options.IsCode) || (unit != "\n" && char.IsControl(unit, 0))) continue;
                if (!started) { started = true; RunClock(active || options.CountWhileUnfocused); }
                int index = Position;
                bool correct = unit == TargetUnits[index];
                InputUnits.Add(unit);
                Attempts++;
                if (correct)
                {
                    correctAttempts++;
                    if (rewarded.Add(index))
                    {
                        Combo++;
                        BestCombo = Math.Max(BestCombo, Combo);
                        Score += options.Game == GameKind.Combo ? Math.Min(5, 1 + Combo / 10) : 1;
                    }
                }
                else
                {
                    TotalMistakes++; Combo = 0;
                    string token = PracticeText.MistakeToken(TargetUnits, index);
                    if (!string.IsNullOrWhiteSpace(token)) MissedTokens.Add(token);
                }
                changed = true;
                if (!correct && options.MistakeLimit > 0 && TotalMistakes >= options.MistakeLimit)
                    Finish(FinishReason.Lost);
                else if (Position >= TargetUnits.Count)
                {
                    bool badCopy = (options.Game == GameKind.Memory || options.Game == GameKind.Race || options.Game == GameKind.Revenge) && ErrorCount > 0;
                    bool lowComboAccuracy = options.Game == GameKind.Combo && Accuracy < 90;
                    Finish(badCopy || lowComboAccuracy ? FinishReason.Lost : FinishReason.Completed);
                }
            }
            return changed;
        }
        public bool Backspace(bool word)
        {
            Tick();
            if (Position == 0) return false;
            if (IsFinished)
            {
                if (Reason != FinishReason.Completed || options.Game != GameKind.None || options.TimeLimitSeconds > 0) return false;
                Reason = FinishReason.None;
            }
            int end = Position - 1;
            if (word)
            {
                end = Position;
                while (end > 0 && string.IsNullOrWhiteSpace(InputUnits[end - 1])) end--;
                if (end > 0 && PracticeText.IsWord(InputUnits[end - 1]))
                    while (end > 0 && PracticeText.IsWord(InputUnits[end - 1])) end--;
                else if (end > 0) end--;
            }
            InputUnits.RemoveRange(end, Position - end);
            RunClock(active || options.CountWhileUnfocused);
            return true;
        }
    }
    public struct GlyphCell
    {
        public int Column, Row, Span;
        public GlyphCell(int col, int row, int span) { Column = col; Row = row; Span = span; }
    }
    public static class WordLayout
    {
        public static List<GlyphCell> Build(IList<string> units, int columns)
        {
            columns = Math.Max(1, columns);
            var cells = new List<GlyphCell>();
            int col = 0, row = 0;
            for (int i = 0; i < units.Count; i++)
            {
                string unit = units[i]; int span = PracticeText.CellSpan(unit);
                bool boundary = unit == " " || unit == "\n" || "，。！？；：、）》」』】".Contains(unit);
                if (col > 0 && col + span > columns && !(boundary && col <= columns)) { row++; col = 0; }
                if (PracticeText.IsWord(unit) && (i == 0 || !PracticeText.IsWord(units[i - 1])))
                {
                    int length = 0, j = i;
                    while (j < units.Count && PracticeText.IsWord(units[j])) { length += PracticeText.CellSpan(units[j]); j++; }
                    if (length <= columns && col > 0 && col + length > columns) { row++; col = 0; }
                }
                cells.Add(new GlyphCell(col, row, span));
                if (unit == "\n") { row++; col = 0; }
                else col += span;
            }
            cells.Add(new GlyphCell(col, row, 1));
            return cells;
        }
    }
}
