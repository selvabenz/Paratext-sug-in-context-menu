using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ParatextNoteSuggestions
{
    sealed class SkippedReviewDialog : Form
    {
        readonly ListBox list = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true };
        readonly TextBox detail = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both };
        readonly List<ReviewRecord> records;
        public string SelectedId { get; private set; }
        public SkippedReviewDialog(List<ReviewRecord> items, Action<string> dismiss)
        {
            records = items.ToList(); Text = "Skipped proposals - manual review later";
            Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
            Size = new Size(800, 650); MinimumSize = new Size(600, 480); StartPosition = FormStartPosition.CenterParent;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 3, ColumnCount = 1 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(new Label { AutoSize = true, Text = "Saved skipped proposals across all books and filters, including previous sessions.\r\nReview in queue rechecks the source. Removing a reminder changes no Scripture or notes." });
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Size = new Size(750, 450), SplitterDistance = 150 };
            split.Panel1.Controls.Add(list); split.Panel2.Controls.Add(detail); root.Controls.Add(split);
            var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            var open = new Button { Name = "reviewSkipped", Text = "Review in queue (all books)", AutoSize = true };
            var remove = new Button { Name = "dismissSkipped", Text = "Remove reminder", AutoSize = true };
            var close = new Button { Text = "Close", DialogResult = DialogResult.Cancel, AutoSize = true };
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
            footer.Controls.Add(open); footer.Controls.Add(remove); footer.Controls.Add(close); root.Controls.Add(footer); Controls.Add(root); CancelButton = close;
        }
    }
    sealed class BulkResolveDialog : Form
    {
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
            Text = sandhi ? "Bulk apply " + category + " and resolve" : "Bulk review and resolve"; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
            Size = new Size(760, 650); MinimumSize = new Size(600, 500); StartPosition = FormStartPosition.CenterParent;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(16) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 95)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(new Label { Text = sandhi ? "Review each correction, then select the Scripture edits to apply one item at a time.\r\nRead-only preview beside the editor. Opening a verse does not change Scripture.\r\nChanged passages are skipped. An uncertain save stops the batch. No batch undo.\r\n" + skipped + " proposals skipped at preview; these remain unresolved and appear in the final totals." : "Select proposals from the current filters. Save decisions records manual/no-change review.\r\nFor Scripture changes, choose Preview Scripture edits, then approve the before/after text.\r\nSupports Sandhi, confirmed/possible typo, plural agreement, punctuation and grammar.", AutoSize = true });
            foreach (var item in candidates) list.Items.Add(item.DisplayReference + " | " + item.Proposal.Category + " | " + item.Proposal.Region + " | " + item.Proposal.Original + " → " + item.Proposal.Suggested);
            var preview = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill };
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Size = new Size(700, 280), SplitterDistance = 130 };
            split.Panel1.Controls.Add(list); split.Panel2.Controls.Add(preview); root.Controls.Add(split);
            list.SelectedIndexChanged += delegate { preview.Text = list.SelectedIndex < 0 ? "" : (sandhi ? "BEFORE (USFM)\r\n" + previews[candidates[list.SelectedIndex].StableId].Before + "\r\n\r\nAFTER (USFM)\r\n" + previews[candidates[list.SelectedIndex].StableId].After + "\r\n\r\nSOURCE DISCUSSION\r\n" : "") + candidates[list.SelectedIndex].NoteText; };
            var selection = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            var all = new Button { Text = "Select all", AutoSize = true }; var none = new Button { Text = "Clear selection", AutoSize = true };
            all.Click += delegate { for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, true); };
            none.Click += delegate { for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, false); };
            selection.Controls.Add(all); selection.Controls.Add(none); root.Controls.Add(selection);
            if (navigate != null) {
                var open = new Button { Name = "openInEditor", Text = "Open selected verse in editor", AutoSize = true };
                open.Click += delegate {
                    if (list.SelectedIndex < 0) return;
                    try { navigate(candidates[list.SelectedIndex]); }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Verse navigation failed", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                };
                selection.Controls.Add(open);
            }
            root.Controls.Add(new Label { Text = "Required decision comment (saved separately on each selected proposal)", AutoSize = true }); root.Controls.Add(comment); root.Controls.Add(acknowledged);
            var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            var confirm = new Button { Text = "Save decisions and resolve", AutoSize = true, Enabled = false };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            var edit = new Button { Name = "previewScriptureEdits", Text = "Preview Scripture edits...", AutoSize = true, Enabled = false };
            edit.Click += delegate { if (!edit.Enabled) return; PreviewScriptureEdits = true; DialogResult = DialogResult.OK; Close(); };
            cancel.Click += delegate { Close(); };
            Action update = delegate { edit.Enabled = list.CheckedItems.Count > 0 && !String.IsNullOrWhiteSpace(comment.Text); confirm.Enabled = edit.Enabled && acknowledged.Checked; confirm.Text = (sandhi ? "Apply " : "Save ") + list.CheckedItems.Count + (sandhi ? " corrections and resolve" : " decisions and resolve"); };
            list.ItemCheck += delegate { BeginInvoke(update); }; comment.TextChanged += delegate { update(); }; acknowledged.CheckedChanged += delegate { update(); };
            confirm.Click += delegate { if (!confirm.Enabled) return; DialogResult = DialogResult.OK; Close(); };
            footer.Controls.Add(confirm); if (!sandhi) footer.Controls.Add(edit); footer.Controls.Add(cancel); root.Controls.Add(footer); Controls.Add(root); CancelButton = cancel;
            update();
        }
    }
}
