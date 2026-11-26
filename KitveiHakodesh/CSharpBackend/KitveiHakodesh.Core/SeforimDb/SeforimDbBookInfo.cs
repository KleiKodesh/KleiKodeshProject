namespace KitveiHakodesh.Core.SeforimDb
{
    public sealed partial class SeforimDbQueries
    {
        /// <summary>One book's metadata for the book view: line count, which connections it has, its
        /// title and category path, and the publisher's copy limit and credit line (when the library
        /// has them). Null for a book the library does not have.</summary>
        public BookInfo? GetBookById(int id)
        {
            var data = ReadBookInfo(id);
            if (data == null) return null;

            var book = data.Info;
            if (data.ProtectionLevel is int level && level > 0)
                book.Protection = BookProtection.ForLevel(level);
            if (data.BannerText != null)
            {
                string banner = ExpandBannerTitle(data.BannerText, data.BannerBookTitle ?? "");
                if (banner.Trim().Length > 0) book.PublisherBanner = banner;
            }
            return book;
        }
    }
}
