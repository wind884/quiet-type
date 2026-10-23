using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Json;
namespace QuietType
{
    public sealed class ContentIndex
    {
        public readonly List<Lesson> All = new List<Lesson>();
        public ContentIndex(IEnumerable<Lesson> source)
        {
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var item in source)
            {
                if(string.IsNullOrEmpty(item.Id)||!ids.Add(item.Id)) throw new InvalidDataException("Duplicate or missing lesson id");
                item.ShortText=PracticeText.Normalize(item.ShortText,item.IsCode);
                item.LongText=PracticeText.Normalize(item.LongText,item.IsCode);
                All.Add(item);
            }
        }
        public static ContentIndex Builtin()
        {
            using(Stream input=Assembly.GetExecutingAssembly().GetManifestResourceStream("QuietType.lessons.json"))
            {
                if(input==null)throw new InvalidDataException("Missing lesson catalogue");
                var serializer=new DataContractJsonSerializer(typeof(List<Lesson>));
                return new ContentIndex((List<Lesson>)serializer.ReadObject(input));
            }
        }
        public List<Lesson> Query(Workspace page,string language,string group,string search,IList<SavedText> saved)
        {
            var candidates=new List<Lesson>();
            if(page==Workspace.Library&&saved!=null)
            {
                foreach(var entry in saved)
                {
                    try { candidates.Add(new Lesson{Id="user:"+entry.Id,Title=entry.Title,TitleEn=entry.Title,Language=entry.Language,
                        Section=Section.Custom,ShortText=PracticeText.Shorten(entry.Text,160,false),LongText=PracticeText.Normalize(entry.Text,false),
                        Category="我的文本",CategoryEn="My texts",Description="本机文本库",DescriptionEn="Saved on this device",SourceInfo="本机文本库",SourceInfoEn="Saved on this device"}); }
                    catch(ArgumentException) { }
                }
            }
            else foreach(var item in All)if(InPage(item,page))candidates.Add(item);
            var result=new List<Lesson>(); search=(search??"").Trim();
            foreach(var item in candidates)
            {
                if(!string.IsNullOrEmpty(language)&&LanguageKey(item)!=language)continue;
                if(!string.IsNullOrEmpty(group))
                {
                    string value=page==Workspace.Games?item.Game.ToString():page==Workspace.Exams?ExamGroup(item):item.CategoryEn;
                    if(value!=group)continue;
                }
                if(search.Length>0&&item.Title.IndexOf(search,StringComparison.OrdinalIgnoreCase)<0
                    &&(item.TitleEn??"").IndexOf(search,StringComparison.OrdinalIgnoreCase)<0)continue;
                result.Add(item);
            }
            return result;
        }
        static bool InPage(Lesson item,Workspace page)
        {
            switch(page)
            {
                case Workspace.Reading:return item.Section==Section.English||item.Section==Section.Chinese||item.Section==Section.Custom;
                case Workspace.Exams:return item.Section==Section.Exams;
                case Workspace.Code:return item.Section==Section.Code;
                case Workspace.Games:return item.Section==Section.Games;
                case Workspace.Test:return item.Section==Section.English||item.Section==Section.Chinese||item.Section==Section.Test;
                default:return false;
            }
        }
        public List<Choice> Languages(Workspace page,IList<SavedText> saved)
        {
            var result=new List<Choice>(); var keys=new HashSet<string>(StringComparer.Ordinal);
            var candidates=page==Workspace.Library?Query(page,"","","",saved):All;
            foreach(var item in candidates)
            {
                if(page!=Workspace.Library&&!InPage(item,page))continue;
                string key=LanguageKey(item);
                if(page==Workspace.Test&&key!="en"&&key!="zh"&&key!="mixed")continue;
                if(keys.Add(key))result.Add(new Choice(key,Ui.Language(key,false),Ui.Language(key,true)));
            }
            result.Sort(delegate(Choice a,Choice b){return LanguageOrder(a.Key).CompareTo(LanguageOrder(b.Key));});
            return result;
        }
        static string LanguageOrder(string key)
        { return key=="en"?"0":key=="zh"?"1":key=="mixed"?"2":"3"+key; }
        public List<Choice> Groups(Workspace page)
        {
            var result=new List<Choice>();
            if(page==Workspace.Games)
            {
                foreach(GameKind game in Enum.GetValues(typeof(GameKind)))if(game!=GameKind.None)
                    result.Add(new Choice(game.ToString(),Ui.GameName(game,false),Ui.GameName(game,true)));
                return result;
            }
            result.Add(new Choice("","全部分类","All topics"));
            if(page==Workspace.Exams)
            {
                result[0]=new Choice("","全部题型","All task types");
                foreach(string value in new[]{"IELTS Task 1","IELTS Task 2","TOEFL Email","TOEFL Discussion","CET-4","CET-6"})
                    result.Add(new Choice(value,value=="TOEFL Email"?"托福邮件":value=="TOEFL Discussion"?"托福学术讨论":value,value));
                return result;
            }
            string[] zh={"生活","旅行","自然","科学","城市","人物","饮食","微悬疑"};
            string[] en={"Everyday","Travel","Nature","Science","Cities","People","Food","Mystery"};
            for(int i=0;i<zh.Length;i++)result.Add(new Choice(en[i],zh[i],en[i]));
            return result;
        }
        public static string LanguageKey(Lesson lesson)
        {
            if(lesson.IsCode)return lesson.Language;
            string value=(lesson.Language??"").Trim();
            switch(value.ToLowerInvariant())
            {
                case "英语":case "english":case "en":return "en";
                case "中文":case "chinese":case "zh":return "zh";
                case "日本語":case "日语":case "japanese":case "ja":return "ja";
                case "한국어":case "韩语":case "korean":case "ko":return "ko";
                case "français":case "法语":case "french":case "fr":return "fr";
                case "español":case "西班牙语":case "spanish":case "es":return "es";
                case "deutsch":case "德语":case "german":case "de":return "de";
                case "русский":case "俄语":case "russian":case "ru":return "ru";
                case "混合":case "混合文本":case "混合语言":case "mixed":return "mixed";
                default:return value;
            }
        }
        public static string Title(Lesson lesson,bool english)
        { return english&&!string.IsNullOrEmpty(lesson.TitleEn)?lesson.TitleEn:lesson.Title; }
        public static bool TokenMatches(string token,string language)
        {
            if(string.IsNullOrEmpty(token))return false;
            bool chinese=false,latin=false;
            foreach(char c in token) { if(c>='\u3400'&&c<='\u9fff')chinese=true; if((c>='A'&&c<='Z')||(c>='a'&&c<='z'))latin=true; }
            return language=="zh"?chinese:language=="en"?latin&&!chinese:true;
        }
        static string ExamGroup(Lesson lesson)
        {
            if(lesson.Id.Contains("ielts-chart"))return "IELTS Task 1";
            if(lesson.Id.Contains("ielts-essay"))return "IELTS Task 2";
            if(lesson.Id.Contains("toefl-email"))return "TOEFL Email";
            if(lesson.Id.Contains("toefl-discussion"))return "TOEFL Discussion";
            if(lesson.Id.Contains("cet4"))return "CET-4";
            if(lesson.Id.Contains("cet6"))return "CET-6";
            return lesson.CategoryEn;
        }
    }
}
