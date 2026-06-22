document.addEventListener("keydown", function (e) {
    if (e.key !== "Enter") return;

    const current = e.target.closest("[data-focus-order]");
    if (!current || current.dataset.canMoveNext !== "true") return;

    e.preventDefault();

    const controls = [...document.querySelectorAll("[data-focus-order]")]
        .filter(x => !x.disabled && x.offsetParent !== null)
        .sort((a, b) => Number(a.dataset.focusOrder) - Number(b.dataset.focusOrder));

    const index = controls.indexOf(current);
    if (index === -1) return;

    // ✅ No wrap-around: stop at the last field
    const next = controls[index + 1];
    next?.focus();
});

// ✅ Call this after page load OR after a modal opens
window.focusFirstInput = (scope = document) => {
    const el = scope.querySelector("[data-autofocus='true']");
    if (el) el.focus();
};