using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ParatextNoteSuggestions
{
    public enum ContentRegion { Body, Introduction, Heading, Footnote, CrossReference, Structure }
    public enum ProposalRoute { Apply, Review, Structure, NoChange, Legacy }

    public sealed class Proposal
    {
        public string FullNote = "", Pass = "", Topic = "", Category = "", Original = "", Suggested = "", Reason = "", Location = "", Confidence = "", Severity = "", Reference = "";
        public bool ReviewerDecision, AnchorWarning;
        public ContentRegion Region;
        public ProposalRoute Route;
        public string Fingerprint;
        public static readonly string[] Categories = {
            "Sandhi / word-joining", "Source comparison", "Punctuation", "Textual-basis review", "Grammar",
            "Footnote / cross-reference", "USFM marker", "Name consistency", "Possible missed source word",
            "Confirmed typo", "Possible typo", "Plural agreement", "Case/object marker", "Theological consistency",
            "Reviewer decision", "Addition not in source"
        };
    }

    public static class NoteParser
    {
        static readonly Regex Header = new Regex(@"^\s*\[AI-QA\s+([^\]]+)\]\s*(.*?)\s+[\u2014\u2013]\s*([^\r\n]+)");
        static readonly Regex Field = new Regex(@"(?m)^\s*(Original|Suggested|Reason|Reference|Location|Severity)\s*:\s*");
        public static Proposal Parse(string text)
        {
            var p = new Proposal { FullNote = text ?? "" };
            p.Fingerprint = Hash(p.FullNote);
            var h = Header.Match(p.FullNote);
            if (!h.Success) { p.Route = ProposalRoute.Legacy; p.Category = "Unstructured / discussion"; p.ReviewerDecision = true; return p; }
            p.Pass = h.Groups[1].Value.Trim(); p.Topic = h.Groups[2].Value.Trim(); p.Category = h.Groups[3].Value.Trim();
            var fields = new Dictionary<string, string>();
            var matches = Field.Matches(p.FullNote);
            bool duplicate = false;
            for (int i = 0; i < matches.Count; i++)
            {
                string key = matches[i].Groups[1].Value;
                int start = matches[i].Index + matches[i].Length;
                int end = i + 1 < matches.Count ? matches[i + 1].Index : p.FullNote.Length;
                if (fields.ContainsKey(key)) duplicate = true;
                else fields[key] = p.FullNote.Substring(start, end - start).Trim();
            }
            p.Original = Get(fields, "Original"); p.Suggested = Get(fields, "Suggested"); p.Reason = Get(fields, "Reason");
            p.Location = Get(fields, "Location"); p.Reference = Get(fields, "Reference");
            p.Confidence = Metadata(p.FullNote, "Confidence"); p.Severity = Metadata(p.FullNote, "Severity");
            string reviewer = Metadata(p.FullNote, "Reviewer decision needed");
            // Missing or unrecognized reviewer metadata requires review.
            p.ReviewerDecision = !String.Equals(reviewer, "No", StringComparison.OrdinalIgnoreCase);
            string topic = (p.Topic + " " + p.Location).ToLowerInvariant();
            p.AnchorWarning = Regex.IsMatch(p.Location, "not found verbatim|anchored on the start|differs from the current", RegexOptions.IgnoreCase);
            p.Region = topic.Contains("introduction") ? ContentRegion.Introduction :
                Regex.IsMatch(topic, "heading|chapter label") ? ContentRegion.Heading :
                topic.Contains("cross-reference") || topic.Contains("cross reference") ? ContentRegion.CrossReference :
                topic.Contains("footnote") ? ContentRegion.Footnote : ContentRegion.Body;
            bool structural = p.Category == "USFM marker" || p.Original.Contains("\\") || p.Suggested.Contains("\\") ||
                Regex.IsMatch(topic, "verse alignment|verse bridge|verse order|footnote placement|usfm|markup", RegexOptions.IgnoreCase);
            if (structural) { p.Region = ContentRegion.Structure; p.Route = ProposalRoute.Structure; }
            else if (Regex.IsMatch(p.Suggested, @"^\s*\(?\s*(?:no change|no edit|retain\b|keep (?:as|the (?:current|existing))|unchanged\b)", RegexOptions.IgnoreCase)) p.Route = ProposalRoute.NoChange;
            else
            {
                bool uncertainCategory = new[] { "Source comparison", "Textual-basis review", "Theological consistency", "Reviewer decision", "Possible missed source word", "Addition not in source", "Possible typo", "Footnote / cross-reference" }.Contains(p.Category);
                // Latin instructions, alternatives, annotations and unfamiliar categories never become automatic replacement strings.
                bool literal = p.Original.Length > 0 && p.Suggested.Length > 0 && !Regex.IsMatch(p.Suggested, @"[A-Za-z]|\s/\s|[()\[\]{}\u00ab\u00bb]|\u2026|\.\.\.");
                p.Route = literal && !duplicate && !p.ReviewerDecision && !uncertainCategory && !p.AnchorWarning &&
                    Proposal.Categories.Contains(p.Category) ? ProposalRoute.Apply : ProposalRoute.Review;
            }
            if (duplicate || p.Original.Length == 0 || p.Suggested.Length == 0) p.Route = ProposalRoute.Review;
            return p;
        }
        static string Get(Dictionary<string, string> fields, string key) { string value; return fields.TryGetValue(key, out value) ? value : ""; }
        static string Metadata(string text, string key)
        {
            var m = Regex.Match(text, Regex.Escape(key) + @":\s*([^\r\n\u00b7]+)");
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }
        public static string Hash(string text)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""))).Replace("-", "");
        }
        public static string Preview(string text, int maxElements)
        {
            string s = Regex.Replace(text ?? "", @"\s+", " ");
            var indexes = System.Globalization.StringInfo.ParseCombiningCharacters(s);
            return indexes.Length <= maxElements ? s : s.Substring(0, indexes[maxElements]) + "…";
        }
    }

    public sealed class TextPart
    {
        public int TokenIndex, Chapter, Verse;
        public string Text = "", BeforeAnchor = "", AfterAnchor = "";
        public ContentRegion Region;
    }
    public sealed class ExactMatch
    {
        public int TokenIndex, Start, Length;
        public string Original, Replacement;
    }
    public static class ExactEditor
    {
        public static ExactMatch Locate(IEnumerable<TextPart> parts, ContentRegion region, int chapter, int firstVerse, int lastVerse, string original, string replacement, string before, string after)
        {
            if (String.IsNullOrEmpty(original)) throw new InvalidOperationException("No exact Original text is available. Review the source note.");
            if (replacement == null) throw new InvalidOperationException("A replacement must be explicitly supplied.");
            if (original == replacement) throw new InvalidOperationException("The original and replacement are identical; nothing will be changed.");
            if (replacement.Contains("\\") || replacement.IndexOf('\0') >= 0 || Regex.IsMatch(replacement, @"[\r\n]"))
                throw new InvalidOperationException("This text action cannot insert USFM markers or paragraph breaks. Use the structural review workflow.");
            var found = new List<ExactMatch>();
            var contexts = new Dictionary<ExactMatch, bool>();
            foreach (var part in parts)
            {
                if (part.Region != region) continue;
                if (region != ContentRegion.Introduction && (part.Chapter != chapter || part.Verse < firstVerse || part.Verse > lastVerse)) continue;
                for (int i = 0; i <= part.Text.Length - original.Length; i++)
                {
                    if (String.CompareOrdinal(part.Text, i, original, 0, original.Length) != 0) continue;
                    if (!Boundary(part.Text, i) || !Boundary(part.Text, i + original.Length)) continue;
                    var m = new ExactMatch { TokenIndex = part.TokenIndex, Start = i, Length = original.Length, Original = original, Replacement = replacement };
                    found.Add(m);
                    // Full available context is required for disambiguation. Never guess an offset convention.
                    bool hasContext = !String.IsNullOrEmpty(before) || !String.IsNullOrEmpty(after);
                    bool b = String.IsNullOrEmpty(before) || part.Text.Substring(0, i).EndsWith(before, StringComparison.Ordinal);
                    bool a = String.IsNullOrEmpty(after) || part.Text.Substring(i + original.Length).StartsWith(after, StringComparison.Ordinal);
                    contexts[m] = hasContext && b && a;
                }
            }
            if (found.Count == 1) return found[0];
            if (found.Count > 1)
            {
                var anchored = found.Where(m => contexts[m]).ToList();
                if (anchored.Count == 1) return anchored[0];
                throw new InvalidOperationException("The Original occurs more than once in this region. The note context does not identify exactly one occurrence; no text was changed.");
            }
            throw new InvalidOperationException("The exact Original was not found in the specified region. It may be stale, span formatting markers, or already be corrected. No text was changed.");
        }
        static bool Boundary(string text, int index)
        {
            if (index == 0 || index == text.Length) return true;
            return Array.BinarySearch(System.Globalization.StringInfo.ParseCombiningCharacters(text), index) >= 0;
        }
        public static string Replace(string text, ExactMatch match)
        {
            if (match.Start < 0 || match.Start + match.Length > text.Length || text.Substring(match.Start, match.Length) != match.Original)
                throw new InvalidOperationException("The matched text changed before replacement.");
            return text.Substring(0, match.Start) + match.Replacement + text.Substring(match.Start + match.Length);
        }
    }
}
