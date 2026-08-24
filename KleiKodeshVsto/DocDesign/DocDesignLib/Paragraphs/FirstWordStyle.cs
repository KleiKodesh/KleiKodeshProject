using DocDesign.Helpers;
using Microsoft.Office.Interop.Word;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Threading;
using WpfLib.ViewModels;

namespace DocDesign.Paragraphs
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

        readonly object _loadLock = new object();
        bool _loadInProgress = false;

        public FirstWordStyle()
        {
            // Style enumeration is deferred to ApplicationIdle by the View
        }

        public void DeferredInit()
        {
            if (Vsto.Application == null) return;

            // Ensure the style exists first (must be on UI/COM thread)
            CreateFirstWordStyle();

            RefreshStyles();
        }

        public RelayCommand CreateNewStyleCommand => new RelayCommand(CreateNewStyle);

        void CreateNewStyle()
        {
            if (Vsto.Application == null) return;

            string name = Interaction.InputBox(
                "הזן שם לסגנון התו החדש:",
                "צור סגנון חדש",
                "");

            if (string.IsNullOrWhiteSpace(name)) return;

            name = name.Trim();

            // Check if already exists
            foreach (Style s in Vsto.ActiveDocument.Styles)
            {
                try
                {
                    if (string.Equals(s.NameLocal, name, StringComparison.OrdinalIgnoreCase))
                    {
                        System.Windows.Forms.MessageBox.Show(
                            $"סגנון בשם '{name}' כבר קיים במסמך.",
                            "צור סגנון חדש",
                            System.Windows.Forms.MessageBoxButtons.OK,
                            System.Windows.Forms.MessageBoxIcon.Warning,
                            System.Windows.Forms.MessageBoxDefaultButton.Button1,
                            System.Windows.Forms.MessageBoxOptions.RightAlign | System.Windows.Forms.MessageBoxOptions.RtlReading);
                        return;
                    }
                }
                catch { }
            }

            // Create as character style based on מילה ראשונה — inherits all its presets
            Style baseStyle = CreateFirstWordStyle();
            Style newStyle = Vsto.ActiveDocument.Styles.Add(name, WdStyleType.wdStyleTypeCharacter);
            object baseStyleObj = baseStyle;
            newStyle.set_BaseStyle(ref baseStyleObj);

            // Refresh list and auto-select the new style
            RefreshStyles();
            SelectedStyle = newStyle.NameLocal;

            System.Windows.Forms.MessageBox.Show(
                $"הסגנון '{name}' נוצר בהצלחה וזמין לשימוש.\n\nכדי לערוך את עיצובו, השתמש בכלי עריכת הסגנונות המובנה של וורד').",
                "סגנון נוצר",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Information,
                System.Windows.Forms.MessageBoxDefaultButton.Button1,
                System.Windows.Forms.MessageBoxOptions.RightAlign | System.Windows.Forms.MessageBoxOptions.RtlReading);
        }

        int _lastSeenStylesCount = -1;
        string _lastSeenDocName;

        /// <summary>
        /// Two COM reads (Styles.Count and the document name) that say whether
        /// the style set could have changed since this list's last full refresh.
        /// The focus-return refresh gates on this. Its own stamp, not
        /// ParagraphsViewModel's: that one is also stamped by the styles-picker
        /// toggle, which does not refresh this list, so sharing it would let the
        /// picker silence a refresh this list still needed. A rename slips past
        /// the count; the ungated refreshes (pane shown) still catch it.
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

        public void RefreshStyles()
        {
            if (Vsto.Application == null) return;
            if (_loadInProgress) return;
            _loadInProgress = true;

            try
            {
                // COM must be accessed on the STA/UI thread — no Task.Run.
                var names = new List<string>();
                var doc = Vsto.ActiveDocument;
                if (doc != null)
                {
                    try
                    {
                        _lastSeenStylesCount = doc.Styles.Count;
                        _lastSeenDocName = doc.Name;
                    }
                    catch { }
                    foreach (Style s in doc.Styles)
                    {
                        try
                        {
                            if (s.Type == WdStyleType.wdStyleTypeCharacter && !s.BuiltIn)
                                names.Add(s.NameLocal);
                        }
                        catch { }
                    }
                }

                // Diff — only rebuild if the list actually changed
                bool changed = names.Count != Styles.Count
                            || !names.SequenceEqual(Styles);
                if (changed)
                {
                    var current = SelectedStyle;
                    Styles.Clear();
                    foreach (var name in names)
                        Styles.Add(name);
                    // Restore selection by value
                    if (!string.IsNullOrEmpty(current))
                        SelectedStyle = current;
                }
            }
            finally
            {
                _loadInProgress = false;
            }
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

        // NOTE: nothing calls this today. The pack URI below pointed at
        // WpfLib;component/Dictionaries/, a folder that does not exist -- the
        // dictionary lives in WpfLib/ThemedWindow/. It was only ever wrong
        // because no caller made it throw. Corrected rather than deleted; if
        // this really is dead, delete the method.
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
                Style = (System.Windows.Style)new System.Windows.ResourceDictionary { Source = new Uri("pack://application:,,,/WpfLib;component/themedwindow/themedwindowdictionary.xaml") }["ThemedToolWindowStyle"],
                Content = listView,
                SizeToContent = System.Windows.SizeToContent.WidthAndHeight,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen
            };

            listView.SelectionChanged += (s, _) =>
            {
                if (listView.SelectedItem != null)
                {
                    Interaction.SaveSetting("KleiKodesh", "Settings", "FirstWordStyle", listView.SelectedItem.ToString());
                    window.Close();
                }
            };

            window.ShowDialog();
        }


        public Style CreateFirstWordStyle()
        {
            string targetStyleName = Interaction.GetSetting("KleiKodesh", "Settings", "FirstWordStyle", "מילה ראשונה");
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
