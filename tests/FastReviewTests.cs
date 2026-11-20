using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Paratext.PluginInterfaces;
using ParatextNoteSuggestions;

namespace NoteSuggestionsTests
{
    public static class FastReviewTests
    {
        internal static void Check(bool yes, string what = "Fast review assertion failed") { if (!yes) throw new Exception(what); }
        internal static string Root() { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fr", Guid.NewGuid().ToString("N").Substring(0, 8)); }
        internal static T Field<T>(object o, string name) { return (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(o); }
        internal static object Call(object o, string name, params object[] args)
        { try { return o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(o, args); } catch (TargetInvocationException ex) { throw ex.InnerException; } }
        internal static bool Key(QueueForm q, Keys key, bool repeat = false) { return (bool)Call(q, "FastKey", key, repeat); }
        internal static FastSettings Enable(QueueForm q, NoLiteralMode mode = NoLiteralMode.StopForMe, bool hold = true, bool letters = false)
        {
            var settings = new FastSettings { Enabled = true, NoLiteral = mode, HoldLastReply = hold, LetterKeys = letters, References = new System.Collections.Generic.List<string>() };
            Call(q, "ApplyFast", settings, true); return settings;
        }
        internal static NoteCandidate Current(QueueForm q) { return Field<NoteCandidate>(q, "current"); }
        // Buttons only click on a shown form; keep it off screen.
        internal static void Show(Form q) { q.StartPosition = FormStartPosition.Manual; q.Location = new System.Drawing.Point(-3000, -3000); q.ShowInTaskbar = false; q.Show(); Application.DoEvents(); }
        internal static bool Audited(QueueForm q, NoteCandidate item, string outcome)
        { return Field<ReviewSession>(q, "session").Records().Any(r => r.Id == item.StableId && r.Outcome == outcome); }
        internal static ReviewRecord Saved(QueueForm q, NoteCandidate item) { return Field<ReviewProgress>(q, "progress").Get(item.StableId); }
        // Two literal Punctuation notes: GEN 1:3 in this fixture and MAT 5:6 from another one.
        internal static Fixture TwoBooks(string genesisNote = null)
        {
            var f = new Fixture(); if (genesisNote != null) f.NoteText = genesisNote;
            var matthew = new Fixture(); matthew.Ref = Fixture.MakeRef(5, 6, 40);
            f.AvailableBooks.Add(Fixture.Book(40, "MAT")); f.Notes.Add(matthew.Notes[0]); return f;
        }
        public static void Run(Action<string, Action> test)
        {
            test("fast apply key writes the displayed correction and advances without the review checkbox, holding the reply", delegate {
                var f = TwoBooks(); using (var q = QueueTests.Queue(f, Root())) {
                    Enable(q); var first = Current(q); Check(first.Book == 1 && Field<FastChoice>(q, "fastChoice") != null);
                    var shown = Field<FastChoice>(q, "fastChoice").Preview;
                    Check(Key(q, Keys.F5));
                    Check(f.Writes == 1 && !Field<CheckBox>(q, "reviewed").Checked, "one write without the checkbox");
                    var record = Saved(q, first);
                    Check(record.Outcome == "Applied" && record.ResolutionPending && record.BeforeContext == shown.Before && record.AfterContext == shown.After, "record matches the displayed preview");
                    Check(f.Replies.Count == 0 && !f.Resolved && Current(q).Book == 40, "reply held, next note shown");
                    Check(Key(q, Keys.F9)); // skipping the next note posts the held reply
                    Check(f.Replies.Count == 1 && f.Resolved && Saved(q, first).Resolution == "Resolved", "held reply posted once");
                    Check(f.Replies[0].Contents.Any(p => p.Spans.Any(s => s.Text == "Comment: Accepted as suggested.")));
                }
            });
            test("fast apply is refused when the passage changed after it was shown; nothing is written and the note stays", delegate {
                var f = TwoBooks(); using (var q = QueueTests.Queue(f, Root())) {
                    Enable(q); var first = Current(q);
                    f.Tokens[1] = Fixture.Text("முன் பழைய இடை பின்", 1, 3, true, false);
                    Key(q, Keys.F5);
                    Check(f.Writes == 0 && Current(q).StableId == first.StableId && !Field<bool>(q, "paused"), "no write, same note, not paused");
                    Check(Field<Label>(q, "fastReason").Text.Contains("changed after it was shown"));
                    Check(Saved(q, first) == null || !Saved(q, first).Completed);
                    Key(q, Keys.F5); // the refreshed preview is what the next press approves
                    Check(f.Writes == 1 && Saved(q, first).AfterContext.Contains("முன் புதிய இடை பின்"));
                }
            });
            test("fast review arms nothing for English instructions or ellipsis wording", delegate {
                foreach (string suggestion in new[] { "consider a change", "புதிய...", "புதிய … வேறு (reviewer to decide)" }) {
                    var f = TwoBooks(Fixture.Qa("Grammar", "பழைய", suggestion, "No")); using (var q = QueueTests.Queue(f, Root())) {
                        Enable(q); Check(Field<FastChoice>(q, "fastChoice") == null);
                        Key(q, Keys.F5); Check(f.Writes == 0 && Current(q).Book == 1);
                    }
                }
                var g = TwoBooks(); using (var q = QueueTests.Queue(g, Root())) {
                    Enable(q); Field<TextBox>(q, "replacement").Text = "புதிய…"; Call(q, "RefreshFast");
                    Check(Field<FastChoice>(q, "fastChoice") == null && Field<Label>(q, "fastReason").Text.Length > 0);
                    Key(q, Keys.F5); Check(g.Writes == 0);
                }
            });
            test("fast keys do nothing while paused or decided, and a held key repeat never decides a second note", delegate {
                var f = TwoBooks(); using (var q = QueueTests.Queue(f, Root())) {
                    Show(q); Enable(q);
                    Field<Button>(q, "pause").PerformClick(); Key(q, Keys.F5); Check(f.Writes == 0);
                    Field<Button>(q, "pause").PerformClick();
                    Check(Key(q, Keys.F5, true) && f.Writes == 0, "repeat swallowed");
                    Key(q, Keys.F5); Check(f.Writes == 1);
                    Key(q, Keys.PageUp); Check(Current(q).Book == 1); Key(q, Keys.F5); Key(q, Keys.F6);
                    Check(f.Writes == 1 && Saved(q, Current(q)).Outcome == "Applied", "decided note cannot be decided again");
                    q.Close();
                }
            });
            test("letters typed into the comment box stay there while function keys still act", delegate {
                var f = TwoBooks(); using (var q = QueueTests.Queue(f, Root())) {
                    Show(q); Enable(q, NoLiteralMode.StopForMe, true, true); Application.DoEvents();
                    var first = Current(q); var box = Field<TextBox>(q, "fastComment"); q.ActiveControl = box;
                    Check(!Key(q, Keys.A) && !Key(q, Keys.PageDown) && f.Writes == 0 && Current(q).Book == 1, "letters and caret keys left to the box");
                    box.Text = "Checked against BSI"; Key(q, Keys.F6);
                    Check(Saved(q, first).Outcome == "Rejected" && Saved(q, first).Comment == "Checked against BSI" && f.Writes == 0);
                    q.Close();
                }
            });
            test("reject and fixed-by-hand comments follow the category and Alt+number overrides them", delegate {
                foreach (var pair in new[] { Tuple.Create("Grammar", 2), Tuple.Create("Source comparison", 3) }) {
                    var f = TwoBooks(Fixture.Qa(pair.Item1, "பழைய", "புதிய", "Yes")); using (var q = QueueTests.Queue(f, Root())) {
                        Enable(q); var first = Current(q); Key(q, Keys.F6);
                        Check(Saved(q, first).Comment == CommentPresets.Values[pair.Item2] && Saved(q, first).Outcome == "Rejected", pair.Item1);
                    }
                }
                var g = TwoBooks(); using (var q = QueueTests.Queue(g, Root())) {
                    Enable(q); var first = Current(q);
                    Key(q, Keys.Alt | Keys.D5); Key(q, Keys.F6);
                    Check(Saved(q, first).Comment == CommentPresets.Values[4]);
                }
            });
            test("fixed-by-hand with no saved change asks for a second press and records no change", delegate {
                var f = TwoBooks(); using (var q = QueueTests.Queue(f, Root())) {
                    Enable(q); var first = Current(q);
                    Key(q, Keys.F7); Check(Saved(q, first) == null || !Saved(q, first).Completed, "first press only warns");
                    Check(Field<Label>(q, "fastReason").Text.Contains("No saved change"));
                    Key(q, Keys.F7);
                    Check(Saved(q, first).Outcome == "Completed manually" && Saved(q, first).Comment == CommentPresets.Values[4] && f.Writes == 0);
                }
                var g = TwoBooks(); using (var q = QueueTests.Queue(g, Root())) {
                    Enable(q); var first = Current(q);
                    g.Tokens[1] = Fixture.Text("முன் சரிசெய்த பின்", 1, 3, true, false); // the reviewer's own edit in Paratext
                    Key(q, Keys.F7);
                    Check(Saved(q, first).Outcome == "Completed manually" && Saved(q, first).Comment == CommentPresets.Values[1] && Saved(q, first).AfterContext.Contains("சரிசெய்த"));
                }
            });
            test("fast navigation moves a grouped editor through its scroll group without activating it", delegate {
                var f = TwoBooks(); f.TrackWindow = true; f.EditorSyncGroup = SyncReferenceGroup.A;
                using (var q = QueueTests.Queue(f, Root())) {
                    Show(q); Enable(q); for (int i = 0; i < 60 && Field<object>(q, "navigationPhase").ToString() != "Idle"; i++) Call(q, "CheckNavigation");
                    int windowCalls = f.Navigations, syncCalls = f.SyncCalls;
                    Key(q, Keys.PageDown);
                    for (int i = 0; i < 60 && Field<object>(q, "navigationPhase").ToString() != "Idle"; i++) Call(q, "CheckNavigation");
                    Check(Current(q).Book == 40 && f.Navigations == windowCalls && f.SyncCalls > syncCalls && f.WindowReference.BookNum == 40, "no activating window call");
                    q.Close();
                }
            });
            test("later round leaves notes without ready-to-paste Tamil open and untouched, but never hides recovery work", delegate {
                var f = TwoBooks(Fixture.Qa("Source comparison", "பழைய", "Keep, or add a footnote noting that some copies lack புதிய", "Yes")); string root = Root();
                using (var q = QueueTests.Queue(f, root)) {
                    Enable(q, NoLiteralMode.LaterRound); Call(q, "ChangeScope", new QueueScope()); while (Field<bool>(q, "indexing")) Call(q, "IndexBook");
                    Check(Current(q).Book == 40 && Field<Label>(q, "heading").Text.Contains("1 left for a later round"), "Genesis note left for later");
                    Check(f.Writes == 0 && f.Replies.Count == 0 && !f.Resolved);
                    var genesis = f.Service().ReadQueueBook(1, true)[0];
                    Field<ReviewProgress>(q, "progress").Save(new ReviewRecord { Id = genesis.StableId, Outcome = "Pending apply", Reference = genesis.DisplayReference });
                    typeof(QueueForm).GetField("position", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(q, -1); Call(q, "Next");
                    Check(Current(q).Book == 1, "interrupted apply is not skipped");
                }
            });
            test("normal mode is unchanged: no fast keys, review checkbox and typed reject comment still required", delegate {
                var f = TwoBooks(); using (var q = QueueTests.Queue(f, Root())) {
                    Show(q);
                    Check(!Key(q, Keys.F5) && !Key(q, Keys.F6) && f.Writes == 0, "keys ignored");
                    Check(Saved(q, Current(q)) == null || !Saved(q, Current(q)).Completed, "nothing decided: " + (Saved(q, Current(q)) == null ? "no record" : Saved(q, Current(q)).Outcome));
                    Check(!Field<Button>(q, "apply").Enabled && !Field<Button>(q, "reject").Enabled, "buttons need the checkbox and a comment");
                    Check(Field<TabControl>(q, "tabs").SelectedTab == Field<TabPage>(q, "reviewTab"), "Review tab stays first");
                    q.Close();
                }
            });
            test("undo before the reply is posted restores Scripture, marks the decision Undone and reopens the note", delegate {
                var f = TwoBooks(); using (var q = QueueTests.Queue(f, Root())) {
                    Enable(q); var first = Current(q); string before = f.Serialize(f.Tokens);
                    Key(q, Keys.F5); Check(f.Writes == 1 && Current(q).Book == 40);
                    Key(q, Keys.F11);
                    Check(f.Writes == 2 && f.Serialize(f.Tokens) == before, "Scripture restored");
                    Check(Audited(q, first, "Undone") && !Audited(q, first, "Applied") && !Saved(q, first).Completed && Current(q).StableId == first.StableId, "note reopened");
                    Check(f.Replies.Count == 0 && !f.Resolved && Field<NoteCandidate>(q, "held") == null);
                    Key(q, Keys.F5); Check(f.Writes == 3 && Saved(q, first).Outcome == "Applied", "the note can be decided again");
                }
            });
            test("undo refuses when the passage changed after the decision and changes nothing", delegate {
                var f = TwoBooks(); using (var q = QueueTests.Queue(f, Root())) {
                    Enable(q); var first = Current(q); Key(q, Keys.F5);
                    f.Tokens[1] = Fixture.Text("மற்றவர் மாற்றம்", 1, 3, true, false);
                    Key(q, Keys.F11);
                    Check(f.Writes == 1 && ((IUSFMTextToken)f.Tokens[1]).Text == "மற்றவர் மாற்றம்" && Saved(q, first).Outcome == "Applied", "nothing reverted");
                    Check(Field<Label>(q, "status").Text.Contains("changed after this decision"));
                }
            });
            test("undo never reverts a different plugin edit made after the decision", delegate {
                var f = TwoBooks(); f.RealisticUsfm = true;
                using (var q = QueueTests.Queue(f, Root())) {
                    Enable(q); var first = Current(q); Key(q, Keys.F5); Check(f.Writes == 1);
                    // Another plugin write in the same chapter, e.g. from the editor's context menu.
                    var s = f.Service(); var n = s.ReadCandidates()[0]; s.InsertFootnote(n, s.PreviewFootnote(n, "சோதனை", false));
                    string after = f.Serialize(f.Tokens); Check(f.Writes == 2);
                    Key(q, Keys.F11);
                    Check(f.Writes == 2 && f.Serialize(f.Tokens) == after && Field<Label>(q, "status").Text.Contains("not this decision"), "nothing reverted: " + Field<Label>(q, "status").Text);
                    Check(Saved(q, first).Outcome == "Applied" && Saved(q, first).ResolutionPending, "decision kept for its reply");
                }
            });
            test("a held reply is not posted once its applied text was reverted outside fast review", delegate {
                var f = TwoBooks(); using (var q = QueueTests.Queue(f, Root())) {
                    Enable(q); var first = Current(q); string before = f.Serialize(f.Tokens);
                    Key(q, Keys.F5); f.Service().Revert(); Check(f.Serialize(f.Tokens) == before && f.Writes == 2);
                    Key(q, Keys.F9);
                    Check(f.Replies.Count == 0 && !f.Resolved && Audited(q, first, "Undone") && !Saved(q, first).Completed, "no Applied reply for reverted text");
                    Check(String.Join("\n", Field<System.Collections.Generic.List<string>>(q, "fastLog")).Contains("Reply not posted"));
                }
            });
            test("a choice worked out for another note is never applied to the current one", delegate {
                var f = TwoBooks(); using (var q = QueueTests.Queue(f, Root())) {
                    Enable(q); var choice = Field<FastChoice>(q, "fastChoice"); choice.Id = new string('0', 64);
                    Call(q, "DecideWith", "Applied", choice);
                    Check(f.Writes == 0 && (Saved(q, Current(q)) == null || !Saved(q, Current(q)).Completed));
                    Call(q, "Next"); Check(Field<FastChoice>(q, "fastChoice") == null || Field<FastChoice>(q, "fastChoice").Id == Current(q).StableId, "choice cleared or rebuilt on load");
                }
            });
            test("undo waits while the queue is paused", delegate {
                var f = TwoBooks(); using (var q = QueueTests.Queue(f, Root())) {
                    Show(q); Enable(q); Key(q, Keys.F5);
                    typeof(QueueForm).GetField("paused", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(q, true);
                    Key(q, Keys.F11); Check(f.Writes == 1 && Field<NoteCandidate>(q, "held") != null && Field<Label>(q, "status").Text.Contains("Resume"));
                    typeof(QueueForm).GetField("paused", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(q, false);
                    Key(q, Keys.F11); Check(f.Writes == 2);
                    q.Close();
                }
            });
            test("undo of a rejection changes no Scripture and reopens the note", delegate {
                var f = TwoBooks(); using (var q = QueueTests.Queue(f, Root())) {
                    Enable(q); var first = Current(q); Key(q, Keys.F6); Key(q, Keys.F11);
                    Check(f.Writes == 0 && Audited(q, first, "Undone") && !Saved(q, first).Completed && Current(q).StableId == first.StableId && f.Replies.Count == 0);
                }
            });
            test("undo is unavailable once the reply is posted, and pause posts a held reply exactly once", delegate {
                var f = TwoBooks(); using (var q = QueueTests.Queue(f, Root())) {
                    Show(q); Enable(q); Key(q, Keys.F5);
                    Field<Button>(q, "pause").PerformClick(); Check(f.Replies.Count == 1 && f.Resolved, "pause posts the held reply");
                    Field<Button>(q, "pause").PerformClick(); Key(q, Keys.F11);
                    Check(f.Writes == 1 && f.Replies.Count == 1 && Field<Label>(q, "status").Text.Contains("Nothing to undo"));
                    q.Close(); Check(f.Replies.Count == 1);
                }
            });
            test("without holding, fast decisions post the reply at once and leave nothing to undo", delegate {
                var f = TwoBooks(); using (var q = QueueTests.Queue(f, Root())) {
                    Enable(q, NoLiteralMode.StopForMe, false); Key(q, Keys.F5);
                    Check(f.Replies.Count == 1 && f.Resolved && Field<NoteCandidate>(q, "held") == null);
                }
            });
            test("a held reply left by a crash appears in Recovery and retry posts one reply without repeating the edit", delegate {
                var f = TwoBooks(); string root = Root(); NoteCandidate first;
                using (var q = QueueTests.Queue(f, root)) { Enable(q); first = Current(q); Key(q, Keys.F5); Check(f.Writes == 1 && f.Replies.Count == 0); }
                using (var q = QueueTests.Queue(f, root)) {
                    Show(q);
                    Check(Field<ReviewProgress>(q, "progress").Outstanding().Any(r => r.Id == first.StableId), "listed for recovery");
                    Call(q, "ChangeScope", new QueueScope { RecoveryOnly = true, Status = NoteStatusFilter.All }); while (Field<bool>(q, "indexing")) Call(q, "IndexBook");
                    Check(Current(q) != null && Current(q).StableId == first.StableId, "recovery shows the held decision: " + (Current(q) == null ? "none" : Current(q).DisplayReference));
                    Check(Field<Button>(q, "resolve").Enabled, "retry resolution offered: " + Field<Label>(q, "status").Text);
                    Call(q, "FinishResolution");
                    Check(f.Writes == 1 && f.Replies.Count == 1 && f.Resolved, "one reply, no second edit: writes " + f.Writes + ", replies " + f.Replies.Count);
                    q.Close();
                }
            });
        }
    }
}
