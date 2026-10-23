using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace QuietType
{
    public sealed class UserStore
    {
        private const long MaximumBytes = 4L * 1024 * 1024;
        private readonly string directory;
        private readonly string file;
        private bool needsPreservedCopy;
        private bool loadAttempted;
        private readonly XmlSerializer serializer = new XmlSerializer(typeof(UserData));

        public UserStore(string directory)
        {
            if (String.IsNullOrWhiteSpace(directory)) throw new ArgumentException("需要本机数据目录", "directory");
            this.directory = Path.GetFullPath(directory);
            file = Path.Combine(this.directory, "userdata.xml");
            Data = new UserData(); LastError = "";
        }

        public UserData Data { get; private set; }
        public string LastError { get; private set; }

        public bool Load()
        {
            loadAttempted = true;
            try
            {
                if (!File.Exists(file)) { LastError = ""; return true; }
                UserData loaded;
                using (FileStream stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length > MaximumBytes) throw new InvalidDataException("数据文件超过 4 MB");
                    XmlReaderSettings settings = new XmlReaderSettings();
                    settings.DtdProcessing = DtdProcessing.Prohibit;
                    settings.XmlResolver = null;
                    settings.MaxCharactersInDocument = MaximumBytes;
                    using (XmlReader reader = XmlReader.Create(stream, settings))
                        loaded = (UserData)serializer.Deserialize(reader);
                }
                if (loaded == null) throw new InvalidDataException("数据文件为空");
                Repair(loaded);
                Data = loaded; needsPreservedCopy = false; LastError = "";
                return true;
            }
            catch (Exception ex)
            {
                if (!IsStorageException(ex)) throw;
                needsPreservedCopy = true;
                LastError = "无法读取本机记录，仍可继续练习，保存时会保留原文件副本";
                return false;
            }
        }

        public bool Save()
        {
            string temporary = null;
            try
            {
                Repair(Data);
                byte[] bytes;
                using (MemoryStream buffer = new MemoryStream())
                {
                    XmlWriterSettings settings = new XmlWriterSettings();
                    settings.Encoding = new UTF8Encoding(false);
                    settings.Indent = true;
                    settings.NewLineHandling = NewLineHandling.Entitize;
                    using (XmlWriter writer = XmlWriter.Create(buffer, settings)) serializer.Serialize(writer, Data);
                    if (buffer.Length > MaximumBytes)
                    {
                        LastError = "本机记录超过 4 MB，当前更改仍保留在内存";
                        return false;
                    }
                    bytes = buffer.ToArray();
                }
                Directory.CreateDirectory(directory);
                temporary = Path.Combine(directory, ".userdata-" + Guid.NewGuid().ToString("N") + ".tmp");
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(file))
                {
                    // An explicitly requested save must preserve an unread or damaged original first
                    if (needsPreservedCopy || !loadAttempted)
                    {
                        string preserved = Path.Combine(directory, "userdata.bad-" +
                            DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture) + "-" +
                            Guid.NewGuid().ToString("N") + ".xml");
                        File.Copy(file, preserved, false);
                    }
                    File.Replace(temporary, file, null);
                }
                else File.Move(temporary, file);
                temporary = null;
                needsPreservedCopy = false; loadAttempted = true; LastError = "";
                return true;
            }
            catch (Exception ex)
            {
                if (!IsStorageException(ex)) throw;
                LastError = SaveFailure(ex);
                return false;
            }
            finally
            {
                if (temporary != null)
                {
                    try { File.Delete(temporary); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        public bool SaveText(SavedText text)
        {
            if (text == null || String.IsNullOrWhiteSpace(text.Title) || String.IsNullOrWhiteSpace(text.Language)
                || String.IsNullOrWhiteSpace(text.Text))
            {
                LastError = "请填写标题、语言和正文"; return false;
            }
            if (StringInfo.ParseCombiningCharacters(text.Text).Length > 40000)
            {
                LastError = "正文最多 40000 个字符"; return false;
            }
            Repair(Data);
            if (String.IsNullOrWhiteSpace(text.Id)) text.Id = Guid.NewGuid().ToString("N");
            SavedText saved = new SavedText { Id = text.Id, Title = text.Title.Trim(), Language = text.Language.Trim(), Text = text.Text };
            int index = Data.Texts.FindIndex(delegate(SavedText item) { return String.Equals(item.Id, saved.Id, StringComparison.Ordinal); });
            if (index < 0) Data.Texts.Insert(0, saved); else Data.Texts[index] = saved;
            return Save();
        }

        public bool DeleteText(string id)
        {
            if (String.IsNullOrEmpty(id)) { LastError = "请先选择一篇文本"; return false; }
            Repair(Data);
            Data.Texts.RemoveAll(delegate(SavedText item) { return String.Equals(item.Id, id, StringComparison.Ordinal); });
            return Save();
        }

        public bool Record(ScoreRecord record, IEnumerable<string> missedTokens)
        {
            if (record == null) { LastError = "没有可保存的成绩"; return false; }
            Repair(Data);
            if (String.IsNullOrWhiteSpace(record.Id)) record.Id = Guid.NewGuid().ToString("N");
            if (String.IsNullOrWhiteSpace(record.WhenIso)) record.WhenIso = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            Data.Results.RemoveAll(delegate(ScoreRecord item) { return String.Equals(item.Id, record.Id, StringComparison.Ordinal); });
            Data.Results.Insert(0, CopyRecord(record));
            if (missedTokens != null)
            {
                Dictionary<string, MistakeItem> lookup = new Dictionary<string, MistakeItem>(StringComparer.Ordinal);
                foreach (MistakeItem item in Data.Mistakes) lookup[item.Text] = item;
                foreach (string token in missedTokens)
                {
                    if (String.IsNullOrEmpty(token)) continue;
                    MistakeItem item;
                    if (lookup.TryGetValue(token, out item)) item.Count = Increment(item.Count, 1);
                    else
                    {
                        item = new MistakeItem { Text = token, Count = 1 };
                        lookup.Add(token, item); Data.Mistakes.Add(item);
                    }
                }
            }
            return Save();
        }

        public bool ClearMistakes(IEnumerable<string> tokens)
        {
            Repair(Data);
            if (tokens == null) Data.Mistakes.Clear();
            else
            {
                HashSet<string> selected = new HashSet<string>(tokens, StringComparer.Ordinal);
                Data.Mistakes.RemoveAll(delegate(MistakeItem item) { return selected.Contains(item.Text); });
            }
            return Save();
        }

        private static void Repair(UserData data)
        {
            if (data.Texts == null) data.Texts = new List<SavedText>();
            if (data.Results == null) data.Results = new List<ScoreRecord>();
            if (data.Mistakes == null) data.Mistakes = new List<MistakeItem>();
            data.FontSize = Math.Max(13, Math.Min(21, data.FontSize));
            data.UiLanguage = data.UiLanguage == "en" ? "en" : "zh";
            HashSet<string> textIds = new HashSet<string>(StringComparer.Ordinal);
            List<SavedText> texts = new List<SavedText>();
            foreach (SavedText item in data.Texts)
            {
                if (item == null) continue;
                if (String.IsNullOrWhiteSpace(item.Id)) item.Id = Guid.NewGuid().ToString("N");
                item.Title = item.Title ?? ""; item.Language = item.Language ?? ""; item.Text = item.Text ?? "";
                if (textIds.Add(item.Id)) texts.Add(item);
            }
            data.Texts = texts;
            HashSet<string> recordIds = new HashSet<string>(StringComparer.Ordinal);
            List<ScoreRecord> results = new List<ScoreRecord>();
            foreach (ScoreRecord item in data.Results)
            {
                if (item == null) continue;
                if (String.IsNullOrWhiteSpace(item.Id)) item.Id = Guid.NewGuid().ToString("N");
                item.WhenIso = item.WhenIso ?? ""; item.Section = item.Section ?? ""; item.Title = item.Title ?? "";
                item.TitleEn = item.TitleEn ?? ""; item.Language = item.Language ?? ""; item.LengthMode = item.LengthMode ?? ""; item.Result = item.Result ?? "";
                if (Double.IsNaN(item.Cpm) || Double.IsInfinity(item.Cpm) || item.Cpm < 0) item.Cpm = 0;
                if (Double.IsNaN(item.Accuracy) || Double.IsInfinity(item.Accuracy) || item.Accuracy < 0) item.Accuracy = 0;
                if (Double.IsNaN(item.Seconds) || Double.IsInfinity(item.Seconds) || item.Seconds < 0) item.Seconds = 0;
                if (results.Count < 100 && recordIds.Add(item.Id)) results.Add(item);
            }
            data.Results = results;
            Dictionary<string, MistakeItem> mistakes = new Dictionary<string, MistakeItem>(StringComparer.Ordinal);
            foreach (MistakeItem item in data.Mistakes)
            {
                if (item == null || String.IsNullOrEmpty(item.Text)) continue;
                MistakeItem found;
                if (mistakes.TryGetValue(item.Text, out found)) found.Count = Increment(found.Count, Math.Max(1, item.Count));
                else mistakes.Add(item.Text, new MistakeItem { Text = item.Text, Count = Math.Max(1, item.Count) });
            }
            List<MistakeItem> ordered = new List<MistakeItem>(mistakes.Values);
            ordered.Sort(delegate(MistakeItem a, MistakeItem b) {
                int count = b.Count.CompareTo(a.Count); return count == 0 ? StringComparer.Ordinal.Compare(a.Text, b.Text) : count;
            });
            if (ordered.Count > 200) ordered.RemoveRange(200, ordered.Count - 200);
            data.Mistakes = ordered;
        }

        private static ScoreRecord CopyRecord(ScoreRecord item)
        {
            return new ScoreRecord { Id = item.Id, WhenIso = item.WhenIso, Section = item.Section, Title = item.Title,
                TitleEn = item.TitleEn, Language = item.Language, LengthMode = item.LengthMode, Result = item.Result, Cpm = item.Cpm,
                Accuracy = item.Accuracy, Seconds = item.Seconds, Score = item.Score, BestCombo = item.BestCombo,
                TypedCount = item.TypedCount, CorrectCount = item.CorrectCount };
        }
        private static int Increment(int value, int amount)
        {
            return value >= Int32.MaxValue - amount ? Int32.MaxValue : value + amount;
        }
        private static bool IsStorageException(Exception ex)
        {
            return ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is InvalidOperationException
                || ex is XmlException || ex is ArgumentException || ex is System.Security.SecurityException
                || ex is NotSupportedException;
        }
        private static string SaveFailure(Exception ex)
        {
            if (ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
                return "没有本机数据目录的写入权限，当前更改仍保留在内存";
            if (ex is InvalidOperationException || ex is XmlException || ex is ArgumentException)
                return "记录中有无法保存的字符或数据，当前更改仍保留在内存";
            return "无法写入本机记录，请检查目录权限或文件占用，当前更改仍保留在内存";
        }
    }
}
