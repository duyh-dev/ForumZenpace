(() => {
    const root = document.querySelector('[data-story-music-player]');
    if (!(root instanceof HTMLElement)) {
        return;
    }

    const toggleButton = root.querySelector('[data-story-player-toggle]');
    const toggleIcon = root.querySelector('[data-story-player-toggle-icon]');
    const rangeInput = root.querySelector('[data-story-player-range]');
    const currentTimeText = root.querySelector('[data-story-player-current]');
    const durationText = root.querySelector('[data-story-player-duration]');
    const audioElement = root.querySelector('[data-story-player-audio]');
    const youTubeHost = root.querySelector('[data-story-player-youtube-host]');
    const spotifyHost = root.querySelector('[data-story-player-spotify-host]');

    if (!(toggleButton instanceof HTMLButtonElement)
        || !(toggleIcon instanceof HTMLElement)
        || !(rangeInput instanceof HTMLInputElement)
        || !(currentTimeText instanceof HTMLElement)
        || !(durationText instanceof HTMLElement)) {
        return;
    }

    const playerKind = (root.dataset.playerKind || '').trim().toLowerCase();
    const playerKey = (root.dataset.playerKey || '').trim();
    const playerUri = (root.dataset.playerUri || '').trim();
    const canSeek = (root.dataset.canSeek || '').trim().toLowerCase() === 'true';

    const state = {
        isReady: false,
        isPlaying: false,
        currentTime: 0,
        duration: 0,
        isSeeking: false
    };

    let pollTimerId = 0;
    let autoplayAttempted = false;
    let isDestroyed = false;
    let controller = null;

    const toFiniteSeconds = (value) => {
        const numericValue = Number(value);
        return Number.isFinite(numericValue) && numericValue > 0 ? numericValue : 0;
    };

    const formatTime = (totalSeconds) => {
        const safeSeconds = Math.max(0, Math.floor(toFiniteSeconds(totalSeconds)));
        const minutes = Math.floor(safeSeconds / 60);
        const seconds = safeSeconds % 60;
        return `${minutes}:${String(seconds).padStart(2, '0')}`;
    };

    const updateProgressUi = (currentTime, duration) => {
        const safeDuration = toFiniteSeconds(duration);
        const safeCurrentTime = Math.min(toFiniteSeconds(currentTime), safeDuration || toFiniteSeconds(currentTime));
        const progressPercentage = safeDuration > 0
            ? Math.min(100, (safeCurrentTime / safeDuration) * 100)
            : 0;

        currentTimeText.textContent = formatTime(safeCurrentTime);
        durationText.textContent = formatTime(safeDuration);
        rangeInput.max = String(safeDuration > 0 ? safeDuration : 100);
        rangeInput.value = String(safeDuration > 0 ? safeCurrentTime : 0);
        rangeInput.disabled = !state.isReady || !canSeek || safeDuration <= 0;
        root.style.setProperty('--story-player-progress', `${progressPercentage}%`);
    };

    const render = () => {
        const iconClassName = state.isPlaying ? 'fa-solid fa-pause' : 'fa-solid fa-play';
        toggleIcon.className = iconClassName;
        toggleButton.disabled = controller === null;
        toggleButton.setAttribute('aria-label', state.isPlaying ? 'Tam dung nhac' : 'Phat nhac');

        if (state.isSeeking) {
            const previewValue = toFiniteSeconds(rangeInput.value);
            updateProgressUi(previewValue, state.duration);
            return;
        }

        updateProgressUi(state.currentTime, state.duration);
    };

    const applyState = (partialState) => {
        Object.assign(state, partialState);
        render();
    };

    const clearPolling = () => {
        if (pollTimerId > 0) {
            window.clearInterval(pollTimerId);
            pollTimerId = 0;
        }
    };

    const startPolling = (callback) => {
        clearPolling();
        pollTimerId = window.setInterval(() => {
            if (isDestroyed) {
                clearPolling();
                return;
            }

            callback();
        }, 400);
    };

    const attemptAutoplay = () => {
        if (autoplayAttempted || controller === null) {
            return;
        }

        autoplayAttempted = true;
        Promise.resolve(controller.play()).catch(() => { });
    };

    const syncAudioState = (audio) => {
        if (!(audio instanceof HTMLAudioElement)) {
            return;
        }

        applyState({
            isReady: audio.readyState >= 1,
            isPlaying: !audio.paused && !audio.ended,
            currentTime: toFiniteSeconds(audio.currentTime),
            duration: toFiniteSeconds(audio.duration)
        });
    };

    const initializeNativeAudio = () => {
        if (!(audioElement instanceof HTMLAudioElement)) {
            return;
        }

        const sync = () => syncAudioState(audioElement);
        const eventNames = ['loadedmetadata', 'durationchange', 'timeupdate', 'play', 'pause', 'ended', 'canplay'];
        eventNames.forEach((eventName) => audioElement.addEventListener(eventName, sync));
        audioElement.addEventListener('canplay', attemptAutoplay, { once: true });

        controller = {
            play: () => audioElement.play(),
            pause: () => {
                audioElement.pause();
            },
            seekTo: (seconds) => {
                audioElement.currentTime = Math.max(0, toFiniteSeconds(seconds));
                sync();
            },
            destroy: () => {
                audioElement.pause();
            }
        };

        sync();
        if (audioElement.readyState >= 2) {
            attemptAutoplay();
        }
    };

    const loadYouTubeApi = () => {
        if (window.YT && typeof window.YT.Player === 'function') {
            return Promise.resolve(window.YT);
        }

        if (window.__storyMusicYouTubeApiPromise) {
            return window.__storyMusicYouTubeApiPromise;
        }

        window.__storyMusicYouTubeApiPromise = new Promise((resolve, reject) => {
            const timeoutId = window.setTimeout(() => reject(new Error('YouTube IFrame API timed out.')), 15000);
            const previousReadyHandler = window.onYouTubeIframeAPIReady;

            window.onYouTubeIframeAPIReady = () => {
                window.clearTimeout(timeoutId);
                if (typeof previousReadyHandler === 'function') {
                    previousReadyHandler();
                }

                resolve(window.YT);
            };

            if (document.querySelector('script[data-story-youtube-api]')) {
                return;
            }

            const script = document.createElement('script');
            script.src = 'https://www.youtube.com/iframe_api';
            script.async = true;
            script.dataset.storyYoutubeApi = 'true';
            script.onerror = () => {
                window.clearTimeout(timeoutId);
                reject(new Error('Unable to load the YouTube IFrame API.'));
            };

            document.head.appendChild(script);
        });

        return window.__storyMusicYouTubeApiPromise;
    };

    const initializeYouTubePlayer = async () => {
        if (!(youTubeHost instanceof HTMLElement) || !playerKey) {
            return;
        }

        const YT = await loadYouTubeApi();
        if (isDestroyed) {
            return;
        }

        let player = null;
        const sync = () => {
            if (player === null) {
                return;
            }

            applyState({
                currentTime: toFiniteSeconds(player.getCurrentTime()),
                duration: toFiniteSeconds(player.getDuration())
            });
        };

        await new Promise((resolve) => {
            player = new YT.Player(youTubeHost, {
                height: '220',
                width: '220',
                videoId: playerKey,
                playerVars: {
                    autoplay: 1,
                    controls: 0,
                    playsinline: 1,
                    rel: 0,
                    modestbranding: 1,
                    origin: window.location.origin
                },
                events: {
                    onReady: () => {
                        controller = {
                            play: () => player.playVideo(),
                            pause: () => player.pauseVideo(),
                            seekTo: (seconds) => player.seekTo(Math.max(0, toFiniteSeconds(seconds)), true),
                            destroy: () => {
                                clearPolling();
                                player.destroy();
                            }
                        };

                        applyState({
                            isReady: true,
                            duration: toFiniteSeconds(player.getDuration())
                        });

                        startPolling(sync);
                        attemptAutoplay();
                        resolve();
                    },
                    onStateChange: (event) => {
                        sync();

                        const isPlaying = event.data === YT.PlayerState.PLAYING;
                        applyState({
                            isPlaying
                        });

                        if (event.data === YT.PlayerState.PLAYING || event.data === YT.PlayerState.BUFFERING) {
                            startPolling(sync);
                            return;
                        }

                        if (event.data === YT.PlayerState.ENDED) {
                            applyState({
                                isPlaying: false,
                                currentTime: 0
                            });
                        }

                        if (event.data === YT.PlayerState.PAUSED
                            || event.data === YT.PlayerState.ENDED
                            || event.data === YT.PlayerState.CUED) {
                            clearPolling();
                        }
                    },
                    onError: () => {
                        clearPolling();
                        applyState({
                            isReady: true,
                            isPlaying: false
                        });
                        resolve();
                    }
                }
            });
        });
    };

    const loadSpotifyIframeApi = () => {
        if (window.__storyMusicSpotifyIframeApi) {
            return Promise.resolve(window.__storyMusicSpotifyIframeApi);
        }

        if (window.__storyMusicSpotifyIframeApiPromise) {
            return window.__storyMusicSpotifyIframeApiPromise;
        }

        window.__storyMusicSpotifyIframeApiPromise = new Promise((resolve, reject) => {
            const timeoutId = window.setTimeout(() => reject(new Error('Spotify iFrame API timed out.')), 15000);
            const previousReadyHandler = window.onSpotifyIframeApiReady;

            window.onSpotifyIframeApiReady = (iFrameApi) => {
                window.clearTimeout(timeoutId);
                window.__storyMusicSpotifyIframeApi = iFrameApi;

                if (typeof previousReadyHandler === 'function') {
                    previousReadyHandler(iFrameApi);
                }

                resolve(iFrameApi);
            };

            if (document.querySelector('script[data-story-spotify-api]')) {
                return;
            }

            const script = document.createElement('script');
            script.src = 'https://open.spotify.com/embed/iframe-api/v1';
            script.async = true;
            script.dataset.storySpotifyApi = 'true';
            script.onerror = () => {
                window.clearTimeout(timeoutId);
                reject(new Error('Unable to load the Spotify iFrame API.'));
            };

            document.head.appendChild(script);
        });

        return window.__storyMusicSpotifyIframeApiPromise;
    };

    const initializeSpotifyPlayer = async () => {
        if (!(spotifyHost instanceof HTMLElement) || !playerUri) {
            return;
        }

        const spotifyIframeApi = await loadSpotifyIframeApi();
        if (isDestroyed) {
            return;
        }

        spotifyIframeApi.createController(spotifyHost, {
            uri: playerUri,
            width: 320,
            height: 152
        }, (embedController) => {
            if (isDestroyed) {
                embedController.destroy();
                return;
            }

            controller = {
                play: () => embedController.play(),
                pause: () => embedController.pause(),
                seekTo: (seconds) => embedController.seek(Math.max(0, Math.floor(toFiniteSeconds(seconds)))),
                destroy: () => embedController.destroy()
            };

            embedController.addListener('ready', () => {
                applyState({
                    isReady: true
                });

                attemptAutoplay();
            });

            embedController.addListener('playback_started', () => {
                applyState({
                    isPlaying: true
                });
            });

            embedController.addListener('playback_update', (event) => {
                const data = event && typeof event === 'object' && 'data' in event ? event.data : null;
                if (!data) {
                    return;
                }

                const durationInSeconds = toFiniteSeconds(Number(data.duration) / 1000);
                const positionInSeconds = toFiniteSeconds(Number(data.position) / 1000);
                applyState({
                    isReady: true,
                    isPlaying: !data.isPaused && !data.isBuffering,
                    duration: durationInSeconds || state.duration,
                    currentTime: positionInSeconds
                });
            });
        });
    };

    const seekTo = (value) => {
        if (controller === null || typeof controller.seekTo !== 'function') {
            return;
        }

        Promise.resolve(controller.seekTo(value)).catch(() => { });
    };

    const destroyPlayer = () => {
        if (isDestroyed) {
            return;
        }

        isDestroyed = true;
        clearPolling();

        if (controller !== null && typeof controller.destroy === 'function') {
            controller.destroy();
        }
    };

    toggleButton.addEventListener('click', () => {
        if (controller === null) {
            return;
        }

        const action = state.isPlaying ? controller.pause : controller.play;
        if (typeof action !== 'function') {
            return;
        }

        Promise.resolve(action()).catch(() => { });
    });

    rangeInput.addEventListener('input', () => {
        if (!canSeek || rangeInput.disabled) {
            return;
        }

        state.isSeeking = true;
        render();
    });

    rangeInput.addEventListener('change', () => {
        if (!canSeek || rangeInput.disabled) {
            return;
        }

        state.isSeeking = false;
        seekTo(rangeInput.value);
        render();
    });

    rangeInput.addEventListener('blur', () => {
        if (!state.isSeeking) {
            return;
        }

        state.isSeeking = false;
        render();
    });

    window.addEventListener('pagehide', destroyPlayer, { once: true });

    render();

    if (playerKind === 'audio') {
        initializeNativeAudio();
        return;
    }

    if (playerKind === 'youtube') {
        initializeYouTubePlayer().catch(() => {
            applyState({
                isReady: false,
                isPlaying: false
            });
        });
        return;
    }

    if (playerKind === 'spotify') {
        initializeSpotifyPlayer().catch(() => {
            applyState({
                isReady: false,
                isPlaying: false
            });
        });
    }
})();
