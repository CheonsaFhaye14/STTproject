let salesInvoiceKeydownHandler = null;
let modalFocusTrapHandler = null;
let modalFocusTrapContainer = null;
let previousFocusedElement = null;

function getFocusableElements(container) {
    if (!container) {
        return [];
    }

    const selectors = [
        'a[href]',
        'button:not([disabled])',
        'input:not([disabled]):not([type="hidden"])',
        'select:not([disabled])',
        'textarea:not([disabled])',
        '[tabindex]:not([tabindex="-1"])'
    ];

    return Array.from(container.querySelectorAll(selectors.join(',')))
        .filter((element) => {
            if (element.hasAttribute('disabled')) {
                return false;
            }

            if (element.getAttribute('aria-hidden') === 'true') {
                return false;
            }

            return true;
        });
}

export function activateModalFocusTrap(containerSelector) {
    const container = document.querySelector(containerSelector);
    if (!container) {
        return;
    }

    if (modalFocusTrapContainer === container && modalFocusTrapHandler) {
        return;
    }

    deactivateModalFocusTrap();

    previousFocusedElement = document.activeElement;
    modalFocusTrapContainer = container;

    const focusables = getFocusableElements(container);
    if (focusables.length > 0 && !container.contains(document.activeElement)) {
        focusables[0].focus();
    }

    modalFocusTrapHandler = (event) => {
        if (event.key !== 'Tab' || !modalFocusTrapContainer) {
            return;
        }

        const currentFocusables = getFocusableElements(modalFocusTrapContainer);
        if (currentFocusables.length === 0) {
            event.preventDefault();
            return;
        }

        const first = currentFocusables[0];
        const last = currentFocusables[currentFocusables.length - 1];
        const active = document.activeElement;

        if (!modalFocusTrapContainer.contains(active)) {
            event.preventDefault();
            first.focus();
            return;
        }

        if (event.shiftKey && active === first) {
            event.preventDefault();
            last.focus();
            return;
        }

        if (!event.shiftKey && active === last) {
            event.preventDefault();
            first.focus();
        }
    };

    document.addEventListener('keydown', modalFocusTrapHandler, true);
}

export function deactivateModalFocusTrap() {
    if (modalFocusTrapHandler) {
        document.removeEventListener('keydown', modalFocusTrapHandler, true);
        modalFocusTrapHandler = null;
    }

    modalFocusTrapContainer = null;

    if (previousFocusedElement && typeof previousFocusedElement.focus === 'function') {
        previousFocusedElement.focus();
    }

    previousFocusedElement = null;
}

export function registerF3(dotNetHelper) {
    if (salesInvoiceKeydownHandler) {
        window.removeEventListener('keydown', salesInvoiceKeydownHandler);
        salesInvoiceKeydownHandler = null;
    }

    salesInvoiceKeydownHandler = (event) => {
        if (event.key === 'F3') {
            event.preventDefault();
            dotNetHelper.invokeMethodAsync('OpenAddItemsModalFromShortcut');
            return;
        }

        if (event.key === 'F4') {
            event.preventDefault();
            dotNetHelper.invokeMethodAsync('ToggleEditItemsModalFromShortcut');
            return;
        }

        if (event.ctrlKey && (event.key === 's' || event.key === 'S')) {
            event.preventDefault();
            dotNetHelper.invokeMethodAsync('SaveOpenModalFromShortcut');
            return;
        }

        if (event.key === 'Escape') {
            event.preventDefault();
            dotNetHelper.invokeMethodAsync('EscapeActionFromShortcut');
        }

    };

    window.addEventListener('keydown', salesInvoiceKeydownHandler);
}

export function unregisterF3() {
    if (salesInvoiceKeydownHandler) {
        window.removeEventListener('keydown', salesInvoiceKeydownHandler);
        salesInvoiceKeydownHandler = null;
    }
}

export function openSelectDropdown(selectElement) {
    if (!selectElement || selectElement.disabled) {
        return;
    }

    selectElement.focus();

    if (typeof selectElement.showPicker === 'function') {
        try {
            selectElement.showPicker();
            return;
        } catch {
            // Fall through to synthetic events for browsers that block showPicker.
        }
    }

    const mouseDown = new MouseEvent('mousedown', { bubbles: true, cancelable: true, view: window });
    const mouseUp = new MouseEvent('mouseup', { bubbles: true, cancelable: true, view: window });
    const click = new MouseEvent('click', { bubbles: true, cancelable: true, view: window });
    selectElement.dispatchEvent(mouseDown);
    selectElement.dispatchEvent(mouseUp);
    selectElement.dispatchEvent(click);
}

export function openDatalist(inputElement) {
    if (!inputElement || inputElement.disabled) {
        return;
    }

    try {
        inputElement.focus();
        const ev = new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true });
        inputElement.dispatchEvent(ev);
    } catch {
        // best-effort; some browsers may ignore synthetic events
    }
}

export function saveSalesInvoiceDraft(storageKey, draftJson) {
    if (!storageKey) {
        return;
    }

    if (!draftJson) {
        localStorage.removeItem(storageKey);
        return;
    }

    localStorage.setItem(storageKey, draftJson);
}

export function loadSalesInvoiceDraft(storageKey) {
    if (!storageKey) {
        return null;
    }

    return localStorage.getItem(storageKey);
}

export function clearSalesInvoiceDraft(storageKey) {
    if (!storageKey) {
        return;
    }

    localStorage.removeItem(storageKey);
}

