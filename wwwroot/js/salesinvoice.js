let salesInvoiceKeydownHandler = null;

export function registerF3(dotNetHelper) {
    if (salesInvoiceKeydownHandler) {
        window.removeEventListener('keydown', salesInvoiceKeydownHandler);
        salesInvoiceKeydownHandler = null;
    }

    salesInvoiceKeydownHandler = (event) => {
        if (event.key === 'F3') {
            event.preventDefault();
            dotNetHelper.invokeMethodAsync('OpenAddItemsModalFromShortcut');
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
