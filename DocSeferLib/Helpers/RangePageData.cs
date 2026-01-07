namespace DocSeferLib.Helpers
{
    public class RangePageData
    {
        public int PageCount => LastPage - FirstPage + 1;
        public int FirstPage { get; set; }
        public int LastPage { get; set; }
    }
}
