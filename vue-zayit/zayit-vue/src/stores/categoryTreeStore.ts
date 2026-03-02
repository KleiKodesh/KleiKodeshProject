import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { Category } from '../types/BookCategoryTree'
import type { Book } from '../types/Book'
import { dbService } from '../services/dbService'

export const useCategoryTreeStore = defineStore('categoryTree', () => {
    const isLoading = ref(true);
    const error = ref<string | null>(null);
    const categoryTree = ref<Category[]>([])
    const allBooks = ref<Book[]>([])

    // Initialize tree loading after store setup
    if (categoryTree.value.length === 0) {
        buildTree().catch(error => {
            console.error('Failed to build category tree:', error);
            isLoading.value = false;
        });
    }

    async function buildTree() {
        try {
            error.value = null;
            const { categoriesFlat: categories, booksFlat: books } = await dbService.getTree()

            // Helper function to find category period by traversing up
            function findCategoryPeriod(categoryId: number, categoryMap: Map<number, Category>): string | null {
                const visited = new Set<number>()

                function traverse(id: number): string | null {
                    if (visited.has(id)) return null
                    visited.add(id)

                    const category = categoryMap.get(id)
                    if (!category) return null

                    const title = category.title.toLowerCase()

                    // Check for major arch-categories (in priority order)
                    if (title.includes('תנ"ך') || title.includes('תנך')) {
                        return 'תנ"ך'
                    } else if (title.includes('משנה') && !title.includes('משנה תורה')) {
                        return 'ספרות חז"ל'
                    } else if (title.includes('תלמוד')) {
                        return 'ספרות חז"ל'
                    } else if (title.includes('מדרש')) {
                        return 'ספרות חז"ל'
                    } else if (title.includes('תוספתא')) {
                        return 'ספרות חז"ל'
                    } else if (title.includes('גאונים')) {
                        return 'גאונים'
                    } else if (title.includes('ראשונים')) {
                        return 'ראשונים'
                    } else if (title.includes('אחרונים')) {
                        return 'אחרונים'
                    } else if (title.includes('קבלה')) {
                        return 'קבלה'
                    } else if (title.includes('מוסר') || title.includes('חסידות')) {
                        return 'מוסר וחסידות'
                    } else if (title.includes('הלכה') || title.includes('משנה תורה') || title.includes('שולחן ערוך')) {
                        return 'הלכה'
                    } else if (title.includes('אחר')) {
                        return 'אחר'
                    }

                    // Traverse up to parent
                    if (category.parentId) {
                        return traverse(category.parentId)
                    }

                    return null
                }

                return traverse(categoryId)
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
            throw err; // Re-throw so caller knows it failed
        }
    }

    return {
        categoryTree,
        allBooks,
        isLoading,
        error
    }
})