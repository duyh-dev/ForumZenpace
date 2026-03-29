export const updateChatState = (payload) => {
    const chatPanel = document.querySelector('[data-chat-panel]');
    if (!(chatPanel instanceof HTMLElement)) {
        return;
    }

    const targetUserId = Number.parseInt(chatPanel.dataset.targetUserId || '', 10);
    if (targetUserId !== payload.targetUserId) {
        return;
    }

    let message = '';
    if (payload.isMessageBlockedByViewer) {
        message = 'Ban da chan tin nhan voi nguoi dung nay.';
    } else if (payload.isMessageBlockedByOtherUser) {
        message = 'Nguoi dung nay da chan tin nhan voi ban.';
    }

    chatPanel.dataset.chatCanSend = payload.isConversationBlocked ? 'false' : 'true';
    chatPanel.dataset.chatBlockMessage = message;

    const banner = document.querySelector('[data-chat-banner]');
    if (banner instanceof HTMLElement) {
        banner.textContent = message;
        banner.hidden = !message;
    } else if (message) {
        const nextBanner = document.createElement('div');
        nextBanner.className = 'profile-chat-banner is-error';
        nextBanner.setAttribute('data-chat-banner', '');
        nextBanner.textContent = message;
        chatPanel.insertBefore(nextBanner, chatPanel.querySelector('[data-chat-form]'));
    }

    document.dispatchEvent(new CustomEvent('zenpace:chat-state-changed'));
};
