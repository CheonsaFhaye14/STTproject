export function registerMapItemKeyHandler(dotNetRef) {
    // Lightweight stub to satisfy the import and prevent 404/JS errors.
    // Keep it intentionally minimal to avoid calling nonexistent .NET methods.
    if (!window.__mapItemHandlers) {
        window.__mapItemHandlers = { refs: [] };
    }
    window.__mapItemHandlers.refs.push(dotNetRef);
}

export function unregisterMapItemKeyHandler(dotNetRef) {
    if (window.__mapItemHandlers) {
        window.__mapItemHandlers.refs = window.__mapItemHandlers.refs.filter(r => r !== dotNetRef);
    }
}
