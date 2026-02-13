// SmallEBot: scroll chat messages container to bottom (used when new messages or streaming updates)
window.SmallEBot = window.SmallEBot || {};
window.SmallEBot.scrollChatToBottom = function (element) {
    if (element) {
        element.scrollTop = element.scrollHeight;
    }
};
