namespace WordToPdfLib
{
    public class ConversionOptions
    {
        /// <summary>Default font size in points.</summary>
        public float DefaultFontSize { get; set; } = 12f;
        public string DefaultFontName { get; set; } = "Arial";

        /// <summary>Page margins in points (72 pts = 1 inch).</summary>
        public float MarginLeft   { get; set; } = 60f;
        public float MarginRight  { get; set; } = 60f;
        public float MarginTop    { get; set; } = 60f;
        public float MarginBottom { get; set; } = 60f;

        /// <summary>Default line spacing multiplier.</summary>
        public float LineSpacing { get; set; } = 1.4f;

        /// <summary>Space between paragraphs in points.</summary>
        public float ParagraphSpacing { get; set; } = 8f;

        /// <summary>When true, paragraphs default to right-to-left layout.</summary>
        public bool DefaultRtl { get; set; } = true;
    }
}
