using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Windows.Forms;
using System.Xml.Linq;
using Paratext.PluginInterfaces;
using ParatextNoteSuggestions;

namespace NoteSuggestionsTests
{
    public static class DesktopDemo
    {
        [STAThread]
        public static void Main() { Program.Main(new[] { "--demo" }); }
    }
    public static class DesktopMenuDemo
    {
        [STAThread]
        public static void Main() { Program.Main(new[] { "--menu-demo" }); }
    }
    public sealed class Stub<T> : RealProxy
    {
        readonly Func<MethodInfo, object[], object> invoke;
        Stub(Func<MethodInfo, object[], object> fn) : base(typeof(T)) { invoke = fn; }
        public static T Create(Func<MethodInfo, object[], object> fn) { return (T)new Stub<T>(fn).GetTransparentProxy(); }
        public override IMessage Invoke(IMessage msg)
        {
            var call = (IMethodCallMessage)msg;
            try { return new ReturnMessage(invoke((MethodInfo)call.MethodBase, call.Args), null, 0, call.LogicalCallContext, call); }
            catch (Exception ex) { return new ReturnMessage(ex, call); }
        }
    }
    public sealed class Fixture
    {
        public IPluginHost Host;
        public IProject Project;
        public IParatextChildState State;
        public IVerseRef Ref;
        public IScriptureTextSelection Anchor;
        public List<IProjectNote> Notes = new List<IProjectNote>();
        public List<IUSFMToken> Tokens = new List<IUSFMToken>();
        public Dictionary<string, List<IUSFMToken>> Serialized = new Dictionary<string, List<IUSFMToken>>();
        public NoteSuggestionsPlugin Plugin = new NoteSuggestionsPlugin();
        public bool Editable = true, LockAvailable = true, Resolved, IgnorePut, Revoke, ThrowPut;
        public bool LockHeld, NotesLockHeld, NotesLockAvailable = true, ThrowResolve, IgnoreResolve;
        public int Resolves;
        public Action OnHistory;
        public int Writes, Released, Notifications, History, Navigations, ActiveVerse = 3;
        public List<IComment> Replies = new List<IComment>();
        public string UserName = "Synthetic Reviewer";
        public string ProjectId = Guid.NewGuid().ToString(), NoteText, AnchorText = "பழைய", Before = "", After = "", DiskFile;
        public IReadOnlyList<ISelection> Selections = new List<ISelection>();
        public Fixture()
        {
            Ref = MakeRef(1, 3);
            Anchor = Stub<IScriptureTextSelection>.Create((m, a) => {
                switch(m.Name) { case "get_VerseRefStart": case "get_VerseRefEnd": return Ref; case "get_SelectedText": return AnchorText; case "get_BeforeContext": return Before; case "get_AfterContext": return After; case "get_Offset": return 0; default: throw new NotSupportedException(m.Name); }
            });
            NoteText = Qa("Punctuation", "பழைய", "புதிய", "No");
            var note = Stub<IProjectNote>.Create((m, a) => {
                switch(m.Name) {
                    case "get_IsResolved": return Resolved;
                    case "Resolve":
                        if (!LockHeld || !NotesLockHeld) throw new InvalidOperationException("Notes lock required");
                        if (ThrowResolve) throw new InvalidOperationException("Injected note resolution failure");
                        Resolves++; if (!IgnoreResolve) Resolved = true; return null;
                    case "get_Anchor": return Anchor;
                    case "get_Comments": return new List<IComment> { Comment(NoteText) }.Concat(Replies).ToList();
                    default: throw new NotSupportedException(m.Name);
                }
            });
            Notes.Add(note);
            Tokens.Add(Marker("v", "3", 1, 3, MarkerType.Verse)); Tokens.Add(Text("முன் பழைய பின்", 1, 3, true, false));
            Project = Stub<IProject>.Create((m, a) => {
                switch(m.Name) {
                    case "get_ID": return ProjectId;
                    case "get_ShortName": return "Synthetic Test";
                    case "get_AvailableBooks": return new List<IBookInfo> { Stub<IBookInfo>.Create((bm, ba) => bm.Name == "get_Number" ? (object)1 : (object)"GEN") };
                    case "GetNotes": return Notes;
                    case "CanEdit": return Editable;
                    case "RequestWriteLock":
                        bool notesLock = a.Length == 3 && a[2] is WriteLockScope;
                        if (!LockAvailable || (notesLock && !NotesLockAvailable)) return null;
                        LockHeld = true; NotesLockHeld = notesLock;
                        var value = Stub<IWriteLock>.Create((lm, la) => { if(lm.Name == "Dispose") { LockHeld = false; NotesLockHeld = false; Released++; return null; } if(lm.Name == "SendNotifications") { Notifications++; return null; } throw new NotSupportedException(lm.Name); });
                        if (Revoke) ((Action<IWriteLock>)a[1])(value);
                        return value;
                    case "GetUSFMTokens": return Tokens.ToList();
                    case "ConvertToUSFMString": return Serialize((IEnumerable<IUSFMToken>)a[0]);
                    case "ConvertToUSFMTokens":
                        List<IUSFMToken> saved;
                        if (!Serialized.TryGetValue((string)a[0], out saved)) throw new InvalidOperationException("Unknown synthetic USFM");
                        return saved.ToList();
                    case "MakeHistoryPoint": if (LockHeld) throw new InvalidOperationException("History checkpoint invalidates active write locks"); History++; if (OnHistory != null) OnHistory(); return null;
                    case "PutUSFMTokens":
                        Writes++;
                        if (ThrowPut) throw new InvalidOperationException("Injected save failure");
                        if (!IgnorePut) Tokens = ((IEnumerable<IUSFMToken>)a[1]).ToList();
                        if (DiskFile != null) File.WriteAllText(DiskFile, Serialize(Tokens), new System.Text.UTF8Encoding(false));
                        return null;
                    default: throw new NotSupportedException("project." + m.Name);
                }
            });
            State = Stub<IParatextChildState>.Create((m, a) => {
                switch(m.Name) { case "get_Project": return Project; case "get_VerseRef": return MakeRef(1, ActiveVerse); case "get_Selections": return Selections; default: throw new NotSupportedException(m.Name); }
            });
            Host = Stub<IPluginHost>.Create((m, a) => {
                switch(m.Name) { case "get_UserInfo": return Stub<IUserInfo>.Create((um, ua) => um.Name == "get_Name" ? UserName : null); case "get_ActiveWindowState": return State; case "get_ApplicationVersion": return new Version(9,5,110,1); case "OpenTextWindowFor": Navigations++; return null; default: throw new NotSupportedException("host."+m.Name); }
            });
        }
        public NoteService Service() { return new NoteService(Plugin, Host, State); }
        public string Serialize(IEnumerable<IUSFMToken> tokens)
        {
            var list = tokens.ToList();
            string value = String.Concat(list.Select(t => t is IUSFMTextToken ? ((IUSFMTextToken)t).Text : "\\" + ((IUSFMMarkerToken)t).Marker + (((IUSFMMarkerToken)t).Data.Length > 0 ? " " + ((IUSFMMarkerToken)t).Data : "") + " "));
            Serialized[value] = list;
            return value;
        }
        public static IVerseRef MakeRef(int chapter, int verse)
        {
            return Stub<IVerseRef>.Create((m,a) => {
                switch(m.Name) { case "get_BookNum": return 1; case "get_BookCode": return "GEN"; case "get_ChapterNum": return chapter; case "get_VerseNum": return verse; case "get_RepresentsMultipleVerses": return false; case "ToString": return "GEN " + chapter + ":" + verse; default: throw new NotSupportedException(m.Name); }
            });
        }
        public static IUSFMTextToken Text(string text, int chapter, int verse, bool scripture, bool note)
        {
            return Stub<IUSFMTextToken>.Create((m,a) => {
                switch(m.Name) { case "get_Text": return text; case "get_VerseRef": return MakeRef(chapter,verse); case "get_VerseOffset": return 0; case "get_IsScripture": case "get_IsPublishableVernacular": return scripture; case "get_IsFootnoteOrCrossReference": return note; case "get_IsSpecial": case "get_IsFigure": case "get_IsMetadata": return false; default: throw new NotSupportedException(m.Name); }
            });
        }
        public static IUSFMMarkerToken Marker(string marker, string data, int chapter, int verse, MarkerType type)
        {
            return Stub<IUSFMMarkerToken>.Create((m,a) => {
                switch(m.Name) { case "get_Marker": return marker; case "get_Data": return data; case "get_Type": return type; case "get_EndMarker": return ""; case "get_Attributes": return new List<IUSFMAttribute>(); case "get_VerseRef": return MakeRef(chapter,verse); case "get_VerseOffset": return 0; case "get_IsScripture": return true; case "get_IsSpecial": case "get_IsFigure": case "get_IsMetadata": case "get_IsPublishableVernacular": case "get_IsFootnoteOrCrossReference": return false; default: throw new NotSupportedException(m.Name); }
            });
        }
        public static IComment Comment(string text)
        {
            return Stub<IComment>.Create((m,a) => {
                switch(m.Name) { case "get_Contents": return text.Split(new[]{"\r\n","\n"}, StringSplitOptions.None).Select(s => new CommentParagraph(new[] { new FormattedString(s) })).ToList(); case "get_Created": return new DateTimeOffset(2026,10,31,0,0,0,TimeSpan.Zero); case "get_Author": return null; default: throw new NotSupportedException(m.Name); }
            });
        }
        public static string Qa(string category, string original, string suggested, string decision)
        { return "[AI-QA R2] " + category + " — " + category + "\r\nOriginal: " + original + "\r\nSuggested: " + suggested + "\r\nReason: Synthetic reason\r\nReference: Synthetic reference\r\nSeverity: Medium · Confidence: High · Reviewer decision needed: " + decision + "\r\nAI proposal — human review required. Not an edit."; }
    }
    public static class Program
    {
        static int passed, failed;
        static void Test(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS " + name); } catch(Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.ToString()); } }
        static void Eq<T>(T expected, T actual) { if (!Object.Equals(expected,actual)) throw new Exception("Expected " + expected + "; got " + actual); }
        static void Reject(Action action) { try { action(); } catch(InvalidOperationException) { return; } throw new Exception("Expected a refusal"); }
        static void Apply(Fixture f, bool reviewed = false) { var s=f.Service(); var n=s.ReadCandidates()[0]; s.Apply(n,n.Proposal.Original,n.Proposal.Suggested,n.Proposal.Region,reviewed); }
        static TextPart Part(string text, ContentRegion region = ContentRegion.Body, int chapter = 1, int verse = 3, int index = 0) { return new TextPart {Text=text,Region=region,Chapter=chapter,Verse=verse,TokenIndex=index}; }
        static IEnumerable<Control> Descendants(Control control) { foreach (Control child in control.Controls) { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; } }
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--audit")
            {
                var totals = new Dictionary<string, int>(); int count = 0;
                foreach (string file in Directory.GetFiles(args[1], "Notes_*.xml"))
                {
                    var doc = XDocument.Load(file);
                    foreach (var comment in doc.Descendants("Comment"))
                    {
                        var contents = comment.Element("Contents"); if (contents == null) continue;
                        string text = contents.Elements("p").Any() ? String.Join("\r\n", contents.Elements("p").Select(p => p.Value.Trim())) : contents.Value.Trim();
                        if (!text.StartsWith("[AI-QA ", StringComparison.Ordinal)) continue;
                        var parsed = NoteParser.Parse(text); count++;
                        string key = parsed.Category + " | " + parsed.Route; if (!totals.ContainsKey(key)) totals[key] = 0; totals[key]++;
                    }
                }
                foreach (var pair in totals.OrderBy(p => p.Key)) Console.WriteLine(pair.Key + " | " + pair.Value);
                Console.WriteLine("Stored QA comments inspected read-only: " + count + ". Routing only; applicability and live thread status not established.");
                return 0;
            }
            if (args.Length == 2 && args[0] == "--report-demo")
            { Application.EnableVisualStyles(); Application.Run(new ReviewReportViewer(args[1])); return 0; }
            if (args.Contains("--queue-demo"))
            {
                Application.EnableVisualStyles(); var f = new Fixture();
                Application.Run(QueueTests.Queue(f, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "qd-" + Guid.NewGuid().ToString("N").Substring(0,8)))); return 0;
            }
            if (args.Contains("--menu-demo"))
            {
                Application.EnableVisualStyles(); var f = new Fixture();
                var form = new Form { Text = "Synthetic Note Suggestions menu host", Width = 600, Height = 230, StartPosition = FormStartPosition.CenterScreen };
                var button = new Button { Text = "Open plugin suggestions popup", Dock = DockStyle.Top, Height = 65 };
                button.Click += delegate { f.Plugin.Open(f.Host, f.State); };
                form.Controls.Add(button); form.Controls.Add(new Label { Text = "Synthetic API host only. No live Paratext project is used.", Dock = DockStyle.Bottom, Height = 45 });
                Application.Run(form); return 0;
            }
            if (args.Contains("--demo"))
            {
                Application.EnableVisualStyles();
                var f=new Fixture(); f.NoteText=Fixture.Qa("Punctuation","உண்டாகட்டும், என்றார்","உண்டாகட்டும் என்றார்","No"); f.Tokens[1]=Fixture.Text("தேவன் உண்டாகட்டும், என்றார்",1,3,true,false);
                f.DiskFile=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"synthetic-demo.sfm"); File.WriteAllText(f.DiskFile,f.Serialize(f.Tokens));
                var svc=f.Service(); Application.Run(new ReviewForm(svc,svc.ReadCandidates())); return 0;
            }
            ReportTests.Run(Test);
            QueueTests.Run(Test);
            foreach(string category in Proposal.Categories)
            {
                string captured=category;
                Test("category parsed: "+captured,()=>Eq(captured,NoteParser.Parse(Fixture.Qa(captured,"பழைய","புதிய","No")).Category));
            }
            Test("clear punctuation applies",()=>Eq(ProposalRoute.Apply,NoteParser.Parse(Fixture.Qa("Punctuation","பழைய","புதிய","No")).Route));
            Test("reviewer flag overrides clear category",()=>Eq(ProposalRoute.Review,NoteParser.Parse(Fixture.Qa("Punctuation","பழைய","புதிய","Yes")).Route));
            Test("missing reviewer flag requires review",()=>Eq(ProposalRoute.Review,NoteParser.Parse(Fixture.Qa("Punctuation","பழைய","புதிய","")).Route));
            Test("source meaning requires review",()=>Eq(ProposalRoute.Review,NoteParser.Parse(Fixture.Qa("Source comparison","பழைய","புதிய","No")).Route));
            Test("unknown category requires review",()=>Eq(ProposalRoute.Review,NoteParser.Parse(Fixture.Qa("Unknown","பழைய","புதிய","No")).Route));
            Test("alternatives not applied",()=>Eq(ProposalRoute.Review,NoteParser.Parse(Fixture.Qa("Grammar","பழைய","புதிய / வேறு","No")).Route));
            Test("English instruction not applied",()=>Eq(ProposalRoute.Review,NoteParser.Parse(Fixture.Qa("Grammar","பழைய","consider a change","No")).Route));
            Test("parenthetical commentary not applied",()=>Eq(ProposalRoute.Review,NoteParser.Parse(Fixture.Qa("Grammar","பழைய","புதிய (for reviewer)","No")).Route));
            Test("no-change has no apply",()=>Eq(ProposalRoute.NoChange,NoteParser.Parse(Fixture.Qa("Grammar","பழைய","(no change proposed)","Yes")).Route));
            Test("raw marker uses structural route",()=>Eq(ProposalRoute.Structure,NoteParser.Parse(Fixture.Qa("USFM marker","\\ms பழைய","\\cl பழைய","Yes")).Route));
            Test("duplicate field requires review",()=>Eq(ProposalRoute.Review,NoteParser.Parse(Fixture.Qa("Punctuation","பழைய","புதிய","No")+"\r\nSuggested: வேறு").Route));
            Test("legacy changed-to stays review-only",()=>Eq(ProposalRoute.Legacy,NoteParser.Parse("Changed to: புதிய").Route));
            Test("P3 pass retained",()=>Eq("P3",NoteParser.Parse(Fixture.Qa("Grammar","பழைய","புதிய","No").Replace("R2","P3")).Pass));
            Test("metadata does not leak into replacement",()=>Eq("புதிய",NoteParser.Parse(Fixture.Qa("Punctuation","பழைய","புதிய","No")).Suggested));
            Test("intro location overrides verse anchor",()=>Eq(ContentRegion.Introduction,NoteParser.Parse(Fixture.Qa("Grammar","பழைய","புதிய","No").Replace("Original:","Location: book introduction\r\nOriginal:")).Region));
            Test("fallback anchor requires review",()=>Eq(ProposalRoute.Review,NoteParser.Parse(Fixture.Qa("Grammar","பழைய","புதிய","No").Replace("Original:","Location: whole verse — flagged text not found verbatim\r\nOriginal:")).Route));
            Test("exact edit preserves surroundings",()=>{var m=ExactEditor.Locate(new[]{Part("முன் பழைய பின்")},ContentRegion.Body,1,3,3,"பழைய","புதிய","","");Eq("முன் புதிய பின்",ExactEditor.Replace("முன் பழைய பின்",m));});
            Test("stale original refused",()=>Reject(()=>ExactEditor.Locate(new[]{Part("புதிய")},ContentRegion.Body,1,3,3,"பழைய","புதிய","","")));
            Test("repeated word refused without context",()=>Reject(()=>ExactEditor.Locate(new[]{Part("பழைய பழைய")},ContentRegion.Body,1,3,3,"பழைய","புதிய","","")));
            Test("repeated word exact context disambiguates",()=>Eq("பழைய ".Length,ExactEditor.Locate(new[]{Part("பழைய பழைய")},ContentRegion.Body,1,3,3,"பழைய","புதிய","பழைய ","").Start));
            Test("footnote cannot match body action",()=>Reject(()=>ExactEditor.Locate(new[]{Part("பழைய",ContentRegion.Footnote)},ContentRegion.Body,1,3,3,"பழைய","புதிய","","")));
            Test("other verse cannot match",()=>Reject(()=>ExactEditor.Locate(new[]{Part("பழைய",ContentRegion.Body,1,4)},ContentRegion.Body,1,3,3,"பழைய","புதிய","","")));
            Test("other chapter cannot match",()=>Reject(()=>ExactEditor.Locate(new[]{Part("பழைய",ContentRegion.Body,2,3)},ContentRegion.Body,1,3,3,"பழைய","புதிய","","")));
            Test("Tamil combining sequence not split",()=>Reject(()=>ExactEditor.Locate(new[]{Part("கா")},ContentRegion.Body,1,3,3,"க","ப","","")));
            Test("markers cannot be injected as text",()=>Reject(()=>ExactEditor.Locate(new[]{Part("பழைய")},ContentRegion.Body,1,3,3,"பழைய","\\v 4 புதிய","","")));
            Test("newlines cannot be injected as text",()=>Reject(()=>ExactEditor.Locate(new[]{Part("பழைய")},ContentRegion.Body,1,3,3,"பழைய","புதிய\nவேறு","","")));
            Test("empty original refused",()=>Reject(()=>ExactEditor.Locate(new[]{Part("பழைய")},ContentRegion.Body,1,3,3,"","புதிய","","")));
            Test("identical replacement refused",()=>Reject(()=>ExactEditor.Locate(new[]{Part("பழைய")},ContentRegion.Body,1,3,3,"பழைய","பழைய","","")));
            Test("explicit deletion supported",()=>Eq("",ExactEditor.Locate(new[]{Part("பழைய")},ContentRegion.Body,1,3,3,"பழைய","","","").Replacement));
            Test("preview preserves text elements",()=>Eq("கா…",NoteParser.Preview("காபா",1)));
            Test("apply writes and verifies token USFM",()=>{var f=new Fixture();Apply(f);Eq("\\v 3 முன் புதிய பின்",f.Serialize(f.Tokens));Eq(1,f.Writes);Eq(1,f.Released);Eq(1,f.Notifications);Eq(2,f.History);});
            Test("read-only project writes nothing",()=>{var f=new Fixture{Editable=false};Reject(()=>Apply(f));Eq(0,f.Writes);});
            Test("denied lock writes nothing",()=>{var f=new Fixture{LockAvailable=false};Reject(()=>Apply(f));Eq(0,f.Writes);});
            Test("revoked lock writes nothing",()=>{var f=new Fixture{Revoke=true};Reject(()=>Apply(f));Eq(0,f.Writes);Eq(1,f.Released);});
            Test("stale source note refused under lock",()=>{var f=new Fixture();var s=f.Service();var n=s.ReadCandidates()[0];f.NoteText+=" changed";Reject(()=>s.Apply(n,n.Proposal.Original,n.Proposal.Suggested,n.Proposal.Region,false));Eq(0,f.Writes);Eq(1,f.Released);});
            Test("resolved source note refused",()=>{var f=new Fixture();var s=f.Service();var n=s.ReadCandidates()[0];f.Resolved=true;Reject(()=>s.Apply(n,n.Proposal.Original,n.Proposal.Suggested,n.Proposal.Region,false));Eq(0,f.Writes);});
            Test("repeated click cannot reapply",()=>{var f=new Fixture();Apply(f);Reject(()=>Apply(f));Eq(1,f.Writes);});
            Test("moved verse refused",()=>{var f=new Fixture();var s=f.Service();var n=s.ReadCandidates()[0];f.ActiveVerse=4;Reject(()=>s.Apply(n,n.Proposal.Original,n.Proposal.Suggested,n.Proposal.Region,false));Eq(0,f.Writes);});
            Test("failed verification is reported",()=>{var f=new Fixture{IgnorePut=true};Reject(()=>Apply(f));Eq(1,f.Released);});
            Test("save exception releases lock",()=>{var f=new Fixture{ThrowPut=true};Reject(()=>Apply(f));Eq(1,f.Released);});
            Test("review-required candidate cannot bypass review",()=>{var f=new Fixture();f.NoteText=Fixture.Qa("Source comparison","பழைய","புதிய","Yes");Reject(()=>Apply(f));Eq(0,f.Writes);});
            Test("reviewed literal source correction writes",()=>{var f=new Fixture();f.NoteText=Fixture.Qa("Source comparison","பழைய","புதிய","Yes");Apply(f,true);Eq(1,f.Writes);});
            Test("review checkbox cannot apply English instructions",()=>{var f=new Fixture();f.NoteText=Fixture.Qa("Source comparison","பழைய","consider a change","Yes");Reject(()=>Apply(f,true));Eq(0,f.Writes);});
            Test("review checkbox cannot apply alternative list",()=>{var f=new Fixture();f.NoteText=Fixture.Qa("Grammar","பழைய","புதிய / வேறு","Yes");Reject(()=>Apply(f,true));Eq(0,f.Writes);});
            Test("empty correction needs explicit deletion",()=>{var f=new Fixture();var s=f.Service();var n=s.ReadCandidates()[0];Reject(()=>s.Apply(n,n.Proposal.Original,"",ContentRegion.Body,true));Eq(0,f.Writes);});
            Test("reviewed explicit deletion changes only target",()=>{var f=new Fixture();var s=f.Service();var n=s.ReadCandidates()[0];s.Apply(n,n.Proposal.Original,"",ContentRegion.Body,true,true);Eq("\\v 3 முன்  பின்",f.Serialize(f.Tokens));});
            Test("revert restores exact prior USFM",()=>{var f=new Fixture();string before=f.Serialize(f.Tokens);Apply(f);f.Service().Revert();Eq(before,f.Serialize(f.Tokens));Eq(2,f.Writes);});
            Test("revert refuses concurrent edit",()=>{var f=new Fixture();Apply(f);f.Tokens[1]=Fixture.Text("மற்றவர் மாற்றம்",1,3,true,false);Reject(()=>f.Service().Revert());Eq(1,f.Writes);});
            Test("revert cannot cross projects",()=>{var f=new Fixture();Apply(f);Reject(()=>new Fixture().Service().Revert());});
            Test("split formatting token span refused",()=>Reject(()=>ExactEditor.Locate(new[]{Part("பழைய "),Part("சொல்",ContentRegion.Body,1,3,1)},ContentRegion.Body,1,3,3,"பழைய சொல்","புதிய","","")));
            Test("footnote mapping and return to body",()=>{var ts=new List<IUSFMToken>{Fixture.Marker("v","3",1,3,MarkerType.Verse),Fixture.Text("body",1,3,true,false),Fixture.Marker("f","+",1,3,MarkerType.Note),Fixture.Marker("ft","",1,3,MarkerType.Character),Fixture.Text("note",1,3,false,true),Fixture.Marker("f*","",1,3,MarkerType.End),Fixture.Text("body2",1,3,true,false)};var parts=TokenMapping.Parts(ts);Eq(ContentRegion.Body,parts[0].Region);Eq(ContentRegion.Footnote,parts[1].Region);Eq(ContentRegion.Body,parts[2].Region);});
            Test("note context cannot consume body tokens",()=>{var ts=new List<IUSFMToken>{Fixture.Marker("f","+",1,3,MarkerType.Note),Fixture.Text("body",1,3,true,false)};Eq(0,TokenMapping.Parts(ts).Count);});
            Test("introduction mapping",()=>{var ts=new List<IUSFMToken>{Fixture.Marker("ip","",0,0,MarkerType.Paragraph),Fixture.Text("intro",0,0,false,false)};Eq(ContentRegion.Introduction,TokenMapping.Parts(ts)[0].Region);});
            Test("heading mapping",()=>{var ts=new List<IUSFMToken>{Fixture.Marker("s1","",1,3,MarkerType.Paragraph),Fixture.Text("title",1,3,false,false)};Eq(ContentRegion.Heading,TokenMapping.Parts(ts)[0].Region);});
            Test("footnote edit preserves body",()=>{var f=new Fixture();f.NoteText=Fixture.Qa("Grammar","பழைய","புதிய","Yes").Replace("[AI-QA R2] Grammar", "[AI-QA R2] Footnote");f.Tokens.Add(Fixture.Marker("f","+",1,3,MarkerType.Note));f.Tokens.Add(Fixture.Marker("ft","",1,3,MarkerType.Character));f.Tokens.Add(Fixture.Text("பழைய",1,3,false,true));f.Tokens.Add(Fixture.Marker("f*","",1,3,MarkerType.End));Apply(f,true);Eq("முன் பழைய பின்",((IUSFMTextToken)f.Tokens[1]).Text);Eq("புதிய",((IUSFMTextToken)f.Tokens[4]).Text);});
            Test("marker rename preserves label",()=>{var f=new Fixture();f.NoteText=Fixture.Qa("USFM marker","\\ms பழைய","\\cl பழைய","Yes");f.Tokens=new List<IUSFMToken>{Fixture.Marker("ms","",1,3,MarkerType.Paragraph),Fixture.Text("பழைய",1,3,false,false)};var s=f.Service();s.ApplyMarkerRename(s.ReadCandidates()[0]);Eq("\\cl பழைய",f.Serialize(f.Tokens));Eq(1,f.Released);});
            Test("verse numbering cannot be renamed",()=>{var f=new Fixture();f.NoteText=Fixture.Qa("USFM marker","\\v 3","\\v 4","Yes");var s=f.Service();Reject(()=>s.ApplyMarkerRename(s.ReadCandidates()[0]));Eq(0,f.Writes);});
            Test("resolved notes excluded from menu",()=>{var f=new Fixture{Resolved=true};Eq(0,f.Service().ReadCandidates().Count);});
            Test("competing corrections require review",()=>{var f=new Fixture();var a=f.Anchor;f.Notes.Add(Stub<IProjectNote>.Create((m,args2)=>{switch(m.Name){case "get_IsResolved":return false;case "get_Anchor":return a;case "get_Comments":return new List<IComment>{Fixture.Comment(Fixture.Qa("Punctuation","பழைய","வேறு","No"))};default:throw new NotSupportedException(m.Name);}}));var ns=f.Service().ReadCandidates();Eq(2,ns.Count);Eq(true,ns.All(n=>n.Proposal.Route==ProposalRoute.Review));});
            Test("unsafe structural instructions refused",()=>{var f=new Fixture();f.NoteText=Fixture.Qa("USFM marker","பழைய","Exchange verses 3 and 4","Yes");Reject(()=>f.Service().ApplyMarkerRename(f.Service().ReadCandidates()[0]));Eq(0,f.Writes);});
            Test("structural original cannot enter text action",()=>{var f=new Fixture();f.NoteText=Fixture.Qa("USFM marker","\\v 3","\\v 4","Yes");Reject(()=>Apply(f,true));Eq(0,f.Writes);});
            Test("native menu registration verified",()=>Eq(PluginMenuLocation.ScrTextContextMenuDefault,new NoteSuggestionsPlugin().PluginMenuEntries.First().Location));
            Test("footnote region survives form creation",()=>{var f=new Fixture();f.NoteText=Fixture.Qa("Footnote / cross-reference","பழைய","புதிய","Yes").Replace("[AI-QA R2] Footnote / cross-reference","[AI-QA R2] Footnote");var s=f.Service();var ns=s.ReadCandidates();Eq(ContentRegion.Footnote,ns[0].Proposal.Region);using(var form=new ReviewForm(s,ns)){form.StartPosition=FormStartPosition.Manual;form.Location=new System.Drawing.Point(-2000,-2000);form.ShowInTaskbar=false;form.Show();Application.DoEvents();var combo=Descendants(form).OfType<ComboBox>().Single(c=>c.Items.Count==Enum.GetValues(typeof(ContentRegion)).Length);Eq(ContentRegion.Footnote,(ContentRegion)combo.SelectedItem);form.Close();}});
            Test("assembly target verified",()=>Eq(".NETFramework,Version=v4.8",((System.Runtime.Versioning.TargetFrameworkAttribute)Attribute.GetCustomAttribute(typeof(NoteSuggestionsPlugin).Assembly,typeof(System.Runtime.Versioning.TargetFrameworkAttribute))).FrameworkName));
            Console.WriteLine("RESULT: "+passed+" passed, "+failed+" failed. Synthetic fixtures only; no live project touched.");
            return failed==0?0:1;
        }
    }
}
