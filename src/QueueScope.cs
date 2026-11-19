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
        static int S(int pixels) { return ParatextTheme.Scale(pixels); }
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
            Text = "Review scope"; ParatextTheme.Window(this); MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
            Size = new Size(S(440), S(580)); MinimumSize = new Size(S(320), S(500)); StartPosition = FormStartPosition.CenterParent;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(S(10), S(10), S(10), 0) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(ParatextTheme.Wrap(new Label { Text = "Paratext note status (independent of local decisions)" }, root));
            status.Items.AddRange(Enum.GetValues(typeof(NoteStatusFilter)).Cast<object>().ToArray()); status.SelectedItem = scope.Status; root.Controls.Add(status);
            root.RowStyles.Insert(2, new RowStyle(SizeType.AutoSize)); root.RowStyles.Insert(3, new RowStyle(SizeType.AutoSize));
            category.Items.Add(""); category.Items.AddRange(Proposal.Categories); category.Text = scope.Category;
            var categoryGroup = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1 };
            categoryGroup.Controls.Add(ParatextTheme.Wrap(new Label { Text = "Category (blank = all; custom categories allowed)", Margin = new Padding(0, S(8), 0, S(2)) }, root)); categoryGroup.Controls.Add(category); root.Controls.Add(categoryGroup);
            var searchGroup = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1 };
            searchGroup.Controls.Add(ParatextTheme.Wrap(new Label { Text = "Discussion contains (e.g. verse alignment or spacing)", Margin = new Padding(0, S(8), 0, S(2)) }, root)); search.Text = scope.Search; searchGroup.Controls.Add(search); root.Controls.Add(searchGroup);
            books.Margin = new Padding(0, S(8), 0, S(2)); books.MinimumSize = new Size(0, S(90)); all.Margin = new Padding(0, S(2), 0, S(4));
            numbers = owner.QueueBooks().ToList(); foreach (int book in numbers) books.Items.Add(owner.QueueBookCode(book), scope.Books.Contains(book));
            root.Controls.Add(books); all.Checked = scope.AllBooks; books.Enabled = !all.Checked;
            all.CheckedChanged += delegate { books.Enabled = !all.Checked; }; root.Controls.Add(all);
            var note = ParatextTheme.Wrap(new Label { Text = "Scope selects work; it does not reserve books. Decision comments are saved in Paratext notes; drafts stay local.", Tag = ParatextTheme.KeepColour, ForeColor = ParatextTheme.ColorOf("text_dimmed") }, root);
            root.Controls.Add(note);
            var ok = ParatextTheme.CommandButton("Apply scope"); ok.DialogResult = DialogResult.OK;
            var cancel = ParatextTheme.CommandButton("Cancel"); cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(root); Controls.Add(ParatextTheme.DialogButtons(ok, cancel)); AcceptButton = ok; CancelButton = cancel;
        }
    }
}
