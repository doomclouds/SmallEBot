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

// Set chat input cursor to end (e.g. after inserting @path or /skillId)
window.SmallEBot.setChatInputCursorToEnd = function (wrapperId) {
    var wrap = document.getElementById(wrapperId);
    if (!wrap) return;
    var input = wrap.querySelector('textarea, input');
    if (!input) return;
    var len = input.value.length;
    input.setSelectionRange(len, len);
    input.focus();
};

// Scroll attachment popover list so the selected index is in view (for arrow key nav)
window.SmallEBot.scrollAttachmentPopoverToIndex = function (scrollContainerId, selectedIndex) {
    var container = document.getElementById(scrollContainerId);
    if (!container) return;
    var list = container.querySelector('.mud-list');
    if (!list) return;
    var item = list.children[selectedIndex];
    if (item) item.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
};

// Set chat input value and cursor to end (after @ or / completion so DOM is in sync)
window.SmallEBot.setChatInputValueAndCursorToEnd = function (wrapperId, value) {
    var wrap = document.getElementById(wrapperId);
    if (!wrap) return;
    var input = wrap.querySelector('textarea, input');
    if (!input) return;
    if (value !== undefined && value !== null) input.value = value;
    var len = input.value.length;
    input.setSelectionRange(len, len);
    input.focus();
};

// Chat input: Enter sends, Shift+Enter newline (preventDefault only for Enter)
window.SmallEBot.attachChatInputSend = function (wrapperId, dotNetRef) {
    var wrap = document.getElementById(wrapperId);
    if (!wrap) return;
    var input = wrap.querySelector('textarea, input');
    if (!input) return;
    input.addEventListener('keydown', function (e) {
        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            dotNetRef.invokeMethodAsync('InvokeSend');
        }
    });
};

// Suggestion popover: when open, intercept ArrowUp/ArrowDown/Enter/Escape on input so user can navigate and select without leaving input
var _suggestionKeyHandler = null;
var _suggestionKeyWrapperId = null;
window.SmallEBot.attachChatInputSuggestionKeys = function (wrapperId, dotNetRef) {
    var wrap = document.getElementById(wrapperId);
    if (!wrap) return;
    var input = wrap.querySelector('textarea, input');
    if (!input) return;
    window.SmallEBot.detachChatInputSuggestionKeys(wrapperId);
    var keys = ['ArrowDown', 'ArrowUp', 'Enter', 'Escape'];
    _suggestionKeyHandler = function (e) {
        if (keys.indexOf(e.key) !== -1) {
            e.preventDefault();
            e.stopPropagation();
            dotNetRef.invokeMethodAsync('OnSuggestionKeyDown', e.key);
        }
    };
    _suggestionKeyWrapperId = wrapperId;
    input.addEventListener('keydown', _suggestionKeyHandler, true);
};
window.SmallEBot.detachChatInputSuggestionKeys = function (wrapperId) {
    if (!_suggestionKeyHandler || _suggestionKeyWrapperId !== wrapperId) return;
    var wrap = document.getElementById(wrapperId);
    if (wrap) {
        var input = wrap.querySelector('textarea, input');
        if (input) input.removeEventListener('keydown', _suggestionKeyHandler, true);
    }
    _suggestionKeyHandler = null;
    _suggestionKeyWrapperId = null;
};

// Expose for Blazor JSInvoke (cannot call SmallEBot.getTheme directly)
window.SmallEBotGetTheme = function () { return window.SmallEBot.getTheme(); };
window.SmallEBotSetTheme = function (id) { window.SmallEBot.setTheme(id); };
