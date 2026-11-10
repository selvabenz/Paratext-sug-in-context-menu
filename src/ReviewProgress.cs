using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ParatextNoteSuggestions
{
    public sealed class ReviewRecord
    {
        public string Id = "", Outcome = "", Reference = "", Category = "", Original = "", Replacement = "", Comment = "", Region = "", Updated = "", Resolution = "", Discussion = "";
        public string SessionId = "", AuditId = "", Reviewer = "", DecisionUtc = "", Actor = "", ActionUtc = "", BeforeContext = "", AfterContext = "", ContextScope = "", Evidence = "", SourceNote = "";
        public string QueueScope = "";
        public bool ResolutionPending { get { return Completed && Resolution == "Pending"; } }
        public bool Completed { get { return Outcome == "Applied" || Outcome == "Rejected" || Outcome == "Completed manually"; } }
        public XElement ToXml()
        {
            return new XElement("review", new XAttribute("id", Id), new XAttribute("outcome", Outcome),
                new XElement("reference", Reference), new XElement("category", Category), new XElement("original", Original),
                new XElement("replacement", Replacement), new XElement("comment", Comment), new XElement("region", Region), new XElement("updated", Updated), new XElement("resolution", Resolution), new XElement("discussion", Discussion),
                new XElement("session", SessionId), new XElement("audit", AuditId), new XElement("reviewer", Reviewer), new XElement("decisionUtc", DecisionUtc), new XElement("actor", Actor), new XElement("actionUtc", ActionUtc),
                new XElement("beforeContext", BeforeContext), new XElement("afterContext", AfterContext), new XElement("contextScope", ContextScope), new XElement("evidence", Evidence), new XElement("sourceNote", SourceNote), new XElement("queueScope", QueueScope));
        }
        public static ReviewRecord FromXml(XElement e)
        {
            return new ReviewRecord { Id = (string)e.Attribute("id"), Outcome = (string)e.Attribute("outcome"), Reference = (string)e.Element("reference") ?? "",
                Category = (string)e.Element("category") ?? "", Original = (string)e.Element("original") ?? "", Replacement = (string)e.Element("replacement") ?? "",
                Comment = (string)e.Element("comment") ?? "", Region = (string)e.Element("region") ?? "", Updated = (string)e.Element("updated") ?? "", Resolution = (string)e.Element("resolution") ?? "", Discussion = (string)e.Element("discussion") ?? "",
                SessionId = (string)e.Element("session") ?? "", AuditId = (string)e.Element("audit") ?? "", Reviewer = (string)e.Element("reviewer") ?? "", DecisionUtc = (string)e.Element("decisionUtc") ?? "", Actor = (string)e.Element("actor") ?? "", ActionUtc = (string)e.Element("actionUtc") ?? "",
                BeforeContext = (string)e.Element("beforeContext") ?? "", AfterContext = (string)e.Element("afterContext") ?? "", ContextScope = (string)e.Element("contextScope") ?? "", Evidence = (string)e.Element("evidence") ?? "", SourceNote = (string)e.Element("sourceNote") ?? "", QueueScope = (string)e.Element("queueScope") ?? "" };
        }
    }
    // Local, per-user progress: never a project notes/USFM side channel. Each replacement is atomic.
    public sealed class ReviewProgress : IDisposable
    {
        readonly string directory;
        readonly FileStream lease;
        readonly Dictionary<string, ReviewRecord> cache = new Dictionary<string, ReviewRecord>();
        Dictionary<string, ReviewRecord> skipped;
        public string DirectoryPath { get { return directory; } }
        public ReviewProgress(string root, string projectId)
        {
            directory = Path.Combine(root, NoteParser.Hash(projectId)); Directory.CreateDirectory(directory);
            try { lease = new FileStream(Path.Combine(directory, "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { throw new InvalidOperationException("A review queue for this project is already open. Use that window before opening another."); }
        }
        internal static void AtomicWrite(string path, XElement value)
        {
            string temp = path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), NewLineHandling = NewLineHandling.Entitize, CloseOutput = false }))
                    value.Save(writer);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        string RecordPath(string id) { return Path.Combine(directory, NoteParser.Hash(id) + ".review.xml"); }
        public ReviewRecord Get(string id)
        {
            ReviewRecord record;
            if (cache.TryGetValue(id, out record)) return record;
            string path = RecordPath(id); if (!File.Exists(path)) return null;
            record = ReviewRecord.FromXml(XElement.Load(path));
            if (record.Id != id) throw new InvalidOperationException("Progress identity mismatch. Review stopped to protect your decisions.");
            cache.Add(id, record); return record;
        }
        public void Save(ReviewRecord record)
        {
            if (String.IsNullOrEmpty(record.Id)) throw new InvalidOperationException("A review identity is required.");
            if (record.AuditId.Length > 0) ReviewSession.ValidateIds(record.SessionId, record.AuditId);
            record.Updated = DateTime.UtcNow.ToString("O"); AtomicWrite(RecordPath(record.Id), record.ToXml()); cache[record.Id] = record;
            if (record.AuditId.Length > 0) ReviewSession.WriteEntry(directory, record.SessionId, record);
        }
        public void SaveCursor(ReviewRecord draft)
        { AtomicWrite(Path.Combine(directory, "cursor.xml"), draft.ToXml()); }
        public void SaveCursor(ReviewRecord draft, QueueScope scope)
        { SaveCursor(draft); AtomicWrite(Path.Combine(directory, "cursor-" + scope.Key + ".xml"), draft.ToXml()); }
        public ReviewRecord Cursor(QueueScope scope)
        { string path = Path.Combine(directory, "cursor-" + scope.Key + ".xml"); return File.Exists(path) ? ReviewRecord.FromXml(XElement.Load(path)) : Cursor(); }
        public QueueScope LoadScope()
        { string path = Path.Combine(directory, "scope.xml"); return QueueScope.FromXml(File.Exists(path) ? XElement.Load(path) : null); }
        public void SaveScope(QueueScope scope)
        { AtomicWrite(Path.Combine(directory, "scope.xml"), scope.ToXml()); }
        public ReviewRecord Cursor()
        { string path = Path.Combine(directory, "cursor.xml"); return File.Exists(path) ? ReviewRecord.FromXml(XElement.Load(path)) : null; }
        public void SaveStartBook(int book)
        { AtomicWrite(Path.Combine(directory, "start-book.xml"), new XElement("book", book)); }
        public int StartBook()
        { string path = Path.Combine(directory, "start-book.xml"); return File.Exists(path) ? (int)XElement.Load(path) : 0; }
        void LoadSkipped()
        {
            if (skipped != null) return;
            skipped = Directory.EnumerateFiles(directory, "*.skipped.xml").Select(f => ReviewRecord.FromXml(XElement.Load(f))).ToDictionary(r => r.Id);
        }
        public void SaveSkipped(NoteCandidate item, string reason)
        {
            var prior = Get(item.StableId);
            // Interrupted writes and saved decisions belong to Recovery, never a fresh manual edit.
            if (prior != null && (prior.Completed || prior.Outcome == "Pending apply")) return;
            LoadSkipped();
            var record = new ReviewRecord { Id = item.StableId, Outcome = "Skipped", Reference = item.DisplayReference,
                Category = item.Proposal.Category, Original = item.Proposal.Original, Replacement = item.Proposal.Suggested,
                Region = item.Proposal.Region.ToString(), SourceNote = item.NoteText, Evidence = reason, Updated = DateTime.UtcNow.ToString("O") };
            AtomicWrite(Path.Combine(directory, NoteParser.Hash(record.Id) + ".skipped.xml"), record.ToXml());
            skipped[record.Id] = record;
        }
        public List<ReviewRecord> Skipped()
        {
            LoadSkipped();
            return skipped.Values.Where(r => r.Outcome == "Skipped" && (Get(r.Id) == null || (!Get(r.Id).Completed && Get(r.Id).Outcome != "Pending apply")))
                .OrderBy(r => r.Reference, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
        }
        public void DismissSkipped(string id)
        {
            LoadSkipped(); ReviewRecord record;
            if (!skipped.TryGetValue(id, out record)) return;
            var dismissed = ReviewRecord.FromXml(record.ToXml()); dismissed.Outcome = "Dismissed";
            AtomicWrite(Path.Combine(directory, NoteParser.Hash(id) + ".skipped.xml"), dismissed.ToXml()); skipped[id] = dismissed;
        }
        public IEnumerable<ReviewRecord> Outstanding()
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*.review.xml"))
            {
                var record = ReviewRecord.FromXml(XElement.Load(file));
                if (record.AuditId.Length > 0)
                {
                    string audit = ReviewSession.EntryPath(directory, record.SessionId, record.AuditId);
                    if (!File.Exists(audit) || File.GetLastWriteTimeUtc(audit) < File.GetLastWriteTimeUtc(file)) ReviewSession.WriteEntry(directory, record.SessionId, record);
                }
                if (record.ResolutionPending || record.Outcome == "Pending apply") yield return record;
            }
        }
        public void Export(string path)
        {
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                writer.WriteLine("Reference,Category,Decision,Original,Replacement,Comment,UTC,Id,Note resolution");
                foreach (string file in Directory.EnumerateFiles(directory, "*.review.xml"))
                {
                    var r = ReviewRecord.FromXml(XElement.Load(file));
                    writer.WriteLine(String.Join(",", Array.ConvertAll(new[] { r.Reference, r.Category, r.Outcome, r.Original, r.Replacement, r.Comment, r.Updated, r.Id, r.Resolution }, Csv)));
                }
            }
        }
        static string Csv(string s) { s = s ?? ""; if (s.Length > 0 && "=+-@".IndexOf(s[0]) >= 0) s = "'" + s; return "\"" + s.Replace("\"", "\"\"") + "\""; }
        public void Dispose() { lease.Dispose(); }
    }
}
