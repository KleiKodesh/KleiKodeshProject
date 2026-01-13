namespace Zayit.Models
{
    public class TocEntry
    {
        public int Id { get; set; }
        public int BookId { get; set; }
        public int? ParentId { get; set; }
        public int? TextId { get; set; }
        public int Level { get; set; }
        public int LineId { get; set; }
        public int LineIndex { get; set; }
        public bool IsLastChild { get; set; }
        public bool HasChildren { get; set; }

        // From TocText table
        public string Text { get; set; }

        public string Path { get; set; }

        public TocEntry[] Children { get; set; }
    }
}
