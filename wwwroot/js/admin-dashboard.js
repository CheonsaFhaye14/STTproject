// Admin dashboard: tells each panel how many table rows fit in its area,
// so tables page instead of making the page scroll.
const watchers = new Map();
let nextId = 1;

export function watchRows(element, rowHeight, headerHeight, dotnetRef) {
    const id = nextId++;
    let last = -1;

    const observer = new ResizeObserver(() => {
        const height = element.clientHeight;
        if (!height) return; // panel hidden
        const rows = Math.max(1, Math.floor((height - headerHeight) / rowHeight));
        if (rows !== last) {
            last = rows;
            dotnetRef.invokeMethodAsync('SetPageSize', rows);
        }
    });

    observer.observe(element);
    watchers.set(id, observer);
    return id;
}

export function unwatch(id) {
    watchers.get(id)?.disconnect();
    watchers.delete(id);
}
