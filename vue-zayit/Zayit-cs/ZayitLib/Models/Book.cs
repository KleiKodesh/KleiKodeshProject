namespace Zayit.Models
{
    public class Book
    {
        public int Id { get; set; }

        public int CategoryId { get; set; }

        public string Path { get; set; }

        public string Title { get; set; }

        public string HeShortDesc { get; set; }

        public int OrderIndex { get; set; }

        public int TotalLines { get; set; }

        public int HasTargumConnection { get; set; }

        public int HasReferenceConnection { get; set; }

        public int HasCommentaryConnection { get; set; }

        public int HasOtherConnection { get; set; }
    }

}