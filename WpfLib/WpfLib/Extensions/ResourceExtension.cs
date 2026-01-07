using System;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using WpfLib.Helpers;

namespace WpfLib.Extensions
{
    public class ResourceExtension : MarkupExtension
    {
        public string Path { get; set; }

        public ResourceExtension(string path)
        {
            Path = path;
        }

        public ResourceExtension() { }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            if (string.IsNullOrEmpty(Path))
                throw new InvalidOperationException("Name Cannot Be Blank");

            Path = System.IO.Path.Combine(ResourcesHelper.ResourcesDirectory, Path);

            var binding = new Binding
            {
                Source = this,
                Path = new System.Windows.PropertyPath(nameof(Path)),
                Mode = BindingMode.OneWay
            };

            return binding;
        }
    }
}
