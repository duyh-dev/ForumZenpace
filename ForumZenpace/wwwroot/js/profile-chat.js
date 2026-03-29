(() => {
    const chatPanel = document.querySelector('[data-chat-panel]');
    const chatThread = document.querySelector('[data-chat-thread]');
    const chatForm = document.querySelector('[data-chat-form]');
    const chatInput = document.querySelector('[data-chat-input]');
    const chatSubmit = document.querySelector('[data-chat-submit]');
    const chatStatus = document.querySelector('[data-chat-status]');
    const chatTab = document.querySelector('[data-chat-tab]');
    const replyDraft = document.querySelector('[data-chat-reply-draft]');
    const replyInput = document.querySelector('[data-chat-reply-input]');
    const replySender = document.querySelector('[data-chat-reply-draft-sender]');
    const replyContent = document.querySelector('[data-chat-reply-draft-content]');
    const replyClear = document.querySelector('[data-chat-reply-clear]');
    const voicePopup = document.querySelector('[data-voice-popup]');
    const voiceEyebrow = document.querySelector('[data-voice-eyebrow]');
    const voiceStatus = document.querySelector('[data-voice-status]');
    const voiceNote = document.querySelector('[data-voice-note]');
    const voiceStart = document.querySelector('[data-voice-start]');
    const voiceAccept = voicePopup?.querySelector('[data-voice-accept]') || null;
    const voiceReject = voicePopup?.querySelector('[data-voice-reject]') || null;
    const voiceMute = voicePopup?.querySelector('[data-voice-mute]') || null;
    const voiceEnd = voicePopup?.querySelector('[data-voice-end]') || null;
    const remoteAudio = document.querySelector('[data-voice-remote]');
    let chatEmpty = document.querySelector('[data-chat-empty]');
    let chatCount = document.querySelector('[data-chat-count]');

    if (!(chatPanel instanceof HTMLElement)
        || !(chatThread instanceof HTMLElement)
        || !(chatForm instanceof HTMLFormElement)
        || !(chatInput instanceof HTMLTextAreaElement)
        || !(chatSubmit instanceof HTMLButtonElement)) {
        return;
    }

    const signalRClient = window.signalR;
    const currentUserId = Number.parseInt(chatPanel.dataset.currentUserId || '', 10);
    const targetUserId = Number.parseInt(chatPanel.dataset.targetUserId || '', 10);
    const targetUsername = chatPanel.dataset.targetUsername || '';
    const targetDisplayName = chatPanel.dataset.targetDisplayName || 'Nguoi dung';
    const hubUrl = chatPanel.dataset.hubUrl || '';
    const hasVoiceSupport = typeof window.RTCPeerConnection === 'function'
        && !!(navigator.mediaDevices && typeof navigator.mediaDevices.getUserMedia === 'function');
    const MediaStreamCtor = typeof window.MediaStream === 'function' ? window.MediaStream : null;
    const PeerConnectionCtor = typeof window.RTCPeerConnection === 'function' ? window.RTCPeerConnection : null;
    const rtcConfiguration = {
        iceServers: [
            { urls: 'stun:stun.l.google.com:19302' },
            { urls: 'stun:stun1.l.google.com:19302' }
        ]
    };
    const seenMessageIds = new Set(
        Array.from(chatThread.querySelectorAll('[data-chat-message-id]'))
            .map((element) => Number.parseInt(element.getAttribute('data-chat-message-id') || '', 10))
            .filter((messageId) => Number.isInteger(messageId) && messageId > 0)
    );

    let isSending = false;
    let realtimeReady = false;
    let connection = null;
    let jumpHighlightTimeoutId = 0;
    let voiceNoticeTimeoutId = 0;
    let activeVoiceNoticeMessage = '';
    let voiceRingTimeoutId = 0;
    let voiceState = 'idle';
    let voicePopupVisible = false;
    let activeVoiceSessionId = '';
    let localStream = null;
    let peerConnection = null;
    let remoteStream = null;
    let localMuted = false;
    let pendingIceCandidates = [];
    const getCanSendMessages = () => chatPanel.dataset.chatCanSend === 'true';
    const isMediaStream = (value) => !!MediaStreamCtor && value instanceof MediaStreamCtor;
    const isPeerConnection = (value) => !!PeerConnectionCtor && value instanceof PeerConnectionCtor;

    const autoResizeTextarea = () => {
        chatInput.style.height = 'auto';
        chatInput.style.height = `${Math.min(chatInput.scrollHeight, 180)}px`;
    };

    let typingIndicatorElement = null;
    let seenIndicatorElement = null;
    let typingTimeoutId = 0;
    let sendTypingTimeoutId = 0;

    const showTypingIndicator = () => {
        if (!typingIndicatorElement) {
            typingIndicatorElement = document.createElement('div');
            typingIndicatorElement.className = 'profile-chat-meta profile-chat-typing';
            typingIndicatorElement.textContent = `${targetDisplayName} đang nhập...`;
            chatThread.appendChild(typingIndicatorElement);
        }
        typingIndicatorElement.style.display = 'block';
        chatThread.scrollTop = chatThread.scrollHeight;

        clearTimeout(typingTimeoutId);
        typingTimeoutId = window.setTimeout(() => {
            if (typingIndicatorElement) typingIndicatorElement.style.display = 'none';
        }, 3000);
    };

    const markAsSeen = () => {
        if (!seenIndicatorElement) {
            seenIndicatorElement = document.createElement('div');
            seenIndicatorElement.className = 'profile-chat-meta profile-chat-seen';
            seenIndicatorElement.textContent = 'Đã xem';
        }
        chatThread.appendChild(seenIndicatorElement);
        chatThread.scrollTop = chatThread.scrollHeight;
    };

    const setStatus = (message, isError = false) => {
        if (!(chatStatus instanceof HTMLElement)) {
            return;
        }

        if (!message) {
            chatStatus.hidden = true;
            chatStatus.textContent = '';
            chatStatus.classList.remove('is-error');
            return;
        }

        chatStatus.hidden = false;
        chatStatus.textContent = message;
        chatStatus.classList.toggle('is-error', isError);
    };

    const extractErrorMessage = (error, fallbackMessage) => {
        if (error && typeof error === 'object' && typeof error.message === 'string' && error.message.trim().length > 0) {
            return error.message.trim();
        }

        return fallbackMessage;
    };

    const ensureCountBadge = () => {
        if (!(chatTab instanceof HTMLElement)) {
            return null;
        }

        if (chatCount instanceof HTMLElement) {
            return chatCount;
        }

        const badge = document.createElement('span');
        badge.className = 'profile-tab-count';
        badge.setAttribute('data-chat-count', '');
        badge.textContent = '0';
        chatTab.appendChild(badge);
        chatCount = badge;
        return badge;
    };

    const getReplyToMessageId = () => {
        if (!(replyInput instanceof HTMLInputElement)) {
            return null;
        }

        const value = Number.parseInt(replyInput.value || '', 10);
        return Number.isInteger(value) && value > 0 ? value : null;
    };

    const clearReplyDraft = ({ focusComposer = false } = {}) => {
        if (replyDraft instanceof HTMLElement) {
            replyDraft.hidden = true;
        }

        if (replyInput instanceof HTMLInputElement) {
            replyInput.value = '';
        }

        if (replySender instanceof HTMLElement) {
            replySender.textContent = '';
        }

        if (replyContent instanceof HTMLElement) {
            replyContent.textContent = '';
        }

        if (focusComposer) {
            chatInput.focus();
        }
    };

    const setReplyDraft = (messageId, senderLabel, content) => {
        if (!(replyDraft instanceof HTMLElement)
            || !(replyInput instanceof HTMLInputElement)
            || !(replySender instanceof HTMLElement)
            || !(replyContent instanceof HTMLElement)) {
            return;
        }

        replyInput.value = `${messageId}`;
        replySender.textContent = senderLabel || 'Nguoi dung';
        replyContent.textContent = content || '';
        replyDraft.hidden = false;
        chatInput.focus();
    };

    const updateComposerState = () => {
        const canSendMessages = getCanSendMessages();
        const hasContent = chatInput.value.trim().length > 0;
        chatInput.disabled = !canSendMessages;
        chatSubmit.disabled = !canSendMessages || isSending || !hasContent;
        autoResizeTextarea();
    };

    const getDefaultVoicePromptNote = () => localMuted
        ? `Goi voice voi ${targetDisplayName} ngay trong khung chat nay. Mic se duoc tat khi vao cuoc goi.`
        : `Goi voice voi ${targetDisplayName} ngay trong khung chat nay.`;

    const setVoicePromptDisplay = () => {
        setVoiceDisplay('Bat dau voice chat', getDefaultVoicePromptNote(), false, 'VOICE CHAT');
    };

    const setVoiceDisplay = (title, note = '', isError = false, eyebrow = 'VOICE CHAT') => {
        if (voiceEyebrow instanceof HTMLElement) {
            voiceEyebrow.textContent = eyebrow;
        }

        if (voiceStatus instanceof HTMLElement) {
            voiceStatus.textContent = title;
            voiceStatus.classList.toggle('is-error', isError);
        }

        if (voiceNote instanceof HTMLElement) {
            voiceNote.textContent = note;
            voiceNote.classList.toggle('is-error', isError);
        }
    };

    const setIncomingVoiceDisplay = (title, note = '') => setVoiceDisplay(title, note, false, 'VOICE CHAT');

    const clearVoiceNotice = () => {
        if (voiceNoticeTimeoutId) {
            window.clearTimeout(voiceNoticeTimeoutId);
            voiceNoticeTimeoutId = 0;
        }

        if (activeVoiceNoticeMessage && chatStatus instanceof HTMLElement && chatStatus.textContent === activeVoiceNoticeMessage) {
            setStatus('');
        }

        activeVoiceNoticeMessage = '';
    };

    const setIdleVoiceDisplay = () => {
        if (!hasVoiceSupport) {
            setVoiceDisplay('Voice chat khong ho tro', 'Can microphone va WebRTC de su dung tinh nang nay.', true);
            return;
        }

        if (!getCanSendMessages()) {
            setVoiceDisplay('Voice chat dang khoa', chatPanel.dataset.chatBlockMessage || 'Khong the bat dau voice chat luc nay.', true);
            return;
        }

        if (!realtimeReady) {
            setVoiceDisplay('Dang cho ket noi realtime', 'Voice chat se san sang sau khi kenh chat noi lai.');
            return;
        }

        setVoicePromptDisplay();
    };

    const queueVoiceNotice = (title, note = '', isError = false, durationMs = 4200) => {
        clearVoiceNotice();
        const message = [title, note].filter((part) => typeof part === 'string' && part.trim().length > 0).join(' - ');
        activeVoiceNoticeMessage = message;
        setStatus(message, isError);

        if (durationMs > 0) {
            voiceNoticeTimeoutId = window.setTimeout(() => {
                clearVoiceNotice();
            }, durationMs);
        }
    };

    const updateCallButtonState = () => {
        if (!(voiceStart instanceof HTMLButtonElement)) {
            return;
        }

        let disabledReason = '';
        if (voiceState === 'idle') {
            if (!hasVoiceSupport) {
                disabledReason = 'Trinh duyet khong ho tro voice chat.';
            } else if (!getCanSendMessages()) {
                disabledReason = chatPanel.dataset.chatBlockMessage || 'Khong the bat dau voice chat luc nay.';
            } else if (!realtimeReady) {
                disabledReason = 'Dang cho ket noi realtime.';
            }
        }

        const label = disabledReason
            || (voiceState === 'prompt'
                ? 'Dong popup voice chat'
                : voiceState === 'outgoing'
                    ? (voicePopupVisible ? 'An popup goi voice' : 'Mo popup goi voice')
                    : voiceState === 'connecting'
                        ? (voicePopupVisible ? 'An popup voice chat' : 'Mo popup voice chat')
                        : voiceState === 'active'
                            ? (voicePopupVisible ? 'An dieu khien voice chat' : 'Mo dieu khien voice chat')
                            : `Goi voice voi ${targetDisplayName}`);

        voiceStart.hidden = false;
        voiceStart.disabled = voiceState === 'idle' && !!disabledReason;
        voiceStart.title = label;
        voiceStart.setAttribute('aria-label', label);
    };

    const updateMuteButton = () => {
        if (!(voiceMute instanceof HTMLButtonElement)) {
            return;
        }

        const icon = voiceMute.querySelector('i');
        const label = voiceMute.querySelector('span');

        voiceMute.classList.toggle('is-muted', localMuted);
        if (icon instanceof HTMLElement) {
            icon.className = localMuted
                ? 'fa-solid fa-microphone-slash'
                : 'fa-solid fa-microphone';
        }

        if (label instanceof HTMLElement) {
            label.textContent = localMuted ? 'Bat mic' : 'Tat mic';
        }
    };

    const updateVoiceControls = () => {
        const showIncoming = voiceState === 'incoming';
        const showPrompt = voiceState === 'prompt';
        const showManagedCall = (voiceState === 'outgoing'
            || voiceState === 'connecting'
            || voiceState === 'active')
            && voicePopupVisible;
        const showPopup = voiceState === 'incoming'
            || voiceState === 'prompt'
            || showManagedCall;

        if (voicePopup instanceof HTMLElement) {
            voicePopup.hidden = !showPopup;
        }

        if (voiceAccept instanceof HTMLButtonElement) {
            const showAccept = showIncoming || showPrompt;
            voiceAccept.hidden = !showAccept;
            voiceAccept.disabled = !showAccept;
        }

        if (voiceReject instanceof HTMLButtonElement) {
            const showReject = showIncoming || showPrompt;
            voiceReject.hidden = !showReject;
            voiceReject.disabled = !showReject;
        }

        if (voiceMute instanceof HTMLButtonElement) {
            const showMute = showPrompt
                || voiceState === 'outgoing'
                || voiceState === 'connecting'
                || voiceState === 'active';
            voiceMute.hidden = !showMute;
            voiceMute.disabled = !showMute;
        }

        if (voiceEnd instanceof HTMLButtonElement) {
            const showEnd = showPrompt
                || voiceState === 'outgoing'
                || voiceState === 'connecting'
                || voiceState === 'active';
            voiceEnd.hidden = !showEnd;
            voiceEnd.disabled = !showEnd;

            const icon = voiceEnd.querySelector('i');
            const label = voiceEnd.querySelector('span');
            if (icon instanceof HTMLElement) {
                icon.className = voiceState === 'outgoing'
                    ? 'fa-solid fa-xmark'
                    : 'fa-solid fa-phone-slash';
            }

            if (label instanceof HTMLElement) {
                label.textContent = voiceState === 'outgoing' ? 'Huy goi' : 'Ket thuc';
            }
        }

        const actionButtons = [voiceReject, voiceAccept, voiceMute, voiceEnd]
            .filter((button) => button instanceof HTMLButtonElement);
        actionButtons.forEach((button) => button.classList.remove('is-single'));

        const visibleButtons = actionButtons.filter((button) => !button.hidden);
        if (visibleButtons.length === 1) {
            visibleButtons[0].classList.add('is-single');
        }

        updateCallButtonState();
        updateMuteButton();
    };

    const stopMediaStream = (stream) => {
        if (!isMediaStream(stream)) {
            return;
        }

        stream.getTracks().forEach((track) => track.stop());
    };

    const clearVoiceRingTimeout = () => {
        if (voiceRingTimeoutId) {
            window.clearTimeout(voiceRingTimeoutId);
            voiceRingTimeoutId = 0;
        }
    };

    const resetRemoteAudio = () => {
        if (!(remoteAudio instanceof HTMLAudioElement)) {
            return;
        }

        remoteAudio.pause();
        remoteAudio.srcObject = null;
        remoteAudio.removeAttribute('src');
    };

    const resetVoiceSession = () => {
        clearVoiceRingTimeout();
        pendingIceCandidates = [];
        activeVoiceSessionId = '';
        voiceState = 'idle';
        voicePopupVisible = false;
        localMuted = false;

        if (isPeerConnection(peerConnection)) {
            peerConnection.ontrack = null;
            peerConnection.onicecandidate = null;
            peerConnection.onconnectionstatechange = null;
            peerConnection.close();
        }

        peerConnection = null;
        stopMediaStream(localStream);
        localStream = null;
        remoteStream = null;
        resetRemoteAudio();
        updateVoiceControls();
    };

    const sendVoiceEnd = async (sessionId, reason) => {
        if (!connection || !realtimeReady || !sessionId) {
            return;
        }

        try {
            await connection.invoke('EndVoiceChat', {
                targetUserId,
                sessionId,
                reason
            });
        } catch {
            // No-op. The local session should still be cleaned up.
        }
    };

    const closeVoiceSession = async (title, note, { isError = false, notifyRemote = false } = {}) => {
        const sessionId = activeVoiceSessionId;
        resetVoiceSession();
        if (notifyRemote && sessionId) {
            await sendVoiceEnd(sessionId, note || title);
        }

        queueVoiceNotice(title, note, isError);
    };

    const ensureLocalStream = async () => {
        if (isMediaStream(localStream)) {
            return localStream;
        }

        const stream = await navigator.mediaDevices.getUserMedia({
            audio: {
                echoCancellation: true,
                noiseSuppression: true
            }
        });

        localStream = stream;
        localStream.getAudioTracks().forEach((track) => {
            track.enabled = !localMuted;
        });
        updateVoiceControls();
        return stream;
    };

    const relayVoiceSignal = async (payload) => {
        if (!connection || !realtimeReady || !activeVoiceSessionId) {
            return;
        }

        await connection.invoke('RelayVoiceChatSignal', {
            targetUserId,
            sessionId: activeVoiceSessionId,
            ...payload
        });
    };

    const flushPendingIceCandidates = async () => {
        if (!isPeerConnection(peerConnection) || !peerConnection.remoteDescription) {
            return;
        }

        const queuedCandidates = [...pendingIceCandidates];
        pendingIceCandidates = [];

        for (const candidateSignal of queuedCandidates) {
            try {
                await peerConnection.addIceCandidate({
                    candidate: candidateSignal.candidate,
                    sdpMid: candidateSignal.sdpMid || null,
                    sdpMLineIndex: Number.isInteger(candidateSignal.sdpMLineIndex) ? candidateSignal.sdpMLineIndex : null
                });
            } catch {
                // Ignore stale or invalid ICE candidates.
            }
        }
    };

    const createPeerConnection = async (sessionId) => {
        if (!PeerConnectionCtor) {
            throw new Error('Trinh duyet khong ho tro voice chat.');
        }

        if (isPeerConnection(peerConnection)) {
            return peerConnection;
        }

        const stream = await ensureLocalStream();
        remoteStream = MediaStreamCtor ? new MediaStreamCtor() : null;
        peerConnection = new PeerConnectionCtor(rtcConfiguration);

        stream.getTracks().forEach((track) => {
            peerConnection.addTrack(track, stream);
        });

        peerConnection.ontrack = (event) => {
            if (!isMediaStream(remoteStream)) {
                remoteStream = MediaStreamCtor ? new MediaStreamCtor() : null;
            }

            event.streams.forEach((streamItem) => {
                streamItem.getTracks().forEach((track) => {
                    if (!isMediaStream(remoteStream)) {
                        return;
                    }

                    if (!remoteStream.getTracks().some((existingTrack) => existingTrack.id === track.id)) {
                        remoteStream.addTrack(track);
                    }
                });
            });

            if (remoteAudio instanceof HTMLAudioElement && isMediaStream(remoteStream)) {
                remoteAudio.srcObject = remoteStream;
                remoteAudio.play().catch(() => {});
            }
        };

        peerConnection.onicecandidate = (event) => {
            if (!event.candidate || activeVoiceSessionId !== sessionId) {
                return;
            }

            relayVoiceSignal({
                type: 'ice-candidate',
                candidate: event.candidate.candidate,
                sdpMid: event.candidate.sdpMid,
                sdpMLineIndex: event.candidate.sdpMLineIndex
            }).catch(() => {});
        };

        peerConnection.onconnectionstatechange = () => {
            if (!isPeerConnection(peerConnection)) {
                return;
            }

            if (peerConnection.connectionState === 'connected') {
                voiceState = 'active';
                voicePopupVisible = false;
                updateVoiceControls();
                setVoiceDisplay('Voice chat dang hoat dong', `Dang noi chuyen voi ${targetDisplayName}.`);
                return;
            }

            if (peerConnection.connectionState === 'failed') {
                closeVoiceSession('Voice chat bi gian doan', 'Khong the duy tri ket noi audio.', { isError: true, notifyRemote: true }).catch(() => {});
            }
        };

        return peerConnection;
    };

    const startVoiceRingTimeout = () => {
        clearVoiceRingTimeout();
        voiceRingTimeoutId = window.setTimeout(() => {
            if (voiceState !== 'outgoing' || !activeVoiceSessionId) {
                return;
            }

            closeVoiceSession('Khong co phan hoi', `${targetDisplayName} chua san sang voice chat.`, {
                isError: true,
                notifyRemote: true
            }).catch(() => {});
        }, 35000);
    };

    const openVoiceCallPrompt = () => {
        if (voiceState !== 'idle') {
            return;
        }

        clearVoiceNotice();

        if (!getCanSendMessages()) {
            queueVoiceNotice('Voice chat dang khoa', chatPanel.dataset.chatBlockMessage || 'Khong the bat dau voice chat luc nay.', true);
            return;
        }

        if (!connection || !realtimeReady) {
            queueVoiceNotice('Realtime chua san sang', 'Can ket noi realtime truoc khi goi voice.', true);
            return;
        }

        if (!hasVoiceSupport) {
            queueVoiceNotice('Trinh duyet khong ho tro voice chat', 'Can microphone va WebRTC de su dung tinh nang nay.', true);
            return;
        }

        voiceState = 'prompt';
        voicePopupVisible = true;
        setVoicePromptDisplay();
        updateVoiceControls();
    };

    const closeVoicePrompt = () => {
        if (voiceState !== 'prompt') {
            return;
        }

        voiceState = 'idle';
        voicePopupVisible = false;
        updateVoiceControls();
    };

    const startVoiceChat = async () => {
        if (voiceState !== 'idle' && voiceState !== 'prompt') {
            return;
        }

        clearVoiceNotice();

        if (!getCanSendMessages()) {
            queueVoiceNotice('Voice chat dang khoa', chatPanel.dataset.chatBlockMessage || 'Khong the bat dau voice chat luc nay.', true);
            return;
        }

        if (!connection || !realtimeReady) {
            queueVoiceNotice('Realtime chua san sang', 'Can ket noi realtime truoc khi goi voice.', true);
            return;
        }

        if (!hasVoiceSupport) {
            queueVoiceNotice('Trinh duyet khong ho tro voice chat', 'Can microphone va WebRTC de su dung tinh nang nay.', true);
            return;
        }

        activeVoiceSessionId = typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
            ? crypto.randomUUID()
            : `voice-${Date.now()}-${Math.random().toString(16).slice(2)}`;
        voiceState = 'outgoing';
        voicePopupVisible = true;
        setVoiceDisplay('Dang goi voice...', `Dang cho ${targetDisplayName} tra loi.`, false, 'VOICE CHAT');
        updateVoiceControls();

        try {
            await connection.invoke('StartVoiceChat', {
                targetUserId,
                sessionId: activeVoiceSessionId
            });
            startVoiceRingTimeout();
        } catch (error) {
            const message = extractErrorMessage(error, 'Khong the bat dau voice chat luc nay.');
            resetVoiceSession();
            queueVoiceNotice(message, '', true);
        }
    };

    const acceptVoiceChat = async () => {
        if (voiceState !== 'incoming' || !activeVoiceSessionId || !connection || !realtimeReady) {
            return;
        }

        try {
            clearVoiceNotice();
            voiceState = 'connecting';
            voicePopupVisible = true;
            setVoiceDisplay('Dang ket noi voice chat', 'Dang mo microphone va cho trao doi ket noi.');
            updateVoiceControls();
            await createPeerConnection(activeVoiceSessionId);
            await connection.invoke('AcceptVoiceChat', {
                targetUserId,
                sessionId: activeVoiceSessionId
            });
        } catch (error) {
            const sessionId = activeVoiceSessionId;
            const message = extractErrorMessage(error, 'Khong the mo microphone de nhan cuoc goi.');
            resetVoiceSession();
            queueVoiceNotice(message, '', true);

            if (sessionId) {
                connection.invoke('RejectVoiceChat', {
                    targetUserId,
                    sessionId,
                    reason: message
                }).catch(() => {});
            }
        }
    };

    const rejectVoiceChat = async (reason) => {
        if (voiceState !== 'incoming' || !activeVoiceSessionId || !connection || !realtimeReady) {
            return;
        }

        const sessionId = activeVoiceSessionId;
        resetVoiceSession();
        queueVoiceNotice('Da tu choi cuoc goi', reason || `Da tu choi voice chat voi ${targetDisplayName}.`, true);

        try {
            await connection.invoke('RejectVoiceChat', {
                targetUserId,
                sessionId,
                reason
            });
        } catch {
            // No-op.
        }
    };

    const toggleMute = () => {
        localMuted = !localMuted;

        if (isMediaStream(localStream)) {
            localStream.getAudioTracks().forEach((track) => {
                track.enabled = !localMuted;
            });
        }

        if (voiceState === 'prompt') {
            setVoicePromptDisplay();
            updateVoiceControls();
            return;
        }

        if (voiceState === 'outgoing') {
            setVoiceDisplay(
                'Dang goi voice...',
                localMuted
                    ? `Dang cho ${targetDisplayName} tra loi. Mic se duoc tat khi vao cuoc goi.`
                    : `Dang cho ${targetDisplayName} tra loi.`,
                false,
                'VOICE CHAT'
            );
            updateVoiceControls();
            return;
        }

        setVoiceDisplay(
            'Voice chat dang hoat dong',
            localMuted ? 'Mic dang tat. Ban van nghe duoc ben kia.' : `Dang noi chuyen voi ${targetDisplayName}.`,
            false,
            'VOICE CHAT'
        );
        updateVoiceControls();
    };

    const handleVoiceAccepted = async (event) => {
        if (voiceState !== 'outgoing' || !activeVoiceSessionId || event.sessionId !== activeVoiceSessionId) {
            return;
        }

        clearVoiceRingTimeout();
        clearVoiceNotice();
        voiceState = 'connecting';
        voicePopupVisible = true;
        setVoiceDisplay('Dang ket noi voice chat', `Dang tao ket noi audio voi ${targetDisplayName}.`, false, 'VOICE CHAT');
        updateVoiceControls();

        try {
            const rtcPeerConnection = await createPeerConnection(activeVoiceSessionId);
            const offer = await rtcPeerConnection.createOffer();
            await rtcPeerConnection.setLocalDescription(offer);
            await relayVoiceSignal({
                type: 'offer',
                sdp: offer.sdp || ''
            });
        } catch (error) {
            await closeVoiceSession(
                'Khong the khoi tao voice chat',
                extractErrorMessage(error, 'Khong the bat dau ket noi audio.'),
                { isError: true, notifyRemote: true }
            );
        }
    };

    const handleVoiceSignal = async (signal) => {
        if (!activeVoiceSessionId || signal.sessionId !== activeVoiceSessionId) {
            return;
        }

        try {
            if (signal.type === 'offer') {
                const rtcPeerConnection = await createPeerConnection(activeVoiceSessionId);
                clearVoiceNotice();
                voiceState = 'connecting';
                voicePopupVisible = true;
                setVoiceDisplay('Dang ket noi voice chat', `Dang dong bo audio voi ${targetDisplayName}.`, false, 'VOICE CHAT');
                updateVoiceControls();
                await rtcPeerConnection.setRemoteDescription({
                    type: 'offer',
                    sdp: signal.sdp || ''
                });
                await flushPendingIceCandidates();
                const answer = await rtcPeerConnection.createAnswer();
                await rtcPeerConnection.setLocalDescription(answer);
                await relayVoiceSignal({
                    type: 'answer',
                    sdp: answer.sdp || ''
                });
                return;
            }

            if (signal.type === 'answer') {
                if (!isPeerConnection(peerConnection)) {
                    return;
                }

                await peerConnection.setRemoteDescription({
                    type: 'answer',
                    sdp: signal.sdp || ''
                });
                await flushPendingIceCandidates();
                return;
            }

            if (signal.type === 'ice-candidate') {
                if (!isPeerConnection(peerConnection) || !peerConnection.remoteDescription) {
                    pendingIceCandidates.push(signal);
                    return;
                }

                await peerConnection.addIceCandidate({
                    candidate: signal.candidate,
                    sdpMid: signal.sdpMid || null,
                    sdpMLineIndex: Number.isInteger(signal.sdpMLineIndex) ? signal.sdpMLineIndex : null
                });
            }
        } catch (error) {
            await closeVoiceSession(
                'Voice chat bi gian doan',
                extractErrorMessage(error, 'Khong the tiep tuc ket noi audio.'),
                { isError: true, notifyRemote: true }
            );
        }
    };

    const createReplyPreviewElement = (replyTo) => {
        const replyToMessageId = Number.parseInt(`${replyTo?.messageId ?? ''}`, 10);
        if (!Number.isInteger(replyToMessageId) || replyToMessageId <= 0) {
            return null;
        }

        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'profile-chat-reply-preview';
        button.setAttribute('data-chat-jump-to', `${replyToMessageId}`);
        button.setAttribute('aria-label', 'Mo tin nhan duoc tra loi');

        const label = document.createElement('span');
        label.className = 'profile-chat-reply-preview__label';
        const isOwnReplyTarget = Number.parseInt(`${replyTo?.senderId ?? ''}`, 10) === currentUserId;
        label.textContent = `Tra loi ${isOwnReplyTarget ? 'Ban' : (replyTo?.senderDisplayName || 'Nguoi dung')}`;

        const content = document.createElement('span');
        content.className = 'profile-chat-reply-preview__content';
        content.textContent = replyTo?.content || '';

        button.append(label, content);
        return button;
    };

    const createReplyActionButton = (messageId, senderLabel, content) => {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'profile-chat-meta-action';
        button.setAttribute('data-chat-reply-trigger', '');
        button.setAttribute('data-reply-message-id', `${messageId}`);
        button.setAttribute('data-reply-sender', senderLabel);
        button.setAttribute('data-reply-content', content);
        button.textContent = 'Tra loi';
        return button;
    };

    const createMessageElement = (message) => {
        const messageId = Number.parseInt(`${message.id ?? ''}`, 10);
        const isOwnMessage = Number.parseInt(`${message.senderId ?? ''}`, 10) === currentUserId;
        const senderLabel = isOwnMessage ? 'Ban' : (message.senderDisplayName || 'Nguoi dung');
        const article = document.createElement('article');
        article.className = 'profile-chat-message';
        if (Number.isInteger(messageId) && messageId > 0) {
            article.setAttribute('data-chat-message-id', `${messageId}`);
        }

        if (isOwnMessage) {
            article.classList.add('profile-chat-message--own');
        }

        const bubble = document.createElement('div');
        bubble.className = 'profile-chat-bubble';

        const replyPreview = createReplyPreviewElement(message.replyTo);
        if (replyPreview instanceof HTMLElement) {
            bubble.appendChild(replyPreview);
        }

        const text = document.createElement('div');
        text.className = 'profile-chat-text';
        text.textContent = message.content || '';
        bubble.appendChild(text);

        const meta = document.createElement('div');
        meta.className = 'profile-chat-meta';

        const sender = document.createElement('span');
        sender.textContent = senderLabel;
        meta.appendChild(sender);

        const dot = document.createElement('span');
        dot.className = 'dot';
        meta.appendChild(dot);

        const time = document.createElement('time');
        if (typeof message.createdAtIso === 'string' && message.createdAtIso.length > 0) {
            time.dateTime = message.createdAtIso;
        }

        time.textContent = message.createdAtDisplay || '';
        meta.appendChild(time);
        meta.appendChild(createReplyActionButton(messageId, senderLabel, message.content || ''));

        article.append(bubble, meta);
        return article;
    };

    const appendMessage = (message) => {
        const messageId = Number.parseInt(`${message.id ?? ''}`, 10);
        if (Number.isInteger(messageId) && messageId > 0) {
            if (seenMessageIds.has(messageId)) {
                return;
            }

            seenMessageIds.add(messageId);
        }

        if (chatEmpty instanceof HTMLElement) {
            chatEmpty.remove();
            chatEmpty = null;
        }

        chatThread.appendChild(createMessageElement(message));
        chatThread.scrollTop = chatThread.scrollHeight;
        if (typingIndicatorElement) typingIndicatorElement.style.display = 'none';
        if (seenIndicatorElement && document.contains(seenIndicatorElement)) {
            chatThread.appendChild(seenIndicatorElement);
        }

        const badge = ensureCountBadge();
        if (badge instanceof HTMLElement) {
            const nextCount = Number.parseInt(badge.textContent || '0', 10) + 1;
            badge.textContent = `${nextCount}`;
        }

        if (Number.parseInt(`${message.senderId ?? ''}`, 10) !== currentUserId && connection && realtimeReady) {
            connection.invoke('MarkConversationAsRead', targetUserId).catch(() => {});
        }
    };

    const sendViaHttp = async () => {
        const response = await fetch(chatForm.action, {
            method: 'POST',
            body: new FormData(chatForm),
            headers: { 'X-Requested-With': 'XMLHttpRequest' }
        });

        const data = await response.json();
        if (!response.ok || !data.success || !data.message) {
            throw new Error(data.message || 'Khong the gui tin nhan luc nay.');
        }

        appendMessage(data.message);
    };

    const highlightMessage = (messageId) => {
        const targetMessage = chatThread.querySelector(`[data-chat-message-id="${messageId}"]`);
        if (!(targetMessage instanceof HTMLElement)) {
            return;
        }

        targetMessage.scrollIntoView({ behavior: 'smooth', block: 'center' });
        targetMessage.classList.add('is-flash');

        if (jumpHighlightTimeoutId) {
            window.clearTimeout(jumpHighlightTimeoutId);
        }

        jumpHighlightTimeoutId = window.setTimeout(() => {
            targetMessage.classList.remove('is-flash');
            jumpHighlightTimeoutId = 0;
        }, 1800);
    };

    const handleThreadClick = (event) => {
        const target = event.target;
        if (!(target instanceof Element)) {
            return;
        }

        const replyTrigger = target.closest('[data-chat-reply-trigger]');
        if (replyTrigger instanceof HTMLElement) {
            const messageId = Number.parseInt(replyTrigger.dataset.replyMessageId || '', 10);
            if (Number.isInteger(messageId) && messageId > 0) {
                setReplyDraft(
                    messageId,
                    replyTrigger.dataset.replySender || 'Nguoi dung',
                    replyTrigger.dataset.replyContent || ''
                );
                updateComposerState();
            }

            return;
        }

        const jumpTrigger = target.closest('[data-chat-jump-to]');
        if (jumpTrigger instanceof HTMLElement) {
            const messageId = Number.parseInt(jumpTrigger.dataset.chatJumpTo || jumpTrigger.getAttribute('data-chat-jump-to') || '', 10);
            if (Number.isInteger(messageId) && messageId > 0) {
                highlightMessage(messageId);
            }
        }
    };

    const connectRealtime = async () => {
        if (!signalRClient
            || typeof signalRClient.HubConnectionBuilder !== 'function'
            || !Number.isInteger(currentUserId)
            || currentUserId <= 0
            || !Number.isInteger(targetUserId)
            || targetUserId <= 0
            || !hubUrl) {
            setIdleVoiceDisplay();
            return;
        }

        connection = new signalRClient.HubConnectionBuilder()
            .withUrl(hubUrl)
            .withAutomaticReconnect()
            .build();

        connection.on('DirectMessageReceived', (message) => {
            if (typingIndicatorElement) typingIndicatorElement.style.display = 'none';
            appendMessage(message);
        });

        connection.on('TypingIndicatorReceived', (senderUserId) => {
            if (senderUserId === targetUserId) {
                showTypingIndicator();
            }
        });

        connection.on('MessagesSeen', (viewerUserId) => {
            if (viewerUserId === targetUserId) {
                markAsSeen();
            }
        });

        connection.on('VoiceChatIncoming', (payload) => {
            if (Number.parseInt(`${payload?.senderUserId ?? ''}`, 10) === currentUserId) {
                return;
            }

            if (voiceState !== 'idle') {
                if (connection && realtimeReady) {
                    connection.invoke('RejectVoiceChat', {
                        targetUserId,
                        sessionId: payload?.sessionId || '',
                        reason: 'Nguoi dung dang o cuoc goi khac.'
                    }).catch(() => {});
                }

                return;
            }

            activeVoiceSessionId = typeof payload?.sessionId === 'string' ? payload.sessionId : '';
            if (!activeVoiceSessionId) {
                return;
            }

            clearVoiceNotice();
            voiceState = 'incoming';
            voicePopupVisible = true;
            setIncomingVoiceDisplay(
                `${targetDisplayName} dang goi voice`,
                'Nhan Chap nhan de bat dau noi chuyen hoac Tu choi de bo qua cuoc goi nay.'
            );
            updateVoiceControls();
        });

        connection.on('VoiceChatAccepted', (payload) => {
            handleVoiceAccepted(payload).catch(() => {});
        });

        connection.on('VoiceChatRejected', (payload) => {
            if (!activeVoiceSessionId || payload?.sessionId !== activeVoiceSessionId) {
                return;
            }

            resetVoiceSession();
            queueVoiceNotice('Cuoc goi bi tu choi', payload?.reason || `${targetDisplayName} da tu choi voice chat.`, true);
        });

        connection.on('VoiceChatEnded', (payload) => {
            if (!activeVoiceSessionId || payload?.sessionId !== activeVoiceSessionId) {
                return;
            }

            resetVoiceSession();
            queueVoiceNotice('Voice chat da ket thuc', payload?.reason || 'Cuoc goi da dong.', false);
        });

        connection.on('VoiceChatSignalReceived', (payload) => {
            handleVoiceSignal(payload).catch(() => {});
        });

        connection.onreconnecting(() => {
            realtimeReady = false;
            updateComposerState();
            updateVoiceControls();
            setStatus('Ket noi realtime dang duoc khoi phuc...');
            if (voiceState === 'idle') {
                setIdleVoiceDisplay();
            }
        });

        connection.onreconnected(async () => {
            try {
                await connection.invoke('JoinConversation', targetUserId);
                realtimeReady = true;
                updateComposerState();
                updateVoiceControls();
                setStatus('');
                if (voiceState === 'idle') {
                    setIdleVoiceDisplay();
                }
            } catch {
                realtimeReady = false;
                updateComposerState();
                updateVoiceControls();
                setStatus('Realtime tam thoi gian doan. He thong se tiep tuc gui thuong.');
                if (voiceState === 'idle') {
                    setIdleVoiceDisplay();
                }
            }
        });

        connection.onclose(() => {
            realtimeReady = false;
            updateComposerState();
            updateVoiceControls();
            setStatus('Realtime tam thoi gian doan. He thong se tiep tuc gui thuong.');
            if (voiceState === 'idle') {
                setIdleVoiceDisplay();
            }
        });

        try {
            await connection.start();
            await connection.invoke('JoinConversation', targetUserId);
            realtimeReady = true;
            updateComposerState();
            updateVoiceControls();
            setStatus('');
            setIdleVoiceDisplay();
        } catch {
            realtimeReady = false;
            updateComposerState();
            updateVoiceControls();
            setStatus('Khong the ket noi realtime. He thong se tiep tuc gui thuong.');
            setIdleVoiceDisplay();
        }
    };

    chatThread.scrollTop = chatThread.scrollHeight;
    updateComposerState();
    updateVoiceControls();
    setIdleVoiceDisplay();

    if (!getCanSendMessages() && chatPanel.dataset.chatBlockMessage) {
        setStatus(chatPanel.dataset.chatBlockMessage, true);
    }

    chatInput.addEventListener('input', () => {
        updateComposerState();
        if (connection && realtimeReady) {
            clearTimeout(sendTypingTimeoutId);
            sendTypingTimeoutId = window.setTimeout(() => {
                connection.invoke('SendTypingIndicator', targetUserId).catch(() => {});
            }, 800);
        }
    });
    chatInput.addEventListener('keydown', (event) => {
        if (!getCanSendMessages()) {
            return;
        }

        if (event.key !== 'Enter' || event.shiftKey || event.isComposing) {
            return;
        }

        event.preventDefault();
        if (chatInput.value.trim().length === 0) {
            return;
        }

        chatForm.requestSubmit();
    });

    chatForm.addEventListener('submit', async (event) => {
        event.preventDefault();

        const content = chatInput.value.trim();
        if (!getCanSendMessages()) {
            setStatus(chatPanel.dataset.chatBlockMessage || 'Khong the gui tin nhan luc nay.', true);
            return;
        }

        if (!content) {
            setStatus('Noi dung tin nhan khong duoc de trong.', true);
            return;
        }

        isSending = true;
        updateComposerState();
        setStatus('');

        try {
            if (connection && realtimeReady) {
                await connection.invoke('SendDirectMessage', {
                    targetUserId,
                    username: targetUsername,
                    content,
                    replyToMessageId: getReplyToMessageId()
                });
            } else {
                await sendViaHttp();
            }

            chatInput.value = '';
            clearReplyDraft();
            setStatus('');
            updateComposerState();
            chatInput.focus();
        } catch (error) {
            setStatus(extractErrorMessage(error, 'Khong the gui tin nhan luc nay.'), true);
        } finally {
            isSending = false;
            updateComposerState();
        }
    });

    chatThread.addEventListener('click', handleThreadClick);
    if (replyClear instanceof HTMLButtonElement) {
        replyClear.addEventListener('click', () => {
            clearReplyDraft({ focusComposer: true });
            updateComposerState();
        });
    }

    if (voiceStart instanceof HTMLButtonElement) {
        voiceStart.addEventListener('click', () => {
            if (voiceState === 'idle') {
                startVoiceChat().catch(() => {});
                return;
            }

            if (voiceState === 'prompt') {
                closeVoicePrompt();
                return;
            }

            if (voiceState === 'outgoing' || voiceState === 'connecting' || voiceState === 'active') {
                voicePopupVisible = !voicePopupVisible;
                updateVoiceControls();
            }
        });
    }

    if (voiceAccept instanceof HTMLButtonElement) {
        voiceAccept.addEventListener('click', () => {
            if (voiceState === 'prompt') {
                startVoiceChat().catch(() => {});
                return;
            }

            acceptVoiceChat().catch(() => {});
        });
    }

    if (voiceReject instanceof HTMLButtonElement) {
        voiceReject.addEventListener('click', () => {
            if (voiceState === 'prompt') {
                closeVoicePrompt();
                return;
            }

            rejectVoiceChat(`Da tu choi voice chat voi ${targetDisplayName}.`).catch(() => {});
        });
    }

    if (voiceMute instanceof HTMLButtonElement) {
        voiceMute.addEventListener('click', toggleMute);
    }

    if (voiceEnd instanceof HTMLButtonElement) {
        voiceEnd.addEventListener('click', () => {
            if (voiceState === 'prompt') {
                closeVoicePrompt();
                return;
            }

            closeVoiceSession('Voice chat da ket thuc', `Da dong cuoc goi voi ${targetDisplayName}.`, {
                notifyRemote: true
            }).catch(() => {});
        });
    }

    document.addEventListener('zenpace:chat-state-changed', () => {
        updateComposerState();
        updateVoiceControls();

        if (!getCanSendMessages() && chatPanel.dataset.chatBlockMessage) {
            setStatus(chatPanel.dataset.chatBlockMessage, true);
        } else {
            setStatus('');
        }

        if (voiceState === 'idle') {
            setIdleVoiceDisplay();
        }
    });

    window.addEventListener('beforeunload', () => {
        if (connection && realtimeReady && activeVoiceSessionId) {
            connection.send('EndVoiceChat', {
                targetUserId,
                sessionId: activeVoiceSessionId,
                reason: 'Cuoc goi da ket thuc.'
            }).catch(() => {});
        }

        resetVoiceSession();
    });

    connectRealtime().catch(() => {});
})();
