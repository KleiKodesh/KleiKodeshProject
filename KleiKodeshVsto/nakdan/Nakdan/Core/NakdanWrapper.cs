using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using System.Threading.Tasks;
using System.Windows;
using Nakdan.Core;
using Word = Microsoft.Office.Interop.Word;

namespace Nakdan.Core
{
    // ═══════════════════════════════════════════════════════════
    //  NAKDAN API
    //
    //  This is the only file your UI / task pane / ribbon needs
    //  to know about. All Word interop lives here.
    //
    //  Setup (once, e.g. in ThisAddIn.cs):
    //      NakdanApi Api = new NakdanApi(Globals.ThisAddIn.Application);
    //
    //  Then wire buttons:
    //      btnSelection.Click += (s,e) => Api.RunSafe(Api.VowelizeSelectionAsync);
    // ═══════════════════════════════════════════════════════════
    public class NakdanWrapper
    {
        private readonly NakdanEngine     _engine = new NakdanEngine();

        // ── Options (set from UI before running) ─────────────────
        public NakdanOptions Options { get; set; } = new NakdanOptions();

        /// <summary>
        /// Callback for progress updates during vowelization.
        /// Set this before calling VowelizeSelectionAsync.
        /// </summary>
        public Action<string> OnProgress
        {
            get => _engine.OnProgress;
            set => _engine.OnProgress = value;
        }

        // ════════════════════════════════════════════════════════
        //  BUTTON — Vowelize current selection only
        // ════════════════════════════════════════════════════════
        public async Task VowelizeSelectionAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            Word.Selection sel = Helpers.VstoHelper.Application.Selection;

            // Guard: make sure something is actually selected
            if (sel.Type == Word.WdSelectionType.wdSelectionIP ||
                string.IsNullOrWhiteSpace(sel.Range.Text))
                throw new InvalidOperationException(
                    "אין טקסט מסומן.\nיש לסמן טקסט לפני הפעלת הניקוד.");

            string ooxml     = sel.Range.WordOpenXML;
            string vowelized = await _engine.VowelizeOoxmlAsync(ooxml, Options, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            
            Word.Range r = sel.Range;
            r.InsertXML(vowelized);
            r.Select();
        }

        // ════════════════════════════════════════════════════════
        //  Remove nikkud from current selection
        // ════════════════════════════════════════════════════════
        public void RemoveNikkud()
        {            
            Word.Selection selection = Helpers.VstoHelper.Application.Selection;

            // Guard: make sure something is actually selected
            if (selection.Type == Word.WdSelectionType.wdSelectionIP ||
                string.IsNullOrWhiteSpace(selection.Range.Text))
                throw new InvalidOperationException(
                    "אין טקסט מסומן.\nיש לסמן טקסט לפני הסרת הניקוד.");

            var find = selection.Range.Find;
            find.Text = "[^0192-^0204^0209^0210]";
            find.Replacement.Text = "";
            find.MatchWildcards = true;
            find.Wrap = Word.WdFindWrap.wdFindStop;
            find.Execute(Replace: Word.WdReplace.wdReplaceAll);
        }

        // ════════════════════════════════════════════════════════
        //  Remove cantillations (ta'amim) from current selection
        // ════════════════════════════════════════════════════════
        public void RemoveCantillations()
        {
            Word.Selection selection = Helpers.VstoHelper.Application.Selection;

            if (selection.Type == Word.WdSelectionType.wdSelectionIP ||
                string.IsNullOrWhiteSpace(selection.Range.Text))
                throw new InvalidOperationException(
                    "אין טקסט מסומן.\nיש לסמן טקסט לפני הסרת הטעמים.");

            var find = selection.Range.Find;
            find.MatchWildcards = true;
            find.Wrap = Word.WdFindWrap.wdFindStop;

            find.Text = " ׀ ";
            find.Replacement.Text = " ";
            find.Execute(Replace: Word.WdReplace.wdReplaceAll);

            // Build a range string with the actual first and last cantillation characters
            // U+0591 (Etnahta) through U+05AF (Masora Circle)
            string first = "\u0591";
            string last = "\u05AF";
            find.Text = "[" + first + "-" + last + "׀]";
            find.Replacement.Text = "";
            find.Execute(Replace: Word.WdReplace.wdReplaceAll);         
        }

        // ════════════════════════════════════════════════════════
        //  SAFE RUNNER
        //  Call this from any button click to handle threading,
        //  busy cursor, and error display automatically.
        //
        //  Usage:
        //      button.Click += (s, e) => RunSafe(VowelizeSelectionAsync);
        // ════════════════════════════════════════════════════════
        public void RunSafe(Func<Task> action)
        {
            SetCursor(busy: true);

            Task.Run(async () =>
            {
                try
                {
                    await action();
                }
                catch (InvalidOperationException ex)
                {
                    // User errors (no selection, no footnotes, etc.)
                    MessageBox.Show(ex.Message, "ניקוד",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    // API / network errors
                    MessageBox.Show(
                        "שגיאה בניקוד:\n" + ex.Message, "שגיאה",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    SetCursor(busy: false);
                }
            });
        }

        // ════════════════════════════════════════════════════════
        //  STYLE HELPERS — call these from your UI
        // ════════════════════════════════════════════════════════

        /// <summary>
        /// Set the list of style names to skip.
        /// Pass Hebrew or English style names, case-insensitive.
        /// e.g. SetIgnoredStyles("כותרת 1", "Heading 2", "הדגשה")
        /// </summary>
        public void SetIgnoredStyles(params string[] styleNames)
        {
            Options.IgnoredStyles = new List<string>(styleNames);
        }

        /// <summary>
        /// Add a single style name to the ignore list.
        /// </summary>
        public void AddIgnoredStyle(string styleName)
        {
            if (string.IsNullOrWhiteSpace(styleName))
                return;

            if (!Options.IgnoredStyles
                .Any(s => string.Equals(s, styleName, StringComparison.OrdinalIgnoreCase)))
            {
                Options.IgnoredStyles.Add(styleName);
            }
        }

        /// <summary>
        /// Remove a style name from the ignore list.
        /// </summary>
        public void RemoveIgnoredStyle(string styleName)
        {
            if (string.IsNullOrWhiteSpace(styleName))
                return;

            var matches = Options.IgnoredStyles
                .Where(s => string.Equals(s, styleName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var m in matches)
                Options.IgnoredStyles.Remove(m);
        }

        /// <summary>
        /// Clear all ignored styles (vowelize everything).
        /// </summary>
        public void ClearIgnoredStyles()
        {
            Options.IgnoredStyles.Clear();
        }

        /// <summary>
        /// Set the Dicta genre (Modern / Poetry / Bible / Rabbinic).
        /// </summary>
        public void SetGenre(DictaGenre genre)
        {
            Options.Genre = genre;
        }

        // ════════════════════════════════════════════════════════
        //  PRIVATE HELPERS
        // ════════════════════════════════════════════════════════
        private void SetCursor(bool busy)
        {
            Helpers.VstoHelper.Application.System.Cursor = busy
                ? Word.WdCursorType.wdCursorWait
                : Word.WdCursorType.wdCursorNormal;
        }
    }
}
