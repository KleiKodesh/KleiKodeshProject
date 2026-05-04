using FtsLibDemo.Services;
using FtsLibDemo.ViewModels;
using System.Windows;

namespace FtsLibDemo
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var settings      = new SettingsService();
            var indexService  = new IndexService();
            var searchService = new SearchService(indexService);
            var htmlService   = new ResultsHtmlService();
            var viewModel     = new MainViewModel(settings, indexService, searchService, htmlService);

            var window = new MainWindow { DataContext = viewModel };
            window.Show();
        }
    }
}
