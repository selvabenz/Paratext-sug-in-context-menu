using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Windows.Forms;
using Paratext.PluginInterfaces;

[assembly: AssemblyTitle("Paratext Note Suggestions")]
[assembly: AssemblyDescription("Category-aware QA note corrections for Paratext 9.5")]
[assembly: AssemblyCompany("selvabenz")]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.3.0.1")]
[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace ParatextNoteSuggestions
{
    public sealed class NoteSuggestionsPlugin : IParatextWindowPlugin, IParatextScrTextAnnotationPlugin
    {
        // Paratext creates one plugin object; a queue window restored from a saved layout finds it here.
        internal static NoteSuggestionsPlugin Current { get; private set; }
        public NoteSuggestionsPlugin() { Current = this; }
        readonly Dictionary<string, QueueTargetHighlight> highlights = new Dictionary<string, QueueTargetHighlight>();
        public QueueTargetHighlight HighlightFor(IProject project)
        {
            QueueTargetHighlight source;
            if (!highlights.TryGetValue(project.ID, out source)) { source = new QueueTargetHighlight(project); highlights.Add(project.ID, source); }
            return source;
        }
        public IEnumerable<PluginAnnotationMenuEntry> PluginAnnotationMenuEntries
        { get { yield return new PluginAnnotationMenuEntry("Highlight queued note target", HighlightFor); } }
        static readonly Dictionary<string, QueueForm> Queues = new Dictionary<string, QueueForm>();
        public string Name { get { return "Note Suggestions"; } }
        public string Publisher { get { return "selvabenz"; } }
        public Version Version { get { return new Version(0, 3, 0, 1); } }
        public string VersionString { get { return "0.3.0-preview.1"; } }
        public string GetDescription(string locale) { return "Apply exact QA note corrections to project Scripture, with category-aware review."; }
        public IDataFileMerger GetMerger(IPluginHost host, string identifier) { return null; }
        public IEnumerable<WindowPluginMenuEntry> PluginMenuEntries
        {
            get
            {
                yield return new WindowPluginMenuEntry("Note suggestions…", (host, state) => Open(host, state), PluginMenuLocation.ScrTextContextMenuDefault);
                yield return new WindowPluginMenuEntry("Note suggestions…", (host, state) => Open(host, state), PluginMenuLocation.ScrTextTools);
                yield return new WindowPluginMenuEntry("Review note queue…", OpenQueueWindow, PluginMenuLocation.ScrTextContextMenuDefault);
                yield return new WindowPluginMenuEntry("Review note queue…", OpenQueueWindow, PluginMenuLocation.ScrTextTools);
            }
        }
        internal static void Track(string projectId, QueueForm form)
        {
            Queues[projectId] = form;
            form.FormClosed += delegate { QueueForm open; if (Queues.TryGetValue(projectId, out open) && open == form) Queues.Remove(projectId); };
        }
        // The queue as a Paratext child window that docks, tabs and floats like the host's own tools.
        public void OpenQueueWindow(IWindowPluginHost host, IParatextChildState state)
        {
            try
            {
                if (state == null || state.Project == null || state.VerseRef == null) throw new InvalidOperationException("Open the Scripture project whose notes you want to review first.");
                if (host.ApplicationVersion.Major != 9 || host.ApplicationVersion.Minor != 5) throw new InvalidOperationException("This preview targets Paratext 9.5.");
                QueueForm existing;
                if (Queues.TryGetValue(state.Project.ID, out existing) && !existing.IsDisposed) { existing.Reveal(); return; }
                host.ShowEmbeddedUi(new QueueControl(this), state.Project);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Note review queue", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        // Separate floating window; the menus open the docked window above.
        public void OpenQueue(IPluginHost host, IParatextChildState state)
        {
            try
            {
                if (state == null || state.Project == null || state.VerseRef == null) throw new InvalidOperationException("Open the Scripture project whose notes you want to review first.");
                if (host.ApplicationVersion.Major != 9 || host.ApplicationVersion.Minor != 5) throw new InvalidOperationException("This preview targets Paratext 9.5.");
                QueueForm existing;
                if (Queues.TryGetValue(state.Project.ID, out existing) && !existing.IsDisposed) { existing.Reveal(); return; }
                var form = new QueueForm(new NoteService(this, host, state));
                Track(state.Project.ID, form);
                System.ComponentModel.CancelEventHandler shutdown = delegate(object sender, System.ComponentModel.CancelEventArgs e) { form.Close(); if (!form.IsDisposed) e.Cancel = true; };
                host.ShuttingDown += shutdown;
                form.FormClosed += delegate { host.ShuttingDown -= shutdown; };
                // Modeless ownership keeps the queue visible when the host asynchronously focuses a verse.
                // It does not disable the Scripture/reference windows like ShowDialog would.
                var parent = Form.ActiveForm;
                if (parent != null && !(parent is QueueForm)) form.Show(parent); else form.Show();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Note review queue", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        public void Open(IPluginHost host, IParatextChildState state)
        {
            try
            {
                if (state == null || state.Project == null || state.VerseRef == null) throw new InvalidOperationException("Open a Scripture project and choose a passage first.");
                if (host.ApplicationVersion.Major != 9 || host.ApplicationVersion.Minor != 5)
                    throw new InvalidOperationException("This preview targets Paratext 9.5. Test compatibility before using another version.");
                var service = new NoteService(this, host, state);
                var candidates = service.ReadCandidates();
                using (var picker = new SuggestionPicker(candidates, state.VerseRef.ToString()))
                {
                    if (picker.ShowDialog() != DialogResult.OK) return;
                    if (picker.SelectedCandidate != null) RunAction(service, picker.SelectedCandidate);
                    else if (picker.RevertRequested) ShowRevert(service);
                    else using (var form = new ReviewForm(service, candidates)) form.ShowDialog();
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, Name, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        static void RunAction(NoteService service, NoteCandidate candidate)
        {
            if (candidate.Proposal.Route != ProposalRoute.Apply)
            { using (var form = new ReviewForm(service, service.ReadCandidates(), candidate.Key)) form.ShowDialog(); return; }
            try { service.Apply(candidate, candidate.Proposal.Original, candidate.Proposal.Suggested, candidate.Proposal.Region, false); MessageBox.Show("Applied and verified in the project USFM. The QA note remains unresolved.", "Note Suggestions"); }
            catch (Exception ex) { service.ReportFailure(ex); MessageBox.Show(ex.Message, "Correction not applied or not verified", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        internal static void ShowRevert(NoteService service)
        {
            try
            {
                if (MessageBox.Show("Revert the last plugin edit in this project? Revert is available only while the saved post-edit chapter still matches exactly.", "Revert plugin edit", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                service.Revert(); MessageBox.Show("Previous USFM restored and verified.", "Note Suggestions");
            }
            catch (Exception ex) { service.ReportFailure(ex); MessageBox.Show(ex.Message, "Revert not verified", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }
    public sealed class SuggestionPicker : Form
    {
        static int S(int pixels) { return ParatextTheme.Scale(pixels); }
        public NoteCandidate SelectedCandidate { get; private set; }
        public bool RevertRequested { get; private set; }
        public SuggestionPicker(IList<NoteCandidate> candidates, string reference)
        {
            Text = "Note suggestions · " + reference;
            ParatextTheme.Window(this);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false; MaximizeBox = false;
            Size = new Size(S(600), Math.Min(S(650), S(190) + Math.Min(12, candidates.Count) * S(58))); MinimumSize = new Size(S(340), S(300));
            var list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(S(10), S(10), S(10), 0) };
            var label = new Label { Text = candidates.Count == 0 ? "No unresolved notes for this passage." : candidates.Count + " proposal(s). Apply entries change project Scripture; Review entries open the source and choices.", AutoSize = true, Margin = new Padding(0, 0, 0, S(6)) };
            list.Controls.Add(label);
            foreach (var candidate in candidates.Take(12))
            {
                var captured = candidate;
                string prefix = candidate.Proposal.Route == ProposalRoute.Apply ? "Apply: " : "Review: ";
                string value = candidate.Proposal.Suggested.Length == 0 ? candidate.Proposal.Category : candidate.Proposal.Suggested;
                var button = ParatextTheme.CommandButton(prefix + NoteParser.Preview(value, 65) + "\r\n" + candidate.Proposal.Category + " · " + candidate.Proposal.Pass);
                button.AutoSize = false; button.Height = S(50); button.TextAlign = ContentAlignment.MiddleLeft; button.Margin = new Padding(0, 0, 0, S(4));
                button.Image = ParatextTheme.ImageOf(candidate.Proposal.Route == ProposalRoute.Apply ? "Checkmark" : "edit_pencil");
                button.ImageAlign = ContentAlignment.MiddleLeft; button.TextImageRelation = TextImageRelation.ImageBeforeText;
                button.Click += delegate { SelectedCandidate = captured; DialogResult = DialogResult.OK; };
                list.Controls.Add(button);
            }
            var review = ParatextTheme.CommandButton("Review all / choose correction…");
            review.Click += delegate { DialogResult = DialogResult.OK; };
            var revert = ParatextTheme.CommandButton("Revert last plugin edit…");
            revert.Click += delegate { RevertRequested = true; DialogResult = DialogResult.OK; };
            var close = ParatextTheme.CommandButton("Close"); close.DialogResult = DialogResult.Cancel;
            CancelButton = close;
            Controls.Add(list); Controls.Add(ParatextTheme.DialogButtons(review, revert, close));
            list.SizeChanged += delegate {
                int width = Math.Max(S(200), list.ClientSize.Width - list.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
                label.MaximumSize = new Size(width, 0);
                foreach (Control control in list.Controls) if (control is Button) control.Width = width;
            };
        }
    }
}
