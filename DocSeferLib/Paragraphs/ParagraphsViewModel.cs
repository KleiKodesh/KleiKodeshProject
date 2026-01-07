using Microsoft.Office.Interop.Word;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Threading;
using WpfLib;
using WpfLib.ViewModels;

namespace DocSeferLib.Paragraphs
{
    public class ParagraphsViewModel : ViewModelBase
    {
        public class ActiveStyle : ViewModelBase
        {
            bool _apply;
            public Style Style { get; set; }
            public string Name { get; set; }
            public bool Apply 
            {
                get => _apply;
                set => SetProperty(ref _apply, value);
            }
        }

        int _minLineCount = 2;
        ObservableCollection<ActiveStyle> _activeStyles = new ObservableCollection<ActiveStyle>();
        bool _refreshStyles;
        bool? _checkAllStyles;
        public int MinLineCount { get => _minLineCount; set => SetProperty(ref _minLineCount, value); }
        public bool? CheckAllStyles { get => _checkAllStyles; set { if (SetProperty(ref _checkAllStyles, value)) CheckAllChanged(value); } }
        public ObservableCollection<ActiveStyle> ActiveStyles { get => _activeStyles; set => SetProperty(ref _activeStyles, value); } 
        public bool RefreshStyles  {set => RefreshActiveStylesAction(); }
        List<Style> ValidStyles => ActiveStyles.Where(s => s.Apply == true).Select(s => s.Style).ToList();

        // SubClasses
        public CenterLastLine CenterLastLine { get; } = new CenterLastLine();
        public FirstWordStyle FirstWordStyle { get; } = new FirstWordStyle();
        public FirstWordHanging FirstWordHanging { get; } = new FirstWordHanging();

        // Apply Commands
        public RelayCommand ApplyFirstWordHangingCommand => new RelayCommand(() => FirstWordHanging.Apply(ValidStyles, MinLineCount));
        public RelayCommand ApplyDoubleFirstWordHangingCommand => new RelayCommand(() => FirstWordHanging.DoubleWindow(ValidStyles, MinLineCount));
        public RelayCommand ApplyFirstWordStyleCommand => new RelayCommand(() => FirstWordStyle.Apply(ValidStyles, MinLineCount));
        public RelayCommand ApplyCenterLastLineCommand => new RelayCommand(() => CenterLastLine.Apply(ValidStyles, MinLineCount));

        // Remove Commands
        public RelayCommand RemoveFirstWordHangingCommand => new RelayCommand(() => FirstWordHanging.Remove());
        public RelayCommand RemoveFirstWordStyleCommand => new RelayCommand(() => FirstWordStyle.Remove());
        public RelayCommand RemoveCenterLastLineCommand => new RelayCommand(() => CenterLastLine.Remove());

        public ParagraphsViewModel()
        {
            RefreshActiveStylesAction();
        }

        void RefreshActiveStylesAction()
        {
            Dispatcher.CurrentDispatcher.InvokeAsync(() =>
            {
                foreach (Style style in Vsto.ActiveStyles)
                {
                    string name = style.NameLocal.ToLower();
                    if (!ActiveStyles.Any(s => s.Name == name))
                    {
                        var newActiveStyle = new ActiveStyle
                        {
                            Style = style,
                            Name = name,
                            Apply = !(style.BuiltIn && (name.StartsWith("head") || name.StartsWith("כותר")))
                        };
                        ActiveStyles.Add(newActiveStyle);
                    }
                }

                CheckAllStyles = ActiveStyles.All(s => s.Apply) ? true : ActiveStyles.All(s => !s.Apply) ? false : (bool?)null;
            }, DispatcherPriority.ApplicationIdle);
        }

        void CheckAllChanged(bool? value)
        {
            foreach (var entry in ActiveStyles) entry.Apply = value ?? false;
        }
    }
}
