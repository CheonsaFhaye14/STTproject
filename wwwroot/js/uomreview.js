
const FLASH_MS = 2200;

function reducedMotion() {
    return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
}

function glow(el, keyframes) {
    if (!el || typeof el.animate !== 'function') return;
    el.animate(keyframes, {
        duration: reducedMotion() ? 600 : FLASH_MS,
        easing: 'ease-out'
    });
}

const ringFlash = [
    { boxShadow: '0 0 0 3px rgba(201, 162, 39, 0.55)' },
    { boxShadow: '0 0 0 3px rgba(201, 162, 39, 0.55)', offset: 0.5 },
    { boxShadow: '0 0 0 0 rgba(201, 162, 39, 0)' }
];

export function jumpTo(targetId, column) {
    const el = document.getElementById(targetId);
    if (!el) return false;

    el.scrollIntoView({
        block: 'center',
        inline: 'nearest',
        behavior: reducedMotion() ? 'auto' : 'smooth'
    });

    window.setTimeout(() => {
        const field = column ? el.querySelector(`[data-col="${CSS.escape(column)}"]`) : null;

        if (!field || field.disabled) {
            glow(el, ringFlash);
        }

        if (column) {
            if (field && !field.disabled) {
                field.focus({ preventScroll: true });
                try {
                    if (field.type !== 'radio' && typeof field.select === 'function') field.select();
                } catch { /* some input types do not support select() */ }
                glow(field, ringFlash);
            }
        }
    }, reducedMotion() ? 0 : 350);

    return true;
}

export function flash(targetId) {
    const el = document.getElementById(targetId);
    if (!el) return false;
    glow(el, [
        { boxShadow: '0 0 0 4px rgba(192, 57, 43, 0.6)' },
        { boxShadow: '0 0 0 4px rgba(192, 57, 43, 0.6)', offset: 0.5 },
        { boxShadow: '0 0 0 0 rgba(192, 57, 43, 0)' }
    ]);
    return true;
}