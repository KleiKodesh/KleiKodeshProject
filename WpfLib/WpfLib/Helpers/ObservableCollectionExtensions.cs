using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace WpfLib.Helpers
{
    public static class ObservableCollectionExtensions
    {
        public static void RemoveAll<T>(this ObservableCollection<T> collection, Func<T, bool> predicate)
        {
            var itemsToRemove = collection.Where(predicate).ToList(); // מוודא שאין מעבר על האוסף עצמו
            foreach (var item in itemsToRemove)
            {
                collection.Remove(item);
            }
        }
    }
}
