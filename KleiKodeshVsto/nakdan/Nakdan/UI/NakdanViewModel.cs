using Nakdan.Core;
using Nakdan.Helpers;
using Nakdan.WdStyles;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Word = Microsoft.Office.Interop.Word;
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
    //  CLEAN OPTION ITEM — one checkbox in the "clean text" dropdown
    // ═══════════════════════════════════════════════════════════
    public class CleanOptionItem : INotifyPropertyChanged
    {
        private bool _isChecked;

        public CleanOptionItem(TextCleanOptions option, string displayName)
        {
            Option      = option;
            DisplayName = displayName;
        }

        public TextCleanOptions Option      { get; }
        public string           DisplayName { get; }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value) return;
                _isChecked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
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
                new GenreItem { Genre = DictaGenre.Rabbinic, DisplayName = Nakdan.Properties.Strings.Genre_Rabbinic       },
                new GenreItem { Genre = DictaGenre.Bible,    DisplayName = Nakdan.Properties.Strings.Genre_Bible      },
                new GenreItem { Genre = DictaGenre.Modern,   DisplayName = Nakdan.Properties.Strings.Genre_Modern  },
                new GenreItem { Genre = DictaGenre.Poetry,   DisplayName = Nakdan.Properties.Strings.Genre_Poetry            },
            };

            // Load saved genre preference from registry, default to Modern
            DictaGenre savedGenre = SettingsManager.GetEnum("Nakdan", "SelectedGenre", DictaGenre.Modern);
            _selectedGenre = FindGenreItem(savedGenre) ?? Genres[0];

            // ── Clean-text options (persisted as one flags value) ─
            var savedClean = SettingsManager.GetEnum("Nakdan", "CleanTextOptions", DefaultCleanOptions);
            CleanOptions = new ObservableCollection<CleanOptionItem>
            {
                new CleanOptionItem(TextCleanOptions.Nikkud,                 Nakdan.Properties.Strings.Clean_Nikkud),
                new CleanOptionItem(TextCleanOptions.Cantillations,          Nakdan.Properties.Strings.Clean_Cantillations),
                new CleanOptionItem(TextCleanOptions.Periods,                Nakdan.Properties.Strings.Clean_Periods),
                new CleanOptionItem(TextCleanOptions.Commas,                 Nakdan.Properties.Strings.Clean_Commas),
                new CleanOptionItem(TextCleanOptions.Dashes,                 Nakdan.Properties.Strings.Clean_Dashes),
                new CleanOptionItem(TextCleanOptions.QuestionAndExclamation, Nakdan.Properties.Strings.Clean_QuestionExclamation),
                new CleanOptionItem(TextCleanOptions.Colons,                 Nakdan.Properties.Strings.Clean_Colons),
            };
            _keepPunctuationAtParagraphEnd = (savedClean & TextCleanOptions.KeepPunctuationAtParagraphEnd) != 0;
            foreach (var o in CleanOptions)
            {
                o.IsChecked = (savedClean & o.Option) != 0;
                o.PropertyChanged += OnCleanOptionChanged;
            }

            // ── Styles list (loaded dynamically from Word) ─
            Styles = new ObservableCollection<StyleItem>();

            // Propagate checkbox changes into the API options immediately
            foreach (var s in Styles)
                s.PropertyChanged += OnStyleItemChanged;

            // ── Commands ──────────────────────────────────────────
            VowelizeSelectionCommand = new RelayCommand(VowelizeSelection,  () => !IsBusy);
            CleanTextCommand         = new RelayCommand(CleanText,          () => !IsBusy);
            CancelCommand            = new RelayCommand(Cancel,            () => IsBusy);
            ClearStatusCommand       = new RelayCommand(() => { StatusMessage = string.Empty; HasError = false; });

            // ── Nikud suggestions ─────────────────────────────────
            WordOptions = new ObservableCollection<string>();
            WordOptions.CollectionChanged += (_, __) => { OnPropertyChanged(nameof(HasWordOptions)); OnPropertyChanged(nameof(SuggestionsHint)); };
            ApplyWordOptionCommand   = new RelayCommand<string>(ApplyWordOption);
            ShowSuggestionsCommand   = new RelayCommand(ShowSuggestions, () => !IsBusy);
            NextWordCommand          = new RelayCommand(() => SelectWordAndShowSuggestions(true),  () => !IsBusy);
            PreviousWordCommand      = new RelayCommand(() => SelectWordAndShowSuggestions(false), () => !IsBusy);
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

        // ── Clean-text options ────────────────────────────────
        private const TextCleanOptions DefaultCleanOptions =
            TextCleanOptions.Nikkud | TextCleanOptions.Cantillations;

        public ObservableCollection<CleanOptionItem> CleanOptions { get; }

        private TextCleanOptions SelectedCleanOptions
        {
            get
            {
                var flags = TextCleanOptions.None;
                foreach (var o in CleanOptions)
                    if (o.IsChecked) flags |= o.Option;
                if (_keepPunctuationAtParagraphEnd) flags |= TextCleanOptions.KeepPunctuationAtParagraphEnd;
                return flags;
            }
        }

        /// <summary>
        /// Spare the punctuation that ends a paragraph. Its own checkbox, outside "All":
        /// it modifies the removals rather than naming a mark kind to remove.
        /// </summary>
        private bool _keepPunctuationAtParagraphEnd;
        public bool KeepPunctuationAtParagraphEnd
        {
            get => _keepPunctuationAtParagraphEnd;
            set
            {
                if (_keepPunctuationAtParagraphEnd == value) return;
                _keepPunctuationAtParagraphEnd = value;
                OnPropertyChanged(nameof(KeepPunctuationAtParagraphEnd));
                SettingsManager.Save("Nakdan", "CleanTextOptions", SelectedCleanOptions);
            }
        }

        /// <summary>"All" checkbox: true = all, false = none, null = mixed.</summary>
        public bool? AllCleanOptionsChecked
        {
            get
            {
                int count = 0;
                foreach (var o in CleanOptions) if (o.IsChecked) count++;
                return count == 0 ? false : count == CleanOptions.Count ? true : (bool?)null;
            }
            set
            {
                // A click on a mixed box cycles to null; treat it as "check all".
                bool check = value != false;
                foreach (var o in CleanOptions) o.IsChecked = check;
            }
        }

        private void OnCleanOptionChanged(object sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(nameof(AllCleanOptionsChecked));
            SettingsManager.Save("Nakdan", "CleanTextOptions", SelectedCleanOptions);
        }

        // ── Commands ──────────────────────────────────────────
        public ICommand VowelizeSelectionCommand { get; }
        public ICommand CleanTextCommand         { get; }
        public ICommand CancelCommand            { get; }
        public ICommand ClearStatusCommand       { get; }
        public ICommand ApplyWordOptionCommand   { get; }

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
                    SetStatus(Nakdan.Properties.Strings.Status_Done, false);
                }
                catch (OperationCanceledException)
                {
                    SetStatus(Nakdan.Properties.Strings.Status_Cancelled, false);
                }
                finally
                {
                    IsBusy = false;
                    await AutoHideStatusAsync();
                }
            });
        }

        private async void CleanText()
        {
            SetStatus(string.Empty, false);
            IsBusy = true;

            try
            {
                _nakdanWrapper.CleanSelection(SelectedCleanOptions);
                SetStatus(Nakdan.Properties.Strings.Status_Cleaned, false);
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

        // ── Nikud suggestions ─────────────────────────────────
        //  On explicit request (button / next-prev / Ctrl+arrows), the Word
        //  selection's single Hebrew word fetches Dicta's alternative vocalizations for it
        //  (in its sentence context); clicking one replaces the word.

        private readonly DictaApiClient _suggestionsApi = new DictaApiClient();
        private string _suggestionsStatus;
        private CancellationTokenSource _suggestionsCts;
        private Word.Range _suggestionRange;
        private string     _suggestionWord;

        public ObservableCollection<string> WordOptions { get; }

        public ICommand ShowSuggestionsCommand { get; }
        public ICommand NextWordCommand        { get; }
        public ICommand PreviousWordCommand    { get; }

        public string SuggestionsStatus
        {
            get => _suggestionsStatus;
            private set
            {
                _suggestionsStatus = value;
                OnPropertyChanged(nameof(SuggestionsStatus));
                OnPropertyChanged(nameof(HasSuggestionsStatus));
            }
        }
        public bool HasSuggestionsStatus => !string.IsNullOrEmpty(_suggestionsStatus);
        public bool HasWordOptions => WordOptions.Count > 0;
        public string SuggestionsHint => HasWordOptions
            ? Nakdan.Properties.Strings.Hint_ClickOption
            : Nakdan.Properties.Strings.Hint_SelectWord;

        /// <summary>Explicit request: fetch options for the word at the selection, even if it is the same word.</summary>
        public void ShowSuggestions() => UpdateSuggestionsAsync(true);

        /// <summary>Select the logically next (forward) / previous word, then show its options.</summary>
        private void SelectWordAndShowSuggestions(bool forward)
        {
            try
            {
                Word.Selection sel = VstoHelper.Application?.Selection;
                if (sel == null) return;
                Word.Range target = FindAdjacentWord(sel.Range, forward);
                if (target == null) return;
                target.Select();
            }
            catch { return; }
            UpdateSuggestionsAsync(true);
        }

        private void ResetSuggestions(string status)
        {
            _suggestionsCts?.Cancel();
            _suggestionsCts = null;
            _suggestionRange = null;
            _suggestionWord = null;
            WordOptions.Clear();
            SuggestionsStatus = status;
        }

        private static bool IsHebrewLetter(char c) => c >= 'א' && c <= 'ת';

        private static bool IsWordChar(char c) =>
            char.IsLetter(c) || (c >= '֑' && c <= 'ׇ') || c == '"' || c == '\'' || c == '׳' || c == '״';

        private async void UpdateSuggestionsAsync(bool force = false)
        {
            Word.Range wordRange;
            string word, context;
            int offset;
            try
            {
                Word.Selection sel = VstoHelper.Application?.Selection;
                if (sel == null) { ResetSuggestions(string.Empty); return; }

                wordRange = sel.Range.Duplicate;
                wordRange.Expand(Word.WdUnits.wdWord);

                // Trim leading/trailing spaces and punctuation.
                string text = wordRange.Text ?? string.Empty;
                int lead = 0, trail = 0;
                while (lead < text.Length && !IsWordChar(text[lead])) lead++;
                while (trail < text.Length - lead && !IsWordChar(text[text.Length - 1 - trail])) trail++;
                if (lead + trail >= text.Length) { ResetSuggestions(string.Empty); return; }
                wordRange.SetRange(wordRange.Start + lead, wordRange.End - trail);
                word = wordRange.Text ?? string.Empty;

                // Skip multi-word selections and non-Hebrew words.
                bool hasHebrew = false;
                foreach (char c in word)
                {
                    if (char.IsWhiteSpace(c)) { ResetSuggestions(string.Empty); return; }
                    if (IsHebrewLetter(c)) hasHebrew = true;
                }
                if (!hasHebrew) { ResetSuggestions(string.Empty); return; }

                // Same word as last time — keep the current list.
                if (!force && _suggestionRange != null && word == _suggestionWord
                    && _suggestionRange.Start == wordRange.Start && _suggestionRange.End == wordRange.End)
                return;

                // Context = containing sentence, capped around the word.
                Word.Range sentence = wordRange.Sentences[1];
                int ctxStart = sentence.Start, ctxEnd = sentence.End;
                const int MaxContext = 300;
                if (ctxEnd - ctxStart > MaxContext)
                {
                    int half = Math.Max(0, (MaxContext - word.Length) / 2);
                    ctxStart = Math.Max(ctxStart, wordRange.Start - half);
                    ctxEnd = Math.Min(ctxEnd, ctxStart + MaxContext);
                    if (ctxEnd < wordRange.End) ctxEnd = wordRange.End;
                }
                Word.Document doc = wordRange.Document;
                context = doc.Range(ctxStart, ctxEnd).Text ?? string.Empty;

                // Range character counts can diverge from string length (fields,
                // hidden marks), so locate the word near its expected index.
                string before = doc.Range(ctxStart, wordRange.Start).Text ?? string.Empty;
                offset = context.IndexOf(word, Math.Min(before.Length, context.Length), StringComparison.Ordinal);
                if (offset < 0) offset = context.IndexOf(word, StringComparison.Ordinal);
                if (offset < 0) { context = word; offset = 0; }
            }
            catch
            {
                ResetSuggestions(string.Empty);
                return;
            }

            ResetSuggestions(Nakdan.Properties.Strings.Sugg_Loading);
            _suggestionRange = wordRange;
            _suggestionWord = word;
            var cts = new CancellationTokenSource();
            _suggestionsCts = cts;
            string genre = (_selectedGenre?.Genre ?? DictaGenre.Modern).ToString().ToLowerInvariant();

            try
            {
                List<string> options = await _suggestionsApi.GetWordOptionsAsync(context, offset, genre, cts.Token);
                if (cts.IsCancellationRequested) return;
                foreach (string o in options) WordOptions.Add(o);
                SuggestionsStatus = options.Count == 0 ? Nakdan.Properties.Strings.Sugg_None : string.Empty;
            }
            catch
            {
                if (cts.IsCancellationRequested) return;
                SuggestionsStatus = Nakdan.Properties.Strings.Sugg_LoadFailed;
            }
        }

        private void ApplyWordOption(string option)
        {
            if (string.IsNullOrEmpty(option) || _suggestionRange == null) return;
            Word.Range range = _suggestionRange;
            Word.UndoRecord undo = null;
            bool started = false;
            Word.Range next = null;
            try
            {
                if (range.Text != _suggestionWord)
                {
                    ResetSuggestions(Nakdan.Properties.Strings.Sugg_WordChanged);
                    return;
                }
                undo = VstoHelper.Application.UndoRecord;
                undo.StartCustomRecord(Nakdan.Properties.Strings.Undo_Suggestion);
                started = true;
                range.Text = option;
                _suggestionWord = option;
                next = FindNextWord(range);
            }
            catch
            {
                SuggestionsStatus = Nakdan.Properties.Strings.Sugg_ReplaceFailed;
            }
            finally
            {
                if (started)
                    try { undo.EndCustomRecord(); } catch { }
            }
            // Advance: select the next word and show its options right away.
            if (next != null)
            {
                try { next.Select(); } catch { return; }
                UpdateSuggestionsAsync(true);
            }
        }


        /// <summary>The next word after <paramref name="range"/> that holds a letter, trailing spaces trimmed; null at the end.</summary>
        private static Word.Range FindNextWord(Word.Range range) => FindAdjacentWord(range, true);

        /// <summary>The previous word before <paramref name="range"/> that holds a letter, trailing spaces trimmed; null at the start.</summary>
        private static Word.Range FindPreviousWord(Word.Range range) => FindAdjacentWord(range, false);

        /// <summary>
        /// Ctrl+Right / Ctrl+Left from the pane: select the next / previous word in
        /// Word. Visual like Word — in an RTL paragraph Left is forward. Then shows
        /// the new word's suggestions.
        /// </summary>
        public bool MoveToAdjacentWord(bool rightArrow)
        {
            try
            {
                Word.Selection sel = VstoHelper.Application?.Selection;
                if (sel == null) return false;
                bool rtl = false;
                try { rtl = sel.ParagraphFormat.ReadingOrder == Word.WdReadingOrder.wdReadingOrderRtl; } catch { }
                if (!rtl)
                    try { rtl = sel.Paragraphs[1].ReadingOrder == Word.WdReadingOrder.wdReadingOrderRtl; } catch { }
                bool forward = rtl ? !rightArrow : rightArrow;
                Word.Range target = FindAdjacentWord(sel.Range, forward);
                if (target == null) return true;
                target.Select();
            }
            catch
            {
                return false;
            }
            UpdateSuggestionsAsync(true);
            return true;
        }

        private static Word.Range FindAdjacentWord(Word.Range range, bool forward)
        {
            Word.Range r = range.Duplicate;
            for (int i = 0; i < 20; i++)
            {
                Word.Range w = forward
                    ? r.Next(Word.WdUnits.wdWord, 1)
                    : r.Previous(Word.WdUnits.wdWord, 1);
                if (w == null) return null;
                string t = w.Text ?? string.Empty;
                foreach (char c in t)
                {
                    if (char.IsLetter(c))
                    {
                        w.MoveEndWhile("  ", Word.WdConstants.wdBackward);
                        return w;
                    }
                }
                r = w;
            }
            return null;
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
