using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using System.Threading;

namespace WpfLib.ViewModels
{
    public class TreeItemBase<T> : ViewModelBase where T : TreeItemBase<T>
    {
        string _name;
        ObservableCollection<T> _items;

        [JsonIgnore] public T Parent { get; set; }
        public virtual string Name { get => _name; set => SetProperty(ref _name, value); }
        public ObservableCollection<T> Items { get => _items; set => SetProperty(ref _items, value); }

        public override string ToString() => Name;

        public void AddChild(T item)
        {
            if (Items == null)
                Items = new ObservableCollection<T>();
            Items.Add(item);
            item.Parent = (T)this;
        }

        public IEnumerable<T> EnumerateItems()
        {
            if (_items == null) yield break;

            foreach (var item in _items)
            {
                yield return item;
                foreach (var child in item.EnumerateItems())
                    yield return child;
            }
        }
    }

}
