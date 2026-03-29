(() => {
    const editors = document.querySelectorAll('[data-markdown-editor]');
    if (editors.length === 0) {
        return;
    }

    editors.forEach((editor) => {
        const textarea = editor.querySelector('[data-markdown-input]');
        const imageInput = editor.querySelector('[data-markdown-image-input]');
        const status = editor.querySelector('[data-markdown-status]');
        const form = editor.closest('form');
        const field = editor.closest('.field');
        const draftTokenInput = field?.querySelector('[data-markdown-draft-token]');
        const postIdInput = field?.querySelector('[data-markdown-post-id]');

        if (!(textarea instanceof HTMLTextAreaElement) || !(imageInput instanceof HTMLInputElement)) {
            return;
        }

        let selectionStart = textarea.selectionStart ?? 0;
        let selectionEnd = textarea.selectionEnd ?? 0;
        let isUploading = false;

        const syncSelection = () => {
            selectionStart = textarea.selectionStart ?? 0;
            selectionEnd = textarea.selectionEnd ?? selectionStart;
        };

        ['keyup', 'click', 'mouseup', 'select', 'focus'].forEach((eventName) => {
            textarea.addEventListener(eventName, syncSelection);
        });

        editor.querySelectorAll('[data-markdown-action]').forEach((button) => {
            if (!(button instanceof HTMLButtonElement)) {
                return;
            }

            button.addEventListener('click', () => {
                syncSelection();

                if (button.dataset.markdownAction === 'image') {
                    imageInput.click();
                    return;
                }

                applyMarkdownAction(textarea, button.dataset.markdownAction || '');
                syncSelection();
            });
        });

        imageInput.addEventListener('change', async () => {
            const file = imageInput.files && imageInput.files[0];
            imageInput.value = '';

            if (!file) {
                return;
            }

            await uploadImage(file);
        });

        textarea.addEventListener('paste', async (event) => {
            if (!event.clipboardData || isUploading) {
                return;
            }

            const imageItem = Array.from(event.clipboardData.items)
                .find((item) => item.kind === 'file' && item.type.startsWith('image/'));

            if (!imageItem) {
                return;
            }

            const file = imageItem.getAsFile();
            if (!file) {
                return;
            }

            syncSelection();
            event.preventDefault();
            await uploadImage(file);
        });

        async function uploadImage(file) {
            const uploadUrl = editor.dataset.uploadUrl;
            if (!uploadUrl || isUploading) {
                return;
            }

            isUploading = true;
            setBusyState(true);
            setStatus('Dang tai anh len...');

            const formData = new FormData();
            formData.append('Image', file, file.name);

            if (postIdInput instanceof HTMLInputElement && postIdInput.value) {
                formData.append('PostId', postIdInput.value);
            }

            if (draftTokenInput instanceof HTMLInputElement && draftTokenInput.value) {
                formData.append('DraftToken', draftTokenInput.value);
            }

            const antiForgeryToken = form?.querySelector('input[name="__RequestVerificationToken"]');
            if (antiForgeryToken instanceof HTMLInputElement && antiForgeryToken.value) {
                formData.append('__RequestVerificationToken', antiForgeryToken.value);
            }

            try {
                const response = await fetch(uploadUrl, {
                    method: 'POST',
                    body: formData,
                    headers: { 'X-Requested-With': 'XMLHttpRequest' }
                });

                const data = await response.json();
                if (!response.ok || !data.success || typeof data.markdown !== 'string') {
                    setStatus(data.message || 'Khong the tai anh luc nay.', true);
                    return;
                }

                restoreSelection();
                insertImageMarkdown(textarea, data.markdown);
                setStatus('Da chen anh vao bai viet.');
            } catch {
                setStatus('Khong the tai anh luc nay.', true);
            } finally {
                isUploading = false;
                setBusyState(false);
            }
        }

        function restoreSelection() {
            textarea.focus();
            textarea.setSelectionRange(selectionStart, selectionEnd);
        }

        function setBusyState(isBusy) {
            editor.classList.toggle('is-uploading', isBusy);
            editor.querySelectorAll('[data-markdown-action]').forEach((button) => {
                if (button instanceof HTMLButtonElement && button.dataset.markdownAction === 'image') {
                    button.disabled = isBusy;
                }
            });
        }

        function setStatus(message, isError = false) {
            if (!(status instanceof HTMLElement)) {
                return;
            }

            status.textContent = message;
            status.classList.toggle('is-error', isError);
            status.classList.toggle('is-success', !isError && message.length > 0);

            if (!message) {
                return;
            }

            window.clearTimeout(status._clearTimer);
            status._clearTimer = window.setTimeout(() => {
                status.textContent = '';
                status.classList.remove('is-error', 'is-success');
            }, 2400);
        }
    });

    function applyMarkdownAction(textarea, action) {
        switch (action) {
            case 'heading-1':
                prefixSelectedLines(textarea, () => '# ');
                break;
            case 'heading-2':
                prefixSelectedLines(textarea, () => '## ');
                break;
            case 'bold':
                wrapSelection(textarea, '**', '**', 'doan van dam');
                break;
            case 'italic':
                wrapSelection(textarea, '*', '*', 'doan van nghieng');
                break;
            case 'bullets':
                prefixSelectedLines(textarea, () => '- ');
                break;
            case 'numbers':
                prefixSelectedLines(textarea, (_, index) => `${index + 1}. `);
                break;
            case 'quote':
                prefixSelectedLines(textarea, () => '> ');
                break;
            case 'code':
                insertCode(textarea);
                break;
            case 'link':
                insertLink(textarea);
                break;
            default:
                break;
        }
    }

    function wrapSelection(textarea, prefix, suffix, placeholder) {
        const start = textarea.selectionStart ?? 0;
        const end = textarea.selectionEnd ?? start;
        const selection = textarea.value.slice(start, end);
        const content = selection || placeholder;
        const replacement = `${prefix}${content}${suffix}`;

        replaceRange(textarea, start, end, replacement, start + prefix.length, start + prefix.length + content.length);
    }

    function insertLink(textarea) {
        const start = textarea.selectionStart ?? 0;
        const end = textarea.selectionEnd ?? start;
        const selection = textarea.value.slice(start, end) || 'mo ta lien ket';
        const url = 'https://example.com';
        const replacement = `[${selection}](${url})`;
        const urlStart = start + selection.length + 3;
        replaceRange(textarea, start, end, replacement, urlStart, urlStart + url.length);
    }

    function insertCode(textarea) {
        const start = textarea.selectionStart ?? 0;
        const end = textarea.selectionEnd ?? start;
        const selection = textarea.value.slice(start, end);

        if (selection.includes('\n')) {
            const blockContent = selection || 'console.log("Xin chao");';
            const replacement = `\`\`\`\n${blockContent}\n\`\`\``;
            replaceRange(textarea, start, end, replacement, start + 4, start + 4 + blockContent.length);
            return;
        }

        wrapSelection(textarea, '`', '`', 'doan ma');
    }

    function insertImageMarkdown(textarea, markdown) {
        const start = textarea.selectionStart ?? 0;
        const end = textarea.selectionEnd ?? start;
        const value = textarea.value;
        const needsLeadingNewLine = start > 0 && value[start - 1] !== '\n';
        const needsTrailingNewLine = end < value.length && value[end] !== '\n';
        const replacement = `${needsLeadingNewLine ? '\n' : ''}${markdown}${needsTrailingNewLine ? '\n' : ''}`;
        const caret = start + replacement.length;

        replaceRange(textarea, start, end, replacement, caret, caret);
    }

    function prefixSelectedLines(textarea, prefixFactory) {
        const value = textarea.value;
        const start = textarea.selectionStart ?? 0;
        const end = textarea.selectionEnd ?? start;
        const blockStart = value.lastIndexOf('\n', Math.max(0, start - 1)) + 1;
        const blockEndCandidate = value.indexOf('\n', end);
        const blockEnd = blockEndCandidate === -1 ? value.length : blockEndCandidate;
        const block = value.slice(blockStart, blockEnd);
        const lines = block.split('\n');
        const updatedLines = lines.map((line, index) => {
            if (!line.trim()) {
                return line;
            }

            return `${prefixFactory(line, index)}${line}`;
        });

        const replacement = updatedLines.join('\n');
        replaceRange(textarea, blockStart, blockEnd, replacement, blockStart, blockStart + replacement.length);
    }

    function replaceRange(textarea, start, end, replacement, selectionStart, selectionEnd) {
        const value = textarea.value;
        textarea.value = `${value.slice(0, start)}${replacement}${value.slice(end)}`;
        textarea.focus();
        textarea.setSelectionRange(selectionStart, selectionEnd);
        textarea.dispatchEvent(new Event('input', { bubbles: true }));
    }
})();
