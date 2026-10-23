using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using QuietType;
class FullCatalogTests
{
    static int checks;
    static void Check(bool value,string why) { checks++;if(!value)throw new Exception(why); }
    static int Words(string value) { return Regex.Matches(value,@"\S+").Count; }
    static void Range(int n,int low,int high,string why) { Check(n>=low&&n<=high,why+" length "+n); }
    static int Main()
    {
        try {
            var index=ContentIndex.Builtin();Check(index.All.Count==201,"Expected 201 paired lessons, got "+index.All.Count);
            var ids=new HashSet<string>();var bodies=new HashSet<string>();var titles=new HashSet<string>();
            var categories=new Dictionary<string,int>();int en=0,zh=0,games=0;
            foreach(var text in index.All) {
                Check(ids.Add(text.Id),"Duplicate ID "+text.Id);Check(bodies.Add(text.LongText),"Duplicate body "+text.Id);
                Check(text.ShortText.Length>0&&text.LongText.Length>text.ShortText.Length,"Missing length variant "+text.Id);
                Check(!string.IsNullOrEmpty(text.TitleEn)&&!string.IsNullOrEmpty(text.DescriptionEn),"Missing English metadata "+text.Id);
                Check(PracticeText.Normalize(text.LongText,text.IsCode)==text.LongText,"Invalid normalization "+text.Id);
                if(text.Id.StartsWith("reading-en-")||text.Id.StartsWith("reading-zh-")) {
                    Check(titles.Add(text.Title),"Duplicate new title "+text.Title);Check(text.LongText.StartsWith(text.ShortText,StringComparison.Ordinal),"Short text is not an excerpt "+text.Id);
                    bool english=text.Id.StartsWith("reading-en-");string key=(english?"en":"zh")+"/"+text.CategoryEn;
                    if(!categories.ContainsKey(key))categories[key]=0;categories[key]++;
                    if(english) { en++;Range(Words(text.ShortText),50,80,text.Id+" short");Range(Words(text.LongText),160,230,text.Id+" long");Check(!Regex.IsMatch(text.LongText,@"[\u3400-\u9fff]"),"CJK inside English target "+text.Id); }
                    else { zh++;Range(text.ShortText.Length,90,150,text.Id+" short");Range(text.LongText.Length,300,480,text.Id+" long"); }
                }
                if(text.Section==Section.Games) { games++;Check(text.Id.StartsWith("challenge-"),"Unexpected challenge entry");Check(!string.IsNullOrEmpty(text.SourceInfoEn),"Missing game translation "+text.Id); }
            }
            Check(en==65&&zh==65&&games==40,"New catalogue counts wrong");
            foreach(string lang in new[]{"en","zh"})foreach(string category in new[]{"Everyday","Travel","Nature","Science","Cities","People","Food","Mystery"})
                Check(categories[lang+"/"+category]==(category=="Everyday"?9:8),"Category not balanced "+lang+category);
            foreach(GameKind game in Enum.GetValues(typeof(GameKind)))if(game!=GameKind.None)foreach(string lang in new[]{"en","zh"}) {
                var entries=index.Query(Workspace.Games,lang,game.ToString(),"",null);Check(entries.Count==4,"Missing game-language scenarios "+game+lang);
                foreach(var text in entries) {
                    Check(text.Game==game&&!text.IsCode,"Wrong game metadata "+text.Id);
                    if(lang=="en") { Check(!Regex.IsMatch(text.LongText,@"[\u3400-\u9fff]"),"English game contains CJK");Range(Words(text.ShortText),game==GameKind.Memory?8:35,game==GameKind.Memory?12:55,text.Id+" short");Range(Words(text.LongText),game==GameKind.Memory?20:110,game==GameKind.Memory?28:170,text.Id+" long"); }
                    else { Range(text.ShortText.Length,game==GameKind.Memory?24:80,game==GameKind.Memory?36:140,text.Id+" short");Range(text.LongText.Length,game==GameKind.Memory?65:250,game==GameKind.Memory?90:400,text.Id+" long"); }
                }
            }
            Check(index.Languages(Workspace.Reading,null).Count==8,"Expected eight reading languages");
            Check(index.Query(Workspace.Test,"mixed","","",null).Count==1,"Mixed test missing");
            Check(index.Query(Workspace.Exams,"en","IELTS Task 1","",null)[0].TitleEn.Contains("Library"),"Chart mistranslated");
            Console.WriteLine("PASS full catalogue: 201 paired lessons, "+checks+" assertions");return 0;
        }catch(Exception ex) { Console.WriteLine("FAIL catalogue: "+ex);return 1; }
    }
}
