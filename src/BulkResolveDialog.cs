using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ParatextNoteSuggestions
{
    sealed class BulkResolveDialog : Form
    {
        readonly List<NoteCandidate> candidates;
        readonly CheckedListBox list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, HorizontalScrollbar = true };
        readonly TextBox comment = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };
        readonly CheckBox acknowledged = new CheckBox { Text = "I reviewed the selected proposals; no queue Scripture edit is needed.", AutoSize = true };
        public List<NoteCandidate> SelectedItems { get { return list.CheckedIndices.Cast<int>().Select(i => candidates[i]).ToList(); } }
        public string DecisionComment { get { return comment.Text; } }
        public BulkResolveDialog(List<NoteCandidate> items)
        {
            candidates = items;
            Text = "Bulk review and resolve"; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
            Size = new Size(760, 650); MinimumSize = new Size(600, 500); StartPosition = FormStartPosition.CenterParent;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(16) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 95)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(new Label { Text = "Select reviewed proposals from the current filters. Comments go to Paratext notes.\r\nNo Scripture is changed. Threads resolve when every proposal has a decision.", AutoSize = true });
            foreach (var item in candidates) list.Items.Add(item.DisplayReference + " | " + item.Proposal.Category + " | " + item.Proposal.Original + " → " + item.Proposal.Suggested);
            var preview = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill };
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Size = new Size(700, 280), SplitterDistance = 130 };
            split.Panel1.Controls.Add(list); split.Panel2.Controls.Add(preview); root.Controls.Add(split);
            list.SelectedIndexChanged += delegate { preview.Text = list.SelectedIndex < 0 ? "" : candidates[list.SelectedIndex].NoteText; };
            var selection = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            var all = new Button { Text = "Select all", AutoSize = true }; var none = new Button { Text = "Clear selection", AutoSize = true };
            all.Click += delegate { for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, true); };
            none.Click += delegate { for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, false); };
            selection.Controls.Add(all); selection.Controls.Add(none); root.Controls.Add(selection);
            root.Controls.Add(new Label { Text = "Required decision comment (saved separately on each selected proposal)", AutoSize = true }); root.Controls.Add(comment); root.Controls.Add(acknowledged);
            var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            var confirm = new Button { Text = "Save decisions and resolve", AutoSize = true, Enabled = false };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            Action update = delegate { confirm.Enabled = list.CheckedItems.Count > 0 && acknowledged.Checked && !String.IsNullOrWhiteSpace(comment.Text); confirm.Text = "Save " + list.CheckedItems.Count + " decisions and resolve"; };
            list.ItemCheck += delegate { BeginInvoke(update); }; comment.TextChanged += delegate { update(); }; acknowledged.CheckedChanged += delegate { update(); };
            confirm.Click += delegate { if (!confirm.Enabled) return; DialogResult = DialogResult.OK; Close(); };
            footer.Controls.Add(confirm); footer.Controls.Add(cancel); root.Controls.Add(footer); Controls.Add(root); CancelButton = cancel;
        }
    }
}
