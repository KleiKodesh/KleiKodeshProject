// Toggle button functionality
function setupToggleButtons() {
    // Get all elements with toggle-button class
    const toggleButtons = document.querySelectorAll('.toggle-button');
    
    toggleButtons.forEach(button => {
        button.addEventListener('click', function() {
            // Toggle the active class
            this.classList.toggle('active');
        });
    });
}

// Initialize toggle buttons when DOM is loaded
document.addEventListener('DOMContentLoaded', setupToggleButtons);