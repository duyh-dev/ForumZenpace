(() => {
    const modal = document.querySelector('[data-story-modal]');
    if (!(modal instanceof HTMLElement)) {
        return;
    }

    const dialog = modal.querySelector('.story-modal-dialog');
    const form = modal.querySelector('[data-story-form]');
    const textInput = modal.querySelector('[data-story-text-input]');
    const backgroundInput = modal.querySelector('[data-story-background-input]');
    const imageInput = modal.querySelector('[data-story-image-input]');
    const imageName = modal.querySelector('[data-story-image-name]');
    const musicSelect = modal.querySelector('[data-story-music-select]');
    const musicLinkInput = modal.querySelector('[data-story-music-link-input]');
    const musicTitleInput = modal.querySelector('[data-story-music-title-input]');
    const musicArtistInput = modal.querySelector('[data-story-music-artist-input]');
    const musicUploadInput = modal.querySelector('[data-story-music-upload-input]');
    const trimControls = modal.querySelector('[data-story-music-trim-controls]');
    const musicName = modal.querySelector('[data-story-music-name]');
    const preview = modal.querySelector('[data-story-preview]');
    const previewCopy = modal.querySelector('[data-story-preview-copy]');
    const imagePreview = modal.querySelector('[data-story-image-preview]');
    const audioShell = modal.querySelector('[data-story-audio-shell]');
    const audioPreview = modal.querySelector('[data-story-audio-preview]');
    const audioLabel = modal.querySelector('[data-story-audio-label]');
    const audioCaption = modal.querySelector('[data-story-audio-caption]');
    const status = modal.querySelector('[data-story-form-status]');
    const submitButton = modal.querySelector('[data-story-submit]');
    let lastFocusedElement = null;
    let imagePreviewUrl = '';

    const openModal = () => {
        lastFocusedElement = document.activeElement instanceof HTMLElement ? document.activeElement : null;
        if (form instanceof HTMLFormElement) {
            form.reset();
        }

        setStatus('');
        updatePreviewBackground();
        updatePreviewCopy();
        updatePreviewImage();
        updatePreviewAudio();

        modal.hidden = false;
        modal.setAttribute('aria-hidden', 'false');
        document.body.classList.add('profile-modal-open');

        if (textInput instanceof HTMLTextAreaElement) {
            window.requestAnimationFrame(() => textInput.focus());
        }
    };

    const closeModal = () => {
        modal.hidden = true;
        modal.setAttribute('aria-hidden', 'true');
        document.body.classList.remove('profile-modal-open');
        window.clearTimeout(status?._clearTimer);
        setStatus('');

        if (lastFocusedElement instanceof HTMLElement) {
            lastFocusedElement.focus();
        }

        lastFocusedElement = null;
    };

    const updatePreviewBackground = () => {
        if (!(preview instanceof HTMLElement) || !(backgroundInput instanceof HTMLSelectElement)) {
            return;
        }

        preview.classList.remove(
            'story-surface--aurora',
            'story-surface--sunset',
            'story-surface--lagoon',
            'story-surface--midnight'
        );

        preview.classList.add(`story-surface--${backgroundInput.value || 'aurora'}`);
    };

    const updatePreviewCopy = () => {
        if (!(previewCopy instanceof HTMLElement) || !(textInput instanceof HTMLTextAreaElement)) {
            return;
        }

        const value = textInput.value.trim();
        previewCopy.textContent = value || 'Nhap van ban hoac chon anh de xem truoc story.';
        previewCopy.classList.toggle('is-placeholder', value.length === 0);
    };

    const updatePreviewImage = () => {
        if (!(imagePreview instanceof HTMLImageElement) || !(imageInput instanceof HTMLInputElement)) {
            return;
        }

        if (imagePreviewUrl) {
            URL.revokeObjectURL(imagePreviewUrl);
            imagePreviewUrl = '';
        }

        const file = imageInput.files && imageInput.files[0];
        if (!file) {
            imagePreview.hidden = true;
            imagePreview.removeAttribute('src');
            if (imageName instanceof HTMLElement) {
                imageName.textContent = 'Chap nhan JPG, PNG, GIF, WEBP. Gioi han toi da 10MB.';
            }
            return;
        }

        imagePreviewUrl = URL.createObjectURL(file);
        imagePreview.src = imagePreviewUrl;
        imagePreview.hidden = false;
        if (imageName instanceof HTMLElement) {
            imageName.textContent = `Da chon: ${file.name}`;
        }
    };

    const updatePreviewAudio = () => {
        if (!(audioShell instanceof HTMLElement)
            || !(audioPreview instanceof HTMLAudioElement)
            || !(audioLabel instanceof HTMLElement)) {
            return;
        }

        const externalUrl = musicLinkInput instanceof HTMLInputElement
            ? musicLinkInput.value.trim()
            : '';
        const externalTitle = musicTitleInput instanceof HTMLInputElement
            ? musicTitleInput.value.trim()
            : '';
        const externalArtist = musicArtistInput instanceof HTMLInputElement
            ? musicArtistInput.value.trim()
            : '';

        if (musicSelect instanceof HTMLSelectElement) {
            musicSelect.disabled = externalUrl.length > 0;
        }

        const selectedOption = musicSelect instanceof HTMLSelectElement && musicSelect.selectedOptions.length > 0
            ? musicSelect.selectedOptions[0]
            : null;
        const trackKey = musicSelect instanceof HTMLSelectElement
            ? musicSelect.value.trim()
            : '';
        const libraryHint = musicName instanceof HTMLElement
            ? (musicName.dataset.storyMusicLibraryHint || 'Chon nhac co san tu thu vien chung.')
            : 'Chon nhac co san tu thu vien chung.';
        const emptyHint = musicName instanceof HTMLElement
            ? (musicName.dataset.storyMusicEmptyHint || 'Bo trong neu ban muon dang story khong kem nhac.')
            : 'Bo trong neu ban muon dang story khong kem nhac.';

        if (externalUrl) {
            audioShell.hidden = false;
            audioPreview.hidden = true;
            audioPreview.pause();
            audioPreview.removeAttribute('src');
            audioPreview.load();

            let sourceLabel = 'Link nhac';
            try {
                const parsedUrl = new URL(externalUrl);
                const host = parsedUrl.hostname.replace(/^www\./i, '').toLowerCase();
                if (host.includes('spotify.com')) {
                    sourceLabel = 'Spotify';
                } else if (host.includes('youtube.com') || host === 'youtu.be') {
                    sourceLabel = 'YouTube';
                }
            } catch {
                sourceLabel = 'Link nhac';
            }

            const composedLabel = externalTitle
                ? (externalArtist ? `${externalTitle} - ${externalArtist}` : externalTitle)
                : `${sourceLabel} link`;

            audioLabel.textContent = composedLabel;
            if (audioCaption instanceof HTMLElement) {
                audioCaption.textContent = `${sourceLabel} se hien trong thanh player mini o dau story.`;
            }
            if (musicName instanceof HTMLElement) {
                musicName.textContent = `${sourceLabel} link se duoc uu tien trong player mini cua story.`;
            }
            return;
        }

        if (audioCaption instanceof HTMLElement) {
            audioCaption.textContent = 'Nhac nay se di kem story khi xem.';
        }

        if (!trackKey || !(selectedOption instanceof HTMLOptionElement)) {
            audioShell.hidden = true;
            audioPreview.hidden = true;
            audioPreview.pause();
            audioPreview.removeAttribute('src');
            audioPreview.load();
            audioLabel.textContent = 'Chua chon nhac';
            if (musicName instanceof HTMLElement) {
                musicName.textContent = trackKey.length === 0 ? libraryHint : emptyHint;
            }
            return;
        }

        const trackLabel = (selectedOption.dataset.trackLabel || selectedOption.textContent || 'Nhac story').trim();
        const audioUrl = (selectedOption.dataset.trackAudioUrl || '').trim();
        if (!audioUrl) {
            audioShell.hidden = true;
            audioPreview.hidden = true;
            audioPreview.pause();
            audioPreview.removeAttribute('src');
            audioPreview.load();
            audioLabel.textContent = 'Chua chon nhac';
            if (musicName instanceof HTMLElement) {
                musicName.textContent = 'Bai nhac nay hien khong kha dung.';
            }
            return;
        }

        audioPreview.pause();
        audioPreview.src = audioUrl;
        audioPreview.load();
        audioPreview.hidden = false;
        audioShell.hidden = false;
        audioLabel.textContent = trackLabel;
        if (musicName instanceof HTMLElement) {
            musicName.textContent = `Da chon: ${trackLabel}`;
        }

        audioPreview.currentTime = 0;
        audioPreview.play().catch(() => {});
    };

    const setStatus = (message, isError = false) => {
        if (!(status instanceof HTMLElement)) {
            return;
        }

        if (!message) {
            status.hidden = true;
            status.textContent = '';
            status.classList.remove('is-error', 'is-success');
            return;
        }

        status.hidden = false;
        status.textContent = message;
        status.classList.toggle('is-error', isError);
        status.classList.toggle('is-success', !isError);
    };

    document.querySelectorAll('[data-open-story-modal]').forEach((button) => {
        button.addEventListener('click', () => openModal());
    });

    modal.querySelectorAll('[data-close-story-modal]').forEach((button) => {
        button.addEventListener('click', () => closeModal());
    });

    document.addEventListener('keydown', (event) => {
        if (event.key === 'Escape' && !modal.hidden) {
            closeModal();
        }
    });

    if (textInput instanceof HTMLTextAreaElement) {
        textInput.addEventListener('input', () => updatePreviewCopy());
    }

    if (backgroundInput instanceof HTMLSelectElement) {
        backgroundInput.addEventListener('change', () => updatePreviewBackground());
    }

    if (imageInput instanceof HTMLInputElement) {
        imageInput.addEventListener('change', () => updatePreviewImage());
    }

    if (musicSelect instanceof HTMLSelectElement) {
        musicSelect.addEventListener('change', () => updatePreviewAudio());
    }

    if (musicLinkInput instanceof HTMLInputElement) {
        musicLinkInput.addEventListener('input', () => updatePreviewAudio());
    }

    if (musicTitleInput instanceof HTMLInputElement) {
        musicTitleInput.addEventListener('input', () => updatePreviewAudio());
    }

    if (musicArtistInput instanceof HTMLInputElement) {
        musicArtistInput.addEventListener('input', () => updatePreviewAudio());
    }

    if (musicUploadInput instanceof HTMLInputElement) {
        musicUploadInput.addEventListener('change', () => {
            if (trimControls instanceof HTMLElement) {
                const hasFile = musicUploadInput.files && musicUploadInput.files.length > 0;
                trimControls.hidden = !hasFile;
            }
        });
    }

    if (form instanceof HTMLFormElement) {
        form.addEventListener('submit', async (event) => {
            event.preventDefault();

            if (!(submitButton instanceof HTMLButtonElement)) {
                return;
            }

            submitButton.disabled = true;
            setStatus('Dang dang story...');

            try {
                const response = await fetch(form.action, {
                    method: 'POST',
                    body: new FormData(form),
                    headers: { 'X-Requested-With': 'XMLHttpRequest' }
                });

                const data = await response.json().catch(() => null);
                if (!response.ok || !data?.success) {
                    setStatus(data?.message || 'Khong the dang story luc nay.', true);
                    submitButton.disabled = false;
                    return;
                }

                window.location.assign(data.redirectUrl || '/');
            } catch {
                setStatus('Khong the dang story luc nay.', true);
                submitButton.disabled = false;
            }
        });
    }

    updatePreviewBackground();
    updatePreviewCopy();
    updatePreviewAudio();
})();
