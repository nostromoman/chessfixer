// Keeps the selected move in view in the list of moves. While the user is typing in the list, the keyboard focus moves
// along to the selected move, so a whole game can be typed with Enter after each move.
export function showSelected(list) {
    const selected = list?.querySelector(".review-half.selected");
    if (!selected) return;
    selected.scrollIntoView({ block: "nearest" });
    const typing = list.contains(document.activeElement) && document.activeElement.classList.contains("review-move-input");
    const input = selected.querySelector(".review-move-input");
    if (typing && input && document.activeElement !== input) input.focus();
}
