// Recent Searches Manager - Persistent search history using localStorage
class RecentSearches {
    constructor() {
        this.maxHistorySize = 10; // Maximum number of recent searches to keep
        this.storageKeys = {
            find: 'regexfind_recent_searches',
            replace: 'regexfind_recent_replacements'
        };
    }

    // Get recent searches for a specific type (find/replace)
    getRecentSearches(type) {
        try {
            const key = this.storageKeys[type];
            const stored = localStorage.getItem(key);
            return stored ? JSON.parse(stored) : [];
        } catch (error) {
            console.warn('Failed to load recent searches:', error);
            return [];
        }
    }

    // Add a new search to history
    addToHistory(type, searchText) {
        if (!searchText || !searchText.trim()) {
            return; // Don't save empty searches
        }

        try {
            const key = this.storageKeys[type];
            let history = this.getRecentSearches(type);
            
            // Remove if already exists (to move to top)
            history = history.filter(item => item !== searchText.trim());
            
            // Add to beginning
            history.unshift(searchText.trim());
            
            // Limit size
            if (history.length > this.maxHistorySize) {
                history = history.slice(0, this.maxHistorySize);
            }
            
            // Save back to localStorage
            localStorage.setItem(key, JSON.stringify(history));
            
        } catch (error) {
            console.warn('Failed to save search to history:', error);
        }
    }

    // Clear all history for a type
    clearHistory(type) {
        try {
            const key = this.storageKeys[type];
            localStorage.removeItem(key);
        } catch (error) {
            console.warn('Failed to clear search history:', error);
        }
    }

    // Clear all history
    clearAllHistory() {
        this.clearHistory('find');
        this.clearHistory('replace');
    }
}

// Export for use in other modules
window.RecentSearches = RecentSearches;