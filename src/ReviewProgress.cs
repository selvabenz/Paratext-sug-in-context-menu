using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ParatextNoteSuggestions
{
    public sealed class ReviewRecord
    {
        public string Id = "", Outcome = "", Reference = "", Category = "", Original = "", Replacement = "", Comment = "", Region = "", Updated = "";
        public bool Completed { get { return Outcome == "Applied" || Outcome == "Rejected" || Outcome == "Completed manually"; } }
        public XElement ToXml()
        {
            return new XElement("review", new XAttribute("id", Id), new XAttribute("outcome", Outcome),
                new XElement("reference", Reference), new XElement("category", Category), new XElement("original", Original),
                new XElement("replacement", Replacement), new XElement("comment", Comment), new XElement("region", Region), new XElement("updated", Updated));
        }
        public static ReviewRecord FromXml(XElement e)
        {
            return new ReviewRecord { Id = (string)e.Attribute("id"), Outcome = (string)e.Attribute("outcome"), Reference = (string)e.Element("reference") ?? "",
                Category = (string)e.Element("category") ?? "", Original = (string)e.Element("original") ?? "", Replacement = (string)e.Element("replacement") ?? "",
                Comment = (string)e.Element("comment") ?? "", Region = (string)e.Element("region") ?? "", Updated = (string)e.Element("updated") ?? "" };
        }
    }
    // Local, per-user progress: never a project notes/USFM side channel. Each replacement is atomic.
    public sealed class ReviewProgress : IDisposable
    {
        readonly string directory;
        readonly FileStream lease;
        readonly Dictionary<string, ReviewRecord> cache = new Dictionary<string, ReviewRecord>();
        public string DirectoryPath { get { return directory; } }
        public ReviewProgress(string root, string projectId)
        {
            directory = Path.Combine(root, NoteParser.Hash(projectId)); Directory.CreateDirectory(directory);
            try { lease = new FileStream(Path.Combine(directory, "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { throw new InvalidOperationException("A review queue for this project is already open. Use that window before opening another."); }
        }
        static void AtomicWrite(string path, XElement value)
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
            record.Updated = DateTime.UtcNow.ToString("O"); AtomicWrite(RecordPath(record.Id), record.ToXml()); cache[record.Id] = record;
        }
        public void SaveCursor(ReviewRecord draft)
        { AtomicWrite(Path.Combine(directory, "cursor.xml"), draft.ToXml()); }
        public ReviewRecord Cursor()
        { string path = Path.Combine(directory, "cursor.xml"); return File.Exists(path) ? ReviewRecord.FromXml(XElement.Load(path)) : null; }
        public void Export(string path)
        {
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                writer.WriteLine("Reference,Category,Decision,Original,Replacement,Comment,UTC,Id");
                foreach (string file in Directory.EnumerateFiles(directory, "*.review.xml"))
                {
                    var r = ReviewRecord.FromXml(XElement.Load(file));
                    writer.WriteLine(String.Join(",", Array.ConvertAll(new[] { r.Reference, r.Category, r.Outcome, r.Original, r.Replacement, r.Comment, r.Updated, r.Id }, Csv)));
                }
            }
        }
        static string Csv(string s) { s = s ?? ""; if (s.Length > 0 && "=+-@".IndexOf(s[0]) >= 0) s = "'" + s; return "\"" + s.Replace("\"", "\"\"") + "\""; }
        public void Dispose() { lease.Dispose(); }
    }
}
