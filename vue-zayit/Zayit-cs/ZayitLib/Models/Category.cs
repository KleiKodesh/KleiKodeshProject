using System.Collections.Generic;

namespace Zayit.Models
{
    public class Category
    {
        public int Id { get; set; }
        public int ParentId { get; set; }
        public string Title { get; set; }
        public string Path { get; set; }
        public int Level { get; set; }
        public Book[] Books { get; set; }
        public List<Category> Children { get; set; } = new List<Category>();
    }
}
