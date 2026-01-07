using DocSeferLib.Helpers;
using Microsoft.Office.Interop.Word;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DocSeferLib.Paragraphs
{
    public class FirstWordStyle : PargaraphsBase
    {
        string _selectedStyle = "מילה ראשונה";
        ObservableCollection<string> _styles = new ObservableCollection<string>();
        public string SelectedStyle 
        {
            get => _selectedStyle; 
            set => SetProperty(ref _selectedStyle, value); 
        }

        public ObservableCollection<string> Styles { get => _styles; set => SetProperty(ref _styles, value); }

        public FirstWordStyle()
        {
            Dispatcher.CurrentDispatcher.InvokeAsync(() =>
            {
                CreateFirstWordStyle();
                foreach (Style style in Vsto.ActiveDocument.Styles)
                    if (style.Type == WdStyleType.wdStyleTypeCharacter)
                        Styles.Add(style.NameLocal);
            }, DispatcherPriority.ApplicationIdle);
        }

        public void Apply(List<Style> styles, int minLineCount)
        {
            //Remove();

            var selectionRange = Vsto.Application.Selection.Range;

            using (new UndoRecordHelper("עיצוב מילה ראשונה"))
            {
                PrepareFootnotes(selectionRange);
                var paragraphs = ValidParagraphs(selectionRange, styles, minLineCount);
                counter = 0;
                foreach (var paragraph in paragraphs)
                {
                    if (counter++ >= MaxSafeIterations)
                    {
                        counter = 0;
                        System.Windows.Forms.Application.DoEvents();
                    }
                    Range paraRange = paragraph.Range;
                    paraRange.Collapse();
                    paraRange.MoveEndUntil(" ");
                    paraRange.Font.Reset();
                    paraRange.set_Style(SelectedStyle);
                    paraRange.Select();
                }
            }
        }

        public void SetFirstWordStyle()
        {
            var listView = new ListView
            {
                Width = 300,
                Height = 400
            };

            foreach (Style style in Vsto.ActiveDocument.Styles.Cast<Style>())
            {
                listView.Items.Add(style.NameLocal);
            }

            var window = new System.Windows.Window
            {
                Style = (System.Windows.Style)new System.Windows.ResourceDictionary { Source = new Uri("pack://application:,,,/WpfLib;component/Dictionaries/ThemedWindowDictionary.xaml") }["ThemedToolWindowStyle"],
                Content = listView,
                SizeToContent = System.Windows.SizeToContent.WidthAndHeight,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen
            };

            listView.SelectionChanged += (s, _) =>
            {
                if (listView.SelectedItem != null)
                {
                    Interaction.SaveSetting(AppDomain.CurrentDomain.FriendlyName, "Settings", "FirstWordStyle", listView.SelectedItem.ToString());
                    window.Close();
                }
            };

            window.ShowDialog();
        }


        public Style CreateFirstWordStyle()
        {
            string targetStyleName = Interaction.GetSetting(AppDomain.CurrentDomain.FriendlyName, "Settings", "FirstWordStyle", "מילה ראשונה");
            if (string.IsNullOrEmpty(targetStyleName))
                targetStyleName = "מילה ראשונה";

            foreach (Style targetStyle in Vsto.ActiveDocument.Styles)
                if (targetStyle.NameLocal == targetStyleName)
                    return targetStyle;

            Style newStyle = Vsto.ActiveDocument.Styles.Add(targetStyleName, WdStyleType.wdStyleTypeCharacter);
            Font font = newStyle.Font;
            font.Bold = 1;
            font.BoldBi = 1;
            font.Size += 2;
            font.SizeBi += 2;
            font.Position = -1;
            //newStyle.QuickStyle = true;

            return newStyle;

            // Optional: Copy style to Normal template (commented out as in original)
            // Application.OrganizerCopy(
            //     Application.ActiveDocument.Name,
            //     Application.NormalTemplate,
            //     targetStyleName,
            //     WdOrganizerObject.wdOrganizerObjectStyles
            // );
        }

        public void Remove(Range targetRange = null)
        {
            if (targetRange == null)
                targetRange = Vsto.Selection.Range;

            targetRange.Start = targetRange.Paragraphs.First.Range.Start;
            targetRange.End = targetRange.Paragraphs.Last.Range.End;

            using (new UndoRecordHelper("הסרת עיצוב מילה ראשונה"))
            {
                counter = 0;
                foreach (Paragraph paragraph in targetRange.Paragraphs.Cast<Paragraph>().ToList())
                {
                    if (counter++ >= MaxSafeIterations)
                    {
                        counter = 0;
                        System.Windows.Forms.Application.DoEvents();
                    }
                    Range paraRange = paragraph.Range;
                    if (!paraRange.Text.Contains(" ")) continue;
                    paraRange.Collapse();
                    paraRange.MoveEndUntil(" ");
                    paraRange.MoveEnd();
                    var txt = paraRange.Text;
                    paraRange.Text = "";
                    paraRange.Text = txt;
                    paraRange.Select();
                }
            }
        }
    }
}
