using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using ParatextNoteSuggestions;

namespace NoteSuggestionsTests
{
    public static class QueueTests
    {
        static void Check(bool yes) { if (!yes) throw new Exception("Queue assertion failed"); }
        static bool FailInfo(QueueForm q) { throw new Exception(Field<Label>(q, "status").Text); }
        static string Root() { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "queue-tests", Guid.NewGuid().ToString("N")); }
        static T Field<T>(object o, string name) { return (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(o); }
        static void Call(object o, string name, params object[] args)
        { try { o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(o, args); } catch (TargetInvocationException ex) { throw ex.InnerException; } }
        static QueueForm Queue(Fixture f, string root)
        {
            var q = new QueueForm(f.Service(), root);
            Call(q, "IndexBook"); Call(q, "IndexBook"); return q;
        }
        public static void Run(Action<string, Action> test)
        {
            test("apply saves decision then resolves the original note", delegate {
                var f = new Fixture(); string root = Root(), id = f.Service().ReadQueueBook(1).Single().StableId;
                using (var q = Queue(f, root)) { Field<CheckBox>(q, "reviewed").Checked = true; Call(q, "Decide", "Applied"); Check(f.Writes == 1 && f.Resolved && f.Resolves == 1 && !f.LockHeld); q.Close(); }
                using (var p = new ReviewProgress(root, f.ProjectId)) Check(p.Get(id).Outcome == "Applied" && p.Get(id).Resolution == "Resolved");
            });
            test("rejection resolves note but never edits Scripture", delegate {
                var f = new Fixture(); using (var q = Queue(f, Root()))
                { Field<TextBox>(q, "comment").Text = "Keep the existing reading"; Call(q, "Decide", "Rejected"); Check(f.Writes == 0 && f.Resolved && f.Resolves == 1); q.Close(); }
            });
            test("manual completed and no-change decisions resolve note", delegate {
                var f = new Fixture(); using (var q = Queue(f, Root()))
                { Field<TextBox>(q, "comment").Text = "Verified no change needed"; Field<CheckBox>(q, "reviewed").Checked = true; Call(q, "Decide", "Completed manually"); Check(f.Writes == 0 && f.Resolved); q.Close(); }
            });
            test("thread remains open until every QA proposal has a decision", delegate {
                var f = new Fixture(); f.Replies.Add(Fixture.Comment(Fixture.Qa("Grammar", "பழைய", "வேறு", "Yes")));
                string root = Root(); var ids = f.Service().ReadQueueBook(1).Select(c => c.StableId).ToArray();
                using (var q = Queue(f, root))
                {
                    Field<TextBox>(q, "comment").Text = "First decision"; Call(q, "Decide", "Rejected"); Check(!f.Resolved && f.Resolves == 0 && Field<NoteCandidate>(q, "current") != null);
                    Field<TextBox>(q, "comment").Text = "Second decision"; Call(q, "Decide", "Rejected"); Check(f.Resolved && f.Resolves == 1); q.Close();
                }
                using (var p = new ReviewProgress(root, f.ProjectId)) Check(ids.All(id => p.Get(id).Resolution == "Resolved"));
            });
            test("notes lock failure resumes resolution without repeating an applied edit", delegate {
                var f = new Fixture { NotesLockAvailable = false }; string root = Root(), id = f.Service().ReadQueueBook(1).Single().StableId;
                using (var q = Queue(f, root)) { Field<CheckBox>(q, "reviewed").Checked = true; Call(q, "Decide", "Applied"); Check(f.Writes == 1 && !f.Resolved && Field<NoteCandidate>(q, "current") != null); q.Close(); }
                using (var p = new ReviewProgress(root, f.ProjectId)) Check(p.Get(id).Outcome == "Applied" && p.Get(id).ResolutionPending);
                f.NotesLockAvailable = true;
                using (var q = Queue(f, root)) { Check(!Field<Button>(q, "apply").Enabled); Call(q, "FinishResolution"); Check(f.Writes == 1 && f.Resolved && f.Resolves == 1); q.Close(); }
            });
            test("lost local resolve acknowledgement is idempotent across reopen", delegate {
                var f = new Fixture(); string root = Root(); var c = f.Service().ReadQueueBook(1).Single();
                using (var p = new ReviewProgress(root, f.ProjectId)) p.Save(new ReviewRecord { Id = c.StableId, Outcome = "Rejected", Comment = "reviewed", Resolution = "Pending", Discussion = c.DiscussionKey });
                f.Resolved = true;
                using (var q = Queue(f, root)) { Check(Field<NoteCandidate>(q, "current") != null); Call(q, "FinishResolution"); Check(f.Resolves == 0 && f.Writes == 0); q.Close(); }
                using (var p = new ReviewProgress(root, f.ProjectId)) Check(p.Get(c.StableId).Resolution == "Resolved");
            });
            test("changed discussion cannot be resolved automatically", delegate {
                var f = new Fixture(); var s = f.Service(); var c = s.ReadQueueBook(1).Single(); f.Replies.Add(Fixture.Comment("Wait for a reviewer"));
                bool refused = false; System.Collections.Generic.List<string> ids;
                try { s.ResolveReviewedThread(c, c.DiscussionKey, id => true, out ids); } catch (InvalidOperationException) { refused = true; }
                Check(refused && !f.Resolved && f.Resolves == 0 && !f.LockHeld);
            });
            test("unverified note resolution stays pending for retry", delegate {
                var f = new Fixture { IgnoreResolve = true }; string root = Root(), id = f.Service().ReadQueueBook(1).Single().StableId;
                using (var q = Queue(f, root)) { Field<TextBox>(q, "comment").Text = "Reject"; Call(q, "Decide", "Rejected"); Check(!f.Resolved && f.Resolves == 1); q.Close(); }
                using (var p = new ReviewProgress(root, f.ProjectId)) Check(p.Get(id).ResolutionPending);
                f.IgnoreResolve = false;
                using (var q = Queue(f, root)) { Call(q, "FinishResolution"); Check(f.Resolved && f.Writes == 0); q.Close(); }
            });
            test("old completed decisions are not automatically resolved on upgrade", delegate {
                var f = new Fixture(); var c = f.Service().ReadQueueBook(1).Single(); string root = Root();
                using (var p = new ReviewProgress(root, f.ProjectId)) p.Save(new ReviewRecord { Id = c.StableId, Outcome = "Rejected", Comment = "old decision" });
                using (var q = Queue(f, root)) { Check(Field<NoteCandidate>(q, "current") == null && !f.Resolved && f.Resolves == 0); q.Close(); }
            });
            test("pending resolution with a missing source remains visible as recovery work", delegate {
                var f = new Fixture(); var c = f.Service().ReadQueueBook(1).Single(); string root = Root();
                using (var p = new ReviewProgress(root, f.ProjectId)) p.Save(new ReviewRecord { Id = c.StableId, Outcome = "Rejected", Resolution = "Pending", Discussion = c.DiscussionKey });
                f.Notes.Clear();
                using (var q = Queue(f, root)) { Check(Field<Label>(q, "heading").Text.Contains("missing/changed")); q.Close(); }
                using (var p = new ReviewProgress(root, f.ProjectId)) Check(p.Outstanding().Single().Id == c.StableId);
            });
            test("queue identity survives discussion replies", delegate {
                var f = new Fixture(); var s = f.Service(); var first = s.ReadQueueBook(1).Single();
                f.Replies.Add(Fixture.Comment("Human discussion")); var next = s.ReadQueueBook(1).Single();
                Check(first.StableId == next.StableId && first.Key != next.Key);
            });
            test("queue target remains pinned while browsing references", delegate {
                var f = new Fixture(); var s = f.Service(); var n = s.ReadQueueBook(1).Single();
                f.ActiveVerse = 8;
                s.ForQueueItem(n).Apply(n, n.Proposal.Original, n.Proposal.Suggested, n.Proposal.Region, true);
                Check(f.Writes == 1);
            });
            test("queue source changed under lock is refused", delegate {
                var f = new Fixture(); var s = f.Service(); var n = s.ReadQueueBook(1).Single(); f.Replies.Add(Fixture.Comment("Do not use the old proposal"));
                bool refused = false; try { s.ForQueueItem(n).Apply(n, n.Proposal.Original, n.Proposal.Suggested, n.Proposal.Region, true); } catch (InvalidOperationException) { refused = true; }
                Check(refused && f.Writes == 0);
            });
            test("queue indexing and reference browsing never write", delegate {
                var f = new Fixture(); using (var q = Queue(f, Root())) { Check(f.Navigations == 1 && f.Writes == 0); f.ActiveVerse = 8; Check(f.Writes == 0); q.Close(); }
            });
            test("decision advances to next note and later retains its draft", delegate {
                var f = new Fixture(); var anchor = f.Anchor; bool secondResolved = false;
                f.Notes.Add(Stub<Paratext.PluginInterfaces.IProjectNote>.Create((m, a) => {
                    switch (m.Name) {
                        case "get_Anchor": return anchor;
                        case "get_IsResolved": return secondResolved;
                        case "Resolve": secondResolved = true; return null;
                        case "get_Comments": return new[] { Fixture.Comment(Fixture.Qa("Grammar", "பழைய", "வேறு", "Yes")) };
                        default: throw new NotSupportedException(m.Name);
                    }
                }));
                string root = Root();
                using (var q = Queue(f, root))
                {
                    string first = Field<NoteCandidate>(q, "current").StableId;
                    Field<TextBox>(q, "comment").Text = "Investigating"; Call(q, "SaveDraft"); Call(q, "Next");
                    Check(Field<NoteCandidate>(q, "current").StableId != first && f.Navigations == 2 && f.Writes == 0);
                    Field<TextBox>(q, "comment").Text = "Reject second"; Call(q, "Decide", "Rejected"); q.Close();
                }
                using (var q = Queue(f, root)) { Check(Field<TextBox>(q, "comment").Text == "Investigating"); q.Close(); }
            });
            test("duplicate source identities cannot skip either note", delegate {
                var f = new Fixture(); f.Notes.Add(f.Notes[0]);
                using (var q = Queue(f, Root()))
                {
                    Field<TextBox>(q, "comment").Text = "review"; Field<CheckBox>(q, "reviewed").Checked = true;
                    Call(q, "Decide", "Applied"); Call(q, "Decide", "Rejected"); Call(q, "Decide", "Completed manually");
                    Check(f.Writes == 0 && Field<NoteCandidate>(q, "current") != null); q.Close();
                }
            });
            test("apply advances only after explicit review and persists decision", delegate {
                var f = new Fixture(); string root = Root(), id = f.Service().ReadQueueBook(1).Single().StableId;
                using (var q = Queue(f, root))
                {
                    Call(q, "Decide", "Applied"); Check(f.Writes == 0);
                    Field<CheckBox>(q, "reviewed").Checked = true; Call(q, "Decide", "Applied"); Check(f.Writes == 1 || FailInfo(q)); q.Close();
                }
                using (var p = new ReviewProgress(root, f.ProjectId)) Check(p.Get(id).Outcome == "Applied");
            });
            test("reject requires comment and writes no Scripture", delegate {
                var f = new Fixture(); string root = Root(), id = f.Service().ReadQueueBook(1).Single().StableId;
                using (var q = Queue(f, root))
                {
                    Call(q, "Decide", "Rejected"); Check(Field<NoteCandidate>(q, "current") != null);
                    Field<TextBox>(q, "comment").Text = "Source supports current reading"; Call(q, "Decide", "Rejected"); Check(f.Writes == 0 && Field<NoteCandidate>(q, "current") == null); q.Close();
                }
                using (var p = new ReviewProgress(root, f.ProjectId)) Check(p.Get(id).Outcome == "Rejected" && p.Get(id).Comment.Contains("Source"));
            });
            test("pause prevents decisions and resume retains draft", delegate {
                var f = new Fixture(); string root = Root();
                using (var q = Queue(f, root))
                {
                    Field<TextBox>(q, "comment").Text = "Checking reference";
                    typeof(QueueForm).GetField("paused", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(q, true);
                    Call(q, "Decide", "Rejected"); Check(f.Writes == 0 && Field<NoteCandidate>(q, "current") != null); Call(q, "SaveDraft"); q.Close();
                }
                using (var q = Queue(f, root)) { Check(Field<TextBox>(q, "comment").Text == "Checking reference" && !Field<CheckBox>(q, "reviewed").Checked); q.Close(); }
            });
            test("interrupted apply stays pending and cannot blindly repeat", delegate {
                var f = new Fixture { IgnorePut = true }; string root = Root(), id = f.Service().ReadQueueBook(1).Single().StableId;
                using (var q = Queue(f, root)) { Field<CheckBox>(q, "reviewed").Checked = true; Call(q, "Decide", "Applied"); Check(f.Writes == 1 || FailInfo(q)); q.Close(); }
                using (var p = new ReviewProgress(root, f.ProjectId)) Check(p.Get(id).Outcome == "Pending apply");
                using (var q = Queue(f, root)) { Field<CheckBox>(q, "reviewed").Checked = true; Call(q, "Decide", "Applied"); Call(q, "Decide", "Rejected"); Check(f.Writes == 1 && Field<NoteCandidate>(q, "current") != null); q.Close(); }
            });
            test("pre-write refusal allows rejection without uncertain-write reconciliation", delegate {
                var f = new Fixture { Editable = false }; string root = Root(), id = f.Service().ReadQueueBook(1).Single().StableId;
                using (var q = Queue(f, root))
                {
                    Field<CheckBox>(q, "reviewed").Checked = true; Call(q, "Decide", "Applied");
                    Check(f.Writes == 0 && !Field<Button>(q, "retry").Visible);
                    q.Close();
                }
                using (var p = new ReviewProgress(root, f.ProjectId)) Check(p.Get(id).Outcome != "Pending apply");
            });
            test("progress failure immediately before submission prevents USFM write", delegate {
                var f = new Fixture(); var s = f.Service(); var n = s.ReadQueueBook(1).Single(); bool refused = false;
                try { s.ForQueueItem(n).Apply(n, n.Proposal.Original, n.Proposal.Suggested, n.Proposal.Region, true, false, delegate { throw new IOException("Disk unavailable"); }); }
                catch (IOException) { refused = true; }
                Check(refused && f.Writes == 0 && !f.LockHeld);
            });
            test("progress excludes concurrent queue and preserves Unicode after reopen", delegate {
                string root = Root();
                using (var p = new ReviewProgress(root, "test"))
                {
                    bool refused = false; try { using (var duplicate = new ReviewProgress(root, "test")) { } } catch (InvalidOperationException) { refused = true; } Check(refused);
                    p.Save(new ReviewRecord { Id = "one", Outcome = "Rejected", Comment = "தமிழ், \"குறிப்பு\"\r\nreason" });
                    p.SaveCursor(new ReviewRecord { Id = "one", Outcome = "Draft", Original = "பழைய" });
                }
                using (var p = new ReviewProgress(root, "test"))
                {
                    Check(p.Get("one").Comment == "தமிழ், \"குறிப்பு\"\r\nreason" && p.Cursor().Original == "பழைய");
                    string path = Path.Combine(root, "export.csv"); p.Export(path); Check(File.ReadAllText(path).Contains("\"\"குறிப்பு\"\""));
                }
            });
            test("16k decision lookups and atomic updates retain all outcomes", delegate {
                string root = Root();
                using (var p = new ReviewProgress(root, "large"))
                {
                    // One atomic file per actual decision; indexing pending notes creates no decision files.
                    for (int i = 0; i < 16000; i++) Check(p.Get("pending-" + i) == null);
                    for (int i = 0; i < 20; i++) p.Save(new ReviewRecord { Id = "decided-" + i, Outcome = "Rejected", Comment = "test" });
                    p.Save(new ReviewRecord { Id = "decided-0", Outcome = "Completed manually", Comment = "reviewed" });
                }
                using (var p = new ReviewProgress(root, "large")) { Check(p.Get("decided-0").Outcome == "Completed manually"); for (int i = 1; i < 20; i++) Check(p.Get("decided-" + i).Outcome == "Rejected"); }
            });
        }
    }
}
