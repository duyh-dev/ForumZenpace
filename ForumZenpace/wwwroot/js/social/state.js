export const body = document.body;
export const currentUserId = Number.parseInt(body.dataset.currentUserId || '', 10);
export const hubUrl = body.dataset.socialHubUrl || '';
export const signalRClient = window.signalR;

export const connection = hubUrl && signalRClient
    ? new signalRClient.HubConnectionBuilder()
        .withUrl(hubUrl)
        .withAutomaticReconnect()
        .build()
    : null;

export const state = {
    realtimeReady: false
};

export const setRealtimeReady = (ready) => { state.realtimeReady = ready; };
export const isRealtimeAvailable = () => Boolean(connection && state.realtimeReady);

export const notificationLink = document.querySelector('[data-notification-link]');
export let notificationBadge = document.querySelector('[data-notification-badge]');
export const friendModal = document.querySelector('[data-friend-modal]');
export const friendModalDialog = friendModal?.querySelector('.social-modal-dialog');
export const friendRail = document.querySelector('[data-friend-rail]');
export const friendList = document.querySelector('[data-friend-list]');
export const friendPrevButton = document.querySelector('[data-friend-nav-prev]');
export const friendNextButton = document.querySelector('[data-friend-nav-next]');
export const profileSocial = document.querySelector('[data-profile-social]');
export const profileSocialStatus = document.querySelector('[data-profile-social-status]');
export const notificationPage = document.querySelector('[data-notification-page]');
export let notificationList = document.querySelector('[data-notification-list]');

export const setNotificationBadge = (elem) => { notificationBadge = elem; };
export const setNotificationList = (elem) => { notificationList = elem; };

export const getProfileTargetUserId = () => Number.parseInt(profileSocial?.dataset.targetUserId || '', 10);
export const getProfileTargetUsername = () => profileSocial?.dataset.targetUsername || '';

export const getCurrentReturnUrl = () => `${window.location.pathname}${window.location.search}${window.location.hash}`;
