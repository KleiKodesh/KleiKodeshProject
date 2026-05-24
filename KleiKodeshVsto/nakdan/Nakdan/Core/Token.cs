namespace Nakdan.Core
{
    public class Token
    {
        public char Base { get; set; }

        public string VowelsAfter { get; set; }

        public int RunIndex { get; set; }

        public int PosInRun { get; set; }
    }
}
