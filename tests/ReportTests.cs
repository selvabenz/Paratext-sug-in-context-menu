using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Xml.Linq;
using ParatextNoteSuggestions;

namespace NoteSuggestionsTests
{
    public static class ReportTests
    {
        static string Root() { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rp", Guid.NewGuid().ToString("N").Substring(0, 8)); }
        static void Check(bool value) { if (!value) throw new Exception("Report assertion failed"); }
        static T Field<T>(object q, string name) { return (T)q.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(q); }
        static object Call(object q, string name, params object[] args)
        { try { return q.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(q, args); } catch (TargetInvocationException ex) { throw ex.InnerException; } }
        static string Project(string root, string id)
        { string path = Path.Combine(root, "p"); Directory.CreateDirectory(path); new XElement("ScriptureText", new XElement("Guid", id)).Save(Path.Combine(path, "Settings.xml")); return path; }
        static ReviewRecord Entry(ReviewSession session, int n = 0)
        {
            return new ReviewRecord { Id = "source-" + n, AuditId = Guid.NewGuid().ToString("N"), SessionId = session.Id,
                Outcome = n % 2 == 0 ? "Applied" : "Rejected", Resolution = "Resolved", Reviewer = "Reviewer A", Actor = "Reviewer A", DecisionUtc = DateTime.UtcNow.ToString("O"), ActionUtc = DateTime.UtcNow.ToString("O"),
                Reference = "GEN 1:" + (n + 1), Category = "Punctuation", Region = "Body", BeforeContext = "\\v 1 முன் பழைய பின்", AfterContext = "\\v 1 முன் புதிய பின்", Original = "பழைய", Replacement = "புதிய", Comment = "Reviewed", Evidence = "Verified saved USFM" };
        }
        public static void Run(Action<string, Action> test)
        {
            test("five compiled presets fill an editable comment without taking a decision", delegate {
                var f = new Fixture(); using (var q = QueueTests.Queue(f, Root()))
                {
                    Check(CommentPresets.Values.Length == 5 && CommentPresets.Values.Distinct().Count() == 5);
                    var presets = Field<ComboBox>(q, "commentPreset"); presets.SelectedIndex = 3;
                    var comment = Field<TextBox>(q, "comment"); Check(comment.Text == CommentPresets.Values[2]);
                    comment.Text += " Added context."; Check(comment.Text.EndsWith("Added context.") && f.Writes == 0 && f.Resolves == 0); q.Close();
                }
            });
            test("applied report retains full before/after and actual Paratext reviewer", delegate {
                var f = new Fixture { UserName = "Reviewer <A>" }; string root = Root(), id = f.Service().ReadQueueBook(1).Single().StableId;
                using (var q = QueueTests.Queue(f, root)) { Field<CheckBox>(q, "reviewed").Checked = true; Call(q, "Decide", "Applied"); Check(f.Writes == 1 && f.Resolved); q.Close(); }
                using (var p = new ReviewProgress(root, f.ProjectId))
                {
                    var r = p.Get(id); Check(r.Reviewer == "Reviewer <A>" && r.BeforeContext.Contains("முன் பழைய பின்") && r.AfterContext.Contains("முன் புதிய பின்") && r.Evidence.Contains("Verified saved"));
                    string report = ReviewSession.Latest(p.DirectoryPath); string html = File.ReadAllText(report); Check(report.Contains("Review Queue") && html.Contains("Reviewer &lt;A&gt;") && html.Contains("<mark"));
                    f.Tokens[1] = Fixture.Text("Later unrelated change", 1, 3, true, false); Check(p.Get(id).AfterContext.Contains("புதிய"));
                }
            });
            test("rejected report retains identical passage and escapes private comment markup", delegate {
                var f = new Fixture(); string root = Root(), id = f.Service().ReadQueueBook(1).Single().StableId;
                using (var q = QueueTests.Queue(f, root)) { Field<TextBox>(q, "comment").Text = "<script>alert('note')</script> & reason"; Call(q, "Decide", "Rejected"); Check(f.Writes == 0 && f.Resolved); q.Close(); }
                using (var p = new ReviewProgress(root, f.ProjectId))
                {
                    var r = p.Get(id); Check(r.BeforeContext == r.AfterContext && r.BeforeContext.Contains("முன்"));
                    string html = File.ReadAllText(ReviewSession.Latest(p.DirectoryPath)); Check(html.Contains("&lt;script&gt;") && !html.Contains("<script>alert") && html.Contains("existing text retained"));
                }
            });
            test("manual report compares observed load and decision contexts without claiming editor", delegate {
                var f = new Fixture(); string root = Root(), id = f.Service().ReadQueueBook(1).Single().StableId;
                using (var q = QueueTests.Queue(f, root))
                {
                    f.Tokens[1] = Fixture.Text("முன் புதிய பின்", 1, 3, true, false);
                    Field<TextBox>(q, "comment").Text = "Completed in Paratext"; Field<CheckBox>(q, "reviewed").Checked = true; Call(q, "Decide", "Completed manually"); q.Close();
                }
                using (var p = new ReviewProgress(root, f.ProjectId)) { var r = p.Get(id); Check(r.BeforeContext.Contains("பழைய") && r.AfterContext.Contains("புதிய") && r.Evidence.Contains("actual editor") && f.Writes == 0); }
            });
            test("unverified edit report labels expected result and preserves pending audit", delegate {
                var f = new Fixture { IgnorePut = true }; string root = Root(), id = f.Service().ReadQueueBook(1).Single().StableId;
                using (var q = QueueTests.Queue(f, root)) { Field<CheckBox>(q, "reviewed").Checked = true; Call(q, "Decide", "Applied"); q.Close(); }
                using (var p = new ReviewProgress(root, f.ProjectId))
                { var r = p.Get(id); Check(r.Outcome == "Pending apply" && r.BeforeContext.Contains("பழைய") && r.AfterContext.Contains("புதிய")); Check(File.ReadAllText(ReviewSession.Latest(p.DirectoryPath)).Contains("EXPECTED AFTER - NOT VERIFIED")); }
            });
            test("full passage evidence is captured under Scripture lock before submission", delegate {
                var f = new Fixture(); var s = f.Service(); var c = s.ReadCandidates().Single(); bool captured = false;
                s.Apply(c, c.Proposal.Original, c.Proposal.Suggested, c.Proposal.Region, true, false, delegate { Check(captured && f.LockHeld && f.Writes == 0); }, delegate(PassageEvidence e) { Check(f.LockHeld && f.Writes == 0 && e.Before.Contains("முன் பழைய பின்") && e.After.Contains("முன் புதிய பின்")); captured = true; }); Check(f.Writes == 1);
            });
            test("footnote report includes the entire verse and preserves body context", delegate {
                var f = new Fixture(); f.NoteText = Fixture.Qa("Grammar", "பழைய", "புதிய", "Yes").Replace("[AI-QA R2] Grammar", "[AI-QA R2] Footnote");
                f.Tokens.Add(Fixture.Marker("f", "+", 1, 3, Paratext.PluginInterfaces.MarkerType.Note)); f.Tokens.Add(Fixture.Marker("ft", "", 1, 3, Paratext.PluginInterfaces.MarkerType.Character));
                f.Tokens.Add(Fixture.Text("பழைய", 1, 3, false, true)); f.Tokens.Add(Fixture.Marker("f*", "", 1, 3, Paratext.PluginInterfaces.MarkerType.End));
                var s = f.Service(); var c = s.ReadCandidates().Single(); PassageEvidence captured = null;
                s.Apply(c, "பழைய", "புதிய", ContentRegion.Footnote, true, false, null, e => captured = e);
                Check(captured.Before.Contains("முன் பழைய பின்") && captured.After.Contains("முன் பழைய பின்") && captured.Before.Contains("\\ft பழைய") && captured.After.Contains("\\ft புதிய"));
            });
            test("introduction snapshot includes full introduction and excludes chapter text", delegate {
                var f = new Fixture(); f.Tokens.Insert(0, Fixture.Text("Introduction context", 0, 0, false, false)); f.Tokens.Insert(0, Fixture.Marker("ip", "", 0, 0, Paratext.PluginInterfaces.MarkerType.Paragraph));
                f.Tokens.Insert(2, Fixture.Marker("c", "1", 1, 0, Paratext.PluginInterfaces.MarkerType.Chapter)); var s = f.Service();
                string context = s.CapturePassage(s.ReadCandidates().Single(), ContentRegion.Introduction); Check(context.Contains("Introduction context") && !context.Contains("முன்"));
            });
            test("cross-chapter rejected snapshot includes both anchored chapter portions", delegate {
                var f = new Fixture(); f.Tokens.Add(Fixture.Text("Second chapter context", 2, 1, true, false)); var s = f.Service(); var c = s.ReadCandidates().Single();
                c.CrossChapter = true; c.EndChapter = 2; c.LastVerse = 1; string context = s.CapturePassage(c, ContentRegion.Body); Check(context.Contains("முன்") && context.Contains("Second chapter context"));
            });
            test("changing Paratext user during write preparation prevents misattribution", delegate {
                var f = new Fixture(); string root = Root(); using (var q = QueueTests.Queue(f, root))
                { f.OnHistory = delegate { f.UserName = "Other User"; }; Field<CheckBox>(q, "reviewed").Checked = true; Call(q, "Decide", "Applied"); Check(f.Writes == 0 && !f.Resolved); q.Close(); }
            });
            test("20 decisions out of 100 create one cumulative report and no empty duplicates", delegate {
                string root = Root(), project = Project(root, "project-id"); using (var p = new ReviewProgress(root, "project-id"))
                {
                    var session = new ReviewSession(p.DirectoryPath, "project-id", "Test", "Reviewer A"); Check(session.WriteReport(project, 100, 0, "Paused") == null);
                    for (int n = 0; n < 20; n++) p.Save(Entry(session, n));
                    string first = session.WriteReport(project, 100, 20, "Paused"); string html = File.ReadAllText(first);
                    Check(session.Records().Count == 20 && html.Contains("20 reviewed / 100 indexed") && html.Split(new[] { "<article " }, StringSplitOptions.None).Length == 21);
                    Check(session.WriteReport(project, 100, 20, "Closed") == first && Directory.GetFiles(Path.Combine(project, "Review Queue"), "*.html").Length == 1);
                    p.Save(Entry(session, 20)); string next = session.WriteReport(project, 100, 21, "Paused"); Check(next != first && File.Exists(first));
                }
            });
            test("interrupted session produces a recovered report after reopening", delegate {
                string root = Root(), project = Project(root, "project-id"); using (var p = new ReviewProgress(root, "project-id"))
                {
                    var interrupted = new ReviewSession(p.DirectoryPath, "project-id", "Test", "Reviewer A"); p.Save(Entry(interrupted));
                    var current = new ReviewSession(p.DirectoryPath, "project-id", "Test", "Reviewer B");
                    Check(ReviewSession.NeedsRecovery(p.DirectoryPath, current.Id)); Check(ReviewSession.Recover(p.DirectoryPath, current.Id, project, "project-id") == 1);
                    Check(File.ReadAllText(ReviewSession.Latest(p.DirectoryPath)).Contains("Recovered interrupted session")); Check(!ReviewSession.NeedsRecovery(p.DirectoryPath, current.Id));
                }
            });
            test("wrong project folder cannot receive reports", delegate {
                string root = Root(), project = Project(root, "wrong-id"); using (var p = new ReviewProgress(root, "project-id"))
                { var s = new ReviewSession(p.DirectoryPath, "project-id", "Test", "Reviewer A"); p.Save(Entry(s)); bool refused = false; try { s.WriteReport(project, 1, 1, "Paused"); } catch (InvalidOperationException) { refused = true; } Check(refused && !Directory.Exists(Path.Combine(project, "Review Queue"))); }
            });
            test("malformed audit paths cannot escape local progress", delegate {
                string root = Root(); using (var p = new ReviewProgress(root, "project-id"))
                { bool refused = false; try { p.Save(new ReviewRecord { Id = "bad", SessionId = "../outside", AuditId = "../outside" }); } catch (InvalidOperationException) { refused = true; } Check(refused && p.Get("bad") == null); }
            });
            test("canonical saved audit repairs a missing session copy", delegate {
                string root = Root(); using (var p = new ReviewProgress(root, "project-id"))
                {
                    var s = new ReviewSession(p.DirectoryPath, "project-id", "Test", "Reviewer A"); var r = Entry(s); p.Save(r);
                    string path = Path.Combine(p.DirectoryPath, "sessions", s.Id, r.AuditId + ".entry.xml");
                    File.Delete(path); p.Outstanding().ToList(); Check(File.Exists(path) && s.Records().Single().BeforeContext == r.BeforeContext);
                }
            });
            test("Tamil diff highlighting never splits combining sequences", delegate {
                var r = new ReviewRecord { BeforeContext = "கா", AfterContext = "கி" };
                Check(ReviewReportHtml.ContextHtml(r, false).Contains(">கா</mark>") && ReviewReportHtml.ContextHtml(r, true).Contains(">கி</mark>"));
            });
            test("old snapshots remain explicitly unavailable instead of reconstructed", delegate {
                var r = ReviewRecord.FromXml(new XElement("review", new XAttribute("id", "old"), new XAttribute("outcome", "Rejected")));
                string html = ReviewReportHtml.Render("Test", "Current User", "session", "", "", "Recovered", 1, 1, new[] { r }); Check(html.Contains("Context not recorded") && html.Contains("Not recorded by older version"));
            });
        }
    }
}
