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
        const { categoriesFlat: categories, booksFlat: books } = await dbManager.getTree()

        // Group books by categoryId
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
            cat.books = booksByCategory.get(cat.id) || []
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
    }

    return {
        categoryTree,
        allBooks,
        isLoading
    }
})