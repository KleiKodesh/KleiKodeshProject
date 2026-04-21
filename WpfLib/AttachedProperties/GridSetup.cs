using System;
using System.Windows.Controls;
using System.Windows;

namespace WpfLib.AttachedProperties
{
    public class GridSetup
    {
        private static GridLengthConverter _gridLengthConverter = new GridLengthConverter();

        public static readonly DependencyProperty ColumnsProperty =
           DependencyProperty.RegisterAttached(
               "Columns",
               typeof(string),
               typeof(GridSetup),
               new PropertyMetadata(null, OnColumnsChanged));


        public static readonly DependencyProperty RowsProperty =
            DependencyProperty.RegisterAttached(
                "Rows",
                typeof(string),
                typeof(GridSetup),
                new PropertyMetadata(null, OnRowsChanged));

        public static void SetColumns(DependencyObject element, string value) =>
            element.SetValue(ColumnsProperty, value);

        public static string GetColumns(DependencyObject element) =>
            (string)element.GetValue(ColumnsProperty);

        public static void SetRows(DependencyObject element, string value) =>
          element.SetValue(RowsProperty, value);

        public static string GetRows(DependencyObject element) =>
            (string)element.GetValue(RowsProperty);

        private static void OnColumnsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Grid grid && e.NewValue is string definition)
            {
                grid.ColumnDefinitions.Clear();
                foreach (var item in definition.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = ParseGridLength(item.Trim()) });
            }
        }

        private static void OnRowsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Grid grid && e.NewValue is string definition)
            {
                grid.RowDefinitions.Clear();
                foreach (var item in definition.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    grid.RowDefinitions.Add(new RowDefinition { Height = ParseGridLength(item.Trim()) });
            }
        }

        static GridLength ParseGridLength(string value) =>
             (GridLength)_gridLengthConverter.ConvertFromString(value);
    }
}
