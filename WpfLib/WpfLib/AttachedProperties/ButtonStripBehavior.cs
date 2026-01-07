namespace WpfLib.AttachedProperties
{
    using System.Windows;
    using System.Windows.Controls;

    public static class GridStripBehavior
    {
        public static readonly DependencyProperty GridStripLayoutProperty =
            DependencyProperty.RegisterAttached(
                "GridStripLayout",
                typeof(bool),
                typeof(GridStripBehavior),
                new PropertyMetadata(false, OnGridStripLayoutChanged));

        public static bool GetGridStripLayout(DependencyObject obj)
        {
            return (bool)obj.GetValue(GridStripLayoutProperty);
        }

        public static void SetGridStripLayout(DependencyObject obj, bool value)
        {
            obj.SetValue(GridStripLayoutProperty, value);
        }

        private static void OnGridStripLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Grid grid)
            {
                if ((bool)e.NewValue)
                {
                    grid.Loaded += Grid_Loaded;
                    grid.LayoutUpdated += Grid_VisualChildrenChanged;
                    UpdateLayoutColumns(grid);
                }
                else
                {
                    grid.Loaded -= Grid_Loaded;
                    grid.LayoutUpdated -= Grid_VisualChildrenChanged;
                }
            }
        }

        private static void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Grid grid)
            {
                UpdateLayoutColumns(grid);
            }
        }

        private static void Grid_VisualChildrenChanged(object sender, System.EventArgs e)
        {
            if (sender is Grid grid)
            {
                UpdateLayoutColumns(grid);
            }
        }

        private static void UpdateLayoutColumns(Grid grid)
        {
            grid.ColumnDefinitions.Clear();

            int childrenCount = grid.Children.Count;
            if (childrenCount == 0)
                return;

            for (int i = 0; i < childrenCount * 2 - 1; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = (i % 2 == 0) ? GridLength.Auto : new GridLength(1, GridUnitType.Star)
                });
            }

            for (int i = 0, j = 0; i < childrenCount; i++, j += 2)
            {
                Grid.SetColumn(grid.Children[i], j);
            }
        }
    }

}
