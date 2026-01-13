using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Zayit.Models
{
    public class JoinedLink
    {
        public int TargetLineId { get; set; }
        public int TargetBookId { get; set; }
        public int ConnectionTypeId { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
    }

}
