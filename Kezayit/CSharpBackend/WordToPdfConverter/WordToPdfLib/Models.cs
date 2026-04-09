using System.Collections.Generic;

namespace WordToPdfLib
{
    public enum ParagraphType { Normal, Heading1, Heading2, Heading3 }

    public class TextRun
    {
        public string Text { get; set; }
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public bool Underline { get; set; }
        public bool Strikethrough { get; set; }
        public bool Superscript { get; set; }
        public bool Subscript { get; set; }
        public string Color { get; set; }
        public string Highlight { get; set; }
        public float? FontSize { get; set; }
        public string FontName { get; set; }
    }

    public enum ParagraphAlignment { Left, Center, Right, Justify }

    public class DocParagraph
    {
        public List<TextRun> Runs { get; set; } = new List<TextRun>();
        public ParagraphType Type { get; set; } = ParagraphType.Normal;
        public bool IsRtl { get; set; }
        public bool PageBreakBefore { get; set; }
        public ParagraphAlignment? Alignment { get; set; } = null;
        public float IndentLeft { get; set; }
        public float IndentRight { get; set; }
        public float HangingIndent { get; set; }   // points — prefix column width from numbering
        public float SpaceBefore { get; set; }
        public float SpaceAfter { get; set; }
        public float? LineSpacing { get; set; }
        public string ListPrefix { get; set; }
        public bool ContextualSpacing { get; set; }  // suppress spacing between adjacent same-style paras
        public string FootnoteId { get; set; }
    }

    public class DocFootnote
    {
        public string Id { get; set; }
        public List<DocParagraph> Paragraphs { get; set; } = new List<DocParagraph>();
    }

    public class DocPageLayout
    {
        public float  MarginLeft        { get; set; } = 90f;
        public float  MarginRight       { get; set; } = 90f;
        public float  MarginTop         { get; set; } = 72f;
        public float  MarginBottom      { get; set; } = 72f;
        public bool   IsRtl             { get; set; } = false;
        public string DefaultFontName   { get; set; } = "Arial";
        public float  DefaultFontSize   { get; set; } = 12f;
        public float  DefaultSpaceAfter { get; set; } = 8f;
        public float  DefaultLineSpacing{ get; set; } = 1.15f;
    }

    public class DocContent
    {
        public List<DocParagraph> Paragraphs { get; set; } = new List<DocParagraph>();
        public Dictionary<string, DocFootnote> Footnotes { get; set; } = new Dictionary<string, DocFootnote>();
        public DocPageLayout PageLayout { get; set; } = new DocPageLayout();
    }
}
