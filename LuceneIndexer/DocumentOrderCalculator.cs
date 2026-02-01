using System.Collections.Generic;
using System.Linq;

namespace LuceneIndexer
{
    /// <summary>
    /// Calculates document order from flat tree structure using depth-first traversal.
    /// </summary>
    public class DocumentOrderCalculator
    {
        /// <summary>
        /// Calculate the correct book order using depth-first traversal of the category tree.
        /// Returns only book IDs ordered by their position in the category hierarchy.
        /// </summary>
        public static List<int> CalculateDocumentOrder(Category[] flatCategories, Book[] flatBooks)
        {
            var orderedBookIds = new List<int>();

            // Build the hierarchical tree structure
            var categoryTree = BuildCategoryTree(flatCategories);

            // Build book lookup by category
            var booksByCategory = flatBooks
                .GroupBy(b => b.CategoryId)
                .ToDictionary(g => g.Key, g => g.OrderBy(b => b.OrderIndex).ToList());

            // Find root categories (ParentId is 0 or null) and sort them
            var roots = categoryTree.Values
                .Where(c => c.ParentId == 0 || c.ParentId == null)
                .OrderBy(c => c.OrderIndex)
                .ThenBy(c => c.Id)
                .ToList();

            // Traverse each root depth-first
            foreach (var root in roots)
            {
                TraverseDepthFirst(root, booksByCategory, orderedBookIds);
            }

            return orderedBookIds;
        }

        /// <summary>
        /// Build hierarchical tree structure from flat category list.
        /// </summary>
        private static Dictionary<int, Category> BuildCategoryTree(Category[] flatCategories)
        {
            var categoryMap = flatCategories.ToDictionary(c => c.Id, c => c);

            // Build parent-child relationships
            foreach (var category in flatCategories)
            {
                category.Children = new List<Category>();
            }

            foreach (var category in flatCategories)
            {
                if (category.ParentId.HasValue && category.ParentId.Value != 0)
                {
                    if (categoryMap.ContainsKey(category.ParentId.Value))
                    {
                        categoryMap[category.ParentId.Value].Children.Add(category);
                    }
                }
            }

            // Sort children by orderIndex within each category
            foreach (var category in flatCategories)
            {
                category.Children = category.Children
                    .OrderBy(c => c.OrderIndex)
                    .ThenBy(c => c.Id)
                    .ToList();
            }

            return categoryMap;
        }

        /// <summary>
        /// Depth-first traversal: process books in current category, then recurse to children.
        /// </summary>
        private static void TraverseDepthFirst(
            Category node,
            Dictionary<int, List<Book>> booksByCategory,
            List<int> orderedBookIds)
        {
            // First, add all books in the current category (sorted by their orderIndex)
            if (booksByCategory.ContainsKey(node.Id))
            {
                foreach (var book in booksByCategory[node.Id])
                {
                    orderedBookIds.Add(book.Id);
                }
            }

            // Then, recursively traverse child categories in order
            // Children are already sorted by orderIndex in BuildCategoryTree
            foreach (var child in node.Children)
            {
                TraverseDepthFirst(child, booksByCategory, orderedBookIds);
            }
        }

        /// <summary>
        /// Calculate detailed document order including both categories and books.
        /// Useful for debugging or UI display.
        /// </summary>
        public static List<(string Type, int Id)> CalculateDetailedDocumentOrder(
            Category[] flatCategories,
            Book[] flatBooks)
        {
            var orderedItems = new List<(string, int)>();

            // Build the hierarchical tree structure
            var categoryTree = BuildCategoryTree(flatCategories);

            // Build book lookup by category
            var booksByCategory = flatBooks
                .GroupBy(b => b.CategoryId)
                .ToDictionary(g => g.Key, g => g.OrderBy(b => b.OrderIndex).ToList());

            // Find root categories and sort them
            var roots = categoryTree.Values
                .Where(c => c.ParentId == 0 || c.ParentId == null)
                .OrderBy(c => c.OrderIndex)
                .ThenBy(c => c.Id)
                .ToList();

            // Traverse each root depth-first
            foreach (var root in roots)
            {
                TraverseDepthFirstDetailed(root, booksByCategory, orderedItems);
            }

            return orderedItems;
        }

        /// <summary>
        /// Depth-first traversal with both categories and books for detailed output.
        /// </summary>
        private static void TraverseDepthFirstDetailed(
            Category node,
            Dictionary<int, List<Book>> booksByCategory,
            List<(string Type, int Id)> orderedItems)
        {
            // Add current category
            orderedItems.Add(("category", node.Id));

            // Add books in this category (sorted by orderIndex)
            if (booksByCategory.ContainsKey(node.Id))
            {
                foreach (var book in booksByCategory[node.Id])
                {
                    orderedItems.Add(("book", book.Id));
                }
            }

            // Recursively traverse child categories
            foreach (var child in node.Children)
            {
                TraverseDepthFirstDetailed(child, booksByCategory, orderedItems);
            }
        }
    }
}