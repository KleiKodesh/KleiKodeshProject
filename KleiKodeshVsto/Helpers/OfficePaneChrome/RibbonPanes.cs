using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Office = Microsoft.Office.Core;

namespace OfficePaneChrome
{
    /// <summary>
    /// The add-in's panes as its ribbon describes them: every ribbon &lt;button tag="pane"&gt;, with label, image and
    /// visibility from the ribbon object's OWN callbacks (getLabel / getImage / getVisible), opened by its onAction.
    /// The XML is parsed once; labels and visibility are asked fresh each time.
    /// </summary>
    public sealed class RibbonPanes
    {
        /// <summary>A pane button: <see cref="Tip"/> / <see cref="SuperTip"/> are the ribbon's screentip and supertip.</summary>
        public sealed class Entry { public string Id, Title, Tip, SuperTip; public Image Icon; public Action Open; }

        sealed class Button { public string Id, Label, GetLabel, GetImage, GetVisible, OnAction, Screentip, GetScreentip, Supertip, GetSupertip; public bool Hidden; }

        readonly Func<object> _create;
        object _ribbon;
        List<Button> _buttons;
        readonly Dictionary<string, Image> _icons = new Dictionary<string, Image>();

        /// <param name="createRibbon">Returns a ribbon object: IRibbonExtensibility (for its XML) plus its callbacks.</param>
        public RibbonPanes(Func<object> createRibbon) { _create = createRibbon; }

        List<Button> Buttons()
        {
            if (_buttons != null) return _buttons;
            _ribbon = _create();
            string xml = (_ribbon as Office.IRibbonExtensibility)?.GetCustomUI("");
            _buttons = string.IsNullOrEmpty(xml) ? new List<Button>() : XDocument.Parse(xml).Descendants()
                .Where(e => e.Name.LocalName == "button" && (string)e.Attribute("tag") == "pane" && e.Attribute("id") != null)
                .GroupBy(e => (string)e.Attribute("id")).Select(g => g.First())
                .Select(e => new Button
                {
                    Id = (string)e.Attribute("id"), Label = (string)e.Attribute("label"),
                    GetLabel = (string)e.Attribute("getLabel"), GetImage = (string)e.Attribute("getImage"),
                    GetVisible = (string)e.Attribute("getVisible"), OnAction = (string)e.Attribute("onAction"),
                    Screentip = (string)e.Attribute("screentip"), GetScreentip = (string)e.Attribute("getScreentip"),
                    Supertip = (string)e.Attribute("supertip"), GetSupertip = (string)e.Attribute("getSupertip"),
                    Hidden = (string)e.Attribute("visible") == "false",
                }).ToList();
            return _buttons;
        }

        public Image IconFor(string id)
        {
            if (id == null) return null;
            if (_icons.TryGetValue(id, out var icon)) return icon;
            var b = Buttons().FirstOrDefault(x => x.Id == id);
            return _icons[id] = b == null ? null : Invoke<Image>(b.GetImage, new Control(b.Id));
        }

        /// <summary>Visible pane entries, in ribbon order, as the ribbon describes them right now.</summary>
        public List<Entry> Entries()
        {
            var list = new List<Entry>();
            foreach (var b in Buttons())
            {
                var control = new Control(b.Id);
                if (b.Hidden || Invoke<bool?>(b.GetVisible, control) == false) continue;
                list.Add(new Entry
                {
                    Id = b.Id,
                    Title = Invoke<string>(b.GetLabel, control) ?? b.Label ?? b.Id,
                    Tip = Invoke<string>(b.GetScreentip, control) ?? b.Screentip,
                    SuperTip = Invoke<string>(b.GetSupertip, control) ?? b.Supertip,
                    Icon = IconFor(b.Id),
                    Open = () => Invoke<object>(b.OnAction, control),
                });
            }
            return list;
        }

        /// <summary>Calls ribbon.callback(IRibbonControl) by name, as Office would; default(T) if missing or failing.</summary>
        T Invoke<T>(string callback, Office.IRibbonControl control)
        {
            if (_ribbon == null || string.IsNullOrEmpty(callback)) return default(T);
            var method = _ribbon.GetType().GetMethod(callback, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { typeof(Office.IRibbonControl) }, null);
            if (method == null) { SidePanes.Log("ribbon callback missing: " + callback); return default(T); }
            try { return method.Invoke(_ribbon, new object[] { control }) is T t ? t : default(T); }
            catch (TargetInvocationException ex) { SidePanes.Log(callback + " threw: " + ex.InnerException?.Message); return default(T); }
        }

        sealed class Control : Office.IRibbonControl
        {
            public Control(string id) { Id = id; }
            public string Id { get; }
            public string Tag => "pane";
            public object Context => null;
        }
    }
}
