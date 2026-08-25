using Microsoft.Office.Interop.Word;
using System;
using System.Collections.Generic;
using System.Text;

namespace KleiKodesh.Helpers
{
    /// <summary>
    /// Rewrites the Word selection into a reference text as tracked revisions - the
    /// "השווה עם כתבי הקודש" context-menu item (issue #244). The reference (the text
    /// selected in the Kitvei Hakodesh pane) is the authority: after accepting all
    /// revisions the Word passage IS the reference passage, and every difference is
    /// visible as an insertion or strike-through the user can judge one by one.
    ///
    /// The diff is word-by-word, not character-by-character - character edits inside
    /// Hebrew words produce unreadable mid-word revision fragments. Words are compared
    /// exactly as they are: nikud and te'amim count as differences, by design.
    /// </summary>
    public static class TrackedCompare
    {
        struct Token { public string Text; public int Start; public int End; }

        // One contiguous difference: source tokens [AFrom,ATo) become reference
        // tokens [BFrom,BTo). Either side may be empty (pure insert / pure delete).
        struct Hunk { public int AFrom, ATo, BFrom, BTo; }

        /// <summary>
        /// Applies <paramref name="referenceText"/> onto the current selection as
        /// tracked revisions. Returns null on success, or a Hebrew message telling
        /// the user why nothing was changed.
        /// </summary>
        public static string ApplyReference(Selection selection, string referenceText)
        {
            var range = selection?.Range?.Duplicate;
            if (range == null) return "יש לסמן קטע להשוואה במסמך";

            TrimTrailingMarks(range);
            string source = range.Text;
            if (string.IsNullOrWhiteSpace(source)) return "יש לסמן קטע להשוואה במסמך";
            if (range.Paragraphs.Count > 1) return "ניתן להשוות פסקה אחת בלבד";

            // Pending revisions poison the comparison: Range.Text still contains text
            // that is struck through but not yet accepted, so a second compare over the
            // same passage would diff run 1's deletions and insertions as live words.
            if (range.Revisions.Count > 0)
                return "יש לאשר או לדחות תחילה את השינויים המסומנים בקטע";

            // Everything below maps character indexes in Text to document positions
            // one-to-one. Content the mapping cannot represent (fields, footnote
            // references and the like) makes the range longer than its text - refuse
            // rather than strike through the wrong words.
            if (range.End - range.Start != source.Length)
                return "לא ניתן להשוות קטע עם תוכן מיוחד (שדות או הערות)";

            // Footnote, endnote and comment reference marks and inline objects occupy
            // one character each, so the length identity above holds for them - but a
            // tracked delete would carry the mark, and its footnote, away with the word.
            if (source.IndexOf('\u0001') >= 0 || source.IndexOf('\u0002') >= 0 || source.IndexOf('\u0005') >= 0)
                return "לא ניתן להשוות קטע עם תוכן מיוחד (שדות או הערות)";

            var a = Tokenize(source);
            var b = Tokenize(referenceText);
            if (b.Count == 0) return "לא נמצא טקסט מסומן בכתבי הקודש";
            if ((long)a.Count * b.Count > 4_000_000) return "הקטעים ארוכים מדי להשוואה";

            var hunks = Diff(a, b);
            if (hunks.Count == 0) return "לא נמצאו הבדלים בין הקטעים";

            var doc = range.Document;
            var app = doc.Application;
            bool wasTracking = doc.TrackRevisions;

            // One custom undo record so a single Ctrl+Z reverts the whole comparison.
            // No Find/Replace in here: wdReplaceAll inside an open UndoRecord crashes
            // Word (see DocDesignLib) - all edits go through Range operations.
            app.UndoRecord.StartCustomRecord("השוואה עם כתבי הקודש");
            try
            {
                doc.TrackRevisions = true;

                // End to start, so positions computed from the original text stay
                // valid: a tracked deletion keeps its characters in place, and an
                // insertion only shifts positions after it - which were already done.
                for (int i = hunks.Count - 1; i >= 0; i--)
                    ApplyHunk(doc, range.Start, a, b, hunks[i]);
            }
            finally
            {
                doc.TrackRevisions = wasTracking;
                app.UndoRecord.EndCustomRecord();
            }

            ShowAllMarkup(doc);
            return null;
        }

        // The revisions are the entire output of the comparison - in "Simple Markup"
        // or "No Markup" the click would appear to do nothing. Word's UI calls the
        // mode set here "כל הסימונים" (All Markup).
        static void ShowAllMarkup(Document doc)
        {
            try
            {
                doc.ActiveWindow.View.RevisionsFilter.Markup =
                    WdRevisionsMarkup.wdRevisionsMarkupAll;
            }
            catch { /* best-effort - the revisions are in the document regardless */ }
        }

        static void ApplyHunk(Document doc, int origin, List<Token> a, List<Token> b, Hunk h)
        {
            // Delete before inserting: the struck text keeps its positions under
            // tracking, so the insertion lands just before it and the result reads
            // as "new text replaces old text".
            if (h.AFrom < h.ATo)
            {
                int delStart = a[h.AFrom].Start;
                int delEnd = a[h.ATo - 1].End;

                // A pure deletion takes one separator with it, or accepting the
                // revisions would leave a doubled space where the words were. Hunks
                // are separated by at least one matched token, so the whitespace
                // eaten here never belongs to a neighbouring hunk.
                if (h.BFrom == h.BTo)
                {
                    if (h.ATo < a.Count) delEnd = a[h.ATo].Start;
                    else if (h.AFrom > 0) delStart = a[h.AFrom - 1].End;
                }

                doc.Range(origin + delStart, origin + delEnd).Delete();
            }

            if (h.BFrom == h.BTo) return;

            var text = new StringBuilder();
            for (int i = h.BFrom; i < h.BTo; i++)
            {
                if (i > h.BFrom) text.Append(' ');
                text.Append(b[i].Text);
            }

            int pos;
            if (h.AFrom < h.ATo)
            {
                // Replacement - sit where the deleted words start; the whitespace
                // around the hunk is untouched.
                pos = origin + a[h.AFrom].Start;
            }
            else if (h.AFrom < a.Count)
            {
                // Pure insertion before an existing word - bring a separating space.
                pos = origin + a[h.AFrom].Start;
                text.Append(' ');
            }
            else
            {
                // Pure insertion after the last word.
                pos = origin + a[a.Count - 1].End;
                text.Insert(0, ' ');
            }

            doc.Range(pos, pos).Text = text.ToString();
        }

        /// <summary>
        /// Longest-common-subsequence diff over word tokens, folding every run of
        /// consecutive non-matching steps into one hunk.
        /// </summary>
        static List<Hunk> Diff(List<Token> a, List<Token> b)
        {
            int m = a.Count, n = b.Count;
            var lcs = new int[m + 1, n + 1];
            for (int i = m - 1; i >= 0; i--)
                for (int j = n - 1; j >= 0; j--)
                    lcs[i, j] = a[i].Text == b[j].Text
                        ? lcs[i + 1, j + 1] + 1
                        : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

            var hunks = new List<Hunk>();
            int x = 0, y = 0;
            while (x < m || y < n)
            {
                if (x < m && y < n && a[x].Text == b[y].Text) { x++; y++; continue; }

                var h = new Hunk { AFrom = x, BFrom = y };
                while (x < m || y < n)
                {
                    if (x < m && y < n && a[x].Text == b[y].Text) break;
                    if (y < n && (x == m || lcs[x, y + 1] >= lcs[x + 1, y])) y++;
                    else x++;
                }
                h.ATo = x; h.BTo = y;
                hunks.Add(h);
            }
            return hunks;
        }

        // Words are maximal runs of non-whitespace, remembered with their character
        // offsets in the source. Newlines count as whitespace, which also flattens a
        // pane selection spanning several rendered lines into one comparable stream.
        static List<Token> Tokenize(string text)
        {
            var tokens = new List<Token>();
            int i = 0;
            while (i < text.Length)
            {
                if (char.IsWhiteSpace(text[i])) { i++; continue; }
                int start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
                tokens.Add(new Token { Text = text.Substring(start, i - start), Start = start, End = i });
            }
            return tokens;
        }

        // A selection dragged to the end of a paragraph includes its paragraph mark
        // (and, in a table, the cell mark). Left in, the mark makes Paragraphs.Count
        // read one too many and puts the boundary itself inside the diff - only the
        // text is compared.
        static void TrimTrailingMarks(Range range)
        {
            while (range.End > range.Start)
            {
                string t = range.Text;
                if (string.IsNullOrEmpty(t)) return;
                char last = t[t.Length - 1];
                if (last != '\r' && last != '\a') return;
                if (range.MoveEnd(WdUnits.wdCharacter, -1) == 0) return; // no movement = no progress
            }
        }
    }
}
