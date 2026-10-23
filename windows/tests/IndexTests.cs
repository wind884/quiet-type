using System;
using System.Collections.Generic;
using QuietType;
class IndexTests
{
    static int passed,failed;
    static void Check(bool v,string why) { if(!v)throw new Exception(why); }
    static void Run(string name,Action a) { try{a();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);} }
    static ContentIndex Fixture()
    { return new ContentIndex(new[]{
        new Lesson{Id="en-one",Title="The red door",TitleEn="The red door",Language="英语",Section=Section.English,Category="城市",CategoryEn="Cities",ShortText="A door",LongText="A door on the street"},
        new Lesson{Id="zh-one",Title="小巷",TitleEn="The quiet lane",Language="中文",Section=Section.Chinese,Category="城市",CategoryEn="Cities",ShortText="小巷",LongText="小巷里有一棵树"},
        new Lesson{Id="fr-one",Title="Le carnet",Language="Français",Section=Section.Custom,Category="生活",CategoryEn="Everyday",ShortText="Le carnet",LongText="Le carnet est ouvert"},
        new Lesson{Id="exam-ielts-essay-x",Title="IELTS Task 2",Language="英语",Section=Section.Exams,Category="IELTS Task 2",CategoryEn="IELTS Task 2",ShortText="Study",LongText="Study with a friend"},
        new Lesson{Id="g-en",Title="寻找钥匙",TitleEn="Missing keys",Language="英语",Section=Section.Games,Game=GameKind.Memory,ShortText="Blue key",LongText="The blue key is missing"},
        new Lesson{Id="g-zh",Title="寻找钥匙",TitleEn="Missing keys",Language="中文",Section=Section.Games,Game=GameKind.Memory,ShortText="钥匙",LongText="蓝色钥匙不见了"},
        new Lesson{Id="g-race",Title="赛跑",TitleEn="Race",Language="英语",Section=Section.Games,Game=GameKind.Race,ShortText="Go",LongText="Go to the gate"}
    }); }
    static int Main()
    {
        Run("reading separates content language and category",delegate {
            var q=Fixture().Query(Workspace.Reading,"en","Cities","",new List<SavedText>());
            Check(q.Count==1&&q[0].Id=="en-one","Reading mixed languages or included another workspace");
        });
        Run("both Chinese and English titles can be searched",delegate {
            var index=Fixture();
            Check(index.Query(Workspace.Reading,"zh","","quiet",null).Count==1,"English title search failed");
            Check(index.Query(Workspace.Reading,"zh","","小巷",null).Count==1,"Chinese title search failed");
            Check(index.Query(Workspace.Reading,"en","","not present",null).Count==0,"Empty search result ignored");
        });
        Run("game choice and text language filter independently",delegate {
            var q=Fixture().Query(Workspace.Games,"en","Memory","",null);
            Check(q.Count==1&&q[0].Id=="g-en","Games mixed another mode or language");
            Check(Fixture().Query(Workspace.Games,"zh","Memory","",null)[0].Id=="g-zh","Chinese game missing");
        });
        Run("saved texts live in My texts and cannot leak into builtins",delegate {
            var saved=new List<SavedText>{new SavedText{Id="s1",Title="自己的文章",Language="中文",Text="自己的文字"}};
            var index=Fixture();
            Check(index.Query(Workspace.Library,"zh","","",saved).Count==1,"Saved library not shown");
            Check(index.Query(Workspace.Reading,"zh","","",saved).Count==1,"Saved library polluted builtins");
        });
        Run("language labels are stable and UI translation is independent",delegate {
            Check(ContentIndex.LanguageKey(Fixture().All[0])=="en","English key unstable");
            Check(ContentIndex.LanguageKey(Fixture().All[2])=="fr","French key unstable");
            Check(ContentIndex.Title(Fixture().All[1],true)=="The quiet lane","English metadata missing");
            Check(Ui.GameName(GameKind.Memory,true)=="Memory flash","Game UI not translated");
        });
        Run("revenge excludes words from another practice language",delegate {
            Check(ContentIndex.TokenMatches("mistake","en")&&!ContentIndex.TokenMatches("你好","en"),"English revenge mixed Chinese");
            Check(ContentIndex.TokenMatches("你好","zh")&&!ContentIndex.TokenMatches("mistake","zh"),"Chinese revenge mixed English");
        });
        Console.WriteLine(passed+" passed, "+failed+" failed"); return failed==0?0:1;
    }
}
