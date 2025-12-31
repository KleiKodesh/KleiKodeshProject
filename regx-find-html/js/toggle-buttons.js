// Three-state toggle button functionality for formatting buttons
function setupToggleButtons() {
    // Get all elements with toggle-button class
    const toggleButtons = document.querySelectorAll('.toggle-button');
    
    toggleButtons.forEach(button => {
        // Check if this is a formatting button (has format-btn class)
        const isFormattingButton = button.classList.contains('format-btn');
        
        button.addEventListener('click', function() {
            if (isFormattingButton) {
                // Three-state cycle for formatting buttons: none → true → false → none
                if (this.classList.contains('toggled-true')) {
                    // true → false
                    this.classList.remove('toggled-true');
                    this.classList.add('toggled-false');
                } else if (this.classList.contains('toggled-false')) {
                    // false → none
                    this.classList.remove('toggled-false');
                } else {
                    // none → true
                    this.classList.add('toggled-true');
                }
            } else {
                // Two-state toggle for non-formatting buttons (legacy behavior)
                this.classList.toggle('active');
            }
        });
    });
}

// Helper function to get the three-state value of a formatting button
function getThreeStateValue(button) {
    if (button.classList.contains('toggled-true')) {
        return true;
    } else if (button.classList.contains('toggled-false')) {
        return false;
    } else {
        return null; // Not specified
    }
}

// Helper function to set the three-state value of a formatting button
function setThreeStateValue(button, value) {
    // Clear all states first
    button.classList.remove('toggled-true', 'toggled-false');
    
    if (value === true) {
        button.classList.add('toggled-true');
    } else if (value === false) {
        button.classList.add('toggled-false');
    }
    // null/undefined = no classes (default state)
}

// Initialize toggle buttons when DOM is loaded
document.addEventListener('DOMContentLoaded', setupToggleButtons);

// Export functions for use in other modules
window.getThreeStateValue = getThreeStateValue;
window.setThreeStateValue = setThreeStateValue;