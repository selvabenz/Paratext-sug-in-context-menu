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
[assembly: AssemblyFileVersion("0.1.0.0")]
[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace ParatextNoteSuggestions
{
    public sealed class NoteSuggestionsPlugin : IParatextStandalonePlugin
    {
        public string Name { get { return "Note Suggestions"; } }
        public string Publisher { get { return "selvabenz"; } }
        public Version Version { get { return new Version(0, 1, 0); } }
        public string VersionString { get { return "0.1.0-preview.1"; } }
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
                Action chosenAction = null;
                using (var menu = new ContextMenuStrip())
                {
                    menu.Font = new Font("Segoe UI", 11);
                    menu.ShowItemToolTips = true;
                    menu.Items.Add(new ToolStripMenuItem("Note suggestions · " + state.VerseRef) { Enabled = false });
                    if (candidates.Count == 0) menu.Items.Add(new ToolStripMenuItem("No unresolved notes for this passage") { Enabled = false });
                    foreach (var candidate in candidates.Take(12))
                    {
                        var captured = candidate;
                        string prefix = candidate.Proposal.Route == ProposalRoute.Apply ? "Apply: " : "Review: ";
                        string label = candidate.Proposal.Suggested.Length == 0 ? candidate.Proposal.Category : candidate.Proposal.Suggested;
                        var item = new ToolStripMenuItem(prefix + NoteParser.Preview(label, 64));
                        item.ToolTipText = candidate.Proposal.Category + " · " + candidate.Proposal.Pass + "\n" + candidate.Proposal.Reason;
                        item.Click += delegate { chosenAction = delegate { RunAction(service, captured); }; };
                        menu.Items.Add(item);
                    }
                    menu.Items.Add(new ToolStripSeparator());
                    menu.Items.Add("Review all notes / choose correction…", null, delegate { chosenAction = delegate { using (var form = new ReviewForm(service, candidates)) form.ShowDialog(); }; });
                    menu.Items.Add("Revert last plugin edit…", null, delegate { chosenAction = delegate { ShowRevert(service); }; });
                    // ContextMenuStrip needs a message loop while the host callback remains active.
                    using (var owner = new MenuOwnerForm())
                    {
                        owner.Shown += delegate { menu.Show(owner, owner.PointToClient(Cursor.Position)); };
                        // Close after the menu item Click event finishes so selected actions survive dropdown dismissal.
                        menu.Closed += delegate { if (!owner.IsDisposed && owner.IsHandleCreated) owner.BeginInvoke(new MethodInvoker(owner.Close)); };
                        owner.ShowDialog();
                    }
                }
                if (chosenAction != null) chosenAction();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, Name, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        static void RunAction(NoteService service, NoteCandidate candidate)
        {
            if (candidate.Proposal.Route != ProposalRoute.Apply)
            { using (var form = new ReviewForm(service, service.ReadCandidates(), candidate.Key)) form.ShowDialog(); return; }
            try { service.Apply(candidate, candidate.Proposal.Original, candidate.Proposal.Suggested, candidate.Proposal.Region, false); MessageBox.Show("Applied and verified in the project USFM. The QA note remains unresolved.", "Note Suggestions"); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Correction not applied or not verified", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        internal static void ShowRevert(NoteService service)
        {
            try
            {
                if (MessageBox.Show("Revert the last plugin edit in this project? Revert is available only while the saved post-edit chapter still matches exactly.", "Revert plugin edit", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                service.Revert(); MessageBox.Show("Previous USFM restored and verified.", "Note Suggestions");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Revert not verified", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }
    sealed class MenuOwnerForm : Form
    {
        public MenuOwnerForm() { ShowInTaskbar = false; FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual; Location = Cursor.Position; Size = new Size(1, 1); Opacity = 0; }
    }
}
