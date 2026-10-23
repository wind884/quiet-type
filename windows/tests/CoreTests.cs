using System;
using QuietType;
class ManualTime : ITimeSource { public double Value; public double Now { get { return Value; } } }
class CoreTests
{
    static int passed, failed;
    static void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
    static void Run(string name, Action a)
    { try { a(); passed++; Console.WriteLine("PASS " + name); } catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.Message); } }
    static void Reject(Action a) { try { a(); } catch (ArgumentException) { return; } throw new Exception("Expected validation error"); }
    static int Main()
    {
        Run("IME committed Chinese is counted as characters", delegate {
            var s = new TypingSession("你好，世界", new SessionOptions());
            s.Commit("你好"); Check(s.Typed == "你好" && s.Position == 2 && s.Attempts == 2, "Multi-character commit lost or miscounted");
            s.Commit("，世界"); Check(s.IsFinished && s.ErrorCount == 0, "Chinese punctuation was rejected");
        });
        Run("accents and emoji are whole text elements", delegate {
            var s = new TypingSession("á👩‍💻好", new SessionOptions());
            s.Commit("a\u0301👩‍💻"); Check(s.Position == 2 && s.CorrectCount == 2, "Unicode clusters split");
            s.Backspace(false); Check(s.Typed == "á", "Backspace split an emoji");
        });
        Run("only UI full stops are removed so practice keeps punctuation", delegate {
            Check(PracticeText.Normalize("Hello. 你好。", false) == "Hello. 你好。", "Practice punctuation removed");
            Check(PracticeText.Normalize("console.log(1.5);\r\n\treturn 1;", true) == "console.log(1.5);\n    return 1;", "Code punctuation or indentation changed");
        });
        Run("mistakes remain correctable with cumulative accuracy", delegate {
            var s = new TypingSession("hello world", new SessionOptions());
            s.Commit("hexx"); Check(s.ErrorCount == 2 && s.TotalMistakes == 2, "Mistakes skipped");
            s.Backspace(true); s.Commit("hello ");
            Check(s.Typed == "hello " && s.ErrorCount == 0 && s.Accuracy < 100, "Correction reset accuracy");
            Check(s.MissedTokens.Contains("hello"), "Wrong word not captured for revenge");
        });
        Run("ordinary practice pauses while timed tests keep wall time", delegate {
            var t = new ManualTime();
            var s = new TypingSession("abcdef", new SessionOptions(), t);
            s.SetActive(true); s.Commit("a"); t.Value = 5; s.SetActive(false); t.Value = 20;
            Check(Math.Abs(s.ElapsedSeconds - 5) < 0.001, "Practice counted background time");
            s.SetActive(true); t.Value = 22; Check(Math.Abs(s.ElapsedSeconds - 7) < 0.001, "Practice did not resume");
            var u = new TypingSession("abcdef", new SessionOptions { TimeLimitSeconds = 30, CountWhileUnfocused = true }, t);
            u.SetActive(true); u.Commit("a"); t.Value = 27; u.SetActive(false); t.Value = 52; u.Tick();
            Check(u.Reason == FinishReason.TimeUp && u.ElapsedSeconds == 30, "Test paused or missed its deadline");
            Check(!u.Commit("b") && !u.Backspace(false), "Timed result remained editable");
        });
        Run("sudden death fails on first wrong committed character", delegate {
            var s = new TypingSession("中国文字", new SessionOptions { Game = GameKind.SuddenDeath, MistakeLimit = 1 });
            s.Commit("中错后续"); Check(s.Reason == FinishReason.Lost && s.Position == 2, "Failed round kept accepting text");
            Check(!s.Backspace(false), "Lost game was reversible without restart");
        });
        Run("combo multiplier rewards progress without backspace farming", delegate {
            var s = new TypingSession(new string('a', 40), new SessionOptions { Game = GameKind.Combo });
            s.Commit(new string('a', 20)); int score = s.Score;
            Check(s.Combo == 20 && s.BestCombo == 20 && score > 20, "Combo multiplier missing");
            s.Backspace(false); s.Commit("a"); Check(s.Score == score && s.BestCombo == 20, "Repeated position farmed score");
            s.Commit("x"); Check(s.Combo == 0, "Mistake did not reset combo");
        });
        Run("revenge ends after its stated error allowance", delegate {
            var s = new TypingSession("abcdefghi", new SessionOptions { Game = GameKind.Revenge, MistakeLimit = 3 });
            s.Commit("xxx"); Check(s.Reason == FinishReason.Lost && s.TotalMistakes == 3, "Lives never ran out");
        });
        Run("game victories require actual accuracy rather than mashing keys", delegate {
            var race = new TypingSession("abcd", new SessionOptions { Game = GameKind.Race, TimeLimitSeconds = 30 });
            race.Commit("xxxx"); Check(race.Reason == FinishReason.Lost, "Wrong text beat the racing opponent");
            var memory = new TypingSession("abcd", new SessionOptions { Game = GameKind.Memory });
            memory.Commit("xxxx"); Check(memory.Reason == FinishReason.Lost, "Wrong recall won the memory game");
            var combo = new TypingSession("abcd", new SessionOptions { Game = GameKind.Combo });
            combo.Commit("xxxx"); Check(combo.Reason == FinishReason.Lost, "Zero-accuracy combo cleared the challenge");
        });
        Run("completion can be corrected only in free practice", delegate {
            var s = new TypingSession("ab", new SessionOptions()); s.Commit("ab");
            Check(s.Reason == FinishReason.Completed, "Passage not completed");
            Check(s.Backspace(false) && !s.IsFinished && s.Typed == "a", "Free practice completion cannot be corrected");
            var g = new TypingSession("ab", new SessionOptions { Game = GameKind.Combo }); g.Commit("ab");
            Check(!g.Backspace(false), "Finished game score remained editable");
        });
        Run("code preserves real Enter and indentation positions", delegate {
            var s = new TypingSession("if (ok) {\n    run();\n}", new SessionOptions { IsCode = true });
            s.Commit("if (ok) {\n    run();\n}"); Check(s.IsFinished && s.ErrorCount == 0, "Code newline could not be typed");
            var cells = WordLayout.Build(PracticeText.Elements("ab\n    c"), 20);
            Check(cells[3].Row == 1 && cells[7].Column == 4, "Indent layout was collapsed");
        });
        Run("mixed-width layout wraps CJK and full-line separators", delegate {
            var c = WordLayout.Build(PracticeText.Elements("中文ab"), 5);
            Check(c.Count == 5 && c[1].Column == 2 && c[2].Row == 1, "CJK width or Latin word wrapping wrong");
            var d = WordLayout.Build(PracticeText.Elements("abc def"), 3);
            Check(d[3].Row == 0 && d[4].Row == 1, "Full-line separator inserted an empty row");
        });
        Run("blank invalid and oversized content fail without partial text", delegate {
            Reject(delegate { PracticeText.Normalize(" \t\r\n", false); });
            Reject(delegate { PracticeText.Normalize("\ud800", false); });
            Reject(delegate { PracticeText.Normalize(new string('中', 40001), false); });
            Check(PracticeText.Shorten("你好世界👩‍💻", 3, false) == "你好世", "Short text broke element boundaries");
        });
        Run("unsupported right-to-left targets are rejected rather than garbled", delegate {
            Reject(delegate { PracticeText.Normalize("مرحبا بالعالم", false); });
            Reject(delegate { PracticeText.Normalize("שלום", false); });
        });
        Console.WriteLine(passed + " passed, " + failed + " failed"); return failed == 0 ? 0 : 1;
    }
}
