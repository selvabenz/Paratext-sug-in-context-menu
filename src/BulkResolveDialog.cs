using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ParatextNoteSuggestions
{
    sealed class SkippedReviewDialog : Form
    {
        static int S(int pixels) { return ParatextTheme.Scale(pixels); }
        readonly ListBox list = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true };
        readonly TextBox detail = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both };
        readonly List<ReviewRecord> records;
        public string SelectedId { get; private set; }
        public SkippedReviewDialog(List<ReviewRecord> items, Action<string> dismiss)
        {
            records = items.ToList(); Text = "Skipped proposals - manual review later";
            ParatextTheme.Window(this); MinimizeBox = false; ShowInTaskbar = false;
            Size = new Size(S(800), S(620)); MinimumSize = new Size(S(380), S(360)); StartPosition = FormStartPosition.CenterParent;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(S(10), S(10), S(10), 0), RowCount = 2, ColumnCount = 1 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(ParatextTheme.Wrap(new Label { Text = "Saved skipped proposals across all books and filters, including previous sessions. Review in queue rechecks the source. Removing a reminder changes no Scripture or notes.", Margin = new Padding(0, 0, 0, S(6)) }, root));
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Size = new Size(S(750), S(450)), SplitterDistance = S(150), Panel1MinSize = S(60), Panel2MinSize = S(80) };
            ParatextTheme.Responsive(split, 720, 0.38);
            split.Panel1.Controls.Add(list); split.Panel2.Controls.Add(detail); root.Controls.Add(split);
            var open = ParatextTheme.CommandButton("Review in queue (all books)"); open.Name = "reviewSkipped";
            var remove = ParatextTheme.CommandButton("Remove reminder"); remove.Name = "dismissSkipped";
            var close = ParatextTheme.CommandButton("Close"); close.DialogResult = DialogResult.Cancel;
            list.SelectedIndexChanged += delegate {
                int i = list.SelectedIndex; open.Enabled = remove.Enabled = i >= 0;
                detail.Text = i < 0 ? "No skipped proposals awaiting manual review." : "SKIP REASON\r\n" + records[i].Evidence + "\r\n\r\nSAVED (UTC): " + records[i].Updated +
                    "\r\n\r\nORIGINAL\r\n" + records[i].Original + "\r\n\r\nSUGGESTED\r\n" + records[i].Replacement + "\r\n\r\nSOURCE DISCUSSION (saved snapshot; may have changed)\r\n" + records[i].SourceNote;
            };
            open.Click += delegate { if (list.SelectedIndex < 0) return; SelectedId = records[list.SelectedIndex].Id; DialogResult = DialogResult.OK; Close(); };
            remove.Click += delegate {
                int i = list.SelectedIndex; if (i < 0) return;
                try { dismiss(records[i].Id); records.RemoveAt(i); list.Items.RemoveAt(i); if (list.Items.Count > 0) list.SelectedIndex = Math.Min(i, list.Items.Count - 1); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not remove reminder", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            foreach (var record in records) list.Items.Add(record.Reference + " | " + record.Category + " | " + record.Evidence);
            open.Enabled = remove.Enabled = false;
            if (list.Items.Count > 0) list.SelectedIndex = 0; else detail.Text = "No skipped proposals awaiting manual review.";
            Controls.Add(root); Controls.Add(ParatextTheme.DialogButtons(open, remove, close)); CancelButton = close;
        }
    }
    sealed class BulkResolveDialog : Form
    {
        static int S(int pixels) { return ParatextTheme.Scale(pixels); }
        readonly List<NoteCandidate> candidates;
        readonly CheckedListBox list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, HorizontalScrollbar = true };
        readonly TextBox comment = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };
        readonly CheckBox acknowledged = new CheckBox { Text = "I reviewed the selected proposals; no queue Scripture edit is needed.", AutoSize = true };
        public List<NoteCandidate> SelectedItems { get { return list.CheckedIndices.Cast<int>().Select(i => candidates[i]).ToList(); } }
        public string DecisionComment { get { return comment.Text; } }
        public bool PreviewScriptureEdits { get; private set; }
        public void SetDecisionComment(string text) { comment.Text = text; }
        public BulkResolveDialog(List<NoteCandidate> items, Dictionary<string, PassageEvidence> previews = null)
            : this(items, previews, 0) { }
        public BulkResolveDialog(List<NoteCandidate> items, Dictionary<string, PassageEvidence> previews, int skipped)
            : this(items, previews, skipped, "Sandhi", null) { }
        public BulkResolveDialog(List<NoteCandidate> items, Dictionary<string, PassageEvidence> previews, int skipped, string category, Action<NoteCandidate> navigate)
        {
            candidates = items;
            bool sandhi = previews != null;
            if (sandhi) acknowledged.Text = "I reviewed every selected before/after correction and approve these Scripture edits.";
            Text = sandhi ? "Bulk apply " + category + " and resolve" : "Bulk review and resolve";
            ParatextTheme.Window(this); MinimizeBox = false; ShowInTaskbar = false;
            Size = new Size(S(780), S(660)); MinimumSize = new Size(S(420), S(580)); StartPosition = FormStartPosition.CenterParent;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(S(10), S(10), S(10), 0) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, S(80))); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(ParatextTheme.Wrap(new Label { Text = sandhi ? "Review each correction, then select the Scripture edits to apply one item at a time. Read-only preview beside the editor; opening a verse does not change Scripture. Changed passages are skipped. An uncertain save stops the batch. No batch undo. " + skipped + " proposals skipped at preview; these remain unresolved and appear in the final totals." : "Select proposals from the current filters. Save decisions records manual/no-change review. For Scripture changes, choose Preview Scripture edits, then approve the before/after text. Supports Sandhi, confirmed/possible typo, plural agreement, punctuation and grammar.", Margin = new Padding(0, 0, 0, S(6)) }, root));
            foreach (var item in candidates) list.Items.Add(item.DisplayReference + " | " + item.Proposal.Category + " | " + item.Proposal.Region + " | " + item.Proposal.Original + " → " + item.Proposal.Suggested);
            var preview = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill };
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Size = new Size(S(700), S(280)), SplitterDistance = S(130), Panel1MinSize = S(60), Panel2MinSize = S(80) };
            ParatextTheme.Responsive(split, 820, 0.42);
            split.Panel1.Controls.Add(list); split.Panel2.Controls.Add(preview); root.Controls.Add(split);
            list.SelectedIndexChanged += delegate { preview.Text = list.SelectedIndex < 0 ? "" : (sandhi ? "BEFORE (USFM)\r\n" + previews[candidates[list.SelectedIndex].StableId].Before + "\r\n\r\nAFTER (USFM)\r\n" + previews[candidates[list.SelectedIndex].StableId].After + "\r\n\r\nSOURCE DISCUSSION\r\n" : "") + candidates[list.SelectedIndex].NoteText; };
            var selection = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, S(4), 0, 0) };
            var all = ParatextTheme.CommandButton("Select all"); var none = ParatextTheme.CommandButton("Clear selection");
            all.Click += delegate { for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, true); };
            none.Click += delegate { for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, false); };
            selection.Controls.Add(all); selection.Controls.Add(none); root.Controls.Add(selection);
            if (navigate != null) {
                var open = ParatextTheme.CommandButton("Open selected verse in editor"); open.Name = "openInEditor";
                open.Image = ParatextTheme.ImageOf("map_pin"); open.TextImageRelation = TextImageRelation.ImageBeforeText;
                open.Click += delegate {
                    if (list.SelectedIndex < 0) return;
                    try { navigate(candidates[list.SelectedIndex]); }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Verse navigation failed", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                };
                selection.Controls.Add(open);
            }
            root.Controls.Add(ParatextTheme.Wrap(new Label { Text = "Required decision comment (saved separately on each selected proposal)", Margin = new Padding(0, S(8), 0, S(2)) }, root)); root.Controls.Add(comment); root.Controls.Add(acknowledged);
            acknowledged.Margin = new Padding(0, S(4), 0, 0); ParatextTheme.Wrap(acknowledged, root);
            var confirm = ParatextTheme.CommandButton("Save decisions and resolve"); confirm.Enabled = false;
            var cancel = ParatextTheme.CommandButton("Cancel"); cancel.DialogResult = DialogResult.Cancel;
            var edit = ParatextTheme.CommandButton("Preview Scripture edits..."); edit.Name = "previewScriptureEdits"; edit.Enabled = false;
            edit.Click += delegate { if (!edit.Enabled) return; PreviewScriptureEdits = true; DialogResult = DialogResult.OK; Close(); };
            cancel.Click += delegate { Close(); };
            Action update = delegate { edit.Enabled = list.CheckedItems.Count > 0 && !String.IsNullOrWhiteSpace(comment.Text); confirm.Enabled = edit.Enabled && acknowledged.Checked; confirm.Text = (sandhi ? "Apply " : "Save ") + list.CheckedItems.Count + (sandhi ? " corrections and resolve" : " decisions and resolve"); };
            list.ItemCheck += delegate { BeginInvoke(update); }; comment.TextChanged += delegate { update(); }; acknowledged.CheckedChanged += delegate { update(); };
            confirm.Click += delegate { if (!confirm.Enabled) return; DialogResult = DialogResult.OK; Close(); };
            Controls.Add(root); Controls.Add(sandhi ? ParatextTheme.DialogButtons(confirm, cancel) : ParatextTheme.DialogButtons(confirm, edit, cancel)); CancelButton = cancel;
            update();
        }
    }
}
