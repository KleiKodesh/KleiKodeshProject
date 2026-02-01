using System;
using System.Collections.Generic;
using System.Linq;
using Zayit.Models;

namespace Zayit.Utils
{
    /// <summary>
    /// Calculates document order from flat tree structure using depth-first traversal.
    /// 
    /// The database returns a flat list of categories/documents with parent-child relationships
    /// via ParentId. This utility determines the correct display order by traversing the tree
    /// structure depth-first, maintaining hierarchical ordering.
    /// </summary>
    public class DocumentOrderCalculator
    {
        /// <summary>
        /// Calculate the correct document order using depth-first traversal of the category tree.
        /// </summary>
        /// <param name="flatCategories">Flat list of categories from database</param>
        /// <param name="flatBooks">Flat list of books from database</param>
        /// <returns>Ordered list of category/book IDs in depth-first order</returns>
        public static List<int> CalculateDocumentOrder(Category[] flatCategories, Book[] flatBooks)
        {
            var orderedIds = new List<int>();

            // Build a map of category by ID for quick lookup
            var categoryMap = flatCategories.ToDictionary(c => c.Id, c => c);

            // Find root categories (ParentId is 0 or null)
            var roots = flatCategories
                .Where(c => c.ParentId == 0 || c.ParentId == null)
                .OrderBy(c => c.Level)
                .ThenBy(c => c.Id)
                .ToList();

            // Traverse each root depth-first
            foreach (var root in roots)
            {
                TraverseDepthFirst(root, categoryMap, orderedIds);
            }

            return orderedIds;
        }

        /// <summary>
        /// Calculate document order and include book ordering within categories.
        /// </summary>
        /// <param name="flatCategories">Flat list of categories from database</param>
        /// <param name="flatBooks">Flat list of books from database</param>
        /// <returns>Ordered list of tuples (type: "category"|"book", id: int)</returns>
        public static List<(string Type, int Id)> CalculateDetailedDocumentOrder(Category[] flatCategories, Book[] flatBooks)
        {
            var orderedItems = new List<(string, int)>();

            // Build maps for quick lookup
            var categoryMap = flatCategories.ToDictionary(c => c.Id, c => c);
            var booksByCategory = flatBooks
                .GroupBy(b => b.CategoryId)
                .ToDictionary(g => g.Key, g => g.OrderBy(b => b.OrderIndex).ToList());

            // Find root categories
            var roots = flatCategories
                .Where(c => c.ParentId == 0 || c.ParentId == null)
                .OrderBy(c => c.Level)
                .ThenBy(c => c.Id)
                .ToList();

            // Traverse each root depth-first
            foreach (var root in roots)
            {
                TraverseDepthFirstDetailed(root, categoryMap, booksByCategory, orderedItems);
            }

            return orderedItems;
        }

        /// <summary>
        /// Get documents in the order they should be displayed using level-first depth traversal.
        /// </summary>
        /// <param name="flatCategories">Flat list of categories from database</param>
        /// <param name="flatBooks">Flat list of books from database</param>
        /// <returns>Ordered dictionary mapping hierarchy level to items at that level</returns>
        public static Dictionary<int, List<(int Id, string Title, int ParentId?)>> CalculateHierarchicalOrder(Category[] flatCategories, Book[] flatBooks)
        {
            var hierarchyMap = new Dictionary<int, List<(int, string, int?)>>();

            // Build category map
            var categoryMap = flatCategories.ToDictionary(c => c.Id, c => c);

            // Group categories by level
            foreach (var category in flatCategories)
            {
                if (!hierarchyMap.ContainsKey(category.Level))
                {
                    hierarchyMap[category.Level] = new List<(int, string, int?)>();
                }

                hierarchyMap[category.Level].Add((category.Id, category.Title, category.ParentId));
            }

            return hierarchyMap;
        }

        /// <summary>
        /// Perform depth-first traversal starting from a category node.
        /// </summary>
        private static void TraverseDepthFirst(Category node, Dictionary<int, Category> categoryMap, List<int> orderedIds)
        {
            // Add current category ID
            orderedIds.Add(node.Id);

            // Find and traverse children depth-first
            var children = categoryMap.Values
                .Where(c => c.ParentId == node.Id)
                .OrderBy(c => c.Id)
                .ToList();

            foreach (var child in children)
            {
                TraverseDepthFirst(child, categoryMap, orderedIds);
            }
        }

        /// <summary>
        /// Perform depth-first traversal with book details.
        /// </summary>
        private static void TraverseDepthFirstDetailed(
            Category node,
            Dictionary<int, Category> categoryMap,
            Dictionary<int, List<Book>> booksByCategory,
            List<(string Type, int Id)> orderedItems)
        {
            // Add current category
            orderedItems.Add(("category", node.Id));

            // Add books in this category (ordered by OrderIndex)
            if (booksByCategory.ContainsKey(node.Id))
            {
                foreach (var book in booksByCategory[node.Id])
                {
                    orderedItems.Add(("book", book.Id));
                }
            }

            // Traverse child categories depth-first
            var children = categoryMap.Values
                .Where(c => c.ParentId == node.Id)
                .OrderBy(c => c.Id)
                .ToList();

            foreach (var child in children)
            {
                TraverseDepthFirstDetailed(child, categoryMap, booksByCategory, orderedItems);
            }
        }
    }
}
