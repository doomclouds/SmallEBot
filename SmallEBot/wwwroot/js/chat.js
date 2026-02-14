// SmallEBot: scroll chat messages container to bottom (used when new messages or streaming updates)
window.SmallEBot = window.SmallEBot || {};
window.SmallEBot.scrollChatToBottom = function (element) {
    if (element) {
        element.scrollTop = element.scrollHeight;
    }
};

// Theme persistence and root attribute
var DEFAULT_THEME = 'editorial-dark';
var THEME_KEY = 'smallebot.theme';
var VALID_IDS = ['editorial-dark', 'paper-light', 'terminal', 'dusk', 'mono'];

window.SmallEBot.getTheme = function () {
    try {
        var id = localStorage.getItem(THEME_KEY);
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
// Expose for Blazor JSInvoke (cannot call SmallEBot.getTheme directly)
window.SmallEBotGetTheme = function () { return window.SmallEBot.getTheme(); };
window.SmallEBotSetTheme = function (id) { window.SmallEBot.setTheme(id); };
