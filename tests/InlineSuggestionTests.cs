using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Paratext.PluginInterfaces;
using ParatextNoteSuggestions;

namespace NoteSuggestionsTests
{
    public static class InlineSuggestionTests
    {
        static void Check(bool value) { if (!value) throw new Exception("Inline suggestion assertion failed"); }
        static string Root() { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "inline-tests", Guid.NewGuid().ToString("N")); }
        static T Field<T>(object o, string name) { return (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(o); }
        static object Call(object o, string name, params object[] args)
        {
            try { return o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(o, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException; }
        }
        public static void Run(Action<string, Action> test)
        {
            test("inline annotation renders a bounded suggestion label and preserves the full tooltip", delegate {
                var f = new Fixture(); var item = f.Service().ReadQueueBook(1).Single();
                item.Proposal.Suggested = "தமிழ் & " + new string('x', 200);
                var source = f.Plugin.HighlightFor(f.Project); source.GetStyleInfo(1); source.SetTarget(item);
                var mark = source.GetAnnotations(item.TargetReference, item.Proposal.Original).Single();
                Check(File.Exists(mark.Icon));
                using (var bitmap = Image.FromFile(mark.Icon)) Check(bitmap.Width <= 368 && bitmap.Height > 10);
                Check(mark.IconToolTipText.Contains(item.Proposal.Original) && mark.IconToolTipText.Contains(item.Proposal.Suggested));
                Check(f.Writes == 0 && f.Replies.Count == 0 && f.Resolves == 0);
            });
            test("only right-clicks on a current annotation dispatch its pinned item and screen location", delegate {
                var f = new Fixture(); var source = f.Plugin.HighlightFor(f.Project); var item = f.Service().ReadQueueBook(1).Single();
                source.SetTarget(item); int clicks = 0; var point = new Point(123, 234);
                source.ContextMenuRequested = delegate(NoteCandidate c, Point p) { Check(c == item && p == point); clicks++; };
                var mark = source.GetAnnotations(item.TargetReference, item.Proposal.Original).Single();
                Check(!mark.Click(MouseButton.Left, true, point));
                Check(mark.Click(MouseButton.Right, true, point) && mark.Click(MouseButton.Right, false, point) && clicks == 2);
                source.SetTarget(null); Check(!mark.Click(MouseButton.Right, false, point) && clicks == 2);
                source.SetTarget(item); Check(!mark.Click(MouseButton.Right, false, point));
                Check(f.Writes == 0);
            });
            test("inline apply verifies Scripture and records the existing queue reply and resolution once", delegate {
                var f = new Fixture(); using (var q = QueueTests.Queue(f, Root())) {
                    var item = Field<NoteCandidate>(q, "current");
                    Call(q, "CompleteInlineDecision", item, "Applied", "Reviewed inline correction");
                    Check(f.Writes == 1 && f.Resolves == 1 && f.Replies.Count == 1);
                    Check(f.Tokens.OfType<IUSFMTextToken>().Any(t => t.Text.Contains(item.Proposal.Suggested)));
                    Check(Field<ReviewProgress>(q, "progress").Get(item.StableId).Outcome == "Applied");
                    Call(q, "CompleteInlineDecision", item, "Applied", "Stale click"); Check(f.Writes == 1); q.Close();
                }
            });
            test("right-click opens the action menu and an ignored proposal cannot be applied from its stale menu", delegate {
                var f = new Fixture(); using (var q = QueueTests.Queue(f, Root())) {
                    q.Show(); Application.DoEvents();
                    var item = Field<NoteCandidate>(q, "current");
                    var mark = f.Plugin.HighlightFor(f.Project).GetAnnotations(item.TargetReference, item.Proposal.Original).Single();
                    Check(mark.Click(MouseButton.Right, false, new Point(100, 100)));
                    var menu = Field<ContextMenuStrip>(q, "inlineMenu");
                    var accept = menu.Items.OfType<ToolStripMenuItem>().Single(m => m.Text == "Apply suggestion...");
                    var decline = menu.Items.OfType<ToolStripMenuItem>().Single(m => m.Text == "Reject suggestion...");
                    var ignore = menu.Items.OfType<ToolStripMenuItem>().Single(m => m.Text.StartsWith("Ignore for now"));
                    Check(menu.Visible && accept.Enabled && decline.Enabled && ignore.Enabled);
                    ignore.PerformClick(); accept.PerformClick();
                    Check(Field<NoteCandidate>(q, "current") == null && f.Writes == 0 && f.Resolves == 0 && f.Replies.Count == 0);
                    menu.Close(); q.Close();
                }
            });
            test("inline reject records a commented decision without Scripture edits", delegate {
                var f = new Fixture(); using (var q = QueueTests.Queue(f, Root())) {
                    var item = Field<NoteCandidate>(q, "current");
                    Call(q, "CompleteInlineDecision", item, "Rejected", "Existing wording is correct");
                    Check(f.Writes == 0 && f.Resolves == 1 && f.Replies.Count == 1);
                    Check(Field<ReviewProgress>(q, "progress").Get(item.StableId).Outcome == "Rejected"); q.Close();
                }
            });
            test("inline ignore preserves its draft and unresolved source for later review", delegate {
                var f = new Fixture(); using (var q = QueueTests.Queue(f, Root())) {
                    var item = Field<NoteCandidate>(q, "current"); Field<TextBox>(q, "comment").Text = "Check this later";
                    Call(q, "CompleteInlineDecision", item, "Ignored", "");
                    var record = Field<ReviewProgress>(q, "progress").Get(item.StableId);
                    Check(record.Outcome == "Draft" && record.Comment == "Check this later" && !record.Completed);
                    Check(Field<NoteCandidate>(q, "current") == null && f.Writes == 0 && f.Resolves == 0 && f.Replies.Count == 0); q.Close();
                }
            });
            test("inline decisions refuse empty comments, changed drafts and paused queues", delegate {
                var f = new Fixture(); using (var q = QueueTests.Queue(f, Root())) {
                    var item = Field<NoteCandidate>(q, "current");
                    Call(q, "CompleteInlineDecision", item, "Applied", "  ");
                    Field<TextBox>(q, "replacement").Text = "Different reviewed draft";
                    Check(!(bool)Call(q, "CanInlineDecision", item, "Applied"));
                    Call(q, "CompleteInlineDecision", item, "Applied", "Do not apply a different draft");
                    q.GetType().GetField("paused", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(q, true);
                    Call(q, "CompleteInlineDecision", item, "Rejected", "Paused");
                    Call(q, "CompleteInlineDecision", item, "Ignored", "");
                    Check(Field<NoteCandidate>(q, "current") == item && f.Writes == 0 && f.Resolves == 0 && f.Replies.Count == 0); q.Close();
                }
            });
            test("pending inline apply retains recovery and cannot be submitted again", delegate {
                var f = new Fixture(); using (var q = QueueTests.Queue(f, Root())) {
                    var item = Field<NoteCandidate>(q, "current"); f.IgnorePut = true;
                    Call(q, "CompleteInlineDecision", item, "Applied", "Reviewed exact correction");
                    Check(Field<ReviewProgress>(q, "progress").Get(item.StableId).Outcome == "Pending apply");
                    Check(f.Writes == 1 && f.Resolves == 0 && f.Replies.Count == 0);
                    Call(q, "CompleteInlineDecision", item, "Applied", "Never repeat an uncertain write"); Check(f.Writes == 1); q.Close();
                }
            });
            test("closing the queue detaches inline actions and clears its annotation", delegate {
                var f = new Fixture(); var source = f.Plugin.HighlightFor(f.Project);
                using (var q = QueueTests.Queue(f, Root())) {
                    var item = Field<NoteCandidate>(q, "current");
                    var mark = source.GetAnnotations(item.TargetReference, item.Proposal.Original).Single();
                    Check(source.ContextMenuRequested != null); q.Dispose();
                    Check(source.ContextMenuRequested == null && !mark.Click(MouseButton.Right, false, Point.Empty));
                    Check(source.GetAnnotations(item.TargetReference, item.Proposal.Original).Count == 0);
                }
            });
        }
    }
}
