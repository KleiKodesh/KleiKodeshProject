using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Nakdan.WdStyles
{
    /// <summary>
    /// Represents one row in the "ignored styles" checklist.
    /// </summary>
    public class StyleItem : INotifyPropertyChanged
    {
        private bool _isIgnored;

        public string Name         { get; set; }
        public string DisplayName  { get; set; }   // human-friendly Hebrew label
        public string InternalName { get; set; }   // Word style internal/English name

        public bool IsIgnored
        {
            get => _isIgnored;
            set { _isIgnored = value; OnPropertyChanged(nameof(IsIgnored)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
