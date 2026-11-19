using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Paratext.PluginInterfaces;
using ParatextNoteSuggestions;
using F = NoteSuggestionsTests.FastReviewTests;

namespace NoteSuggestionsTests
{
    public static class FootnoteTests
    {
        const string Note = "சோதனை"; // சோதனை
        static Fixture Realistic(string verse = "முன் பழைய பின்")
        { var f = new Fixture { RealisticUsfm = true }; f.Tokens[1] = Fixture.Text(verse, 1, 3, true, false); return f; }
        static void Refused(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Expected a refusal"); }
        static void Insert(Fixture f, string text, bool afterOriginal)
        { var s = f.Service(); var n = s.ReadCandidates()[0]; s.InsertFootnote(n, s.PreviewFootnote(n, text, afterOriginal)); }
        public static void Run(Action<string, Action> test)
        {
            test("footnote after the Original inserts exactly one \\f + \\fr C:V \\ft text\\f* and leaves the verse text unchanged", delegate {
                var f = Realistic(); Insert(f, Note, true);
                F.Check(f.Serialize(f.Tokens) == "\\v 3 முன் பழைய\\f + \\fr 1:3 \\ft " + Note + "\\f* பின்", f.Serialize(f.Tokens));
                F.Check(f.Writes == 1 && f.Released == 1 && f.History == 2 && f.Notifications == 1);
            });
            test("footnote at the end of the verse goes before trailing space", delegate {
                var f = Realistic("முன் பழைய பின் "); Insert(f, Note, false);
                F.Check(f.Serialize(f.Tokens) == "\\v 3 முன் பழைய பின்\\f + \\fr 1:3 \\ft " + Note + "\\f* ", f.Serialize(f.Tokens));
            });
            test("footnote text with a backslash, line break, brace, bar or Latin letters is refused before reading Scripture", delegate {
                foreach (string bad in new[] { Note + "\\f*", Note + "\r\nமேலும்", "{" + Note + "}", Note + "|x", "some copies " + Note, " " + Note, "" }) {
                    var f = Realistic(); Refused(() => Insert(f, bad, true));
                    F.Check(f.Writes == 0 && f.History == 0, "refused: " + bad);
                }
            });
            test("footnote refuses an identical footnote already in the verse", delegate {
                var f = Realistic();
                f.Tokens.Add(Fixture.Marker("f", "+", 1, 3, MarkerType.Note)); f.Tokens.Add(Fixture.Marker("fr", "", 1, 3, MarkerType.Character)); f.Tokens.Add(Fixture.Text("1:3 ", 1, 3, false, true));
                f.Tokens.Add(Fixture.Marker("ft", "", 1, 3, MarkerType.Character)); f.Tokens.Add(Fixture.Text(Note, 1, 3, false, true)); f.Tokens.Add(Fixture.Marker("f*", "", 1, 3, MarkerType.End));
                Refused(() => Insert(f, Note, false)); F.Check(f.Writes == 0);
            });
            test("footnote refuses when Paratext's reparse does not reproduce the chapter, and writes nothing", delegate {
                var f = Realistic(); f.ReparseDropsLastToken = true;
                Refused(() => Insert(f, Note, true)); F.Check(f.Writes == 0);
                var g = Realistic(); g.RealisticUsfm = false; // the host cannot parse the prepared text
                Refused(() => Insert(g, Note, true)); F.Check(g.Writes == 0);
            });
            test("footnote refuses to split a word and an Original that occurs twice without context", delegate {
                var f = Realistic(); f.NoteText = Fixture.Qa("Source comparison", "பழை", "புதிய", "Yes"); f.AnchorText = "பழை";
                Refused(() => Insert(f, Note, true)); F.Check(f.Writes == 0);
                var g = Realistic("பழைய இடை பழைய"); g.AnchorText = "";
                Refused(() => Insert(g, Note, true)); F.Check(g.Writes == 0);
                Insert(g, Note, false); F.Check(g.Writes == 1, "end of verse still possible");
            });
            test("footnote takes its history point before the lock, and a denied or revoked lock writes nothing", delegate {
                var f = Realistic(); f.LockAvailable = false; Refused(() => Insert(f, Note, true)); F.Check(f.Writes == 0);
                var g = Realistic(); g.Revoke = true; Refused(() => Insert(g, Note, true)); F.Check(g.Writes == 0 && g.Released == 1);
                var h = Realistic(); h.Editable = false; Refused(() => Insert(h, Note, true)); F.Check(h.Writes == 0);
            });
            test("footnote refused when the position changed after the preview", delegate {
                var f = Realistic(); var s = f.Service(); var n = s.ReadCandidates()[0]; var plan = s.PreviewFootnote(n, Note, true);
                f.Tokens[1] = Fixture.Text("இடை முன் பழைய பின்", 1, 3, true, false);
                Refused(() => s.InsertFootnote(n, plan, null, value => { if (value.Before != plan.Preview.Before) throw new InvalidOperationException("changed"); }));
                F.Check(f.Writes == 0);
            });
            test("footnote decision through the queue records the insertion for the reply and report", delegate {
                var f = Realistic(); var matthew = new Fixture(); matthew.Ref = Fixture.MakeRef(5, 6, 40); f.AvailableBooks.Add(Fixture.Book(40, "MAT")); f.Notes.Add(matthew.Notes[0]);
                using (var q = QueueTests.Queue(f, F.Root())) {
                    F.Enable(q, NoLiteralMode.StopForMe, false); var item = F.Current(q);
                    var plan = f.Service().ForQueueItem(item).PreviewFootnote(item, Note, true);
                    plan.Template = "சில பிரதிகளில் {text} என்று உள்ளது."; plan.Source = "note Original";
                    F.Call(q, "DecideWith", "Applied", new FastChoice { Id = item.StableId, Original = plan.Target, Replacement = plan.Target + plan.Usfm, Region = ContentRegion.Body, Preview = plan.Preview, Footnote = plan,
                        Comment = "Footnote added; verse wording unchanged: " + Note, Source = "footnote" });
                    var record = F.Saved(q, item);
                    F.Check(f.Writes == 1 && record.Outcome == "Applied" && record.Original == "பழைய" && record.Replacement == "பழைய\\f + \\fr 1:3 \\ft " + Note + "\\f*");
                    F.Check(record.Comment.StartsWith("Footnote added") && record.Evidence.Contains("one footnote") && record.Evidence.Contains("note Original") && record.AfterContext.Contains("\\ft " + Note));
                    F.Check(f.Replies.Count == 1 && f.Resolved, "reply posted and note resolved");
                }
            });
            test("an unverified footnote save keeps Pending apply and cannot be repeated blindly", delegate {
                var f = Realistic(); f.IgnorePut = true;
                using (var q = QueueTests.Queue(f, F.Root())) {
                    F.Enable(q); var item = F.Current(q); var plan = f.Service().ForQueueItem(item).PreviewFootnote(item, Note, true);
                    F.Call(q, "DecideWith", "Applied", new FastChoice { Id = item.StableId, Original = plan.Target, Replacement = plan.Target + plan.Usfm, Region = ContentRegion.Body, Preview = plan.Preview, Footnote = plan, Comment = "x", Source = "footnote" });
                    F.Check(F.Saved(q, item).Outcome == "Pending apply" && F.Field<bool>(q, "paused") && f.Writes == 1);
                    F.Key(q, Keys.F8); F.Check(f.Writes == 1, "footnote key unavailable while recovery is pending");
                }
            });
            test("footnote templates allow {text} once and refuse markup or Latin; defaults are the project's own forms", delegate {
                foreach (string t in FootnotePresets.Defaults) FootnotePresets.Validate(t);
                F.Check(FootnotePresets.Defaults.Length == 5 && FootnotePresets.Defaults[0] == "சில பிரதிகளில் {text} இவ்வாறு உள்ளது." && FootnotePresets.Defaults[1] == "சில பிரதிகளில் {text} காணப்படவில்லை.");
                foreach (string bad in new[] { "சில {text} பிரதிகளில் {text}", "சில \\ft பிரதிகளில் {text}", "some copies {text}", "" }) Refused(() => FootnotePresets.Validate(bad));
                F.Check(FootnotePresets.Render("சில பிரதிகளில் {text} காணப்படவில்லை.", "  இந்த வார்த்தை ") == "சில பிரதிகளில் இந்த வார்த்தை காணப்படவில்லை.");
                string root = F.Root(); System.IO.Directory.CreateDirectory(root);
                FootnotePresets.Save(root, new List<string> { "சில பிரதிகளில் {text} உள்ளது." });
                F.Check(FootnotePresets.Load(root).SequenceEqual(new[] { "சில பிரதிகளில் {text} உள்ளது." }));
                Refused(() => FootnotePresets.Save(root, new List<string> { "bad {text}" }));
            });
            test("footnote composer pulls the quoted text from the note, the editor selection and the reference text", delegate {
                var f = Realistic(); var item = f.Service().ReadQueueBook(1)[0];
                var composer = (Form)Activator.CreateInstance(typeof(QueueForm).Assembly.GetType("ParatextNoteSuggestions.FootnoteComposer"), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null,
                    new object[] { item, FootnotePresets.Defaults.ToList(), (Func<string, bool, FootnotePlan>)((text, after) => f.Service().ForQueueItem(item).PreviewFootnote(item, text, after)),
                        (Func<string>)(() => "தேர்ந்த சொல்"), (Func<string>)(() => "  வேத   வாக்கியம் "), (Action<IList<string>>)(t => { }), ParatextTheme.Font }, null);
                using (composer) {
                    var sources = F.Field<Dictionary<Keys, Func<string>>>(composer, "sources"); var quoted = F.Field<TextBox>(composer, "quoted");
                    F.Check(quoted.Text == "பழைய", "note Original by default");
                    sources[Keys.P](); F.Check(quoted.Text == "தேர்ந்த சொல்");
                    sources[Keys.B](); F.Check(quoted.Text == "வேத வாக்கியம்");
                    F.Call(composer, "RefreshPreview");
                    var plan = (FootnotePlan)composer.GetType().GetProperty("Plan").GetValue(composer);
                    F.Check(plan != null && plan.Text == "சில பிரதிகளில் வேத வாக்கியம் இவ்வாறு உள்ளது." && plan.Source == "reference text" && f.Writes == 0);
                }
            });
        }
    }
}
