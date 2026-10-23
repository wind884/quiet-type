using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using QuietType;
class ShellTests
{
    static int passed, failed;
    static string root;
    static void Check(bool ok,string why) { if(!ok)throw new Exception(why); }
    static void Run(string name,Action action) { try { action();passed++;Console.WriteLine("PASS "+name); }catch(Exception ex) { failed++;Console.WriteLine("FAIL "+name+": "+ex); } }
    static MainForm Window(out UserStore store) { store=new UserStore(Path.Combine(root,Guid.NewGuid().ToString("N")));return new MainForm(store); }
    static Control Find(MainForm f,string name) { return f.Controls.Find(name,true)[0]; }
    static void Show(MainForm f) { f.Opacity=0;f.ShowInTaskbar=false;f.Show();Application.DoEvents(); }
    static void Pump(int ms) { var watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<ms) { Application.DoEvents();Thread.Sleep(15); } }
    static void Send(MainForm f,string text) { ((InputSink)f.Practice.Controls.Find("InputSink",true)[0]).Text=text;Application.DoEvents(); }
    static void Choose(MainForm f,string name,string key)
    {
        var box=(ComboBox)Find(f,name);
        for(int i=0;i<box.Items.Count;i++)if(((Choice)box.Items[i]).Key==key) { box.SelectedIndex=i;return; }
        throw new Exception("Missing filter "+name+" / "+key);
    }
    static string Key(MainForm f,string name) { return ((Choice)((ComboBox)Find(f,name)).SelectedItem).Key; }
    static void Game(MainForm f,GameKind game,string lang) { f.SelectWorkspace(Workspace.Games,"");Choose(f,"Group",game.ToString());Choose(f,"Language",lang); }
    static void Snapshot(MainForm f,string file) { using(var b=new Bitmap(f.Width,f.Height)) { f.DrawToBitmap(b,new Rectangle(Point.Empty,f.Size));b.Save(Path.Combine(root,file),ImageFormat.Png); } }
    [STAThread] static int Main()
    {
        root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"previews");Directory.CreateDirectory(root);
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        Run("navigation filters each practice type and every built-in offers two lengths",delegate {
            UserStore store;using(var f=Window(out store)) {
                Show(f);
                foreach(var page in new[]{Workspace.Reading,Workspace.Exams,Workspace.Code,Workspace.Games,Workspace.Test}) {
                    ((RadioButton)Find(f,"ShortMode")).Checked=true;f.SelectWorkspace(page,"");int brief=f.Practice.Session.TargetUnits.Count;
                    Check(((ListBox)Find(f,"Lessons")).Items.Count>0,"Empty page "+page);
                    ((RadioButton)Find(f,"LongMode")).Checked=true;Check(f.Practice.Session.TargetUnits.Count>brief,"No long target "+page);
                }
            }
        });
        Run("locale switch keeps session input timer lesson and filters",delegate {
            UserStore store;using(var f=Window(out store)) {
                Show(f);Choose(f,"Group","Travel");Send(f,f.Practice.Session.Target.Substring(0,14));Pump(180);
                var session=f.Practice.Session;string id=f.SelectedLesson.Id,target=session.Target;int position=session.Position,attempts=session.Attempts;double elapsed=session.ElapsedSeconds;
                ((Button)Find(f,"Locale")).PerformClick();
                Check(f.IsEnglish&&f.Text=="Quiet Type","UI did not switch");Check(object.ReferenceEquals(session,f.Practice.Session),"Locale created a new round");
                Check(session.Position==position&&session.Attempts==attempts&&session.Target==target,"Locale altered typed progress");
                Check(session.ElapsedSeconds>=elapsed&&session.ElapsedSeconds<elapsed+1,"Timer reset");Check(f.SelectedLesson.Id==id&&Key(f,"Group")=="Travel"&&Key(f,"Language")=="en","Filter changed");
                Check(store.Data.UiLanguage=="en","Locale preference not retained");f.SetUiLanguage(false);Check(object.ReferenceEquals(session,f.Practice.Session),"Second switch restarted");
            }
        });
        Run("search accepts Chinese and English titles and clears empty results",delegate {
            UserStore store;using(var f=Window(out store)) {
                Show(f);Choose(f,"Language","zh");var text=f.SelectedLesson;
                ((TextBox)Find(f,"Search")).Text=text.TitleEn;Check(f.SelectedLesson!=null&&f.SelectedLesson.Id==text.Id,"English title did not find Chinese article");
                ((TextBox)Find(f,"Search")).Text="nothing-will-match-900000";Check(f.SelectedLesson==null&&!f.Practice.Visible,"Stale text left after empty search");
                ((TextBox)Find(f,"Search")).Text="";Check(f.SelectedLesson!=null&&f.Practice.Visible,"Cleared search stayed empty");
            }
        });
        Run("all five challenges independently support English and Chinese",delegate {
            UserStore store;using(var f=Window(out store)) {
                Show(f);
                foreach(GameKind game in Enum.GetValues(typeof(GameKind)))if(game!=GameKind.None)
                    foreach(string lang in new[]{"en","zh"}) {
                        Game(f,game,lang);Check(f.SelectedLesson.Game==game&&ContentIndex.LanguageKey(f.SelectedLesson)==lang,"Incorrect mode/language "+game+lang);
                        Check(((ListBox)Find(f,"Lessons")).Items.Count==4,"Expected four scenarios "+game+lang);
                        ((RadioButton)Find(f,"ShortMode")).Checked=true;string brief=f.Practice.Session.Target;
                        ((RadioButton)Find(f,"LongMode")).Checked=true;Check(f.Practice.Session.Target.Length>brief.Length,"Missing long game "+game+lang);
                    }
            }
        });
        Run("one wrong key ends one-life and saves a result",delegate {
            UserStore store;using(var f=Window(out store)) { Show(f);Game(f,GameKind.SuddenDeath,"en");Send(f,f.Practice.Session.TargetUnits[0]=="x"?"y":"x");
                Check(f.Practice.Session.Reason==FinishReason.Lost&&store.Data.Results.Count==1,"One-life did not end/store");Snapshot(f,"one-life.png"); }
        });
        Run("history localizes generated game labels and preserves translated titles across reload",delegate {
            UserStore store;using(var f=Window(out store)) { Show(f);Game(f,GameKind.SuddenDeath,"en");Send(f,"?");
                string title="One life / "+ContentIndex.Title(f.SelectedLesson,true);
                Check(store.Data.Results[0].TitleEn==title,"Translated title not stored");
                using(var history=new HistoryDialog(store,true)) {
                    var grid=(DataGridView)history.Controls.Find("HistoryRecords",true)[0];Check((string)grid.Rows[0].Cells[2].Value==title,"English game title has Chinese app prefix");
                }
                Check(store.Load()&&store.Data.Results[0].TitleEn==title,"Translation lost during save/reload");
            }
        });
        Run("changing length and page retains the previous record labels",delegate {
            UserStore store;using(var f=Window(out store)) { Show(f);Send(f,f.Practice.Session.TargetUnits[0]);((RadioButton)Find(f,"LongMode")).Checked=true;
                Check(store.Data.Results.Count==1&&store.Data.Results[0].LengthMode=="短篇速练","Previous short round relabeled");
                Send(f,f.Practice.Session.TargetUnits[0]);f.SelectWorkspace(Workspace.Test,"");Check(store.Data.Results[0].Section=="日常练习","Previous page relabeled"); }
        });
        Run("memory preview and peek expire while locale switch retains state",delegate {
            UserStore store;using(var f=Window(out store)) { Show(f);Game(f,GameKind.Memory,"en");Check(!f.Practice.InputAllowed&&f.Practice.MaskReference,"Preview is visible without starting");
                ((Button)Find(f,"Restart")).PerformClick();Check(!f.Practice.InputAllowed&&!f.Practice.MaskReference,"No memory preview");Pump(4200);
                Check(f.Practice.InputAllowed&&f.Practice.MaskReference,"Preview never expired");var session=f.Practice.Session;f.SetUiLanguage(true);
                Check(object.ReferenceEquals(session,f.Practice.Session)&&f.Practice.MaskReference,"Locale reset memory state");Send(f,session.TargetUnits[0]);
                ((Button)Find(f,"Peek")).PerformClick();Check(!f.Practice.MaskReference,"No peek");Pump(1950);Check(f.Practice.MaskReference,"Unlimited peek");Snapshot(f,"memory-english.png"); }
        });
        Run("mistake rematch uses only the selected language and clears only those words",delegate {
            UserStore store;using(var f=Window(out store)) { Show(f);store.Record(new ScoreRecord{Id="seed"},new[]{"troublesome","容易混淆"});
                Game(f,GameKind.Revenge,"en");Check(f.Practice.Session.Target=="troublesome","English revenge mixed languages");Send(f,"troublesome");
                Check(f.Practice.Session.IsFinished&&store.Data.Mistakes.Count==1&&store.Data.Mistakes[0].Text=="容易混淆","Cleared the wrong language");
                Game(f,GameKind.Revenge,"zh");Check(f.Practice.Session.Target=="容易混淆","Chinese bank not used");Send(f,"容易混淆");Check(store.Data.Mistakes.Count==0,"Chinese mistake not cleared"); }
        });
        Run("ghost moves against correct typed progress in wall time",delegate {
            UserStore store;using(var f=Window(out store)) { Show(f);Game(f,GameKind.Race,"en");f.SetUiLanguage(true);Send(f,f.Practice.Session.TargetUnits[0]);Pump(800);
                Check(f.Practice.GhostPosition>0,"Static ghost");Snapshot(f,"race-english.png"); }
        });
        Run("mixed test language is available without code or games in its list",delegate {
            UserStore store;using(var f=Window(out store)) { Show(f);f.SelectWorkspace(Workspace.Test,"");Choose(f,"Language","mixed");
                Check(ContentIndex.LanguageKey(f.SelectedLesson)=="mixed"&&f.Practice.Session.TargetUnits.Count>=4000,"Mixed test not reachable");
                foreach(Lesson text in ((ListBox)Find(f,"Lessons")).Items)Check(!text.IsCode&&text.Game==GameKind.None,"Test mixed with games/code"); }
        });
        Run("primary filters remain visible after switching between one and two filter pages",delegate {
            UserStore store;using(var f=Window(out store)) { Show(f);
                f.SelectWorkspace(Workspace.Exams,"");Check(Find(f,"Group").Visible&&!Find(f,"Language").Visible,"Exam filters incorrect");
                f.SelectWorkspace(Workspace.Reading,"");Check(Find(f,"Language").Visible&&Find(f,"Group").Visible,"Reading lost its language filter after Exams");
                f.SelectWorkspace(Workspace.Code,"");Check(Find(f,"Language").Visible&&!Find(f,"Group").Visible,"Code filters incorrect");
                f.SelectWorkspace(Workspace.Games,"");Check(Find(f,"Group").Visible&&Find(f,"Language").Visible,"Challenges lost mode after Code");
            }
        });
        Run("library empty state and edits refresh after Cancel",delegate {
            UserStore store;using(var f=Window(out store)) { Show(f);f.SelectWorkspace(Workspace.Library,"");Check(f.SelectedLesson==null&&!f.Practice.Visible,"Empty library shows built-in text");Snapshot(f,"my-texts-empty.png");
                store.SaveText(new SavedText{Id="edit",Title="笔记",Language="中文",Text="原来的一段文字"});f.SelectWorkspace(Workspace.Library,"user:edit");
                Check(f.SelectedLesson.Id=="user:edit","Preferred saved Chinese text not selected");
                using(var closeTimer=new System.Windows.Forms.Timer()) { closeTimer.Interval=100;closeTimer.Tick+=delegate {
                    foreach(Form dialog in Application.OpenForms)if(dialog is LibraryDialog) { closeTimer.Stop();store.SaveText(new SavedText{Id="edit",Title="笔记",Language="中文",Text="修改后的另一段文字"});dialog.DialogResult=DialogResult.Cancel;dialog.Close();break; }
                };closeTimer.Start();((Button)Find(f,"Library")).PerformClick(); }
                Check(f.Practice.Session.Target=="修改后的另一段文字","Saved text stayed stale"); }
        });
        Run("both locales and minimum window render without editor/button overlap",delegate {
            UserStore store;using(var f=Window(out store)) { Show(f);Send(f,PracticeText.Shorten(f.Practice.Session.Target,45,false));Snapshot(f,"reading-chinese-ui.png");
                f.SetUiLanguage(true);Snapshot(f,"reading-english-ui.png");Choose(f,"Language","zh");Send(f,PracticeText.Shorten(f.Practice.Session.Target,30,false));Snapshot(f,"chinese-text-english-ui.png");
                f.SetUiLanguage(false);Snapshot(f,"chinese-text-chinese-ui.png");f.SelectWorkspace(Workspace.Exams,"");f.SetUiLanguage(true);Snapshot(f,"exams-english.png");
                f.SelectWorkspace(Workspace.Code,"");Snapshot(f,"code-english.png");f.SelectWorkspace(Workspace.Test,"");Snapshot(f,"test-english.png");
                f.Size=f.MinimumSize;Application.DoEvents();Snapshot(f,"minimum.png");Check(f.Practice.Height>100&&f.Practice.Right<f.ClientSize.Width,"Editor clipped at minimum");
                Check(f.Practice.Bottom<Find(f,"Next").Top,"Buttons overlap editor");Check(Find(f,"Lessons").Right<f.Practice.Left,"List overlaps editor"); }
        });
        Console.WriteLine(passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
}
