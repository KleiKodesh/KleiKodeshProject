using Microsoft.Office.Interop.Word;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
using DocDesign.Helpers;
using WpfLib.ViewModels;

namespace DocDesign.Paragraphs
{
    public class ParagraphsViewModel : ViewModelBase
    {
        public class ActiveStyle : ViewModelBase
        {
            bool _apply;
            public string Name { get; set; }
            public bool Apply
            {
                get => _apply;
                set => SetProperty(ref _apply, value);
            }
        }

        int _firstWordMinLineCount = 1;
        int _hangingMinLineCount = 2;
        int _centerLastLineMinLineCount = 2;
        ObservableCollection<ActiveStyle> _activeStyles = new ObservableCollection<ActiveStyle>();
        bool? _checkAllStyles;

        public int FirstWordMinLineCount { get => _firstWordMinLineCount; set => SetProperty(ref _firstWordMinLineCount, Math.Max(1, value)); }
        public int HangingMinLineCount { get => _hangingMinLineCount; set => SetProperty(ref _hangingMinLineCount, Math.Max(2, value)); }
        public int CenterLastLineMinLineCount { get => _centerLastLineMinLineCount; set => SetProperty(ref _centerLastLineMinLineCount, Math.Max(2, value)); }
        public bool? CheckAllStyles { get => _checkAllStyles; set { if (SetProperty(ref _checkAllStyles, value)) CheckAllChanged(value); } }
        public ObservableCollection<ActiveStyle> ActiveStyles { get => _activeStyles; set => SetProperty(ref _activeStyles, value); }
        public bool RefreshStyles { set => RefreshActiveStylesAction(); }

        int _lastSeenStylesCount = -1;
        string _lastSeenDocName;

        /// <summary>
        /// Two COM reads (Styles.Count and the document name) that say whether
        /// the style set could have changed since the last full refresh. The
        /// focus-return refresh gates on this so it usually costs two calls
        /// instead of enumerating every style. The document name is part of the
        /// stamp because a popped-out pane follows the user across documents,
        /// and two documents easily share a style count. Count misses two rarer
        /// changes - a rename, and a style newly IN USE - which the ungated
        /// refreshes (pane shown, styles-picker opened) still catch.
        /// </summary>
        public bool StylesLookChanged()
        {
            try
            {
                var doc = Vsto.ActiveDocument;
                if (doc == null) return false;
                return doc.Styles.Count != _lastSeenStylesCount || doc.Name != _lastSeenDocName;
            }
            catch { return true; }
        }

        // Resolve selected style names back to COM Style objects at apply-time.
        // Use doc.Styles[name] — direct name lookup, no full scan.
        List<Style> ValidStyles
        {
            get
            {
                var doc = Vsto.ActiveDocument;
                if (doc == null) return new List<Style>();
                var result = new List<Style>();
                foreach (var entry in ActiveStyles)
                {
                    if (!entry.Apply) continue;
                    try { result.Add(doc.Styles[entry.Name]); }
                    catch { } // style may have been deleted since last refresh
                }
                return result;
            }
        }

        // SubClasses
        public CenterLastLine CenterLastLine { get; } = new CenterLastLine();
        public FirstWordStyle FirstWordStyle { get; } = new FirstWordStyle();
        public FirstWordHanging FirstWordHanging { get; } = new FirstWordHanging();

        // Apply Commands
        public RelayCommand ApplyFirstWordStyleCommand => new RelayCommand(() => FirstWordStyle.Apply(ValidStyles, FirstWordMinLineCount));
        public RelayCommand ApplyFirstWordHangingCommand => new RelayCommand(() => FirstWordHanging.Apply(ValidStyles, HangingMinLineCount));
        public RelayCommand ApplyDoubleFirstWordHangingCommand => new RelayCommand(() => FirstWordHanging.DoubleWindow(ValidStyles, HangingMinLineCount));
        public RelayCommand ApplyCenterLastLineCommand => new RelayCommand(() => CenterLastLine.Apply(ValidStyles, CenterLastLineMinLineCount));

        // Remove Commands
        public RelayCommand RemoveFirstWordHangingCommand => new RelayCommand(() => FirstWordHanging.Remove());
        public RelayCommand RemoveFirstWordStyleCommand => new RelayCommand(() => FirstWordStyle.Remove());
        public RelayCommand RemoveCenterLastLineCommand => new RelayCommand(() => CenterLastLine.Remove());

        readonly object _refreshLock = new object();
        bool _refreshInProgress = false;

        public ParagraphsViewModel()
        {
            // Initial style load is deferred to ApplicationIdle by the View
        }

        public void RefreshActiveStylesAction()
        {
            // COM (Word Styles) must be accessed on the STA/UI thread — no Task.Run.
            // Guard against re-entrant calls (e.g. IsVisible + GotFocus firing together).
            if (_refreshInProgress) return;
            _refreshInProgress = true;

            try
            {
                var doc = Vsto.ActiveDocument;
                if (doc == null) return;

                try
                {
                    _lastSeenStylesCount = doc.Styles.Count;
                    _lastSeenDocName = doc.Name;
                }
                catch { }

                // Single-pass: build fetched set and add new entries in one loop
                var fetchedNames = new HashSet<string>();
                var existingNames = new HashSet<string>(ActiveStyles.Select(s => s.Name));

                foreach (Style s in doc.Styles)
                {
                    try
                    {
                        if (!s.InUse) continue;
                        string name = s.NameLocal.ToLower();
                        fetchedNames.Add(name);
                        if (!existingNames.Contains(name))
                        {
                            bool builtIn = s.BuiltIn;
                            ActiveStyles.Add(new ActiveStyle
                            {
                                Name = name,
                                Apply = !(builtIn && (name.StartsWith("head") || name.StartsWith("כותר")))
                            });
                        }
                    }
                    catch { }
                }

                // Remove styles no longer in the doc
                for (int i = ActiveStyles.Count - 1; i >= 0; i--)
                {
                    if (!fetchedNames.Contains(ActiveStyles[i].Name))
                        ActiveStyles.RemoveAt(i);
                }

                CheckAllStyles = ActiveStyles.All(s => s.Apply) ? true
                               : ActiveStyles.All(s => !s.Apply) ? false
                               : (bool?)null;
            }
            finally
            {
                _refreshInProgress = false;
            }
        }

        void CheckAllChanged(bool? value)
        {
            foreach (var entry in ActiveStyles) entry.Apply = value ?? false;
        }
    }
}
