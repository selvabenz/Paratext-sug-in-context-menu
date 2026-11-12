using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using ParatextNoteSuggestions;

namespace NoteSuggestionsTests
{
    public static class QueueInformationTests
    {
        static T Field<T>(object o, string name) { return (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(o); }
        static void Call(object o, string name) { o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(o, null); }
        static void Check(bool value) { if (!value) throw new Exception("Queue information assertion failed"); }
        static string Root() { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "qt", Guid.NewGuid().ToString("N")); }
        static void Capture(Form form, string name)
        {
            using (var bitmap = new Bitmap(form.Width, form.Height)) {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name + ".png"));
            }
        }
        static TabControl Tabs(Control parent)
        {
            foreach (Control c in parent.Controls) {
                var tabs = c as TabControl; if (tabs != null) return tabs;
                tabs = Tabs(c); if (tabs != null) return tabs;
            }
            return null;
        }
        public static void Run(Action<string, Action> test)
        {
            test("Log tab retains recovery notices and full session warnings outside review heading", delegate {
                var f = new Fixture(); string root = Root();
                using (var p = new ReviewProgress(root, f.ProjectId)) p.Save(new ReviewRecord { Id = "missing-source", Outcome = "Pending apply" });
                using (var q = QueueTests.Queue(f, root)) {
                    var log = Field<TextBox>(q, "queueLog"); var tabs = Tabs(q);
                    Check(tabs.TabPages.Cast<TabPage>().Any(t => t.Text == "Log" && log.Parent == t));
                    Check(log.ReadOnly && log.Text.Contains("1 interrupted decision(s)") && log.Text.Contains("export decisions"));
                    Check(!Field<Label>(q, "heading").Text.Contains("interrupted"));
                    var session = Field<ReviewSession>(q, "session"); session.AddWarning("A correction was skipped; inspect its source.");
                    Call(q, "RefreshQueueLog");
                    Check(log.Text.Contains("A correction was skipped") && !log.Text.Contains("unreadable discussions"));
                    q.Show(); Application.DoEvents();
                    Capture(q, "queue-review-information");
                    tabs.SelectedTab = (TabPage)log.Parent; Application.DoEvents(); Capture(q, "queue-log");
                    Check(f.Writes == 0 && f.Resolves == 0); q.Close();
                }
            });
            test("information icon shows current review and saved skip details and follows navigation", delegate {
                var f = new Fixture(); string root = Root(); var candidate = f.Service().ReadQueueBook(1).Single();
                using (var p = new ReviewProgress(root, f.ProjectId)) p.SaveSkipped(candidate, "Saved reason: target needs individual review.");
                using (var q = QueueTests.Queue(f, root)) {
                    q.Show(); Application.DoEvents();
                    var icon = Field<Button>(q, "information"); var status = Field<Label>(q, "status");
                    Check(icon.Visible && icon.AccessibleName == "Review information" && status.Parent == null);
                    icon.PerformClick(); Application.DoEvents();
                    var window = Field<Form>(q, "informationWindow"); var details = Field<TextBox>(q, "informationText");
                    Check(window.Visible && details.ReadOnly && details.Text.Contains("Review the correction") && details.Text.Contains("Saved bulk skip") && details.Text.Contains("target needs individual review"));
                    Capture(window, "queue-information-details");
                    status.Text = "Next proposal: review this correction.";
                    Check(details.Text == status.Text && !details.Text.Contains("Saved bulk skip"));
                    icon.PerformClick(); Check(Field<Form>(q, "informationWindow") == window);
                    window.Close(); icon.PerformClick(); Application.DoEvents();
                    Check(Field<Form>(q, "informationWindow").Visible && Field<TextBox>(q, "informationText").Text == status.Text);
                    Check(f.Writes == 0 && f.Resolves == 0); q.Close();
                    Check(Field<Form>(q, "informationWindow").IsDisposed);
                }
            });
        }
    }
}
