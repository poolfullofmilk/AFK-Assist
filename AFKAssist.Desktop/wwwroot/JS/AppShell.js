// Escape Closes An Open Menu First, Focus Goes Back To Its Button
window.addEventListener(
    "keydown",
    (event) => {
        if (event.key !== "Escape") return;
        const activator = document.querySelector('.mud-menu :is(button, .mud-menu-activator)[aria-expanded="true"]');
        if (!activator) return;
        event.preventDefault();
        event.stopImmediatePropagation();
        activator.click();
        activator.focus?.();
    },
    true
);

// An Info Icon Inside A Switch Label Never Toggles The Switch
document.addEventListener("click", (event) => {
    if (event.target instanceof Element && event.target.closest(".info-icon")) {
        event.preventDefault();
    }
});

window.afkShell = {
    // The Next Key Pressed Becomes The Custom Key, Escape Or A Click Cancels
    captureKey: () =>
        new Promise((resolve) => {
            const modifiers = [16, 17, 18, 91, 92];
            const finish = (keyCode) => {
                window.removeEventListener("keydown", onKey, true);
                window.removeEventListener("pointerdown", onCancel, true);
                window.removeEventListener("blur", onCancel);
                resolve(keyCode);
            };
            const onKey = (event) => {
                event.preventDefault();
                event.stopImmediatePropagation();
                if (modifiers.includes(event.keyCode)) return;
                finish(event.key === "Escape" ? 0 : event.keyCode);
            };
            const onCancel = () => finish(0);
            window.addEventListener("keydown", onKey, true);
            window.addEventListener("pointerdown", onCancel, true);
            window.addEventListener("blur", onCancel);
        }),
};
