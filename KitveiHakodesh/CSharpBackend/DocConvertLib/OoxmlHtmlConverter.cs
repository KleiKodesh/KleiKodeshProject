using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Xml.Linq;

namespace DocConvertLib;

/// <summary>
/// Office-free, AOT-safe OOXML (.docx/.docm/.dotx/.dotm) → HTML renderer. The Word COM converter
/// (<see cref="AotWordConverter"/>) is preferred; this is the fallback when Word fails / isn't
/// installed. Designed to match the coverage of Open-Xml-PowerTools' HtmlConverter — the full
/// style cascade, direct run/paragraph formatting, numbering/lists, tables (borders/shading/
/// merges/widths) and embedded images — while ALSO doing what PowerTools drops: footnotes and
/// endnotes, rendered Wikipedia-style (superscript [n] refs → a "הערות" section with ↑ backlinks).
///
/// Robustness is the hard requirement: every part is optional, every risky step is guarded, and
/// every loop is bounded — a malformed or unusual document degrades (skips the bad bit) rather
/// than throwing or hanging. Only ZipArchive + LINQ-to-XML + StringBuilder are used (no reflection,
/// no System.Drawing) so it is fully native-AOT compatible.
/// </summary>
public static class OoxmlHtmlConverter
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace PIC = "http://schemas.openxmlformats.org/drawingml/2006/picture";
    private static readonly XNamespace XmlNs = XNamespace.Xml;

    public static string ConvertToHtml(string docxPath, string? title = null)
    {
        using FileStream fs = File.OpenRead(docxPath);
        return ConvertToHtml(fs, title ?? Path.GetFileNameWithoutExtension(docxPath));
    }

    public static string ConvertToHtml(Stream docxStream, string title)
    {
        using var zip = new ZipArchive(docxStream, ZipArchiveMode.Read, leaveOpen: true);

        XDocument doc = LoadPart(zip, "word/document.xml")
                        ?? throw new InvalidDataException("Not a Word document (no word/document.xml).");
        XDocument? stylesDoc = LoadPart(zip, "word/styles.xml");
        var ctx = new Ctx
        {
            Styles = new StyleResolver(stylesDoc),
            Numbering = new NumberingResolver(LoadPart(zip, "word/numbering.xml"), stylesDoc),
            Footnotes = LoadNotes(zip, "word/footnotes.xml", W + "footnote"),
            Endnotes = LoadNotes(zip, "word/endnotes.xml", W + "endnote"),
            Rels = LoadRels(zip),
            Zip = zip,
        };

        var main = new StringBuilder(1 << 16);
        XElement? body = doc.Root?.Element(W + "body");
        if (body is not null) RenderBlocks(body, main, ctx);
        // (footnotes appended below build; the fabricated CSS classes are emitted into the page head)

        var notes = new StringBuilder();
        if (ctx.Ordered.Count > 0)
        {
            notes.Append("<hr class=\"notes-sep\"><section class=\"footnotes\" dir=\"rtl\"><h2>הערות</h2><ol>");
            foreach ((char kind, string id, int num) in ctx.Ordered)
            {
                var dict = kind == 'f' ? ctx.Footnotes : ctx.Endnotes;
                notes.Append($"<li id=\"fn-{num}\"><a class=\"fn-back\" href=\"#fnref-{num}\" title=\"חזרה\">↑</a> ");
                if (dict.TryGetValue(id, out XElement? note)) RenderNoteBody(note, notes, ctx);
                notes.Append("</li>");
            }
            notes.Append("</ol></section>");
        }

        return Page(WebUtility.HtmlEncode(title), main.ToString() + notes.ToString(),
                    ctx.Styles.DocDefaultRun.BaseFontCss(), ctx.Classes.Emit());
    }

    // ── block-level (paragraphs, tables) ───────────────────────────────────────────────
    private static void RenderBlocks(XElement parent, StringBuilder sb, Ctx ctx)
    {
        foreach (XElement el in parent.Elements())
        {
            try
            {
                if (el.Name == W + "p") RenderParagraph(el, sb, ctx);
                else if (el.Name == W + "tbl") RenderTable(el, sb, ctx);
                // sdt (content controls) wrap content — descend into their body.
                else if (el.Name == W + "sdt")
                {
                    XElement? content = el.Element(W + "sdtContent");
                    if (content is not null) RenderBlocks(content, sb, ctx);
                }
            }
            catch { /* one bad block never breaks the document */ }
        }
    }

    private static void RenderParagraph(XElement p, StringBuilder sb, Ctx ctx)
    {
        XElement? pPr = p.Element(W + "pPr");
        string? pStyleId = pPr?.Element(W + "pStyle")?.Attribute(W + "val")?.Value;

        // effective paragraph props (docDefaults → basedOn chain → style → direct)
        var pp = ctx.Styles.ResolveParagraph(pStyleId, pPr);

        // list prefix (numbering)
        string listPrefix = "";
        XElement? numPr = pPr?.Element(W + "numPr");
        if (numPr is not null)
        {
            string? numId = numPr.Element(W + "numId")?.Attribute(W + "val")?.Value;
            int ilvl = ParseInt(numPr.Element(W + "ilvl")?.Attribute(W + "val")?.Value, 0);
            if (numId is not null && numId != "0")
                listPrefix = ctx.Numbering.NextLabel(numId, ilvl);
        }

        string tag = HeadingTag(pStyleId, ctx);
        // paragraph layout + the paragraph's own run-level formatting (from its style), as a
        // reusable class — deduped across all paragraphs with the same resolved formatting.
        string cls = ctx.Classes.ClassFor(ParagraphCss(pp) + pp.RunProps.CssDelta(ctx.Styles.DocDefaultRun));
        var inner = new StringBuilder();
        if (listPrefix.Length > 0) inner.Append("<span class=\"lbl\">").Append(WebUtility.HtmlEncode(listPrefix)).Append("</span> ");
        RenderInline(p, inner, ctx, pp.RunProps);

        string content = inner.ToString();
        if (content.Length == 0 && tag == "p") { sb.Append("<p class=\"empty\"></p>"); return; }
        sb.Append('<').Append(tag);
        if (cls.Length > 0) sb.Append(" class=\"").Append(cls).Append('"');
        sb.Append('>').Append(content).Append("</").Append(tag).Append('>');
    }

    // ── inline (runs, hyperlinks, drawings) ─────────────────────────────────────────────
    private static void RenderInline(XElement container, StringBuilder sb, Ctx ctx, RunProps inherited)
    {
        foreach (XElement node in container.Elements())
        {
            if (node.Name == W + "r") RenderRun(node, sb, ctx, inherited);
            else if (node.Name == W + "hyperlink")
            {
                string? relId = node.Attribute(R + "id")?.Value;
                string? href = relId is not null && ctx.Rels.TryGetValue(relId, out string? t) ? t : null;
                string? anchor = node.Attribute(W + "anchor")?.Value;
                string target = href ?? (anchor is not null ? "#" + anchor : null) ?? "";
                if (target.Length > 0) sb.Append($"<a href=\"{WebUtility.HtmlEncode(target)}\"{(href is not null ? " target=\"_blank\" rel=\"noreferrer\"" : "")}>");
                foreach (XElement child in node.Elements(W + "r")) RenderRun(child, sb, ctx, inherited);
                if (target.Length > 0) sb.Append("</a>");
            }
            else if (node.Name == W + "smartTag" || node.Name == W + "ins" || node.Name == W + "fldSimple")
                RenderInline(node, sb, ctx, inherited); // transparent wrappers (tracked inserts, simple fields)
            else if (node.Name == W + "bookmarkStart" && node.Attribute(W + "name")?.Value is { Length: > 0 } bm && !bm.StartsWith("_GoBack", StringComparison.Ordinal))
                sb.Append("<a id=\"").Append(WebUtility.HtmlEncode(bm)).Append("\"></a>"); // hyperlink @anchor targets
        }
    }

    private static void RenderRun(XElement r, StringBuilder sb, Ctx ctx, RunProps inherited)
    {
        XElement? rPr = r.Element(W + "rPr");
        string? rStyleId = rPr?.Element(W + "rStyle")?.Attribute(W + "val")?.Value;
        RunProps rp = ctx.Styles.ResolveRun(rStyleId, rPr, inherited);
        if (rp.Vanish) return; // hidden text

        // note references — our own wiki sup-link (independent of run formatting)
        foreach (XElement c in r.Elements())
        {
            if (c.Name == W + "footnoteReference") AppendRefLink(sb, ctx.Number('f', c.Attribute(W + "id")?.Value ?? ""));
            else if (c.Name == W + "endnoteReference") AppendRefLink(sb, ctx.Number('e', c.Attribute(W + "id")?.Value ?? ""));
        }

        // text / drawings / breaks
        var body = new StringBuilder();
        foreach (XElement c in r.Elements())
        {
            if (c.Name == W + "t") body.Append(EncodeText(c.Value, c));
            else if (c.Name == W + "br")
            {
                string? brType = c.Attribute(W + "type")?.Value;
                body.Append(brType == "page" ? "<hr class=\"pagebreak\">" : "<br>");
            }
            else if (c.Name == W + "cr") body.Append("<br>");
            else if (c.Name == W + "tab") body.Append("<span class=\"tab\"></span>");
            else if (c.Name == W + "noBreakHyphen") body.Append("&#8209;");
            else if (c.Name == W + "sym") body.Append(RenderSym(c));
            else if (c.Name == W + "drawing" || c.Name == W + "pict") { string img = RenderImage(c, ctx); if (img.Length > 0) body.Append(img); }
        }
        if (body.Length == 0) return;

        string cls = ctx.Classes.ClassFor(rp.CssDelta(inherited));
        if (cls.Length > 0) sb.Append("<span class=\"").Append(cls).Append("\">").Append(body).Append("</span>");
        else sb.Append(body);
    }

    // Whitespace fidelity comes from `span{white-space:pre-wrap}` in the page CSS (the
    // PowerTools trick) — no &nbsp; hacks needed; consecutive spaces render as authored.
    private static string EncodeText(string s, XElement t) => WebUtility.HtmlEncode(s);

    private static void AppendRefLink(StringBuilder sb, int num) =>
        sb.Append($"<sup class=\"fn-ref\" id=\"fnref-{num}\"><a href=\"#fn-{num}\">[{num}]</a></sup>");

    // ── CSS builders ─────────────────────────────────────────────────────────────────
    private static string ParagraphCss(ParaProps pp)
    {
        var css = new StringBuilder();
        if (pp.Align is not null) css.Append("text-align:").Append(pp.Align).Append(';');
        if (pp.Rtl == true) css.Append("direction:rtl;");
        else if (pp.Rtl == false) css.Append("direction:ltr;");
        if (pp.IndLeftTwips is int l && l != 0) css.Append("padding-inline-start:").Append(TwipsToPt(l)).Append("pt;");
        if (pp.IndRightTwips is int rgt && rgt != 0) css.Append("padding-inline-end:").Append(TwipsToPt(rgt)).Append("pt;");
        if (pp.FirstLineTwips is int fl && fl != 0) css.Append("text-indent:").Append(TwipsToPt(fl)).Append("pt;");
        if (pp.HangingTwips is int hg && hg != 0) css.Append("text-indent:-").Append(TwipsToPt(hg)).Append("pt;");
        if (pp.SpaceBeforeTwips is int sb && sb >= 0) css.Append("margin-top:").Append(TwipsToPt(sb)).Append("pt;");
        if (pp.SpaceAfterTwips is int sa && sa >= 0) css.Append("margin-bottom:").Append(TwipsToPt(sa)).Append("pt;");
        if (pp.LineHeight is not null) css.Append("line-height:").Append(pp.LineHeight).Append(';');
        if (pp.ShdFill is not null) css.Append("background-color:#").Append(pp.ShdFill).Append(';');
        if (pp.Border) css.Append("border:1px solid #888;padding:.2rem .5rem;");
        return css.ToString();
    }

    private static string TwipsToPt(int twips) => (twips / 20.0).ToString("0.#", CultureInfo.InvariantCulture);

    // ── tables ─────────────────────────────────────────────────────────────────────────
    private static void RenderTable(XElement tbl, StringBuilder sb, Ctx ctx)
    {
        XElement? tblPr = tbl.Element(W + "tblPr");
        XElement? tblBorders = tblPr?.Element(W + "tblBorders");
        bool bidi = IsOn(tblPr?.Element(W + "bidiVisual"));

        // A table is borderless when its tblBorders explicitly set every side to none/nil
        // (very common for layout tables in Torah documents) or when a borderless table
        // style is implied by "none" on all present sides. Default (no tblBorders) keeps
        // the readable 1px grid.
        bool tableSaysNone = tblBorders is not null &&
            tblBorders.Elements().Any() &&
            tblBorders.Elements().All(e => e.Attribute(W + "val")?.Value is "none" or "nil");

        var tblCss = new StringBuilder("border-collapse:collapse;");
        if (bidi) tblCss.Append("direction:rtl;");
        string? tblW = tblPr?.Element(W + "tblW")?.Attribute(W + "w")?.Value;
        string? tblWType = tblPr?.Element(W + "tblW")?.Attribute(W + "type")?.Value;
        if (tblWType == "pct" && int.TryParse(tblW, out int pctW) && pctW > 0)
            tblCss.Append("width:").Append((pctW / 50.0).ToString("0.#", CultureInfo.InvariantCulture)).Append("%;");

        sb.Append("<table class=\"doc-table ").Append(ctx.Classes.ClassFor(tblCss.ToString())).Append('"');
        if (bidi) sb.Append(" dir=\"rtl\"");
        sb.Append('>');

        foreach (XElement tr in tbl.Elements(W + "tr"))
        {
            sb.Append("<tr>");
            foreach (XElement tc in tr.Elements(W + "tc"))
            {
                XElement? tcPr = tc.Element(W + "tcPr");
                // vertical merge continuation cells are absorbed by the cell above → skip them
                XElement? vMerge = tcPr?.Element(W + "vMerge");
                if (vMerge is not null && vMerge.Attribute(W + "val")?.Value is null or "continue") continue;

                int colspan = ParseInt(tcPr?.Element(W + "gridSpan")?.Attribute(W + "val")?.Value, 1);
                int rowspan = vMerge is not null ? CountVMergeSpan(tr, tc) : 1;

                // border: direct tcBorders (any side none → drop that side; keep it simple by
                // treating all-none as borderless) → else table-level default.
                XElement? tcBorders = tcPr?.Element(W + "tcBorders");
                bool cellSaysNone = tcBorders is not null && tcBorders.Elements().Any() &&
                    tcBorders.Elements().All(e => e.Attribute(W + "val")?.Value is "none" or "nil");
                bool borderless = cellSaysNone || (tcBorders is null && tableSaysNone);

                // Word's default cell margins ≈ 5.4pt left/right even when borderless.
                var cellCss = new StringBuilder(borderless
                    ? "padding:1pt 5.4pt;vertical-align:top;"
                    : "border:1px solid #999;padding:1pt 5.4pt;vertical-align:top;");
                if (tcPr?.Element(W + "vAlign")?.Attribute(W + "val")?.Value is { } va)
                    cellCss.Append("vertical-align:").Append(va == "center" ? "middle" : va == "bottom" ? "bottom" : "top").Append(';');
                XElement? shd = tcPr?.Element(W + "shd");
                string? fill = shd?.Attribute(W + "fill")?.Value;
                if (fill is not null && fill != "auto" && IsHex(fill)) cellCss.Append("background-color:#").Append(fill).Append(';');
                // width: dxa → pt, pct (50ths of a percent) → %
                XElement? tcW = tcPr?.Element(W + "tcW");
                if (tcW?.Attribute(W + "type")?.Value is { } wt && int.TryParse(tcW.Attribute(W + "w")?.Value, out int wv) && wv > 0)
                {
                    if (wt == "dxa") cellCss.Append("width:").Append(TwipsToPt(wv)).Append("pt;");
                    else if (wt == "pct") cellCss.Append("width:").Append((wv / 50.0).ToString("0.#", CultureInfo.InvariantCulture)).Append("%;");
                }

                sb.Append("<td");
                if (colspan > 1) sb.Append(" colspan=\"").Append(colspan).Append('"');
                if (rowspan > 1) sb.Append(" rowspan=\"").Append(rowspan).Append('"');
                sb.Append(" class=\"").Append(ctx.Classes.ClassFor(cellCss.ToString())).Append("\">");
                RenderBlocks(tc, sb, ctx);
                sb.Append("</td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</table>");
    }

    // Count how many following rows continue this column's vertical merge.
    private static int CountVMergeSpan(XElement startRow, XElement startCell)
    {
        int span = 1;
        // column index of startCell (accounting for gridSpan of preceding cells)
        int col = 0;
        foreach (XElement c in startRow.Elements(W + "tc"))
        {
            if (c == startCell) break;
            col += ParseInt(c.Element(W + "tcPr")?.Element(W + "gridSpan")?.Attribute(W + "val")?.Value, 1);
        }
        XElement? row = startRow.ElementsAfterSelf(W + "tr").FirstOrDefault();
        while (row is not null)
        {
            XElement? cell = CellAtColumn(row, col);
            XElement? vm = cell?.Element(W + "tcPr")?.Element(W + "vMerge");
            if (vm is not null && vm.Attribute(W + "val")?.Value is null or "continue") { span++; row = row.ElementsAfterSelf(W + "tr").FirstOrDefault(); }
            else break;
            if (span > 500) break; // safety
        }
        return span;
    }

    private static XElement? CellAtColumn(XElement row, int col)
    {
        int c = 0;
        foreach (XElement tc in row.Elements(W + "tc"))
        {
            if (c == col) return tc;
            c += ParseInt(tc.Element(W + "tcPr")?.Element(W + "gridSpan")?.Attribute(W + "val")?.Value, 1);
        }
        return null;
    }

    // ── images ───────────────────────────────────────────────────────────────────────
    private static string RenderImage(XElement drawingOrPict, Ctx ctx)
    {
        try
        {
            // DrawingML: a:blip @r:embed. VML fallback (w:pict): v:imagedata @r:id.
            string? embed = drawingOrPict.Descendants(A + "blip").Attributes(R + "embed").FirstOrDefault()?.Value
                ?? drawingOrPict.Descendants().FirstOrDefault(e => e.Name.LocalName == "imagedata")?.Attribute(R + "id")?.Value;
            if (embed is null || !ctx.Rels.TryGetValue("media:" + embed, out string? part)) return "";

            ZipArchiveEntry? entry = ctx.Zip.GetEntry(part);
            if (entry is null) return "";
            using Stream s = entry.Open();
            using var mem = new MemoryStream();
            s.CopyTo(mem);
            if (mem.Length == 0 || mem.Length > 12_000_000) return ""; // skip absurd images
            string mime = MimeForExt(Path.GetExtension(part));
            string b64 = Convert.ToBase64String(mem.ToArray());

            // size from wp:extent (EMU) if present
            string sizeCss = "";
            XElement? extent = drawingOrPict.Descendants().FirstOrDefault(e => e.Name.LocalName == "extent");
            if (extent is not null)
            {
                long cx = ParseLong(extent.Attribute("cx")?.Value);
                if (cx > 0) sizeCss = $" style=\"max-width:100%;width:{cx / 9525}px;height:auto\"";
            }
            if (sizeCss.Length == 0) sizeCss = " style=\"max-width:100%;height:auto\"";
            return $"<img alt=\"\"{sizeCss} src=\"data:{mime};base64,{b64}\">";
        }
        catch { return ""; }
    }

    private static string MimeForExt(string ext) => ext.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".tif" or ".tiff" => "image/tiff",
        ".svg" => "image/svg+xml",
        ".emf" or ".wmf" => "image/x-emf", // browsers can't render EMF/WMF; harmless <img> that won't show
        _ => "application/octet-stream",
    };

    private static string RenderSym(XElement sym)
    {
        string? ch = sym.Attribute(W + "char")?.Value;
        if (ch is null || !int.TryParse(ch, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code)) return "";
        // Symbol/Wingdings live in the F0xx PUA; map the common bullet, else emit a bullet.
        if (code is >= 0xF000 and <= 0xF0FF) return "&#8226;";
        try { return WebUtility.HtmlEncode(char.ConvertFromUtf32(code)); } catch { return ""; }
    }

    private static void RenderNoteBody(XElement note, StringBuilder sb, Ctx ctx)
    {
        bool first = true;
        foreach (XElement p in note.Elements(W + "p"))
        {
            if (!first) sb.Append("<br>");
            first = false;
            foreach (XElement node in p.Elements())
            {
                // drop the note's own auto-number mark (we render our own backlink)
                if (node.Name == W + "r" && (node.Element(W + "footnoteRef") is not null || node.Element(W + "endnoteRef") is not null)) continue;
                if (node.Name == W + "r") RenderRun(node, sb, ctx, RunProps.Empty);
                else if (node.Name == W + "hyperlink")
                    foreach (XElement child in node.Elements(W + "r")) RenderRun(child, sb, ctx, RunProps.Empty);
            }
        }
    }

    // ── parts / loaders ─────────────────────────────────────────────────────────────────
    private static XDocument? LoadPart(ZipArchive zip, string name)
    {
        try
        {
            ZipArchiveEntry? e = zip.GetEntry(name);
            if (e is null) return null;
            using Stream s = e.Open();
            return XDocument.Load(s);
        }
        catch { return null; }
    }

    private static Dictionary<string, XElement> LoadNotes(ZipArchive zip, string part, XName elem)
    {
        var map = new Dictionary<string, XElement>(StringComparer.Ordinal);
        XDocument? d = LoadPart(zip, part);
        if (d?.Root is null) return map;
        foreach (XElement n in d.Root.Elements(elem))
        {
            string? type = n.Attribute(W + "type")?.Value;
            if (type is "separator" or "continuationSeparator") continue;
            string id = n.Attribute(W + "id")?.Value ?? "";
            if (id.Length > 0) map[id] = n;
        }
        return map;
    }

    // Rels: hyperlink targets (External) keyed by rId, and image parts keyed "media:rId".
    private static Dictionary<string, string> LoadRels(ZipArchive zip)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        XDocument? d = LoadPart(zip, "word/_rels/document.xml.rels");
        if (d?.Root is null) return map;
        foreach (XElement rel in d.Root.Elements())
        {
            string id = rel.Attribute("Id")?.Value ?? "";
            string target = rel.Attribute("Target")?.Value ?? "";
            string type = rel.Attribute("Type")?.Value ?? "";
            if (id.Length == 0) continue;
            bool external = string.Equals(rel.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase);
            if (external) map[id] = target;                                 // hyperlink
            else if (type.EndsWith("/image", StringComparison.Ordinal))
                map["media:" + id] = "word/" + target.Replace("../", "");   // image part path in the zip
        }
        return map;
    }

    private static string HeadingTag(string? styleId, Ctx ctx)
    {
        if (styleId is null || styleId.Length == 0) return "p"; // (net48 IsNullOrEmpty lacks NotNullWhen)
        // Primary: the resolved style's outlineLvl (0-5 → h1-h6) — locale-proof, covers
        // custom heading styles (PowerTools' approach). Name matching is the fallback.
        if (ctx.Styles.OutlineLvlOf(styleId) is int lvl && lvl <= 5) return "h" + (lvl + 1);
        string s = styleId.ToLowerInvariant();
        string name = (ctx.Styles.NameOf(styleId) ?? "").ToLowerInvariant();
        if (s == "title" || name == "title") return "h1";
        foreach (string probe in new[] { s, name })
            if (probe.Contains("heading") || probe.Contains("כותרת"))
            {
                char d = probe.LastOrDefault(char.IsDigit);
                int level = d is >= '1' and <= '6' ? d - '0' : 2;
                return "h" + level;
            }
        return "p";
    }

    // ── small helpers ────────────────────────────────────────────────────────────────
    private static int ParseInt(string? s, int def) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : def;
    private static long ParseLong(string? s) => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : 0;
    private static bool IsHex(string s) { foreach (char c in s) if (!Uri.IsHexDigit(c)) return false; return s.Length is 6 or 3; }

    private sealed class Ctx
    {
        public StyleResolver Styles = null!;
        public NumberingResolver Numbering = null!;
        public Dictionary<string, XElement> Footnotes = null!;
        public Dictionary<string, XElement> Endnotes = null!;
        public Dictionary<string, string> Rels = null!;
        public ZipArchive Zip = null!;
        public readonly CssClasses Classes = new();
        public readonly List<(char kind, string id, int num)> Ordered = [];
        private readonly Dictionary<string, int> _seen = new(StringComparer.Ordinal);
        public int Number(char kind, string id)
        {
            string key = kind + id;
            if (_seen.TryGetValue(key, out int n)) return n;
            n = Ordered.Count + 1; _seen[key] = n; Ordered.Add((kind, id, n));
            return n;
        }
    }

    // Deduplicates resolved element formatting into reusable CSS classes (like Word's own style
    // model): identical formatting → one class, referenced by many elements. Empty formatting →
    // no class. This is what keeps large documents compact.
    private sealed class CssClasses
    {
        private readonly Dictionary<string, string> _map = new(StringComparer.Ordinal);
        public string ClassFor(string css)
        {
            if (css.Length == 0) return "";
            if (_map.TryGetValue(css, out string? c)) return c;
            c = "c" + (_map.Count + 1);
            _map[css] = c;
            return c;
        }
        public string Emit()
        {
            var sb = new StringBuilder();
            foreach (var kv in _map) sb.Append('.').Append(kv.Value).Append('{').Append(kv.Key).Append('}');
            return sb.ToString();
        }
    }

    private static bool IsOn(XElement? e)
    {
        if (e is null) return false;
        string? v = e.Attribute(W + "val")?.Value;
        return v is null || !(v is "0" or "false" or "off");
    }

    private static int? TwipsAttr(XElement e, string name)
        => int.TryParse(e.Attribute(W + name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : null;

    // Resolved run formatting (nullable bool = "inherit / not set").
    private sealed class RunProps
    {
        public bool? Bold, Italic;
        public bool Underline, Strike, Dstrike, Super, Sub, Caps, SmallCaps, Vanish, Bdr, Bidi;
        public string? Color, Font, Highlight, ShdFill;
        public int HalfPt, HalfPtCs;
        public static RunProps Empty => new();
        public RunProps Clone() => (RunProps)MemberwiseClone();

        // For bidi (Hebrew/CS) runs Word uses szCs, not sz — the single most important
        // fidelity rule for a Hebrew app (learned from PowerTools' FormattingAssembler).
        public int EffectiveHalfPt => Bidi && HalfPtCs > 0 ? HalfPtCs : (HalfPt > 0 ? HalfPt : HalfPtCs);

        /// <summary>Merge an rPr layer. <paramref name="styleLayer"/> enables Word's TOGGLE
        /// semantics for b/i/caps/smallCaps/strike: two style layers both setting a toggle ON
        /// cancel out (XOR). Direct formatting is exempt — it overrides absolutely.</summary>
        public void Apply(XElement? rPr, bool styleLayer = false)
        {
            if (rPr is null) return;
            void Toggle(XName n, Func<bool?> get, Action<bool?> set)
            {
                if (rPr.Element(n) is not { } e) return;
                bool v = IsOn(e);
                set(styleLayer && v && get() == true ? false : v);
            }
            Toggle(W + "b", () => Bold, v => Bold = v);
            Toggle(W + "i", () => Italic, v => Italic = v);
            if (rPr.Element(W + "u") is { } u) Underline = !string.Equals(u.Attribute(W + "val")?.Value, "none", StringComparison.OrdinalIgnoreCase);
            if (rPr.Element(W + "strike") is { } st) Strike = IsOn(st) && !(styleLayer && Strike);
            if (rPr.Element(W + "dstrike") is { } dst) Dstrike = IsOn(dst);
            if (rPr.Element(W + "vertAlign")?.Attribute(W + "val")?.Value is { } va) { Super = va == "superscript"; Sub = va == "subscript"; }
            if (rPr.Element(W + "color")?.Attribute(W + "val")?.Value is { } col && col != "auto" && IsHex(col)) Color = col;
            if (rPr.Element(W + "sz")?.Attribute(W + "val")?.Value is { } sz && int.TryParse(sz, out int v) && v is > 0 and < 400) HalfPt = v;
            if (rPr.Element(W + "szCs")?.Attribute(W + "val")?.Value is { } szCs && int.TryParse(szCs, out int vc) && vc is > 0 and < 400) HalfPtCs = vc;
            if (rPr.Element(W + "rFonts") is { } rf) { string? f = rf.Attribute(W + "cs")?.Value ?? rf.Attribute(W + "ascii")?.Value ?? rf.Attribute(W + "hAnsi")?.Value; if (!string.IsNullOrEmpty(f)) Font = f; }
            if (rPr.Element(W + "highlight")?.Attribute(W + "val")?.Value is { } hl && hl != "none") Highlight = MapHighlight(hl);
            if (rPr.Element(W + "shd")?.Attribute(W + "fill")?.Value is { } fill && fill != "auto" && IsHex(fill)) ShdFill = fill;
            if (rPr.Element(W + "caps") is { } cp) Caps = IsOn(cp) && !(styleLayer && Caps);
            if (rPr.Element(W + "smallCaps") is { } sc) SmallCaps = IsOn(sc) && !(styleLayer && SmallCaps);
            if (rPr.Element(W + "vanish") is { } vn) Vanish = IsOn(vn);
            if (rPr.Element(W + "bdr") is { } bd) Bdr = !string.Equals(bd.Attribute(W + "val")?.Value, "none", StringComparison.OrdinalIgnoreCase);
            // Run is CS/bidi when w:rtl or w:cs is present (MS-OI29500 §2.1.87 short form).
            if (rPr.Element(W + "rtl") is { } rt) Bidi = IsOn(rt);
            else if (rPr.Element(W + "cs") is not null) Bidi = true;
        }
        private static string MapHighlight(string w) => w.ToLowerInvariant() switch
        {
            "darkyellow" => "olive", "darkgray" => "darkgray", "lightgray" => "lightgray", _ => w.ToLowerInvariant(),
        };

        // Base font-family + size only (goes on <body> once so runs don't repeat it).
        public string BaseFontCss()
        {
            var css = new StringBuilder();
            if (Font is not null) css.Append("font-family:'").Append(Font.Replace("'", "")).Append("',serif;");
            if (EffectiveHalfPt > 0) css.Append("font-size:").Append((EffectiveHalfPt / 2.0).ToString("0.#", CultureInfo.InvariantCulture)).Append("pt;");
            return css.ToString();
        }

        // Emit ONLY the properties that differ from the inherited base — this is what keeps the
        // output small (a run/paragraph matching its context emits nothing).
        public string CssDelta(RunProps b)
        {
            var css = new StringBuilder();
            if ((Bold ?? false) != (b.Bold ?? false)) css.Append((Bold ?? false) ? "font-weight:bold;" : "font-weight:normal;");
            if ((Italic ?? false) != (b.Italic ?? false)) css.Append((Italic ?? false) ? "font-style:italic;" : "font-style:normal;");
            bool lineThrough = Strike || Dstrike, bLineThrough = b.Strike || b.Dstrike;
            if (Underline != b.Underline || lineThrough != bLineThrough)
            {
                if (!Underline && !lineThrough) css.Append("text-decoration:none;");
                else { css.Append("text-decoration:"); if (Underline) css.Append("underline "); if (lineThrough) css.Append("line-through"); css.Append(';'); }
            }
            if (Super != b.Super || Sub != b.Sub)
                css.Append(Super ? "vertical-align:super;font-size:.83em;" : Sub ? "vertical-align:sub;font-size:.83em;" : "vertical-align:baseline;");
            if (Color != b.Color && Color is not null) css.Append("color:#").Append(Color).Append(';');
            if (EffectiveHalfPt != b.EffectiveHalfPt && EffectiveHalfPt > 0)
                css.Append("font-size:").Append((EffectiveHalfPt / 2.0).ToString("0.#", CultureInfo.InvariantCulture)).Append("pt;");
            if (Font != b.Font && Font is not null) css.Append("font-family:'").Append(Font.Replace("'", "")).Append("',serif;");
            string? bg = Highlight ?? (ShdFill is not null ? "#" + ShdFill : null);
            string? bbg = b.Highlight ?? (b.ShdFill is not null ? "#" + b.ShdFill : null);
            if (bg != bbg && bg is not null) css.Append("background-color:").Append(bg).Append(';');
            if (Caps != b.Caps) css.Append(Caps ? "text-transform:uppercase;" : "text-transform:none;");
            if (SmallCaps != b.SmallCaps) css.Append(SmallCaps ? "font-variant:small-caps;" : "font-variant:normal;");
            if (Bdr && !b.Bdr) css.Append("border:1pt solid currentColor;padding:0 2px;");
            return css.ToString();
        }
    }

    private sealed class ParaProps
    {
        public string? Align, ShdFill, LineHeight;
        public bool? Rtl;
        public bool Border;
        public int? IndLeftTwips, IndRightTwips, FirstLineTwips, HangingTwips, SpaceBeforeTwips, SpaceAfterTwips;
        public RunProps RunProps = new();
        public void Apply(XElement? pPr, bool styleLayer = false)
        {
            if (pPr is null) return;
            if (pPr.Element(W + "jc")?.Attribute(W + "val")?.Value is { } jc)
                Align = jc switch { "center" => "center", "left" or "start" => "left", "right" or "end" => "right", "both" or "distribute" => "justify", _ => Align };
            if (pPr.Element(W + "bidi") is { } bd) Rtl = IsOn(bd);
            if (pPr.Element(W + "ind") is { } ind)
            {
                if ((TwipsAttr(ind, "start") ?? TwipsAttr(ind, "left")) is int l) IndLeftTwips = l;
                if ((TwipsAttr(ind, "end") ?? TwipsAttr(ind, "right")) is int rr) IndRightTwips = rr;
                if (TwipsAttr(ind, "firstLine") is int fl) { FirstLineTwips = fl; HangingTwips = null; }
                if ((TwipsAttr(ind, "hanging") ?? TwipsAttr(ind, "startChars")) is int hg && ind.Attribute(W + "hanging") is not null) { HangingTwips = hg; FirstLineTwips = null; }
            }
            if (pPr.Element(W + "spacing") is { } sp)
            {
                if (TwipsAttr(sp, "before") is int bef) SpaceBeforeTwips = bef;
                if (TwipsAttr(sp, "after") is int aft) SpaceAfterTwips = aft;
                // line spacing (PowerTools rule): auto → %, exact → pt, atLeast → pt when ≥14pt
                if (TwipsAttr(sp, "line") is int line && line > 0)
                {
                    string rule = sp.Attribute(W + "lineRule")?.Value ?? "auto";
                    if (rule == "auto") LineHeight = (line / 240.0 * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
                    else if (rule == "exact" || (rule == "atLeast" && line >= 280)) LineHeight = TwipsToPt(line) + "pt";
                }
            }
            if (pPr.Element(W + "shd")?.Attribute(W + "fill")?.Value is { } fill && fill != "auto" && IsHex(fill)) ShdFill = fill;
            if (pPr.Element(W + "pBdr") is { } pbdr &&
                pbdr.Elements().Any(e => !string.Equals(e.Attribute(W + "val")?.Value, "none", StringComparison.OrdinalIgnoreCase)))
                Border = true;
            RunProps.Apply(pPr.Element(W + "rPr"), styleLayer); // paragraph-mark run props → inherited by runs
        }
    }

    // Full style cascade: docDefaults → default style → basedOn chain → style → direct.
    private sealed class StyleResolver
    {
        private readonly XElement? _ddRPr, _ddPPr;
        private readonly Dictionary<string, XElement> _styles = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
        private string? _defaultParaStyleId;

        /// <summary>The document's default run formatting (docDefaults + default paragraph style)
        /// — used as the <body> base so runs/paragraphs only emit deviations from it.</summary>
        public RunProps DocDefaultRun { get; } = new();

        public StyleResolver(XDocument? stylesDoc)
        {
            XElement? root = stylesDoc?.Root;
            if (root is null) return;
            XElement? dd = root.Element(W + "docDefaults");
            _ddRPr = dd?.Element(W + "rPrDefault")?.Element(W + "rPr");
            _ddPPr = dd?.Element(W + "pPrDefault")?.Element(W + "pPr");
            foreach (XElement st in root.Elements(W + "style"))
            {
                string? id = st.Attribute(W + "styleId")?.Value;
                if (id is null) continue;
                _styles[id] = st;
                if (st.Element(W + "name")?.Attribute(W + "val")?.Value is { } nm) _names[id] = nm;
                if (st.Attribute(W + "type")?.Value == "paragraph" && st.Attribute(W + "default")?.Value is "1" or "true")
                    _defaultParaStyleId = id;
            }
            DocDefaultRun.Apply(_ddRPr);
            if (_defaultParaStyleId is not null)
                foreach (XElement st in Chain(_defaultParaStyleId)) DocDefaultRun.Apply(st.Element(W + "rPr"));
        }

        public string? NameOf(string? id) => id is not null && _names.TryGetValue(id, out string? n) ? n : null;

        private List<XElement> Chain(string? styleId)
        {
            var stack = new List<XElement>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string? cur = styleId;
            int guard = 0;
            while (cur is not null && _styles.TryGetValue(cur, out XElement? st) && seen.Add(cur) && guard++ < 50)
            {
                stack.Add(st);
                cur = st.Element(W + "basedOn")?.Attribute(W + "val")?.Value;
            }
            stack.Reverse();
            return stack;
        }

        public ParaProps ResolveParagraph(string? styleId, XElement? directPPr)
        {
            var pp = new ParaProps();
            pp.RunProps.Apply(_ddRPr);
            pp.Apply(_ddPPr);
            if (_defaultParaStyleId is not null && _defaultParaStyleId != styleId)
                foreach (XElement st in Chain(_defaultParaStyleId)) Overlay(pp, st);
            foreach (XElement st in Chain(styleId)) Overlay(pp, st);
            pp.Apply(directPPr); // direct formatting: absolute override (no toggle XOR)
            return pp;
        }

        private static void Overlay(ParaProps pp, XElement style)
        {
            pp.RunProps.Apply(style.Element(W + "rPr"), styleLayer: true);
            pp.Apply(style.Element(W + "pPr"), styleLayer: true);
        }

        public RunProps ResolveRun(string? rStyleId, XElement? directRPr, RunProps inherited)
        {
            RunProps rp = inherited.Clone();
            foreach (XElement st in Chain(rStyleId)) rp.Apply(st.Element(W + "rPr"), styleLayer: true);
            rp.Apply(directRPr); // direct formatting: absolute override (no toggle XOR)
            return rp;
        }

        /// <summary>The resolved style's w:outlineLvl (0-8), or null. Locale-proof heading
        /// detection — subsumes name matching for custom heading styles.</summary>
        public int? OutlineLvlOf(string? styleId)
        {
            int? lvl = null;
            foreach (XElement st in Chain(styleId))
                if (st.Element(W + "pPr")?.Element(W + "outlineLvl")?.Attribute(W + "val")?.Value is { } v &&
                    int.TryParse(v, out int n) && n is >= 0 and <= 8)
                    lvl = n;
            return lvl;
        }
    }

    // numbering.xml → sequential list labels (decimal/letter/roman/hebrew/bullet).
    private sealed class NumberingResolver
    {
        private readonly Dictionary<string, string> _numToAbstract = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<int, (string fmt, string text, int start)>> _abstract = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _counters = new(StringComparer.Ordinal);
        // num/lvlOverride/startOverride: numId|ilvl → forced start value (consumed on first use).
        private readonly Dictionary<string, int> _startOverrides = new(StringComparer.Ordinal);

        public NumberingResolver(XDocument? numDoc, XDocument? stylesDoc = null)
        {
            XElement? root = numDoc?.Root;
            if (root is null) return;
            var styleLink = new Dictionary<string, string>(StringComparer.Ordinal); // abstractId → linked styleId
            foreach (XElement an in root.Elements(W + "abstractNum"))
            {
                string? aid = an.Attribute(W + "abstractNumId")?.Value;
                if (aid is null) continue;
                if (an.Element(W + "numStyleLink")?.Attribute(W + "val")?.Value is { } lk) styleLink[aid] = lk;
                var levels = new Dictionary<int, (string, string, int)>();
                foreach (XElement lvl in an.Elements(W + "lvl"))
                {
                    int ilvl = ParseInt(lvl.Attribute(W + "ilvl")?.Value, 0);
                    levels[ilvl] = (
                        lvl.Element(W + "numFmt")?.Attribute(W + "val")?.Value ?? "decimal",
                        lvl.Element(W + "lvlText")?.Attribute(W + "val")?.Value ?? "%1.",
                        ParseInt(lvl.Element(W + "start")?.Attribute(W + "val")?.Value, 1));
                }
                _abstract[aid] = levels;
            }
            foreach (XElement num in root.Elements(W + "num"))
            {
                if (num.Attribute(W + "numId")?.Value is not { } nid ||
                    num.Element(W + "abstractNumId")?.Attribute(W + "val")?.Value is not { } aid) continue;
                _numToAbstract[nid] = aid;
                foreach (XElement ov in num.Elements(W + "lvlOverride"))
                    if (ov.Element(W + "startOverride")?.Attribute(W + "val")?.Value is { } so && int.TryParse(so, out int sov))
                        _startOverrides[nid + "|" + ParseInt(ov.Attribute(W + "ilvl")?.Value, 0)] = sov;
            }

            // numStyleLink double-indirection: the abstract delegates to a numbering STYLE whose
            // pPr/numPr/numId names the real num → real abstract. Rewire _numToAbstract through it.
            if (styleLink.Count > 0 && stylesDoc?.Root is { } stylesRoot)
            {
                var styleNumId = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (XElement st in stylesRoot.Elements(W + "style"))
                    if (st.Attribute(W + "styleId")?.Value is { } sid &&
                        st.Element(W + "pPr")?.Element(W + "numPr")?.Element(W + "numId")?.Attribute(W + "val")?.Value is { } snid)
                        styleNumId[sid] = snid;
                foreach (var kv in _numToAbstract.ToList()) // KVP deconstruct is unavailable on net48
                    if (styleLink.TryGetValue(kv.Value, out string? sid) && styleNumId.TryGetValue(sid, out string? realNid) &&
                        realNid != kv.Key && _numToAbstract.TryGetValue(realNid, out string? realAid))
                        _numToAbstract[kv.Key] = realAid;
            }
        }

        public string NextLabel(string numId, int ilvl)
        {
            try
            {
                if (!_numToAbstract.TryGetValue(numId, out string? aid) || !_abstract.TryGetValue(aid, out var levels) || !levels.TryGetValue(ilvl, out var def))
                    return "•";
                string key = numId + "|" + ilvl;
                int startAt = _startOverrides.TryGetValue(key, out int so) ? so : def.start;
                _counters[key] = _counters.TryGetValue(key, out int c) ? c + 1 : startAt;
                foreach (string k in _counters.Keys.ToList())
                    if (k.StartsWith(numId + "|", StringComparison.Ordinal) && ParseInt(k.Substring(numId.Length + 1), 0) > ilvl) _counters.Remove(k);
                if (def.fmt == "bullet") return "•";
                string label = def.text;
                for (int lv = 0; lv <= ilvl; lv++)
                {
                    int val = _counters.TryGetValue(numId + "|" + lv, out int cv) ? cv : (levels.TryGetValue(lv, out var d2) ? d2.start : 1);
                    string fmt = levels.TryGetValue(lv, out var d3) ? d3.fmt : "decimal";
                    label = label.Replace("%" + (lv + 1), FormatNum(val, fmt));
                }
                return label;
            }
            catch { return "•"; }
        }

        private static string FormatNum(int n, string fmt) => fmt switch
        {
            "lowerLetter" => Alpha(n, false),
            "upperLetter" => Alpha(n, true),
            "lowerRoman" => Roman(n).ToLowerInvariant(),
            "upperRoman" => Roman(n),
            "hebrew1" or "hebrew2" => Hebrew(n),
            _ => n.ToString(CultureInfo.InvariantCulture),
        };

        private static string Alpha(int n, bool upper)
        {
            if (n <= 0) return "";
            var sb = new StringBuilder();
            while (n > 0) { n--; sb.Insert(0, (char)((upper ? 'A' : 'a') + n % 26)); n /= 26; }
            return sb.ToString();
        }

        private static string Roman(int n)
        {
            if (n is <= 0 or > 3999) return n.ToString(CultureInfo.InvariantCulture);
            int[] v = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
            string[] s = ["M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"];
            var sb = new StringBuilder();
            for (int i = 0; i < v.Length; i++) while (n >= v[i]) { sb.Append(s[i]); n -= v[i]; }
            return sb.ToString();
        }

        private static string Hebrew(int n)
        {
            if (n is <= 0 or > 999) return n.ToString(CultureInfo.InvariantCulture);
            string[] ones = ["", "א", "ב", "ג", "ד", "ה", "ו", "ז", "ח", "ט"];
            string[] tens = ["", "י", "כ", "ל", "מ", "נ", "ס", "ע", "פ", "צ"];
            string[] huns = ["", "ק", "ר", "ש", "ת", "תק", "תר", "תש", "תת", "תתק"];
            var sb = new StringBuilder();
            sb.Append(huns[n / 100]); n %= 100;
            if (n == 15) sb.Append("טו");
            else if (n == 16) sb.Append("טז");
            else { sb.Append(tens[n / 10]); sb.Append(ones[n % 10]); }
            return sb.ToString();
        }
    }

    // ── page shell (wiki-like, RTL, Hebrew-friendly) ───────────────────────────────────
    private static string Page(string title, string body, string baseFontCss, string classCss) =>
        "<!doctype html><html lang=\"he\" dir=\"rtl\"><head><meta charset=\"utf-8\">" +
        "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">" +
        $"<title>{title}</title><style>" +
        (baseFontCss.Length > 0 ? "body{" + baseFontCss + "}" : "") +
        classCss +
        ":root{color-scheme:light dark}" +
        "body{margin:0;padding:2rem 1rem;font-family:'David','Frank Ruehl','Times New Roman',serif;line-height:1.7;background:#fff;color:#111}" +
        "@media(prefers-color-scheme:dark){body{background:#1b1b1b;color:#e8e8e8}a{color:#6cb4ff}}" +
        "main,.footnotes{max-width:46rem;margin:0 auto}" +
        "h1{font-size:1.9rem}h2{font-size:1.5rem}h3{font-size:1.25rem}h4,h5,h6{font-size:1.1rem}" +
        "p{margin:.5rem 0}p.empty{margin:.4rem 0;min-height:1em}" +
        "main span{white-space:pre-wrap}" +
        "a{color:#0645ad;text-decoration:none}a:hover{text-decoration:underline}" +
        ".lbl{font-weight:bold}.tab{display:inline-block;width:2em}" +
        "sup.fn-ref{font-size:.8em;line-height:0;vertical-align:super}.fn-ref a{padding:0 1px}" +
        "hr.pagebreak{border:0;border-top:1px dashed #bbb;margin:1.5rem 0}" +
        ".notes-sep{margin-top:2rem;border:0;border-top:1px solid #ccc}" +
        ".footnotes{font-size:.9rem;color:#333}@media(prefers-color-scheme:dark){.footnotes{color:#bbb}}" +
        ".footnotes ol{padding-inline-start:1.6rem}.footnotes li{margin:.35rem 0}.fn-back{font-weight:bold;margin-inline-end:.3rem}" +
        ".doc-table{margin:1rem 0;max-width:100%}img{max-width:100%}" +
        "</style></head><body><main dir=\"rtl\">" + body + "</main></body></html>";
}
