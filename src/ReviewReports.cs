using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;

namespace ParatextNoteSuggestions
{
    public static class CommentPresets
    {
        // Compiled reusable decisions, not quotations or frequency claims about private notes.
        public static readonly string[] Values = {
            "Accepted - applied the suggested correction.",
            "Accepted with revision - used the reviewed wording after checking the reference.",
            "Rejected - the existing wording is correct in context.",
            "Rejected - the suggestion would change the intended meaning.",
            "No change needed - already corrected or correct as written."
        };
    }
    public static class ProjectReportDirectory
    {
        static string Identity(string value) { return new string((value ?? "").Where(Char.IsLetterOrDigit).ToArray()).ToUpperInvariant(); }
        public static bool Matches(string directory, string projectId)
        {
            try
            {
                using (var reader = XmlReader.Create(Path.Combine(directory, "Settings.xml"), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                    return Identity((string)XElement.Load(reader).Element("Guid")) == Identity(projectId) && Identity(projectId).Length > 0;
            }
            catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; } catch (XmlException) { return false; }
        }
        public static string Find(string name, string id)
        {
            if (String.IsNullOrEmpty(name) || name != Path.GetFileName(name)) return null;
            foreach (string root in new[] { Path.Combine(Path.GetPathRoot(Environment.SystemDirectory), "My Paratext 9 Projects"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Paratext 9 Projects"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), "Paratext 9 Projects") })
            {
                string candidate = Path.Combine(root, name); if (Matches(candidate, id)) return Path.GetFullPath(candidate);
            }
            return null;
        }
        public static string ReportFolder(string directory, string projectId)
        {
            if (!Matches(directory, projectId)) throw new InvalidOperationException("Select the original project folder: its Settings.xml identity must match this queue's project.");
            string folder = Path.Combine(Path.GetFullPath(directory), "Review Queue");
            if (Directory.Exists(folder) && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("The Review Queue folder is a redirected link. Choose an ordinary folder inside the project.");
            return folder;
        }
    }
    public sealed class ReviewSession
    {
        readonly string root, projectId, projectName, reviewer;
        public readonly string Id;
        readonly DateTime started;
        string lastDigest = "", lastReport = "";
        public ReviewSession(string progressRoot, string id, string name, string user)
        {
            root = progressRoot; projectId = id; projectName = name; reviewer = user;
            Id = Guid.NewGuid().ToString("N"); started = DateTime.UtcNow;
            Directory.CreateDirectory(SessionDirectory(root, Id)); SaveManifest("");
        }
        static string SessionDirectory(string root, string session) { ValidateIds(session, session); return Path.Combine(root, "sessions", session); }
        public static void ValidateIds(string session, string audit)
        {
            Guid value;
            if (!Guid.TryParseExact(session, "N", out value) || !Guid.TryParseExact(audit, "N", out value)) throw new InvalidOperationException("Invalid session/audit identity. Review stopped to protect its local history.");
        }
        internal static string EntryPath(string root, string session, string audit)
        { ValidateIds(session, audit); return Path.Combine(SessionDirectory(root, session), audit + ".entry.xml"); }
        internal static void WriteEntry(string root, string session, ReviewRecord record)
        {
            string path = EntryPath(root, session, record.AuditId); Directory.CreateDirectory(Path.GetDirectoryName(path)); ReviewProgress.AtomicWrite(path, record.ToXml());
        }
        public void Track(ReviewRecord record) { if (record.AuditId.Length > 0) WriteEntry(root, Id, record); }
        public List<ReviewRecord> Records()
        { return Directory.EnumerateFiles(SessionDirectory(root, Id), "*.entry.xml").Select(f => ReviewRecord.FromXml(XElement.Load(f))).OrderBy(r => r.DecisionUtc, StringComparer.Ordinal).ThenBy(r => r.AuditId, StringComparer.Ordinal).ToList(); }
        void SaveManifest(string ended)
        {
            ReviewProgress.AtomicWrite(Path.Combine(SessionDirectory(root, Id), "session.xml"), new XElement("session", new XAttribute("id", Id),
                new XElement("projectId", projectId), new XElement("projectName", projectName), new XElement("reviewer", reviewer), new XElement("started", started.ToString("O")),
                new XElement("ended", ended), new XElement("digest", lastDigest), new XElement("report", lastReport)));
        }
        public string WriteReport(string projectDirectory, int total, int reviewed, string reason)
        {
            var records = Records();
            if (records.Count == 0) { SaveManifest(reason == "Closed" ? DateTime.UtcNow.ToString("O") : ""); return null; }
            string folder = ProjectReportDirectory.ReportFolder(projectDirectory, projectId);
            string digest = NoteParser.Hash(String.Join("|", records.Select(r => r.ToXml().ToString())));
            if (digest == lastDigest && File.Exists(lastReport)) { SaveManifest(reason == "Closed" ? DateTime.UtcNow.ToString("O") : ""); return lastReport; }
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "Review-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Id.Substring(0, 8) + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".html");
            WriteHtml(path, ReviewReportHtml.Render(projectName, reviewer, Id, started.ToString("O"), DateTime.UtcNow.ToString("O"), reason, total, reviewed, records));
            lastDigest = digest; lastReport = path;
            ReviewProgress.AtomicWrite(Path.Combine(root, "latest-report.xml"), new XElement("report", path));
            SaveManifest(reason == "Closed" ? DateTime.UtcNow.ToString("O") : ""); return path;
        }
        internal static void WriteHtml(string path, string html)
        {
            string temp = path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { byte[] bytes = new UTF8Encoding(false).GetBytes(html); stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            File.Move(temp, path);
        }
        public static string Latest(string progressRoot)
        { string path = Path.Combine(progressRoot, "latest-report.xml"); return File.Exists(path) ? XElement.Load(path).Value : null; }
        public static bool NeedsRecovery(string root, string currentSession)
        {
            string sessions = Path.Combine(root, "sessions"); if (!Directory.Exists(sessions)) return false;
            return Directory.EnumerateDirectories(sessions).Where(d => Path.GetFileName(d) != currentSession).Any(d =>
                File.Exists(Path.Combine(d, "session.xml")) && String.IsNullOrEmpty((string)XElement.Load(Path.Combine(d, "session.xml")).Element("ended")) && Directory.EnumerateFiles(d, "*.entry.xml").Any());
        }
        public static int Recover(string progressRoot, string currentSession, string projectDirectory, string projectId)
        {
            string sessions = Path.Combine(progressRoot, "sessions"); if (!Directory.Exists(sessions)) return 0;
            int count = 0;
            foreach (string directory in Directory.EnumerateDirectories(sessions))
            {
                if (Path.GetFileName(directory) == currentSession) continue;
                string manifest = Path.Combine(directory, "session.xml"); if (!File.Exists(manifest)) continue;
                var m = XElement.Load(manifest); if (!String.IsNullOrEmpty((string)m.Element("ended"))) continue;
                if ((string)m.Element("projectId") != projectId) throw new InvalidOperationException("Recovered session belongs to a different project. Report generation stopped.");
                var records = Directory.EnumerateFiles(directory, "*.entry.xml").Select(f => ReviewRecord.FromXml(XElement.Load(f))).OrderBy(r => r.DecisionUtc).ToList();
                if (records.Count == 0) continue;
                string digest = NoteParser.Hash(String.Join("|", records.Select(r => r.ToXml().ToString())));
                if ((string)m.Element("digest") != digest || !File.Exists((string)m.Element("report")))
                {
                    string folder = ProjectReportDirectory.ReportFolder(projectDirectory, projectId); Directory.CreateDirectory(folder);
                    string path = Path.Combine(folder, "Review-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-recovered-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".html");
                    WriteHtml(path, ReviewReportHtml.Render((string)m.Element("projectName"), (string)m.Element("reviewer"), (string)m.Attribute("id"), (string)m.Element("started"), DateTime.UtcNow.ToString("O"), "Recovered interrupted session", 0, 0, records));
                    m.SetElementValue("report", path); m.SetElementValue("digest", digest); count++;
                    ReviewProgress.AtomicWrite(Path.Combine(progressRoot, "latest-report.xml"), new XElement("report", path));
                }
                m.SetElementValue("ended", DateTime.UtcNow.ToString("O")); ReviewProgress.AtomicWrite(manifest, m);
            }
            return count;
        }
    }
    public static class ReviewReportHtml
    {
        static string H(string value) { return WebUtility.HtmlEncode(value ?? ""); }
        static string[] Elements(string text)
        { var parts = new List<string>(); var e = StringInfo.GetTextElementEnumerator(text); while (e.MoveNext()) parts.Add((string)e.Current); return parts.ToArray(); }
        public static string ContextHtml(ReviewRecord record, bool after)
        {
            string text = after ? record.AfterContext : record.BeforeContext;
            if (text.Length == 0) return H(after ? "After context unavailable; no verified result is claimed." : "Context not recorded; unavailable for this older/interrupted decision.");
            if (record.BeforeContext.Length == 0 || record.AfterContext.Length == 0 || record.BeforeContext == record.AfterContext) return H(text);
            var a = Elements(record.BeforeContext); var b = Elements(record.AfterContext); int prefix = 0, suffix = 0;
            while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix]) prefix++;
            while (suffix < a.Length - prefix && suffix < b.Length - prefix && a[a.Length - 1 - suffix] == b[b.Length - 1 - suffix]) suffix++;
            var selected = after ? b : a;
            return H(String.Concat(selected.Take(prefix))) + "<mark style='background:" + (after ? "#bcebd0" : "#ffd4cf") + ";color:#17273c'>" + H(String.Concat(selected.Skip(prefix).Take(selected.Length - prefix - suffix))) + "</mark>" + H(String.Concat(selected.Skip(selected.Length - suffix)));
        }
        public static string Render(string project, string reviewer, string session, string started, string ended, string reason, int total, int reviewed, IList<ReviewRecord> records)
        {
            var b = new StringBuilder();
            b.Append("<!doctype html><html lang='en'><head><meta charset='utf-8'><meta http-equiv='X-UA-Compatible' content='IE=edge'><meta name='viewport' content='width=device-width,initial-scale=1'><meta http-equiv='Content-Security-Policy' content=\"default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; img-src 'none'; connect-src 'none'\"><title>Review Queue - ").Append(H(project)).Append("</title><style>");
            b.Append("*{box-sizing:border-box}header,main,article,section,details,summary{display:block}body{margin:0;background:#f2f5fa;color:#17273c;font:15px 'Segoe UI',Arial,sans-serif}header{background:#142d4c;color:white;padding:28px 32px}h1{margin:0 0 8px;font-size:30px}header p{color:#d5e3f4;margin:8px 0}main{max-width:1400px;margin:auto;padding:22px}.stats{display:flex;flex-wrap:wrap;gap:12px;margin-bottom:18px}.stat{background:white;border:1px solid #dbe4ef;border-radius:12px;padding:15px;min-width:140px;margin-right:10px;margin-bottom:10px}.stat strong{display:block;font-size:28px}.tools{background:white;padding:15px;border:1px solid #dbe4ef;border-radius:12px;margin-bottom:20px}input,select,button{font:inherit;padding:9px;border:1px solid #bac9db;border-radius:6px;margin:3px}input{min-width:240px}.entry{background:white;border:1px solid #d7e2ef;border-radius:14px;margin-bottom:22px;overflow:hidden}.meta{padding:18px 22px;background:#fbfcfe;border-bottom:1px solid #dde6ef}.meta h2{margin:0 0 9px;font-size:20px}.badge{display:inline-block;border-radius:20px;padding:4px 10px;background:#e7eef8;margin:0 6px 5px 0}.Applied{background:#dff4e7;color:#135c31}.Rejected{background:#fff0da;color:#7b4a12}.comment{padding:16px 22px;border-bottom:1px solid #e4eaf2;white-space:pre-wrap}.panes{display:table;table-layout:fixed;width:100%}.pane{display:table-cell;width:50%;vertical-align:top;padding:18px 22px}.before{background:#fff7f6}.after{background:#f1fbf6}.pane h3{margin:0 0 10px;font-size:15px}pre{font:17px/1.7 'Nirmala UI','Meera Inimai','Segoe UI',sans-serif;white-space:pre-wrap;overflow-wrap:anywhere;word-wrap:break-word;margin:0}.target{font-size:15px;white-space:pre-wrap;border-top:1px solid #dce5ed;margin-top:16px;padding-top:12px}.evidence{padding:12px 22px;color:#425977;background:#f6f8fc;white-space:pre-wrap}details{padding:15px 22px}summary{cursor:pointer;font-weight:600}small{font-size:13px;color:#526984}.empty{color:#8a4251}.hidden{display:none}@media(max-width:750px){.panes,.pane{display:block;width:100%}main{padding:12px}}@media print{header{background:white;color:black}.tools{display:none}.entry{break-inside:avoid;border:1px solid #aaa}main{padding:0}.hidden{display:block}} </style></head><body><header><h1>Review Queue <span style='font-weight:400'>/ ");
            b.Append(H(project)).Append("</span></h1><p>Session report - ").Append(H(reason)).Append("</p><p>Paratext reviewer: <strong>").Append(H(reviewer)).Append("</strong> | Started (UTC): ").Append(H(started)).Append(" | Report (UTC): ").Append(H(ended)).Append("</p></header><main><div class='stats'>");
            Stat(b, records.Count, "Decisions / recorded attempts"); Stat(b, records.Count(r => r.Outcome == "Applied"), "Applied"); Stat(b, records.Count(r => r.Outcome == "Rejected"), "Rejected"); Stat(b, records.Count(r => r.Outcome == "Completed manually"), "Manually completed"); Stat(b, records.Count(r => r.Outcome == "Pending apply" || r.ResolutionPending), "Need follow-up");
            b.Append("</div><p><strong>Queue at stop:</strong> ").Append(reviewed).Append(" reviewed / ").Append(total).Append(" indexed. This report contains this session's decisions and recovered actions, not every project note. Previously edited text is never reconstructed from today's Scripture.</p><div class='tools'><input id='search' placeholder='Search verse, reviewer, comment or text' oninput='filterRows()'><select id='outcome' onchange='filterRows()'><option value=''>All decisions</option><option>Applied</option><option>Rejected</option><option>Completed manually</option><option>Pending apply</option></select><button onclick='window.print()'>Print / save PDF</button><span id='visible'></span></div>");
            foreach (var r in records)
            {
                b.Append("<article class='entry' data-outcome='").Append(H(r.Outcome)).Append("'><div class='meta'><h2>").Append(H(r.Reference)).Append(" <small>").Append(H(r.Category)).Append(" / ").Append(H(r.Region)).Append("</small></h2><span class='badge ").Append(r.Outcome == "Applied" ? "Applied" : r.Outcome == "Rejected" ? "Rejected" : "").Append("'>").Append(H(r.Outcome)).Append("</span><span class='badge'>Note: ").Append(H(r.Resolution)).Append("</span><div><small>Decision by <strong>").Append(H(r.Reviewer.Length == 0 ? "Not recorded by older version" : r.Reviewer)).Append("</strong> at ").Append(H(r.DecisionUtc)).Append(" UTC. Latest action by ").Append(H(r.Actor)).Append(" at ").Append(H(r.ActionUtc)).Append(" UTC.</small></div></div><div class='comment'><strong>Comment</strong>\n").Append(H(r.Comment.Length == 0 ? "No comment entered." : r.Comment)).Append("</div><div class='panes'><section class='pane before'><h3>BEFORE - full passage / text context</h3><pre>").Append(ContextHtml(r, false)).Append("</pre><div class='target'><strong>Exact target</strong>\n").Append(H(r.Original)).Append("</div></section><section class='pane after'><h3>").Append(r.Outcome == "Pending apply" ? "EXPECTED AFTER - NOT VERIFIED" : r.Outcome == "Rejected" ? "AFTER - existing text retained" : "AFTER - full passage / text context").Append("</h3><pre>").Append(ContextHtml(r, true)).Append("</pre><div class='target'><strong>Chosen / proposed replacement</strong>\n").Append(H(r.Replacement)).Append("</div></section></div><div class='evidence'><strong>Evidence:</strong> ").Append(H(r.Evidence)).Append("\nContext: ").Append(H(r.ContextScope)).Append(r.SessionId != session ? "\nDecision originated in an earlier session; this session updated its resolution/recovery." : "").Append("</div><details><summary>Source note and audit details</summary><pre>").Append(H(r.SourceNote)).Append("</pre><p><small>Audit: ").Append(H(r.AuditId)).Append(" | Source identity: ").Append(H(r.Id)).Append("</small></p></details></article>");
            }
            b.Append("<p><small>Session: ").Append(H(session)).Append(". Local report; no external fonts, scripts or network requests. Rejected text is unchanged by the queue. Manual comparisons show observations at review-load and decision time; the reviewer is not proof of who made a manual edit.</small></p></main><script>function filterRows(){var q=document.getElementById('search').value.toLowerCase(),v=document.getElementById('outcome').value,rows=document.querySelectorAll('.entry'),n=0;for(var i=0;i<rows.length;i++){var r=rows[i],ok=(!v||r.getAttribute('data-outcome')===v)&&(!q||(r.textContent||r.innerText).toLowerCase().indexOf(q)>=0);r.style.display=ok?'':'none';if(ok)n++;}document.getElementById('visible').innerText=n+' shown';}filterRows();</script></body></html>");
            return b.ToString();
        }
        static void Stat(StringBuilder b, int value, string label) { b.Append("<div class='stat'><strong>").Append(value).Append("</strong>").Append(H(label)).Append("</div>"); }
    }
    public sealed class ReviewReportViewer : Form
    {
        public ReviewReportViewer(string path)
        {
            Text = "Review Queue report - " + Path.GetFileName(path); Width = 1100; Height = 750;
            var browser = new WebBrowser { Dock = DockStyle.Fill, AllowWebBrowserDrop = false, IsWebBrowserContextMenuEnabled = false, ScriptErrorsSuppressed = true };
            browser.NewWindow += delegate(object sender, System.ComponentModel.CancelEventArgs e) { e.Cancel = true; };
            browser.Navigating += delegate(object sender, WebBrowserNavigatingEventArgs e) { if (e.Url.AbsoluteUri != "about:blank") e.Cancel = true; };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(5) };
            var open = new Button { Text = "Open in browser", AutoSize = true };
            open.Click += delegate { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); };
            buttons.Controls.Add(open); buttons.Controls.Add(new Label { Text = "Saved HTML report - available offline", AutoSize = true, Padding = new Padding(8) });
            Controls.Add(browser); Controls.Add(buttons); browser.DocumentText = File.ReadAllText(path, Encoding.UTF8);
        }
    }
}
