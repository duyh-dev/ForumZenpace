import { profileSocial, profileSocialStatus, getProfileTargetUserId, isRealtimeAvailable, connection, getCurrentReturnUrl } from './state.js';
import { postSocialAction } from './utils.js';

export const renderProfileActions = () => {
    if (!(profileSocial instanceof HTMLElement)) return;

    const isFriend = profileSocial.dataset.isFriend === 'true';
    const hasOutgoing = profileSocial.dataset.hasOutgoingRequest === 'true';
    const hasIncoming = profileSocial.dataset.hasIncomingRequest === 'true';
    const isBlockedByViewer = profileSocial.dataset.messageBlockedByViewer === 'true';

    const primaryAction = isFriend
        ? '<button type="button" class="btn btn-outline" data-social-remove-friend><i class="fa-solid fa-user-minus"></i><span>Xoa ban</span></button>'
        : hasOutgoing
            ? '<button type="button" class="btn btn-outline" disabled><i class="fa-solid fa-paper-plane"></i><span>Da gui loi moi</span></button>'
            : hasIncoming
                ? '<a href="/Notification" class="btn btn-primary"><i class="fa-solid fa-bell"></i><span>Mo thong bao de chap nhan</span></a>'
                : '<button type="button" class="btn btn-primary" data-social-send-request><i class="fa-solid fa-user-plus"></i><span>Ket ban</span></button>';

    profileSocial.innerHTML = `
        ${primaryAction}
        <button type="button" class="btn btn-text" data-social-toggle-block>
            <i class="fa-solid fa-ban"></i>
            <span>${isBlockedByViewer ? 'Bo chan tin nhan' : 'Chan tin nhan'}</span>
        </button>`;
};

export const renderProfileStatus = () => {
    if (!(profileSocial instanceof HTMLElement) || !(profileSocialStatus instanceof HTMLElement)) return;

    const isFriend = profileSocial.dataset.isFriend === 'true';
    const hasOutgoing = profileSocial.dataset.hasOutgoingRequest === 'true';
    const hasIncoming = profileSocial.dataset.hasIncomingRequest === 'true';
    const isBlockedByViewer = profileSocial.dataset.messageBlockedByViewer === 'true';
    const isBlockedByOtherUser = profileSocial.dataset.messageBlockedByOtherUser === 'true';

    profileSocialStatus.textContent = isFriend
        ? 'Hai ban dang la ban be tren Zenpace.'
        : hasOutgoing
            ? 'Ban da gui loi moi ket ban va dang cho phan hoi.'
            : hasIncoming
                ? 'Nguoi dung nay da gui loi moi ket ban cho ban. Hay vao thong bao de chap nhan.'
                : isBlockedByViewer
                    ? 'Ban dang chan tin nhan tu nguoi dung nay.'
                    : isBlockedByOtherUser
                        ? 'Nguoi dung nay dang chan tin nhan voi ban.'
                        : 'Ban co the ket ban, chat rieng hoac quan ly quyen nhan tin ngay tai day.';
};

export const syncProfileState = (state) => {
    if (!(profileSocial instanceof HTMLElement)) return;
    Object.entries(state).forEach(([key, value]) => {
        if (typeof value === 'boolean') {
            profileSocial.dataset[key] = `${value}`;
        }
    });
    renderProfileActions();
    renderProfileStatus();
};

export const bindProfileSocial = () => {
    if (!(profileSocial instanceof HTMLElement)) return;

    renderProfileActions();
    renderProfileStatus();

    profileSocial.addEventListener('click', async (event) => {
        const action = event.target instanceof Element ? event.target.closest('[data-social-send-request], [data-social-remove-friend], [data-social-toggle-block]') : null;
        if (!(action instanceof HTMLElement)) return;

        const targetUserId = getProfileTargetUserId();
        if (!Number.isInteger(targetUserId) || targetUserId <= 0) return;

        try {
            if (action.hasAttribute('data-social-send-request')) {
                if (isRealtimeAvailable()) {
                    await connection.invoke('SendFriendRequest', targetUserId);
                } else {
                    await postSocialAction('/Social/SendFriendRequest', {
                        targetUserId, returnUrl: getCurrentReturnUrl()
                    });
                }
                syncProfileState({ isFriend: false, hasOutgoingRequest: true, hasIncomingRequest: false });
                return;
            }

            if (action.hasAttribute('data-social-remove-friend')) {
                if (isRealtimeAvailable()) {
                    await connection.invoke('RemoveFriend', targetUserId);
                    syncProfileState({ isFriend: false, hasOutgoingRequest: false, hasIncomingRequest: false });
                } else {
                    await postSocialAction('/Social/RemoveFriend', {
                        targetUserId, returnUrl: getCurrentReturnUrl()
                    });
                    window.location.reload();
                }
                return;
            }

            if (isRealtimeAvailable()) {
                await connection.invoke('ToggleMessageBlock', targetUserId);
            } else {
                await postSocialAction('/Social/ToggleMessageBlock', {
                    targetUserId, returnUrl: getCurrentReturnUrl()
                });
                window.location.reload();
            }
        } catch {
            renderProfileStatus();
        }
    });
};
