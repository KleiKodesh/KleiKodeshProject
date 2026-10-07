using KitveiHakodeshLib.Bridge;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using CoreQueries = KitveiHakodesh.Core.SeforimDb.SeforimDbQueries;

namespace KitveiHakodeshLib.Db
{
    /// <summary>
    /// The TYPED seforim bridge for the hosted app: the frontend names an operation and its
    /// arguments, and KitveiHakodesh.Core runs the query.
    ///
    /// This is the hosted counterpart of the dev service's RPC dispatcher, and it answers the
    /// SAME operation names — so seforimApi.ts stops being two implementations of every
    /// capability, one branch sending SQL text and the other naming an op.
    ///
    /// Why it replaces the SQL passthrough (see DbHandler.HandleSql): with the SQL living in the
    /// page, a query and the code reading its rows had two separate homes, and a schema change
    /// had to be made in both. Core owns both halves now, and the hosted and dev legs cannot
    /// drift apart because they call the same methods.
    /// </summary>
    public sealed class SeforimQueryHandler
    {
        private readonly WebBridge _bridge;
        private readonly CoreQueries _queries;

        public SeforimQueryHandler(WebBridge bridge, string seforimDbPath)
        {
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            _queries = new CoreQueries(seforimDbPath);
        }

        /// <summary>Whether there is a database to read.</summary>
        public bool IsReady => _queries.IsAvailable;

        /// <summary>
        /// Runs one named operation and replies with its rows.
        ///
        /// The query runs OFF the WinForms UI thread: that thread also services WebView2
        /// accelerator keys, so a stall here delays Ctrl+Tab / Ctrl+W reaching the page. Core
        /// opens a fresh read-only connection per call, so concurrent ops are safe.
        /// </summary>
        public async Task HandleAsync(JsonElement root, string id)
        {
            if (!_queries.IsAvailable)
            {
                _bridge.Reply(id, new { error = "No database loaded" });
                return;
            }

            string op = root.TryGetProperty("op", out var o) ? o.GetString() : null;
            if (string.IsNullOrEmpty(op))
            {
                _bridge.Reply(id, new { error = "seforimQuery requires an op" });
                return;
            }

            // Arguments are read on THIS thread: a JsonElement is a view onto a JsonDocument
            // that the message loop disposes when it returns, so reading one on the worker
            // thread would be a use-after-dispose.
            Args args;
            try { args = Args.From(root); }
            catch (Exception ex) { _bridge.Reply(id, new { error = ex.Message }); return; }

            try
            {
                object payload = await Task.Run(() => Run(op, args)).ConfigureAwait(false);
                _bridge.Reply(id, payload);
            }
            catch (Exception ex)
            {
                _bridge.Reply(id, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Maps an operation name onto a Core query. The reply shape matches the dev service's
        /// for the same op, because seforimApi.ts reads one shape whichever leg answered.
        /// </summary>
        private object Run(string op, Args a)
        {
            switch (op)
            {
                // -- Catalog --------------------------------------------------------
                case "getAllCategories": return new { rows = _queries.GetAllCategories() };
                case "getAllBooks": return new { rows = _queries.GetAllBooks() };
                case "getBookById": return new { book = _queries.GetBookById(a.Int("id")) };
                case "getBookIdsByTitlePattern": return new { rows = _queries.GetBookIdsByTitlePattern(a.Str("pattern")) };
                case "getBookIdByExactTitle": return new { rows = _queries.GetBookIdByExactTitle(a.Str("title")) };
                case "getBookSourceName": return new { rows = _queries.GetBookSourceName(a.Int("bookId")) };
                case "getLibraryDbVersion": return new { rows = _queries.GetLibraryDbVersion() };

                // -- Lines ----------------------------------------------------------
                case "getLinesPaged":
                    return new { rows = _queries.GetLinesPaged(
                        a.Int("bookId"), a.Int("limit"), a.Int("offset"), a.IntOr("versionId", 0)) };
                case "getLineContents": return new { rows = _queries.GetLineContents(a.Ints("lineIds")) };
                case "getLineIndexFromLineId": return new { rows = _queries.GetLineIndexFromLineId(a.Int("lineId")) };
                case "getLineByBookAndLineIndex":
                    return new { rows = _queries.GetLineByBookAndLineIndex(a.Int("bookId"), a.Int("lineIndex")) };
                case "getLineRefAndContent":
                    return new { rows = _queries.GetLineRefAndContent(a.Int("bookId"), a.Int("lineIndex")) };
                case "getLinesWithContentPatternForBooks":
                    return new { rows = _queries.GetLinesWithContentPatternForBooks(a.Ints("bookIds"), a.Str("pattern")) };
                case "getLinesWithEitherContentPattern":
                    return new { rows = _queries.GetLinesWithEitherContentPattern(
                        a.Int("bookId"), a.Str("p1"), a.Str("p2")) };

                // -- Versions -------------------------------------------------------
                case "getBookVersions": return new { rows = _queries.GetBookVersions(a.Int("bookId")) };

                // -- TOC ------------------------------------------------------------
                case "getAllTocEntries": return new { rows = _queries.GetAllTocEntries(a.Int("bookId")) };
                case "getAllAltTocEntries": return new { rows = _queries.GetAllAltTocEntries(a.Int("structureId")) };
                case "getAltTocStructures": return new { rows = _queries.GetAltTocStructures(a.Int("bookId")) };
                case "getTocEntryByTextPrefix":
                    return new { rows = _queries.GetTocEntryByTextPrefix(a.Int("bookId"), a.Str("pattern")) };
                case "getTocTitlesForBooks":
                    return new { rows = _queries.GetTocTitlesForBooks(a.Ints("bookIds"), a.StrOrNull("filterWord")) };
                case "getTocPathsForLines": return new { rows = _queries.GetTocPathsForLines(a.Ints("lineIds")) };
                case "getEnclosingTocPathForLineRanges":
                    return new { rows = _queries.GetEnclosingTocPathForLineRanges(a.Ints("triples")) };
                case "getTocSectionWithCommentary":
                    return new { rows = _queries.GetTocSectionWithCommentary(
                        a.Int("mainBookId"), a.Int("commentaryBookId"), a.Ints("rangePairs"), a.IsNext()) };

                // -- Links and commentary -------------------------------------------
                case "getAllConnectionTypes": return new { rows = _queries.GetAllConnectionTypes() };
                case "getDefaultCommentators": return new { rows = _queries.GetDefaultCommentators(a.Int("bookId")) };
                case "getCommentaryLinksForSourceLineRange":
                    return new { rows = _queries.GetCommentaryLinksForSourceLineRange(a.Ints("lineIds")) };
                case "getLinkTargetForSourceLineAndBook":
                    return new { rows = _queries.GetLinkTargetForSourceLineAndBook(
                        a.Int("sourceLineId"), a.Int("targetBookId")) };
                case "getSectionWithCommentary":
                    return new { rows = _queries.GetSectionWithCommentary(
                        a.Int("mainBookId"), a.Int("commentaryBookId"), a.Int("lineIndex"), a.IsNext()) };
                case "getStaticFilterBooks":
                    return new { rows = _queries.GetStaticFilterBooks(a.Int("sourceBookId"), a.Ints("typeIds")) };
                case "getBookIdsForLines": return new { rows = _queries.GetBookIdsForLines(a.Ints("lineIds")) };

                // -- Reverse lookups (source / targum) ------------------------------
                case "getReverseLineData":
                    return new { rows = _queries.GetReverseLineData(a.Ints("lineIds"), a.Ints("typeIds")) };
                case "getReverseBooks":
                    return new { rows = _queries.GetReverseBooks(a.Int("bookId"), a.Ints("typeIds")) };

                // -- Word links -----------------------------------------------------
                case "getWordLinkAnchorsForLines":
                {
                    var anchors = _queries.GetWordLinkAnchorsForLines(a.Ints("lineIds"));
                    return new { rows = anchors.Rows, supported = anchors.Supported };
                }
                case "getWordLinkAnchorTargetsForBook":
                {
                    var targets = _queries.GetWordLinkAnchorTargetsForBook(a.Int("bookId"));
                    return new { rows = targets.Rows, supported = targets.Supported };
                }

                default:
                    throw new ArgumentException("unknown seforimQuery op: " + op);
            }
        }

        /// <summary>
        /// The operation's arguments, COPIED out of the bridge message — see HandleAsync for
        /// why they cannot be read later.
        /// </summary>
        private sealed class Args
        {
            private readonly Dictionary<string, JsonElement> _values =
                new Dictionary<string, JsonElement>(StringComparer.Ordinal);

            /// <summary>
            /// Reads the arguments out of the message's nested "params" object.
            ///
            /// Nested, not flattened: JsBridge builds the message as
            /// Object.assign({ id }, payload), so an argument spread onto the root called "id"
            /// would overwrite the bridge's correlation id and the reply could never be routed
            /// back — getBookById takes exactly such an argument. Nesting keeps every argument
            /// name legal.
            /// </summary>
            public static Args From(JsonElement root)
            {
                var args = new Args();
                if (!root.TryGetProperty("params", out var bag) || bag.ValueKind != JsonValueKind.Object)
                    return args;

                foreach (var property in bag.EnumerateObject())
                    args._values[property.Name] = property.Value.Clone();
                return args;
            }

            private JsonElement Require(string name)
            {
                if (!_values.TryGetValue(name, out var value))
                    throw new ArgumentException("missing argument: " + name);
                return value;
            }

            public int Int(string name) => Require(name).GetInt32();

            public int IntOr(string name, int fallback) =>
                _values.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.Number
                    ? value.GetInt32() : fallback;

            /// <summary>
            /// Which way a section walk goes. The wire carries "next" / "prev" as a STRING, and
            /// anything that is not "prev" means next — the same rule the dev service applies,
            /// so an omitted or unexpected value behaves identically on both legs.
            /// </summary>
            public bool IsNext() =>
                !(_values.TryGetValue("direction", out var value)
                  && value.ValueKind == JsonValueKind.String
                  && string.Equals(value.GetString(), "prev", StringComparison.Ordinal));

            public string Str(string name) => Require(name).GetString() ?? "";

            public string StrOrNull(string name) =>
                _values.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString() : null;

            public List<int> Ints(string name)
            {
                var list = new List<int>();
                var value = Require(name);
                if (value.ValueKind != JsonValueKind.Array)
                    throw new ArgumentException("argument must be an array: " + name);
                foreach (var item in value.EnumerateArray()) list.Add(item.GetInt32());
                return list;
            }
        }
    }
}
