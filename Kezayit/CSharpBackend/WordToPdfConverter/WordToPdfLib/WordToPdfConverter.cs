using System.IO;

namespace WordToPdfLib
{
    /// <summary>
    /// Converts .docx files to PDF with full RTL/Hebrew support.
    /// </summary>
    public class WordToPdfConverter
    {
        /// <summary>
        /// Convert a Word document to PDF.
        /// </summary>
        /// <param name="docxPath">Full path to the input .docx file.</param>
        /// <param name="pdfPath">Full path for the output .pdf file.</param>
        public void Convert(string docxPath, string pdfPath, TextWriter log = null)
        {
            var content = DocxReader.Read(docxPath, log);
            new PdfWriter(new ConversionOptions
            {
                MarginLeft        = content.PageLayout.MarginLeft,
                MarginRight       = content.PageLayout.MarginRight,
                MarginTop         = content.PageLayout.MarginTop,
                MarginBottom      = content.PageLayout.MarginBottom,
                DefaultFontName   = content.PageLayout.DefaultFontName,
                DefaultFontSize   = content.PageLayout.DefaultFontSize,
                ParagraphSpacing  = content.PageLayout.DefaultSpaceAfter,
                LineSpacing       = content.PageLayout.DefaultLineSpacing,
                DefaultRtl        = content.PageLayout.IsRtl,
            }).Write(content, pdfPath, log);
        }
    }
}
