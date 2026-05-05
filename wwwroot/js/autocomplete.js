window.scrollHighlightedIntoView = function() {
    const highlightedElement = document.querySelector('.autocomplete-popup .highlighted');
    if (highlightedElement) {
        const popup = highlightedElement.closest('.autocomplete-popup');
        if (popup) {
            const rect = highlightedElement.getBoundingClientRect();
            const popupRect = popup.getBoundingClientRect();
            
            if (rect.top < popupRect.top) {
                // Scroll up
                popup.scrollTop -= popupRect.top - rect.top;
            } else if (rect.bottom > popupRect.bottom) {
                // Scroll down
                popup.scrollTop += rect.bottom - popupRect.bottom;
            }
        }
    }
};
