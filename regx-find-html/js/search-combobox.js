// Search Combobox - Custom combobox with recent searches functionality
class SearchCombobox extends HTMLElement {
    constructor() {
        super();
        this.recentSearches = new RecentSearches();
        this.attachShadow({ mode: 'open' });
        
        this.state = {
            isOpen: false,
            selectedValue: '',
            filteredOptions: [],
            focusedIndex: -1,
            searchType: 'find' // 'find' or 'replace'
        };
    }

    static get observedAttributes() {
        return ['type', 'placeholder', 'value', 'search-type'];
    }

    connectedCallback() {
        this.state.searchType = this.getAttribute('search-type') || 'find';
        this.render();
        this.setupEventListeners();
        this.loadRecentSearches();
    }

    attributeChangedCallback(name, oldValue, newValue) {
        if (oldValue !== newValue && this.shadowRoot) {
            if (name === 'search-type') {
                this.state.searchType = newValue || 'find';
                this.loadRecentSearches();
            } else if (name === 'value') {
                this.setValue(newValue);
            } else {
                this.render();
            }
        }
    }

    render() {
        const placeholder = this.getAttribute('placeholder') || '';

        this.shadowRoot.innerHTML = `
            <style>
                :host {
                    position: relative;
                    display: flex;
                    align-items: center;
                    flex: 1;
                }

                .search-input {
                    flex: 1;
                    background: var(--primary-background-color, #ffffff);
                    color: var(--text-color, #000000);
                    border: none;
                    border-radius: 0;
                    padding: 6px 8px 6px 30px;
                    font-size: 14px;
                    min-width: 0;
                    font-family: inherit;
                }

                .search-input:focus {
                    outline: none;
                }

                .dropdown-toggle {
                    position: absolute;
                    left: 5px;
                    background: none;
                    border: none;
                    color: var(--text-color, #000000);
                    cursor: pointer;
                    padding: 4px;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    border-radius: 2px;
                    transition: background-color 0.2s ease, transform 0.2s ease;
                    opacity: 0.7;
                }

                .dropdown-toggle:hover {
                    background: var(--hover-background-color, rgba(0, 0, 0, 0.1));
                    opacity: 1;
                }

                :host([open]) .dropdown-toggle {
                    transform: rotate(180deg);
                    opacity: 1;
                }

                .search-dropdown {
                    position: absolute;
                    top: 100%;
                    left: 0;
                    right: 0;
                    z-index: 1000;
                    background: var(--primary-background-color, #ffffff);
                    border: 2px solid var(--border-color, #cccccc);
                    border-radius: var(--border-radius, 4px);
                    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
                    max-height: 200px;
                    overflow: hidden;
                    margin-top: 2px;
                    display: none;
                }

                :host([open]) .search-dropdown {
                    display: block;
                }

                .dropdown-options {
                    max-height: 200px;
                    overflow-y: auto;
                    padding: 2px 0;
                }

                .search-option {
                    padding: 6px 12px;
                    cursor: pointer;
                    color: var(--text-color, #000000);
                    background: var(--primary-background-color, #ffffff);
                    transition: background-color 0.1s ease;
                    font-size: 14px;
                    white-space: nowrap;
                    overflow: hidden;
                    text-overflow: ellipsis;
                    display: flex;
                    align-items: center;
                    justify-content: space-between;
                }

                .search-option:hover,
                .search-option.focused {
                    background: var(--hover-background-color, rgba(0, 0, 0, 0.1));
                }

                .search-text {
                    flex: 1;
                    overflow: hidden;
                    text-overflow: ellipsis;
                }

                .remove-btn {
                    background: none;
                    border: none;
                    color: var(--text-secondary, #666666);
                    cursor: pointer;
                    padding: 2px 4px;
                    margin-left: 8px;
                    border-radius: 2px;
                    font-size: 12px;
                    opacity: 0;
                    transition: opacity 0.2s ease, background-color 0.2s ease;
                }

                .search-option:hover .remove-btn {
                    opacity: 1;
                }

                .remove-btn:hover {
                    background: var(--error-color, #ff4444);
                    color: white;
                }

                .no-recent {
                    padding: 8px 12px;
                    color: var(--text-secondary, #666666);
                    font-style: italic;
                    text-align: center;
                }

                .clear-all-btn {
                    padding: 6px 12px;
                    background: var(--bg-secondary, #f5f5f5);
                    border: none;
                    border-top: 1px solid var(--border-color, #cccccc);
                    color: var(--text-secondary, #666666);
                    cursor: pointer;
                    font-size: 12px;
                    width: 100%;
                    transition: background-color 0.2s ease;
                }

                .clear-all-btn:hover {
                    background: var(--hover-background-color, rgba(0, 0, 0, 0.1));
                }

                .dropdown-options::-webkit-scrollbar {
                    width: 6px;
                }

                .dropdown-options::-webkit-scrollbar-track {
                    background: var(--bg-secondary, #f5f5f5);
                }

                .dropdown-options::-webkit-scrollbar-thumb {
                    background: var(--border-color, #cccccc);
                    border-radius: 3px;
                }
            </style>

            <input type="text"
                   class="search-input"
                   placeholder="${placeholder}"
                   autocomplete="off">
            <button type="button" class="dropdown-toggle" aria-label="Show recent searches">
                <svg viewBox="0 0 24 24" width="12" height="12" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M6 9l6 6 6-6"/>
                </svg>
            </button>
            <div class="search-dropdown">
                <div class="dropdown-options"></div>
            </div>
        `;
    }

    setupEventListeners() {
        const input = this.shadowRoot.querySelector('.search-input');
        const toggle = this.shadowRoot.querySelector('.dropdown-toggle');

        input.addEventListener('input', (e) => this.handleInput(e));
        input.addEventListener('focus', () => this.handleFocus());
        input.addEventListener('blur', () => this.handleBlur());
        input.addEventListener('keydown', (e) => this.handleKeydown(e));
        toggle.addEventListener('click', (e) => this.handleToggleClick(e));

        // Click outside to close
        document.addEventListener('click', (e) => this.handleOutsideClick(e));
    }

    loadRecentSearches() {
        const recent = this.recentSearches.getRecentSearches(this.state.searchType);
        this.state.filteredOptions = recent;
        if (this.state.isOpen) {
            this.renderOptions();
        }
    }

    handleInput(event) {
        const value = event.target.value;
        this.state.selectedValue = value;

        // Filter recent searches based on input
        const recent = this.recentSearches.getRecentSearches(this.state.searchType);
        const searchTerm = value.toLowerCase();
        this.state.filteredOptions = recent.filter(item => 
            item.toLowerCase().includes(searchTerm)
        );

        if (!this.state.isOpen && value.length > 0) {
            this.openDropdown();
        }

        this.state.focusedIndex = -1;
        this.renderOptions();

        // Dispatch change event
        this.dispatchEvent(new CustomEvent('input', {
            detail: { value },
            bubbles: true
        }));
    }

    handleFocus() {
        // Don't select all text on focus - this interferes with regex tip insertion
    }

    handleBlur() {
        setTimeout(() => {
            if (!this.shadowRoot.activeElement && !this.contains(document.activeElement)) {
                this.closeDropdown();
            }
        }, 200);
    }

    handleKeydown(event) {
        switch (event.key) {
            case 'ArrowDown':
                event.preventDefault();
                if (!this.state.isOpen) {
                    this.showAllRecent();
                } else {
                    this.focusNextOption();
                }
                break;
            case 'ArrowUp':
                event.preventDefault();
                if (this.state.isOpen) {
                    this.focusPreviousOption();
                }
                break;
            case 'Enter':
                event.preventDefault();
                if (this.state.isOpen && this.state.focusedIndex >= 0) {
                    this.selectOption(this.state.filteredOptions[this.state.focusedIndex]);
                } else {
                    // Add current value to history and dispatch enter event
                    const currentValue = this.shadowRoot.querySelector('.search-input').value;
                    if (currentValue.trim()) {
                        this.recentSearches.addToHistory(this.state.searchType, currentValue);
                    }
                    this.closeDropdown();
                    this.dispatchEvent(new CustomEvent('enter', {
                        detail: { value: currentValue },
                        bubbles: true
                    }));
                }
                break;
            case 'Escape':
                event.preventDefault();
                if (this.state.isOpen) {
                    this.closeDropdown();
                } else {
                    this.clearValue();
                }
                break;
        }
    }

    handleToggleClick(event) {
        event.preventDefault();
        if (this.state.isOpen) {
            this.closeDropdown();
        } else {
            this.showAllRecent();
            this.shadowRoot.querySelector('.search-input').focus();
        }
    }

    handleOutsideClick(event) {
        if (!this.contains(event.target)) {
            this.closeDropdown();
        }
    }

    showAllRecent() {
        this.loadRecentSearches();
        this.openDropdown();
    }

    renderOptions() {
        const container = this.shadowRoot.querySelector('.dropdown-options');
        container.innerHTML = '';

        if (this.state.filteredOptions.length === 0) {
            const noResultsText = this.state.searchType === 'find' ? 
                'אין חיפושים אחרונים' : 'אין החלפות אחרונות';
            container.innerHTML = `<div class="no-recent">${noResultsText}</div>`;
            return;
        }

        this.state.filteredOptions.forEach((option, index) => {
            const optionElement = this.createOptionElement(option, index);
            container.appendChild(optionElement);
        });

        // Add clear all button if there are options
        if (this.state.filteredOptions.length > 0) {
            const clearAllBtn = document.createElement('button');
            clearAllBtn.className = 'clear-all-btn';
            clearAllBtn.textContent = 'נקה הכל';
            clearAllBtn.addEventListener('click', (e) => {
                e.preventDefault();
                e.stopPropagation();
                this.clearAllHistory();
            });
            container.appendChild(clearAllBtn);
        }
    }

    createOptionElement(option, index) {
        const optionElement = document.createElement('div');
        optionElement.className = 'search-option';
        optionElement.dataset.index = index;

        const textSpan = document.createElement('span');
        textSpan.className = 'search-text';
        textSpan.textContent = option;

        const removeBtn = document.createElement('button');
        removeBtn.className = 'remove-btn';
        removeBtn.textContent = '×';
        removeBtn.title = 'הסר מההיסטוריה';

        optionElement.appendChild(textSpan);
        optionElement.appendChild(removeBtn);

        if (index === this.state.focusedIndex) {
            optionElement.classList.add('focused');
        }

        // Select option on click
        textSpan.addEventListener('click', (e) => {
            e.preventDefault();
            e.stopPropagation();
            this.selectOption(option);
        });

        // Remove option on remove button click
        removeBtn.addEventListener('click', (e) => {
            e.preventDefault();
            e.stopPropagation();
            this.removeFromHistory(option);
        });

        return optionElement;
    }

    selectOption(option) {
        this.setValue(option);
        this.closeDropdown();

        this.dispatchEvent(new CustomEvent('select', {
            detail: { value: option },
            bubbles: true
        }));
    }

    removeFromHistory(option) {
        const recent = this.recentSearches.getRecentSearches(this.state.searchType);
        const filtered = recent.filter(item => item !== option);
        
        try {
            const key = this.recentSearches.storageKeys[this.state.searchType];
            localStorage.setItem(key, JSON.stringify(filtered));
            this.loadRecentSearches();
            this.renderOptions();
        } catch (error) {
            console.warn('Failed to remove from history:', error);
        }
    }

    clearAllHistory() {
        this.recentSearches.clearHistory(this.state.searchType);
        this.loadRecentSearches();
        this.renderOptions();
    }

    focusNextOption() {
        if (this.state.focusedIndex < this.state.filteredOptions.length - 1) {
            this.state.focusedIndex++;
            this.renderOptions();
            this.scrollToFocusedOption();
        }
    }

    focusPreviousOption() {
        if (this.state.focusedIndex > 0) {
            this.state.focusedIndex--;
            this.renderOptions();
            this.scrollToFocusedOption();
        }
    }

    scrollToFocusedOption() {
        if (this.state.focusedIndex >= 0) {
            const container = this.shadowRoot.querySelector('.dropdown-options');
            const focusedOption = container.children[this.state.focusedIndex];
            if (focusedOption) {
                focusedOption.scrollIntoView({ block: 'nearest' });
            }
        }
    }

    openDropdown() {
        this.state.isOpen = true;
        this.setAttribute('open', '');
        this.renderOptions();
    }

    closeDropdown() {
        this.state.isOpen = false;
        this.removeAttribute('open');
        this.state.focusedIndex = -1;
    }

    // Public methods
    getValue() {
        return this.shadowRoot.querySelector('.search-input').value;
    }

    setValue(value) {
        this.state.selectedValue = value;
        const input = this.shadowRoot.querySelector('.search-input');
        if (input) {
            input.value = value;
        }
        this.setAttribute('value', value);
    }

    clearValue() {
        this.setValue('');
        this.dispatchEvent(new CustomEvent('clear', { bubbles: true }));
    }

    // Add current value to history (called when search is performed)
    addCurrentToHistory() {
        const currentValue = this.getValue();
        if (currentValue.trim()) {
            this.recentSearches.addToHistory(this.state.searchType, currentValue);
        }
    }
}

// Register the custom element
customElements.define('search-combobox', SearchCombobox);

// Export for use in other files
window.SearchCombobox = SearchCombobox;