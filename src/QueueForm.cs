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
        readonly ComboBox startBook = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = ParatextTheme.Scale(110) };
        readonly Button startFromBook = Button("Start from book");
        readonly Button previous = Button("Previous"), next = Button("Next");
        readonly ToolStripButton filters = ParatextTheme.ToolButton("Filters...", "parameters", "Choose books, note status, category and discussion text"),
            recovery = ParatextTheme.ToolButton("Recovery", "history", "Interrupted applies and pending note resolutions in all books");
        readonly ComboBox syncGroup = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = ParatextTheme.Scale(85) };
        readonly Label scopeLabel = new Label { AutoSize = true }, navigationLabel = new Label { AutoSize = true };
        readonly Timer navigationTimer = new Timer { Interval = 150 };
        QueueScope scope;
        // One host request is not proof of where the editor ends up: Paratext re-renders the
        // editor and echoes reference changes between its windows on later UI ticks.
        enum NavigationPhase { Idle, Start, Reveal, Settle }
        NavigationPhase navigationPhase;
        int navigationTicks, navigationStable, navigationRepairs;
        bool navigationConfirmed;
        const int NavigationStableTicks = 8, NavigationMaxTicks = 30, NavigationReopenLimit = 2;
        int firstBook;
        sealed class BookChoice
        {
            public int Number;
            public string Code;
            public override string ToString() { return Code; }
        }
        readonly Timer indexTimer = new Timer { Interval = 30 };
        readonly Timer draftTimer = new Timer { Interval = 600 };
        readonly Label passageReference = new Label(), heading = new Label(), status = new Label();
        // Paratext reports progress in a status bar along the bottom of the window.
        readonly StatusStrip activityStrip = new StatusStrip { SizingGrip = false, Renderer = ParatextTheme.Renderer(), Font = ParatextTheme.Font };
        readonly ToolStripProgressBar activityProgress = new ToolStripProgressBar();
        readonly ToolStripStatusLabel activityLabel = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        int indexingTotal;
        Form activityWindow;
        TextBox activityDetails;
        void Activity(string text, int done, int total)
        {
            activityLabel.Text = text + (total > 0 ? " (" + done + " / " + total + ")" : "");
            activityProgress.Style = total > 0 ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;
            activityProgress.Maximum = Math.Max(1, total);
            activityProgress.Value = Math.Max(0, Math.Min(done, activityProgress.Maximum));
            activityProgress.Visible = true;
            activityStrip.Refresh();
        }
        void ActivityComplete(string summary, bool showPopup = false)
        {
            activityProgress.Style = ProgressBarStyle.Continuous;
            activityProgress.Value = activityProgress.Maximum;
            activityLabel.Text = "Activity finished";
            activityStrip.Refresh();
            if (!showPopup || !Visible || IsDisposed) return;
            if (activityWindow != null && !activityWindow.IsDisposed) {
                activityDetails.Text = summary;
                activityWindow.Activate(); return;
            }
            var popup = new Form { Text = "Review queue activity", Size = new Size(ParatextTheme.Scale(530), ParatextTheme.Scale(280)),
                MinimumSize = new Size(ParatextTheme.Scale(320), ParatextTheme.Scale(200)), StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, MinimizeBox = false };
            ParatextTheme.Window(popup);
            activityWindow = popup;
            activityDetails = new TextBox { Text = summary, Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(ParatextTheme.Scale(8), ParatextTheme.Scale(8), ParatextTheme.Scale(8), 0) }; body.Controls.Add(activityDetails);
            var close = ParatextTheme.CommandButton("OK"); close.DialogResult = DialogResult.OK;
            close.Click += delegate { popup.Close(); }; popup.Controls.Add(body); popup.Controls.Add(ParatextTheme.DialogButtons(close)); popup.AcceptButton = close;
            popup.Show(this);
        }
        readonly TextBox queueLog = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
        readonly ToolStripButton information = ParatextTheme.ToolButton("Review information", "info", null);
        readonly HashSet<string> unreadableSources = new HashSet<string>();
        Form informationWindow;
        TextBox informationText;
        readonly TextBox original = Box(65), replacement = Box(65), source = Box(180), comment = Box(65);
        readonly ComboBox region = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
        readonly CheckBox reviewed = new CheckBox { Text = "I reviewed this exact correction", AutoSize = true };
        readonly CheckBox deletion = new CheckBox { Text = "Explicitly delete the original text", AutoSize = true };
        readonly Button apply = Button("Apply + next"), reject = Button("Reject + next"), manual = Button("Completed manually / no change"), retry = Button("Verified no edit; allow retry"), pause = Button("Pause"), later = Button("Leave for later"), back = Button("Return to passage");
        readonly ToolStripButton bulk = ParatextTheme.ToolButton("Bulk resolve...", "tickall", "Review several filtered proposals together");
        readonly Button bulkSandhi = Button("Bulk apply Sandhi..."), bulkTypos = Button("Bulk apply confirmed typos...");
        readonly Button skippedReview = Button("Manual review (0)");
        string requestedManualId;
        readonly Button resolve = Button("Retry note resolution");
        // Fast review: one key per note on its own tab, with the comment filled in automatically.
        readonly TabControl tabs = new TabControl { Dock = DockStyle.Fill, Margin = new Padding(0, ParatextTheme.Scale(2), 0, ParatextTheme.Scale(2)) };
        readonly TabPage reviewTab = new TabPage("Review") { Padding = new Padding(ParatextTheme.Scale(2)) }, fastTab = new TabPage("Fast review") { Padding = new Padding(ParatextTheme.Scale(4)) }, logTab = new TabPage("Log") { Padding = new Padding(ParatextTheme.Scale(4)) };
        readonly ToolStripButton fastButton = ParatextTheme.ToolButton("Fast review...", null, "One key per note: turn fast review on or off and choose its settings");
        readonly DiffBox fastBefore = new DiffBox(), fastAfter = new DiffBox();
        readonly KeyPanel fastKeys = new KeyPanel { Dock = DockStyle.Fill, Height = ParatextTheme.Scale(28) };
        readonly Label fastBand = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Tag = ParatextTheme.KeepColour },
            fastSource = new Label(), fastComments = new Label(), fastReason = new Label { Tag = ParatextTheme.KeepColour }, fastLegend = new Label { Tag = ParatextTheme.KeepColour };
        readonly TextBox fastComment = new TextBox { Dock = DockStyle.Top };
        readonly TextBox referenceBox = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
        readonly Button fastSettings = Button("Fast review settings..."), probe = Button("Host probe"), openReferences = Button("Open reference windows"),
            exportWording = Button("Export for wording..."), importWording = Button("Import wording sheet...");
        readonly List<string> fastLog = new List<string>();
        readonly Dictionary<string, string> unreadableReferences = new Dictionary<string, string>(), referenceTexts = new Dictionary<string, string>();
        FastSettings fast;
        WordingSheet wording;
        FastChoice fastChoice;
        // The last fast decision's note reply and resolution wait for the next action, so it can be undone completely.
        NoteCandidate held;
        int presetOverride;
        bool manualWarned, probing, navigationQuiet, navigationHadFocus;
        NoteCandidate current;
        int position = -1, completed;
        int missingRecoveryCount;
        bool paused, busy, populating, indexing = true, blocked;
        bool resourcesDisposed;
        bool embedded;
        // Hosts the queue inside a Paratext child window, which then owns size, position and docking.
        public void Embed(Control parent)
        {
            embedded = true; TopLevel = false; FormBorderStyle = FormBorderStyle.None; WindowState = FormWindowState.Normal;
            // A docked pane can be smaller than the floating window's minimum: scroll instead of clipping.
            MinimumSize = Size.Empty; AutoScroll = true; AutoScrollMinSize = new Size(430, 560); Dock = DockStyle.Fill;
            parent.Controls.Add(this); Show();
            if (indexing && !paused) indexTimer.Start();
        }
        // Brings the queue back in front, whether it floats on its own or sits in a Paratext tab.
        public void Reveal()
        {
            if (!embedded) { Show(); WindowState = FormWindowState.Normal; Activate(); return; }
            var tab = Parent == null ? null : Parent.FindForm();
            if (tab != null)
            {
                // Paratext's docked windows replace Form.Activate with one that selects their tab.
                var activate = tab.GetType().GetMethod("Activate", Type.EmptyTypes);
                try { if (activate != null) activate.Invoke(tab, null); } catch (Exception) { }
            }
            Focus();
        }
        public QueueForm(NoteService service, string progressRoot = null, string reportDirectory = null)
        {
            owner = service;
            progress = new ReviewProgress(progressRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ParatextNoteSuggestions", "Review"), owner.ProjectId);
            try { books = new Queue<int>(owner.QueueBooks()); session = new ReviewSession(progress.DirectoryPath, owner.ProjectId, owner.ProjectName, owner.Reviewer); }
            catch { progress.Dispose(); throw; }
            scope = progress.LoadScope();
            fast = FastSettings.Load(progress.DirectoryPath); wording = new WordingSheet(progress.DirectoryPath);
            books.Clear(); foreach (int number in owner.QueueBooks().Where(scope.IncludesBook)) books.Enqueue(number);
            reportProjectDirectory = reportDirectory;
            string savedFolder = Path.Combine(progress.DirectoryPath, "report-folder.xml");
            if (reportProjectDirectory == null && File.Exists(savedFolder)) reportProjectDirectory = XElement.Load(savedFolder).Value;
            if (reportProjectDirectory == null || !ProjectReportDirectory.Matches(reportProjectDirectory, owner.ProjectId)) reportProjectDirectory = ProjectReportDirectory.Find(owner.ProjectName, owner.ProjectId);
            Text = "Note review queue - " + owner.ProjectName;
            ParatextTheme.Window(this);
            var area = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(ParatextTheme.Scale(760), area.Width - 30), Math.Min(ParatextTheme.Scale(900), area.Height - 35));
            MinimumSize = new Size(ParatextTheme.Scale(450), ParatextTheme.Scale(640)); StartPosition = FormStartPosition.CenterScreen;
            // Paratext tool-window layout: command toolbar, content, status bar with the loader.
            var toolbar = ParatextTheme.ToolBar();
            toolbar.Items.Add(filters); toolbar.Items.Add(bulk); toolbar.Items.Add(recovery); toolbar.Items.Add(fastButton);
            information.Alignment = ToolStripItemAlignment.Right; information.DisplayStyle = ToolStripItemDisplayStyle.Image; information.Overflow = ToolStripItemOverflow.Never;
            toolbar.Items.Add(information);
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(ParatextTheme.Scale(8), ParatextTheme.Scale(6), ParatextTheme.Scale(8), ParatextTheme.Scale(4)) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            // Only the reference is enlarged; the rest of the header uses the normal window font.
            passageReference.Font = ParatextTheme.Larger(Font, 1.6f); passageReference.AutoSize = true; passageReference.Margin = new Padding(0, 0, 0, ParatextTheme.Scale(2));
            heading.AutoSize = true; heading.Margin = new Padding(0, 0, 0, ParatextTheme.Scale(2));
            scopeLabel.Tag = ParatextTheme.KeepColour; scopeLabel.ForeColor = ParatextTheme.ColorOf("text_dimmed"); scopeLabel.Margin = new Padding(0, 0, 0, ParatextTheme.Scale(4));
            navigationLabel.Tag = ParatextTheme.KeepColour; navigationLabel.ForeColor = ParatextTheme.ColorOf("text_dimmed");
            var top = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 1, RowCount = 3, Margin = new Padding(0) };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var bookPicker = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0) };
            bookPicker.Controls.Add(new Label { Text = "Review starting at", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, ParatextTheme.Scale(6), ParatextTheme.Scale(4), 0) });
            bookPicker.Controls.Add(startBook); bookPicker.Controls.Add(startFromBook);
            syncGroup.Items.AddRange(Enum.GetValues(typeof(SyncReferenceGroup)).Cast<object>().ToArray()); syncGroup.SelectedItem = owner.NavigationGroup;
            syncGroup.SelectedIndexChanged += delegate { owner.NavigationGroup = (SyncReferenceGroup)syncGroup.SelectedItem; };
            top.Controls.Add(passageReference, 0, 0); top.Controls.Add(heading, 0, 1); top.Controls.Add(scopeLabel, 0, 2); root.Controls.Add(top, 0, 0);
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
            var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true, Padding = new Padding(ParatextTheme.Scale(4)) };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
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
            reviewTab.Controls.Add(fields); BuildFastTab();
            var sourceTab = new TabPage("Source discussion") { Padding = new Padding(ParatextTheme.Scale(4)) }; sourceTab.Controls.Add(source);
            tabs.TabPages.Add(reviewTab); tabs.TabPages.Add(fastTab); tabs.TabPages.Add(sourceTab); root.Controls.Add(tabs);
            // Infrequent settings and recovery live on Tools, grouped like a Paratext settings page.
            var tools = new FlowLayoutPanel { AutoScroll = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(ParatextTheme.Scale(8)) };
            Action<string> section = delegate(string title) {
                tools.Controls.Add(new Label { Text = title, AutoSize = true, Font = ParatextTheme.Bold(Font), Margin = new Padding(0, tools.Controls.Count == 0 ? 0 : ParatextTheme.Scale(12), 0, ParatextTheme.Scale(4)) });
            };
            section("Navigation"); tools.Controls.Add(bookPicker);
            var groupPicker = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
            groupPicker.Controls.Add(new Label { Text = "Scroll group", AutoSize = true, Margin = new Padding(0, ParatextTheme.Scale(6), ParatextTheme.Scale(4), 0) }); groupPicker.Controls.Add(syncGroup);
            tools.Controls.Add(groupPicker); tools.Controls.Add(navigationLabel);
            section("Bulk corrections"); foreach (var b in new[] { bulkSandhi, bulkTypos, skippedReview }) tools.Controls.Add(b);
            section("Recovery"); foreach (var b in new[] { retry, resolve, later }) tools.Controls.Add(b);
            section("Reports"); var export = Button("Export decisions...");
            foreach (var b in new[] { viewReport, reportFolder, export }) tools.Controls.Add(b);
            section("Fast review"); foreach (var b in new[] { fastSettings, probe, openReferences, exportWording, importWording }) tools.Controls.Add(b);
            var toolsTab = new TabPage("Tools"); toolsTab.Controls.Add(tools); tabs.TabPages.Add(toolsTab);
            queueLog.Name = "queueLog"; queueLog.AccessibleName = "Queue log";
            logTab.Controls.Add(queueLog); tabs.TabPages.Add(logTab);
            // Wraps onto a second row in a narrow window or docked pane.
            var actions = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0) };
            foreach (var b in new[] { previous, next, back, apply, reject, pause }) actions.Controls.Add(b);
            root.Controls.Add(actions);
            information.Name = "reviewInformation"; information.AccessibleName = "Review information";
            information.ToolTipText = "Review information: current status and saved bulk skip reason";
            information.Click += delegate { ShowReviewInformation(); };
            status.TextChanged += delegate {
                information.AccessibleDescription = status.Text;
                if (informationText != null && !informationText.IsDisposed) informationText.Text = status.Text;
                RefreshQueueLog();
            };
            activityProgress.Size = new Size(ParatextTheme.Scale(160), ParatextTheme.Scale(16));
            activityStrip.Items.Add(activityProgress); activityStrip.Items.Add(activityLabel);
            Controls.Add(root); Controls.Add(toolbar); Controls.Add(activityStrip);
            indexingTotal = books.Count; Activity("Loading review notes", 0, indexingTotal);
            Action reflow = delegate {
                if (WindowState == FormWindowState.Minimized || root.ClientSize.Width < 300) return;
                int width = root.ClientSize.Width - root.Padding.Horizontal - 6;
                passageReference.MaximumSize = heading.MaximumSize = scopeLabel.MaximumSize = new Size(width, 0);
                navigationLabel.MaximumSize = bookPicker.MaximumSize = new Size(Math.Max(200, tools.ClientSize.Width - tools.Padding.Horizontal - 4), 0);
                foreach (Control control in fields.Controls) {
                    control.Margin = new Padding(ParatextTheme.Scale(2));
                    if (control is CheckBox || control is Button) control.MaximumSize = new Size(Math.Max(180, fields.ClientSize.Width - 10), 0);
                }
            };
            fields.SizeChanged += delegate { reflow(); }; root.SizeChanged += delegate { reflow(); }; tools.SizeChanged += delegate { reflow(); };
            UpdateScopeLabel(); RestoreLayout();
            status.Text = "Indexing notes... Pause or close to stop.";
            heading.Text = owner.ProjectName + " - preparing review queue";
            apply.Click += delegate { Decide("Applied"); }; reject.Click += delegate { Decide("Rejected"); };
            manual.Click += delegate { Decide("Completed manually"); };
            retry.Click += delegate { Retry(); };
            resolve.Click += delegate {
                if (!resolve.Enabled) return;
                Guard(delegate { FlushHeld(); busy = true; EnableActions(); Activity("Retrying note reply and resolution", 0, 0);
                    try { string reference = current.DisplayReference, id = current.StableId; FinishResolution(); ActivityComplete("Note reply saved for " + reference + ". Note status: " + progress.Get(id).Resolution + ". No Scripture edit repeated."); }
                    finally { busy = false; EnableActions(); } });
            };
            pause.Click += delegate {
                Guard(delegate {
                    FlushHeld(); SaveDraft(); paused = !paused; pause.Text = paused ? "Resume" : "Pause";
                    if (indexing) { if (paused) indexTimer.Stop(); else indexTimer.Start(); }
                    else if (!paused && current == null) { position = -1; Next(); }
                    string report = paused ? GenerateReports("Paused", true) : null;
                    status.Text = paused ? "Paused. Position and draft saved." + (report == null ? " No new decisions to report." : " HTML report saved. Use View latest report.") : "Waiting for your decision. You can browse reference windows freely.";
                    EnableActions();
                });
            };
            later.Click += delegate { Guard(delegate { FlushHeld(); SaveDraft(); Next(); }); };
            back.Click += delegate { Guard(delegate {
                NavigateCurrent();
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
            draftTimer.Tick += delegate { draftTimer.Stop(); Guard(SaveDraft); if (fast.Enabled) Guard(RefreshFast); };
            indexTimer.Tick += delegate { Guard(IndexBook); };
            navigationTimer.Tick += delegate { Guard(CheckNavigation); };
            Shown += delegate { if (indexing && !paused) indexTimer.Start(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (busy) { e.Cancel = true; return; }
                try { FlushHeld(); SaveDraft(); SaveLayout(); GenerateReports("Closed", true); } catch (Exception ex) { e.Cancel = true; Fail(ex); }
            };
            owner.SetInlineMenuHandler(ShowInlineMenu);
            fastButton.Click += delegate { Guard(ChooseFastSettings); }; fastSettings.Click += delegate { Guard(ChooseFastSettings); };
            probe.Click += delegate { Guard(RunProbe); }; openReferences.Click += delegate { Guard(OpenReferenceWindows); };
            exportWording.Click += delegate { Guard(ExportWording); }; importWording.Click += delegate { Guard(ImportWording); };
            tabs.SelectedIndexChanged += delegate { if (tabs.SelectedTab == fastTab && fast.Enabled) Guard(RefreshFast); UpdateBand(); };
            Enter += delegate { UpdateBand(); }; Activated += delegate { UpdateBand(); };
            Leave += delegate { FocusLeft(); }; Deactivate += delegate { FocusLeft(); };
            foreach (Control c in new Control[] { fastKeys, fastComment }) { c.Enter += delegate { UpdateBand(); }; c.Leave += delegate { UpdateBand(); }; }
            ApplyFast(fast, false);
            EnableActions();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !resourcesDisposed)
            {
                resourcesDisposed = true; indexTimer.Dispose(); draftTimer.Dispose(); navigationTimer.Dispose();
                status.Dispose();
                if (informationWindow != null) informationWindow.Dispose();
                if (inlineMenu != null) inlineMenu.Dispose();
                owner.SetInlineMenuHandler(null);
                owner.HighlightTarget(null);
                if (progress != null) progress.Dispose();
            }
            base.Dispose(disposing);
            // Fonts outlive the controls that draw with them; disposing one earlier breaks their last repaint.
            if (disposing) { foreach (var font in fonts) font.Dispose(); fonts.Clear(); }
        }
        ContextMenuStrip inlineMenu;
        bool IsInlineTarget(NoteCandidate item)
        {
            return !IsDisposed && !Disposing && current != null && Object.ReferenceEquals(current, item) &&
                !busy && !paused && !indexing;
        }
        bool CanInlineDecision(NoteCandidate item, string outcome)
        {
            if (!IsInlineTarget(item)) return false;
            EnableActions();
            if (outcome == "Ignored") return later.Enabled;
            if (item.IsResolved || item.Unreadable || blocked || duplicateIds.Contains(item.StableId) || Pending() ||
                (progress.Get(item.StableId) ?? new ReviewRecord()).Completed) return false;
            if (outcome == "Rejected") return true;
            return outcome == "Applied" && item.Proposal.Route == ProposalRoute.Apply && !deletion.Checked &&
                original.Text == item.Proposal.Original && replacement.Text == item.Proposal.Suggested &&
                replacement.Text.Length > 0 && region.SelectedItem != null && (ContentRegion)region.SelectedItem == item.Proposal.Region;
        }
        void ShowInlineMenu(NoteCandidate item, Point location)
        {
            if (!IsInlineTarget(item)) return;
            if (inlineMenu != null) inlineMenu.Dispose();
            inlineMenu = new ContextMenuStrip { ShowImageMargin = true, Renderer = ParatextTheme.Renderer(), Font = ParatextTheme.Font };
            inlineMenu.Items.Add(new ToolStripMenuItem("Suggested: " + NoteParser.Preview(item.Proposal.Suggested, 65).Replace("&", "&&")) { Enabled = false });
            inlineMenu.Items.Add(new ToolStripSeparator());
            var accept = new ToolStripMenuItem("Apply suggestion...", ParatextTheme.ImageOf("Checkmark")) { Enabled = CanInlineDecision(item, "Applied") };
            var decline = new ToolStripMenuItem("Reject suggestion...", ParatextTheme.ImageOf("deny_blue")) { Enabled = CanInlineDecision(item, "Rejected") };
            var ignore = new ToolStripMenuItem("Ignore for now (leave unresolved)", ParatextTheme.ImageOf("clock")) { Enabled = CanInlineDecision(item, "Ignored") };
            accept.Click += delegate { PromptInlineDecision(item, "Applied"); };
            decline.Click += delegate { PromptInlineDecision(item, "Rejected"); };
            ignore.Click += delegate { CompleteInlineDecision(item, "Ignored", ""); };
            inlineMenu.Items.Add(accept); inlineMenu.Items.Add(decline); inlineMenu.Items.Add(ignore);
            inlineMenu.Items.Add(new ToolStripSeparator());
            inlineMenu.Items.Add("Review source / edit correction in queue", ParatextTheme.ImageOf("edit_pencil"), delegate {
                if (IsInlineTarget(item)) Reveal();
            });
            inlineMenu.Show(location);
        }
        void PromptInlineDecision(NoteCandidate item, string outcome)
        {
            if (!CanInlineDecision(item, outcome)) return;
            using (var dialog = new Form { Text = outcome == "Applied" ? "Apply note suggestion" : "Reject note suggestion",
                StartPosition = FormStartPosition.CenterParent,
                // An inline action can originate while this queue is minimized. Keep its
                // confirmation reachable independently through the taskbar and Alt+Tab.
                Size = new Size(ParatextTheme.Scale(620), ParatextTheme.Scale(540)), MinimumSize = new Size(ParatextTheme.Scale(360), ParatextTheme.Scale(360)), ShowInTaskbar = true, MinimizeBox = false, MaximizeBox = false })
            {
                ParatextTheme.Window(dialog);
                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(ParatextTheme.Scale(10), ParatextTheme.Scale(10), ParatextTheme.Scale(10), 0) };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, ParatextTheme.Scale(80)));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var preview = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
                    Text = item.DisplayReference + " | " + item.Proposal.Category + "\r\n\r\nOriginal: " + item.Proposal.Original +
                    "\r\nSuggested: " + item.Proposal.Suggested + "\r\n\r\n" + item.NoteText };
                var decisionComment = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Text = comment.Text };
                var acknowledge = new CheckBox { Text = "I reviewed this exact correction", AutoSize = true, Visible = outcome == "Applied" };
                var confirm = ParatextTheme.CommandButton(outcome == "Applied" ? "Apply" : "Reject"); confirm.DialogResult = DialogResult.OK;
                var cancel = ParatextTheme.CommandButton("Cancel"); cancel.DialogResult = DialogResult.Cancel;
                Action enable = delegate { confirm.Enabled = decisionComment.Text.Trim().Length > 0 && (outcome != "Applied" || acknowledge.Checked); };
                decisionComment.TextChanged += delegate { enable(); }; acknowledge.CheckedChanged += delegate { enable(); }; enable();
                layout.Controls.Add(preview, 0, 0); layout.Controls.Add(new Label { Text = "Decision comment (required)", AutoSize = true, Margin = new Padding(0, ParatextTheme.Scale(8), 0, ParatextTheme.Scale(2)) }, 0, 1);
                layout.Controls.Add(decisionComment, 0, 2); layout.Controls.Add(acknowledge, 0, 3);
                dialog.Controls.Add(layout); dialog.Controls.Add(ParatextTheme.DialogButtons(confirm, cancel)); dialog.AcceptButton = confirm; dialog.CancelButton = cancel;
                dialog.Shown += delegate { decisionComment.Focus(); decisionComment.SelectionStart = decisionComment.TextLength; };
                if (dialog.ShowDialog(this) == DialogResult.OK && confirm.Enabled)
                    CompleteInlineDecision(item, outcome, decisionComment.Text);
            }
        }
        void CompleteInlineDecision(NoteCandidate item, string outcome, string decisionComment)
        {
            // Menus and dialogs capture their proposal; never retarget a stale click to the next item.
            if (!CanInlineDecision(item, outcome)) return;
            if (outcome == "Ignored") { Guard(delegate { FlushHeld(); SaveDraft(); Next(); }); return; }
            if (decisionComment.Trim().Length == 0) return;
            comment.Text = decisionComment;
            if (outcome == "Applied") reviewed.Checked = true;
            Decide(outcome);
        }
        static TextBox Box(int height) { return new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Height = ParatextTheme.Scale(height), Dock = DockStyle.Top }; }
        static Button Button(string text) { return ParatextTheme.CommandButton(text); }
        static void Add(TableLayoutPanel panel, string label, Control control)
        {
            int row = panel.RowCount++;
            var box = control as TextBox;
            panel.RowStyles.Add(new RowStyle(box == null ? SizeType.AutoSize : SizeType.Percent, box == null ? 0 : 33.33f));
            panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Left, Margin = new Padding(ParatextTheme.Scale(2), ParatextTheme.Scale(5), ParatextTheme.Scale(8), 0) }, 0, row);
            if (box != null) { box.MinimumSize = new Size(0, ParatextTheme.Scale(40)); box.Dock = DockStyle.Fill; }
            panel.Controls.Add(control, 1, row);
        }
        void Guard(Action action) { try { action(); } catch (Exception ex) { Fail(ex); } }
        void ShowReviewInformation()
        {
            if (informationWindow == null || informationWindow.IsDisposed) {
                informationText = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Text = status.Text, AccessibleName = "Current review details" };
                informationWindow = new Form { Text = "Review information", Size = new Size(ParatextTheme.Scale(520), ParatextTheme.Scale(300)), MinimumSize = new Size(ParatextTheme.Scale(300), ParatextTheme.Scale(180)), StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, MinimizeBox = false };
                ParatextTheme.Window(informationWindow);
                var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(ParatextTheme.Scale(8)) }; body.Controls.Add(informationText);
                informationWindow.Controls.Add(body);
                informationWindow.Show(this);
            }
            else informationWindow.Activate();
        }
        void RefreshQueueLog()
        {
            var notices = new List<string>();
            if (indexing) notices.Add("Indexing notes in the current scope...");
            else if (missingRecoveryCount > 0) notices.Add(missingRecoveryCount + " interrupted decision(s) are outside this scope or have missing/changed sources. Use Recovery or export decisions and inspect Paratext history/notes.");
            if (unreadableSources.Count > 0) notices.Add(unreadableSources.Count + " unreadable discussions in this session (see report).");
            string[] warnings = session.Warnings.ToArray();
            queueLog.Text = scopeLabel.Text + "\r\n\r\n" + (notices.Count == 0 ? "No queue notices." : String.Join("\r\n\r\n", notices)) +
                "\r\n\r\nSESSION LOG\r\n" + (warnings.Length == 0 ? "No warnings recorded." : String.Join("\r\n\r\n", warnings)) +
                (fastLog.Count == 0 ? "" : "\r\n\r\nFAST REVIEW LOG\r\n" + String.Join("\r\n", fastLog));
        }
        void UpdateScopeLabel()
        {
            scopeLabel.Text = "Scope: " + scope.Describe(owner.QueueBookCode);
            session.SetScope(scopeLabel.Text, items.Count, completed);
            missingRecoveryCount = indexing ? 0 : progress.Outstanding().Count(r => !seen.Contains(r.Id));
            RefreshQueueLog();
        }
        void ChangeScope(QueueScope selection)
        {
            if (busy) return;
            FlushHeld(); SaveDraft(); progress.SaveScope(selection);
            indexTimer.Stop(); StopNavigation(); draftTimer.Stop(); scope = selection;
            items.Clear(); duplicateIds.Clear(); seen.Clear(); books.Clear(); current = null; position = -1; completed = 0;
            owner.HighlightTarget(null);
            foreach (int number in owner.QueueBooks().Where(scope.IncludesBook)) books.Enqueue(number);
            paused = false; pause.Text = "Pause"; indexing = true; indexingTotal = books.Count; Activity("Loading review notes", 0, indexingTotal); UpdateScopeLabel();
            passageReference.Text = "";
            heading.Text = "Indexing selected scope, one book at a time...";
            source.Clear(); original.Clear(); replacement.Clear(); comment.Clear(); navigationLabel.Text = "";
            EnableActions(); indexTimer.Start();
        }
        void SaveLayout()
        {
            var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            string saved = Path.Combine(progress.DirectoryPath, "layout.xml");
            if (embedded)
            {
                // Paratext stores the docked window's place. Keep the floating window's own
                // saved bounds and remember only the scroll group.
                var layout = File.Exists(saved) ? XElement.Load(saved) : new XElement("layout");
                layout.SetAttributeValue("group", owner.NavigationGroup);
                ReviewProgress.AtomicWrite(saved, layout); return;
            }
            ReviewProgress.AtomicWrite(Path.Combine(progress.DirectoryPath, "layout.xml"), new XElement("layout", new XAttribute("x", bounds.X), new XAttribute("y", bounds.Y),
                new XAttribute("width", bounds.Width), new XAttribute("height", bounds.Height), new XAttribute("maximized", WindowState == FormWindowState.Maximized), new XAttribute("group", owner.NavigationGroup)));
        }
        void RestoreLayout()
        {
            string path = Path.Combine(progress.DirectoryPath, "layout.xml"); if (!File.Exists(path)) return;
            var e = XElement.Load(path);
            SyncReferenceGroup group;
            // A layout saved by a docked queue alone carries no window bounds.
            if (e.Attribute("x") == null || e.Attribute("y") == null || e.Attribute("width") == null || e.Attribute("height") == null)
            { if (Enum.TryParse((string)e.Attribute("group"), out group) && Enum.IsDefined(typeof(SyncReferenceGroup), group)) syncGroup.SelectedItem = group; return; }
            var saved = new Rectangle((int)e.Attribute("x"), (int)e.Attribute("y"), (int)e.Attribute("width"), (int)e.Attribute("height"));
            var area = Screen.FromRectangle(saved).WorkingArea;
            int width = Math.Min(area.Width, Math.Max(MinimumSize.Width, saved.Width)), height = Math.Min(area.Height, Math.Max(MinimumSize.Height, saved.Height));
            StartPosition = FormStartPosition.Manual; Bounds = new Rectangle(Math.Max(area.Left, Math.Min(saved.X, area.Right - width)), Math.Max(area.Top, Math.Min(saved.Y, area.Bottom - height)), width, height); if (Enum.TryParse((string)e.Attribute("group"), out group) && Enum.IsDefined(typeof(SyncReferenceGroup), group)) syncGroup.SelectedItem = group;
            if ((bool?)e.Attribute("maximized") == true) WindowState = FormWindowState.Maximized;
        }
        void NavigateCurrent()
        {
            StopNavigation(); if (current == null) return;
            navigationLabel.Text = "Opening " + current.DisplayReference + " in " + owner.ProjectName + ". Verse navigation; anchor: " + NoteParser.Preview(current.AnchorText ?? "", 60);
            navigationTicks = navigationStable = navigationRepairs = 0; navigationConfirmed = false;
            // Fast review moves a grouped editor without activating it, so the keyboard stays in the queue.
            navigationQuiet = fast.Enabled && owner.EditorGrouped(); navigationHadFocus = ContainsFocus;
            // The annotation change is rendered by the host before the first timer tick, so
            // the navigation request that follows is the last thing to move the editor.
            try { owner.PrepareHighlight(current); }
            catch (Exception ex) { owner.ReportFailure(ex); } // The highlight is optional; the verse is still opened.
            navigationPhase = NavigationPhase.Start; navigationTimer.Start();
        }
        void StopNavigation() { navigationTimer.Stop(); navigationPhase = NavigationPhase.Idle; }
        void CheckNavigation()
        {
            if (current == null || navigationPhase == NavigationPhase.Idle) { StopNavigation(); return; }
            // A failed scroll request must not pause the review or block a decision.
            try { AdvanceNavigation(); }
            catch (Exception ex)
            {
                StopNavigation(); owner.ReportFailure(ex);
                navigationLabel.Text = "Navigation to " + current.DisplayReference + " failed: " + ex.Message + " Use Return to passage to try again.";
            }
        }
        void AdvanceNavigation()
        {
            if (navigationPhase == NavigationPhase.Start)
            {
                // Same verse as the previous item, or an editor the reviewer scrolled away
                // from its own reference: only a real reference change reveals the verse.
                if (navigationQuiet)
                {
                    if (owner.NavigationVisible(current) && owner.BeginQuietReturn(current)) { navigationPhase = NavigationPhase.Reveal; return; }
                    owner.QuietNavigate(current); navigationPhase = NavigationPhase.Settle; return;
                }
                if (owner.NavigationVisible(current) && owner.BeginReturnToPassage(current)) { navigationPhase = NavigationPhase.Reveal; KeepKeys(); return; }
                owner.Navigate(current); navigationPhase = NavigationPhase.Settle; KeepKeys(); return;
            }
            if (navigationPhase == NavigationPhase.Reveal)
            {
                if (navigationQuiet) owner.QuietNavigate(current); else { owner.Navigate(current); KeepKeys(); }
                navigationPhase = NavigationPhase.Settle; return;
            }
            navigationTicks++;
            // No render asked for the target verse: the editor kept its loaded text.
            if (navigationTicks == 2 && owner.HighlightPending) owner.RefreshHighlight();
            bool visible = owner.NavigationVisible(current);
            if (visible)
            {
                navigationConfirmed = true; navigationStable++;
                // Also covers an editor that was busy loading when its window took the reference.
                if (navigationTicks == 4) owner.ReassertNavigation(current, false);
            }
            else
            {
                // A late echo of the neighbouring verse, a reload after an edit, or a window
                // that was still opening moved the editor: send the pinned reference again.
                navigationStable = 0;
                if (navigationTicks % 2 == 0)
                {
                    bool reopen = !navigationQuiet && navigationRepairs++ < NavigationReopenLimit;
                    owner.ReassertNavigation(current, reopen);
                    if (reopen) KeepKeys();
                }
            }
            if (navigationStable < NavigationStableTicks && navigationTicks < NavigationMaxTicks) return;
            StopNavigation();
            if ((fast.Enabled || probing) && navigationHadFocus)
                FastLog("Opened " + current.DisplayReference + (navigationQuiet ? " through the scroll group" : " with an activating window call") + "; the queue " + (ContainsFocus ? "kept" : "LOST") + " the keyboard.");
            navigationLabel.Text = (visible ? "Project window reference confirmed: " : navigationConfirmed ? "Project window moved away again after navigation: " : "Navigation requested; window reference not confirmed: ") + current.DisplayReference +
                ". Verse-only; anchored text: " + NoteParser.Preview(current.AnchorText ?? "", 60) + ". Scroll group: " + owner.NavigationGroup + ". " + owner.HighlightStatus;
        }
        void Fail(Exception ex)
        {
            indexTimer.Stop(); draftTimer.Stop(); StopNavigation(); ActivityComplete("Activity stopped: " + ex.Message + " Review paused. Check Recovery before retrying an interrupted write."); paused = true; pause.Text = "Resume"; owner.ReportFailure(ex);
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
                    if (c.Unreadable) {
                        unreadableSources.Add(c.StableId);
                        session.AddWarning(c.DisplayReference + " - unreadable note discussion in an indexed book (" + (c.IsResolved ? "already resolved; may be outside status filter" : "unresolved; handle manually") + "). " + c.NoteText);
                    }
                    if (scope.RecoveryOnly ? !outstanding : (!scope.IncludesStatus(c.IsResolved) || !scope.IncludesCandidate(c))) continue;
                    if (!seen.Add(c.StableId)) duplicateIds.Add(c.StableId);
                    items.Add(c);
                }
                books.Dequeue(); Activity("Loading review notes", indexingTotal - books.Count, indexingTotal);
                heading.Text = owner.ProjectName + " - indexed " + items.Count + " proposals; " + books.Count + " books remaining";
                RefreshQueueLog();
                return;
            }
            indexTimer.Stop(); indexing = false;
            ActivityComplete("Loading complete: " + items.Count + " proposals in the selected scope. " + unreadableSources.Count + " unreadable sources; inspect the Log tab for details.");
            recovery.Text = "Recovery (" + progress.Outstanding().Count() + ")";
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
            if (resume >= 0 && (record == null || !record.Completed || record.ResolutionPending || scope.Status != NoteStatusFilter.Unresolved) && !LaterRound(items[resume], record)) { position = resume; LoadCurrent(); }
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
            FlushHeld(); SaveDraft();
            progress.SaveStartBook(chosen.Number); firstBook = chosen.Number; SortItems();
            paused = false; pause.Text = "Pause"; position = -1; Next();
        }
        void Next()
        {
            draftTimer.Stop(); current = null; blocked = false; fastChoice = null;
            while (++position < items.Count)
            {
                var r = progress.Get(items[position].StableId);
                if (scope.Status == NoteStatusFilter.Unresolved && !scope.RecoveryOnly && r != null && r.Completed && !r.ResolutionPending) continue;
                if (LaterRound(items[position], r)) continue;
                current = items[position]; break;
            }
            if (current == null)
            {
                FlushHeld();
                owner.HighlightTarget(null);
                passageReference.Text = "";
                heading.Text = owner.ProjectName + " - " + completed + " / " + items.Count + " reviewed";
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
            FlushHeld(); SaveDraft(); position = target; paused = false; pause.Text = "Pause"; LoadCurrent();
        }
        void LoadCurrent()
        {
            // No key may act on a choice worked out for another note, even if loading fails part way.
            fastChoice = null;
            StopNavigation(); draftTimer.Stop(); blocked = false; current = items[position];
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
            passageReference.Text = current.DisplayReference;
            heading.Text = current.Proposal.Pass + " | " + current.Proposal.Category + "\r\n" + (position + 1) + " / " + items.Count + " | " + completed + " reviewed | " + (current.IsResolved ? "Resolved" : "Unresolved") + (previous == null ? "" : " | " + previous.Outcome) +
                (fast.Enabled && fast.NoLiteral == NoLiteralMode.LaterRound ? " | " + items.Count(c => LaterRound(c, progress.Get(c.StableId))) + " left for a later round" : "");
            if (!blocked) status.Text = ResolutionPending() ? "Decision saved locally. Retry note resolution to finish; the Scripture correction will not be repeated." : Pending() ? "An earlier apply did not finish recording. Inspect Paratext history and the passage, then confirm completed manually or confirm no edit before retrying." : "Review the correction, then apply or reject.";
            else status.Text = "Source changed, unavailable, or duplicate identity. Automatic Apply is blocked. Inspect in Paratext; leave for later or record a commented manual decision.";
            if (current.Unreadable) status.Text = "Paratext cannot read this discussion. Decisions are blocked for this note; inspect/fix it in Paratext or Leave for later. Other notes can continue.";
            if (previous != null && previous.Completed) source.Text += "\r\n\r\nSAVED LOCAL DECISION: " + previous.Outcome + "\r\n" + previous.Comment + "\r\n" + previous.Evidence + "\r\nBefore:\r\n" + previous.BeforeContext + "\r\nAfter:\r\n" + previous.AfterContext;
            if (current.IsResolved && !ResolutionPending()) status.Text = "Paratext resolved note - inspection only. Navigation never repeats its decision or Scripture edit.";
            var skipped = progress.Skipped().FirstOrDefault(r => r.Id == current.StableId);
            if (skipped != null) status.Text += "\r\nSaved bulk skip: " + skipped.Evidence;
            SaveDraft(); EnableActions();
            presetOverride = 0; manualWarned = false; fastComment.Clear();
            if (fast.Enabled) RefreshReferences();
            RefreshFast();
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
        void Decide(string outcome) { DecideWith(outcome, null); }
        // A fast choice carries the displayed wording, region, preview and comment in place of the Review tab's fields.
        void DecideWith(string outcome, FastChoice choice)
        {
            if (busy || paused || current == null) return;
            Guard(delegate {
                if (choice == null)
                {
                    if (outcome == "Applied" && (!apply.Enabled || region.SelectedItem == null)) return;
                    if (outcome == "Rejected" && !reject.Enabled) return;
                    if (outcome == "Completed manually" && !manual.Enabled) return;
                }
                else if (choice.Id != current.StableId || !FastCan(choice.Footnote != null ? "Footnote" : outcome)) return;
                FlushHeld();
                busy = true; EnableActions(); Activity("Saving review decision", 0, 0);
                // Set once Scripture was submitted or the decision saved; earlier refusals changed nothing.
                bool committed = false;
                try
                {
                    var record = Record(outcome);
                    if (choice != null) { record.Original = choice.Original; record.Replacement = choice.Replacement; record.Comment = choice.Comment; record.Region = choice.Region.ToString(); }
                    record.SessionId = session.Id; record.AuditId = Guid.NewGuid().ToString("N"); record.Reviewer = owner.Reviewer;
                    record.DecisionUtc = record.ActionUtc = DateTime.UtcNow.ToString("O"); record.Actor = record.Reviewer; record.SourceNote = current.NoteText;
                    if (outcome == "Applied")
                    {
                        // Persist intent first: any uncertain failure must stop, never blindly retry.
                        Action<PassageEvidence> evidence = delegate(PassageEvidence value) {
                            // A fast key press approves exactly the passage that was on screen.
                            if (choice != null && (value.Before != choice.Preview.Before || value.After != choice.Preview.After))
                                throw new InvalidOperationException("The passage changed after it was shown. Nothing was written; check the new text and press the key again.");
                            record.BeforeContext = value.Before; record.AfterContext = value.After; record.ContextScope = value.Scope; record.Evidence = "Expected result captured before submission; saved USFM not yet verified.";
                        };
                        Action beforeSubmit = delegate {
                            if (owner.Reviewer != record.Reviewer) throw new InvalidOperationException("The Paratext user changed during review. Reopen this item before applying.");
                            var intent = ReviewRecord.FromXml(record.ToXml()); intent.Outcome = "Pending apply";
                            progress.Save(intent); session.Track(intent); committed = true;
                        };
                        var service = owner.ForQueueItem(current);
                        if (choice != null && choice.Footnote != null) service.InsertFootnote(current, choice.Footnote, beforeSubmit, evidence);
                        else if (choice != null) service.Apply(current, choice.Original, choice.Replacement, choice.Region, true, false, beforeSubmit, evidence);
                        else if (current.Proposal.Route == ProposalRoute.Structure)
                        {
                            if (original.Text != current.Proposal.Original || replacement.Text != current.Proposal.Suggested || deletion.Checked || (ContentRegion)region.SelectedItem != current.Proposal.Region)
                                throw new InvalidOperationException("Structural review requires the unchanged source proposal and region.");
                            service.ApplyMarkerRename(current, beforeSubmit, evidence);
                        }
                        else service.Apply(current, original.Text, record.Replacement, (ContentRegion)region.SelectedItem, true, deletion.Checked, beforeSubmit, evidence);
                        record.Evidence = (choice == null ? "Verified saved USFM after the queue correction." :
                            choice.Footnote != null ? "Verified saved USFM after one footnote was inserted with fast review; the verse wording is unchanged. Template: " + choice.Footnote.Template + ". Quoted text from: " + choice.Footnote.Source + "." :
                            "Verified saved USFM after a fast review key press on the displayed before/after. Wording: " + choice.Source) + " Scripture write by Paratext user: " + record.Reviewer;
                    }
                    else
                    {
                        string snapshot = owner.CapturePassage(current, choice != null ? choice.Region : (ContentRegion)region.SelectedItem);
                        record.BeforeContext = outcome == "Rejected" ? snapshot : loadedContext;
                        record.AfterContext = snapshot; record.ContextScope = "Full passage context observed at decision time (USFM)";
                        record.Evidence = outcome == "Rejected" ? "No Scripture edit by the queue. Before and after are the same captured passage." : "Manual completion acknowledged by the reviewer. Before was observed when this item loaded; after at decision time. The actual editor of manual changes is not verified.";
                        if (choice != null) record.Evidence += " Recorded with a fast review key press.";
                    }
                    record.Resolution = "Pending";
                    var previousAttempt = progress.Get(current.StableId);
                    Action saveDecision = delegate { progress.Save(record); session.Track(record); committed = true; };
                    if (outcome == "Applied") saveDecision(); else owner.ValidateQueueDecision(current, saveDecision);
                    if (previousAttempt != null && previousAttempt.Outcome == "Pending apply" && previousAttempt.AuditId.Length > 0)
                    {
                        var reconciled = ReviewRecord.FromXml(previousAttempt.ToXml()); reconciled.Outcome = "Reconciled manually";
                        reconciled.AfterContext = record.AfterContext; reconciled.Actor = record.Reviewer; reconciled.ActionUtc = record.ActionUtc;
                        reconciled.Evidence = "Reviewer manually acknowledged completion; after context observed at confirmation. Linked audit: " + record.AuditId;
                        ReviewSession.WriteEntry(progress.DirectoryPath, reconciled.SessionId, reconciled);
                    }
                    completed++;
                    if (choice != null && fast.HoldLastReply) { held = current; Next(); }
                    else FinishResolution();
                    ActivityComplete(outcome + " decision saved for " + record.Reference + ". Reviewer: " + record.Reviewer + ". Note status: " +
                        (held != null && held.StableId == record.Id ? "reply held until your next action; " + FastKeys.Label(fast.KeyMap[FastAction.Undo]) + " undoes it" : progress.Get(record.Id).Resolution) + ".");
                }
                catch (InvalidOperationException ex)
                {
                    // Nothing was written or saved: a fast decision stays on its note, and the queue keeps going.
                    if (choice == null || committed) throw;
                    busy = false; ShowFastRefusal(ex.Message);
                }
                finally { busy = false; EnableActions(); }
            });
        }
        void FinishResolution()
        {
            FinishRecord(current); Next();
        }
        void BuildFastTab()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(ParatextTheme.Scale(2)) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Action<Control, SizeType, float> add = delegate(Control c, SizeType type, float height) { layout.RowStyles.Add(new RowStyle(type, height)); layout.Controls.Add(c, 0, layout.RowCount++); };
            Func<string, Label> caption = text => new Label { Text = text, AutoSize = true, Margin = new Padding(0, ParatextTheme.Scale(4), 0, ParatextTheme.Scale(1)) };
            fastKeys.Controls.Add(fastBand); fastKeys.Margin = new Padding(0, 0, 0, ParatextTheme.Scale(2));
            add(fastKeys, SizeType.Absolute, ParatextTheme.Scale(30));
            add(caption("Now"), SizeType.AutoSize, 0); add(fastBefore, SizeType.Percent, 32);
            add(caption("After the apply key"), SizeType.AutoSize, 0); add(fastAfter, SizeType.Percent, 32);
            fastReason.ForeColor = ParatextTheme.ColorOf("text_error");
            foreach (var label in new[] { fastSource, fastReason, fastComments }) add(ParatextTheme.Wrap(label, layout), SizeType.AutoSize, 0);
            add(caption("Your comment instead of the automatic one (F2 to type, Esc to return to keys)"), SizeType.AutoSize, 0); add(fastComment, SizeType.AutoSize, 0);
            add(caption("Reference texts (read-only)"), SizeType.AutoSize, 0); add(referenceBox, SizeType.Percent, 36);
            fastLegend.ForeColor = ParatextTheme.ColorOf("text_dimmed"); add(ParatextTheme.Wrap(fastLegend, layout), SizeType.AutoSize, 0);
            // A click anywhere outside the boxes gives the keyboard back to the queue.
            foreach (Control c in new Control[] { layout, fastBand, fastSource, fastReason, fastComments, fastLegend }) c.MouseDown += delegate { FocusKeys(); };
            fastTab.Controls.Add(layout);
        }
        Font tamilFont;
        readonly List<Font> fonts = new List<Font>();
        void ApplyFast(FastSettings settings, bool save)
        {
            fast = settings; if (save) fast.Save(progress.DirectoryPath);
            if (tamilFont == null || tamilFont.Name != fast.TamilFont || tamilFont.Size != fast.TamilSize) { tamilFont = new Font(fast.TamilFont, fast.TamilSize); fonts.Add(tamilFont); }
            fastBefore.Font = fastAfter.Font = referenceBox.Font = fastComment.Font = tamilFont;
            fastButton.Checked = fast.Enabled; fastLegend.Text = FastKeys.Legend(fast);
            unreadableReferences.Clear();
            if (fast.Enabled) { tabs.SelectedTab = fastTab; RefreshReferences(); RefreshFast(); FocusKeys(); }
            else if (tabs.SelectedTab == fastTab) tabs.SelectedTab = reviewTab;
            UpdateBand();
        }
        void ChooseFastSettings()
        {
            if (busy) return;
            using (var dialog = new FastReviewDialog(fast, owner.ReferenceSourceNames()))
                if (dialog.ShowDialog(this) == DialogResult.OK) {
                    ApplyFast(dialog.Result, true);
                    FastLog("Fast review " + (fast.Enabled ? "on" : "off") + "; notes without ready-to-paste Tamil: " + fast.NoLiteral + "; reply held for undo: " + fast.HoldLastReply + "; letter keys: " + fast.LetterKeys + ".");
                    if (fast.Enabled && current != null && !owner.EditorGrouped()) FastLog("The project editor is not in a scroll group, so each note opens with a window call that can take the keyboard. Put the editor in the queue's scroll group (" + owner.NavigationGroup + ") to keep the keys in the queue.");
                    if (fast.Enabled && current != null) { LoadCurrent(); }
                }
        }
        static bool ReadyToPaste(NoteCandidate item)
        { return !item.Unreadable && item.Proposal.ReviewedBulkLiteral && item.Proposal.Route != ProposalRoute.Structure && item.Proposal.Route != ProposalRoute.NoChange; }
        // Later round: notes without ready-to-paste Tamil stay open and untouched in this run. Recovery work is never skipped.
        bool LaterRound(NoteCandidate item, ReviewRecord record)
        {
            return fast.Enabled && fast.NoLiteral == NoLiteralMode.LaterRound && !scope.RecoveryOnly && !ReadyToPaste(item) &&
                !(record != null && (record.Outcome == "Pending apply" || record.ResolutionPending));
        }
        bool FastCan(string kind)
        {
            if (current == null || paused || busy || indexing || current.IsResolved || current.Unreadable || blocked || duplicateIds.Contains(current.StableId) || Pending()) return false;
            var record = progress.Get(current.StableId);
            if (record != null && record.Completed) return false;
            return kind != "Applied" || (fastChoice != null && fastChoice.Preview != null);
        }
        // Works out what the apply key would write for the current note, and shows it.
        void RefreshFast()
        {
            fastChoice = null; fastReason.Text = ""; fastSource.Text = "";
            if (!fast.Enabled || current == null) { ShowFast(); return; }
            var record = progress.Get(current.StableId);
            if (current.IsResolved || current.Unreadable || blocked || duplicateIds.Contains(current.StableId) || Pending() || (record != null && record.Completed))
            { fastReason.Text = record != null && record.Completed ? "Already decided: " + record.Outcome + ". Browse on, or use Recovery for pending work." : status.Text; ShowFast(); return; }
            string orig = original.Text, rep = replacement.Text;
            var reg = region.SelectedItem == null ? current.Proposal.Region : (ContentRegion)region.SelectedItem;
            bool edited = orig != current.Proposal.Original || rep != current.Proposal.Suggested || reg != current.Proposal.Region;
            string source;
            var prepared = fast.NoLiteral == NoLiteralMode.PreparedWording && !edited ? wording.Get(current.StableId) : null;
            if (deletion.Checked) { fastReason.Text = "Deleting text is reviewed in the Review tab."; ShowFast(); return; }
            if (current.Proposal.Route == ProposalRoute.Structure) { fastReason.Text = "Structural proposal: review it in the Review tab, or fix it in Paratext and press the fixed-by-hand key."; ShowFast(); return; }
            if (edited) source = "your wording from the Review tab";
            else if (prepared != null)
            {
                string why = null;
                try { if (WordingSheet.VerseHash(owner.CapturePassage(current, prepared.Region)) != prepared.VerseHash) why = "the verse changed after the wording was prepared"; }
                catch (InvalidOperationException ex) { why = ex.Message; }
                if (why != null) { fastReason.Text = "Prepared wording not used: " + why + ". Edit in Paratext, then press the fixed-by-hand key."; ShowFast(); return; }
                orig = prepared.Original; rep = prepared.Replacement; reg = prepared.Region;
                source = "PREPARED WORDING - not from the note (" + prepared.Sheet + "): " + prepared.Rationale;
            }
            else if (ReadyToPaste(current)) source = "the note's own Suggested text";
            else { fastReason.Text = "The note gives no ready-to-paste Tamil. Edit in Paratext and press the fixed-by-hand key, reject it, add a footnote, or skip."; ShowFast(); return; }
            try
            {
                var preview = owner.ForQueueItem(current).PreviewApply(current, orig, rep, reg);
                fastChoice = new FastChoice { Id = current.StableId, Original = orig, Replacement = rep, Region = reg, Preview = preview, Source = source, OwnWording = !edited && prepared == null };
                fastSource.Text = "Wording: " + source;
            }
            catch (InvalidOperationException ex) { fastReason.Text = "Apply is not available: " + ex.Message; }
            ShowFast();
        }
        // Why the apply key is not armed for the current note, as last worked out by RefreshFast.
        string ApplyUnavailable() { return fastChoice != null ? "" : fastReason.Text.Length > 0 ? fastReason.Text : "Nothing to apply for this note."; }
        void ShowFast()
        {
            if (fastChoice != null)
            {
                fastBefore.ShowChange(fastChoice.Preview.Before, fastChoice.Preview.After, false, ParatextTheme.ColorOf("background_light_red"));
                fastAfter.ShowChange(fastChoice.Preview.After, fastChoice.Preview.Before, true, ParatextTheme.ColorOf("background_light_green"));
            }
            else { fastBefore.ShowChange(fast.Enabled && current != null ? loadedContext : "", null, false, Color.Empty); fastAfter.ShowChange(fast.Enabled && current != null ? "No change is armed for the apply key." : "", null, true, Color.Empty); }
            fastSource.ForeColor = ParatextTheme.ColorOf(fastChoice != null && !fastChoice.OwnWording ? "text_warning" : "text_normal");
            fastComments.Text = !fast.Enabled || current == null ? "" :
                "Comment posted by " + FastKeys.Label(fast.KeyMap[FastAction.Apply]) + ": " + CommentText(FastComment("Applied")) + "\r\n" +
                FastKeys.Label(fast.KeyMap[FastAction.Reject]) + ": " + CommentText(FastComment("Rejected")) + "\r\n" +
                FastKeys.Label(fast.KeyMap[FastAction.Manual]) + ": " + CommentText(FastComment("Changed")) + " (or, if nothing changed: " + CommentText(FastComment("NoChange")) + ")" +
                (presetOverride > 0 ? "\r\nAlt+" + presetOverride + " chose comment " + presetOverride + " for this note; Alt+0 returns to automatic." : "");
            UpdateBand();
            // Keys pressed before this before/after was painted are discarded (see ProcessCmdKey).
            fastBefore.Update(); fastAfter.Update(); fastComments.Update();
            armedAt = Environment.TickCount;
        }
        int armedAt = Environment.TickCount;
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern int GetMessageTime();
        static string CommentText(string comment) { return comment.Length == 0 ? "Accepted as suggested." : comment; }
        string FastComment(string kind)
        {
            string typed = fastComment.Text.Trim();
            if (typed.Length > 0) return typed;
            if (presetOverride > 0) return CommentPresets.Values[presetOverride - 1];
            switch (kind)
            {
                case "Applied": return fastChoice != null && fastChoice.OwnWording ? "" : CommentPresets.Values[1];
                case "Rejected": return current == null ? "" : CommentPresets.Values[fast.RejectPreset(current.Proposal.Category) - 1];
                case "Changed": return CommentPresets.Values[1];
                case "NoChange": return CommentPresets.Values[4];
                default: return "";
            }
        }
        void ShowFastRefusal(string message)
        {
            RefreshFast();
            fastReason.Text = message + (fastReason.Text.Length > 0 ? "\r\n" + fastReason.Text : "");
            status.Text = message + " The note stays open here.";
            FastLog("Refused at " + (current == null ? "?" : current.DisplayReference) + ": " + message);
            ActivityComplete("Nothing written: " + message);
        }
        bool EditingText()
        {
            Control c = ActiveControl;
            while (c is ContainerControl && ((ContainerControl)c).ActiveControl != null) c = ((ContainerControl)c).ActiveControl;
            var box = c as TextBoxBase;
            return box != null && !box.ReadOnly;
        }
        void FocusKeys() { if (tabs.SelectedTab == fastTab) fastKeys.Focus(); UpdateBand(); }
        // After a window call that may have activated the editor, take the keyboard back if the reviewer was using the queue.
        void KeepKeys() { if (fast.Enabled && navigationHadFocus && tabs.SelectedTab == fastTab) { Reveal(); fastKeys.Focus(); } }
        void FocusLeft()
        {
            if (fast.Enabled && navigationPhase != NavigationPhase.Idle && navigationHadFocus && current != null) FastLog("The keyboard left the queue while opening " + current.DisplayReference + ".");
            UpdateBand();
        }
        void UpdateBand()
        {
            bool editing = EditingText(), active = fast.Enabled && tabs.SelectedTab == fastTab && ContainsFocus && !editing;
            fastBand.Text = !fast.Enabled ? "Fast review is off. Turn it on with Fast review... on the toolbar." :
                active ? "KEYS ACTIVE" + (held != null ? " - reply for " + held.DisplayReference + " held; " + FastKeys.Label(fast.KeyMap[FastAction.Undo]) + " undoes it" : "") :
                editing ? "Typing a comment - Esc returns to the keys" : "Keys inactive - click here to give the keyboard to the queue";
            fastBand.BackColor = ParatextTheme.ColorOf(active ? "background_light_green" : "background_help");
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            const int KeyDown = 0x100, SystemKeyDown = 0x104;
            if (msg.Msg == KeyDown || msg.Msg == SystemKeyDown)
            {
                // A key pressed while the previous decision was being written waits in the message queue.
                // It must not decide the next note, whose before/after was not yet on screen when it was pressed.
                int preset;
                if (fast.Enabled && tabs.SelectedTab == fastTab && unchecked(GetMessageTime() - armedAt) < 0 &&
                    FastKeys.Action(keyData, fast, out preset) != FastAction.None && !(EditingText() && FastKeys.TypesInText(keyData)))
                { FastLog("Ignored " + keyData + ": it was pressed before this note was shown."); return true; }
                // Bit 30 of a key-down message marks auto-repeat: holding a key never decides a second note.
                if (FastKey(keyData, (msg.LParam.ToInt64() & 0x40000000) != 0)) return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        internal bool FastKey(Keys keyData, bool repeat)
        {
            if (probing) FastLog("Key reached the queue: " + keyData + (repeat ? " (repeat)" : ""));
            if (!fast.Enabled || tabs.SelectedTab != fastTab) return false;
            int preset; var action = FastKeys.Action(keyData, fast, out preset);
            if (action == FastAction.None) return false;
            if (EditingText() && action != FastAction.BackToKeys && FastKeys.TypesInText(keyData)) return false;
            if (repeat || busy) return true;
            switch (action)
            {
                case FastAction.Apply:
                    if (!FastCan("Applied")) { status.Text = ApplyUnavailable(); break; }
                    fastChoice.Comment = FastComment("Applied"); DecideWith("Applied", fastChoice); break;
                case FastAction.Reject:
                    if (!FastCan("Rejected")) { FastNotice("This note cannot be rejected now: it is decided, resolved, blocked or waiting in Recovery."); break; }
                    DecideWith("Rejected", DecisionChoice(FastComment("Rejected"))); break;
                case FastAction.Manual: Guard(FixedByHand); break;
                case FastAction.Footnote: Guard(ComposeFootnote); break;
                case FastAction.Skip: if (later.Enabled) Guard(delegate { FlushHeld(); SaveDraft(); Next(); }); break;
                case FastAction.Previous: Guard(delegate { Browse(-1); }); break;
                case FastAction.Next: Guard(delegate { Browse(1); }); break;
                case FastAction.Return: if (back.Enabled) Guard(NavigateCurrent); break;
                case FastAction.Preset: presetOverride = preset; ShowFast(); break;
                case FastAction.EditComment: fastComment.Focus(); fastComment.SelectAll(); UpdateBand(); break;
                case FastAction.BackToKeys: FocusKeys(); ShowFast(); break;
                case FastAction.Undo: Guard(Undo); break;
            }
            return true;
        }
        void FastNotice(string message) { fastReason.Text = message.Trim(); status.Text = message.Trim(); }
        FastChoice DecisionChoice(string comment)
        {
            return new FastChoice { Id = current.StableId, Original = original.Text, Replacement = replacement.Text, Comment = comment, Source = "key press",
                Region = region.SelectedItem == null ? current.Proposal.Region : (ContentRegion)region.SelectedItem };
        }
        void FixedByHand()
        {
            if (!FastCan("Completed manually")) { FastNotice("This note cannot be recorded now: it is decided, resolved, blocked or waiting in Recovery."); return; }
            var choice = DecisionChoice("");
            bool changed = owner.CapturePassage(current, choice.Region) != loadedContext;
            if (!changed && !manualWarned)
            {
                manualWarned = true;
                FastNotice("No saved change in this passage since the note opened. Save your edit in Paratext first, or press " + FastKeys.Label(fast.KeyMap[FastAction.Manual]) + " again to record that no change is needed.");
                return;
            }
            choice.Comment = FastComment(changed ? "Changed" : "NoChange");
            DecideWith("Completed manually", choice);
        }
        void ComposeFootnote()
        {
            if (!FastCan("Footnote")) { FastNotice("A footnote cannot be added for this note now: it is decided, resolved, blocked or waiting in Recovery."); return; }
            var item = current;
            using (var composer = new FootnoteComposer(item, FootnotePresets.Load(progress.DirectoryPath), (text, afterOriginal) => owner.ForQueueItem(item).PreviewFootnote(item, text, afterOriginal),
                owner.EditorSelectionText, ReferenceSelection, templates => FootnotePresets.Save(progress.DirectoryPath, templates), tamilFont))
            {
                if (composer.ShowDialog(this) != DialogResult.OK || composer.Plan == null || !Object.ReferenceEquals(item, current)) { FocusKeys(); return; }
                var plan = composer.Plan; string prefix = FastComment("Footnote");
                DecideWith("Applied", new FastChoice { Id = item.StableId, Original = plan.Target, Replacement = plan.AfterOriginal ? plan.Target + plan.Usfm : plan.Usfm, Region = ContentRegion.Body,
                    Preview = plan.Preview, Footnote = plan, Source = "footnote", Comment = (prefix.Length > 0 ? prefix + " " : "") + "Footnote added; verse wording unchanged: " + plan.Text });
            }
            FocusKeys();
        }
        // Posts the held reply and resolves its note, as an immediate decision would have.
        void FlushHeld()
        {
            if (held == null) return;
            var item = held; held = null;
            var record = progress.Get(item.StableId);
            if (record == null || !record.ResolutionPending) { UpdateBand(); return; }
            ContentRegion region;
            // An applied edit that is no longer in the text (for example reverted with Revert last plugin edit)
            // must not be reported as applied: the note reopens instead.
            if (record.Outcome == "Applied" && record.BeforeContext.Length > 0 && record.BeforeContext != record.AfterContext &&
                Enum.TryParse(record.Region, out region) && owner.CapturePassage(item, region) == record.BeforeContext)
            {
                Reopen(item, record, "The applied text was no longer in the passage when its reply was due; it was reverted outside fast review. No reply was posted.", record.BeforeContext);
                FastLog("Reply not posted for " + item.DisplayReference + ": its applied text had been reverted. The note is open again.");
                UpdateBand(); return;
            }
            FinishRecord(item);
            UpdateBand();
        }
        // Records that a held decision did not stand, and restores the note's own wording for the next look at it.
        void Reopen(NoteCandidate item, ReviewRecord record, string why, string passageNow)
        {
            var undone = ReviewRecord.FromXml(record.ToXml());
            undone.Outcome = "Undone"; undone.Resolution = ""; undone.Actor = owner.Reviewer; undone.ActionUtc = DateTime.UtcNow.ToString("O"); undone.AfterContext = passageNow;
            undone.Original = item.Proposal.Original; undone.Replacement = item.Proposal.Suggested; undone.Region = item.Proposal.Region.ToString();
            undone.Evidence = why + " The note stays open. Undone decision: " + record.Outcome + (record.Outcome == "Applied" ? " (" + record.Original + " -> " + record.Replacement + ")" : "") + ".";
            progress.Save(undone); session.Track(undone);
            completed = Math.Max(0, completed - 1);
        }
        void Undo()
        {
            if (busy || indexing) return;
            if (paused) { FastNotice("Resume the queue before undoing."); return; }
            if (held == null)
            {
                FastNotice("Nothing to undo. A posted reply and a resolved note cannot be reopened by plugins; restore text through Paratext project history and reopen the note there.");
                return;
            }
            busy = true; EnableActions();
            try
            {
                var item = held; var record = progress.Get(item.StableId);
                if (record == null || !record.ResolutionPending) { held = null; throw new InvalidOperationException("The reply for " + item.DisplayReference + " was already posted. Nothing was undone."); }
                if (record.Outcome == "Applied")
                    owner.ForQueueItem(item).RevertDecision(item, record, delegate {
                        // From here the text is uncertain until the revert is verified: this decision's reply must never be posted.
                        held = null;
                        var uncertain = ReviewRecord.FromXml(record.ToXml());
                        uncertain.Outcome = "Pending apply"; uncertain.Resolution = ""; uncertain.Actor = owner.Reviewer; uncertain.ActionUtc = DateTime.UtcNow.ToString("O");
                        uncertain.Evidence = "Undo of this applied decision was submitted but not yet verified. Inspect the passage and Paratext history, then reconcile in Recovery.";
                        progress.Save(uncertain); session.Track(uncertain);
                    });
                held = null;
                Reopen(item, record, record.Outcome == "Applied" ? "Undone before the note reply was posted: the verified Scripture edit was reverted and the restored passage verified." :
                    "Undone before the note reply was posted; no Scripture edit was involved.", record.Outcome == "Applied" ? record.BeforeContext : record.AfterContext);
                FastLog("Undone: " + record.Outcome + " at " + item.DisplayReference + ".");
                int index = items.FindIndex(c => c.StableId == item.StableId);
                if (index >= 0) { position = index; paused = false; pause.Text = "Pause"; LoadCurrent(); }
                status.Text = "Undone: " + record.Outcome + " at " + item.DisplayReference + ". The note is open again" + (record.Outcome == "Applied" ? " and its text restored." : ".");
            }
            finally { busy = false; EnableActions(); }
        }
        void FastLog(string line)
        {
            fastLog.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + line);
            if (fastLog.Count > 400) fastLog.RemoveRange(0, fastLog.Count - 400);
            RefreshQueueLog();
        }
        void RefreshReferences()
        {
            referenceTexts.Clear();
            if (current == null || fast.References.Count == 0) { referenceBox.Text = fast.References.Count == 0 ? "No reference texts chosen. Choose them in Fast review settings." : ""; return; }
            var blocks = new List<string>();
            foreach (string name in fast.References)
            {
                string why;
                if (unreadableReferences.TryGetValue(name, out why)) { blocks.Add(name + ": " + why); continue; }
                try { string label; string text = owner.ReadReference(name, current, out label); referenceTexts[name] = text; blocks.Add(label + "\r\n" + (text.Length == 0 ? "(no text at this reference)" : text)); }
                catch (Exception ex)
                {
                    why = "not readable by plugins (" + ex.GetType().Name + "). Use Tools > Open reference windows; they follow the queue's scroll group.";
                    unreadableReferences[name] = why; blocks.Add(name + ": " + why);
                }
            }
            referenceBox.Text = String.Join("\r\n\r\n", blocks);
        }
        // The selected reference words, or else the first Tamil reference text, for the footnote composer.
        string ReferenceSelection()
        {
            if (referenceBox.SelectedText.Trim().Length > 0) return referenceBox.SelectedText;
            return referenceTexts.Values.FirstOrDefault(t => t.Any(c => c >= '஀' && c <= '௿')) ?? "";
        }
        void OpenReferenceWindows()
        {
            if (current == null) { status.Text = "Open a note first."; return; }
            foreach (string line in owner.OpenReferenceWindows(fast.References, current)) FastLog(line);
            FastLog("Put the reference windows in the queue's scroll group (" + owner.NavigationGroup + ") so they follow each note.");
            Reveal(); FocusKeys();
        }
        void RunProbe()
        {
            if (busy) return;
            probing = !probing; probe.Text = probing ? "Stop host probe" : "Host probe";
            if (!probing) { FastLog("Host probe stopped."); return; }
            FastLog("HOST PROBE - nothing is written. Resource text is never logged.");
            foreach (string line in owner.ProbeHost(current)) FastLog(line);
            FastLog("Queue is " + (embedded ? "docked" : "floating") + "; it has the keyboard: " + ContainsFocus + ". Now press the fast review keys, use Next and Previous, and edit in Paratext; keys that reach the queue and focus changes are listed here.");
            tabs.SelectedTab = logTab;
        }
        IEnumerable<NoteCandidate> OpenUndecided()
        {
            return items.Where(c => !c.IsResolved && !c.Unreadable && !duplicateIds.Contains(c.StableId) &&
                (progress.Get(c.StableId) == null || (!progress.Get(c.StableId).Completed && progress.Get(c.StableId).Outcome != "Pending apply")));
        }
        void ExportWording()
        {
            if (busy || indexing) return;
            var open = OpenUndecided().ToList();
            if (open.Count == 0) { status.Text = "No open, undecided notes in this scope to export."; return; }
            using (var dialog = new SaveFileDialog { Filter = "Wording export|*.xml", FileName = "Wording-export-" + owner.ProjectName + "-" + DateTime.Now.ToString("yyyyMMdd") + ".xml" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                busy = true; EnableActions(); Activity("Exporting notes for wording", 0, 0);
                List<string> skipped;
                try { skipped = wording.Export(dialog.FileName, owner.ProjectId, owner.ProjectName, open, owner); }
                finally { busy = false; EnableActions(); }
                foreach (string line in skipped) FastLog("Not exported: " + line);
                ActivityComplete("Exported " + (open.Count - skipped.Count) + " open notes with their current verse text" + (skipped.Count > 0 ? "; " + skipped.Count + " left out (see the Log tab)" : "") + ". Nothing in the project changed.", true);
            }
        }
        void ImportWording()
        {
            if (busy || indexing) return;
            using (var dialog = new OpenFileDialog { Filter = "Wording sheet|*.xml" }) if (dialog.ShowDialog(this) == DialogResult.OK) ImportWordingFile(dialog.FileName);
        }
        void ImportWordingFile(string path)
        {
            if (busy || indexing) return;
            var sheet = WordingSheet.Read(path, owner.ProjectId);
            var accepted = new List<WordingEntry>(); var log = new List<string>(); var ids = new HashSet<string>(); var books = new Dictionary<int, List<NoteCandidate>>();
            busy = true; EnableActions();
            try
            {
                int checkedCount = 0;
                foreach (var w in sheet)
                {
                    Activity("Checking wording sheet", ++checkedCount, sheet.Count);
                    string reason = WordingSheet.StaticReason(w); NoteCandidate item = null;
                    if (reason == null && !ids.Add(w.Id)) reason = "repeated in the sheet";
                    var exported = reason == null ? wording.Exported(w.Id) : null;
                    if (reason == null && exported == null) reason = "unknown proposal id (not in an export from this queue)";
                    if (reason == null)
                    {
                        ContentRegion exportedRegion; if (Enum.TryParse((string)exported.Attribute("region"), out exportedRegion)) w.Region = exportedRegion;
                        int book = (int)exported.Attribute("book"); List<NoteCandidate> open;
                        if (!books.TryGetValue(book, out open)) books[book] = open = owner.ReadQueueBook(book);
                        item = open.FirstOrDefault(c => c.StableId == w.Id && !c.Unreadable);
                        if (item == null) reason = "the note is no longer open, or its text changed";
                    }
                    var prior = reason == null ? progress.Get(w.Id) : null;
                    if (prior != null && (prior.Completed || prior.Outcome == "Pending apply")) reason = "already decided";
                    if (reason == null && WordingSheet.VerseHash(owner.CapturePassage(item, w.Region)) != w.VerseHash) reason = "the verse changed since the export";
                    if (reason == null && (owner.Normalize(w.Original) != w.Original || owner.Normalize(w.Replacement) != w.Replacement)) reason = "not in the project's Unicode normalization";
                    if (reason == null) try { owner.ForQueueItem(item).PreviewApply(item, w.Original, w.Replacement, w.Region); } catch (InvalidOperationException ex) { reason = ex.Message; }
                    if (reason == null) accepted.Add(w);
                    log.Add((item != null ? item.DisplayReference : w.Id.Length > 12 ? w.Id.Substring(0, 12) : w.Id) + ": " + (reason == null ? "accepted" : "ignored - " + reason));
                }
                wording.Accept(accepted); wording.Log(log);
                foreach (string line in log.Where(l => !l.EndsWith(": accepted"))) FastLog("Wording sheet: " + line);
                string summary = "Wording sheet " + Path.GetFileName(path) + ": " + accepted.Count + " accepted, " + (sheet.Count - accepted.Count) + " ignored. Ignored entries and reasons are in the Log tab. Accepted wording is shown, labelled, only in Prepared wording mode, and applied only by your key press.";
                session.AddWarning(summary);
                ActivityComplete(summary, true);
            }
            finally { busy = false; EnableActions(); }
            RefreshFast();
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
            FlushHeld(); SaveDraft();
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
            int previewCount = 0;
            foreach (var item in selected) {
                Activity("Checking selected corrections", ++previewCount, selected.Count);
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
                status.Text = "No selected corrections can be safely previewed. " + skipped + " skipped and left unresolved. Use Manual review for saved proposals and reasons."; ActivityComplete(status.Text, true);
                return;
            }
            ActivityComplete("Preview ready: " + eligible.Count + " eligible; " + skipped + " skipped. No Scripture edits yet.", true);
            ShowCorrectionPreview(eligible, previews, skipped, null, "selected corrections", decisionComment);
        }
        void BulkSandhi()
        { BulkCorrections("Sandhi / word-joining", "Sandhi"); }
        void BulkConfirmedTypos()
        { BulkCorrections("Confirmed typo", "confirmed typos"); }
        void BulkCorrections(string category, string label)
        {
            if (busy || paused || indexing || scope.RecoveryOnly) return;
            FlushHeld(); SaveDraft();
            var previews = new Dictionary<string, PassageEvidence>();
            var eligible = new List<NoteCandidate>(); var skipped = new List<string>();
            var candidates = items.Where(c => c.Proposal.Category == category && !c.IsResolved).ToList(); int checkedCount = 0;
            foreach (var item in candidates)
            {
                Activity("Checking " + label, ++checkedCount, candidates.Count);
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
                session.AddWarning(summary); status.Text = summary + " Use Manual review for saved proposals and reasons."; ActivityComplete(status.Text, true); return;
            }
            ActivityComplete("Preview ready: " + eligible.Count + " eligible; " + skipped.Count + " skipped. No Scripture edits yet.", true);
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
                        status.Text = "Applying " + label + " " + (++processed) + " / " + selectedItems.Count + " - " + item.DisplayReference; status.Refresh(); Activity("Applying " + label, processed, selectedItems.Count);
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
                            Comment = decisionComment.Trim(), Region = item.Proposal.Region.ToString(), Discussion = item.DiscussionKey,
                            SessionId = session.Id, AuditId = Guid.NewGuid().ToString("N"), Reviewer = owner.Reviewer, Actor = owner.Reviewer,
                            DecisionUtc = DateTime.UtcNow.ToString("O"), ActionUtc = DateTime.UtcNow.ToString("O"), Resolution = "Pending",
                            QueueScope = scope.Describe(owner.QueueBookCode), SourceNote = item.NoteText };
                        service.Apply(item, record.Original, record.Replacement, item.Proposal.Region, reviewedSelection, false, delegate {
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
                ActivityComplete(status.Text, true);
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
                    status.Text = "Saving bulk decision " + (++processed) + " / " + selectedItems.Count + " - " + item.DisplayReference; status.Refresh(); Activity("Saving bulk decisions", processed, selectedItems.Count);
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
                ActivityComplete(status.Text, true);
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
