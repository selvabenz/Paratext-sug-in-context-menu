using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ParatextNoteSuggestions
{
    public sealed class ReviewForm : Form
    {
        readonly NoteService service;
        List<NoteCandidate> notes;
        readonly ListBox list = new ListBox();
        readonly ComboBox filter = new ComboBox();
        readonly ComboBox region = new ComboBox();
        readonly TextBox original = new TextBox(), replacement = new TextBox(), source = new TextBox();
        readonly Label metadata = new Label(), status = new Label();
        readonly CheckBox reviewed = new CheckBox();
        readonly CheckBox deletion = new CheckBox();
        readonly Button apply = ParatextTheme.CommandButton("Apply chosen text"), structural = ParatextTheme.CommandButton("Apply supported marker rename");
        bool populating;
        static int S(int pixels) { return ParatextTheme.Scale(pixels); }
        NoteCandidate Current { get { var item = list.SelectedItem as CandidateItem; return item == null ? null : item.Value; } }
        public ReviewForm(NoteService noteService, List<NoteCandidate> candidates, string selectedKey = null)
        {
            service = noteService; notes = candidates;
            Text = "Note Suggestions · " + service.ProjectName + " · " + service.Reference + " · preview";
            ParatextTheme.Window(this);
            var available = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(S(1050), available.Width - 30), Math.Min(S(780), available.Height - 30));
            MinimumSize = new Size(Math.Min(S(420), available.Width - 30), Math.Min(S(520), available.Height - 30)); StartPosition = FormStartPosition.CenterScreen;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(S(10), S(6), S(10), S(6)) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            // Paratext tool-window toolbar: the filter on the left, refresh and revert as commands.
            var toolbar = ParatextTheme.ToolBar();
            toolbar.Items.Add(new ToolStripLabel("Category"));
            filter.DropDownStyle = ComboBoxStyle.DropDownList; filter.Width = S(240);
            toolbar.Items.Add(new ToolStripControlHost(filter) { AutoSize = false, Width = S(240) });
            var refresh = ParatextTheme.ToolButton("Refresh", "refresh", "Read the notes for this passage again"); refresh.Click += delegate { RefreshNotes(); }; toolbar.Items.Add(refresh);
            var revert = ParatextTheme.ToolButton("Revert last plugin edit…", "clock_undo", "Restore the passage as it was before the last edit made by this plugin"); revert.Click += delegate { NoteSuggestionsPlugin.ShowRevert(service); RefreshNotes(); }; toolbar.Items.Add(revert);
            Controls.Add(root); Controls.Add(toolbar);
            var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(S(900), S(560)), Panel1MinSize = S(80), Panel2MinSize = S(200) };
            ParatextTheme.Responsive(split, 760, 0.34);
            root.Controls.Add(split, 0, 0);
            list.Dock = DockStyle.Fill; list.HorizontalScrollbar = true; list.IntegralHeight = false; list.SelectedIndexChanged += delegate { SelectNote(); }; split.Panel1.Controls.Add(list);
            // Below this height the details scroll instead of squeezing the source discussion away.
            var detail = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 11, Padding = new Padding(S(8), 0, S(4), 0), AutoScroll = true, AutoScrollMinSize = new Size(0, S(470)) };
            detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 11; i++) detail.RowStyles.Add(new RowStyle(i == 8 ? SizeType.Percent : SizeType.AutoSize, i == 8 ? 100 : 0));
            ParatextTheme.Wrap(metadata, detail); metadata.Margin = new Padding(0, S(4), 0, S(8)); detail.Controls.Add(metadata, 0, 0);
            detail.Controls.Add(ParatextTheme.Wrap(new Label { Text = "Target region (verify before applying)" }, detail), 0, 1);
            region.DropDownStyle = ComboBoxStyle.DropDownList; region.Dock = DockStyle.Top;
            // A bound enum list resets its currency to Body when the host creates the form handle.
            // Explicit items preserve the proposal's chosen region across handle/binding initialization.
            region.Items.AddRange(Enum.GetValues(typeof(ContentRegion)).Cast<object>().ToArray()); detail.Controls.Add(region, 0, 2);
            detail.Controls.Add(new Label { Text = "Exact original text", AutoSize = true, Margin = new Padding(0, S(8), 0, S(2)) }, 0, 3);
            ConfigureText(original, 66, false); detail.Controls.Add(original, 0, 4);
            detail.Controls.Add(ParatextTheme.Wrap(new Label { Text = "Chosen replacement — literal text only for text actions", Margin = new Padding(0, S(8), 0, S(2)) }, detail), 0, 5);
            ConfigureText(replacement, 66, false); detail.Controls.Add(replacement, 0, 6);
            detail.Controls.Add(ParatextTheme.Wrap(new Label { Text = "Source discussion / reason / reviewer instructions", Margin = new Padding(0, S(8), 0, S(2)) }, detail), 0, 7);
            ConfigureText(source, 120, true); source.Dock = DockStyle.Fill; detail.Controls.Add(source, 0, 8);
            reviewed.Text = "I reviewed the exact target and chosen correction"; reviewed.AutoSize = true; reviewed.Margin = new Padding(0, S(6), 0, S(4));
            deletion.Text = "Delete the exact original text (no replacement)"; deletion.AutoSize = true;
            var decisions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            decisions.Controls.Add(deletion); decisions.Controls.Add(reviewed); detail.Controls.Add(decisions, 0, 9);
            var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true };
            apply.Click += delegate { ApplyText(); }; actions.Controls.Add(apply);
            structural.Click += delegate { ApplyStructure(); }; actions.Controls.Add(structural);
            detail.Controls.Add(actions, 0, 10); split.Panel2.Controls.Add(detail);
            ParatextTheme.Wrap(status, root); status.Margin = new Padding(0, S(6), 0, 0); status.Text = "Edits save to project USFM. Export and Send/Receive remain your normal Paratext actions. Notes are not automatically resolved.";
            root.Controls.Add(status, 0, 1);
            reviewed.CheckedChanged += delegate { EnableActions(); }; region.SelectedValueChanged += delegate { if (!populating) reviewed.Checked = false; EnableActions(); };
            deletion.CheckedChanged += delegate { replacement.Enabled = !deletion.Checked; reviewed.Checked = false; EnableActions(); };
            original.TextChanged += delegate { if (!populating) reviewed.Checked = false; }; replacement.TextChanged += delegate { if (!populating) reviewed.Checked = false; };
            filter.SelectedIndexChanged += delegate { PopulateList(selectedKey); selectedKey = null; };
            PopulateFilter(); PopulateList(selectedKey);
        }
        static void ConfigureText(TextBox box, int height, bool readOnly)
        { box.Multiline = true; box.ScrollBars = ScrollBars.Vertical; box.WordWrap = true; box.Dock = DockStyle.Top; box.Height = height; box.ReadOnly = readOnly; }
        void PopulateFilter()
        {
            object old = filter.SelectedItem;
            filter.Items.Clear(); filter.Items.Add("All categories");
            foreach (string category in notes.Select(c => c.Proposal.Category).Distinct().OrderBy(s => s)) filter.Items.Add(category);
            filter.SelectedItem = old != null && filter.Items.Contains(old) ? old : "All categories";
        }
        void PopulateList(string selectedKey)
        {
            list.Items.Clear();
            string category = filter.SelectedItem as string;
            foreach (var n in notes.Where(n => category == null || category == "All categories" || n.Proposal.Category == category)) list.Items.Add(new CandidateItem(n));
            int index = 0;
            for (int i = 0; i < list.Items.Count; i++) if (((CandidateItem)list.Items[i]).Value.Key == selectedKey) index = i;
            if (list.Items.Count > 0) list.SelectedIndex = index; else { source.Text = "No unresolved notes match this passage and category."; metadata.Text = "No notes"; original.Clear(); replacement.Clear(); EnableActions(); }
        }
        void SelectNote()
        {
            var c = Current; if (c == null) return;
            populating = true;
            try
            {
                var p = c.Proposal;
                metadata.Text = c.DisplayReference + " · " + p.Pass + " · " + p.Category + "\r\n" + p.Route + " · Confidence: " + p.Confidence + " · Reviewer decision: " + (p.ReviewerDecision ? "Yes / unspecified" : "No");
                original.Text = p.Original; replacement.Text = p.Suggested; source.Text = c.NoteText;
                region.SelectedItem = p.Region; deletion.Checked = false; reviewed.Checked = false;
                status.Text = p.Route == ProposalRoute.Structure ? "Structural review: only exact heading/chapter-label marker renames are supported. Verse exchanges, numbering changes and note movement require manual Paratext review." :
                    p.Route == ProposalRoute.NoChange ? "This note proposes no change; Apply is unavailable." :
                    p.Route == ProposalRoute.Apply ? "Verify the target and correction, then Apply. Only an exact unambiguous match will be written." :
                    "Choose or enter the intended literal correction. Do not apply alternatives or reviewer instructions as Scripture text.";
            }
            finally { populating = false; EnableActions(); }
        }
        void EnableActions()
        {
            var c = Current; bool enabled = c != null && reviewed.Checked && c.Proposal.Route != ProposalRoute.NoChange;
            apply.Enabled = enabled && (replacement.Text.Length > 0 || deletion.Checked) && region.SelectedItem != null && (ContentRegion)region.SelectedItem != ContentRegion.Structure && c.Proposal.Route != ProposalRoute.Structure;
            structural.Enabled = enabled && c.Proposal.Route == ProposalRoute.Structure;
        }
        void ApplyText()
        {
            var c = Current; if (c == null || !reviewed.Checked) return;
            try { service.Apply(c, original.Text, deletion.Checked ? "" : replacement.Text, (ContentRegion)region.SelectedItem, true, deletion.Checked); RefreshNotes(); status.Text = "Applied and verified in project USFM. The note remains unresolved. Export or Send/Receive normally when ready."; }
            catch (Exception ex) { service.ReportFailure(ex); status.Text = ex.Message; MessageBox.Show(ex.Message, "Correction not applied or not verified", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        void ApplyStructure()
        {
            var c = Current; if (c == null || !reviewed.Checked) return;
            if (original.Text != c.Proposal.Original || replacement.Text != c.Proposal.Suggested) { status.Text = "Marker review uses the exact source proposal; edited marker instructions are not accepted."; return; }
            try { service.ApplyMarkerRename(c); RefreshNotes(); status.Text = "Marker correction applied and verified in project USFM."; }
            catch (Exception ex) { service.ReportFailure(ex); status.Text = ex.Message; MessageBox.Show(ex.Message, "Structural correction not applied or not verified", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        void RefreshNotes()
        {
            try { notes = service.ReadCandidates(); PopulateFilter(); PopulateList(null); }
            catch (Exception ex) { apply.Enabled = false; structural.Enabled = false; status.Text = ex.Message; }
        }
        sealed class CandidateItem
        {
            public readonly NoteCandidate Value;
            public CandidateItem(NoteCandidate value) { Value = value; }
            public override string ToString() { return Value.Proposal.Pass + " · " + Value.Proposal.Category + " · " + NoteParser.Preview(Value.Proposal.Suggested.Length > 0 ? Value.Proposal.Suggested : Value.NoteText, 50); }
        }
    }
}
