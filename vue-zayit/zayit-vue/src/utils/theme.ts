/**
 * Theme utility for managing dark/light mode
 */

export function toggleTheme(): void {
    const isDark = document.documentElement.classList.contains('dark')

    if (isDark) {
        document.documentElement.classList.remove('dark')
        localStorage.setItem('theme', 'light')
    } else {
        document.documentElement.classList.add('dark')
        localStorage.setItem('theme', 'dark')
    }
}

export function initTheme(): void {
    const savedTheme = localStorage.getItem('theme')
    if (savedTheme === 'dark') {
        document.documentElement.classList.add('dark')
    }
}

export function isDarkTheme(): boolean {
    return document.documentElement.classList.contains('dark')
}
