using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace QuietType
{
    public static class StorageTests
    {
        private static int passed;
        private static string runDirectory;

#if STORAGE_TESTS
        public static int Main() { return Run(); }
#endif

        public static int Run()
        {
            passed = 0;
            runDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "storage-tests", "run-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(runDirectory);
            try
            {
                Check("Unicode text and settings survive a new store instance", UnicodeRoundTrip);
                Check("Matching score ids update instead of duplicate", RecordUpdates);
                Check("Mistake tokens count ordinal occurrences and stay bounded", MistakesAndCaps);
                Check("Null collections and fields are repaired", RepairsMissingData);
                Check("Malformed data is retained before explicit recovery", MalformedSafety);
                Check("DTDs are rejected without resolving local entities", DtdSafety);
                Check("Oversized files are rejected without replacement", SizeLimit);
                Check("IO failures retain unsaved in-memory changes", IoFailureRetention);
                Check("A locked destination preserves the last good file", AtomicFailureRetention);
                Check("Invalid custom texts cannot enter the library", InvalidTexts);
                Console.WriteLine("Storage tests passed: " + passed.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine("Test data: " + runDirectory);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL " + ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
                return 1;
            }
        }

        private static void Check(string name, Action test)
        {
            test(); passed++; Console.WriteLine("PASS " + name);
        }
        private static void Require(bool condition, string failure)
        {
            if (!condition) throw new InvalidOperationException(failure);
        }
        private static string NewDirectory(string name)
        {
            string path = Path.Combine(runDirectory, name);
            Directory.CreateDirectory(path); return path;
        }
        private static string DataFile(string directory) { return Path.Combine(directory, "userdata.xml"); }
        private static ScoreRecord Score(string id, int value)
        {
            return new ScoreRecord { Id = id, WhenIso = "2026-10-22T18:02:03.0000000Z", Section = "中文",
                Title = "日常练习", Language = "中文", LengthMode = "短篇", Result = "完成", Cpm = 123.4,
                Accuracy = 98.5, Seconds = 42, Score = value, BestCombo = 13, TypedCount = 20, CorrectCount = 19 };
        }

        private static void UnicodeRoundTrip()
        {
            string path = NewDirectory("unicode");
            UserStore store = new UserStore(path);
            Require(store.Load(), "An absent data file should start an empty library");
            store.Data.FontSize = 21;
            string text = "你好，世界。 e\u0301 — 日本語！ العربية؟ 😀\r\nKeep punctuation: <tag> & \"quotes\".";
            Require(store.SaveText(new SavedText { Id = "u", Title = "多语言 / 你好", Language = "中文・日本語", Text = text }), store.LastError);
            Require(store.Record(Score("result-1", 300), new string[] { "é", "错", "😀" }), store.LastError);
            UserStore reload = new UserStore(path);
            Require(reload.Load(), reload.LastError);
            Require(reload.Data.Texts.Count == 1 && reload.Data.Texts[0].Text == text, "Unicode or punctuation changed in persistence");
            Require(reload.Data.Texts[0].Language == "中文・日本語" && reload.Data.FontSize == 21, "Settings or language were lost");
            Require(reload.Data.Results[0].Score == 300 && reload.Data.Results[0].Accuracy == 98.5, "Score metrics changed");
            Require(reload.DeleteText("u"), reload.LastError);
            Require(new UserStoreWithLoad(path).Value.Data.Texts.Count == 0, "Deleted text remained on disk");
        }

        private static void RecordUpdates()
        {
            UserStore store = new UserStore(NewDirectory("records"));
            Require(store.Record(Score("same", 10), null), store.LastError);
            Require(store.Record(Score("other", 30), null), store.LastError);
            Require(store.Record(Score("same", 25), null), store.LastError);
            Require(store.Data.Results.Count == 2 && store.Data.Results[0].Id == "same" && store.Data.Results[0].Score == 25,
                "A repeated result id should replace its record and be recent");
            for (int i = 0; i < 110; i++) store.Data.Results.Add(Score("old-" + i.ToString(CultureInfo.InvariantCulture), i));
            Require(store.Save(), store.LastError);
            Require(store.Data.Results.Count == 100 && store.Data.Results[0].Id == "same", "Recent score retention exceeded its cap");
        }

        private static void MistakesAndCaps()
        {
            string path = NewDirectory("mistakes");
            UserStore store = new UserStore(path);
            List<string> tokens = new List<string>(new string[] { "repeat", "repeat", "repeat", "Repeat", "Repeat", "", null });
            for (int i = 0; i < 215; i++) tokens.Add("token-" + i.ToString("000", CultureInfo.InvariantCulture));
            Require(store.Record(Score("m", 1), tokens), store.LastError);
            Require(store.Data.Mistakes.Count == 200, "Mistake bank exceeded 200 entries");
            MistakeItem repeat = store.Data.Mistakes.Find(delegate(MistakeItem m) { return m.Text == "repeat"; });
            MistakeItem capital = store.Data.Mistakes.Find(delegate(MistakeItem m) { return m.Text == "Repeat"; });
            Require(repeat != null && repeat.Count == 3 && capital != null && capital.Count == 2, "Mistakes did not preserve ordinal token counts");
            Require(store.ClearMistakes(new string[] { "repeat" }), store.LastError);
            UserStore reload = new UserStore(path); Require(reload.Load(), reload.LastError);
            Require(reload.Data.Mistakes.Find(delegate(MistakeItem m) { return m.Text == "repeat"; }) == null,
                "Cleared token was restored from disk");
            Require(reload.Data.Mistakes.Find(delegate(MistakeItem m) { return m.Text == "Repeat"; }) != null,
                "Clearing a token should be ordinal");
        }

        private static void RepairsMissingData()
        {
            string path = NewDirectory("repair");
            File.WriteAllText(DataFile(path), "<UserData xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><Texts xsi:nil=\"true\"/><Results><ScoreRecord><Id>x</Id></ScoreRecord></Results><Mistakes xsi:nil=\"true\"/><FontSize>99</FontSize></UserData>", Encoding.UTF8);
            UserStore store = new UserStore(path); Require(store.Load(), store.LastError);
            Require(store.Data.Texts != null && store.Data.Mistakes != null && store.Data.Results[0].Title == "", "Missing lists or fields were not repaired");
            Require(store.Data.FontSize == 21, "Font size did not clamp to supported maximum");
            store.Data.FontSize = 2; Require(store.Save(), store.LastError);
            Require(store.Data.FontSize == 13, "Font size did not clamp to supported minimum");
        }

        private static void MalformedSafety()
        {
            string path = NewDirectory("malformed"); string broken = "<UserData><Texts>broken";
            File.WriteAllText(DataFile(path), broken, new UTF8Encoding(false));
            UserStore store = new UserStore(path);
            Require(!store.Load() && store.LastError.Length > 0, "Malformed XML was not reported");
            Require(File.ReadAllText(DataFile(path)) == broken && store.Data.Texts.Count == 0, "Load modified a damaged file");
            Require(store.SaveText(new SavedText { Id = "new", Title = "新篇", Language = "中文", Text = "仍可继续练习。" }), store.LastError);
            string[] backups = Directory.GetFiles(path, "userdata.bad-*.xml");
            Require(backups.Length == 1 && File.ReadAllText(backups[0]) == broken, "Recovery did not preserve the damaged original");
            UserStore reload = new UserStore(path); Require(reload.Load() && reload.Data.Texts.Count == 1, "Explicit recovery was not persisted");
        }

        private static void DtdSafety()
        {
            string path = NewDirectory("dtd");
            string secret = Path.Combine(path, "entity.txt"); File.WriteAllText(secret, "private-value");
            string xml = "<!DOCTYPE UserData [<!ENTITY xxe SYSTEM \"" + new Uri(secret).AbsoluteUri + "\">]><UserData><Texts/><Results/><Mistakes/><FontSize>&xxe;</FontSize></UserData>";
            File.WriteAllText(DataFile(path), xml, new UTF8Encoding(false));
            UserStore store = new UserStore(path);
            Require(!store.Load() && store.LastError.Length > 0, "DTD-bearing XML was accepted");
            Require(File.ReadAllText(DataFile(path)) == xml, "DTD rejection modified the source file");
        }

        private static void SizeLimit()
        {
            string path = NewDirectory("oversize"); string file = DataFile(path);
            using (FileStream stream = File.Create(file)) { stream.SetLength(4L * 1024 * 1024 + 1); }
            UserStore store = new UserStore(path);
            Require(!store.Load() && new FileInfo(file).Length == 4L * 1024 * 1024 + 1, "Oversized input was accepted or changed");
        }

        private static void IoFailureRetention()
        {
            string parent = NewDirectory("io"); string blocked = Path.Combine(parent, "blocked"); File.WriteAllText(blocked, "file blocks directory");
            UserStore store = new UserStore(blocked);
            Require(!store.SaveText(new SavedText { Id = "retained", Title = "保留", Language = "中文", Text = "未保存也保留。" }), "Saving into a file should fail");
            Require(store.LastError.Length > 0 && store.Data.Texts.Count == 1 && store.Data.Texts[0].Id == "retained", "Save failure discarded in-memory edits");
            File.Delete(blocked); Require(store.Save(), store.LastError);
            UserStore reload = new UserStore(blocked); Require(reload.Load() && reload.Data.Texts.Count == 1, "Retried save lost the retained edit");
        }

        private static void AtomicFailureRetention()
        {
            string path = NewDirectory("atomic"); UserStore store = new UserStore(path);
            Require(store.Record(Score("first", 1), null), store.LastError);
            string original = File.ReadAllText(DataFile(path));
            using (FileStream held = new FileStream(DataFile(path), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Require(!store.Record(Score("second", 2), null), "Replacing a locked destination should fail");
                Require(store.Data.Results.Count == 2 && store.LastError.Length > 0, "Atomic replace failure lost the in-memory score");
                Require(File.ReadAllText(DataFile(path)) == original, "A failed save changed the last good file");
            }
            Require(Directory.GetFiles(path, "*.tmp").Length == 0, "Failed saves left temporary files");
            Require(store.Save(), store.LastError);
        }

        private static void InvalidTexts()
        {
            UserStore store = new UserStore(NewDirectory("validation"));
            Require(!store.SaveText(new SavedText { Title = " ", Language = "中文", Text = "正文" }), "Blank title was accepted");
            Require(!store.SaveText(new SavedText { Title = "标题", Language = " ", Text = "正文" }), "Blank language was accepted");
            Require(!store.SaveText(new SavedText { Title = "标题", Language = "中文", Text = "\r\n " }), "Blank text was accepted");
            Require(!store.SaveText(new SavedText { Title = "标题", Language = "中文", Text = new string('a', 40001) }), "Oversized custom text was accepted");
            Require(store.Data.Texts.Count == 0, "Invalid custom text changed the library");
        }

        private sealed class UserStoreWithLoad
        {
            public readonly UserStore Value;
            public UserStoreWithLoad(string path) { Value = new UserStore(path); Require(Value.Load(), Value.LastError); }
        }
    }
}
