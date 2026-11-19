using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace ParatextNoteSuggestions
{
    // Exact wording prepared outside the plugin for notes whose Suggested is not ready-to-paste Tamil.
    // An entry is only ever shown as a labelled proposal; the reviewer's key press applies it through Apply.
    public sealed class WordingEntry
    {
        public string Id = "", VerseHash = "", Original = "", Replacement = "", Rationale = "", Sheet = "";
        public ContentRegion Region;
        public XElement ToXml()
        {
            return new XElement("wording", new XAttribute("id", Id), new XAttribute("verseHash", VerseHash), new XAttribute("region", Region), new XAttribute("sheet", Sheet),
                new XElement("original", Original), new XElement("replacement", Replacement), new XElement("rationale", Rationale));
        }
        public static WordingEntry FromXml(XElement e)
        {
            ContentRegion region;
            return new WordingEntry { Id = ((string)e.Attribute("id") ?? "").Trim(), VerseHash = ((string)e.Attribute("verseHash") ?? "").Trim(), Sheet = (string)e.Attribute("sheet") ?? "",
                Region = Enum.TryParse((string)e.Attribute("region"), out region) ? region : ContentRegion.Body,
                Original = (string)e.Element("original") ?? "", Replacement = (string)e.Element("replacement") ?? "", Rationale = ((string)e.Element("rationale") ?? "").Trim() };
        }
    }
    public sealed class WordingSheet
    {
        readonly string directory;
        Dictionary<string, WordingEntry> entries;
        Dictionary<string, XElement> exported;
        public WordingSheet(string progressDirectory) { directory = Path.Combine(progressDirectory, "wording"); }
        string EntriesPath { get { return Path.Combine(directory, "entries.xml"); } }
        string ExportedPath { get { return Path.Combine(directory, "exported.xml"); } }
        static XElement LoadSafe(string path)
        {
            using (var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) return XElement.Load(reader);
        }
        void Load()
        {
            if (entries != null) return;
            entries = File.Exists(EntriesPath) ? LoadSafe(EntriesPath).Elements("wording").Select(WordingEntry.FromXml).GroupBy(w => w.Id).ToDictionary(g => g.Key, g => g.Last()) : new Dictionary<string, WordingEntry>();
            exported = File.Exists(ExportedPath) ? LoadSafe(ExportedPath).Elements("proposal").GroupBy(p => (string)p.Attribute("id")).ToDictionary(g => g.Key, g => g.Last()) : new Dictionary<string, XElement>();
        }
        public int Count { get { Load(); return entries.Count; } }
        public WordingEntry Get(string id) { Load(); WordingEntry entry; return entries.TryGetValue(id, out entry) ? entry : null; }
        public XElement Exported(string id) { Load(); XElement e; return exported.TryGetValue(id, out e) ? e : null; }
        public static string VerseHash(string passage) { return NoteParser.Hash(passage ?? ""); }
        // The export carries everything needed to write exact wording offline. StableId is copied, never recomputed.
        // Returns the notes left out because their passage could not be read.
        public List<string> Export(string path, string projectId, string projectName, IEnumerable<NoteCandidate> items, NoteService owner)
        {
            Load(); Directory.CreateDirectory(directory);
            var proposals = new List<XElement>(); var skipped = new List<string>();
            foreach (var c in items)
            {
                var p = c.Proposal; var region = p.Region == ContentRegion.Structure ? ContentRegion.Body : p.Region;
                string passage;
                try { passage = owner.CapturePassage(c, region); }
                catch (InvalidOperationException ex) { skipped.Add(c.DisplayReference + ": " + ex.Message); continue; }
                proposals.Add(new XElement("proposal", new XAttribute("id", c.StableId), new XAttribute("reference", c.DisplayReference), new XAttribute("book", c.Book),
                    new XAttribute("chapter", c.Chapter), new XAttribute("firstVerse", c.FirstVerse), new XAttribute("lastVerse", c.LastVerse), new XAttribute("category", p.Category),
                    new XAttribute("pass", p.Pass), new XAttribute("region", region), new XAttribute("reviewerDecision", p.ReviewerDecision), new XAttribute("verseHash", VerseHash(passage)),
                    new XElement("topic", p.Topic), new XElement("original", p.Original), new XElement("suggested", p.Suggested), new XElement("reason", p.Reason),
                    new XElement("location", p.Location), new XElement("noteReference", p.Reference), new XElement("note", c.NoteText), new XElement("verseUsfm", passage),
                    owner.TargetTexts(c, region).Select(t => new XElement("text", t))));
            }
            ReviewProgress.AtomicWrite(path, new XElement("wordingExport", new XAttribute("version", 1), new XAttribute("projectId", projectId), new XAttribute("project", projectName),
                new XAttribute("created", DateTime.UtcNow.ToString("O")), proposals));
            foreach (var p in proposals) exported[(string)p.Attribute("id")] = new XElement("proposal", p.Attributes());
            ReviewProgress.AtomicWrite(ExportedPath, new XElement("exported", exported.Values));
            return skipped;
        }
        // Reads a sheet without trusting it: every entry is checked again by Import's caller against the live project.
        public static List<WordingEntry> Read(string path, string projectId)
        {
            var root = LoadSafe(path);
            if (root.Name != "wordingSheet") throw new InvalidOperationException("This file is not a wording sheet.");
            if (!String.Equals((string)root.Attribute("projectId"), projectId, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("This wording sheet belongs to another project. Nothing was imported.");
            string sheet = Path.GetFileName(path);
            return root.Elements("wording").Select(e => { var w = WordingEntry.FromXml(e); w.Sheet = sheet; return w; }).ToList();
        }
        // Returns the reason an entry is not usable for its note, or null. Checks that need the live project are done by the caller.
        public static string StaticReason(WordingEntry w)
        {
            if (!Regex.IsMatch(w.Id, "^[0-9A-F]{64}$")) return "malformed proposal id";
            if (w.Original.Length == 0) return "no Original";
            if (w.Replacement.Length == 0) return "empty replacement (deletions are reviewed in the Review tab)";
            if (Regex.IsMatch(w.Original + w.Replacement, @"[\\\r\n\0]")) return "contains a backslash or line break";
            if (!NoteService.TamilLiteral(w.Replacement, false)) return "replacement is not plain Tamil text";
            return null;
        }
        public void Accept(IEnumerable<WordingEntry> accepted)
        {
            Load(); Directory.CreateDirectory(directory);
            foreach (var w in accepted) entries[w.Id] = w;
            ReviewProgress.AtomicWrite(EntriesPath, new XElement("wordingEntries", entries.Values.Select(w => w.ToXml())));
        }
        public void Log(IEnumerable<string> lines)
        {
            Directory.CreateDirectory(directory);
            ReviewProgress.AtomicWrite(Path.Combine(directory, "import-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".xml"), new XElement("import", lines.Select(l => new XElement("line", l))));
        }
    }
}
