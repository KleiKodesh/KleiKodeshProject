using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Markup;

// General Information about an assembly is controlled through the following
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle("WpfLib")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("WpfLib")]
[assembly: AssemblyCopyright("Copyright ©  2025")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("494b02c1-54f2-4244-9a90-23c9984970e7")]

// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version
//      Build Number
//      Revision
//
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

// ── One XML namespace for the whole library ──────────────────────────────────
//
// Consumers were writing four different prefixes over three clr-namespace
// strings, and spelling the same namespace both "conv" and "converters". With
// these, a XAML file declares WpfLib once:
//
//     xmlns:kk="http://schemas.kleikodesh.org/wpf"
//     <kk:UpDownTextBox/>  <kk:BoolToVisibilityConverter x:Key="BoolToVis"/>
//
// The clr-namespace form keeps working; this is additive.
[assembly: XmlnsDefinition("http://schemas.kleikodesh.org/wpf", "WpfLib")]
[assembly: XmlnsDefinition("http://schemas.kleikodesh.org/wpf", "WpfLib.Controls")]
[assembly: XmlnsDefinition("http://schemas.kleikodesh.org/wpf", "WpfLib.Converters")]
[assembly: XmlnsDefinition("http://schemas.kleikodesh.org/wpf", "WpfLib.AttachedProperties")]
[assembly: XmlnsDefinition("http://schemas.kleikodesh.org/wpf", "WpfLib.ViewModels")]
[assembly: XmlnsDefinition("http://schemas.kleikodesh.org/wpf", "WpfLib.Helpers")]
[assembly: XmlnsPrefix("http://schemas.kleikodesh.org/wpf", "kk")]
