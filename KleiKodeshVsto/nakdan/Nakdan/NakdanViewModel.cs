using KleiKodesh.Helpers;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Input;

namespace Nakdan
{
    // ═══════════════════════════════════════════════════════════
    //  RELAY COMMAND — lightweight ICommand for MVVM bindings
    // ═══════════════════════════════════════════════════════════
    public class RelayCommand : ICommand
    {
        private readonly Action    _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute    = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add    => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;
        public void Execute(object parameter)    => _execute();

        /// <summary>Forces WPF to re-evaluate CanExecute on all RelayCommands.</summary>
        public static void RaiseCanExecuteChanged()
            => CommandManager.InvalidateRequerySuggested();
    }

    // ═══════════════════════════════════════════════════════════
    //  STYLE ITEM — one row in the "ignored styles" checklist
    // ═══════════════════════════════════════════════════════════
    public class StyleItem : INotifyPropertyChanged
    {
        private bool _isIgnored;

        public string Name      { get; set; }
        public string DisplayName { get; set; }   // human-friendly Hebrew label

        public bool IsIgnored
        {
            get => _isIgnored;
            set { _isIgnored = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ═══════════════════════════════════════════════════════════
    //  GENRE ITEM — one row in the genre ComboBox
    // ═══════════════════════════════════════════════════════════
    public class GenreItem
    {
        public DictaGenre Genre       { get; set; }
        public string     DisplayName { get; set; }
    }

    // ═══════════════════════════════════════════════════════════
    //  NAKDAN VIEW-MODEL
    // ═══════════════════════════════════════════════════════════
    public class NakdanViewModel : INotifyPropertyChanged
    {
        // ── Internal state ────────────────────────────────────
        private NakdanApi _api;
        private bool      _isBusy;
        private string    _statusMessage;
        private bool      _hasError;
        private GenreItem _selectedGenre;
        private CancellationTokenSource _cancellationTokenSource;

        // ── Constructor ───────────────────────────────────────
        /// <param name="api">
        ///   Pass in the NakdanApi instance created in ThisAddIn.cs.
        ///   The ViewModel takes ownership of the Options property.
        /// </param>
        public NakdanViewModel(NakdanApi api)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));

            // ── Genre list ──────────────────────────────────────
            Genres = new ObservableCollection<GenreItem>
            {
                new GenreItem { Genre = DictaGenre.Rabbinic, DisplayName = "טקסט רבני"       },
                new GenreItem { Genre = DictaGenre.Bible,    DisplayName = "טקסט מקראי"      },
                new GenreItem { Genre = DictaGenre.Modern,   DisplayName = "עברית מודרנית"  },
                new GenreItem { Genre = DictaGenre.Poetry,   DisplayName = "שירה"            },
            };

            // Load saved genre preference from registry, default to Modern
            DictaGenre savedGenre = SettingsManager.GetEnum("Nakdan", "SelectedGenre", DictaGenre.Modern);
            _selectedGenre = FindGenreItem(savedGenre) ?? Genres[0];

            // ── Styles list (common Word styles; UI can add more) ─
            Styles = new ObservableCollection<StyleItem>
            {
                new StyleItem { Name = "Heading1",       DisplayName = "כותרת 1"        },
                new StyleItem { Name = "Heading2",       DisplayName = "כותרת 2"        },
                new StyleItem { Name = "Heading3",       DisplayName = "כותרת 3"        },
                new StyleItem { Name = "Title",          DisplayName = "כותרת ראשית"     },
                new StyleItem { Name = "Subtitle",       DisplayName = "כותרת משנה"      },
                new StyleItem { Name = "Quote",          DisplayName = "ציטוט"           },
                new StyleItem { Name = "IntenseQuote",   DisplayName = "ציטוט מודגש"     },
                new StyleItem { Name = "Caption",        DisplayName = "כיתוב"           },
                new StyleItem { Name = "Footnote Text",  DisplayName = "טקסט הערת שוליים"},
                new StyleItem { Name = "Normal",         DisplayName = "רגיל"            },
            };

            // Propagate checkbox changes into the API options immediately
            foreach (var s in Styles)
                s.PropertyChanged += OnStyleItemChanged;

            // ── Commands ──────────────────────────────────────────
            VowelizeDocumentCommand  = new RelayCommand(VowelizeDocument,  () => !IsBusy);
            VowelizeSelectionCommand = new RelayCommand(VowelizeSelection,  () => !IsBusy);
            VowelizeFootnotesCommand = new RelayCommand(VowelizeFootnotes,  () => !IsBusy);
            CancelCommand            = new RelayCommand(Cancel,            () => IsBusy);
            ClearStatusCommand       = new RelayCommand(() => { StatusMessage = string.Empty; HasError = false; });
        }

        // ── Parameterless constructor for XAML design-time support ─
        public NakdanViewModel() { }

        // ── Bindable properties ────────────────────────────────

        public ObservableCollection<GenreItem>  Genres { get; }
        public ObservableCollection<StyleItem>  Styles { get; }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                _isBusy = value;
                OnPropertyChanged();
                RelayCommand.RaiseCanExecuteChanged();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set { _statusMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasStatus)); }
        }
        public bool HasStatus => !string.IsNullOrEmpty(_statusMessage);

        /// <summary>Number of styles currently marked as ignored — drives the badge.</summary>
        public int IgnoredCount
        {
            get
            {
                int count = 0;
                if (Styles != null)
                    foreach (var s in Styles)
                        if (s.IsIgnored) count++;
                return count;
            }
        }

        public bool HasError
        {
            get => _hasError;
            private set { _hasError = value; OnPropertyChanged(); }
        }

        public GenreItem SelectedGenre
        {
            get => _selectedGenre;
            set
            {
                _selectedGenre = value;
                OnPropertyChanged();
                if (_api != null) _api.Options.Genre = value?.Genre ?? DictaGenre.Modern;
                
                // Persist the user's choice to registry
                if (value != null)
                    SettingsManager.Save("Nakdan", "SelectedGenre", value.Genre);
            }
        }

        // ── Commands ──────────────────────────────────────────
        public ICommand VowelizeDocumentCommand  { get; }
        public ICommand VowelizeSelectionCommand { get; }
        public ICommand VowelizeFootnotesCommand { get; }
        public ICommand CancelCommand            { get; }
        public ICommand ClearStatusCommand       { get; }

        // ── Command implementations ───────────────────────────
        private void VowelizeDocument()
        {
            SyncIgnoredStyles();
            SetStatus(string.Empty, false);
            IsBusy = true;
            _cancellationTokenSource = new CancellationTokenSource();

            _api.RunSafe(async () =>
            {
                try
                {
                    await _api.VowelizeDocumentAsync(_cancellationTokenSource.Token);
                    SetStatus("הניקוד הושלם בהצלחה ✓", false);
                }
                catch (OperationCanceledException)
                {
                    SetStatus("הניקוד בוטל על ידי המשתמש", false);
                }
                finally { IsBusy = false; }
            });
        }

        private void VowelizeSelection()
        {
            SyncIgnoredStyles();
            SetStatus(string.Empty, false);
            IsBusy = true;
            _cancellationTokenSource = new CancellationTokenSource();

            _api.RunSafe(async () =>
            {
                try
                {
                    await _api.VowelizeSelectionAsync(_cancellationTokenSource.Token);
                    SetStatus("ניקוד הסימון הושלם ✓", false);
                }
                catch (OperationCanceledException)
                {
                    SetStatus("הניקוד בוטל על ידי המשתמש", false);
                }
                finally { IsBusy = false; }
            });
        }

        private void VowelizeFootnotes()
        {
            SyncIgnoredStyles();
            SetStatus(string.Empty, false);
            IsBusy = true;
            _cancellationTokenSource = new CancellationTokenSource();

            _api.RunSafe(async () =>
            {
                try
                {
                    await _api.VowelizeFootnotesAsync(_cancellationTokenSource.Token);
                    SetStatus("ניקוד הערות השוליים הושלם ✓", false);
                }
                catch (OperationCanceledException)
                {
                    SetStatus("הניקוד בוטל על ידי המשתמש", false);
                }
                finally { IsBusy = false; }
            });
        }

        private void Cancel()
        {
            _cancellationTokenSource?.Cancel();
        }

        // ── Helpers ───────────────────────────────────────────

        /// <summary>
        /// Find the GenreItem that matches the given DictaGenre.
        /// </summary>
        private GenreItem FindGenreItem(DictaGenre genre)
        {
            foreach (var item in Genres)
                if (item.Genre == genre)
                    return item;
            return null;
        }

        /// <summary>
        /// Push the current checkbox state into NakdanApi.Options.IgnoredStyles.
        /// Called before every vowelize operation.
        /// </summary>
        private void SyncIgnoredStyles()
        {
            _api.ClearIgnoredStyles();
            foreach (var s in Styles)
                if (s.IsIgnored)
                    _api.AddIgnoredStyle(s.Name);
        }

        private void OnStyleItemChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(StyleItem.IsIgnored))
            {
                SyncIgnoredStyles();
                OnPropertyChanged(nameof(IgnoredCount));
            }
        }

        private void SetStatus(string message, bool isError)
        {
            StatusMessage = message;
            HasError      = isError;
        }

        // ── INotifyPropertyChanged ────────────────────────────
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
