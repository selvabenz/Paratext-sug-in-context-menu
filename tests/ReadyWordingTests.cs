using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Paratext.PluginInterfaces;
using ParatextNoteSuggestions;
using F = NoteSuggestionsTests.FastReviewTests;

namespace NoteSuggestionsTests
{
    // The note's own Tamil read out of a longer Suggested line, text found in another place than the note names,
    // and marker-bearing changes written as exact USFM.
    public static class ReadyWordingTests
    {
        const string Base = "\\c 1 \\v 2 முந்தைய வசனம் \\v 3 முன் பழைய\\f + \\fr 1:3 \\ft குறிப்பு\\f* பின் \\v 4 அடுத்த பழைய வசனம்";
        const string Plain = "\\c 1 \\v 3 முன் பழைய பின் \\v 4 அடுத்த பழைய வசனம்";
        static void Refused(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Expected a refusal"); }
        static List<string> Choices(string suggested, string original = "பழைய") { string source; return Wording.Choices(original, suggested, out source); }
        static Fixture Usfm(string usfm, string original, string suggested, string category = "Footnote / cross-reference")
        { var f = new Fixture { RealisticUsfm = true }; f.Tokens = Fixture.Parse(usfm); f.NoteText = Fixture.Qa(category, original, suggested, "Yes"); return f; }
        static void Write(Fixture f) { var s = f.Service(); var n = s.ReadCandidates()[0]; s.ApplyFragment(n, n.Proposal.Original, n.Proposal.Suggested); }
        static string After(string usfm, string original, string suggested)
        { var f = Usfm(usfm, original, suggested); Write(f); F.Check(f.Writes == 1 && f.Released == 1 && f.History == 2 && f.Notifications == 1, "one verified write"); return f.Serialize(f.Tokens); }
        static Fixture WithMatthew(Fixture f)
        { var matthew = new Fixture(); matthew.Ref = Fixture.MakeRef(5, 6, 40); f.AvailableBooks.Add(Fixture.Book(40, "MAT")); f.Notes.Add(matthew.Notes[0]); return f; }
        public static void Run(Action<string, Action> test)
        {
            test("an English remark after the note's Tamil is dropped; the Tamil is used exactly", delegate {
                string source; var one = Wording.Choices("பழைய", "புதிய சொல் (for reviewer)", out source);
                F.Check(one.SequenceEqual(new[] { "புதிய சொல்" }) && source.Contains("English remark removed"));
                F.Check(Choices("(reviewer to decide) புதிய").SequenceEqual(new[] { "புதிய" }));
                F.Check(Choices("புதிய (reviewer to decide).").SequenceEqual(new[] { "புதிய." }), "no space left before closing punctuation");
                F.Check(Choices("புதிய").SequenceEqual(new[] { "புதிய" }) && Choices("பழைய").Count == 0, "unchanged wording is not a choice");
            });
            test("alternatives in the note become separate choices, never one pasted string", delegate {
                F.Check(Choices("புதிய / வேறு").SequenceEqual(new[] { "புதிய", "வேறு" }));
                F.Check(Choices("புதிய (or வேறு)").SequenceEqual(new[] { "புதிய", "வேறு" }) && Choices("புதிய (or: வேறு)").SequenceEqual(new[] { "புதிய", "வேறு" }));
                F.Check(Choices("புதிய / வேறு (reviewer to decide)").SequenceEqual(new[] { "புதிய", "வேறு" }));
                F.Check(Choices("புதிய / consider").Count == 0);
            });
            test("Tamil inside an instruction is taken only when the instruction presents it as the replacement", delegate {
                F.Check(Choices("(reviewer to decide: 'புதிய சொல்' or retain)").SequenceEqual(new[] { "புதிய சொல்" }));
                F.Check(Choices("Keep and add a footnote, or follow the ESV: 'புதிய'").SequenceEqual(new[] { "புதிய" }));
                F.Check(Choices("(reviewer: 'அனுப்பி, அவள் எடுத்தாள்')").SequenceEqual(new[] { "அனுப்பி, அவள் எடுத்தாள்" }));
                foreach (string yes in new[] { "Reviewer to decide: add a footnote or read புதிய", "Keep with a footnote, or follow the reference text (புதிய)", "Reviewer to decide: keep with a footnote, or follow the ESV (புதிய).",
                    "If following the reference text: 'புதிய' otherwise keep and consider a footnote." })
                    F.Check(Choices(yes).SequenceEqual(new[] { "புதிய" }), "replacement: " + yes);
                // Tamil that is a word to add, footnote text, a heading or only part of the wording is never taken.
                foreach (string no in new[] { "Reviewer to decide (e.g. புதிய)", "Add a heading before 2:10 (e.g. புதிய)", "Keep, or add a footnote noting that some copies read புதிய", "Keep with a footnote (e.g. புதிய), or follow the reference",
                    "Reviewer to decide: keep the doubled address or read புதிய once", "புதிய (section heading above this verse)", "புதிய (move from 2:3; reviewer to decide the verse alignment)",
                    "Reviewer to decide (add the line \"as I was\", e.g. புதிய)", "Keep if the traditional wording is intended; ESV: புதிய", "Keep, or add a footnote noting that some copies lack புதிய", "Reviewer to decide (2:3 has புதிய)", "Keep, or add a footnote such as: புதிய",
                    "Reviewer to decide whether a footnote is needed for புதிய", "(consider rendering 'went and')", "Decide whether to keep புதிய (Diana) or use வேறு", "follow the ESV: புதிய, or keep",
                    "follow the ESV: புதிய.", "புதிய … வேறு (reviewer to decide)", "புதிய... (for reviewer)", "n/a - reviewer to decide", "" })
                    F.Check(Choices(no).Count == 0, "not a replacement: " + no);
                F.Check(Choices("follow the ESV: புதிய.", "பழைய.").SequenceEqual(new[] { "புதிய." }), "closing stop kept when the Original has one");
                foreach (string no in new[] { "புதிய (add at the end of the verse)", "புதிய (add as a footnote)", "புதிய (footnote text)", "புதிய (superscription)", "புதிய (or similar)", "புதிய (first word only)", "புதிய (to be inserted)",
                    "புதிய (see the headings)", "Add the missing word, following the ESV: புதிய", "some manuscripts omit this or read புதிய", "புதிய (in v.3) வேறு (in v.4)", "புதிய (after this word)", "—", "2:3", "?" })
                    F.Check(Choices(no).Count == 0, "not a replacement: " + no);
                F.Check(Choices("புதிய (or add a footnote)").SequenceEqual(new[] { "புதிய" }) && Choices(";", ",").SequenceEqual(new[] { ";" }), "the footnote option and punctuation-for-punctuation stay available");
                F.Check(Choices("அ ஆ / இ ஈ", "அ ஆ இ ஈ").Count == 0, "a slash that marks a line break in the Original's own words is not a list of alternatives");
            });
            test("brackets are accepted only when the Original has the same ones", delegate {
                F.Check(Choices("ராஜா. (சேலா)", "ராஜா (சேலா) .").SequenceEqual(new[] { "ராஜா. (சேலா)" }));
                F.Check(Choices("ராஜா (சேலா)", "ராஜா").Count == 0 && Choices("ராஜா [சேலா]", "ராஜா (சேலா)").Count == 0 && Choices("{புதிய}").Count == 0);
            });
            test("parser: derived wording never widens bulk eligibility, and a flagged note with exact Tamil is a text correction", delegate {
                var remark = NoteParser.Parse(Fixture.Qa("Grammar", "பழைய", "புதிய (for reviewer)", "No"));
                F.Check(remark.Paste.Count == 1 && !remark.ReviewedBulkLiteral && remark.Route == ProposalRoute.Review, "remark note is single-apply only");
                var warned = NoteParser.Parse(Fixture.Qa("Grammar", "பழைய", "புதிய", "No").Replace("\r\nOriginal:", "\r\nLocation: whole verse — flagged text not found verbatim in the current text\r\nOriginal:"));
                F.Check(warned.AnchorWarning && warned.Paste.Count == 1 && !warned.ReviewedBulkLiteral && warned.Route == ProposalRoute.Review);
                var flagged = NoteParser.Parse(Fixture.Qa("USFM marker", "பழைய தலைப்பு", "புதிய தலைப்பு", "No"));
                F.Check(flagged.Route == ProposalRoute.Review && flagged.Region != ContentRegion.Structure && flagged.Paste.Count == 1 && !flagged.ReviewedBulkLiteral);
                var instruction = NoteParser.Parse(Fixture.Qa("USFM marker", "பழைய", "Exchange verses 3 and 4", "Yes"));
                F.Check(instruction.Route == ProposalRoute.Structure && !instruction.Fragment && instruction.Paste.Count == 0);
                var none = NoteParser.Parse(Fixture.Qa("Textual-basis review", "பழைய", "(no change proposed)", "Yes"));
                F.Check(none.Route == ProposalRoute.NoChange && none.Paste.Count == 0);
                var fragment = NoteParser.Parse(Fixture.Qa("Footnote / cross-reference", "\\ft குறிப்பு\\f*", "\\ft புதிய குறிப்பு\\f*", "Yes"));
                F.Check(fragment.Route == ProposalRoute.Structure && fragment.Fragment && fragment.Paste.Count == 0);
            });
            test("marker change validation refuses numbering, book markers, instructions and stray backslashes", delegate {
                F.Check(UsfmFragment.Problem("\\ft குறிப்பு\\f*", "\\ft புதிய\\f*") == null && UsfmFragment.Problem("\\q வரி", "\\q2 வரி") == null && UsfmFragment.Problem("பழைய\\f + \\fr 1:3 \\ft க\\f* பின்", "பழைய பின்") == null);
                foreach (var bad in new[] { new[] { "\\v 3 முன்", "\\v 4 முன்" }, new[] { "\\c 1", "\\c 2" }, new[] { "\\id GEN", "\\id EXO" }, new[] { "\\ft குறிப்பு\\f*", "\\ft see the note\\f*" },
                    new[] { "\\ft குறிப்பு\\f*", "\\ft புதிய / வேறு\\f*" }, new[] { "\\ft குறிப்பு\\f*", "\\ft புதிய…\\f*" }, new[] { "\\ft குறிப்பு\\f*", "\\ft புதிய\\ \\f*" },
                    new[] { "\\ft குறிப்பு\\f*", "\\ft புதிய\r\n\\f*" }, new[] { "\\ft குறிப்பு\\f*", "\\ft குறிப்பு\\f*" }, new[] { "\\ft குறிப்பு\\f*", "" }, new[] { "\\w சொல்\\w*", "\\w சொல்|lemma\\w*" },
                    new[] { "\\zcustom உரை", "\\q உரை" }, new[] { "பழைய", "புதிய" } })
                    F.Check(UsfmFragment.Problem(bad[0], bad[1]) != null, bad[0] + " -> " + bad[1]);
                // Suggested lines that give only the changed part: writing them as they stand would delete verse words, markers or a heading.
                foreach (var partial in new[] { new[] { "செய்தார்.\\x + \\xo 7:4 \\xt ஆதி 11:31; ஆதி 12:4-5\\x*", "ஆதி 11:31; 12:4-5" }, new[] { "கழித்தேன்.\\x + \\xo 11:25 \\xt அப் 16:22; அப் 14:19\\x*", "\\xt அப் 16:22; 14:19" },
                    new[] { "\\s எதிரிகளின் மேல் கோபம்", "\\s1" }, new[] { "\\d பேத். \\q", "\\qa பேத்." }, new[] { "\\s மனம் திரும்ப அழைப்பு", "யெகோவாவிடம் திரும்புங்கள்" },
                    new[] { "காசுகளைக்\\f + \\fr 8:11 \\ft ஒரு நாள் கூலி\\f*", "\\ft ஒரு வெள்ளிக்காசு, ஒரு நாள் கூலி\\f*" }, new[] { "அவனை \\f + \\fr 35:25 \\ft விழுங்கிவிட்டோம்\\f*என்று", "அவனை விழுங்கிவிட்டோம் என்று" },
                    new[] { "\\x + \\xo 95:7 \\xt எபி 3:7-11\\x*\\x + \\xo 95:7 \\xt எபி 3:15\\x*", "\\x + \\xo 95:7 \\xt எபி 3:7-11; 3:15\\x*" }, new[] { "முன் பழைய", "\\s தலைப்பு" } })
                    F.Check(UsfmFragment.Problem(partial[0], partial[1]) != null, "partial: " + partial[0] + " -> " + partial[1]);
                foreach (var whole in new[] { new[] { "\\ms1 சங்கீதம் 35", "\\s1 சங்கீதம் 35" }, new[] { "\\f + \\fr 4:8 \\ft அல்லது:", "\\f + \\fr 4:8 \\fq சுகமாக \\ft அல்லது:" }, new[] { "\\d ஆசாபின் பாடல்.", "\\s1 தேவனே என் பங்கு \\d ஆசாபின் பாடல்." },
                    new[] { "அவர்களைப்\\f + \\fr 22:16 \\ft விளக்கம்.\\f* பரிசுத்தமாக்குகிற", "அவர்களைப் பரிசுத்தமாக்குகிற" }, new[] { "பெயரிட்டு,", "பெயரிட்டு\\f + \\fr 17:15 \\ft கொடி\\f*," } })
                    F.Check((UsfmFragment.Problem(whole[0], whole[1]) == null) == (whole[0] != "பெயரிட்டு,"), "whole: " + whole[0]);
                F.Check(UsfmFragment.Occurrences("\\q ஒன்று \\q2 இரண்டு \\q மூன்று", "\\q").Count == 2, "\\q never matches inside \\q2");
            });
            test("footnote text, verse word beside a footnote, and footnote removal are written as exact USFM", delegate {
                F.Check(After(Base, "\\ft குறிப்பு\\f*", "\\ft புதிய குறிப்பு\\f*") == Base.Replace("\\ft குறிப்பு", "\\ft புதிய குறிப்பு"));
                F.Check(After(Base, "பழைய\\f + \\fr 1:3 \\ft குறிப்பு\\f*", "பழைய சொல்\\f + \\fr 1:3 \\ft குறிப்பு\\f*") == Base.Replace("முன் பழைய\\f", "முன் பழைய சொல்\\f"));
                F.Check(After(Base, "பழைய\\f + \\fr 1:3 \\ft குறிப்பு\\f* பின்", "பழைய பின்") == "\\c 1 \\v 2 முந்தைய வசனம் \\v 3 முன் பழைய பின் \\v 4 அடுத்த பழைய வசனம்");
            });
            test("a footnote the note spells out is added at the note's verse only, leaving the verse wording unchanged", delegate {
                F.Check(After(Plain, "பழைய", "பழைய\\f + \\fr 1:3 \\ft விளக்கம்\\f*") == "\\c 1 \\v 3 முன் பழைய\\f + \\fr 1:3 \\ft விளக்கம்\\f* பின் \\v 4 அடுத்த பழைய வசனம்");
            });
            test("a cross-reference is corrected inside its own markers", delegate {
                string usfm = "\\c 1 \\v 3 \\x + \\xo 1:3 \\xt ஆதி 12:7;  ஆதி 26:3\\x*முன் பழைய பின்";
                F.Check(After(usfm, "\\xt ஆதி 12:7;  ஆதி 26:3\\x*", "\\xt ஆதி 12:7; 26:3\\x*") == usfm.Replace("12:7;  ஆதி 26:3", "12:7; 26:3"));
            });
            test("a poetry line marker is changed on the one matching line; a bare marker that matches two lines is refused", delegate {
                string poem = "\\c 1 \\v 3 \\q முதல் வரி \\q இரண்டாம் வரி \\v 4 \\q2 வேறு";
                F.Check(After(poem, "\\q இரண்டாம் வரி", "\\q2 இரண்டாம் வரி") == "\\c 1 \\v 3 \\q முதல் வரி \\q2 இரண்டாம் வரி \\v 4 \\q2 வேறு");
                var f = Usfm(poem, "\\q", "\\q2", "USFM marker"); Refused(() => Write(f)); F.Check(f.Writes == 0);
            });
            test("a heading above the verse is changed with the whole chapter as evidence", delegate {
                string usfm = "\\c 1 \\v 2 முந்தைய \\s பழைய தலைப்பு \\v 3 முன் பின்";
                var f = Usfm(usfm, "\\s பழைய தலைப்பு", "\\s புதிய தலைப்பு"); var s = f.Service(); var n = s.ReadCandidates()[0]; ContentRegion region;
                var preview = s.PreviewFragment(n, n.Proposal.Original, n.Proposal.Suggested, out region);
                F.Check(region == ContentRegion.Heading && preview.Before == usfm && preview.After == usfm.Replace("பழைய தலைப்பு", "புதிய தலைப்பு") && f.Writes == 0);
                Write(f); F.Check(f.Serialize(f.Tokens) == usfm.Replace("பழைய தலைப்பு", "புதிய தலைப்பு"));
            });
            test("a marker change is refused when it is not at the note's verse, is ambiguous, or the text is stale", delegate {
                var elsewhere = Usfm("\\c 1 \\v 3 முன் பின் \\v 4 அடுத்த\\f + \\fr 1:4 \\ft குறிப்பு\\f* வசனம்", "\\ft குறிப்பு\\f*", "\\ft புதிய\\f*");
                Refused(() => Write(elsewhere)); F.Check(elsewhere.Writes == 0);
                var twice = Usfm("\\c 1 \\v 3 ஒன்று\\f + \\fr 1:3 \\ft குறிப்பு\\f* இரண்டு\\f + \\fr 1:3 \\ft குறிப்பு\\f*", "\\ft குறிப்பு\\f*", "\\ft புதிய\\f*");
                Refused(() => Write(twice)); F.Check(twice.Writes == 0);
                var stale = Usfm(Base, "\\ft வேறு குறிப்பு\\f*", "\\ft புதிய\\f*"); Refused(() => Write(stale)); F.Check(stale.Writes == 0);
                var spacing = Usfm(Base, "\\ft  குறிப்பு\\f*", "\\ft புதிய\\f*"); Refused(() => Write(spacing)); F.Check(spacing.Writes == 0, "spacing must match exactly");
            });
            test("a marker change never lands in the verse before the note's verse, splits a word, or cites another verse", delegate {
                // The note's verse no longer has the footnote, but the previous verse has an identical one.
                var previous = Usfm("\\c 1 \\v 2 முந்தைய\\f + \\fr 1:2 \\ft குறிப்பு\\f* வசனம் \\v 3 முன் பின்", "\\ft குறிப்பு\\f*", "\\ft புதிய\\f*");
                Refused(() => Write(previous)); F.Check(previous.Writes == 0, "previous verse's footnote");
                var previousText = Usfm("\\c 1 \\v 2 முந்தைய பழைய \\s தலைப்பு \\v 3 முன் பின்", "பழைய", "பழைய\\f + \\fr 1:3 \\ft விளக்கம்\\f*");
                Refused(() => Write(previousText)); F.Check(previousText.Writes == 0, "previous verse's words above a heading");
                var first = Usfm("\\c 1 \\s பழைய தலைப்பு \\v 3 முன் பின்", "\\c 1 \\s பழைய", "\\c 1 \\s புதிய"); Refused(() => Write(first)); F.Check(first.Writes == 0, "chapter marker");
                var longer = Usfm("\\c 1 \\v 3 முன் பழையது\\f + \\fr 1:3 \\ft குறிப்புகள்\\f* பின்", "\\ft குறிப்பு", "\\ft விளக்கம்"); Refused(() => Write(longer)); F.Check(longer.Writes == 0, "start of a longer word");
                var split = Usfm("\\c 1 \\v 3 முன் பழையது பின்", "பழைய", "பழைய\\f + \\fr 1:3 \\ft விளக்கம்\\f*"); Refused(() => Write(split)); F.Check(split.Writes == 0, "footnote call inside a word");
                var origin = Usfm(Plain, "பழைய", "பழைய\\f + \\fr 2:9 \\ft விளக்கம்\\f*"); Refused(() => Write(origin)); F.Check(origin.Writes == 0, "note origin names another verse");
                F.Check(UsfmFragment.Problem("கர்த்தர்", "\\nd கர்த்தர்") != null && UsfmFragment.Problem("\\nd கர்த்தர்\\nd*", "\\nd யெகோவா\\nd*") == null && UsfmFragment.Problem("(சேலா) \\qs*", "(சேலா)\\qs*") == null, "character styles stay balanced");
            });
            test("undo of a heading change restores the whole chapter", delegate {
                string usfm = "\\c 1 \\v 2 முந்தைய \\s பழைய தலைப்பு \\v 3 முன் பின்";
                var f = WithMatthew(Usfm(usfm, "\\s பழைய தலைப்பு", "\\s புதிய தலைப்பு"));
                using (var q = QueueTests.Queue(f, F.Root())) {
                    F.Enable(q); var item = F.Current(q); F.Check(F.Field<FastChoice>(q, "fastChoice").Region == ContentRegion.Heading);
                    F.Key(q, Keys.F5); F.Check(f.Writes == 1 && f.Serialize(f.Tokens) == usfm.Replace("பழைய தலைப்பு", "புதிய தலைப்பு") && F.Saved(q, item).Region == "Heading");
                    F.Key(q, Keys.F11); F.Check(f.Writes == 2 && f.Serialize(f.Tokens) == usfm && F.Current(q).StableId == item.StableId);
                }
            });
            test("a marker change that would break a note, change numbering or drop verse words is refused with nothing written", delegate {
                var unclosed = Usfm(Base, "குறிப்பு\\f*", "குறிப்பு\\x*"); Refused(() => Write(unclosed)); F.Check(unclosed.Writes == 0, "footnote closed by a cross-reference marker");
                var dropped = Usfm(Base, "குறிப்பு\\f* பின்", "குறிப்பு பின்"); Refused(() => Write(dropped)); F.Check(dropped.Writes == 0, "closing marker removed");
                var numbering = Usfm(Base, "\\v 3 முன்", "\\v 5 முன்"); Refused(() => Write(numbering)); F.Check(numbering.Writes == 0 && numbering.History == 0);
                // The note quotes verse words only to say where a heading belongs.
                var heading = Usfm(Plain, "முன் பழைய", "\\s தலைப்பு"); Refused(() => Write(heading)); F.Check(heading.Writes == 0, "heading instruction would delete verse words");
                var swap = Usfm(Plain, "பழைய", "புதிய\\f + \\fr 1:3 \\ft விளக்கம்\\f*"); Refused(() => Write(swap)); F.Check(swap.Writes == 0, "added note may not also change the wording");
                var extra = Usfm(Plain, "பழைய", "பழைய கூடுதல்\\f + \\fr 1:3 \\ft விளக்கம்\\f*"); Refused(() => Write(extra)); F.Check(extra.Writes == 0 && extra.History == 1, "caught on the reparsed chapter: verse words added beside a new note");
            });
            test("a marker change needs Paratext's exact round trip, the lock, permission and a fresh note", delegate {
                var f = Usfm(Base, "\\ft குறிப்பு\\f*", "\\ft புதிய\\f*"); f.ReparseDropsLastToken = true; Refused(() => Write(f)); F.Check(f.Writes == 0);
                var g = Usfm(Base, "\\ft குறிப்பு\\f*", "\\ft புதிய\\f*"); g.RealisticUsfm = false; Refused(() => Write(g)); F.Check(g.Writes == 0, "host cannot parse the prepared text");
                var locked = Usfm(Base, "\\ft குறிப்பு\\f*", "\\ft புதிய\\f*"); locked.LockAvailable = false; Refused(() => Write(locked)); F.Check(locked.Writes == 0);
                var revoked = Usfm(Base, "\\ft குறிப்பு\\f*", "\\ft புதிய\\f*"); revoked.Revoke = true; Refused(() => Write(revoked)); F.Check(revoked.Writes == 0 && revoked.Released == 1);
                var readOnly = Usfm(Base, "\\ft குறிப்பு\\f*", "\\ft புதிய\\f*"); readOnly.Editable = false; Refused(() => Write(readOnly)); F.Check(readOnly.Writes == 0);
                var changed = Usfm(Base, "\\ft குறிப்பு\\f*", "\\ft புதிய\\f*"); var s = changed.Service(); var n = s.ReadCandidates()[0]; changed.NoteText += " changed";
                Refused(() => s.ApplyFragment(n, n.Proposal.Original, n.Proposal.Suggested)); F.Check(changed.Writes == 0 && changed.Released == 1);
                var normal = Usfm(Base, "\\ft குறிப்பு\\f*", "\\ft புதிய\\f*"); normal.Normalizer = text => text + "x"; Refused(() => Write(normal)); F.Check(normal.Writes == 0);
            });
            test("fast apply writes a marker change exactly as previewed, and undo restores the chapter", delegate {
                var f = WithMatthew(Usfm(Base, "\\ft குறிப்பு\\f*", "\\ft புதிய குறிப்பு\\f*"));
                using (var q = QueueTests.Queue(f, F.Root())) {
                    F.Enable(q); var item = F.Current(q); var choice = F.Field<FastChoice>(q, "fastChoice");
                    F.Check(item.Book == 1 && choice != null && choice.Fragment && choice.Region == ContentRegion.Body && choice.Preview.After.Contains("\\ft புதிய குறிப்பு"), "marker change armed with its preview");
                    F.Check(F.Key(q, Keys.F5) && f.Writes == 1 && f.Serialize(f.Tokens) == Base.Replace("\\ft குறிப்பு", "\\ft புதிய குறிப்பு"));
                    var record = F.Saved(q, item);
                    F.Check(record.Outcome == "Applied" && record.BeforeContext == choice.Preview.Before && record.AfterContext == choice.Preview.After && f.Replies.Count == 0, "recorded as previewed, reply held");
                    F.Key(q, Keys.F11);
                    F.Check(f.Writes == 2 && f.Serialize(f.Tokens) == Base && f.Replies.Count == 0 && !f.Resolved && F.Current(q).StableId == item.StableId, "undo restored the chapter and reopened the note");
                }
            });
            test("fast apply refuses a marker change when the chapter changed after the preview", delegate {
                var f = WithMatthew(Usfm(Base, "\\ft குறிப்பு\\f*", "\\ft புதிய குறிப்பு\\f*"));
                using (var q = QueueTests.Queue(f, F.Root())) {
                    F.Enable(q); var item = F.Current(q);
                    f.Tokens = Fixture.Parse(Base.Replace("முன் பழைய", "முன் இடை பழைய"));
                    F.Key(q, Keys.F5);
                    F.Check(f.Writes == 0 && F.Current(q).StableId == item.StableId && !F.Field<bool>(q, "paused") && F.Field<Label>(q, "fastReason").Text.Contains("changed after it was shown"));
                }
            });
            test("a structural instruction without exact USFM is never armed, and an edited marker change is not used", delegate {
                var f = WithMatthew(Usfm(Base, "\\f + \\fr 1:3 \\ft குறிப்பு\\f*", "(supply the intended note text or drop the footnote)"));
                using (var q = QueueTests.Queue(f, F.Root())) { F.Enable(q); F.Check(F.Field<FastChoice>(q, "fastChoice") == null); F.Key(q, Keys.F5); F.Check(f.Writes == 0); }
                var g = WithMatthew(Usfm(Base, "\\ft குறிப்பு\\f*", "\\ft புதிய குறிப்பு\\f*"));
                using (var q = QueueTests.Queue(g, F.Root())) {
                    F.Enable(q); F.Field<TextBox>(q, "replacement").Text = "\\ft வேறு\\f*"; F.Call(q, "RefreshFast");
                    F.Check(F.Field<FastChoice>(q, "fastChoice") == null); F.Key(q, Keys.F5); F.Check(g.Writes == 0);
                }
            });
            test("fast apply uses the note's Tamil without its English remark", delegate {
                var f = F.TwoBooks(Fixture.Qa("Source comparison", "பழைய", "புதிய (for reviewer)", "Yes"));
                using (var q = QueueTests.Queue(f, F.Root())) {
                    F.Enable(q); var item = F.Current(q); var choice = F.Field<FastChoice>(q, "fastChoice");
                    F.Check(choice != null && choice.Replacement == "புதிய" && choice.Derived && F.Field<Label>(q, "fastSource").Text.Contains("English remark removed"));
                    F.Key(q, Keys.F5);
                    F.Check(f.Writes == 1 && f.Serialize(f.Tokens) == "\\v 3 முன் புதிய பின்" && F.Saved(q, item).Replacement == "புதிய");
                }
            });
            test("the other-wording key steps through the note's alternatives and the apply key writes the one shown", delegate {
                var f = F.TwoBooks(Fixture.Qa("Name consistency", "பழைய", "புதிய / வேறு", "Yes"));
                using (var q = QueueTests.Queue(f, F.Root())) {
                    F.Enable(q); F.Check(F.Field<FastChoice>(q, "fastChoice").Replacement == "புதிய" && F.Field<Label>(q, "fastSource").Text.Contains("wording 1 of 2"));
                    F.Check(F.Key(q, Keys.F4) && F.Field<FastChoice>(q, "fastChoice").Replacement == "வேறு" && F.Field<FastChoice>(q, "fastChoice").Preview.After.Contains("வேறு") && f.Writes == 0);
                    F.Key(q, Keys.F4); F.Check(F.Field<FastChoice>(q, "fastChoice").Replacement == "புதிய", "wraps round");
                    F.Key(q, Keys.F4); F.Key(q, Keys.F5);
                    F.Check(f.Writes == 1 && f.Serialize(f.Tokens) == "\\v 3 முன் வேறு பின்");
                    F.Check(F.Current(q).Book == 40 && F.Field<int>(q, "wordingIndex") == 0, "the next note starts at its first wording");
                    F.Check(F.Key(q, Keys.F4) && f.Writes == 1, "a note with one wording has nothing to step through");
                }
            });
            test("two open notes with different suggestions for the same words each stay available, with the other one shown", delegate {
                var f = F.TwoBooks(Fixture.Qa("Grammar", "பழைய", "புதிய", "No")); var second = new Fixture(); second.NoteText = Fixture.Qa("Name consistency", "பழைய", "வேறு", "No"); f.Notes.Add(second.Notes[0]);
                using (var q = QueueTests.Queue(f, F.Root())) {
                    F.Enable(q); var choice = F.Field<FastChoice>(q, "fastChoice"); string other = choice != null && choice.Replacement == "புதிய" ? "வேறு" : "புதிய";
                    F.Check(choice != null && F.Field<Label>(q, "fastSource").Text.Contains("Another open note suggests different wording") && F.Field<Label>(q, "fastSource").Text.Contains(other));
                    F.Key(q, Keys.F5); F.Check(f.Writes == 1);
                    // The second note's Original is gone now, so it cannot be applied on top.
                    F.Check(F.Current(q).Book == 1 && F.Field<FastChoice>(q, "fastChoice") == null, "second note not armed after the first was applied");
                    F.Key(q, Keys.F5); F.Check(f.Writes == 1);
                }
            });
            test("words the note quotes from a heading are found there, and the verse text is left alone", delegate {
                var f = F.TwoBooks(Fixture.Qa("Name consistency", "பழைய தலைப்பு", "புதிய தலைப்பு", "Yes"));
                f.Tokens = new List<IUSFMToken> { Fixture.Marker("s", "", 1, 2, MarkerType.Paragraph), Fixture.Text("பழைய தலைப்பு", 1, 2, false, false), Fixture.Marker("v", "3", 1, 3, MarkerType.Verse), Fixture.Text("முன் பின்", 1, 3, true, false) };
                using (var q = QueueTests.Queue(f, F.Root())) {
                    F.Enable(q); var choice = F.Field<FastChoice>(q, "fastChoice");
                    F.Check(choice != null && choice.Region == ContentRegion.Heading && choice.Derived && F.Field<Label>(q, "fastSource").Text.Contains("Found in the heading"));
                    F.Key(q, Keys.F5);
                    F.Check(f.Writes == 1 && f.Serialize(f.Tokens) == "\\s புதிய தலைப்பு\\v 3 முன் பின்");
                }
            });
            test("words found in both the verse and a footnote stay with the place the note names", delegate {
                var f = Usfm(Base.Replace("\\ft குறிப்பு", "\\ft பழைய குறிப்பு"), "பழைய", "புதிய", "Grammar"); var s = f.Service(); var n = s.ReadCandidates()[0];
                F.Check(s.LocateRegion(n, "பழைய", ContentRegion.Body) == ContentRegion.Body && s.LocateRegion(n, "குறிப்பு", ContentRegion.Body) == ContentRegion.Footnote);
                F.Check(s.LocateRegion(n, "இல்லாத", ContentRegion.Body) == ContentRegion.Body && s.LocateRegion(n, "பழைய", ContentRegion.Heading) == ContentRegion.Heading, "no single other place: keep the note's");
                F.Check(s.LocateRegion(n, "முன்", ContentRegion.Heading) == ContentRegion.Heading, "a heading note that quotes verse words never falls back to the verse text");
                F.Check(s.LocateRegion(n, "குறிப்பு", ContentRegion.Heading) == ContentRegion.Footnote);
            });
            test("a note that warned its Original was missing is armed only because the live verse now has it exactly once", delegate {
                string note = Fixture.Qa("Punctuation", "பழைய", "புதிய", "No").Replace("\r\nOriginal:", "\r\nLocation: whole verse — flagged text not found verbatim in the current text\r\nOriginal:");
                var f = F.TwoBooks(note);
                using (var q = QueueTests.Queue(f, F.Root())) { F.Enable(q); F.Check(F.Field<FastChoice>(q, "fastChoice") != null && F.Field<Label>(q, "fastSource").Text.Contains("found exactly once now")); }
                var g = F.TwoBooks(note); g.Tokens[1] = Fixture.Text("முன் வேறு பின்", 1, 3, true, false);
                using (var q = QueueTests.Queue(g, F.Root())) { F.Enable(q); F.Check(F.Field<FastChoice>(q, "fastChoice") == null); F.Key(q, Keys.F5); F.Check(g.Writes == 0); }
                var twice = F.TwoBooks(note); twice.Tokens[1] = Fixture.Text("பழைய இடை பழைய", 1, 3, true, false); twice.AnchorText = "";
                using (var q = QueueTests.Queue(twice, F.Root())) { F.Enable(q); F.Check(F.Field<FastChoice>(q, "fastChoice") == null); F.Key(q, Keys.F5); F.Check(twice.Writes == 0, "twice in the verse"); }
            });
        }
    }
}
