using Nakdan.Core;
using Nakdan.Helpers;
using Nakdan.WdStyles;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using WpfLib;
using WpfLib.ViewModels;

namespace Nakdan.UI
{
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
    public class NakdanViewModel : ViewModelBase
    {
        // ── Static API instance ───────────────────────────────
        public static NakdanWrapper _nakdanWrapper { get; set; }

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
            _nakdanWrapper = new NakdanWrapper();

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
            RemoveNikkudCommand      = new RelayCommand(RemoveNikkud,       () => !IsBusy);
            RemoveCantillationsCommand = new RelayCommand(RemoveCantillations, () => !IsBusy);
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
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(IsActive));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set { _statusMessage = value; OnPropertyChanged(nameof(StatusMessage)); OnPropertyChanged(nameof(HasStatus)); OnPropertyChanged(nameof(IsActive)); }
        }
        public bool HasStatus => !string.IsNullOrEmpty(_statusMessage);

        /// <summary>True when the status/busy block should be visible (busy or has a message).</summary>
        public bool IsActive => _isBusy || !string.IsNullOrEmpty(_statusMessage);

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
            private set { _hasError = value; OnPropertyChanged(nameof(HasError)); }
        }

        public GenreItem SelectedGenre
        {
            get => _selectedGenre;
            set
            {
                _selectedGenre = value;
                OnPropertyChanged(nameof(SelectedGenre));
                if (_nakdanWrapper != null) _nakdanWrapper.Options.Genre = value?.Genre ?? DictaGenre.Modern;
                
                // Persist the user's choice to registry
                if (value != null)
                    SettingsManager.Save("Nakdan", "SelectedGenre", value.Genre);
            }
        }

        // ── Commands ──────────────────────────────────────────
        public ICommand VowelizeSelectionCommand { get; }
        public ICommand RemoveNikkudCommand      { get; }
        public ICommand RemoveCantillationsCommand { get; }
        public ICommand CancelCommand            { get; }
        public ICommand ClearStatusCommand       { get; }

        // ── Command implementations ───────────────────────────
        private void VowelizeSelection()
        {
            SyncIgnoredStyles();
            SetStatus(string.Empty, false);
            IsBusy = true;
            _cancellationTokenSource = new CancellationTokenSource();

            // Wire up progress callback
            _nakdanWrapper.OnProgress = (msg) =>
            {
                Dispatcher.CurrentDispatcher.Invoke(() =>
                {
                    StatusMessage = msg;
                });
            };

            _nakdanWrapper.RunSafe(async () =>
            {
                try
                {
                    await _nakdanWrapper.VowelizeSelectionAsync(_cancellationTokenSource.Token);
                    SetStatus("הניקוד הושלם ✓", false);
                }
                catch (OperationCanceledException)
                {
                    SetStatus("הניקוד בוטל על ידי המשתמש", false);
                }
                finally
                {
                    IsBusy = false;
                    await AutoHideStatusAsync();
                }
            });
        }

        private async void RemoveNikkud()
        {
            SetStatus(string.Empty, false);
            IsBusy = true;

            try
            {
                _nakdanWrapper.RemoveNikkud();
                SetStatus("הניקוד הוסר ✓", false);
            }
            catch( Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
            finally
            {
                IsBusy = false;
                await AutoHideStatusAsync();
            }
        }

        private async void RemoveCantillations()
        {
            SetStatus(string.Empty, false);
            IsBusy = true;

            try
            {
                _nakdanWrapper.RemoveCantillations();
                SetStatus("הטעמים הוסרו ✓", false);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
            finally
            {
                IsBusy = false;
                await AutoHideStatusAsync();
            }
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
                    var documentStyles = VstoHelper.StyleProvider.GetUsedStyles();

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
                    }

                    OnPropertyChanged(nameof(Styles));
                }
                catch (Exception)
                {
                    // Silently handle style loading errors — styles list remains unchanged
                }
            }, DispatcherPriority.ApplicationIdle);
        }

        

        /// <summary>
        /// Push the current checkbox state into NakdanApi.Options.IgnoredStyles.
        /// Called before every vowelize operation.
        /// </summary>
        private void SyncIgnoredStyles()
        {
            _nakdanWrapper.ClearIgnoredStyles();
            
            foreach (StyleItem s in Styles)
            {
                if (s.IsIgnored)
                {
                    _nakdanWrapper.AddIgnoredStyle(s.Name);
                }
            }
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
    }
}
