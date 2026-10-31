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
[assembly: AssemblyFileVersion("0.1.0.2")]
[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace ParatextNoteSuggestions
{
    public sealed class NoteSuggestionsPlugin : IParatextStandalonePlugin
    {
        public string Name { get { return "Note Suggestions"; } }
        public string Publisher { get { return "selvabenz"; } }
        public Version Version { get { return new Version(0, 1, 0, 2); } }
        public string VersionString { get { return "0.1.0-preview.2"; } }
        public string GetDescription(string locale) { return "Apply exact QA note corrections to project Scripture, with category-aware review."; }
        public IDataFileMerger GetMerger(IPluginHost host, string identifier) { return null; }
        public IEnumerable<PluginMenuEntry> PluginMenuEntries
        {
            get
            {
                yield return new PluginMenuEntry("Note suggestions…", Open, PluginMenuLocation.ScrTextContextMenuDefault);
                yield return new PluginMenuEntry("Note suggestions…", Open, PluginMenuLocation.ScrTextTools);
            }
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
        public NoteCandidate SelectedCandidate { get; private set; }
        public bool RevertRequested { get; private set; }
        public SuggestionPicker(IList<NoteCandidate> candidates, string reference)
        {
            Text = "Note suggestions · " + reference;
            Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            Size = new Size(620, Math.Min(650, 215 + Math.Min(12, candidates.Count) * 65)); MinimumSize = new Size(390, 215);
            var list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(10) };
            var label = new Label { Text = candidates.Count == 0 ? "No unresolved notes for this passage." : candidates.Count + " proposal(s). Apply entries change project Scripture; Review entries open the source and choices.", AutoSize = false, Height = 48, Width = 555 };
            list.Controls.Add(label);
            foreach (var candidate in candidates.Take(12))
            {
                var captured = candidate;
                string prefix = candidate.Proposal.Route == ProposalRoute.Apply ? "Apply: " : "Review: ";
                string value = candidate.Proposal.Suggested.Length == 0 ? candidate.Proposal.Category : candidate.Proposal.Suggested;
                var button = new Button { Text = prefix + NoteParser.Preview(value, 65) + "\r\n" + candidate.Proposal.Category + " · " + candidate.Proposal.Pass, Width = 555, Height = 60, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };
                button.Click += delegate { SelectedCandidate = captured; DialogResult = DialogResult.OK; };
                list.Controls.Add(button);
            }
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 90, Padding = new Padding(10), WrapContents = true };
            var review = new Button { Text = "Review all / choose correction…", AutoSize = true };
            review.Click += delegate { DialogResult = DialogResult.OK; };
            var revert = new Button { Text = "Revert last plugin edit…", AutoSize = true };
            revert.Click += delegate { RevertRequested = true; DialogResult = DialogResult.OK; };
            var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
            CancelButton = close; actions.Controls.Add(review); actions.Controls.Add(revert); actions.Controls.Add(close);
            Controls.Add(list); Controls.Add(actions);
            list.SizeChanged += delegate { int width = Math.Max(280, list.ClientSize.Width - 40); foreach (Control control in list.Controls) control.Width = width; };
        }
    }
}
