let mapItemKeydownHandler = null;

export function registerMapItemKeyHandler(dotNetHelper) {
    if (mapItemKeydownHandler) {
        window.removeEventListener('keydown', mapItemKeydownHandler);
        mapItemKeydownHandler = null;
    }

    mapItemKeydownHandler = (event) => {
        if (event.key === 'Escape') {
            event.preventDefault();
            dotNetHelper.invokeMethodAsync('EscapeActionFromShortcut');
        }
    };

    window.addEventListener('keydown', mapItemKeydownHandler);
}

export function unregisterMapItemKeyHandler() {
    if (mapItemKeydownHandler) {
        window.removeEventListener('keydown', mapItemKeydownHandler);
        mapItemKeydownHandler = null;
    }
}
