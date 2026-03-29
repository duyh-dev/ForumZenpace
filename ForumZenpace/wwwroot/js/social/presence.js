import { connection, state } from './state.js';

let heartbeatInterval = null;

export const setUserOnlineStatus = (userId, isOnline) => {
    document.querySelectorAll(`[data-presence-user-id="${userId}"]`).forEach((dot) => {
        dot.classList.toggle('is-online', isOnline);
    });
};

export const startHeartbeat = () => {
    if (heartbeatInterval) return;
    heartbeatInterval = window.setInterval(() => {
        if (state.realtimeReady && connection) {
            connection.invoke('Heartbeat').catch(() => {});
        }
    }, 20000); // every 20 seconds
};

export const stopHeartbeat = () => {
    if (heartbeatInterval) {
        window.clearInterval(heartbeatInterval);
        heartbeatInterval = null;
    }
};

export const handleReconnectPresence = () => {
    startHeartbeat();
    connection.invoke('GetOnlineUsers')
        .then((onlineIds) => {
            if (Array.isArray(onlineIds)) {
                document.querySelectorAll('[data-presence-user-id]').forEach((dot) => {
                    const uid = Number.parseInt(dot.dataset.presenceUserId || '', 10);
                    dot.classList.toggle('is-online', onlineIds.includes(uid));
                });
            }
        })
        .catch(() => {});
};
