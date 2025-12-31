// Custom Combobox Web Component - Pure JavaScript custom HTML element
class CustomCombobox extends HTMLElement {
    constructor() {
        super();

        // Private state
        this.state = {
            isOpen: false,
            selectedValue: '',
            filteredOptions: [],
            focusedIndex: -1,
            options: []
        };

        // Create shadow DOM for encapsulation
        this.attachShadow({ mode: 'open' });
    }

    // Define observed attributes
    static get observedAttributes() {
        return ['type', 'placeholder', 'value', 'options'];
    }

    // Called when element is added to DOM
    connectedCallback() {
        this.render();
        this.setupEventListeners();
        this.loadInitialOptions();
    }

    // Called when attributes change
    attributeChangedCallback(name, oldValue, newValue) {
        if (oldValue !== newValue) {
            switch (name) {
                case 'options':
                    this.updateOptions(this.parseOptions(newValue));
                    break;
                case 'value':
                    this.setValue(newValue);
                    break;
                default:
                    if (this.shadowRoot) {
                        this.render();
                    }
            }
        }
    }

    // Render the component
    render() {
        const placeholder = this.getAttribute('placeholder') || '';

        this.shadowRoot.innerHTML = `
            <style>
                :host {
                    position: relative;
                    display: flex;
                    align-items: center;
                    flex: 1;
                    min-width: 80px;
                    max-width: 100%;
                }

                .combobox-input {
                    flex: 1;
                    background: var(--primary-background-color, #ffffff);
                    color: var(--text-color, #000000);
                    border: 1px solid var(--border-color, #cccccc);
                    border-radius: var(--border-radius, 4px);
                    padding: 5px 5px 5px 30px;
                    font-size: 14px;
                    transition: font-family 0.2s ease, border-color 0.2s ease;
                    min-width: 0;
                    font-family: inherit;
                    white-space: nowrap;
                    overflow: hidden;
                    text-overflow: ellipsis;
                }

                .combobox-input:focus {
                    border-color: var(--accent-color, #0066cc);
                    outline: none;
                }

                .combobox-toggle {
                    position: absolute;
                    left: 5px;
                    background: none;
                    border: none;
                    color: var(--text-color, #000000);
                    cursor: pointer;
                    padding: 2px;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    border-radius: 2px;
                    transition: background-color 0.2s ease, transform 0.2s ease;
                }

                .combobox-toggle:hover {
                    background: var(--hover-background-color, rgba(0, 0, 0, 0.1));
                }

                :host([open]) .combobox-toggle {
                    transform: rotate(180deg);
                }

                .combobox-dropdown {
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

                :host([open]) .combobox-dropdown {
                    display: block;
                }

                .combobox-options {
                    max-height: 200px;
                    overflow-y: auto;
                    padding: 2px 0;
                }

                .combobox-option {
                    padding: 8px 12px;
                    cursor: pointer;
                    color: var(--text-color, #000000);
                    background: var(--primary-background-color, #ffffff);
                    transition: background-color 0.1s ease;
                    font-size: 14px;
                    white-space: nowrap;
                    overflow: hidden;
                    text-overflow: ellipsis;
                }

                .combobox-option:hover,
                .combobox-option.focused {
                    background: var(--hover-background-color, rgba(0, 0, 0, 0.1));
                }

                .combobox-no-options {
                    padding: 8px 12px;
                    color: var(--text-secondary, #666666);
                    font-style: italic;
                    text-align: center;
                }

                /* Font preview specific styling */
                :host([type="font"]) .combobox-option {
                    padding: 8px 12px;
                    min-height: 50px;
                    display: flex;
                    flex-direction: column;
                    align-items: flex-start;
                    justify-content: center;
                    gap: 2px;
                }

                .font-name-line {
                    font-size: 14px;
                    font-weight: 500;
                    color: var(--text-color, #000000);
                    font-family: inherit;
                    line-height: 1.2;
                }

                .font-preview-line {
                    font-size: 12px;
                    color: var(--text-secondary, #666666);
                    line-height: 1.2;
                    opacity: 0.8;
                }

                .combobox-options::-webkit-scrollbar {
                    width: 6px;
                }

                .combobox-options::-webkit-scrollbar-track {
                    background: var(--bg-secondary, #f5f5f5);
                }

                .combobox-options::-webkit-scrollbar-thumb {
                    background: var(--border-color, #cccccc);
                    border-radius: 3px;
                }
            </style>

            <input type="text"
                   class="combobox-input"
                   placeholder="${placeholder}"
                   autocomplete="off">
            <button type="button" class="combobox-toggle" aria-label="Toggle dropdown">
                <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M6 9l6 6 6-6"/>
                </svg>
            </button>
            <div class="combobox-dropdown" dir="auto">
                <div class="combobox-options"></div>
            </div>
        `;
    }

    // Setup event listeners
    setupEventListeners() {
        const input = this.shadowRoot.querySelector('.combobox-input');
        const toggle = this.shadowRoot.querySelector('.combobox-toggle');

        input.addEventListener('input', (e) => this.handleInput(e));
        input.addEventListener('focus', () => this.handleFocus());
        input.addEventListener('blur', () => this.handleBlur());
        input.addEventListener('keydown', (e) => this.handleKeydown(e));
        toggle.addEventListener('click', (e) => this.handleToggleClick(e));

        // Click outside to close
        document.addEventListener('click', (e) => this.handleOutsideClick(e));
    }

    // Load initial options
    loadInitialOptions() {
        const optionsAttr = this.getAttribute('options');
        if (optionsAttr) {
            this.updateOptions(this.parseOptions(optionsAttr));
        }
    }

    // Parse options from attribute
    parseOptions(optionsStr) {
        if (!optionsStr) return [];
        try {
            return JSON.parse(optionsStr);
        } catch {
            return optionsStr.split(',').map(s => s.trim());
        }
    }

    // Handle input changes
    handleInput(event) {
        const value = event.target.value;
        this.state.selectedValue = value;

        // Only filter when typing (not when clicking arrow)
        this.filterOptions(value);

        if (this.getAttribute('type') === 'font' && value.trim()) {
            this.applyFontPreview(value);
        }

        if (!this.state.isOpen) {
            this.openDropdown();
        }

        // Dispatch change event
        this.dispatchEvent(new CustomEvent('change', {
            detail: { value },
            bubbles: true
        }));
    }

    // Handle other events
    handleFocus() {
        // Select all text when clicking in input
        const input = this.shadowRoot.querySelector('.combobox-input');
        setTimeout(() => input.select(), 0);
    }

    handleBlur() {
        // Delay closing to allow click events to fire first
        setTimeout(() => {
            // Only close if no element inside the dropdown has focus
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
                    this.openDropdown();
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
            // Show ALL options when clicking arrow (no filtering)
            this.showAllOptions();
            this.shadowRoot.querySelector('.combobox-input').focus();
        }
    }

    handleOutsideClick(event) {
        if (!this.contains(event.target)) {
            this.closeDropdown();
        }
    }

    // Filter and render options
    filterOptions(value) {
        const searchTerm = value.toLowerCase();
        this.state.filteredOptions = this.state.options.filter(option => {
            const optionText = typeof option === 'string' ? option : option.name;
            return optionText.toLowerCase().includes(searchTerm);
        });
        this.state.focusedIndex = -1;
        this.renderOptions();
    }

    renderOptions() {
        const container = this.shadowRoot.querySelector('.combobox-options');
        container.innerHTML = '';

        if (this.state.filteredOptions.length === 0) {
            container.innerHTML = '<div class="combobox-no-options">No options found</div>';
            return;
        }

        this.state.filteredOptions.forEach((option, index) => {
            const optionElement = this.createOptionElement(option, index);
            container.appendChild(optionElement);
        });
    }

    createOptionElement(option, index) {
        const optionElement = document.createElement('div');
        optionElement.className = 'combobox-option';
        optionElement.setAttribute('dir', 'auto');
        optionElement.dataset.index = index;

        if (this.getAttribute('type') === 'font') {
            const fontName = typeof option === 'string' ? option : option.name;
            
            // Create two-line layout for font options
            const fontNameLine = document.createElement('div');
            fontNameLine.className = 'font-name-line';
            fontNameLine.textContent = fontName;
            
            const previewLine = document.createElement('div');
            previewLine.className = 'font-preview-line';
            previewLine.textContent = 'ABC abc אבג';
            previewLine.style.fontFamily = `"${fontName}", sans-serif`;
            
            optionElement.appendChild(fontNameLine);
            optionElement.appendChild(previewLine);
        } else {
            optionElement.textContent = typeof option === 'string' ? option : option.name;
        }

        if (index === this.state.focusedIndex) {
            optionElement.classList.add('focused');
        }

        optionElement.addEventListener('click', (e) => {
            e.preventDefault();
            e.stopPropagation();

            // Immediately select the option
            const value = typeof option === 'string' ? option : option.name;
            this.setValue(value);
            this.closeDropdown();

            // Dispatch events
            this.dispatchEvent(new CustomEvent('select', {
                detail: { value, option },
                bubbles: true
            }));

            this.dispatchEvent(new CustomEvent('change', {
                detail: { value },
                bubbles: true
            }));
        });

        return optionElement;
    }

    applyFontPreview(fontName) {
        const input = this.shadowRoot.querySelector('.combobox-input');
        if (fontName && fontName.trim()) {
            input.style.fontFamily = `"${fontName}", sans-serif`;
        } else {
            input.style.fontFamily = '';
        }
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

    // Scroll to the focused option
    scrollToFocusedOption() {
        if (this.state.focusedIndex >= 0) {
            const container = this.shadowRoot.querySelector('.combobox-options');
            const focusedOption = container.children[this.state.focusedIndex];
            if (focusedOption) {
                focusedOption.scrollIntoView({ block: 'nearest' });
            }
        }
    }

    openDropdown() {
        this.state.isOpen = true;
        this.setAttribute('open', '');
        this.filterOptions(this.shadowRoot.querySelector('.combobox-input').value);
    }

    // Show all options without filtering (for arrow click)
    showAllOptions() {
        this.state.isOpen = true;
        this.setAttribute('open', '');
        this.state.filteredOptions = [...this.state.options];

        // Find and highlight current selection
        const currentValue = this.shadowRoot.querySelector('.combobox-input').value;
        this.state.focusedIndex = this.state.filteredOptions.findIndex(option => {
            const optionText = typeof option === 'string' ? option : option.name;
            return optionText === currentValue;
        });

        this.renderOptions();
        this.scrollToFocusedOption();
    }

    closeDropdown() {
        this.state.isOpen = false;
        this.removeAttribute('open');
        this.state.focusedIndex = -1;
    }

    // Public methods
    updateOptions(newOptions) {
        this.state.options = newOptions || [];
        if (this.state.isOpen) {
            this.filterOptions(this.shadowRoot.querySelector('.combobox-input').value);
        }
    }

    getValue() {
        return this.state.selectedValue;
    }

    setValue(value) {
        this.state.selectedValue = value;
        const input = this.shadowRoot.querySelector('.combobox-input');
        if (input) {
            input.value = value;
            if (this.getAttribute('type') === 'font') {
                this.applyFontPreview(value);
            }
        }
        this.setAttribute('value', value);
    }

    clearValue() {
        this.setValue('');
        this.dispatchEvent(new CustomEvent('clear', { bubbles: true }));
    }
}

// Register the custom element
customElements.define('custom-combobox', CustomCombobox);

// Export for use in other files
window.CustomCombobox = CustomCombobox;
