using System.Collections.Generic;
using System.Linq;

namespace WpfLib.ViewModels
{
    public class CheckedTreeItemBase<T> : TreeItemBase<T> where T : CheckedTreeItemBase<T>
    {
        bool? _isChecked = false;
        public bool? IsChecked { get => _isChecked; set => SetCheckedValue(value, true); }

        public void SetCheckedValue(bool? isChecked, bool updateChildren)
        {
            if (SetProperty(ref _isChecked, isChecked, nameof(IsChecked)))
            {
                if (updateChildren && Items != null)
                {
                    foreach (var child in Items)
                    {
                        if (child.IsChecked != isChecked)
                            child.IsChecked = isChecked == true;
                    }
                }

                if (Parent != null)
                {
                    var siblings = Parent.Items;
                    var parentCheckedValue = siblings.All(c => c.IsChecked == true) ? true :
                                             siblings.All(c => c.IsChecked == false) ? (bool?)false : null;
                    Parent.SetCheckedValue(parentCheckedValue, false);
                }
            }
        }

        public IEnumerable<T> EnumerateCheckedItems()
        {
            if (Items != null)
            {
                foreach (var child in Items)
                {
                    if (child.IsChecked == true)
                        yield return child;

                    foreach (var item in child.EnumerateCheckedItems())
                        yield return item;
                }
            }
        }
    }
}

///sample usage
//public class MyCheckedItem : TreeItemBase<MyCheckedItem>
//{
//    // Custom properties if needed
//}

