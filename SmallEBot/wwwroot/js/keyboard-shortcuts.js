window.keyboardShortcuts = {
    callbacks: {},
    register: function (dotNetRef, shortcuts) {
        this.callbacks = {};
        shortcuts.forEach(s => {
            this.callbacks[s.key.toLowerCase()] = {
                ctrl: s.ctrl || false,
                shift: s.shift || false,
                alt: s.alt || false,
                method: s.method
            };
        });
        this.handler = (e) => {
            const key = e.key.toLowerCase();
            const callback = this.callbacks[key];
            if (!callback) return;
            if (callback.ctrl !== e.ctrlKey) return;
            if (callback.shift !== e.shiftKey) return;
            if (callback.alt !== e.altKey) return;
            e.preventDefault();
            dotNetRef.invokeMethodAsync(callback.method);
        };
        document.addEventListener('keydown', this.handler);
    },
    unregister: function () {
        if (this.handler) {
            document.removeEventListener('keydown', this.handler);
            this.handler = null;
        }
        this.callbacks = {};
    }
};
