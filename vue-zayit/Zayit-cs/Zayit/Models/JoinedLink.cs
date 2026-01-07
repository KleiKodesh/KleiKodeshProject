using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Zayit.Models
{
    /// <summary>
    /// Represents a link with joined data from book and line tables
    /// </summary>
    public class JoinedLink
    {
        public int TargetLineId { get; set; }
        public int TargetBookId { get; set; }
        public int ConnectionTypeId { get; set; }
        public string Title { get; set; }  // Book title
        public string Content { get; set; }  // Line content (HTML)
    }
}
