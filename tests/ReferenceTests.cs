using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ParatextNoteSuggestions;
using F = NoteSuggestionsTests.FastReviewTests;

namespace NoteSuggestionsTests
{
    public static class ReferenceTests
    {
        const string Bsi = "பைபிள் சங்கத்தின் உரை";
        public static void Run(Action<string, Action> test)
        {
            test("reference text is read through the source's versification and labelled with its reference", delegate {
                var f = new Fixture(); f.OtherProjects.Add(Fixture.Source("TAOVBSI", Bsi, false));
                var item = f.Service().ReadQueueBook(1)[0]; string label;
                F.Check(f.Service().ReadReference("TAOVBSI", item, out label) == Bsi && label == "TAOVBSI GEN 1:3", label);
                F.Check(f.Service().ReferenceSourceNames().SequenceEqual(new[] { "TAOVBSI" }), "own project is not offered as a reference");
            });
            test("a resource Paratext refuses is reported once, not read again, and the queue keeps working", delegate {
                int reads = 0; var f = F.TwoBooks(); f.OtherProjects.Add(Fixture.Source("ESVUS11", "", true, () => reads++)); f.OtherProjects.Add(Fixture.Source("TAOVBSI", Bsi, false));
                using (var q = QueueTests.Queue(f, F.Root())) {
                    var settings = new FastSettings { Enabled = true, References = new System.Collections.Generic.List<string> { "ESVUS11", "TAOVBSI" } };
                    F.Call(q, "ApplyFast", settings, true);
                    F.Key(q, Keys.PageDown); F.Key(q, Keys.PageUp);
                    var box = F.Field<TextBox>(q, "referenceBox").Text;
                    F.Check(reads == 1 && box.Contains("ESVUS11: not readable by plugins") && box.Contains(Bsi), box);
                    F.Key(q, Keys.F5); F.Check(f.Writes == 1, "decisions are unaffected");
                }
            });
            test("reference text is never written to decisions, reports or the wording export", delegate {
                var f = F.TwoBooks(); f.OtherProjects.Add(Fixture.Source("TAOVBSI", Bsi, false)); string root = F.Root();
                using (var q = QueueTests.Queue(f, root)) {
                    F.Call(q, "ApplyFast", new FastSettings { Enabled = true, HoldLastReply = false, References = new System.Collections.Generic.List<string> { "TAOVBSI" } }, true);
                    F.Check(F.Field<TextBox>(q, "referenceBox").Text.Contains(Bsi));
                    var first = F.Current(q); F.Key(q, Keys.F5);
                    F.Check(!F.Saved(q, first).ToXml().ToString().Contains(Bsi));
                    string export = Path.Combine(root, "export.xml");
                    F.Field<WordingSheet>(q, "wording").Export(export, f.ProjectId, "Synthetic", F.Field<System.Collections.Generic.List<NoteCandidate>>(q, "items"), f.Service());
                    F.Check(!File.ReadAllText(export).Contains(Bsi));
                    q.Close();
                    F.Check(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(p => p.EndsWith(".xml") || p.EndsWith(".html")).All(p => !File.ReadAllText(p).Contains(Bsi)), "no saved file holds reference text");
                }
            });
            test("open reference windows uses Paratext's own resource windows", delegate {
                var f = new Fixture(); f.OtherProjects.Add(Fixture.Source("TAOVBSI", Bsi, true));
                var item = f.Service().ReadQueueBook(1)[0];
                var lines = f.Service().OpenReferenceWindows(new[] { "TAOVBSI", "MISSING" }, item);
                F.Check(f.Navigations == 1 && f.LastNavigationProject.ShortName == "TAOVBSI" && lines[0] == "TAOVBSI: opened." && lines[1] == "MISSING: not installed.");
            });
        }
    }
}
