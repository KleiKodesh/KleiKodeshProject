import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { Category } from '../types/BookCategoryTree'
import type { Book } from '../types/Book'
import { dbManager } from '../data/dbManager'

export const useCategoryTreeStore = defineStore('categoryTree', () => {
    const isLoading = ref(true);
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
            const { categoriesFlat: categories, booksFlat: books } = await dbManager.getTree()

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
        } catch (error) {
            isLoading.value = false;
        }
    }

    return {
        categoryTree,
        allBooks,
        isLoading
    }
})