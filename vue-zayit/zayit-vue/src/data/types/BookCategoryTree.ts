import type { Book } from './Book'

export interface Category {
    id: number
    parentId: number
    title: string
    path?: string
    level: number
    orderIndex: number
    books: Book[]
    children: Category[]
}

export interface TreeData {
    categoryTree: Category[]
    allBooks: Book[]
}
