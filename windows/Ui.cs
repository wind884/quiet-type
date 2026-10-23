using System;
using System.Collections.Generic;
namespace QuietType
{
    public static class Ui
    {
        public static string Pick(bool english,string chinese,string translated) { return english ? translated : chinese; }
        public static string Language(string raw,bool english)
        {
            switch(raw)
            {
                case "en": case "英语": return Pick(english,"英语","English");
                case "zh": case "中文": return Pick(english,"中文","Chinese");
                case "ja": case "日本語": return Pick(english,"日语","Japanese");
                case "ko": case "한국어": return Pick(english,"韩语","Korean");
                case "fr": case "Français": return Pick(english,"法语","French");
                case "es": case "Español": return Pick(english,"西班牙语","Spanish");
                case "de": case "Deutsch": return Pick(english,"德语","German");
                case "ru": case "Русский": return Pick(english,"俄语","Russian");
                case "mixed": case "混合": return Pick(english,"混合文本","Mixed text");
                default: return raw ?? "";
            }
        }
        public static string GameName(GameKind game,bool english)
        {
            switch(game)
            {
                case GameKind.Combo: return Pick(english,"连击闯关","Combo run");
                case GameKind.SuddenDeath: return Pick(english,"一命通关","One life");
                case GameKind.Memory: return Pick(english,"记忆闪打","Memory flash");
                case GameKind.Race: return Pick(english,"影子赛跑","Ghost race");
                case GameKind.Revenge: return Pick(english,"错字复仇","Mistake rematch");
                default: return "";
            }
        }
        public static string PageName(Workspace page,bool english)
        {
            switch(page)
            {
                case Workspace.Reading: return Pick(english,"日常练习","Daily practice");
                case Workspace.Exams: return Pick(english,"英语考试","Exam writing");
                case Workspace.Code: return Pick(english,"程序员","Code");
                case Workspace.Games: return Pick(english,"趣味挑战","Challenges");
                case Workspace.Test: return Pick(english,"计时测试","Timed test");
                case Workspace.Library: return Pick(english,"我的文本","My texts");
                default: return "";
            }
        }
        static readonly Dictionary<string,string> Messages = new Dictionary<string,string>(StringComparer.Ordinal) {
            {"请先输入练习文本","Enter some practice text first"},
            {"文本含有不完整字符，请重新复制","The text contains an incomplete character — please copy it again"},
            {"暂不支持从右向左排版的文本，可使用中文、日韩文及从左向右的其他语言","Right-to-left scripts are not supported — use Chinese, Japanese, Korean or another left-to-right language"},
            {"文本中有无法输入的控制字符","The text contains a control character that cannot be typed"},
            {"文本最多支持 40,000 个字符","Use no more than 40,000 characters"},
            {"无法读取本机记录，仍可继续练习，保存时会保留原文件副本","Local records could not be read — practice is still available and the original file will be preserved"},
            {"本机记录超过 4 MB，当前更改仍保留在内存","Local data exceeds 4 MB — current edits remain in memory"},
            {"请填写标题、语言和正文","Fill in the title, language and text"},
            {"正文最多 40000 个字符","Use no more than 40,000 characters"},
            {"请先选择一篇文本","Select a saved text first"},
            {"没有可保存的成绩","There is no result to save"},
            {"没有本机数据目录的写入权限，当前更改仍保留在内存","The local data folder is not writable — current edits remain in memory"},
            {"记录中有无法保存的字符或数据，当前更改仍保留在内存","Some record data could not be saved — current edits remain in memory"},
            {"无法写入本机记录，请检查目录权限或文件占用，当前更改仍保留在内存","Local records could not be saved — check folder access or file locks; current edits remain in memory"},
            {"有文本暂时无法练习，可在文本库中修改","Some saved text cannot be used — edit it in My texts"},
            {"中途结束","Stopped early"}, {"阅读练习","Daily practice"}, {"日常练习","Daily practice"}, {"英语考试","Exam writing"},
            {"程序员","Code"}, {"趣味挑战","Challenges"}, {"计时测试","Timed test"}, {"我的文本","My texts"},
            {"英语","English"}, {"中文","Chinese"}, {"自定义","Custom"}, {"考试范文","Exam models"}, {"打字测试","Timed test"},
            {"短篇速练","Short practice"}, {"长篇精练","Long practice"},
            {"短篇","Short"}, {"长篇","Long"}, {"完成","Finished"}, {"失败","Lost"},
            {"测试结束","Test finished"}, {"错字未改","Uncorrected errors"}, {"影子先到了","The ghost finished first"},
            {"记忆有误","Recall errors"}, {"准确率未达90%","Accuracy below 90%"}, {"错字未清完","Mistakes remain"},
            {"挑战失败","Challenge lost"}, {"挑战完成","Challenge complete"}, {"练习完成","Practice complete"},
            {"练习中","In progress"}, {"原创练习素材","Original practice text"}, {"原创练习范文","Original model answer"},
            {"本机文本库","Your saved text"}, {"连击闯关","Combo run"}, {"一命通关","One life"},
            {"记忆闪打","Memory flash"}, {"影子赛跑","Ghost race"}, {"错字复仇","Mistake rematch"}
        };
        public static string Error(string raw,bool english)
        {
            if (!english || string.IsNullOrEmpty(raw)) return raw ?? "";
            string result;
            if (Messages.TryGetValue(raw,out result)) return result;
            foreach(char c in raw) if(c >= '\u3400' && c <= '\u9fff') return "This text or local data could not be used — please check it and try again";
            return raw;
        }
        public static string RecordText(string raw,bool english)
        {
            if(string.IsNullOrEmpty(raw)) return "";
            if(raw=="阅读练习"||raw=="Reading"||raw=="日常练习"||raw=="Daily practice") return Pick(english,"日常练习","Daily practice");
            if(english)
            {
                string result;
                if(Messages.TryGetValue(raw,out result)) return result;
                int split=raw.IndexOf(" / ",StringComparison.Ordinal);
                if(split>0) return RecordText(raw.Substring(0,split),true)+" / "+RecordText(raw.Substring(split+3),true);
                return raw;
            }
            foreach(var item in Messages) if(item.Value==raw) return item.Key;
            return raw;
        }
    }
    public sealed class Choice
    {
        public readonly string Key,Zh,En;
        public Choice(string key,string zh,string en) { Key=key;Zh=zh;En=en; }
        public string Text(bool english) { return Ui.Pick(english,Zh,En); }
        public override string ToString() { return Zh; }
    }
}
