using Microsoft.Office.Interop.Word;
using Nakdan.Core;
using Nakdan.Styles;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;

namespace Nakdan.UI
{
    // ═══════════════════════════════════════════════════════════
    //  VSTO HELPER — access to Word application and styles
    // ═══════════════════════════════════════════════════════════
    public static class VstoHelper
    {
        public static Microsoft.Office.Interop.Word.Application Application { get; set; }
        public static Microsoft.Office.Tools.Word.ApplicationFactory ApplicationFactory { get; set; }

        public static Document ActiveDocument => Application?.ActiveDocument;

        public static IEnumerable<Style> ActiveStyles =>
            ActiveDocument?.Styles.Cast<Style>().Where(s => s.InUse) ?? Enumerable.Empty<Style>();

        public static DocumentStyleProvider StyleProvider =>
            new DocumentStyleProvider(Application);
    }

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

        public string Name         { get; set; }
        public string DisplayName  { get; set; }   // human-friendly Hebrew label
        public string InternalName { get; set; }   // Word style internal/English name

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
        // ── Static API instance ───────────────────────────────
        public static NakdanApi Api { get; set; }

        // ── Internal state ────────────────────────────────────
        private bool      _isBusy;
        private string    _statusMessage;
        private bool      _hasError;
        private GenreItem _selectedGenre;
        private CancellationTokenSource _cancellationTokenSource;
        private const int StatusAutoHideDelayMs = 3000; // Auto-hide notification after 3 seconds

        // ── Constructor ───────────────────────────────────────
        /// <summary>
        /// Parameterless constructor for XAML instantiation.
        /// The Vsto helper is set up by NakdanView before the view is shown.
        /// </summary>
        public NakdanViewModel()
        {
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

            // ── Styles list (loaded dynamically from Word) ─
            Styles = new ObservableCollection<StyleItem>();

            // Propagate checkbox changes into the API options immediately
            foreach (var s in Styles)
                s.PropertyChanged += OnStyleItemChanged;

            // ── Commands ──────────────────────────────────────────
            VowelizeSelectionCommand = new RelayCommand(VowelizeSelection,  () => !IsBusy);
            CancelCommand            = new RelayCommand(Cancel,            () => IsBusy);
            ClearStatusCommand       = new RelayCommand(() => { StatusMessage = string.Empty; HasError = false; });
        }

        // ── Bindable properties ────────────────────────────────

        public ObservableCollection<GenreItem>  Genres { get; }
        public ObservableCollection<StyleItem>  Styles { get; }

        public bool RefreshStyles
        {
            set => RefreshActiveStylesAction();
        }

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
                if (Api != null) Api.Options.Genre = value?.Genre ?? DictaGenre.Modern;
                
                // Persist the user's choice to registry
                if (value != null)
                    SettingsManager.Save("Nakdan", "SelectedGenre", value.Genre);
            }
        }

        // ── Commands ──────────────────────────────────────────
        public ICommand VowelizeSelectionCommand { get; }
        public ICommand CancelCommand            { get; }
        public ICommand ClearStatusCommand       { get; }

        // ── Command implementations ───────────────────────────
        private void VowelizeSelection()
        {
            SyncIgnoredStyles();
            SetStatus(string.Empty, false);
            IsBusy = true;
            _cancellationTokenSource = new CancellationTokenSource();

            Api.RunSafe(async () =>
            {
                try
                {
                    await Api.VowelizeSelectionAsync(_cancellationTokenSource.Token);
                    SetStatus("ניקוד הסימון הושלם ✓", false);
                    await AutoHideStatusAsync();
                }
                catch (OperationCanceledException)
                {
                    SetStatus("הניקוד בוטל על ידי המשתמש", false);
                    await AutoHideStatusAsync();
                }
                finally { IsBusy = false; }
            });
        }

        private void Cancel()
        {
            _cancellationTokenSource?.Cancel();
        }

        /// <summary>
        /// Auto-hide the status notification after a delay.
        /// This allows the user to see the completion message briefly before it disappears.
        /// </summary>
        private async System.Threading.Tasks.Task AutoHideStatusAsync()
        {
            await System.Threading.Tasks.Task.Delay(StatusAutoHideDelayMs);
            StatusMessage = string.Empty;
            HasError = false;
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
        /// Refresh the styles list from the active Word document.
        /// Loads all in-use styles directly from the document's OOXML.
        /// Uses style IDs internally for efficient comparison.
        /// Called on view load (ApplicationIdle priority) to avoid blocking the UI.
        /// </summary>
        public void RefreshActiveStylesAction()
        {
            Dispatcher.CurrentDispatcher.InvokeAsync(() =>
            {
                // Snapshot currently ignored style IDs before clearing
                var previouslyIgnored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var s in Styles)
                    if (s.IsIgnored) previouslyIgnored.Add(s.Name);

                Styles.Clear();

                try
                {
                    System.Diagnostics.Debug.WriteLine($"[NAKDAN-VM] RefreshActiveStylesAction called");
                    
                    var documentStyles = VstoHelper.StyleProvider.GetUsedStyles();
                    
                    System.Diagnostics.Debug.WriteLine($"[NAKDAN-VM] Got {documentStyles.Count} styles from provider");

                    foreach (var docStyle in documentStyles)
                    {
                        var styleItem = new StyleItem
                        {
                            Name = docStyle.Id,
                            DisplayName = docStyle.Name,
                            InternalName = docStyle.Id,
                            IsIgnored = previouslyIgnored.Contains(docStyle.Id)
                        };

                        Styles.Add(styleItem);
                        styleItem.PropertyChanged += OnStyleItemChanged;

                        System.Diagnostics.Debug.WriteLine($"[NAKDAN-VM] Style loaded: ID='{docStyle.Id}' Display='{docStyle.Name}'");
                    }

                    OnPropertyChanged(nameof(Styles));
                    System.Diagnostics.Debug.WriteLine($"[NAKDAN-VM] Loaded {Styles.Count} styles from document OOXML");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[NAKDAN-VM] Error loading styles: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"[NAKDAN-VM] Stack trace: {ex.StackTrace}");
                }
            }, DispatcherPriority.ApplicationIdle);
        }

        

        /// <summary>
        /// Push the current checkbox state into NakdanApi.Options.IgnoredStyles.
        /// Called before every vowelize operation.
        /// </summary>
        private void SyncIgnoredStyles()
        {
            Api.ClearIgnoredStyles();
            System.Diagnostics.Debug.WriteLine($"[NAKDAN-VM] ===== SYNC IGNORED STYLES =====");
            System.Diagnostics.Debug.WriteLine($"[NAKDAN-VM] Total styles in UI: {Styles.Count}");
            
            int ignoredCount = 0;
            foreach (StyleItem s in Styles)
            {
                if (s.IsIgnored)
                {
                    System.Diagnostics.Debug.WriteLine($"[NAKDAN-VM] Adding ignored style: '{s.Name}'");
                    Api.AddIgnoredStyle(s.Name);
                    ignoredCount++;
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"[NAKDAN-VM] Total ignored styles: {ignoredCount}");
            System.Diagnostics.Debug.WriteLine($"[NAKDAN-VM] API Options.IgnoredStyles count: {Api.Options.IgnoredStyles.Count}");
            foreach (var style in Api.Options.IgnoredStyles)
                System.Diagnostics.Debug.WriteLine($"[NAKDAN-VM]   - '{style}'");
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
