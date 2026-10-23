using System;
using System.Collections.Generic;

namespace QuietType
{
    public enum Section { English, Chinese, Custom, Exams, Test, Code, Games }
    public enum GameKind { None, Combo, SuddenDeath, Memory, Race, Revenge }
    public enum FinishReason { None, Completed, TimeUp, Lost }
    public enum Workspace { Reading, Exams, Code, Games, Test, Library }

    public sealed class Lesson
    {
        public string Id = "", Title = "", Language = "英语", ShortText = "", LongText = "", Description = "", SourceInfo = "";
        public string TitleEn = "", Category = "", CategoryEn = "", DescriptionEn = "", SourceInfoEn = "";
        public Section Section;
        public bool IsCode;
        public GameKind Game;
        public override string ToString() { return Title; }
    }
    public sealed class SavedText
    {
        public string Id = "", Title = "", Language = "", Text = "";
        public override string ToString() { return Language + " / " + Title; }
    }
    public sealed class ScoreRecord
    {
        public string Id = "", WhenIso = "", Section = "", Title = "", TitleEn = "", Language = "", LengthMode = "", Result = "";
        public double Cpm, Accuracy, Seconds;
        public int Score, BestCombo, TypedCount, CorrectCount;
    }
    public sealed class MistakeItem
    {
        public string Text = "";
        public int Count;
    }
    public sealed class UserData
    {
        public List<SavedText> Texts = new List<SavedText>();
        public List<ScoreRecord> Results = new List<ScoreRecord>();
        public List<MistakeItem> Mistakes = new List<MistakeItem>();
        public int FontSize = 15;
        public string UiLanguage = "zh";
    }
    public sealed class SessionOptions
    {
        public GameKind Game;
        public int TimeLimitSeconds, MistakeLimit;
        public bool CountWhileUnfocused, IsCode;
        public int StageCount = 3;
    }
    public interface ITimeSource { double Now { get; } }
}
