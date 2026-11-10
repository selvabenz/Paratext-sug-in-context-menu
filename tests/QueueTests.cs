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
        static string Root() { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "qt", Guid.NewGuid().ToString("N").Substring(0, 10)); }
        static T Field<T>(object o, string name) { return (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(o); }
        static void Call(object o, string name, params object[] args)
        { try { o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(o, args); } catch (TargetInvocationException ex) { throw ex.InnerException; } }
        static void Scope(QueueForm q, QueueScope scope)
        { Call(q, "ChangeScope", scope); while (Field<bool>(q, "indexing")) Call(q, "IndexBook"); }
        static QueueScope Books(NoteStatusFilter status, params int[] books)
        { var scope = new QueueScope { AllBooks = false, Status = status }; foreach (int book in books) scope.Books.Add(book); return scope; }
        static Fixture MultiBook()
        {
            var f = new Fixture(); var matthew = new Fixture(); matthew.Ref = Fixture.MakeRef(5, 6, 40);
            f.AvailableBooks.Add(Fixture.Book(40, "MAT")); f.Notes.Add(matthew.Notes[0]); return f;
        }
        public static QueueForm Queue(Fixture f, string root)
        {
            string project = Path.Combine(root, "project"); Directory.CreateDirectory(project);
            new System.Xml.Linq.XElement("ScriptureText", new System.Xml.Linq.XElement("Guid", f.ProjectId)).Save(Path.Combine(project, "Settings.xml"));
            var q = new QueueForm(f.Service(), root, project);
            while (Field<bool>(q, "indexing")) Call(q, "IndexBook"); return q;
        }
        static Fixture Sandhi()
        { var f = new Fixture(); f.NoteText = Fixture.Qa("Sandhi / word-joining", "\u0baa\u0bb4\u0bc8\u0baf", "\u0baa\u0bc1\u0ba4\u0bbf\u0baf", "No"); return f; }
        static Fixture Typo()
        { var f = Sandhi(); f.NoteText = f.NoteText.Replace("Sandhi / word-joining", "Confirmed typo"); return f; }
        static System.Collections.Generic.Dictionary<string, PassageEvidence> TypoPreviews(Fixture f, System.Collections.Generic.List<NoteCandidate> items)
        { return items.ToDictionary(c => c.StableId, c => f.Service().ForQueueItem(c).PreviewBulkConfirmedTypo(c)); }
        static System.Collections.Generic.IEnumerable<Control> Descendants(Control root)
        { foreach (Control c in root.Controls) { yield return c; foreach (var child in Descendants(c)) yield return child; } }
        static System.Collections.Generic.Dictionary<string, PassageEvidence> SandhiPreviews(Fixture f, System.Collections.Generic.List<NoteCandidate> items)
        { return items.ToDictionary(c => c.StableId, c => f.Service().ForQueueItem(c).PreviewBulkSandhi(c)); }
        public static void Run(Action<string, Action> test)
        {
            test("confirmed typo exact reviewed batch persists evidence and never replays", delegate {
                var f = Typo(); using (var q = Queue(f, Root())) {
                    var selected = Field<System.Collections.Generic.List<NoteCandidate>>(q, "items").ToList(); var previews = TypoPreviews(f, selected);
                    Check(f.Writes == 0);
                    Call(q, "RunBulkConfirmedTypos", selected, "Checked Tamil spelling in context", previews);
                    var record = Field<ReviewProgress>(q, "progress").Get(selected[0].StableId);
                    Check(f.Writes == 1 && f.Resolved && f.Replies.Count == 1 && record.Category == "Confirmed typo" && record.BeforeContext == previews[selected[0].StableId].Before && record.AfterContext == previews[selected[0].StableId].After);
                    Call(q, "RunBulkConfirmedTypos", selected, "Check again", previews); Check(f.Writes == 1); q.Close();
                }
            });
            test("confirmed typo second correction in the same verse needs a fresh preview", delegate {
                var f = Typo(); var second = Typo(); second.LockHeld = second.NotesLockHeld = true;
                var p = f.Service().ReadCandidates()[0].Proposal;
                second.AnchorText = p.Suggested; second.NoteText = Fixture.Qa("Confirmed typo", p.Suggested, p.Suggested + p.Suggested, "No");
                f.Notes.Add(second.Notes[0]); f.Tokens[1] = Fixture.Text(p.Original + " " + p.Suggested, 1, 3, true, false);
                using (var q = Queue(f, Root())) {
                    var selected = Field<System.Collections.Generic.List<NoteCandidate>>(q, "items").OrderBy(c => c.Proposal.Original == p.Original ? 0 : 1).ToList();
                    Call(q, "RunBulkConfirmedTypos", selected, "Reviewed both", TypoPreviews(f, selected));
                    Check(f.Writes == 1 && f.Resolved && !second.Resolved && Field<Label>(q, "status").Text.Contains("1 skipped")); q.Close();
                }
            });
            test("confirmed typo uncertain save stops later selections without attempting them", delegate {
                var f = Typo(); f.IgnorePut = true; var second = Typo(); second.Ref = Fixture.MakeRef(1, 4);
                second.LockHeld = second.NotesLockHeld = true; f.Notes.Add(second.Notes[0]);
                f.Tokens.Add(Fixture.Marker("v", "4", 1, 4, Paratext.PluginInterfaces.MarkerType.Verse));
                f.Tokens.Add(Fixture.Text(f.AnchorText, 1, 4, true, false));
                using (var q = Queue(f, Root())) {
                    var selected = Field<System.Collections.Generic.List<NoteCandidate>>(q, "items").ToList();
                    Call(q, "RunBulkConfirmedTypos", selected, "Reviewed both", TypoPreviews(f, selected));
                    Check(f.Writes == 1 && !f.Resolved && !second.Resolved && Field<Label>(q, "status").Text.Contains("1 not attempted")); q.Close();
                }
            });
            test("confirmed typo refuses stale preview, source changes and lost permission including under lock", delegate {
                foreach (string change in new[] { "text", "source", "lockText", "permission", "lockPermission", "lockUnavailable" }) {
                    var f = Typo(); using (var q = Queue(f, Root())) {
                        var selected = Field<System.Collections.Generic.List<NoteCandidate>>(q, "items").ToList(); var previews = TypoPreviews(f, selected);
                        if (change == "text") f.Tokens[1] = Fixture.Text("X " + selected[0].Proposal.Original, 1, 3, true, false);
                        if (change == "source") f.NoteText += " Changed source";
                        if (change == "lockText") f.OnHistory = delegate { f.Tokens[1] = Fixture.Text("X " + selected[0].Proposal.Original, 1, 3, true, false); };
                        if (change == "permission") f.Editable = false;
                        if (change == "lockPermission") f.OnHistory = delegate { f.Editable = false; };
                        if (change == "lockUnavailable") f.LockAvailable = false;
                        Call(q, "RunBulkConfirmedTypos", selected, "Reviewed", previews);
                        Check(f.Writes == 0 && !f.Resolved && f.Replies.Count == 0); q.Close();
                    }
                }
            });
            test("confirmed typo uncertain save persists pending intent and blocks replay", delegate {
                var f = Typo(); f.IgnorePut = true; using (var q = Queue(f, Root())) {
                    var selected = Field<System.Collections.Generic.List<NoteCandidate>>(q, "items").ToList(); var previews = TypoPreviews(f, selected);
                    Call(q, "RunBulkConfirmedTypos", selected, "Reviewed", previews);
                    Check(f.Writes == 1 && !f.Resolved && Field<ReviewProgress>(q, "progress").Get(selected[0].StableId).Outcome == "Pending apply");
                    Call(q, "RunBulkConfirmedTypos", selected, "Reviewed again", previews); Check(f.Writes == 1); q.Close();
                }
            });
            test("confirmed typo reply recovery never repeats Scripture correction", delegate {
                var f = Typo(); f.IgnoreComment = true; using (var q = Queue(f, Root())) {
                    var selected = Field<System.Collections.Generic.List<NoteCandidate>>(q, "items").ToList();
                    Call(q, "RunBulkConfirmedTypos", selected, "Reviewed", TypoPreviews(f, selected));
                    Check(f.Writes == 1 && !f.Resolved && Field<ReviewProgress>(q, "progress").Get(selected[0].StableId).Outcome == "Applied");
                    f.IgnoreComment = false; Call(q, "FinishRecord", selected[0]); Check(f.Writes == 1 && f.Resolved && f.Replies.Count == 1); q.Close();
                }
            });
            test("confirmed typo rejects possible typos, reviewer flags, nonliteral strings and partial words", delegate {
                foreach (string kind in new[] { "possible", "review", "punctuation", "instruction", "partial", "ambiguous", "heading", "empty", "marker", "formatting" }) {
                    var f = Typo(); var p = f.Service().ReadCandidates()[0].Proposal;
                    if (kind == "possible") f.NoteText = f.NoteText.Replace("Confirmed typo", "Possible typo");
                    if (kind == "review") f.NoteText = Fixture.Qa("Confirmed typo", p.Original, p.Suggested, "Yes");
                    if (kind == "punctuation") f.NoteText = Fixture.Qa("Confirmed typo", p.Original, p.Suggested + "!", "No");
                    if (kind == "instruction") f.NoteText = Fixture.Qa("Confirmed typo", p.Original, "replace " + p.Suggested, "No");
                    if (kind == "partial") f.Tokens[1] = Fixture.Text("X" + p.Original, 1, 3, true, false);
                    if (kind == "ambiguous") f.Tokens[1] = Fixture.Text(p.Original + " " + p.Original, 1, 3, true, false);
                    if (kind == "heading") f.Tokens[1] = Fixture.Text(p.Original, 1, 3, false, false);
                    if (kind == "empty") f.NoteText = Fixture.Qa("Confirmed typo", p.Original, "", "No");
                    if (kind == "marker") f.NoteText = Fixture.Qa("Confirmed typo", p.Original, "\\p " + p.Suggested, "No");
                    if (kind == "formatting") {
                        f.Tokens[1] = Fixture.Text("X", 1, 3, true, false);
                        f.Tokens.Add(Fixture.Marker("add", "", 1, 3, Paratext.PluginInterfaces.MarkerType.Character));
                        f.Tokens.Add(Fixture.Text(p.Original, 1, 3, true, false));
                    }
                    bool refused = false; var candidate = f.Service().ReadQueueBook(1).Single();
                    try { f.Service().ForQueueItem(candidate).PreviewBulkConfirmedTypo(candidate); } catch (InvalidOperationException) { refused = true; }
                    Check(refused && f.Writes == 0 && !f.Resolved);
                }
            });
            test("confirmed typo modeless preview navigates without writes and cancel releases queue", delegate {
                var f = Typo(); using (var q = Queue(f, Root())) {
                    q.Show(); Application.DoEvents(); Call(q, "BulkConfirmedTypos"); Application.DoEvents();
                    var dialog = q.OwnedForms.Single(d => d.Text.Contains("confirmed typos"));
                    var list = Field<CheckedListBox>(dialog, "list"); Check(Field<bool>(q, "busy") && list.CheckedItems.Count == 0 && !dialog.Modal);
                    list.SelectedIndex = 0;
                    Descendants(dialog).OfType<Button>().Single(b => b.Name == "openInEditor").PerformClick();
                    Check(f.Navigations > 0 && f.LastNavigationProject == f.Project && f.Writes == 0 && f.Replies.Count == 0 && !f.Resolved);
                    using (var bitmap = new System.Drawing.Bitmap(dialog.Width, dialog.Height)) {
                        dialog.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, dialog.Width, dialog.Height));
                        bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "confirmed-typo-preview.png"));
                    }
                    Descendants(dialog).OfType<Button>().Single(b => b.Text == "Cancel").PerformClick(); Application.DoEvents();
                    Check(!Field<bool>(q, "busy") && dialog.IsDisposed && f.Writes == 0 && f.Replies.Count == 0); q.Close();
                }
            });
            test("confirmed typo preview approval requires selection acknowledgement and comment", delegate {
                var f = Typo(); using (var q = Queue(f, Root())) {
                    q.Show(); Application.DoEvents(); Call(q, "BulkConfirmedTypos"); Application.DoEvents();
                    var dialog = q.OwnedForms.Single(d => d.Text.Contains("confirmed typos"));
                    var list = Field<CheckedListBox>(dialog, "list");
                    var confirm = Descendants(dialog).OfType<Button>().Single(b => b.Text.Contains("resolve")); Check(!confirm.Enabled);
                    list.SetItemChecked(0, true); Application.DoEvents(); Check(!confirm.Enabled);
                    Field<CheckBox>(dialog, "acknowledged").Checked = true; Check(!confirm.Enabled);
                    Field<TextBox>(dialog, "comment").Text = "Reviewed spelling"; Check(confirm.Enabled);
                    confirm.PerformClick(); Application.DoEvents(); Check(f.Writes == 1 && f.Resolved && !Field<bool>(q, "busy")); q.Close();
                }
            });
            test("bulk Sandhi skips ambiguous first target and resolves the next with accurate totals", delegate {
                var f = Sandhi(); var second = Sandhi(); second.Ref = Fixture.MakeRef(1, 4);
                // Both note fixtures belong to the project's lock held by the first fixture.
                second.LockHeld = second.NotesLockHeld = true;
                f.Notes.Add(second.Notes[0]);
                string original = f.Service().ReadCandidates()[0].Proposal.Original;
                f.Tokens.Add(Fixture.Marker("v", "4", 1, 4, Paratext.PluginInterfaces.MarkerType.Verse));
                f.Tokens.Add(Fixture.Text(original, 1, 4, true, false));
                using (var q = Queue(f, Root())) {
                    var selected = Field<System.Collections.Generic.List<NoteCandidate>>(q, "items").ToList(); var previews = SandhiPreviews(f, selected);
                    f.Tokens[1] = Fixture.Text(original + " " + original, 1, 3, true, false);
                    Call(q, "RunBulkSandhi", selected, "Reviewed both", previews);
                    Check(f.Writes == 1 && !f.Resolved && second.Resolved);
                    string status = Field<Label>(q, "status").Text;
                    Check(status.Contains("1 resolved; 1 not resolved") && status.Contains("1 skipped; 0 not attempted"));
                    Check(Field<ReviewSession>(q, "session").WarningCount >= 2); q.Close();
                }
            });
            test("bulk Sandhi reviewer-decision exclusions finish without a prompt or Scripture edits", delegate {
                var f = Sandhi(); var p = f.Service().ReadCandidates()[0].Proposal;
                f.NoteText = Fixture.Qa("Sandhi / word-joining", p.Original, p.Suggested, "Yes");
                using (var q = Queue(f, Root())) {
                    Call(q, "BulkSandhi");
                    Check(Field<Label>(q, "status").Text.Contains("0 resolved; 1 not resolved (skipped)"));
                    Check(f.Writes == 0 && !f.Resolved && Field<ReviewSession>(q, "session").WarningCount >= 2); q.Close();
                }
            });
            test("bulk Sandhi final totals include preflight skips and leave them unresolved", delegate {
                var f = Sandhi(); using (var q = Queue(f, Root())) {
                    var selected = Field<System.Collections.Generic.List<NoteCandidate>>(q, "items").ToList();
                    Call(q, "RunSandhiBatch", selected, "Reviewed", SandhiPreviews(f, selected), 2);
                    Check(f.Writes == 1 && f.Resolved && Field<Label>(q, "status").Text.Contains("1 resolved; 2 not resolved")); q.Close();
                }
            });
            test("bulk Sandhi continues after a durable applied decision has a note reply failure", delegate {
                var f = Sandhi(); f.IgnoreComment = true; var second = Sandhi(); second.Ref = Fixture.MakeRef(1, 4);
                second.LockHeld = second.NotesLockHeld = true; f.Notes.Add(second.Notes[0]);
                string original = f.Service().ReadCandidates()[0].Proposal.Original;
                f.Tokens.Add(Fixture.Marker("v", "4", 1, 4, Paratext.PluginInterfaces.MarkerType.Verse));
                f.Tokens.Add(Fixture.Text(original, 1, 4, true, false));
                using (var q = Queue(f, Root())) {
                    var selected = Field<System.Collections.Generic.List<NoteCandidate>>(q, "items").ToList();
                    Call(q, "RunBulkSandhi", selected, "Reviewed", SandhiPreviews(f, selected));
                    Check(f.Writes == 2 && !f.Resolved && second.Resolved);
                    Check(Field<Label>(q, "status").Text.Contains("1 resolved; 1 not resolved"));
                    Check(Field<ReviewProgress>(q, "progress").Outstanding().Count() == 1); q.Close();
                }
            });
            test("bulk Sandhi applies exact preview, verifies Scripture and resolves with reply", delegate {
                var f = Sandhi(); using (var q = Queue(f, Root())) {
                    var selected = Field<System.Collections.Generic.List<NoteCandidate>>(q, "items").ToList();
                    var previews = SandhiPreviews(f, selected);
                    Check(f.Writes == 0);
                    Call(q, "RunBulkSandhi", selected, "Reviewed Sandhi", previews);
                    Check(f.Writes == 1 && f.Resolved && f.Replies.Count == 1);
                    var r = Field<ReviewProgress>(q, "progress").Get(selected[0].StableId);
                    Check(r.Outcome == "Applied" && r.Evidence.Contains("Verified saved") && r.BeforeContext == previews[selected[0].StableId].Before && r.AfterContext == previews[selected[0].StableId].After);
                    Call(q, "RunBulkSandhi", selected, "Reviewed again", previews); Check(f.Writes == 1); q.Close();
                }
            });
            test("bulk Sandhi refuses changed passage or changed source after preview", delegate {
                foreach (bool source in new[] { false, true }) {
                    var f = Sandhi(); using (var q = Queue(f, Root())) {
                        var selected = Field<System.Collections.Generic.List<NoteCandidate>>(q, "items").ToList(); var previews = SandhiPreviews(f, selected);
                        if (source) f.NoteText += " Changed discussion";
                        else f.Tokens[1] = Fixture.Text("\u0bae\u0bbe\u0bb1\u0bcd\u0bb1\u0bae\u0bcd \u0baa\u0bb4\u0bc8\u0baf \u0baa\u0bbf\u0ba9\u0bcd", 1, 3, true, false);
                        Call(q, "RunBulkSandhi", selected, "Reviewed", previews); Check(f.Writes == 0 && !f.Resolved && f.Replies.Count == 0); q.Close();
                    }
                }
            });
            test("bulk Sandhi unverified write is held and cannot be replayed", delegate {
                var f = Sandhi(); f.IgnorePut = true;
                using (var q = Queue(f, Root())) {
                    var selected = Field<System.Collections.Generic.List<NoteCandidate>>(q, "items").ToList(); var previews = SandhiPreviews(f, selected);
                    Call(q, "RunBulkSandhi", selected, "Reviewed", previews);
                    Check(f.Writes == 1 && !f.Resolved && Field<ReviewProgress>(q, "progress").Get(selected[0].StableId).Outcome == "Pending apply");
                    Call(q, "RunBulkSandhi", selected, "Reviewed", previews); Check(f.Writes == 1); q.Close();
                }
            });
            test("bulk Sandhi reply recovery never reapplies verified Scripture", delegate {
                var f = Sandhi(); f.IgnoreComment = true;
                using (var q = Queue(f, Root())) {
                    var selected = Field<System.Collections.Generic.List<NoteCandidate>>(q, "items").ToList();
                    Call(q, "RunBulkSandhi", selected, "Reviewed", SandhiPreviews(f, selected));
                    Check(f.Writes == 1 && !f.Resolved && Field<ReviewProgress>(q, "progress").Get(selected[0].StableId).Outcome == "Applied");
                    f.IgnoreComment = false; Call(q, "FinishRecord", selected[0]); Check(f.Writes == 1 && f.Resolved && f.Replies.Count == 1); q.Close();
                }
            });
            test("bulk Sandhi detects passage change between preflight and Scripture lock", delegate {
                var f = Sandhi(); using (var q = Queue(f, Root())) {
                    var selected = Field<System.Collections.Generic.List<NoteCandidate>>(q, "items").ToList(); var previews = SandhiPreviews(f, selected);
                    f.OnHistory = delegate { f.Tokens[1] = Fixture.Text("X " + selected[0].Proposal.Original, 1, 3, true, false); };
                    Call(q, "RunBulkSandhi", selected, "Reviewed", previews);
                    Check(f.Writes == 0 && !f.Resolved && Field<ReviewProgress>(q, "progress").Get(selected[0].StableId).Outcome != "Pending apply"); q.Close();
                }
            });
            test("bulk Sandhi supports exact joining and splitting without normalizing text", delegate {
                foreach (bool join in new[] { true, false }) {
                    var f = Sandhi(); var p = f.Service().ReadCandidates()[0].Proposal;
                    string spaced = p.Original + " " + p.Suggested, joined = p.Original + p.Suggested;
                    f.NoteText = Fixture.Qa("Sandhi / word-joining", join ? spaced : joined, join ? joined : spaced, "No");
                    f.Tokens[1] = Fixture.Text(join ? spaced : joined, 1, 3, true, false);
                    using (var q = Queue(f, Root())) {
                        var selected = Field<System.Collections.Generic.List<NoteCandidate>>(q, "items").ToList();
                        Call(q, "RunBulkSandhi", selected, "Reviewed synthetic joining", SandhiPreviews(f, selected));
                        Check(f.Writes == 1 && f.Resolved && f.Serialize(f.Tokens).Contains(join ? joined : spaced)); q.Close();
                    }
                }
            });
            test("bulk Sandhi preview starts unchecked and shows full before after evidence", delegate {
                var f = Sandhi(); var selected = f.Service().ReadQueueBook(1); var previews = SandhiPreviews(f, selected);
                var type = typeof(QueueForm).Assembly.GetType("ParatextNoteSuggestions.BulkResolveDialog");
                using (var dialog = (Form)Activator.CreateInstance(type, new object[] { selected, previews })) {
                    dialog.Show(); Application.DoEvents();
                    var list = Field<CheckedListBox>(dialog, "list"); Check(list.CheckedItems.Count == 0);
                    list.SelectedIndex = 0; Application.DoEvents();
                    using (var bitmap = new System.Drawing.Bitmap(dialog.Width, dialog.Height)) {
                        dialog.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, dialog.Width, dialog.Height));
                        bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sandhi-preview.png"));
                    }
                    Check(!Field<CheckBox>(dialog, "acknowledged").Checked && f.Writes == 0); dialog.Close();
                }
            });
            test("bulk Sandhi excludes reviewer decisions, wrong categories, punctuation and partial words", delegate {
                foreach (string kind in new[] { "review", "category", "punctuation", "partial", "ambiguous" }) {
                    var f = Sandhi();
                    if (kind == "review") f.NoteText = Fixture.Qa("Sandhi / word-joining", "\u0baa\u0bb4\u0bc8\u0baf", "\u0baa\u0bc1\u0ba4\u0bbf\u0baf", "Yes");
                    if (kind == "category") f.NoteText = Fixture.Qa("Grammar", "\u0baa\u0bb4\u0bc8\u0baf", "\u0baa\u0bc1\u0ba4\u0bbf\u0baf", "No");
                    if (kind == "punctuation") f.NoteText = Fixture.Qa("Sandhi / word-joining", "\u0baa\u0bb4\u0bc8\u0baf", "\u0baa\u0bc1\u0ba4\u0bbf\u0baf!", "No");
                    if (kind == "partial") f.Tokens[1] = Fixture.Text("X" + f.Service().ReadCandidates()[0].Proposal.Original, 1, 3, true, false);
                    if (kind == "ambiguous") f.Tokens[1] = Fixture.Text(f.Service().ReadCandidates()[0].Proposal.Original + " " + f.Service().ReadCandidates()[0].Proposal.Original, 1, 3, true, false);
                    var candidate = f.Service().ReadQueueBook(1).Single(); bool refused = false;
                    try { f.Service().ForQueueItem(candidate).PreviewBulkSandhi(candidate); } catch (InvalidOperationException) { refused = true; }
                    Check(refused && f.Writes == 0 && !f.Resolved);
                }
            });
            test("decision comment persists in the source thread before resolution", delegate {
                var f = new Fixture(); using (var q = Queue(f, Root())) {
                    Field<TextBox>(q, "comment").Text = "Keep this reading after source review"; Call(q, "Decide", "Rejected");
                    string text = String.Join("", f.Replies.Single().Contents.SelectMany(p => p.Spans).Select(span => span.Text));
                    Check(text.Contains("Keep this reading after source review") && text.Contains(f.UserName) && text.Contains("Decision: Rejected") && f.Resolved && f.Writes == 0); q.Close();
                }
            });
            test("reply failure leaves recoverable decision and retry writes exactly one reply", delegate {
                foreach (bool ignored in new[] { true, false }) {
                    var f = new Fixture { IgnoreComment = ignored, ThrowComment = !ignored }; string root = Root();
                    using (var q = Queue(f, root)) {
                        Field<TextBox>(q, "comment").Text = "Reviewed"; Call(q, "Decide", "Rejected");
                        Check(!f.Resolved && f.Replies.Count == 0 && Field<ReviewProgress>(q, "progress").Outstanding().Count() == 1);
                        f.IgnoreComment = f.ThrowComment = false; Call(q, "FinishResolution");
                        Check(f.Resolved && f.Replies.Count == 1 && f.Writes == 0); q.Close();
                    }
                }
            });
            test("resolve retry after saved reply does not duplicate comment", delegate {
                var f = new Fixture { ThrowResolve = true }; using (var q = Queue(f, Root())) {
                    Field<TextBox>(q, "comment").Text = "Reviewed"; Call(q, "Decide", "Rejected"); Check(f.Replies.Count == 1 && !f.Resolved);
                    f.ThrowResolve = false; Call(q, "FinishResolution"); Check(f.Resolved && f.Replies.Count == 1 && f.Writes == 0); q.Close();
                }
            });
            test("category and discussion filters persist with separate scope cursors", delegate {
                var f = new Fixture(); f.Replies.Add(Fixture.Comment(Fixture.Qa("Textual-basis review", "old", "retain", "Yes"))); string root = Root();
                var scope = new QueueScope { Category = "Textual-basis review", Search = "retain" };
                using (var q = Queue(f, root)) { Scope(q, scope); Check(Field<System.Collections.Generic.List<NoteCandidate>>(q, "items").Count == 1); q.Close(); }
                using (var q = Queue(f, root)) { Check(Field<QueueScope>(q, "scope").Category == scope.Category && Field<QueueScope>(q, "scope").Search == scope.Search); Check(f.Resolves == 0 && f.Writes == 0); q.Close(); }
                Check(scope.Key != new QueueScope().Key);
            });
            test("bulk review writes per-proposal comments and resolves shared thread without USFM edits", delegate {
                var f = new Fixture(); f.Replies.Add(Fixture.Comment(Fixture.Qa("USFM marker", "old", "new", "Yes")));
                using (var q = Queue(f, Root())) {
                    var selected = Field<System.Collections.Generic.List<NoteCandidate>>(q, "items").ToList();
                    Call(q, "RunBulk", selected, "Reviewed manually; no further change needed");
                    Check(f.Resolved && f.Resolves == 1 && f.Writes == 0 && f.Replies.Count == 3); q.Close();
                }
            });
            test("bulk review refuses changed sources and keeps comment failures in recovery", delegate {
                var f = new Fixture { IgnoreComment = true }; using (var q = Queue(f, Root())) {
                    var selected = Field<System.Collections.Generic.List<NoteCandidate>>(q, "items").ToList();
                    Call(q, "RunBulk", selected, "Reviewed manually"); Check(!f.Resolved && f.Replies.Count == 0 && f.Writes == 0);
                    Check(Field<Label>(q, "status").Text.Contains("1 failed") && Field<Label>(q, "status").Text.Contains("1 pending recovery")); q.Close();
                }
                f = new Fixture(); using (var q = Queue(f, Root())) {
                    var selected = Field<System.Collections.Generic.List<NoteCandidate>>(q, "items").ToList(); f.Replies.Add(Fixture.Comment("New human discussion"));
                    Call(q, "RunBulk", selected, "Reviewed manually"); Check(!f.Resolved && f.Replies.Count == 1 && f.Writes == 0); q.Close();
                }
            });
            test("compact review fits every field and action without panel scrolling after resize or restore", delegate {
                var f = MultiBook(); using (var q = Queue(f, Root()))
                {
                    q.Show(); Application.DoEvents();
                    foreach (var size in new[] { new System.Drawing.Size(450, 640), new System.Drawing.Size(669, 720), new System.Drawing.Size(760, 900) })
                    {
                        q.Size = size; Application.DoEvents();
                        q.WindowState = FormWindowState.Minimized; Application.DoEvents(); q.WindowState = FormWindowState.Normal; Application.DoEvents();
                        foreach (string name in new[] { "heading", "scopeLabel", "region", "original", "replacement", "commentPreset", "comment", "deletion", "reviewed", "manual", "previous", "next", "back", "apply", "reject", "pause", "status" })
                        {
                            Control control = Field<Control>(q, name);
                            var bounds = q.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
                            Check(control.Visible && control.Width > 0 && control.Height > 0 && q.ClientRectangle.Contains(bounds));
                            for (Control parent = control.Parent; parent != null && parent != q; parent = parent.Parent) {
                                Check(parent.ClientRectangle.Contains(parent.RectangleToClient(control.RectangleToScreen(control.ClientRectangle))));
                                var table = parent as TableLayoutPanel;
                                if (table != null) Check(!table.VerticalScroll.Visible && !table.HorizontalScroll.Visible);
                            }
                        }
                        using (var bitmap = new System.Drawing.Bitmap(q.Width, q.Height))
                        { q.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, q.Width, q.Height)); bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "queue-layout-" + size.Width + ".png")); }
                    }
                    Check(f.Writes == 0 && f.Resolves == 0); q.Close();
                }
            });
            test("missing sources stay blocked when revisited and layout restores inside the current monitor", delegate {
                var f = MultiBook(); string root = Root();
                using (var q = Queue(f, root))
                {
                    Call(q, "Browse", 1); f.Notes.RemoveAt(0); Call(q, "Browse", -1);
                    Field<TextBox>(q, "comment").Text = "Source disappeared"; Field<CheckBox>(q, "reviewed").Checked = true;
                    Check(!Field<Button>(q, "apply").Enabled && !Field<Button>(q, "reject").Enabled && !Field<Button>(q, "manual").Enabled); q.Close();
                }
                using (var p = new ReviewProgress(root, f.ProjectId))
                    new System.Xml.Linq.XElement("layout", new System.Xml.Linq.XAttribute("x", -30000), new System.Xml.Linq.XAttribute("y", -30000), new System.Xml.Linq.XAttribute("width", 800), new System.Xml.Linq.XAttribute("height", 700)).Save(Path.Combine(p.DirectoryPath, "layout.xml"));
                using (var q = Queue(f, root))
                {
                    Check(System.Windows.Forms.Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(q.Bounds)) && f.Writes == 0 && f.Resolves == 0); q.Close();
                }
            });
            test("Previous and Next retain per-item wording, region, deletion and comments with disabled boundaries", delegate {
                var f = MultiBook(); string root = Root();
                using (var q = Queue(f, root))
                {
                    Check(!Field<Button>(q, "previous").Enabled && Field<Button>(q, "next").Enabled);
                    Field<TextBox>(q, "original").Text = "தமிழ் உரை"; Field<TextBox>(q, "comment").Text = "First draft";
                    Field<ComboBox>(q, "region").SelectedItem = ContentRegion.Footnote; Field<CheckBox>(q, "deletion").Checked = true;
                    Call(q, "Browse", 1); Check(Field<NoteCandidate>(q, "current").Book == 40 && !Field<Button>(q, "next").Enabled);
                    Field<TextBox>(q, "comment").Text = "Second draft"; Call(q, "Browse", -1);
                    Check(Field<TextBox>(q, "original").Text == "தமிழ் உரை" && Field<TextBox>(q, "comment").Text == "First draft");
                    Check(Field<CheckBox>(q, "deletion").Checked && (ContentRegion)Field<ComboBox>(q, "region").SelectedItem == ContentRegion.Footnote);
                    Call(q, "Browse", -1); Check(Field<NoteCandidate>(q, "current").Book == 1 && f.Writes == 0 && f.Resolves == 0); q.Close();
                }
                using (var q = Queue(f, root)) { Check(Field<TextBox>(q, "comment").Text == "First draft"); Call(q, "Browse", 1); Check(Field<TextBox>(q, "comment").Text == "Second draft"); q.Close(); }
            });
            test("single and multiple book filters index selected books before loading, with per-scope resume", delegate {
                var f = MultiBook(); string root = Root();
                using (var q = Queue(f, root))
                {
                    Field<TextBox>(q, "comment").Text = "Genesis retained"; f.ScannedBooks.Clear(); Scope(q, Books(NoteStatusFilter.All, 40));
                    Check(f.ScannedBooks.All(b => b == 40) && Field<NoteCandidate>(q, "current").Book == 40);
                    Check(!Field<Button>(q, "previous").Enabled && !Field<Button>(q, "next").Enabled);
                    Field<TextBox>(q, "comment").Text = "Matthew retained";
                    Scope(q, Books(NoteStatusFilter.All, 1, 40)); Check(Field<System.Collections.Generic.List<NoteCandidate>>(q, "items").Count == 2);
                    Scope(q, Books(NoteStatusFilter.All, 40)); Check(Field<TextBox>(q, "comment").Text == "Matthew retained"); q.Close();
                }
                using (var q = Queue(f, root)) { Check(Field<NoteCandidate>(q, "current").Book == 40 && Field<Label>(q, "scopeLabel").Text.Contains("MAT")); q.Close(); }
            });
            test("Resolved and All use source status independently of local decisions and stay inspection only", delegate {
                var f = MultiBook(); f.Resolved = true;
                using (var q = Queue(f, Root()))
                {
                    Check(Field<NoteCandidate>(q, "current").Book == 40);
                    Scope(q, new QueueScope { Status = NoteStatusFilter.Resolved });
                    Check(Field<NoteCandidate>(q, "current").Book == 1 && Field<NoteCandidate>(q, "current").IsResolved);
                    Field<CheckBox>(q, "reviewed").Checked = true; Field<TextBox>(q, "comment").Text = "Inspection";
                    Call(q, "Decide", "Applied"); Call(q, "Decide", "Rejected"); Call(q, "Decide", "Completed manually");
                    Check(!Field<Button>(q, "apply").Enabled && f.Writes == 0 && f.Resolves == 0);
                    Scope(q, new QueueScope { Status = NoteStatusFilter.All });
                    Check(Field<System.Collections.Generic.List<NoteCandidate>>(q, "items").Count == 2); q.Close();
                }
            });
            test("reviewed history restores the saved evidence and cannot overwrite or resubmit its decision", delegate {
                var f = MultiBook(); string root = Root();
                using (var q = Queue(f, root))
                {
                    Scope(q, new QueueScope { Status = NoteStatusFilter.All });
                    Field<TextBox>(q, "comment").Text = "Reviewed Genesis"; Call(q, "Decide", "Rejected");
                    Check(f.Resolves == 1); Call(q, "Browse", -1);
                    Check(Field<TextBox>(q, "source").Text.Contains("SAVED LOCAL DECISION: Rejected") && Field<TextBox>(q, "comment").Text == "Reviewed Genesis");
                    Call(q, "Decide", "Rejected"); Call(q, "Decide", "Applied"); Check(f.Writes == 0 && f.Resolves == 1); q.Close();
                }
            });
            test("empty or unavailable book scopes stay empty without writes or losing drafts", delegate {
                var f = MultiBook(); using (var q = Queue(f, Root()))
                {
                    Field<TextBox>(q, "comment").Text = "Keep me"; Scope(q, Books(NoteStatusFilter.Unresolved));
                    Check(Field<NoteCandidate>(q, "current") == null && !Field<Button>(q, "next").Enabled);
                    Scope(q, Books(NoteStatusFilter.All, 999)); Check(Field<NoteCandidate>(q, "current") == null && Field<Label>(q, "scopeLabel").Text.Contains("unavailable"));
                    Scope(q, new QueueScope()); Check(Field<TextBox>(q, "comment").Text == "Keep me" && f.Writes == 0 && f.Resolves == 0); q.Close();
                }
            });
            test("another reviewer resolving or changing discussion prevents local rejection before it is saved", delegate {
                foreach (bool resolveSource in new[] { true, false })
                {
                    var f = new Fixture(); string root = Root(); string id = f.Service().ReadQueueBook(1).Single().StableId;
                    using (var q = Queue(f, root))
                    {
                        if (resolveSource) f.Resolved = true; else f.Replies.Add(Fixture.Comment("Another reviewer changed the source"));
                        Field<TextBox>(q, "comment").Text = "Stale review"; Call(q, "Decide", "Rejected");
                        Check(f.Writes == 0 && f.Resolves == 0); q.Close();
                    }
                    using (var p = new ReviewProgress(root, f.ProjectId)) Check(!p.Get(id).Completed);
                }
            });
            test("hidden interrupted applies have an explicit recovery view and cannot be retried by navigation", delegate {
                var f = MultiBook(); string root = Root(); var c = f.Service().ReadQueueBook(1).Single();
                using (var p = new ReviewProgress(root, f.ProjectId)) p.Save(new ReviewRecord { Id = c.StableId, Outcome = "Pending apply", Original = c.Proposal.Original, Replacement = c.Proposal.Suggested, Region = "Body" });
                using (var q = Queue(f, root))
                {
                    Scope(q, Books(NoteStatusFilter.Unresolved, 40)); Check(Field<Label>(q, "heading").Text.Contains("outside this scope"));
                    Scope(q, new QueueScope { RecoveryOnly = true, Status = NoteStatusFilter.All });
                    Check(Field<NoteCandidate>(q, "current").Book == 1 && !Field<Button>(q, "apply").Enabled);
                    Call(q, "Browse", 1); Field<CheckBox>(q, "reviewed").Checked = true; Call(q, "Decide", "Applied");
                    Check(f.Writes == 0 && f.Resolves == 0); q.Close();
                }
            });
            test("pinned project and exact cross-book reference navigate once and only the selected scroll group follows", delegate {
                var f = MultiBook(); var service = f.Service(); var item = service.ReadQueueBook(40).Single();
                service.NavigationGroup = Paratext.PluginInterfaces.SyncReferenceGroup.B; service.Navigate(item);
                Check(f.LastNavigationProject.ID == f.ProjectId && f.LastNavigationReference.BookNum == 40 && f.LastNavigationReference.ChapterNum == 5 && f.LastNavigationReference.VerseNum == 6);
                Check(f.SyncCalls == 1 && f.LastSyncGroup == Paratext.PluginInterfaces.SyncReferenceGroup.B && f.LastSyncReference == item.TargetReference);
                service.NavigationGroup = Paratext.PluginInterfaces.SyncReferenceGroup.None; service.Navigate(item);
                Check(f.SyncCalls == 1 && f.Writes == 0 && f.Resolves == 0 && !service.NavigationVisible(item));
            });
            test("navigation scrolls the actual project group even when the saved group is None or stale", delegate {
                var f = MultiBook(); var service = f.Service(); var item = service.ReadQueueBook(40).Single();
                f.EditorSyncGroup = Paratext.PluginInterfaces.SyncReferenceGroup.C;
                service.NavigationGroup = Paratext.PluginInterfaces.SyncReferenceGroup.None;
                service.Navigate(item);
                Check(f.SyncCalls == 1 && f.LastSyncGroup == f.EditorSyncGroup && f.LastSyncReference == item.TargetReference);
                service.NavigationGroup = Paratext.PluginInterfaces.SyncReferenceGroup.B;
                service.Navigate(item);
                Check(f.SyncCalls == 3 && f.LastSyncGroup == service.NavigationGroup && f.Writes == 0 && f.Resolves == 0);
            });
            test("Return forces a verse change and returns to the exact pinned verse on the next UI tick", delegate {
                var f = new Fixture(); using (var q = Queue(f, Root())) {
                    q.Show(); Application.DoEvents();
                    var target = Field<NoteCandidate>(q, "current");
                    f.ActiveVerse = target.FirstVerse;
                    Field<Button>(q, "back").PerformClick();
                    Check(Field<bool>(q, "returnNavigationPending"));
                    Check(f.LastNavigationReference.VerseNum != target.FirstVerse);
                    Call(q, "CheckNavigation");
                    Check(!Field<bool>(q, "returnNavigationPending") && f.LastNavigationReference == target.TargetReference);
                    Check(f.LastNavigationProject.ID == f.ProjectId && f.Writes == 0 && f.Resolves == 0);
                    q.Close();
                }
            });
            test("Return at the first verse uses the next verse when no previous reference exists", delegate {
                var f = new Fixture(); var service = f.Service(); var item = service.ReadQueueBook(1).Single();
                item.TargetReference = Fixture.MakeRef(1, 1); item.FirstVerse = 1;
                service.BeginReturnToPassage(item); Check(f.LastNavigationReference.VerseNum == 2);
                service.Navigate(item); Check(f.LastNavigationReference == item.TargetReference && f.Writes == 0 && f.Resolves == 0);
            });
            test("choose a later book, preserve drafts, resume and wrap to earlier books without writes", delegate {
                var f = new Fixture(); var laterBook = new Fixture(); laterBook.Ref = Fixture.MakeRef(1, 3, 40);
                f.AvailableBooks.Add(Fixture.Book(40, "MAT")); f.Notes.Add(laterBook.Notes[0]);
                string root = Root();
                using (var q = Queue(f, root))
                {
                    Check(Field<NoteCandidate>(q, "current").Book == 1);
                    Field<TextBox>(q, "comment").Text = "Genesis draft";
                    Field<ComboBox>(q, "startBook").SelectedIndex = 1;
                    Call(q, "StartFromBook");
                    Check(Field<NoteCandidate>(q, "current").Book == 40 && f.Writes == 0 && f.Resolves == 0);
                    Field<TextBox>(q, "comment").Text = "Matthew draft"; Call(q, "SaveDraft"); q.Close();
                }
                using (var q = Queue(f, root))
                {
                    Check(Field<NoteCandidate>(q, "current").Book == 40 && Field<TextBox>(q, "comment").Text == "Matthew draft");
                    Call(q, "Next");
                    Check(Field<NoteCandidate>(q, "current").Book == 1 && Field<TextBox>(q, "comment").Text == "Genesis draft");
                    Check(f.Writes == 0 && f.Resolves == 0); q.Close();
                }
            });
            test("book selection with no unfinished notes leaves the current draft and target intact", delegate {
                var f = new Fixture(); f.AvailableBooks.Add(Fixture.Book(40, "MAT"));
                using (var q = Queue(f, Root()))
                {
                    var current = Field<NoteCandidate>(q, "current"); Field<TextBox>(q, "comment").Text = "Keep draft";
                    Field<ComboBox>(q, "startBook").SelectedIndex = 1; Call(q, "StartFromBook");
                    Check(Field<NoteCandidate>(q, "current") == current && Field<TextBox>(q, "comment").Text == "Keep draft");
                    Check(Field<Label>(q, "status").Text.Contains("No unfinished") && f.Writes == 0); q.Close();
                }
            });
            test("next proposal keeps comment controls enabled after a decision", delegate {
                var f = new Fixture(); f.Replies.Add(Fixture.Comment(Fixture.Qa("Grammar", "old", "new", "Yes")));
                using (var q = Queue(f, Root()))
                {
                    Field<TextBox>(q, "comment").Text = "Reject first"; Call(q, "Decide", "Rejected");
                    Check(Field<TextBox>(q, "comment").Enabled && Field<ComboBox>(q, "commentPreset").Enabled);
                    q.Close();
                }
            });
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
                using (var p = new ReviewProgress(root, f.ProjectId)) f.Replies.Add(Fixture.Comment(NoteService.DecisionReply(p.Get(c.StableId))));
                f.Resolved = true;
                using (var q = Queue(f, root)) {
                    Check(Field<NoteCandidate>(q, "current") == null && Field<Label>(q, "heading").Text.Contains("outside this scope"));
                    Call(q, "ChangeScope", new QueueScope { RecoveryOnly = true, Status = NoteStatusFilter.All });
                    while (Field<bool>(q, "indexing")) Call(q, "IndexBook");
                    Check(Field<NoteCandidate>(q, "current") != null); Call(q, "FinishResolution"); Check(f.Resolves == 0 && f.Writes == 0); q.Close();
                }
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
