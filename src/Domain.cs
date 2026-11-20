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
        public bool ReviewerDecision, AnchorWarning, ReviewedBulkLiteral;
        // Exact wordings the note itself gives for a reviewed single apply (see Wording.Choices), and how they were read.
        public List<string> Paste = new List<string>();
        public string PasteSource = "";
        // A marker-bearing Original and Suggested that pass UsfmFragment's checks.
        public bool Fragment;
        // Other open notes' different suggestions for the same Original in the same passage.
        public string Conflict = "";
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
            p.Region = TargetRegion(topic, p.Reason);
            bool markers = p.Original.Contains("\\") || p.Suggested.Contains("\\");
            bool flagged = p.Category == "USFM marker" || Regex.IsMatch(topic, "verse alignment|verse bridge|verse order|footnote placement|usfm|markup", RegexOptions.IgnoreCase);
            string pasteSource = "";
            var paste = markers || duplicate || p.Original.Length == 0 ? new List<string>() : Wording.Choices(p.Original, p.Suggested, out pasteSource);
            // A flagged note that gives exact Tamil without markers is an ordinary text correction in its region.
            bool structural = markers || (flagged && paste.Count == 0);
            if (structural)
            {
                p.Region = ContentRegion.Structure; p.Route = ProposalRoute.Structure;
                p.Fragment = markers && !duplicate && UsfmFragment.Problem(p.Original, p.Suggested) == null;
            }
            else if (Regex.IsMatch(p.Suggested, @"^\s*\(?\s*(?:no change|no edit|retain\b|keep (?:as|the (?:current|existing))|unchanged\b)", RegexOptions.IgnoreCase)) p.Route = ProposalRoute.NoChange;
            else
            {
                bool uncertainCategory = new[] { "Source comparison", "Textual-basis review", "Theological consistency", "Reviewer decision", "Possible missed source word", "Addition not in source", "Possible typo", "Footnote / cross-reference" }.Contains(p.Category);
                // Latin instructions, alternatives, annotations and unfamiliar categories never become automatic replacement strings.
                bool literal = p.Original.Length > 0 && p.Suggested.Length > 0 && !Regex.IsMatch(p.Suggested, @"[A-Za-z]|\s/\s|[()\[\]{}\u00ab\u00bb]|\u2026|\.\.\.");
                // Eligibility for an explicitly reviewed batch is independent of automatic routing.
                p.ReviewedBulkLiteral = literal && !duplicate && !p.AnchorWarning && !flagged;
                p.Route = literal && !duplicate && !p.ReviewerDecision && !uncertainCategory && !p.AnchorWarning && !flagged &&
                    Proposal.Categories.Contains(p.Category) ? ProposalRoute.Apply : ProposalRoute.Review;
                p.Paste = paste; p.PasteSource = pasteSource;
            }
            if (duplicate || p.Original.Length == 0 || p.Suggested.Length == 0) p.Route = ProposalRoute.Review;
            return p;
        }
        static string Get(Dictionary<string, string> fields, string key) { string value; return fields.TryGetValue(key, out value) ? value : ""; }
        static ContentRegion TargetRegion(string topic, string reason)
        {
            // QA locations may name the USFM marker instead of an English region.
            topic = Regex.Replace(topic, @"\\(?:s[1-4]?|ms[1-3]?|sr|mr|r|d|sp|cl|cd|qa)\b", " heading ");
            topic = Regex.Replace(topic, @"\\i(?:s\d*|p\w*|m\w*|q\d*|li\d*|o\w*|mt\d*|ex|e)\b", " introduction ");
            topic = Regex.Replace(topic, @"\\(?:f|fe|ef|ft|fq|fqa|fk|fr|fl|fw|fp|fv|fdc)\b", " footnote ");
            topic = Regex.Replace(topic, @"\\(?:x|ex|xt|xo|xk|xq|xot|xnt|xdc)\b", " cross-reference ");
            // Topic/Location are authoritative. A location at the start of Reason is
            // also used by existing QA notes (e.g. "In the footnote, ..."). Do not
            // infer a target from incidental comparisons later in the explanation.
            string location = topic;
            if (!Regex.IsMatch(location, @"\b(?:introduction|heading|chapter label|cross[- ]reference|footnote|body text|scripture body|verse text)\b", RegexOptions.IgnoreCase)) {
                const string regions = @"introduction|heading|chapter label|cross[- ]reference|footnote";
                var hint = Regex.Match(reason ?? "", @"^\s*(?:in\s+(?:the\s+)?(?:book\s+)?(" + regions + @")\b|(?:book\s+)?(" + regions + @")\s*[:,])", RegexOptions.IgnoreCase);
                location = hint.Success ? (hint.Groups[1].Value + hint.Groups[2].Value).ToLowerInvariant() : "";
            }
            return location.Contains("introduction") ? ContentRegion.Introduction :
                Regex.IsMatch(location, "heading|chapter label") ? ContentRegion.Heading :
                Regex.IsMatch(location, "cross[- ]reference") ? ContentRegion.CrossReference :
                location.Contains("footnote") ? ContentRegion.Footnote : ContentRegion.Body;
        }
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

    // Reads the exact Tamil a note's Suggested line gives, without composing any wording.
    public static class Wording
    {
        const string Tamil = @"\u0B80-\u0BFF";
        static string Brackets(string text) { return Regex.Replace(text ?? "", @"[^()\[\]]", ""); }
        // Text that may replace Scripture as it stands: no Latin, markup, alternatives or omission marks.
        // Brackets are accepted only when the Original has the same ones in the same order.
        public static bool Plain(string text, string original)
        {
            if (String.IsNullOrWhiteSpace(text) || text != text.Trim() || text == original) return false;
            if (Regex.IsMatch(text, @"[A-Za-z\\\r\n\0{}|\u00ab\u00bb\uE000]|\s/\s|\u2026|\.\.\.")) return false;
            // Bare punctuation or digits may replace only an Original that is itself without Tamil letters.
            if (!Regex.IsMatch(text, "[" + Tamil + "]") && Regex.IsMatch(original ?? "", "[" + Tamil + "]")) return false;
            return Brackets(text).Length == 0 || Brackets(text) == Brackets(original);
        }
        static bool All(IEnumerable<string> texts, string original, List<string> result)
        {
            var list = texts.Select(t => t.Trim()).ToList();
            if (list.Count == 0 || !list.All(t => Plain(t, original))) return false;
            foreach (string t in list) if (!result.Contains(t)) result.Add(t);
            return true;
        }
        // One entry when the note gives one exact wording, several when it offers alternatives, none when it
        // gives an instruction, a question or partial text. source says how the wording was read ("" = as written).
        public static List<string> Choices(string original, string suggested, out string source)
        {
            source = ""; var result = new List<string>(); string s = (suggested ?? "").Trim();
            if (s.Length == 0 || Regex.IsMatch(s, @"\u2026|\.\.\.")) return result;
            // " / " can also mark a line break: pieces that together make up the Original are not alternatives.
            Func<string, string> squeeze = t => Regex.Replace(t ?? "", @"\s+", "");
            if (Regex.IsMatch(s, @"\s/\s") && squeeze(Regex.Replace(s, @"\s/\s", " ")) == squeeze(original)) return result;
            if (All(Regex.Split(s, @"\s/\s"), original, result)) { if (result.Count > 1) source = "alternatives offered by the note"; return result; }
            result.Clear();
            // "Tamil (for reviewer)", "Tamil (or Tamil)": drop English-only remarks, keep a Tamil alternative.
            var alternatives = new List<string>();
            string rest = Regex.Replace(s, @"\s*\(\s*or:?\s+([^()A-Za-z]*[" + Tamil + @"][^()A-Za-z]*)\)", delegate(Match m) { alternatives.Add(m.Groups[1].Value); return ""; });
            const string remark = @"\((?=[^()]*[A-Za-z])[^()" + Tamil + @"]*\)";
            bool removed = Regex.IsMatch(rest, remark) || alternatives.Count > 0;
            // A remark about a heading, a move or verse alignment means the Tamil is not a plain replacement of the Original.
            // "add a footnote" is allowed: it names the other option, not what to do with this Tamil.
            const string elsewhere = @"\b(?:add(?!\s+a\s+footnote\b)\w*|heading\w*|mov\w+|section\w*|align\w*|plac\w+|insert\w*|and remove|cop(?:y|ies)|manuscripts?|superscription|footnote text|first|only|omit\w*|similar|missing|suppl\w+|before|after)\b|e\.g\.";
            if (Regex.IsMatch(s, elsewhere, RegexOptions.IgnoreCase)) return result;
            // A remark between two pieces of Tamil: joining them would compose wording the note never gave.
            foreach (Match m in Regex.Matches(rest, remark))
                if (Regex.IsMatch(rest.Substring(0, m.Index), "[" + Tamil + "]") && Regex.IsMatch(rest.Substring(m.Index + m.Length), "[" + Tamil + "]")) return result;
            rest = Regex.Replace(rest, @"\s*" + remark + @"(?=\s*(?:[.,;:!?]|$))", "");
            rest = Regex.Replace(rest, @"\s*" + remark + @"\s*", " ").Trim();
            if (removed && All(Regex.Split(rest, @"\s/\s").Concat(alternatives), original, result))
            { source = result.Count > 1 ? "alternatives offered by the note, English remark removed" : "the note's Tamil with its English remark removed"; return result; }
            result.Clear();
            // One Tamil phrase inside an English instruction, only after wording that presents it as the replacement.
            var runs = Regex.Matches(s, "[" + Tamil + "][" + Tamil + @"0-9 .,;:!?\u2018\u2019\u201c\u201d\u2013\u2014-]*");
            if (runs.Count != 1) return result;
            string run = runs[0].Value.TrimEnd(); int start = runs[0].Index, end = start + run.Length;
            const string open = "'\"\u2018\u201c", close = "'\"\u2019\u201d";
            bool quoted = start > 0 && end < s.Length && open.IndexOf(s[start - 1]) >= 0 && close.IndexOf(s[end]) == open.IndexOf(s[start - 1]);
            if (!quoted)
            {
                run = run.TrimEnd('\u2018', '\u2019', '\u201c', '\u201d');
                // Without quotes, closing punctuation may belong to the English sentence: accept it only when the Original ends the same way.
                if (run.Length == 0 || ",;:-\u2013\u2014".IndexOf(run[run.Length - 1]) >= 0) return result;
                if (".!?".IndexOf(run[run.Length - 1]) >= 0 && (original.Length == 0 || original[original.Length - 1] != run[run.Length - 1])) return result;
            }
            // "e.g." and similar leads are not accepted: there the Tamil is often a word to add or footnote text.
            string lead = Regex.Replace(s.Substring(0, start), @"[\s:'""\u2018\u201c(]+$", "");
            if (!Regex.IsMatch(lead, @"(?:follow(?:ing)? the (?:ESV|reference(?: text)?)|\bor read)$", RegexOptions.IgnoreCase) &&
                !(quoted && Regex.IsMatch(lead, @"\breviewer(?: to decide)?$", RegexOptions.IgnoreCase))) return result;
            string tail = Regex.Replace(s.Substring(end), @"^['""\u2019\u201d).\s]+", "");
            if (!Regex.IsMatch(tail, @"^(?:or retain|otherwise keep\b[^()]*)?[).'""\u2019\u201d\s]*$", RegexOptions.IgnoreCase)) return result;
            if (All(new[] { run }, original, result)) source = "Tamil quoted in the note's instruction";
            return result;
        }
    }
    // A marker-bearing correction (footnote, cross-reference, heading or line marker) as exact USFM text.
    public static class UsfmFragment
    {
        static readonly Regex Marker = new Regex(@"\\([a-z]+[0-9]*)\*?");
        // Note, heading, poetry, paragraph and character markers. Chapter, verse, book and peripheral markers are never changed here.
        static readonly Regex Allowed = new Regex(@"^(?:f|fe|fr|ft|fq|fqa|fk|fl|fw|fp|fv|x|xo|xt|xk|xq|xta|s[1-4]?|ms[1-3]?|mr|sr|r|d|sp|cl|qa|q[1-4]?|qr|qc|qm[1-3]?|qs|p|m|pi[1-3]?|mi|nb|pc|li[1-4]?|b|bd|it|bdit|em|nd|wj|tl|sc|add|qt|k|ord|pn)$");
        // Null when the pair can be handled as an exact USFM replacement; otherwise the reason it cannot.
        public static string Problem(string original, string replacement)
        {
            if (String.IsNullOrEmpty(original) || String.IsNullOrEmpty(replacement)) return "The note does not give both an exact Original and an exact Suggested text.";
            if (original == replacement) return "The original and replacement are identical; nothing will be changed.";
            if (!original.Contains("\\") && !replacement.Contains("\\")) return "This is not a marker change.";
            foreach (string text in new[] { original, replacement })
            {
                if (Regex.IsMatch(text, @"[\r\n\0\uE000]")) return "A marker change cannot contain a line break.";
                var found = Marker.Matches(text);
                if (found.Count != text.Count(c => c == '\\')) return "The note contains a backslash that is not a plain USFM marker.";
                foreach (Match m in found)
                    if (!Allowed.IsMatch(m.Groups[1].Value)) return "\\" + m.Groups[1].Value + " cannot be changed here: chapter, verse and book markers, and unfamiliar markers, are changed only in Paratext.";
                string words = Marker.Replace(text, " ");
                if (Regex.IsMatch(words, @"[A-Za-z{}|\u00ab\u00bb]|\s/\s|\u2026|\.\.\.")) return "The note gives an instruction, alternatives or partial text, not the exact USFM to write.";
            }
            if (StyleBalance(original) != StyleBalance(replacement)) return "The Suggested text opens or closes a character style that the Original does not. Make this change in Paratext.";
            if (!MarkersDiffer(original, replacement)) return null;
            // With different markers, a Suggested line is often only the changed part. Writing it as it stands would
            // delete the rest, so nothing of the Original may be lost: only whole notes removed, or markers and words added.
            string withoutNotes = Regex.Replace(original, @"\\(f|fe|x)\s.*?\\\1\*", "");
            if (!replacement.Contains("\\") && !withoutNotes.Contains("\\") && Collapse(withoutNotes) == Collapse(replacement)) return null;
            if (Paragraph.Matches(replacement).Count < Paragraph.Matches(original).Count) return "The Suggested text drops a heading, line or paragraph marker that the Original has. Make this change in Paratext.";
            var kept = Words(replacement); int next = 0;
            foreach (string word in Words(original)) { next = Array.IndexOf(kept, word, next) + 1; if (next == 0) return "The Suggested text changes markers and also leaves out or rewrites words of the Original, so it may be only part of the intended text. Make this change in Paratext."; }
            return null;
        }
        static readonly Regex Paragraph = new Regex(@"\\(?:s[1-4]?|ms[1-3]?|mr|sr|r|d|sp|cl|qa|q[1-4]?|qr|qc|qm[1-3]?|p|m|pi[1-3]?|mi|nb|pc|li[1-4]?|b)(?![a-z0-9*])");
        static string Collapse(string text) { return Regex.Replace(text, @"\s+", " ").Trim(); }
        static bool WordCharacter(string text, int index)
        {
            var kind = Char.GetUnicodeCategory(text, index);
            return Char.IsLetterOrDigit(text, index) || kind == System.Globalization.UnicodeCategory.NonSpacingMark || kind == System.Globalization.UnicodeCategory.SpacingCombiningMark;
        }
        // Opening minus closing markers of each character style.
        static string StyleBalance(string text)
        {
            return String.Join(",", new[] { "bd", "it", "bdit", "em", "nd", "wj", "tl", "sc", "add", "qt", "k", "ord", "pn", "qs" }.Select(name =>
                Regex.Matches(text, @"\\" + name + @"(?![a-z0-9*])").Count - Regex.Matches(text, @"\\" + name + @"\*").Count));
        }
        static string[] Words(string text) { return Marker.Replace(text, " ").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries); }
        // True when the two sides do not have the same markers in the same order.
        public static bool MarkersDiffer(string original, string replacement)
        { return !Marker.Matches(original).Cast<Match>().Select(m => m.Value).SequenceEqual(Marker.Matches(replacement).Cast<Match>().Select(m => m.Value)); }
        // Start offsets of original in usfm that begin and end on whole text elements and whole markers.
        public static List<int> Occurrences(string usfm, string original)
        {
            var result = new List<int>();
            if (String.IsNullOrEmpty(original)) return result;
            int[] elements = null;
            bool endsInMarker = Regex.IsMatch(original, @"\\[a-z]+[0-9]*$"), startsInName = Regex.IsMatch(original, @"^[a-z0-9*]");
            for (int i = usfm.IndexOf(original, StringComparison.Ordinal); i >= 0; i = usfm.IndexOf(original, i + 1, StringComparison.Ordinal))
            {
                int end = i + original.Length;
                if (endsInMarker && end < usfm.Length && Regex.IsMatch(usfm[end].ToString(), "[a-z0-9*]")) continue;
                if (startsInName && Regex.IsMatch(usfm.Substring(0, i), @"\\[a-z0-9]*$")) continue;
                // Never inside a word: the change may not split one, or match the start of a longer one.
                if ((i > 0 && WordCharacter(original, 0) && WordCharacter(usfm, i - 1)) || (end < usfm.Length && WordCharacter(original, original.Length - 1) && WordCharacter(usfm, end))) continue;
                if (elements == null) elements = System.Globalization.StringInfo.ParseCombiningCharacters(usfm);
                if (Array.BinarySearch(elements, i) < 0 || (end < usfm.Length && Array.BinarySearch(elements, end) < 0)) continue;
                result.Add(i);
            }
            return result;
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
            var match = Find(parts, region, chapter, firstVerse, lastVerse, original, before, after);
            match.Replacement = replacement;
            return match;
        }
        // The one occurrence of Original in the region and verses, shared by text edits and footnote placement.
        public static ExactMatch Find(IEnumerable<TextPart> parts, ContentRegion region, int chapter, int firstVerse, int lastVerse, string original, string before, string after)
        {
            if (String.IsNullOrEmpty(original)) throw new InvalidOperationException("No exact Original text is available. Review the source note.");
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
                    var m = new ExactMatch { TokenIndex = part.TokenIndex, Start = i, Length = original.Length, Original = original };
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
    // Before/after comparison in whole text elements, so a Tamil syllable is never split by a highlight.
    public static class TextDiff
    {
        public static string[] Elements(string text)
        { var parts = new List<string>(); var e = System.Globalization.StringInfo.GetTextElementEnumerator(text ?? ""); while (e.MoveNext()) parts.Add((string)e.Current); return parts.ToArray(); }
        // Counts the text elements both strings share at the start and at the end; the rest is the change.
        public static void Split(string[] a, string[] b, out int prefix, out int suffix)
        {
            prefix = 0; suffix = 0;
            while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix]) prefix++;
            while (suffix < a.Length - prefix && suffix < b.Length - prefix && a[a.Length - 1 - suffix] == b[b.Length - 1 - suffix]) suffix++;
        }
        // Character offset and length of the changed part of one side.
        public static void Changed(string before, string after, bool afterSide, out int start, out int length)
        {
            var a = Elements(before); var b = Elements(after); int prefix, suffix;
            Split(a, b, out prefix, out suffix);
            var side = afterSide ? b : a;
            start = String.Concat(side.Take(prefix)).Length;
            length = String.Concat(side.Skip(prefix).Take(side.Length - prefix - suffix)).Length;
        }
    }
}
