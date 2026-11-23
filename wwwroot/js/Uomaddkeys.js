// Keyboard handling for the Add UOM window (AddUom.razor).
//
// Add a unit form (wire):
//   Unit (AutoComplete)  --Enter-->  Conversion input        (handled in .NET via the AutoComplete's OnConfirmed)
//   Conversion input     --Enter-->  "counted in" select     (only when the input isn't empty)
//   "counted in" select  --Enter #1--> opens the list, #2 picks (inside the browser's list), #3 adds the unit
//
// Whole window while it is open (shortcuts / unshortcuts), wherever focus is, even on nothing:
//   a unit being edited:   Enter saves it,   Esc cancels it,   Ctrl+S saves it
//   nothing being edited:  Ctrl+S saves the window (Save changes),   Esc cancels the window

const CONV_ID = "uom-add-conv";
const BASIS_ID = "uom-add-basis";
const BUTTON_ID = "uom-add-btn";
const FORM_ID = "uom-add-form";

/** Focuses the first input, select or button inside the element with this id. Returns false when there is none. */
export function focusIn(id) {
  const el = document
    .getElementById(id)
    ?.querySelector("input, select, button:not([disabled])");
  if (!el) return false;
  el.focus();
  if (el.tagName === "INPUT") el.select?.();
  return true;
}

/** Wires the add form's Enter-key flow once per form element. Safe to call on every render. */
export function wire(rootId, dotnet) {
  const root = document.getElementById(rootId);
  if (!root || root.__uomKeys) return;
  root.__uomKeys = true;

  root.addEventListener("keydown", (e) => {
    if (e.key !== "Enter" || e.isComposing) return;
    const t = e.target;

    // Conversion input: on to the "counted in" select, or the Add button when there is no select.
    if (t.tagName === "INPUT" && t.closest(`#${CONV_ID}`)) {
      e.preventDefault();
      if (!t.value.trim()) return;
      if (!focusIn(BASIS_ID)) focusIn(BUTTON_ID);
      return;
    }

    // "Counted in" select: the first Enter opens the list. The Enter that picks a unit stays inside the browser's
    // own list (it never reaches the page), so any Enter that reaches us after the list was opened adds the unit.
    if (t.tagName === "SELECT" && t.closest(`#${BASIS_ID}`)) {
      e.preventDefault();
      if (!t.dataset.uomStep) {
        try {
          t.showPicker();
        } catch {
          /* older browsers: arrow keys still change the value */
        }
        t.dataset.uomStep = "open";
      } else {
        delete t.dataset.uomStep;
        dotnet.invokeMethodAsync("AddUnitFromKeyboard");
      }
    }
  });

  // Leaving the select starts the open → add sequence over.
  root.addEventListener("focusout", (e) => {
    const t = e.target;
    if (t.tagName === "SELECT" && t.closest(`#${BASIS_ID}`))
      delete t.dataset.uomStep;
  });
}

// ── Window-wide shortcuts ─────────────────────────────────────────────────

let shortcutHandler = null;

/** The unit card being edited, if any. */
const editingCard = () => document.querySelector(".au .au-u.editing");

/** An AutoComplete suggestion list is open: its own Esc / Enter handling comes first. */
const suggestionsOpen = () =>
  !!document.querySelector(".au .autocomplete-list");

/** Leaving the focused field commits what was typed (its change event) before .NET reads it. */
function commitFocused() {
  const a = document.activeElement;
  if (
    a &&
    a !== document.body &&
    (a.tagName === "INPUT" ||
      a.tagName === "SELECT" ||
      a.tagName === "TEXTAREA")
  ) {
    a.blur();
    return a;
  }
  return null;
}

async function saveEdit(dotnet) {
  const was = commitFocused();
  const saved = await dotnet.invokeMethodAsync("SaveEditFromKeyboard");
  if (!saved && was?.isConnected) was.focus(); // errors show under the fields; carry on typing
}

/** Turns the window shortcuts on (once). Called while the Add UOM window is open. */
export function shortcuts(dotnet) {
  if (shortcutHandler) return;

  shortcutHandler = (e) => {
    if (e.isComposing) return;
    const t = e.target instanceof Element ? e.target : null;
    const editing = editingCard();
    const key = e.key;

    // Ctrl+S / Cmd+S: save the unit being edited, else the whole window. Never the browser's "save page".
    if ((e.ctrlKey || e.metaKey) && !e.altKey && key.toLowerCase() === "s") {
      e.preventDefault();
      e.stopImmediatePropagation();
      if (editing) saveEdit(dotnet);
      else {
        commitFocused();
        dotnet.invokeMethodAsync("SaveAllFromKeyboard");
      }
      return;
    }

    if (key === "Escape") {
      if (suggestionsOpen()) return; // let the AutoComplete close its list first
      // Taken over here so the page's own Esc shortcut doesn't close the window without CancelAsync.
      e.preventDefault();
      e.stopImmediatePropagation();
      if (editing) dotnet.invokeMethodAsync("CancelEditFromKeyboard");
      else dotnet.invokeMethodAsync("CancelFromKeyboard");
      return;
    }

    // Enter saves the unit being edited — from its fields or from nowhere in particular (focus on the page).
    // Enter in the add form keeps the add flow, and Enter on a button keeps the button's own click.
    if (key === "Enter" && editing && !e.shiftKey) {
      if (
        t?.closest(`#${FORM_ID}`) ||
        t?.tagName === "BUTTON" ||
        t?.tagName === "TEXTAREA"
      )
        return;
      if (t?.tagName === "SELECT" && !t.closest(".au-u.editing")) return;
      e.preventDefault();
      e.stopImmediatePropagation();
      saveEdit(dotnet);
    }
  };

  // Capture on window: runs before handlers on the page's own elements and on document.
  window.addEventListener("keydown", shortcutHandler, true);
}

/** Turns the window shortcuts off (window closed). */
export function unshortcuts() {
  if (!shortcutHandler) return;
  window.removeEventListener("keydown", shortcutHandler, true);
  shortcutHandler = null;
}
