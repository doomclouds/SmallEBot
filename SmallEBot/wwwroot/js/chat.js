// SmallEBot: scroll chat messages container to bottom (used when new messages or streaming updates)
window.SmallEBot = window.SmallEBot || {};
window.SmallEBot.scrollChatToBottom = function (element) {
    if (element) {
        element.scrollTop = element.scrollHeight;
    }
};

// Theme persistence and root attribute
let DEFAULT_THEME = 'editorial-dark';
let THEME_KEY = 'smallebot.theme';
let VALID_IDS = ['editorial-dark', 'paper-light', 'terminal', 'dusk', 'mono'];

window.SmallEBot.getTheme = function () {
    try {
        let id = localStorage.getItem(THEME_KEY);
        if (id && VALID_IDS.indexOf(id) !== -1) return id;
    } catch (e) {}
    return DEFAULT_THEME;
};

window.SmallEBot.setTheme = function (id) {
    if (!id || VALID_IDS.indexOf(id) === -1) id = DEFAULT_THEME;
    try {
        localStorage.setItem(THEME_KEY, id);
        document.documentElement.setAttribute('data-theme', id);
    } catch (e) {}
};

// Scroll list so the selected index is in view (used by AttachmentPopover if present)
window.SmallEBot.scrollAttachmentPopoverToIndex = function (scrollContainerId, selectedIndex) {
    let container = document.getElementById(scrollContainerId);
    if (!container) return;
    let list = container.querySelector('.mud-list');
    if (!list) return;
    let item = list.children[selectedIndex];
    if (item) item.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
};

// Set chat input cursor to end (e.g. when focusing edit dialog)
window.SmallEBot.setChatInputCursorToEnd = function (wrapperId) {
    let wrap = document.getElementById(wrapperId);
    if (!wrap) return;
    let input = wrap.querySelector('textarea, input');
    if (!input) return;
    let len = input.value.length;
    input.setSelectionRange(len, len);
    input.focus();
};

// Chat input: Enter sends, Shift+Enter newline. Attach to wrapper so handlers survive input re-renders.
let _sendHandler = null;
let _sendWrapperId = null;
window.SmallEBot.attachChatInputSend = function (wrapperId, dotNetRef) {
    let wrap = document.getElementById(wrapperId);
    if (!wrap) return;
    window.SmallEBot.detachChatInputSend(wrapperId);
    _sendHandler = function (e) {
        let input = wrap.querySelector('textarea, input');
        if (!input || e.target !== input) return;
        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            dotNetRef.invokeMethodAsync('InvokeSend');
        }
    };
    _sendWrapperId = wrapperId;
    wrap.addEventListener('keydown', _sendHandler);
};
window.SmallEBot.detachChatInputSend = function (wrapperId) {
    if (!_sendHandler || _sendWrapperId !== wrapperId) return;
    let wrap = document.getElementById(wrapperId);
    if (wrap) wrap.removeEventListener('keydown', _sendHandler);
    _sendHandler = null;
    _sendWrapperId = null;
};

// Expose for Blazor JSInvoke (cannot call SmallEBot.getTheme directly)
window.SmallEBotGetTheme = function () { return window.SmallEBot.getTheme(); };
window.SmallEBotSetTheme = function (id) { return window.SmallEBot.setTheme(id); };
