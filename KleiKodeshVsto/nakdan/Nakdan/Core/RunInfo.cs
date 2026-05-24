using System.Xml.Linq;

namespace Nakdan.Core
{
    public class RunInfo
    {
        public int Index { get; set; }

        public XElement Element { get; set; }

        public XElement TextEl { get; set; }

        public string OrigText { get; set; }

        public string StyleName { get; set; }
    }
}
