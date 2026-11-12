using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Paratext.PluginInterfaces;
using ParatextNoteSuggestions;

namespace NoteSuggestionsTests
{
    public static class TargetingTests
    {
        static void Check(bool value) { if (!value) throw new Exception("Targeting assertion failed"); }
        static void Refuse(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Expected refusal"); }
        static Fixture Target(ContentRegion region)
        {
            var f = new Fixture();
            string label = region == ContentRegion.CrossReference ? "cross-reference" : region.ToString().ToLowerInvariant();
            f.NoteText = Fixture.Qa("Possible typo", "பழைய", "புதிய", "Yes").Replace("Synthetic reason", "In the " + label + ", spelling is incorrect.");
            f.Tokens.Clear();
            if (region == ContentRegion.Introduction) {
                f.Tokens.Add(Fixture.Marker("ip", "", 0, 0, MarkerType.Paragraph));
                f.Tokens.Add(Fixture.Text("பழைய", 0, 0, false, false));
                f.Tokens.Add(Fixture.Marker("c", "1", 1, 0, MarkerType.Chapter));
            }
            f.Tokens.Add(Fixture.Marker("v", "3", 1, 3, MarkerType.Verse));
            f.Tokens.Add(Fixture.Text("பழைய", 1, 3, true, false));
            if (region == ContentRegion.Heading) {
                f.Tokens.Add(Fixture.Marker("s1", "", 1, 3, MarkerType.Paragraph));
                f.Tokens.Add(Fixture.Text("பழைய", 1, 3, false, false));
            }
            if (region == ContentRegion.Footnote || region == ContentRegion.CrossReference) {
                string marker = region == ContentRegion.Footnote ? "f" : "x";
                f.Tokens.Add(Fixture.Marker(marker, "+", 1, 3, MarkerType.Note));
                f.Tokens.Add(Fixture.Marker(marker == "f" ? "ft" : "xt", "", 1, 3, MarkerType.Character));
                f.Tokens.Add(Fixture.Text("பழைய", 1, 3, false, true));
                f.Tokens.Add(Fixture.Marker(marker + "*", "", 1, 3, MarkerType.End));
            }
            return f;
        }
        public static void Run(Action<string, Action> test)
        {
            foreach (ContentRegion region in new[] { ContentRegion.Introduction, ContentRegion.Heading, ContentRegion.Footnote, ContentRegion.CrossReference }) {
                ContentRegion captured = region;
                test("reviewed regional bulk preserves identical body text: " + region, delegate {
                    var f = Target(captured); var service = f.Service(); var candidate = service.ReadQueueBook(1).Single();
                    Check(candidate.Proposal.Region == captured);
                    var preview = service.ForQueueItem(candidate).PreviewBulkCorrection(candidate, "Possible typo", true);
                    Check(f.Writes == 0 && preview.After.Contains("புதிய"));
                    service.ForQueueItem(candidate).Apply(candidate, "பழைய", "புதிய", captured, true, false, null,
                        evidence => Check(evidence.Before == preview.Before && evidence.After == preview.After), true);
                    Check(f.Writes == 1);
                    var parts = TokenMapping.Parts(f.Tokens);
                    Check(parts.Single(p => p.Region == ContentRegion.Body).Text == "பழைய");
                    Check(parts.Single(p => p.Region == captured).Text == "புதிய");
                    Check(f.TokenReadChapters.All(ch => ch == (captured == ContentRegion.Introduction ? 0 : 1)));
                    Check(f.ScriptureLockChapters.Single() == (captured == ContentRegion.Introduction ? 0 : 1));
                });
                test("regional bulk revalidates source and region under lock: " + region, delegate {
                    var f = Target(captured); var s = f.Service(); var c = s.ReadQueueBook(1).Single();
                    s.ForQueueItem(c).PreviewBulkCorrection(c, "Possible typo", true);
                    Refuse(() => s.ForQueueItem(c).Apply(c, "பழைய", "புதிய", ContentRegion.Body, true, false, null, null, true));
                    Check(f.Writes == 0);
                    f.OnHistory = () => { f.NoteText += " Changed discussion"; };
                    Refuse(() => s.ForQueueItem(c).Apply(c, "பழைய", "புதிய", captured, true, false, null, null, true));
                    Check(f.Writes == 0);
                });
            }
            test("queue records actual non-body region and resolves after verified edit", delegate {
                var f = Target(ContentRegion.Footnote);
                string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "qt", Guid.NewGuid().ToString("N"));
                using (var q = QueueTests.Queue(f, root)) {
                    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    var items = (System.Collections.Generic.List<NoteCandidate>)typeof(QueueForm).GetField("items", flags).GetValue(q);
                    var previews = items.ToDictionary(c => c.StableId, c => f.Service().ForQueueItem(c).PreviewBulkCorrection(c, c.Proposal.Category, true));
                    typeof(QueueForm).GetMethod("RunCorrectionBatch", flags).Invoke(q, new object[] { items, "Reviewed footnote", previews, 0, null, "selected corrections" });
                    var progress = (ReviewProgress)typeof(QueueForm).GetField("progress", flags).GetValue(q);
                    Check(f.Writes == 1 && f.Resolved && progress.Get(items[0].StableId).Region == "Footnote");
                    q.Close();
                }
            });
            test("reason comparisons do not retarget body and explicit location wins", delegate {
                string text = Fixture.Qa("Grammar", "பழைய", "புதிய", "Yes");
                Check(NoteParser.Parse(text.Replace("Synthetic reason", "Compare the footnote for another spelling.")).Region == ContentRegion.Body);
                Check(NoteParser.Parse(text.Replace("Synthetic reason", "The footnote uses another spelling for comparison.")).Region == ContentRegion.Body);
                Check(NoteParser.Parse(text.Replace("Synthetic reason", "Introduction, audience section: spelling differs.")).Region == ContentRegion.Introduction);
                Check(NoteParser.Parse(text.Replace("Original:", "Location: section heading\r\nOriginal:").Replace("Synthetic reason", "In the footnote, spelling differs.")).Region == ContentRegion.Heading);
                Check(NoteParser.Parse(text.Replace("Original:", "Location: body text\r\nOriginal:").Replace("Synthetic reason", "In the footnote, spelling differs.")).Region == ContentRegion.Body);
            });
            test("reviewed introduction corrections accept Tamil dates with digits", delegate {
                var f = Target(ContentRegion.Introduction);
                f.NoteText = f.NoteText.Replace("Original: பழைய", "Original: கிபி. 55").Replace("Suggested: புதிய", "Suggested: கி.பி. 55");
                f.Tokens[1] = Fixture.Text("கிபி. 55", 0, 0, false, false);
                var s = f.Service(); var c = s.ReadQueueBook(1).Single();
                Check(s.ForQueueItem(c).PreviewBulkCorrection(c, "Possible typo", true).After.Contains("கி.பி. 55"));
                s.ForQueueItem(c).Apply(c, c.Proposal.Original, c.Proposal.Suggested, c.Proposal.Region, true, false, null, null, true);
                Check(f.Writes == 1 && ((IUSFMTextToken)f.Tokens[1]).Text == "கி.பி. 55");
            });
            test("separate paragraphs do not count as split words but inline formatting does", delegate {
                foreach (bool inline in new[] { false, true }) {
                    var f = Target(ContentRegion.Introduction);
                    f.Tokens.Insert(2, Fixture.Marker(inline ? "it" : "ip", "", 0, 0, inline ? MarkerType.Character : MarkerType.Paragraph));
                    f.Tokens.Insert(3, Fixture.Text("சொல்", 0, 0, false, false));
                    var s = f.Service(); var c = s.ReadQueueBook(1).Single();
                    if (inline) Refuse(() => s.ForQueueItem(c).PreviewBulkCorrection(c, "Possible typo", true));
                    else Check(s.ForQueueItem(c).PreviewBulkCorrection(c, "Possible typo", true).After.Contains("புதிய"));
                    Check(f.Writes == 0);
                }
            });
            test("ambiguous introduction targets remain blocked", delegate {
                var f = Target(ContentRegion.Introduction); f.AnchorText = "";
                f.Tokens.Insert(2, Fixture.Marker("ip", "", 0, 0, MarkerType.Paragraph));
                f.Tokens.Insert(3, Fixture.Text("பழைய", 0, 0, false, false));
                var s = f.Service(); var c = s.ReadQueueBook(1).Single();
                Refuse(() => s.ForQueueItem(c).PreviewBulkCorrection(c, "Possible typo", true));
                Check(f.Writes == 0);
            });
            test("embedded notes do not permit a partial body word", delegate {
                var f = Target(ContentRegion.Footnote);
                f.NoteText = Fixture.Qa("Possible typo", "பழைய", "புதிய", "Yes");
                f.Tokens[1] = Fixture.Text("சொல்", 1, 3, true, false);
                f.Tokens[4] = Fixture.Text("குறிப்பு", 1, 3, false, true);
                f.Tokens.Add(Fixture.Text("பழைய", 1, 3, true, false));
                var s = f.Service(); var c = s.ReadQueueBook(1).Single();
                Refuse(() => s.ForQueueItem(c).PreviewBulkCorrection(c, "Possible typo", true));
                Check(f.Writes == 0);
            });
        }
    }
}
