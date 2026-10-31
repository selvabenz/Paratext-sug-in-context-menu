using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;

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
        readonly Timer indexTimer = new Timer { Interval = 30 };
        readonly Timer draftTimer = new Timer { Interval = 600 };
        readonly Label heading = new Label(), status = new Label();
        readonly TextBox original = Box(65), replacement = Box(65), source = Box(180), comment = Box(65);
        readonly ComboBox region = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
        readonly CheckBox reviewed = new CheckBox { Text = "I reviewed this exact correction", AutoSize = true };
        readonly CheckBox deletion = new CheckBox { Text = "Explicitly delete the original text", AutoSize = true };
        readonly Button apply = Button("Apply and next"), reject = Button("Reject and next"), manual = Button("Completed manually / no change"), retry = Button("Verified no edit; allow retry"), pause = Button("Pause"), later = Button("Leave for later"), back = Button("Return to queued passage");
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
            reportProjectDirectory = reportDirectory;
            string savedFolder = Path.Combine(progress.DirectoryPath, "report-folder.xml");
            if (reportProjectDirectory == null && File.Exists(savedFolder)) reportProjectDirectory = XElement.Load(savedFolder).Value;
            if (reportProjectDirectory == null || !ProjectReportDirectory.Matches(reportProjectDirectory, owner.ProjectId)) reportProjectDirectory = ProjectReportDirectory.Find(owner.ProjectName, owner.ProjectId);
            Text = "Note review queue - " + owner.ProjectName;
            Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
            var area = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(760, area.Width - 30), Math.Min(900, area.Height - 35));
            MinimumSize = new Size(450, 400); StartPosition = FormStartPosition.CenterScreen;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(10) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
            heading.AutoSize = true; heading.MaximumSize = new Size(690, 0); root.Controls.Add(heading);
            var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true, Padding = new Padding(0, 8, 15, 0) };
            source.ReadOnly = true; source.Dock = DockStyle.Fill;
            Add(fields, "Target region", region); region.Items.AddRange(Enum.GetValues(typeof(ContentRegion)).Cast<object>().ToArray());
            original.Height = replacement.Height = 48; comment.Height = 48;
            Add(fields, "Exact original", original); Add(fields, "Chosen literal replacement", replacement);
            Add(fields, "Decision comment - choose a phrase or type/edit below", commentPreset);
            commentPreset.Items.Add("Choose a reusable decision phrase..."); commentPreset.Items.AddRange(CommentPresets.Values); commentPreset.SelectedIndex = 0;
            fields.Controls.Add(comment);
            fields.Controls.Add(deletion); fields.Controls.Add(reviewed);
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var correctionTab = new TabPage("Correction and decision"); correctionTab.Controls.Add(fields);
            var sourceTab = new TabPage("Full note and discussion"); sourceTab.Controls.Add(source);
            tabs.TabPages.Add(correctionTab); tabs.TabPages.Add(sourceTab); root.Controls.Add(tabs);
            var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
            foreach (var b in new[] { apply, reject, manual, retry, resolve, later, back, pause }) actions.Controls.Add(b);
            var export = Button("Export decisions..."); actions.Controls.Add(export); actions.Controls.Add(viewReport); actions.Controls.Add(reportFolder); root.Controls.Add(actions);
            status.Dock = DockStyle.Fill; root.Controls.Add(status); Controls.Add(root);
            status.Text = "Indexing unresolved AI-QA notes, one book at a time. Pause or close to stop. No Scripture changes occur during indexing.";
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
            back.Click += delegate { Guard(delegate { if (current != null) owner.Navigate(current); }); };
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
            Shown += delegate { indexTimer.Start(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (busy) { e.Cancel = true; return; }
                try { SaveDraft(); GenerateReports("Closed", true); } catch (Exception ex) { e.Cancel = true; Fail(ex); }
            };
            EnableActions();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !resourcesDisposed)
            {
                resourcesDisposed = true; indexTimer.Dispose(); draftTimer.Dispose();
                if (progress != null) progress.Dispose();
            }
            base.Dispose(disposing);
        }
        static TextBox Box(int height) { return new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Height = height, Dock = DockStyle.Top }; }
        static Button Button(string text) { return new Button { Text = text, AutoSize = true }; }
        static void Add(TableLayoutPanel panel, string label, Control control)
        { panel.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 7, 0, 0) }); panel.Controls.Add(control); }
        void Guard(Action action) { try { action(); } catch (Exception ex) { Fail(ex); } }
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
            if (session.Records().Count == 0 && !ReviewSession.NeedsRecovery(progress.DirectoryPath, session.Id)) return null;
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
                    if (c.IsResolved && (saved == null || !saved.Completed || String.IsNullOrEmpty(saved.Resolution))) continue;
                    if (!seen.Add(c.StableId)) duplicateIds.Add(c.StableId);
                    items.Add(c);
                }
                books.Dequeue();
                heading.Text = owner.ProjectName + " - indexed " + items.Count + " proposals; " + books.Count + " books remaining";
                return;
            }
            indexTimer.Stop(); indexing = false;
            int missing = progress.Outstanding().Count(r => !seen.Contains(r.Id));
            if (missing > 0) recoveryNotice = "\r\n" + missing + " interrupted decision(s) have missing/changed sources. Export decisions and inspect Paratext history/notes manually.";
            items.Sort((a, b) => { int n = a.Book.CompareTo(b.Book); if (n == 0) n = a.Chapter.CompareTo(b.Chapter); if (n == 0) n = a.FirstVerse.CompareTo(b.FirstVerse); return n == 0 ? StringComparer.Ordinal.Compare(a.StableId, b.StableId) : n; });
            completed = items.Count(c => { var r = progress.Get(c.StableId); return r != null && r.Completed; });
            var cursor = progress.Cursor();
            if (cursor != null) { int found = items.FindIndex(c => c.StableId == cursor.Id); var saved = progress.Get(cursor.Id); if (found >= 0 && (saved == null || !saved.Completed || saved.ResolutionPending)) position = found - 1; }
            Next();
        }
        void Next()
        {
            draftTimer.Stop(); current = null; blocked = false;
            while (++position < items.Count)
            {
                var r = progress.Get(items[position].StableId); if (r != null && r.Completed && !r.ResolutionPending) continue;
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
            try { current = owner.ReloadQueueItem(current, ResolutionPending()); }
            catch (Exception ex) { blocked = true; status.Text = ex.Message; }
            if (duplicateIds.Contains(current.StableId)) blocked = true;
            var previous = progress.Get(current.StableId);
            var draft = progress.Cursor();
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
            heading.Text = owner.ProjectName + " | " + current.DisplayReference + " | " + current.Proposal.Pass + " | " + current.Proposal.Category + "\r\n" + completed + " / " + items.Count + " reviewed - " + current.Proposal.Route + recoveryNotice;
            if (!blocked) status.Text = ResolutionPending() ? "Decision saved locally. Retry note resolution to finish; the Scripture correction will not be repeated." : Pending() ? "An earlier apply did not finish recording. Inspect Paratext history and the passage, then confirm completed manually or confirm no edit before retrying." : "Waiting for Apply or Reject. Browse references freely. Comments/progress stay local; the note resolves when all its proposals have decisions.";
            else status.Text = "Source changed, unavailable, or duplicate identity. Automatic Apply is blocked. Inspect in Paratext; leave for later or record a commented manual decision.";
            SaveDraft(); EnableActions();
            owner.Navigate(current);
            Activate();
        }
        bool Pending() { var r = current == null ? null : progress.Get(current.StableId); return r != null && r.Outcome == "Pending apply"; }
        bool ResolutionPending() { var r = current == null ? null : progress.Get(current.StableId); return r != null && r.ResolutionPending; }
        ReviewRecord Record(string outcome)
        {
            return new ReviewRecord { Id = current.StableId, Outcome = outcome, Reference = current.DisplayReference, Category = current.Proposal.Category,
                Original = original.Text, Replacement = deletion.Checked ? "" : replacement.Text, Comment = comment.Text, Discussion = current.DiscussionKey, BeforeContext = loadedContext, Region = region.SelectedItem == null ? "" : region.SelectedItem.ToString() };
        }
        void SaveDraft()
        {
            if (current == null || populating) return;
            var draft = Record(deletion.Checked ? "Draft deletion" : "Draft");
            var prior = progress.Get(current.StableId);
            // Keep unresolved write intent and completed decisions intact.
            if (prior == null || (!prior.Completed && prior.Outcome != "Pending apply")) progress.Save(draft);
            progress.SaveCursor(draft);
        }
        void EnableActions()
        {
            bool ready = current != null && !paused && !busy && !indexing;
            bool pending = current != null && Pending();
            bool decisionSaved = current != null && (progress.Get(current.StableId) ?? new ReviewRecord()).Completed;
            bool duplicate = current != null && duplicateIds.Contains(current.StableId);
            apply.Enabled = ready && !decisionSaved && !pending && !blocked && reviewed.Checked && current.Proposal.Route != ProposalRoute.NoChange && (replacement.Text.Length > 0 || deletion.Checked);
            reject.Enabled = ready && !decisionSaved && !pending && !duplicate && comment.Text.Trim().Length > 0;
            manual.Enabled = ready && !decisionSaved && !duplicate && reviewed.Checked && comment.Text.Trim().Length > 0;
            retry.Visible = pending; retry.Enabled = ready && pending && comment.Text.Trim().Length > 0;
            resolve.Visible = ResolutionPending(); resolve.Enabled = ready && ResolutionPending() && !duplicate;
            later.Enabled = ready; back.Enabled = current != null && !busy;
            pause.Enabled = !busy;
            viewReport.Enabled = reportFolder.Enabled = !busy;
            commentPreset.Enabled = !busy && !decisionSaved && current != null;
            original.Enabled = replacement.Enabled = comment.Enabled = region.Enabled = reviewed.Enabled = deletion.Enabled = !busy && !decisionSaved;
            replacement.Enabled = !busy && !decisionSaved && !deletion.Checked;
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
                    progress.Save(record); session.Track(record);
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
            var record = progress.Get(current.StableId);
            if (record == null || !record.ResolutionPending) throw new InvalidOperationException("A saved decision is required before resolving the note.");
            List<string> ids;
            bool resolved = owner.ResolveReviewedThread(current, record.Discussion, delegate(string id) { var r = progress.Get(id); return r != null && r.Completed; }, out ids);
            if (resolved)
                foreach (string id in ids.Where(id => id != current.StableId))
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
            progress.Save(finished); session.Track(finished); Next();
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
