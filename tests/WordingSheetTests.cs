using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;
using ParatextNoteSuggestions;
using F = NoteSuggestionsTests.FastReviewTests;

namespace NoteSuggestionsTests
{
    public static class WordingSheetTests
    {
        // A Source comparison note whose Suggested is an instruction, not ready-to-paste Tamil.
        static Fixture Instruction() { return F.TwoBooks(Fixture.Qa("Source comparison", "பழைய", "Use the BSI reading here", "Yes")); }
        static string ExportFor(QueueForm q, Fixture f, string root)
        {
            string path = Path.Combine(root, "export.xml");
            F.Field<WordingSheet>(q, "wording").Export(path, f.ProjectId, "Synthetic", F.Field<List<NoteCandidate>>(q, "items"), f.Service());
            return path;
        }
        static string Sheet(string root, string projectId, params XElement[] entries)
        {
            string path = Path.Combine(root, "sheet-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".xml");
            new XElement("wordingSheet", new XAttribute("version", 1), new XAttribute("projectId", projectId), entries).Save(path); return path;
        }
        static XElement Entry(string id, string hash, string original, string replacement)
        { return new XElement("wording", new XAttribute("id", id), new XAttribute("verseHash", hash), new XElement("original", original), new XElement("replacement", replacement), new XElement("rationale", "BSI reads this word.")); }
        public static void Run(Action<string, Action> test)
        {
            test("wording export lists open undecided notes with id, note fields, verse USFM and its hash", delegate {
                var f = Instruction(); string root = F.Root();
                using (var q = QueueTests.Queue(f, root)) {
                    var genesis = F.Current(q); var export = XElement.Load(ExportFor(q, f, root));
                    var p = export.Elements("proposal").Single(e => (string)e.Attribute("id") == genesis.StableId);
                    F.Check((string)export.Attribute("projectId") == f.ProjectId && (string)p.Attribute("category") == "Source comparison" && (string)p.Element("suggested") == "Use the BSI reading here");
                    F.Check((string)p.Element("verseUsfm") == "\\v 3 முன் பழைய பின்" && (string)p.Attribute("verseHash") == NoteParser.Hash("\\v 3 முன் பழைய பின்") && (string)p.Element("text") == "முன் பழைய பின்");
                    F.Check(f.Writes == 0 && f.Replies.Count == 0);
                }
            });
            test("wording import ignores unknown, stale, repeated, ambiguous, non-Tamil and markup entries and logs each reason", delegate {
                var f = Instruction(); string root = F.Root();
                using (var q = QueueTests.Queue(f, root)) {
                    F.Enable(q, NoLiteralMode.PreparedWording); var genesis = F.Current(q); ExportFor(q, f, root);
                    string hash = NoteParser.Hash("\\v 3 முன் பழைய பின்");
                    string sheet = Sheet(root, f.ProjectId,
                        Entry(new string('A', 64), hash, "பழைய", "புதிய"),            // unknown id
                        Entry(genesis.StableId, new string('B', 64), "பழைய", "புதிய"),  // stale verse
                        Entry(genesis.StableId, hash, "பழைய", "புதிய"),                 // repeated
                        Entry(genesis.StableId, hash, "பழைய", "new word"),              // Latin
                        Entry(genesis.StableId, hash, "பழைய", "புதிய\\f*"),             // markup
                        Entry("not-an-id", hash, "பழைய", "புதிய"));                      // malformed
                    F.Call(q, "ImportWordingFile", sheet);
                    var log = String.Join("\n", F.Field<List<string>>(q, "fastLog"));
                    foreach (string reason in new[] { "unknown proposal id", "the verse changed since the export", "repeated in the sheet", "not plain Tamil", "backslash", "malformed proposal id" }) F.Check(log.Contains(reason), reason + "\n" + log);
                    F.Check(F.Field<WordingSheet>(q, "wording").Get(genesis.StableId) == null && f.Writes == 0);
                    string ambiguous = Sheet(root, f.ProjectId, Entry(genesis.StableId, hash, "இல்லாத", "புதிய"));
                    F.Call(q, "ImportWordingFile", ambiguous);
                    F.Check(String.Join("\n", F.Field<List<string>>(q, "fastLog")).Contains("not found") && F.Field<WordingSheet>(q, "wording").Get(genesis.StableId) == null);
                }
            });
            test("wording import refuses a sheet for another project", delegate {
                var f = Instruction(); string root = F.Root();
                using (var q = QueueTests.Queue(f, root)) {
                    string sheet = Sheet(root, Guid.NewGuid().ToString(), Entry(F.Current(q).StableId, "x", "பழைய", "புதிய"));
                    try { F.Call(q, "ImportWordingFile", sheet); throw new Exception("Expected a refusal"); } catch (InvalidOperationException) { }
                }
            });
            test("prepared wording is labelled as not from the note and applied only by the key through the verified write", delegate {
                var f = Instruction(); string root = F.Root();
                using (var q = QueueTests.Queue(f, root)) {
                    F.Enable(q, NoLiteralMode.StopForMe, false); var genesis = F.Current(q);
                    F.Check(F.Field<FastChoice>(q, "fastChoice") == null, "instruction note has nothing to apply");
                    ExportFor(q, f, root);
                    F.Call(q, "ImportWordingFile", Sheet(root, f.ProjectId, Entry(genesis.StableId, NoteParser.Hash("\\v 3 முன் பழைய பின்"), "பழைய", "புதிய")));
                    F.Check(F.Field<FastChoice>(q, "fastChoice") == null && f.Writes == 0, "not used outside prepared wording mode");
                    F.Enable(q, NoLiteralMode.PreparedWording, false); F.Call(q, "LoadCurrent");
                    var choice = F.Field<FastChoice>(q, "fastChoice");
                    F.Check(choice != null && !choice.OwnWording && choice.Source.StartsWith("PREPARED WORDING - not from the note") && F.Field<Label>(q, "fastSource").Text.Contains("PREPARED WORDING"));
                    F.Check(f.Writes == 0, "nothing written before the key");
                    F.Key(q, Keys.F5);
                    var record = F.Saved(q, genesis);
                    F.Check(f.Writes == 1 && record.Outcome == "Applied" && record.Replacement == "புதிய" && record.Comment == CommentPresets.Values[1] && record.Evidence.Contains("PREPARED WORDING"));
                }
            });
            test("prepared wording made stale by an edit falls back to stop-for-me", delegate {
                var f = Instruction(); string root = F.Root();
                using (var q = QueueTests.Queue(f, root)) {
                    F.Enable(q, NoLiteralMode.PreparedWording); var genesis = F.Current(q); ExportFor(q, f, root);
                    F.Call(q, "ImportWordingFile", Sheet(root, f.ProjectId, Entry(genesis.StableId, NoteParser.Hash("\\v 3 முன் பழைய பின்"), "பழைய", "புதிய")));
                    F.Check(F.Field<FastChoice>(q, "fastChoice") != null);
                    f.Tokens[1] = Fixture.Text("முன் பழைய இடை பின்", 1, 3, true, false);
                    F.Call(q, "LoadCurrent");
                    F.Check(F.Field<FastChoice>(q, "fastChoice") == null && F.Field<Label>(q, "fastReason").Text.Contains("verse changed"));
                    F.Key(q, Keys.F5); F.Check(f.Writes == 0);
                }
            });
        }
    }
}
