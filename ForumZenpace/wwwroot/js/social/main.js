import { connection, currentUserId, setRealtimeReady } from './state.js';
import { bindHomeSocial, setCandidateState, upsertFriendCard, removeFriendCard, updateFriendCardBlockState, setFeedSuggestionState } from './friends.js';
import { bindNotificationPage, setUnreadCount, prependNotification, updateNotificationResolution } from './notifications.js';
import { bindProfileSocial, syncProfileState } from './profile.js';
import { updateChatState } from './chat.js';
import { startHeartbeat, stopHeartbeat, handleReconnectPresence, setUserOnlineStatus } from './presence.js';
import { getProfileTargetUserId } from './state.js';

if (Number.isInteger(currentUserId) && currentUserId > 0) {
    bindHomeSocial();
    bindNotificationPage();
    bindProfileSocial();

    if (connection) {
        connection.onreconnecting(() => {
            setRealtimeReady(false);
        });

        connection.on('NotificationCountChanged', (payload) => setUnreadCount(Number.parseInt(`${payload.unreadCount ?? 0}`, 10)));
        connection.on('NotificationUpserted', (notification) => prependNotification(notification));
        connection.on('FriendRequestResolved', (payload) => {
            updateNotificationResolution(payload.requestId, payload.status);
            setUnreadCount(Number.parseInt(`${payload.unreadCount ?? 0}`, 10));
        });
        
        connection.on('FriendRequestStateChanged', (payload) => {
            const targetUserId = Number.parseInt(`${payload.userId ?? ''}`, 10);
            const state = `${payload.state ?? ''}`;
            if (!Number.isInteger(targetUserId) || targetUserId <= 0) return;

            setCandidateState(targetUserId, state);
            setFeedSuggestionState(targetUserId, state);
            if (getProfileTargetUserId() === targetUserId) {
                syncProfileState({
                    isFriend: state === 'friend',
                    hasOutgoingRequest: state === 'pending-sent',
                    hasIncomingRequest: state === 'pending-received'
                });
            }
        });

        connection.on('FriendshipAdded', (friend) => {
            upsertFriendCard(friend);
            setCandidateState(friend.userId, 'friend');
            setFeedSuggestionState(friend.userId, 'friend');
            if (getProfileTargetUserId() === friend.userId) {
                syncProfileState({
                    isFriend: true,
                    hasOutgoingRequest: false,
                    hasIncomingRequest: false,
                    isMessageBlockedByViewer: !!friend.isMessageBlockedByViewer,
                    isMessageBlockedByOtherUser: !!friend.isMessageBlockedByOtherUser
                });
            }
        });

        connection.on('FriendshipRemoved', (payload) => {
            const friendUserId = Number.parseInt(`${payload.friendUserId ?? ''}`, 10);
            if (!Number.isInteger(friendUserId) || friendUserId <= 0) return;

            removeFriendCard(friendUserId);
            setCandidateState(friendUserId, 'none');
            if (getProfileTargetUserId() === friendUserId) {
                syncProfileState({ isFriend: false, hasOutgoingRequest: false, hasIncomingRequest: false });
            }
        });

        connection.on('MessageBlockChanged', (payload) => {
            const targetUserId = Number.parseInt(`${payload.targetUserId ?? ''}`, 10);
            if (!Number.isInteger(targetUserId) || targetUserId <= 0) return;

            updateFriendCardBlockState(targetUserId, payload);
            updateChatState(payload);
            if (getProfileTargetUserId() === targetUserId) {
                syncProfileState({
                    isMessageBlockedByViewer: !!payload.isMessageBlockedByViewer,
                    isMessageBlockedByOtherUser: !!payload.isMessageBlockedByOtherUser
                });
            }
        });

        // Presence features
        connection.on('UserOnline', (payload) => {
            const userId = Number.parseInt(`${payload.userId ?? ''}`, 10);
            if (Number.isInteger(userId) && userId > 0) {
                setUserOnlineStatus(userId, true);
            }
        });

        connection.on('UserOffline', (payload) => {
            const userId = Number.parseInt(`${payload.userId ?? ''}`, 10);
            if (Number.isInteger(userId) && userId > 0) {
                setUserOnlineStatus(userId, false);
            }
        });

        connection.onreconnected(() => {
            setRealtimeReady(true);
            handleReconnectPresence();
        });

        connection.onclose(() => {
            stopHeartbeat();
        });

        connection.start()
            .then(() => {
                setRealtimeReady(true);
                startHeartbeat();
                return connection.invoke('GetOnlineUsers');
            })
            .then((onlineIds) => {
                if (Array.isArray(onlineIds)) {
                    onlineIds.forEach((id) => setUserOnlineStatus(id, true));
                }
            })
            .catch(() => {
                setRealtimeReady(false);
            });
    }
}
