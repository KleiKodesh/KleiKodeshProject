using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WpfLib;

namespace WebSitesLib
{
    public class WebSitesViewModel : ViewModelBase
    {
        // ── Constants ────────────────────────────────────────────────────────
        private const string FileName = "websites.json";



        // ── State ────────────────────────────────────────────────────────────
        private readonly string _filePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);

        // ── Collections ──────────────────────────────────────────────────────
        /// <summary>Full list of all sites (visible and hidden), bound to WhiteListDialog.</summary>
        public ObservableCollection<WebAdressModel> Adresses { get; }
            = new ObservableCollection<WebAdressModel>();

        /// <summary>Filtered list shown in the address bar ComboBox.</summary>
        public ObservableCollection<WebAdressModel> VisibleAdresses
        {
            get
            {
                var filtered = new ObservableCollection<WebAdressModel>();
                foreach (var a in Adresses)
                    if (a.IsVisible)
                        filtered.Add(a);
                return filtered;
            }
        }

        // ── Commands ─────────────────────────────────────────────────────────
        /// <summary>
        /// Opens the whitelist editor.
        /// Bind CommandParameter="{Binding ElementName=Root}" in XAML so the
        /// dialog can inherit the UserControl's Background/Foreground brushes.
        /// </summary>
        public ICommand ShowWhiteListCommand { get; }

        // ── Constructor ──────────────────────────────────────────────────────
        public WebSitesViewModel()
        {
            //ShowWhiteListCommand = new RelayCommand(p => ShowWhiteListDialog(p));
            Load();
        }

        // ── WhiteList dialog ─────────────────────────────────────────────────
        private void ShowWhiteListDialog(object parameter)
        {
            // Background and Foreground are defined on Control, not FrameworkElement.
            // The CommandParameter is the UserControl (x:Name="Root") which IS a Control.
            var control = parameter as Control;

            var dialog = new WhiteListDialog(this)
            {
                Owner = Application.Current.MainWindow
            };

            if (control != null)
            {
                if (control.Background != null)
                    dialog.Background = control.Background;
                if (control.Foreground != null)
                    dialog.Foreground = control.Foreground;
            }

            // ShowDialog returns true only when OK_Button_Click calls DialogResult = true.
            // Currently the dialog just closes on OK, so we save regardless.
            dialog.ShowDialog();
            Save();
            OnPropertyChanged(nameof(VisibleAdresses));
        }

        // ── Persistence ──────────────────────────────────────────────────────
        private void Load()
        {
            string json;

            if (File.Exists(_filePath))
            {
                json = File.ReadAllText(_filePath);
            }
            else
            {
                // First run: seed from the embedded default list and write it out
                //json = DefaultJson;
                //File.WriteAllText(_filePath, DefaultJson);
            }

            ObservableCollection<WebAdressModel> items = null;

            try
            {
                //items = JsonSerializer.Deserialize<ObservableCollection<WebAdressModel>>(json);
            }
            catch (JsonException)
            {
                // Corrupted file — fall back to defaults
                //items = JsonSerializer.Deserialize<ObservableCollection<WebAdressModel>>(DefaultJson);
            }

            if (items == null) return;

            foreach (var item in items)
                Adresses.Add(item);
        }

        public void Save()
        {
            var json = JsonSerializer.Serialize(
                Adresses,
                new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(_filePath, json);
        }
    }
}
