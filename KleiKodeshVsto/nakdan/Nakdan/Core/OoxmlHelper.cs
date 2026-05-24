using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Nakdan.Core
{
    public static class OoxmlHelper
    {
        public static readonly XNamespace W =
            "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        public static XElement GetBody(XDocument doc)
        {
            return doc?
                .Descendants(W + "body")
                .FirstOrDefault();
        }

        public static IEnumerable<XElement> GetParagraphs(
            XDocument doc)
        {
            XElement body = GetBody(doc);

            if (body == null)
                return Enumerable.Empty<XElement>();

            return body.Descendants(W + "p");
        }

        public static IEnumerable<XElement> GetRuns(
            XElement paragraph)
        {
            if (paragraph == null)
                return Enumerable.Empty<XElement>();

            return paragraph.Descendants(W + "r");
        }

        public static XElement GetTextElement(
            XElement run)
        {
            return run?.Element(W + "t");
        }

        public static string GetParagraphStyleId(
            XElement paragraph)
        {
            return paragraph
                ?.Element(W + "pPr")
                ?.Element(W + "pStyle")
                ?.Attribute(W + "val")
                ?.Value
                ?? string.Empty;
        }

        public static void PreserveSpacesIfNeeded(
            XElement textElement,
            string text)
        {
            if (textElement == null)
                return;

            if (string.IsNullOrEmpty(text))
                return;

            bool preserve =
                text[0] == ' '
                || text[text.Length - 1] == ' ';

            if (!preserve)
                return;

            textElement.SetAttributeValue(
                XNamespace.Xml + "space",
                "preserve");
        }
    }
}
