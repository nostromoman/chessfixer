// Dragging a piece on a ChessBoard, with a mouse or a finger. Clicking (piece, then square) is handled by Blazor;
// this only adds dragging: the piece follows the pointer and on release .NET gets (from, to). The click that the
// browser sends after a drag is swallowed, so it does not select the square again.

const boards = new WeakMap();

export function attach(board, dotnet) {
    detach(board);
    let drag = null;

    const squareAt = (x, y) => {
        const rect = board.getBoundingClientRect();
        if (x < rect.left || x >= rect.right || y < rect.top || y >= rect.bottom) return -1;
        const col = Math.floor((x - rect.left) / rect.width * 8);
        const row = Math.floor((y - rect.top) / rect.height * 8);
        return board.classList.contains("flipped") ? row * 8 + (7 - col) : (7 - row) * 8 + col;
    };

    const moveGhost = (x, y) => {
        drag.ghost.style.left = `${x - drag.size / 2}px`;
        drag.ghost.style.top = `${y - drag.size / 2}px`;
    };

    const end = () => {
        document.removeEventListener("pointermove", onMove);
        document.removeEventListener("pointerup", onUp);
        document.removeEventListener("pointercancel", onCancel);
        if (drag?.ghost) drag.ghost.remove();
        if (drag?.piece) drag.piece.style.opacity = "";
        drag = null;
    };

    const onDown = e => {
        if (e.button !== 0 || drag) return;
        const square = e.target.closest(".chess-square");
        const piece = square?.querySelector(".chess-piece");
        if (!piece) return;
        drag = { from: Number(square.dataset.square), x: e.clientX, y: e.clientY, piece, ghost: null, size: square.offsetWidth, id: e.pointerId };
        document.addEventListener("pointermove", onMove);
        document.addEventListener("pointerup", onUp);
        document.addEventListener("pointercancel", onCancel);
    };

    const onMove = e => {
        if (!drag || e.pointerId !== drag.id) return;
        if (!drag.ghost) {
            if (Math.hypot(e.clientX - drag.x, e.clientY - drag.y) < 6) return; // still a click
            const ghost = drag.piece.cloneNode();
            ghost.className = "chess-piece-ghost";
            ghost.style.width = ghost.style.height = `${drag.size}px`;
            document.body.appendChild(ghost);
            drag.ghost = ghost;
            drag.piece.style.opacity = "0.35";
            dotnet.invokeMethodAsync("DragStarted", drag.from);
        }
        moveGhost(e.clientX, e.clientY);
        e.preventDefault();
    };

    const onUp = e => {
        if (!drag || e.pointerId !== drag.id) return;
        const dragged = drag.ghost !== null;
        const from = drag.from;
        end();
        if (!dragged) return; // a click: Blazor handles it
        swallowClick();
        const to = squareAt(e.clientX, e.clientY);
        if (to >= 0 && to !== from) dotnet.invokeMethodAsync("Dropped", from, to);
    };

    const onCancel = () => end();

    board.addEventListener("pointerdown", onDown);
    boards.set(board, () => {
        board.removeEventListener("pointerdown", onDown);
        end();
    });
}

export function detach(board) {
    const dispose = board && boards.get(board);
    if (dispose) {
        dispose();
        boards.delete(board);
    }
}

function swallowClick() {
    const stop = e => {
        e.stopPropagation();
        e.preventDefault();
    };
    window.addEventListener("click", stop, { capture: true, once: true });
    setTimeout(() => window.removeEventListener("click", stop, { capture: true }), 300);
}
