import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { Category } from '@/data/types/BookCategoryTree'
import type { Book } from '@/data/types/Book'
import { dbService } from '@/data/services/dbService'

export const useCategoryTreeStore = defineStore('categoryTree', () => {
    const isLoading = ref(true);
    const error = ref<string | null>(null);
    const categoryTree = ref<Category[]>([])
    const allBooks = ref<Book[]>([])

    // Start loading immediately (dbService will wait for database to be ready)
    buildTree();

    async function buildTree() {
        try {
            error.value = null;
            const { categoriesFlat: categories, booksFlat: books } = await dbService.getTree()

            // Helper function to find category period by traversing up
            function findCategoryPeriod(categoryId: number, categoryMap: Map<number, Category>): string | null {
                const visited = new Set<number>()
                let rootCategory: Category | null = null

                function traverse(id: number): string | null {
                    if (visited.has(id)) return null
                    visited.add(id)

                    const category = categoryMap.get(id)
                    if (!category) return null

                    // Track root category (first-tier category)
                    if (!category.parentId || category.parentId === 0) {
                        rootCategory = category
                    }

                    const title = category.title.toLowerCase()

                    // Check for meaningful periods (priority order)
                    if (title.includes('גאונים')) {
                        return 'גאונים'
                    } else if (title.includes('ראשונים')) {
                        return 'ראשונים'
                    } else if (title.includes('אחרונים')) {
                        return 'אחרונים'
                    }

                    // Traverse up to parent
                    if (category.parentId) {
                        return traverse(category.parentId)
                    }

                    return null
                }

                const period = traverse(categoryId)

                // If no meaningful period found, return root category title
                if (!period && rootCategory) {
                    return rootCategory.title
                }

                return period
            }

            // Group books by categoryId and sort them by orderIndex
            const booksByCategory = new Map<number, Book[]>()
            for (const book of books) {
                const categoryBooks = booksByCategory.get(book.categoryId)
                if (categoryBooks) {
                    categoryBooks.push(book)
                } else {
                    booksByCategory.set(book.categoryId, [book])
                }
            }

            // Single pass: initialize categories and build hierarchy
            const categoryMap = new Map<number, Category>()
            const roots: Category[] = []

            for (const cat of categories) {
                cat.children = []
                // Get books for this category and sort by orderIndex
                const categoryBooks = booksByCategory.get(cat.id) || []
                categoryBooks.sort((a, b) => a.orderIndex - b.orderIndex)
                cat.books = categoryBooks
                categoryMap.set(cat.id, cat)

                if (!cat.parentId || cat.parentId === 0) {
                    roots.push(cat)
                } else {
                    const parent = categoryMap.get(cat.parentId)
                    if (parent) {
                        parent.children.push(cat)
                    }
                }
            }

            // Assign periods to all books
            for (const book of books) {
                book.period = findCategoryPeriod(book.categoryId, categoryMap) || 'אחר'
            }

            // Sort children within each category by orderIndex
            function sortChildren(category: Category) {
                category.children.sort((a, b) => a.orderIndex - b.orderIndex)
                for (const child of category.children) {
                    sortChildren(child)
                }
            }

            // Sort root categories and recursively sort all children
            roots.sort((a, b) => a.orderIndex - b.orderIndex)
            for (const root of roots) {
                sortChildren(root)
            }

            // Build paths for categories and books
            function buildPaths(category: Category, parentPath: string = '') {
                const currentPath = parentPath ? `${parentPath} > ${category.title}` : category.title

                // Assign path to all books in this category
                for (const book of category.books) {
                    book.path = currentPath
                }

                // Recursively build paths for children
                for (const child of category.children) {
                    buildPaths(child, currentPath)
                }
            }

            for (const root of roots) {
                buildPaths(root)
            }

            allBooks.value = books
            categoryTree.value = roots
            isLoading.value = false;
        } catch (err) {
            const errorMessage = err instanceof Error ? err.message : 'Unknown error';
            console.error('❌ Failed to build category tree:', err);
            error.value = errorMessage;
            isLoading.value = false;
        }
    }

    return {
        categoryTree,
        allBooks,
        isLoading,
        error
    }
})
