using System.Windows;

namespace WpfLib.AttachedProperties
{
    /// <summary>
    /// Placeholder text for an input control - the grey prompt shown while the
    /// control is empty.
    ///
    /// This is a value, not a behaviour: it stores a string and nothing else.
    /// The drawing and the when-to-show logic live in the control templates in
    /// Themes, where they belong, because only the template knows where its own
    /// text sits and when it is empty.
    ///
    ///     &lt;ComboBox IsEditable="True" ap:Placeholder.Text="Pick a style"/&gt;
    ///     &lt;TextBox ap:Placeholder.Text="Search..."/&gt;
    ///
    /// The templates read it with a TemplateBinding, so a control that is not
    /// styled by this library simply ignores it.
    ///
    /// It replaces a per-pane behaviour that reached into the editable
    /// TextBox's own ControlTemplate and inserted an overlay into whatever Grid
    /// it found at the root. That worked against one hand-written template and
    /// broke silently against this library's, whose TextBox template is rooted
    /// in a Border - the walk returned null, no overlay was ever created, and
    /// every update call after that returned early. A template-internal
    /// dependency is not something a pane can be expected to keep working.
    /// </summary>
    public static class Placeholder
    {
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.RegisterAttached(
                "Text",
                typeof(string),
                typeof(Placeholder),
                // Deliberately NOT inheriting. Every template reads the value off
                // the control it is templating, with a TemplateBinding, so
                // inheritance would buy nothing and would only widen the blast
                // radius: a placeholder set on a container would reach every
                // input inside it. Scoped to the control it is set on.
                new PropertyMetadata(null));

        public static string GetText(DependencyObject obj) => (string)obj.GetValue(TextProperty);
        public static void SetText(DependencyObject obj, string value) => obj.SetValue(TextProperty, value);
    }
}
