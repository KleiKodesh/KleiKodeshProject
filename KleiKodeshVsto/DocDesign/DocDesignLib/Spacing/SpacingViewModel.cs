using Microsoft.Office.Interop.Word;
using DocDesign.Helpers;
using WpfLib.ViewModels;

namespace DocDesign.Spacing
{
    public class SpacingViewModel : ViewModelBase
    {
        float _stepSize = (float)0.5;
        float _spaceAfter;
        float _spaceBefore;
        float _lineSpacing;
        float _wordSpacing;
        float _characterStretch;

        public float StepSize { get => _stepSize; set => SetProperty(ref _stepSize, value); }
        public float SpaceAfter { get => _spaceAfter; set => SetSpaceAfter(value); }
        public float SpaceBefore { get => _spaceBefore; set => SetSpaceBefore(value); }
        public float LineSpacing { get => _lineSpacing; set => SetLineSpacing(value); }
        public float WordSpacing { get => _wordSpacing; set => SetWordSpacing(value); }
        public float CharacterStretch { get => _characterStretch; set => SetCharacterStretch(value); }


        public RelayCommand<string> SetSpaceAfterCommand => new RelayCommand<string>(param => SetSpaceAfter(param));
        public RelayCommand<string> SetSpaceBeforeCommand => new RelayCommand<string>(param => SetSpaceBefore(param));
        public RelayCommand<string> SetLineSpacingCommand => new RelayCommand<string>(param => SetLineSpacing(param));
        public RelayCommand<string> SetWordSpacingCommand => new RelayCommand<string>(param => SetWordSpacing(param));
        public RelayCommand<string> SetCharacterStretchCommand => new RelayCommand<string>(param => SetCharacterStretch(param));


        // The constructor deliberately touches no Word COM. The XAML creates this
        // view model eagerly, including for views that are built and immediately
        // discarded (the ribbon used to construct one per click), and a
        // SelectionChange subscription taken here was never released - every
        // discarded view left a handler doing COM reads on each caret move,
        // and they stacked. Attach() is the only place that subscribes.
        public SpacingViewModel() { }

        bool _subscribed;

        /// <summary>
        /// False while the pane is hidden or its WPF tree is torn down (the view
        /// flips it from IsVisibleChanged and Unloaded), so a pane that is not on
        /// screen costs a boolean check per caret move instead of a dozen COM
        /// reads. The show path resyncs the values via DeferredInit. The event
        /// itself cannot be unhooked cheaply (the delegate would need the VSTO
        /// event's exact type), and an inert handler is just as quiet.
        /// </summary>
        public bool Live { get; set; }

        /// <summary>
        /// Subscribes to the document's SelectionChange, once. Called from the
        /// view's Loaded, which only a pane that is actually shown ever raises.
        /// </summary>
        public void Attach()
        {
            Live = true;
            if (_subscribed || Vsto.Application == null) return;
            try
            {
                Vsto.ApplicationFactory.GetVstoObject(Vsto.Application.ActiveDocument).SelectionChange += (_, x) =>
                {
                    if (Live) ScheduleUpdate();
                };
                _subscribed = true;
            }
            catch { }
        }

        System.Windows.Threading.DispatcherTimer _updateDebounce;

        /// <summary>
        /// Coalesces a burst of selection changes (a held arrow key, a drag)
        /// into one property read after the caret settles. Each read is over a
        /// dozen COM calls including a text scan; paying that per keystroke of
        /// a key repeat is what a debounce is for.
        /// </summary>
        void ScheduleUpdate()
        {
            if (_updateDebounce == null)
            {
                _updateDebounce = new System.Windows.Threading.DispatcherTimer(
                    System.Windows.Threading.DispatcherPriority.Background)
                {
                    Interval = System.TimeSpan.FromMilliseconds(300)
                };
                _updateDebounce.Tick += (_, __) =>
                {
                    _updateDebounce.Stop();
                    if (Live) UpdateProperties();
                };
            }
            _updateDebounce.Stop();
            _updateDebounce.Start();
        }

        /// <summary>
        /// Called by the View after first render to populate initial values without
        /// blocking the constructor or the Loaded event.
        /// </summary>
        public void DeferredInit()
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeAsync(
                UpdateProperties,
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        void UpdateProperties()
        {
            // Everything inside the try, the Selection getter included: with zero
            // documents open the COM property THROWS rather than returning null,
            // and the debounce means this can run 300ms after the last document
            // closed - from a timer Tick, where an escaped exception reaches
            // Word's dispatcher instead of anyone's catch.
            try
            {
                var selection = Vsto.Selection;
                if (selection == null) return;

                SetProperty(ref _spaceAfter, selection.ParagraphFormat.SpaceAfter, nameof(SpaceAfter));
                SetProperty(ref _spaceBefore, selection.ParagraphFormat.SpaceBefore, nameof(SpaceBefore));
                SetProperty(ref _lineSpacing, selection.ParagraphFormat.LineSpacing, nameof(LineSpacing));
                SetProperty(ref _wordSpacing, selection.GetSpaceBetweenWords(), nameof(WordSpacing));
                SetProperty(ref _characterStretch, selection.Font.Spacing, nameof(CharacterStretch));
            }
            catch { } // selection can enter transient states (e.g. mid-close) that throw on read
        }

        void SetSpaceAfter(object param)
        {
            Vsto.Selection.ParagraphFormat.SpaceAfterAuto = 0;
            if (param is float f) Vsto.Selection.ParagraphFormat.SpaceAfter = _spaceAfter = f;
            else if (param is string s && !string.IsNullOrEmpty(s))
            {
                if (s == "=") Vsto.Selection.ParagraphFormat.SpaceAfter = Vsto.Selection.GetSpaceAfterFromStyle();
                else if (s == "+") Vsto.Selection.ParagraphFormat.SpaceAfter += StepSize;
                else if (s == "-") Vsto.Selection.ParagraphFormat.SpaceAfter -= StepSize;
                SetProperty(ref _spaceAfter, Vsto.Selection.ParagraphFormat.SpaceAfter, nameof(SpaceAfter));
            }
        }

        void SetSpaceBefore(object param)
        {
            Vsto.Selection.ParagraphFormat.SpaceBeforeAuto = 0;
            if (param is float f) Vsto.Selection.ParagraphFormat.SpaceBefore = _spaceBefore = f;
            else if (param is string s && !string.IsNullOrEmpty(s))
            {
                if (s == "=") Vsto.Selection.ParagraphFormat.SpaceBefore = Vsto.Selection.GetSpaceBeforeFromStyle();
                else if (s == "+") Vsto.Selection.ParagraphFormat.SpaceBefore += StepSize;
                else if (s == "-") Vsto.Selection.ParagraphFormat.SpaceBefore -= StepSize;
                SetProperty(ref _spaceBefore, Vsto.Selection.ParagraphFormat.SpaceBefore, nameof(SpaceBefore));
            }
        }

        void SetLineSpacing(object param)
        {
            if (param is float f) Vsto.Selection.ParagraphFormat.LineSpacing = _lineSpacing = f;
            else if (param is string s && !string.IsNullOrEmpty(s))
            {
                if (s == "=") Vsto.Selection.ParagraphFormat.LineSpacing = Vsto.Selection.GetLineSpacingFromStyle();
                else if (s == "+") Vsto.Selection.ParagraphFormat.LineSpacing += StepSize;
                else if (s == "-") Vsto.Selection.ParagraphFormat.LineSpacing -= StepSize;
                SetProperty(ref _lineSpacing, Vsto.Selection.ParagraphFormat.LineSpacing, nameof(LineSpacing));
            }
        }

        public void SetWordSpacing(object param)
        {
            if (param is float f) ApllyWordSpacing(f);
            else if (param is string s && !string.IsNullOrEmpty(s))
            {
                float value = s == "+" ? (Vsto.Selection.GetSpaceBetweenWords() + StepSize) : 
                             (s == "-" ? (Vsto.Selection.GetSpaceBetweenWords() - StepSize) : 0);
                ApllyWordSpacing(value);
                SetProperty(ref _wordSpacing, value, nameof(WordSpacing));
            }
        }

        void SetCharacterStretch(object param)
        {
            if (param is float f) Vsto.Selection.Font.Spacing = f;
            else if (param is string s && !string.IsNullOrEmpty(s))
            {
                if (s == "=") Vsto.Selection.Font.Spacing = 0;
                else if (s == "+")  Vsto.Selection.Font.Spacing += StepSize;
                else if (s == "-")  try { Vsto.Selection.Font.Spacing -= StepSize; } catch { }
                SetProperty(ref _characterStretch, Vsto.Selection.Font.Spacing, nameof(CharacterStretch));
            }
        }

        void ApllyWordSpacing(float value)
        {
            Range range = Vsto.Selection.Range.Duplicate;
            range.Start = range.Paragraphs.First.Range.Start;
            range.End = range.Paragraphs.Last.Range.End;

            Find find = range.Find;
            find.Text = " ";
            find.Replacement.Text = " ";
            find.Replacement.Font.Spacing = value;
            find.Format = true;
            find.Wrap = WdFindWrap.wdFindStop;
            find.Execute(Replace: WdReplace.wdReplaceAll);
        }
    }
}
