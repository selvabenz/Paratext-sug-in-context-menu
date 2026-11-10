using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;
using Paratext.PluginInterfaces;

namespace ParatextNoteSuggestions
{
    // Modeless: reference browsing never grants or revokes permission to edit the pinned target.
    public sealed class QueueForm : Form
    {
        readonly NoteService owner;
        readonly ReviewProgress progress;
        readonly ReviewSession session;
        readonly ComboBox commentPreset = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
        readonly Button viewReport = Button("View latest report"), reportFolder = Button("Report folder...");
        string reportProjectDirectory, loadedContext = "";
        readonly List<NoteCandidate> items = new List<NoteCandidate>();
        readonly HashSet<string> duplicateIds = new HashSet<string>();
        readonly HashSet<string> seen = new HashSet<string>();
        readonly Queue<int> books;
        readonly ComboBox startBook = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
        readonly Button startFromBook = Button("Start from book");
        readonly Button previous = Button("Previous"), next = Button("Next"), filters = Button("Filters..."), recovery = Button("Recovery");
        readonly ComboBox syncGroup = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 85 };
        readonly Label scopeLabel = new Label { AutoSize = true }, navigationLabel = new Label { AutoSize = true };
        readonly Timer navigationTimer = new Timer { Interval = 150 };
        QueueScope scope;
        int navigationChecks;
        bool returnNavigationPending;
        int firstBook;
        sealed class BookChoice
        {
            public int Number;
            public string Code;
            public override string ToString() { return Code; }
        }
        readonly Timer indexTimer = new Timer { Interval = 30 };
        readonly Timer draftTimer = new Timer { Interval = 600 };
        readonly Label heading = new Label(), status = new Label();
        readonly TextBox original = Box(65), replacement = Box(65), source = Box(180), comment = Box(65);
        readonly ComboBox region = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
        readonly CheckBox reviewed = new CheckBox { Text = "I reviewed this exact correction", AutoSize = true };
        readonly CheckBox deletion = new CheckBox { Text = "Explicitly delete the original text", AutoSize = true };
        readonly Button apply = Button("Apply + next"), reject = Button("Reject + next"), manual = Button("Completed manually / no change"), retry = Button("Verified no edit; allow retry"), pause = Button("Pause"), later = Button("Leave for later"), back = Button("Return to passage");
        readonly Button bulk = Button("Bulk resolve..."), bulkSandhi = Button("Bulk apply Sandhi..."), bulkTypos = Button("Bulk apply confirmed typos...");
        readonly Button skippedReview = Button("Manual review (0)");
        string requestedManualId;
        readonly Button resolve = Button("Retry note resolution");
        NoteCandidate current;
        int position = -1, completed;
        bool paused, busy, populating, indexing = true, blocked;
        bool resourcesDisposed;
        string recoveryNotice = "";
        public QueueForm(NoteService service, string progressRoot = null, string reportDirectory = null)
        {
            owner = service;
            progress = new ReviewProgress(progressRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ParatextNoteSuggestions", "Review"), owner.ProjectId);
            try { books = new Queue<int>(owner.QueueBooks()); session = new ReviewSession(progress.DirectoryPath, owner.ProjectId, owner.ProjectName, owner.Reviewer); }
            catch { progress.Dispose(); throw; }
            scope = progress.LoadScope();
            books.Clear(); foreach (int number in owner.QueueBooks().Where(scope.IncludesBook)) books.Enqueue(number);
            reportProjectDirectory = reportDirectory;
            string savedFolder = Path.Combine(progress.DirectoryPath, "report-folder.xml");
            if (reportProjectDirectory == null && File.Exists(savedFolder)) reportProjectDirectory = XElement.Load(savedFolder).Value;
            if (reportProjectDirectory == null || !ProjectReportDirectory.Matches(reportProjectDirectory, owner.ProjectId)) reportProjectDirectory = ProjectReportDirectory.Find(owner.ProjectName, owner.ProjectId);
            Text = "Note review queue - " + owner.ProjectName;
            Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(245, 247, 250); AutoScaleMode = AutoScaleMode.Dpi;
            var area = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(760, area.Width - 30), Math.Min(900, area.Height - 35));
            MinimumSize = new Size(450, 640); StartPosition = FormStartPosition.CenterScreen;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            heading.Font = new Font("Segoe UI Semibold", 10); heading.ForeColor = Color.FromArgb(30, 49, 70);
            heading.AutoSize = true; heading.MaximumSize = new Size(690, 0);
            var top = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 1, RowCount = 3 };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var bookPicker = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            bookPicker.Controls.Add(new Label { Text = "Review starting at", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            bookPicker.Controls.Add(startBook); bookPicker.Controls.Add(startFromBook);
            var scopePicker = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top }; scopePicker.Controls.Add(filters); scopePicker.Controls.Add(bulk); scopePicker.Controls.Add(recovery);
            syncGroup.Items.AddRange(Enum.GetValues(typeof(SyncReferenceGroup)).Cast<object>().ToArray()); syncGroup.SelectedItem = owner.NavigationGroup;
            syncGroup.SelectedIndexChanged += delegate { owner.NavigationGroup = (SyncReferenceGroup)syncGroup.SelectedItem; };
            top.Controls.Add(heading, 0, 0); top.Controls.Add(scopeLabel, 0, 1); top.Controls.Add(scopePicker, 0, 2); root.Controls.Add(top, 0, 0);
            firstBook = progress.StartBook();
            foreach (int number in owner.QueueBooks()) startBook.Items.Add(new BookChoice { Number = number, Code = owner.QueueBookCode(number) });
            if (startBook.Items.Count > 0) startBook.SelectedIndex = Math.Max(0, startBook.Items.Cast<BookChoice>().ToList().FindIndex(b => b.Number == firstBook));
            startFromBook.Click += delegate { Guard(StartFromBook); };
            filters.Click += delegate { Guard(delegate { using (var dialog = new QueueScopeDialog(owner, scope)) if (dialog.ShowDialog(this) == DialogResult.OK) ChangeScope(dialog.Selection()); }); };
            recovery.Click += delegate { Guard(delegate { ChangeScope(new QueueScope { RecoveryOnly = true, Status = NoteStatusFilter.All }); }); };
            bulk.Click += delegate { Guard(BulkResolve); };
            skippedReview.Click += delegate { Guard(ShowSkippedReview); };
            bulkSandhi.Click += delegate { Guard(BulkSandhi); };
            bulkTypos.Click += delegate { Guard(BulkConfirmedTypos); };
            previous.Click += delegate { Guard(delegate { Browse(-1); }); }; next.Click += delegate { Guard(delegate { Browse(1); }); };
            var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true, Padding = new Padding(2) };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            source.ReadOnly = true; source.Dock = DockStyle.Fill;
            Add(fields, "Target region", region); region.Items.AddRange(Enum.GetValues(typeof(ContentRegion)).Cast<object>().ToArray());
            Add(fields, "Original", original); Add(fields, "Replacement", replacement);
            Add(fields, "Comment", commentPreset);
            commentPreset.Items.Add("Choose a phrase (optional)"); commentPreset.Items.AddRange(CommentPresets.Values); commentPreset.SelectedIndex = 0;
            Add(fields, "", comment);
            foreach (Control control in new Control[] { deletion, reviewed, manual }) {
                int row = fields.RowCount++;
                fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                fields.Controls.Add(control, 0, row); fields.SetColumnSpan(control, 2);
            }
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var correctionTab = new TabPage("Review") { BackColor = Color.White, Padding = new Padding(4) }; correctionTab.Controls.Add(fields);
            var sourceTab = new TabPage("Source discussion"); sourceTab.Controls.Add(source);
            tabs.TabPages.Add(correctionTab); tabs.TabPages.Add(sourceTab); root.Controls.Add(tabs);
            // Keep the review header compact; infrequent navigation settings live with tools.
            var tools = new FlowLayoutPanel { AutoScroll = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            tools.Controls.Add(new Label { Text = "Navigation settings", AutoSize = true, Font = new Font("Segoe UI Semibold", 11) });
            tools.Controls.Add(bookPicker);
            var groupPicker = new FlowLayoutPanel { AutoSize = true };
            groupPicker.Controls.Add(new Label { Text = "Scroll group", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }); groupPicker.Controls.Add(syncGroup);
            tools.Controls.Add(groupPicker); tools.Controls.Add(navigationLabel);
            foreach (var b in new[] { bulkSandhi, bulkTypos, skippedReview, retry, resolve, later, viewReport, reportFolder }) tools.Controls.Add(b);
            var export = Button("Export decisions..."); tools.Controls.Add(export);
            var toolsTab = new TabPage("Tools"); toolsTab.Controls.Add(tools); tabs.TabPages.Add(toolsTab);
            var actions = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 3, 0, 0) };
            foreach (var b in new[] { previous, next, back, apply, reject, pause }) actions.Controls.Add(b);
            root.Controls.Add(actions);
            status.AutoSize = true; status.Margin = new Padding(3, 3, 3, 0); root.Controls.Add(status); Controls.Add(root);
            apply.BackColor = Color.FromArgb(27, 97, 150); apply.ForeColor = Color.White; apply.FlatAppearance.BorderSize = 0;
            Action reflow = delegate {
                if (WindowState == FormWindowState.Minimized || root.ClientSize.Width < 300) return;
                int width = root.ClientSize.Width - root.Padding.Horizontal - 6;
                bookPicker.MaximumSize = scopePicker.MaximumSize = new Size(width, 0);
                heading.MaximumSize = scopeLabel.MaximumSize = navigationLabel.MaximumSize = status.MaximumSize = new Size(width, 0);
                foreach (Control control in fields.Controls) {
                    control.Margin = new Padding(2);
                    if (control is CheckBox || control is Button) control.MaximumSize = new Size(Math.Max(180, fields.ClientSize.Width - 10), 0);
                }
            };
            fields.SizeChanged += delegate { reflow(); }; root.SizeChanged += delegate { reflow(); };
            UpdateScopeLabel(); RestoreLayout();
            status.Text = "Indexing notes... Pause or close to stop.";
            heading.Text = owner.ProjectName + " - preparing review queue";
            apply.Click += delegate { Decide("Applied"); }; reject.Click += delegate { Decide("Rejected"); };
            manual.Click += delegate { Decide("Completed manually"); };
            retry.Click += delegate { Retry(); };
            resolve.Click += delegate {
                if (!resolve.Enabled) return;
                Guard(delegate { busy = true; EnableActions(); try { FinishResolution(); } finally { busy = false; EnableActions(); } });
            };
            pause.Click += delegate {
                Guard(delegate {
                    SaveDraft(); paused = !paused; pause.Text = paused ? "Resume" : "Pause";
                    if (indexing) { if (paused) indexTimer.Stop(); else indexTimer.Start(); }
                    else if (!paused && current == null) { position = -1; Next(); }
                    string report = paused ? GenerateReports("Paused", true) : null;
                    status.Text = paused ? "Paused. Position and draft saved." + (report == null ? " No new decisions to report." : " HTML report saved. Use View latest report.") : "Waiting for your decision. You can browse reference windows freely.";
                    EnableActions();
                });
            };
            later.Click += delegate { Guard(delegate { SaveDraft(); Next(); }); };
            back.Click += delegate { Guard(delegate {
                navigationTimer.Stop(); if (current == null) return;
                navigationLabel.Text = "Returning to " + current.DisplayReference + "...";
                returnNavigationPending = true; navigationChecks = 0;
                try { owner.BeginReturnToPassage(current); }
                finally { navigationTimer.Start(); }
            }); };
            export.Click += delegate { Guard(delegate { using (var dialog = new SaveFileDialog { Filter = "CSV review log|*.csv", FileName = "Note-review-decisions.csv" }) if (dialog.ShowDialog(this) == DialogResult.OK) progress.Export(dialog.FileName); }); };
            commentPreset.SelectedIndexChanged += delegate { if (!populating && commentPreset.SelectedIndex > 0) comment.Text = (string)commentPreset.SelectedItem; };
            reportFolder.Click += delegate { Guard(ChooseReportFolder); };
            viewReport.Click += delegate { Guard(delegate {
                string path = GenerateReports("Viewed during review", true) ?? ReviewSession.Latest(progress.DirectoryPath);
                if (String.IsNullOrEmpty(path)) { status.Text = "No report yet. Review a note, then pause or stop the queue."; return; }
                string folder = ProjectReportDirectory.ReportFolder(reportProjectDirectory, owner.ProjectId) + Path.DirectorySeparatorChar;
                if (!Path.GetFullPath(path).StartsWith(folder, StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path) != ".html" || !File.Exists(path)) throw new InvalidOperationException("The latest report is unavailable in this project's Review Queue folder.");
                var viewer = new ReviewReportViewer(path); viewer.Show(this);
            }); };
            foreach (var box in new[] { original, replacement, comment }) box.TextChanged += delegate {
                if (!populating) { if (box != comment) reviewed.Checked = false; draftTimer.Stop(); draftTimer.Start(); } EnableActions();
            };
            region.SelectedIndexChanged += delegate { if (!populating) { reviewed.Checked = false; draftTimer.Start(); } EnableActions(); };
            reviewed.CheckedChanged += delegate { EnableActions(); };
            deletion.CheckedChanged += delegate { if (!populating) { reviewed.Checked = false; draftTimer.Start(); } replacement.Enabled = !deletion.Checked; EnableActions(); };
            draftTimer.Tick += delegate { draftTimer.Stop(); Guard(SaveDraft); };
            indexTimer.Tick += delegate { Guard(IndexBook); };
            navigationTimer.Tick += delegate { Guard(CheckNavigation); };
            Shown += delegate { if (indexing && !paused) indexTimer.Start(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (busy) { e.Cancel = true; return; }
                try { SaveDraft(); SaveLayout(); GenerateReports("Closed", true); } catch (Exception ex) { e.Cancel = true; Fail(ex); }
            };
            EnableActions();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !resourcesDisposed)
            {
                resourcesDisposed = true; indexTimer.Dispose(); draftTimer.Dispose(); navigationTimer.Dispose();
                if (progress != null) progress.Dispose();
            }
            base.Dispose(disposing);
        }
        static TextBox Box(int height) { return new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Height = height, Dock = DockStyle.Top }; }
        static Button Button(string text) { var button = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlatStyle = FlatStyle.Flat, BackColor = Color.White, Padding = new Padding(4, 1, 4, 1), Margin = new Padding(2), Cursor = Cursors.Hand }; button.FlatAppearance.BorderColor = Color.FromArgb(200, 208, 217); return button; }
        static void Add(TableLayoutPanel panel, string label, Control control)
        {
            int row = panel.RowCount++;
            var box = control as TextBox;
            panel.RowStyles.Add(new RowStyle(box == null ? SizeType.AutoSize : SizeType.Percent, box == null ? 0 : 33.33f));
            panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Left, ForeColor = Color.FromArgb(55, 70, 85), Padding = new Padding(0, 3, 0, 0) }, 0, row);
            if (box != null) { box.MinimumSize = new Size(0, 48); box.Dock = DockStyle.Fill; }
            panel.Controls.Add(control, 1, row);
        }
        void Guard(Action action) { try { action(); } catch (Exception ex) { Fail(ex); } }
        void UpdateScopeLabel()
        {
            scopeLabel.Text = "Scope: " + scope.Describe(owner.QueueBookCode);
            session.SetScope(scopeLabel.Text, items.Count, completed);
        }
        void ChangeScope(QueueScope selection)
        {
            if (busy) return;
            SaveDraft(); progress.SaveScope(selection);
            indexTimer.Stop(); navigationTimer.Stop(); draftTimer.Stop(); scope = selection;
            items.Clear(); duplicateIds.Clear(); seen.Clear(); books.Clear(); current = null; position = -1; completed = 0; recoveryNotice = "";
            foreach (int number in owner.QueueBooks().Where(scope.IncludesBook)) books.Enqueue(number);
            paused = false; pause.Text = "Pause"; indexing = true; UpdateScopeLabel();
            heading.Text = "Indexing selected scope, one book at a time...";
            source.Clear(); original.Clear(); replacement.Clear(); comment.Clear(); navigationLabel.Text = "";
            EnableActions(); indexTimer.Start();
        }
        void SaveLayout()
        {
            var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            ReviewProgress.AtomicWrite(Path.Combine(progress.DirectoryPath, "layout.xml"), new XElement("layout", new XAttribute("x", bounds.X), new XAttribute("y", bounds.Y),
                new XAttribute("width", bounds.Width), new XAttribute("height", bounds.Height), new XAttribute("maximized", WindowState == FormWindowState.Maximized), new XAttribute("group", owner.NavigationGroup)));
        }
        void RestoreLayout()
        {
            string path = Path.Combine(progress.DirectoryPath, "layout.xml"); if (!File.Exists(path)) return;
            var e = XElement.Load(path); var saved = new Rectangle((int)e.Attribute("x"), (int)e.Attribute("y"), (int)e.Attribute("width"), (int)e.Attribute("height"));
            var area = Screen.FromRectangle(saved).WorkingArea;
            int width = Math.Min(area.Width, Math.Max(MinimumSize.Width, saved.Width)), height = Math.Min(area.Height, Math.Max(MinimumSize.Height, saved.Height));
            StartPosition = FormStartPosition.Manual; Bounds = new Rectangle(Math.Max(area.Left, Math.Min(saved.X, area.Right - width)), Math.Max(area.Top, Math.Min(saved.Y, area.Bottom - height)), width, height);
            SyncReferenceGroup group; if (Enum.TryParse((string)e.Attribute("group"), out group) && Enum.IsDefined(typeof(SyncReferenceGroup), group)) syncGroup.SelectedItem = group;
            if ((bool?)e.Attribute("maximized") == true) WindowState = FormWindowState.Maximized;
        }
        void NavigateCurrent()
        {
            navigationTimer.Stop(); returnNavigationPending = false; if (current == null) return;
            navigationLabel.Text = "Opening " + current.DisplayReference + " in " + owner.ProjectName + ". Verse navigation; anchor: " + NoteParser.Preview(current.AnchorText ?? "", 60);
            owner.Navigate(current); navigationChecks = 0; navigationTimer.Start();
        }
        void CheckNavigation()
        {
            if (current == null) { navigationTimer.Stop(); return; }
            if (returnNavigationPending) {
                returnNavigationPending = false; owner.Navigate(current);
                // Allow the host to activate/load the editor before confirming its reference.
                return;
            }
            // Re-send the scroll request after asynchronous project-window activation.
            if (navigationChecks == 0) owner.ScrollProjectTo(current);
            bool visible = owner.NavigationVisible(current);
            if (visible || ++navigationChecks >= 12)
            {
                navigationTimer.Stop(); navigationLabel.Text = (visible ? "Project window reference confirmed: " : "Navigation requested; window reference not confirmed: ") + current.DisplayReference +
                    ". Verse-only; anchored text: " + NoteParser.Preview(current.AnchorText ?? "", 60) + ". Scroll group: " + owner.NavigationGroup + ".";
            }
        }
        void Fail(Exception ex)
        {
            indexTimer.Stop(); draftTimer.Stop(); paused = true; pause.Text = "Resume"; owner.ReportFailure(ex);
            status.Text = ex.Message + " Review paused.";
            try { GenerateReports("Stopped after error", false); } catch (Exception reportError) { owner.ReportFailure(reportError); status.Text += " Report pending; local audit is saved. Use Report folder / View latest report to retry."; }
            EnableActions();
        }
        void ChooseReportFolder()
        {
            using (var dialog = new FolderBrowserDialog { Description = "Choose this Paratext project's folder (the one containing Settings.xml). Reports go in its Review Queue subfolder.", SelectedPath = reportProjectDirectory ?? "" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                ProjectReportDirectory.ReportFolder(dialog.SelectedPath, owner.ProjectId);
                reportProjectDirectory = Path.GetFullPath(dialog.SelectedPath);
                ReviewProgress.AtomicWrite(Path.Combine(progress.DirectoryPath, "report-folder.xml"), new XElement("directory", reportProjectDirectory));
                status.Text = "Reports will be saved under " + Path.Combine(reportProjectDirectory, "Review Queue");
            }
        }
        string GenerateReports(string reason, bool prompt)
        {
            if (session.Records().Count == 0 && session.WarningCount == 0 && !ReviewSession.NeedsRecovery(progress.DirectoryPath, session.Id)) return null;
            if (reportProjectDirectory == null || !ProjectReportDirectory.Matches(reportProjectDirectory, owner.ProjectId))
            {
                if (prompt) ChooseReportFolder();
                if (reportProjectDirectory == null || !ProjectReportDirectory.Matches(reportProjectDirectory, owner.ProjectId)) throw new InvalidOperationException("Choose this project's Report folder to finish its HTML report. Local decisions remain saved.");
            }
            ReviewSession.Recover(progress.DirectoryPath, session.Id, reportProjectDirectory, owner.ProjectId);
            return session.WriteReport(reportProjectDirectory, items.Count, completed, reason) ?? ReviewSession.Latest(progress.DirectoryPath);
        }
        void IndexBook()
        {
            if (books.Count > 0)
            {
                int book = books.Peek();
                foreach (var c in owner.ReadQueueBook(book, true))
                {
                    var saved = progress.Get(c.StableId);
                    bool outstanding = saved != null && (saved.ResolutionPending || saved.Outcome == "Pending apply");
                    if (c.Unreadable) session.AddWarning(c.DisplayReference + " - unreadable note discussion in an indexed book (" + (c.IsResolved ? "already resolved; may be outside status filter" : "unresolved; handle manually") + "). " + c.NoteText);
                    if (scope.RecoveryOnly ? !outstanding : (!scope.IncludesStatus(c.IsResolved) || !scope.IncludesCandidate(c))) continue;
                    if (!seen.Add(c.StableId)) duplicateIds.Add(c.StableId);
                    items.Add(c);
                }
                books.Dequeue();
                heading.Text = owner.ProjectName + " - indexed " + items.Count + " proposals; " + books.Count + " books remaining";
                return;
            }
            indexTimer.Stop(); indexing = false;
            int missing = progress.Outstanding().Count(r => !seen.Contains(r.Id));
            if (missing > 0) recoveryNotice = "\r\n" + missing + " interrupted decision(s) are outside this scope or have missing/changed sources. Use Recovery or export decisions and inspect Paratext history/notes.";
            recovery.Text = "Recovery (" + progress.Outstanding().Count() + ")";
            if (session.WarningCount > 0) recoveryNotice += "\r\n" + session.WarningCount + " unreadable discussions (see report).";
            SortItems();
            completed = items.Count(c => { var r = progress.Get(c.StableId); return r != null && r.Completed; });
            UpdateScopeLabel();
            if (requestedManualId != null) {
                string requested = requestedManualId; requestedManualId = null;
                int target = items.FindIndex(c => c.StableId == requested);
                if (target >= 0) { position = target; LoadCurrent(); return; }
                Next();
                status.Text = "The skipped proposal's source changed or is unavailable. Its saved discussion and reason remain in Manual review; inspect the original note in Paratext.";
                return;
            }
            var cursor = progress.Cursor(scope);
            int resume = cursor == null ? -1 : items.FindIndex(c => c.StableId == cursor.Id);
            var record = cursor == null ? null : progress.Get(cursor.Id);
            if (resume >= 0 && (record == null || !record.Completed || record.ResolutionPending || scope.Status != NoteStatusFilter.Unresolved)) { position = resume; LoadCurrent(); }
            else Next();
        }
        void SortItems()
        {
            items.Sort((a, b) => {
                int n = (a.Book < firstBook ? 1 : 0).CompareTo(b.Book < firstBook ? 1 : 0);
                if (n == 0) n = a.Book.CompareTo(b.Book);
                if (n == 0) n = a.Chapter.CompareTo(b.Chapter);
                if (n == 0) n = a.FirstVerse.CompareTo(b.FirstVerse);
                return n == 0 ? StringComparer.Ordinal.Compare(a.StableId, b.StableId) : n;
            });
        }
        void StartFromBook()
        {
            if (busy || indexing || startBook.SelectedItem == null) return;
            var chosen = (BookChoice)startBook.SelectedItem;
            if (!items.Any(c => c.Book == chosen.Number && (progress.Get(c.StableId) == null || !progress.Get(c.StableId).Completed || progress.Get(c.StableId).ResolutionPending)))
            { status.Text = "No unfinished queued notes in " + chosen.Code + ". Choose another book."; return; }
            SaveDraft();
            progress.SaveStartBook(chosen.Number); firstBook = chosen.Number; SortItems();
            paused = false; pause.Text = "Pause"; position = -1; Next();
        }
        void Next()
        {
            draftTimer.Stop(); current = null; blocked = false;
            while (++position < items.Count)
            {
                var r = progress.Get(items[position].StableId);
                if (scope.Status == NoteStatusFilter.Unresolved && !scope.RecoveryOnly && r != null && r.Completed && !r.ResolutionPending) continue;
                current = items[position]; break;
            }
            if (current == null)
            {
                heading.Text = owner.ProjectName + " - " + completed + " / " + items.Count + " reviewed" + recoveryNotice;
                source.Text = "End of this pass. Resume to revisit items left for later, or close and reopen to scan for new notes.";
                original.Clear(); replacement.Clear(); comment.Clear();
                paused = true; pause.Text = "Resume"; EnableActions();
                string report = GenerateReports("End of pass", true);
                status.Text = "End of this pass. Resume to revisit deferred notes." + (report == null ? "" : " HTML report saved; use View latest report."); return;
            }
            LoadCurrent();
        }
        void Browse(int direction)
        {
            if (busy || indexing || (direction != -1 && direction != 1)) return;
            int target = position + direction;
            if (current == null && direction == -1) target = items.Count - 1;
            if (target < 0 || target >= items.Count) return;
            SaveDraft(); position = target; paused = false; pause.Text = "Pause"; LoadCurrent();
        }
        void LoadCurrent()
        {
            navigationTimer.Stop(); draftTimer.Stop(); blocked = false; current = items[position];
            try { current = owner.ReloadQueueItem(current, true); }
            catch (Exception ex) { blocked = true; status.Text = ex.Message; }
            if (duplicateIds.Contains(current.StableId)) blocked = true;
            if (current.Unreadable) blocked = true;
            var previous = progress.Get(current.StableId);
            var draft = progress.Cursor(scope);
            populating = true;
            source.Text = current.NoteText; original.Text = current.Proposal.Original; replacement.Text = current.Proposal.Suggested;
            comment.Text = ""; commentPreset.SelectedIndex = 0; region.SelectedItem = current.Proposal.Region; deletion.Checked = false; reviewed.Checked = false;
            var restore = previous != null && (previous.Outcome == "Pending apply" || previous.Completed) ? previous : (draft != null && draft.Id == current.StableId ? draft : previous);
            if (restore != null)
            {
                original.Text = restore.Original; replacement.Text = restore.Replacement; comment.Text = restore.Comment;
                ContentRegion parsed; if (Enum.TryParse(restore.Region, out parsed)) region.SelectedItem = parsed;
                deletion.Checked = restore.Outcome == "Draft deletion";
            }
            populating = false;
            try { loadedContext = owner.CapturePassage(current, (ContentRegion)region.SelectedItem); } catch { loadedContext = ""; }
            if (restore != null && restore.BeforeContext.Length > 0) loadedContext = restore.BeforeContext;
            heading.Text = current.DisplayReference + " | " + current.Proposal.Pass + " | " + current.Proposal.Category + "\r\n" + (position + 1) + " / " + items.Count + " | " + completed + " reviewed | " + (current.IsResolved ? "Resolved" : "Unresolved") + (previous == null ? "" : " | " + previous.Outcome) + recoveryNotice;
            if (!blocked) status.Text = ResolutionPending() ? "Decision saved locally. Retry note resolution to finish; the Scripture correction will not be repeated." : Pending() ? "An earlier apply did not finish recording. Inspect Paratext history and the passage, then confirm completed manually or confirm no edit before retrying." : "Review the correction, then apply or reject.";
            else status.Text = "Source changed, unavailable, or duplicate identity. Automatic Apply is blocked. Inspect in Paratext; leave for later or record a commented manual decision.";
            if (current.Unreadable) status.Text = "Paratext cannot read this discussion. Decisions are blocked for this note; inspect/fix it in Paratext or Leave for later. Other notes can continue.";
            if (previous != null && previous.Completed) source.Text += "\r\n\r\nSAVED LOCAL DECISION: " + previous.Outcome + "\r\n" + previous.Comment + "\r\n" + previous.Evidence + "\r\nBefore:\r\n" + previous.BeforeContext + "\r\nAfter:\r\n" + previous.AfterContext;
            if (current.IsResolved && !ResolutionPending()) status.Text = "Paratext resolved note - inspection only. Navigation never repeats its decision or Scripture edit.";
            var skipped = progress.Skipped().FirstOrDefault(r => r.Id == current.StableId);
            if (skipped != null) status.Text += "\r\nSaved bulk skip: " + skipped.Evidence;
            SaveDraft(); EnableActions();
            NavigateCurrent();
        }
        bool Pending() { var r = current == null ? null : progress.Get(current.StableId); return r != null && r.Outcome == "Pending apply"; }
        bool ResolutionPending() { var r = current == null ? null : progress.Get(current.StableId); return r != null && r.ResolutionPending; }
        ReviewRecord Record(string outcome)
        {
            return new ReviewRecord { Id = current.StableId, Outcome = outcome, Reference = current.DisplayReference, Category = current.Proposal.Category,
                Original = original.Text, Replacement = deletion.Checked ? "" : replacement.Text, Comment = comment.Text, Discussion = current.DiscussionKey, BeforeContext = loadedContext, Region = region.SelectedItem == null ? "" : region.SelectedItem.ToString(), QueueScope = scope.Describe(owner.QueueBookCode) };
        }
        void SaveDraft()
        {
            if (current == null || populating || indexing) return;
            var draft = Record(deletion.Checked ? "Draft deletion" : "Draft");
            var prior = progress.Get(current.StableId);
            // Keep unresolved write intent and completed decisions intact.
            if (prior == null || (!prior.Completed && prior.Outcome != "Pending apply")) progress.Save(draft);
            progress.SaveCursor(prior != null && prior.Completed ? prior : draft, scope);
        }
        void EnableActions()
        {
            bool ready = current != null && !paused && !busy && !indexing;
            bool editable = ready && !current.IsResolved;
            bool pending = current != null && Pending();
            bool decisionSaved = current != null && (progress.Get(current.StableId) ?? new ReviewRecord()).Completed;
            bool duplicate = current != null && duplicateIds.Contains(current.StableId);
            apply.Enabled = editable && !decisionSaved && !pending && !blocked && reviewed.Checked && current.Proposal.Route != ProposalRoute.NoChange && (replacement.Text.Length > 0 || deletion.Checked);
            reject.Enabled = editable && !blocked && !current.Unreadable && !decisionSaved && !pending && !duplicate && comment.Text.Trim().Length > 0;
            manual.Enabled = editable && !blocked && !current.Unreadable && !decisionSaved && !duplicate && reviewed.Checked && comment.Text.Trim().Length > 0;
            retry.Visible = pending; retry.Enabled = ready && pending && comment.Text.Trim().Length > 0;
            resolve.Visible = ResolutionPending(); resolve.Enabled = ready && ResolutionPending() && !duplicate;
            later.Enabled = ready; back.Enabled = current != null && !busy;
            pause.Enabled = !busy;
            startFromBook.Enabled = !busy && !indexing && startBook.SelectedItem != null;
            startBook.Enabled = !busy;
            previous.Enabled = !busy && !indexing && items.Count > 0 && (current == null || position > 0);
            next.Enabled = !busy && !indexing && position >= 0 && position < items.Count - 1;
            bulk.Enabled = !busy && !indexing && !paused && !scope.RecoveryOnly && items.Any(c => !c.IsResolved && !c.Unreadable && !duplicateIds.Contains(c.StableId) && (progress.Get(c.StableId) == null || (!progress.Get(c.StableId).Completed && progress.Get(c.StableId).Outcome != "Pending apply")));
            bulkSandhi.Enabled = bulk.Enabled;
            bulkTypos.Enabled = bulk.Enabled;
            skippedReview.Enabled = !busy && !indexing;
            skippedReview.Text = "Manual review (" + progress.Skipped().Count + ")";
            filters.Enabled = recovery.Enabled = syncGroup.Enabled = !busy;
            viewReport.Enabled = reportFolder.Enabled = !busy;
            commentPreset.Enabled = editable && !decisionSaved;
            original.Enabled = replacement.Enabled = comment.Enabled = region.Enabled = reviewed.Enabled = deletion.Enabled = editable && !decisionSaved;
            replacement.Enabled = editable && !decisionSaved && !deletion.Checked;
        }
        void Decide(string outcome)
        {
            if (busy || paused || current == null) return;
            Guard(delegate {
                if (outcome == "Applied" && (!apply.Enabled || region.SelectedItem == null)) return;
                if (outcome == "Rejected" && !reject.Enabled) return;
                if (outcome == "Completed manually" && !manual.Enabled) return;
                busy = true; EnableActions();
                try
                {
                    var record = Record(outcome);
                    record.SessionId = session.Id; record.AuditId = Guid.NewGuid().ToString("N"); record.Reviewer = owner.Reviewer;
                    record.DecisionUtc = record.ActionUtc = DateTime.UtcNow.ToString("O"); record.Actor = record.Reviewer; record.SourceNote = current.NoteText;
                    if (outcome == "Applied")
                    {
                        // Persist intent first: any uncertain failure must stop, never blindly retry.
                        Action<PassageEvidence> evidence = delegate(PassageEvidence value) { record.BeforeContext = value.Before; record.AfterContext = value.After; record.ContextScope = value.Scope; record.Evidence = "Expected result captured before submission; saved USFM not yet verified."; };
                        Action beforeSubmit = delegate {
                            if (owner.Reviewer != record.Reviewer) throw new InvalidOperationException("The Paratext user changed during review. Reopen this item before applying.");
                            var intent = ReviewRecord.FromXml(record.ToXml()); intent.Outcome = "Pending apply";
                            progress.Save(intent); session.Track(intent);
                        };
                        var service = owner.ForQueueItem(current);
                        if (current.Proposal.Route == ProposalRoute.Structure)
                        {
                            if (original.Text != current.Proposal.Original || replacement.Text != current.Proposal.Suggested || deletion.Checked || (ContentRegion)region.SelectedItem != current.Proposal.Region)
                                throw new InvalidOperationException("Structural review requires the unchanged source proposal and region.");
                            service.ApplyMarkerRename(current, beforeSubmit, evidence);
                        }
                        else service.Apply(current, original.Text, record.Replacement, (ContentRegion)region.SelectedItem, true, deletion.Checked, beforeSubmit, evidence);
                        record.Evidence = "Verified saved USFM after the queue correction. Scripture write by Paratext user: " + record.Reviewer;
                    }
                    else
                    {
                        string snapshot = owner.CapturePassage(current, (ContentRegion)region.SelectedItem);
                        record.BeforeContext = outcome == "Rejected" ? snapshot : loadedContext;
                        record.AfterContext = snapshot; record.ContextScope = "Full passage context observed at decision time (USFM)";
                        record.Evidence = outcome == "Rejected" ? "No Scripture edit by the queue. Before and after are the same captured passage." : "Manual completion acknowledged by the reviewer. Before was observed when this item loaded; after at decision time. The actual editor of manual changes is not verified.";
                    }
                    record.Resolution = "Pending";
                    var previousAttempt = progress.Get(current.StableId);
                    Action saveDecision = delegate { progress.Save(record); session.Track(record); };
                    if (outcome == "Applied") saveDecision(); else owner.ValidateQueueDecision(current, saveDecision);
                    if (previousAttempt != null && previousAttempt.Outcome == "Pending apply" && previousAttempt.AuditId.Length > 0)
                    {
                        var reconciled = ReviewRecord.FromXml(previousAttempt.ToXml()); reconciled.Outcome = "Reconciled manually";
                        reconciled.AfterContext = record.AfterContext; reconciled.Actor = record.Reviewer; reconciled.ActionUtc = record.ActionUtc;
                        reconciled.Evidence = "Reviewer manually acknowledged completion; after context observed at confirmation. Linked audit: " + record.AuditId;
                        ReviewSession.WriteEntry(progress.DirectoryPath, reconciled.SessionId, reconciled);
                    }
                    completed++; FinishResolution();
                }
                finally { busy = false; EnableActions(); }
            });
        }
        void FinishResolution()
        {
            FinishRecord(current); Next();
        }
        void FinishRecord(NoteCandidate candidate)
        {
            var record = progress.Get(candidate.StableId);
            if (record == null || !record.ResolutionPending) throw new InvalidOperationException("A saved decision is required before resolving the note.");
            List<string> ids;
            bool resolved = owner.ResolveReviewedThread(candidate, record.Discussion, delegate(string id) { var r = progress.Get(id); return r != null && r.Completed; }, out ids, record, progress.Get);
            if (resolved)
                foreach (string id in ids.Where(id => id != candidate.StableId))
                {
                    var peer = progress.Get(id); if (peer == null || !peer.Completed) continue;
                    var updated = ReviewRecord.FromXml(peer.ToXml()); updated.Resolution = "Resolved"; progress.Save(updated);
                }
            var finished = ReviewRecord.FromXml(record.ToXml());
            if (finished.AuditId.Length == 0)
            {
                finished.SessionId = session.Id; finished.AuditId = Guid.NewGuid().ToString("N");
                finished.Evidence = "This session only retried note resolution. The decision is from an older version; its original reviewer and full before/after snapshots were not recorded.";
            }
            finished.Resolution = resolved ? "Resolved" : "Waiting for remaining proposals";
            finished.Actor = owner.Reviewer; finished.ActionUtc = DateTime.UtcNow.ToString("O");
            progress.Save(finished); session.Track(finished);
        }
        void BulkResolve()
        {
            if (busy || paused || indexing || scope.RecoveryOnly) return;
            SaveDraft();
            var candidates = items.Where(c => !c.IsResolved && !c.Unreadable && !duplicateIds.Contains(c.StableId) &&
                (progress.Get(c.StableId) == null || (!progress.Get(c.StableId).Completed && progress.Get(c.StableId).Outcome != "Pending apply"))).ToList();
            using (var dialog = new BulkResolveDialog(candidates))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (dialog.PreviewScriptureEdits) PreviewSelectedCorrections(dialog.SelectedItems, dialog.DecisionComment);
                else RunBulk(dialog.SelectedItems, dialog.DecisionComment);
            }
        }
        void ShowSkippedReview()
        {
            if (busy || indexing) return;
            using (var dialog = new SkippedReviewDialog(progress.Skipped(), progress.DismissSkipped)) {
                if (dialog.ShowDialog(this) == DialogResult.OK) OpenSkippedReview(dialog.SelectedId);
            }
            EnableActions();
        }
        void OpenSkippedReview(string id)
        {
            if (busy || indexing || String.IsNullOrEmpty(id)) return;
            requestedManualId = id;
            ChangeScope(new QueueScope { AllBooks = true, Status = NoteStatusFilter.All });
        }
        void RememberSkipped(NoteCandidate item, string reason)
        {
            progress.SaveSkipped(item, reason);
            skippedReview.Text = "Manual review (" + progress.Skipped().Count + ")";
        }
        void PreviewSelectedCorrections(List<NoteCandidate> selected, string decisionComment)
        {
            if (busy || paused || indexing || scope.RecoveryOnly || String.IsNullOrWhiteSpace(decisionComment)) return;
            var eligible = new List<NoteCandidate>(); var previews = new Dictionary<string, PassageEvidence>(); int skipped = 0;
            foreach (var item in selected) {
                try {
                    var prior = progress.Get(item.StableId);
                    if (!items.Contains(item) || duplicateIds.Contains(item.StableId) || (prior != null && (prior.Completed || prior.Outcome == "Pending apply")))
                        throw new InvalidOperationException("Target is unavailable, ambiguous or already decided; use Recovery if needed.");
                    previews.Add(item.StableId, owner.ForQueueItem(item).PreviewBulkCorrection(item, item.Proposal.Category, true));
                    eligible.Add(item);
                }
                catch (Exception ex) {
                    skipped++; RememberSkipped(item, ex.Message);
                    session.AddWarning("Selected bulk correction skipped at preview: " + item.DisplayReference + ": " + ex.Message);
                }
            }
            if (eligible.Count == 0) {
                status.Text = "No selected corrections can be safely previewed. " + skipped + " skipped and left unresolved. Use Manual review for saved proposals and reasons.";
                return;
            }
            ShowCorrectionPreview(eligible, previews, skipped, null, "selected corrections", decisionComment);
        }
        void BulkSandhi()
        { BulkCorrections("Sandhi / word-joining", "Sandhi"); }
        void BulkConfirmedTypos()
        { BulkCorrections("Confirmed typo", "confirmed typos"); }
        void BulkCorrections(string category, string label)
        {
            if (busy || paused || indexing || scope.RecoveryOnly) return;
            SaveDraft();
            var previews = new Dictionary<string, PassageEvidence>();
            var eligible = new List<NoteCandidate>(); var skipped = new List<string>();
            foreach (var item in items.Where(c => c.Proposal.Category == category && !c.IsResolved))
            {
                var prior = progress.Get(item.StableId);
                if (prior != null && prior.Completed) continue;
                status.Text = "Checking " + label + " - " + item.DisplayReference + "; " + eligible.Count + " eligible, " + skipped.Count + " skipped."; status.Refresh();
                try {
                    if (duplicateIds.Contains(item.StableId)) throw new InvalidOperationException("Duplicate proposal identity; review individually.");
                    if (prior != null && prior.Outcome == "Pending apply") throw new InvalidOperationException("Interrupted apply requires Recovery.");
                    previews.Add(item.StableId, owner.ForQueueItem(item).PreviewBulkCorrection(item, category)); eligible.Add(item);
                }
                catch (Exception ex) {
                    string detail = item.DisplayReference + ": " + ex.Message;
                    RememberSkipped(item, ex.Message);
                    skipped.Add(detail); session.AddWarning("Bulk " + label + " skipped at preview: " + detail);
                    status.Text = "Skipped " + detail + " Continuing to next proposal."; status.Refresh();
                }
            }
            if (eligible.Count == 0) {
                string summary = "Bulk " + label + " complete: 0 resolved; " + skipped.Count + " not resolved (skipped). No Scripture edits.";
                session.AddWarning(summary); status.Text = summary + " Use Manual review for saved proposals and reasons."; return;
            }
            ShowCorrectionPreview(eligible, previews, skipped.Count, category, label);
        }
        void ShowCorrectionPreview(List<NoteCandidate> eligible, Dictionary<string, PassageEvidence> previews, int skipped, string category, string label, string decisionComment = null)
        {
            // Modeless so the reviewer can read the actual Paratext editor while the preview is open.
            // Freeze queue actions until it closes; navigation itself never writes Scripture.
            var dialog = new BulkResolveDialog(eligible, previews, skipped, label, owner.Navigate);
            if (decisionComment != null) dialog.SetDecisionComment(decisionComment);
            busy = true; EnableActions();
            dialog.FormClosed += delegate {
                var selected = dialog.SelectedItems; string decision = dialog.DecisionComment;
                bool approved = dialog.DialogResult == DialogResult.OK;
                busy = false;
                if (IsDisposed || Disposing) return;
                EnableActions();
                if (approved) Guard(delegate { RunCorrectionBatch(selected, decision, previews, skipped, category, label); });
            };
            try { dialog.Show(this); }
            catch { busy = false; dialog.Dispose(); EnableActions(); throw; }
        }
        void RunBulkSandhi(List<NoteCandidate> selectedItems, string decisionComment, Dictionary<string, PassageEvidence> previews)
        { RunSandhiBatch(selectedItems, decisionComment, previews, 0); }
        void RunSandhiBatch(List<NoteCandidate> selectedItems, string decisionComment, Dictionary<string, PassageEvidence> previews, int previewSkipped)
        { RunCorrectionBatch(selectedItems, decisionComment, previews, previewSkipped, "Sandhi / word-joining", "Sandhi"); }
        void RunBulkConfirmedTypos(List<NoteCandidate> selectedItems, string decisionComment, Dictionary<string, PassageEvidence> previews)
        { RunCorrectionBatch(selectedItems, decisionComment, previews, 0, "Confirmed typo", "confirmed typos"); }
        void RunCorrectionBatch(List<NoteCandidate> selectedItems, string decisionComment, Dictionary<string, PassageEvidence> previews, int previewSkipped, string category, string label)
        {
            if (busy || paused || indexing || scope.RecoveryOnly || String.IsNullOrWhiteSpace(decisionComment)) return;
            busy = true; EnableActions(); int applied = 0, processed = 0, skipped = 0; bool stopped = false;
            try
            {
                foreach (var item in selectedItems)
                {
                    bool submitted = false, decisionDurable = false;
                    try
                    {
                        status.Text = "Applying " + label + " " + (++processed) + " / " + selectedItems.Count + " - " + item.DisplayReference; status.Refresh();
                        var prior = progress.Get(item.StableId);
                        if (!items.Contains(item) || duplicateIds.Contains(item.StableId) || (prior != null && (prior.Completed || prior.Outcome == "Pending apply")))
                            throw new InvalidOperationException("Target is unavailable, ambiguous, or already decided; use Recovery if needed.");
                        PassageEvidence preview;
                        if (!previews.TryGetValue(item.StableId, out preview)) throw new InvalidOperationException("A reviewed preview is required.");
                        var service = owner.ForQueueItem(item);
                        bool reviewedSelection = category == null;
                        var now = service.PreviewBulkCorrection(item, category ?? item.Proposal.Category, reviewedSelection);
                        if (now.Before != preview.Before || now.After != preview.After) throw new InvalidOperationException("Passage changed since preview; review again.");
                        var record = new ReviewRecord { Id = item.StableId, Outcome = "Applied", Reference = item.DisplayReference,
                            Category = item.Proposal.Category, Original = item.Proposal.Original, Replacement = item.Proposal.Suggested,
                            Comment = decisionComment.Trim(), Region = "Body", Discussion = item.DiscussionKey,
                            SessionId = session.Id, AuditId = Guid.NewGuid().ToString("N"), Reviewer = owner.Reviewer, Actor = owner.Reviewer,
                            DecisionUtc = DateTime.UtcNow.ToString("O"), ActionUtc = DateTime.UtcNow.ToString("O"), Resolution = "Pending",
                            QueueScope = scope.Describe(owner.QueueBookCode), SourceNote = item.NoteText };
                        service.Apply(item, record.Original, record.Replacement, ContentRegion.Body, reviewedSelection, false, delegate {
                            if (owner.Reviewer != record.Reviewer) throw new InvalidOperationException("Paratext user changed; review again.");
                            var intent = ReviewRecord.FromXml(record.ToXml()); intent.Outcome = "Pending apply";
                            progress.Save(intent); submitted = true; session.Track(intent);
                        }, delegate(PassageEvidence evidence) {
                            if (evidence.Before != preview.Before || evidence.After != preview.After) throw new InvalidOperationException("Passage changed under lock; no edit submitted.");
                            record.BeforeContext = evidence.Before; record.AfterContext = evidence.After; record.ContextScope = evidence.Scope;
                            record.Evidence = "Expected bulk " + label + " result captured; saved USFM not yet verified.";
                        }, reviewedSelection);
                        applied++;
                        record.Evidence = "Verified saved USFM after reviewed bulk " + label + " correction. Scripture write by Paratext user: " + record.Reviewer;
                        progress.Save(record); session.Track(record); decisionDurable = true; completed++;
                        FinishRecord(item);
                    }
                    catch (Exception ex)
                    {
                        string detail = item.DisplayReference + ": " + ex.Message;
                        session.AddWarning("Bulk " + label + " item failed: " + detail);
                        status.Text = detail + " Continuing to next proposal."; status.Refresh();
                        if (submitted && !decisionDurable) {
                            stopped = true;
                            session.AddWarning("Bulk " + label + " stopped after an uncertain save/audit. Remaining selections were not attempted; inspect Recovery.");
                            foreach (var remaining in selectedItems.Skip(processed)) RememberSkipped(remaining, "Not attempted: batch stopped after an uncertain save. Review this proposal again; reconcile the interrupted item in Recovery first.");
                            break;
                        }
                        if (!submitted) { skipped++; RememberSkipped(item, ex.Message); }
                    }
                }
                int resolved = selectedItems.Count(c => { var r = progress.Get(c.StableId); return r != null && r.Resolution == "Resolved"; });
                int notResolved = selectedItems.Count - resolved + previewSkipped;
                string summary = "Bulk " + label + " " + (stopped ? "stopped" : "complete") + ": " + resolved + " resolved; " + notResolved + " not resolved. " +
                    applied + " Scripture corrections verified; " + (previewSkipped + skipped) + " skipped; " + (selectedItems.Count - processed) + " not attempted.";
                session.AddWarning(summary);
                recovery.Text = "Recovery (" + progress.Outstanding().Count() + ")";
                UpdateScopeLabel(); position = -1; Next();
                status.Text = summary + " " + progress.Outstanding().Count() + " pending recovery. Use Manual review for skipped/unattempted proposals and reasons. Unselected eligible proposals are outside these totals.";
            }
            finally { busy = false; EnableActions(); }
        }
        void RunBulk(List<NoteCandidate> selectedItems, string decisionComment)
        {
            if (busy || indexing || String.IsNullOrWhiteSpace(decisionComment)) return;
            busy = true; EnableActions(); int success = 0; var failures = new List<string>();
            try
            {
                int processed = 0;
                foreach (var item in selectedItems)
                {
                    status.Text = "Saving bulk decision " + (++processed) + " / " + selectedItems.Count + " - " + item.DisplayReference; status.Refresh();
                    try
                    {
                        if (!items.Contains(item) || duplicateIds.Contains(item.StableId)) throw new InvalidOperationException("Target is outside the queue or ambiguous.");
                        var prior = progress.Get(item.StableId);
                        if (prior != null && (prior.Completed || prior.Outcome == "Pending apply")) throw new InvalidOperationException("A decision or interrupted apply already exists; use Recovery.");
                        var fresh = owner.ReloadQueueItem(item, true);
                        if (fresh.IsResolved || fresh.Unreadable || fresh.Key != item.Key) throw new InvalidOperationException("Source changed; refresh and review it again.");
                        var record = new ReviewRecord { Id = item.StableId, Outcome = "Completed manually", Reference = item.DisplayReference,
                            Category = item.Proposal.Category, Original = item.Proposal.Original, Replacement = item.Proposal.Suggested,
                            Comment = decisionComment.Trim(), Region = item.Proposal.Region.ToString(), Discussion = item.DiscussionKey,
                            SessionId = session.Id, AuditId = Guid.NewGuid().ToString("N"), Reviewer = owner.Reviewer, Actor = owner.Reviewer,
                            DecisionUtc = DateTime.UtcNow.ToString("O"), ActionUtc = DateTime.UtcNow.ToString("O"), Resolution = "Pending",
                            QueueScope = scope.Describe(owner.QueueBookCode), SourceNote = item.NoteText,
                            Evidence = "Bulk review acknowledged by reviewer. No Scripture edit by the queue; no claim that a proposed correction was applied." };
                        record.BeforeContext = record.AfterContext = owner.CapturePassage(item, item.Proposal.Region);
                        record.ContextScope = "Passage observed at bulk decision time (USFM)";
                        owner.ValidateQueueDecision(item, delegate { progress.Save(record); session.Track(record); });
                        completed++; FinishRecord(item); success++;
                    }
                    catch (Exception ex) { failures.Add(item.DisplayReference + ": " + ex.Message); }
                }
                recovery.Text = "Recovery (" + progress.Outstanding().Count() + ")";
                UpdateScopeLabel(); position = -1; Next();
                status.Text = success + " decisions saved in Paratext notes. " + failures.Count + " failed; " + progress.Outstanding().Count() + " pending recovery. No Scripture edits.";
                if (failures.Count > 0) { session.AddWarning(String.Join("\r\n", failures)); status.Text += " Failure details are in the session report."; }
            }
            finally { busy = false; EnableActions(); }
        }
        void Retry()
        {
            Guard(delegate {
                if (!retry.Enabled) return;
                if (MessageBox.Show(this, "Confirm you inspected the saved passage and project history and verified that the pending correction was NOT applied. This permits another attempt; it does not change Scripture.", "Reconcile interrupted apply", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
                var previous = progress.Get(current.StableId); var confirmed = ReviewRecord.FromXml(previous.ToXml());
                confirmed.Outcome = "Retry confirmed"; confirmed.Comment = comment.Text; confirmed.Actor = owner.Reviewer; confirmed.ActionUtc = DateTime.UtcNow.ToString("O"); confirmed.Evidence = "Reviewer confirmed no correction was applied; the displayed expected result was not verified as a saved change.";
                confirmed.AfterContext = confirmed.BeforeContext;
                progress.Save(confirmed); session.Track(confirmed); status.Text = "Retry permitted after your verification. Review the exact correction again."; reviewed.Checked = false; EnableActions();
            });
        }
    }
}
