using Microsoft.Office.Interop.Word;
using WpfLib;
using WpfLib.ViewModels;

namespace DocSeferLib.Spacing
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


        public SpacingViewModel()
        {
            try
            {
                Vsto.ApplicationFactory.GetVstoObject(Vsto.Application.ActiveDocument).SelectionChange += (_, x) =>
                    UpdateProperties();
                UpdateProperties();
            }
            catch { }
        }

        void UpdateProperties()
        {
            SetProperty(ref _spaceAfter, Vsto.Selection.ParagraphFormat.SpaceAfter, nameof(SpaceAfter));
            SetProperty(ref _spaceBefore, Vsto.Selection.ParagraphFormat.SpaceBefore, nameof(SpaceBefore));
            SetProperty(ref _lineSpacing, Vsto.Selection.ParagraphFormat.LineSpacing, nameof(LineSpacing));
            SetProperty(ref _wordSpacing, Vsto.Selection.GetSpaceBetweenWords(), nameof(WordSpacing));
            SetProperty(ref _characterStretch, Vsto.Selection.Font.Spacing, nameof(CharacterStretch));
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
