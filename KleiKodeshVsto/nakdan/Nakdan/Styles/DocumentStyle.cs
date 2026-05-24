namespace Nakdan.Styles
{
    /// <summary>
    /// Represents a style from the document with both ID and display name.
    /// </summary>
    public class DocumentStyle
    {
        public string Id { get; set; }
        public string Name { get; set; }

        public DocumentStyle(string id, string name)
        {
            Id = id;
            Name = name;
        }

        public override string ToString() => Name;
    }
}
