using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;

namespace ParatextNoteSuggestions
{
    public enum NoteStatusFilter { Unresolved, Resolved, All }

    // Work scope is local selection, never a reviewer assignment or reservation.
    public sealed class QueueScope
    {
        public bool AllBooks = true, RecoveryOnly;
        public string Category = "", Search = "";
        public bool IncludesCandidate(NoteCandidate item)
        { return (Category.Length == 0 || String.Equals(Category, item.Proposal.Category, StringComparison.OrdinalIgnoreCase)) && (Search.Length == 0 || item.NoteText.IndexOf(Search, StringComparison.OrdinalIgnoreCase) >= 0); }
        public readonly HashSet<int> Books = new HashSet<int>();
        public NoteStatusFilter Status = NoteStatusFilter.Unresolved;
        public bool IncludesBook(int book) { return AllBooks || Books.Contains(book); }
        public bool IncludesStatus(bool resolved) { return Status == NoteStatusFilter.All || resolved == (Status == NoteStatusFilter.Resolved); }
        public string Key { get { return NoteParser.Hash((RecoveryOnly ? "recovery" : Status.ToString()) + "|" + (AllBooks ? "all" : String.Join(",", Books.OrderBy(b => b))) + (Category.Length == 0 && Search.Length == 0 ? "" : "|" + Category + "|" + Search)); } }
        public string Describe(Func<int, string> code)
        { return RecoveryOnly ? "Recovery - interrupted applies and note resolutions (all books/statuses)" : (AllBooks ? "All books" : Books.Count == 0 ? "No books selected" : String.Join(", ", Books.OrderBy(b => b).Select(code))) + " | " + Status + (Category.Length == 0 ? "" : " | " + Category) + (Search.Length == 0 ? "" : " | Search: " + Search); }
        public XElement ToXml()
        { return new XElement("scope", new XAttribute("allBooks", AllBooks), new XAttribute("status", Status), new XAttribute("recovery", RecoveryOnly), new XAttribute("category", Category), new XAttribute("search", Search), Books.OrderBy(b => b).Select(b => new XElement("book", b))); }
        public static QueueScope FromXml(XElement e)
        {
            var result = new QueueScope(); if (e == null) return result;
            result.Category = (string)e.Attribute("category") ?? ""; result.Search = (string)e.Attribute("search") ?? "";
            result.AllBooks = (bool?)e.Attribute("allBooks") ?? true;
            result.RecoveryOnly = (bool?)e.Attribute("recovery") ?? false;
            NoteStatusFilter status; if (Enum.TryParse((string)e.Attribute("status"), out status) && Enum.IsDefined(typeof(NoteStatusFilter), status)) result.Status = status;
            foreach (var book in e.Elements("book")) result.Books.Add((int)book);
            return result;
        }
    }

    sealed class QueueScopeDialog : Form
    {
        readonly CheckBox all = new CheckBox { Text = "All available books", AutoSize = true };
        readonly CheckedListBox books = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };
        readonly ComboBox status = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
        readonly ComboBox category = new ComboBox { Dock = DockStyle.Top };
        readonly TextBox search = new TextBox { Dock = DockStyle.Top };
        readonly List<int> numbers;
        public QueueScope Selection()
        {
            var scope = new QueueScope { AllBooks = all.Checked, Status = (NoteStatusFilter)status.SelectedItem, Category = category.Text.Trim(), Search = search.Text.Trim() };
            foreach (int index in books.CheckedIndices) scope.Books.Add(numbers[index]);
            return scope;
        }
        public QueueScopeDialog(NoteService owner, QueueScope scope)
        {
            Text = "Review scope"; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
            Size = new Size(440, 540); MinimumSize = new Size(330, 350); StartPosition = FormStartPosition.CenterParent;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(10) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(new Label { Text = "Paratext note status (independent of local decisions)", AutoSize = true });
            status.Items.AddRange(Enum.GetValues(typeof(NoteStatusFilter)).Cast<object>().ToArray()); status.SelectedItem = scope.Status; root.Controls.Add(status);
            root.RowStyles.Insert(2, new RowStyle(SizeType.AutoSize)); root.RowStyles.Insert(3, new RowStyle(SizeType.AutoSize));
            category.Items.Add(""); category.Items.AddRange(Proposal.Categories); category.Text = scope.Category;
            var categoryGroup = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1 };
            categoryGroup.Controls.Add(new Label { Text = "Category (blank = all; custom categories allowed)", AutoSize = true }); categoryGroup.Controls.Add(category); root.Controls.Add(categoryGroup);
            var searchGroup = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1 };
            searchGroup.Controls.Add(new Label { Text = "Discussion contains (e.g. verse alignment or spacing)", AutoSize = true }); search.Text = scope.Search; searchGroup.Controls.Add(search); root.Controls.Add(searchGroup);
            numbers = owner.QueueBooks().ToList(); foreach (int book in numbers) books.Items.Add(owner.QueueBookCode(book), scope.Books.Contains(book));
            root.Controls.Add(books); all.Checked = scope.AllBooks; books.Enabled = !all.Checked;
            all.CheckedChanged += delegate { books.Enabled = !all.Checked; }; root.Controls.Add(all);
            var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown };
            footer.Controls.Add(new Label { Text = "Scope selects work; it does not reserve books.\r\nDecision comments are saved in Paratext notes; drafts stay local.", AutoSize = true });
            var buttons = new FlowLayoutPanel { AutoSize = true };
            var ok = new Button { Text = "Apply scope", AutoSize = true, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(ok); buttons.Controls.Add(cancel); footer.Controls.Add(buttons); root.Controls.Add(footer);
            Controls.Add(root); AcceptButton = ok; CancelButton = cancel;
        }
    }
}
