using FtsLibDemo.ViewModels;
using System.Collections.Generic;

namespace FtsLibDemo.Services
{
    /// <summary>
    /// Renders a list of search result items as a self-contained HTML string
    /// suitable for display in a WebView2 control.
    /// </summary>
    public interface IResultsHtmlService
    {
        string Render(IReadOnlyList<SearchResultItem> items, string query);
        string RenderEmpty(string message);
    }
}
