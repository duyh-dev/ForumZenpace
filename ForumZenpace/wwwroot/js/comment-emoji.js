(() => {
    const popover = document.querySelector('[data-emoji-popover]');
    const picker = document.querySelector('[data-emoji-picker]');
    const triggers = Array.from(document.querySelectorAll('[data-emoji-trigger]'));
    const inputs = Array.from(document.querySelectorAll('[data-emoji-input]'));

    if (!(popover instanceof HTMLElement) || !(picker instanceof HTMLElement) || triggers.length === 0 || inputs.length === 0) {
        return;
    }

    if (popover.parentElement !== document.body) {
        document.body.appendChild(popover);
    }

    const selectionMap = new WeakMap();
    let activeInput = null;
    let activeTrigger = null;
    let pickerReady = false;

    const syncSelection = (input) => {
        selectionMap.set(input, {
            start: input.selectionStart ?? input.value.length,
            end: input.selectionEnd ?? input.selectionStart ?? input.value.length
        });
    };

    inputs.forEach((input) => {
        if (!(input instanceof HTMLTextAreaElement)) {
            return;
        }

        const updateSelection = () => syncSelection(input);
        ['keyup', 'click', 'mouseup', 'select', 'focus', 'input'].forEach((eventName) => {
            input.addEventListener(eventName, updateSelection);
        });

        syncSelection(input);
    });

    customElements.whenDefined('emoji-picker')
        .then(() => {
            pickerReady = true;

            triggers.forEach((trigger) => {
                if (!(trigger instanceof HTMLButtonElement)) {
                    return;
                }

                trigger.disabled = false;
                trigger.title = 'Chen emoji';
                trigger.setAttribute('aria-disabled', 'false');
            });
        })
        .catch(() => {
            closePopover();
        });

    document.addEventListener('click', (event) => {
        const target = event.target;
        if (!(target instanceof Element)) {
            return;
        }

        const trigger = target.closest('[data-emoji-trigger]');
        if (trigger instanceof HTMLButtonElement) {
            event.preventDefault();

            if (!pickerReady) {
                return;
            }

            const input = findInputForTrigger(trigger);
            if (!(input instanceof HTMLTextAreaElement)) {
                return;
            }

            if (activeTrigger === trigger && !popover.hidden) {
                closePopover();
                return;
            }

            openPopover(trigger, input);
            return;
        }

        if (popover.hidden) {
            return;
        }

        if (popover.contains(target)) {
            return;
        }

        closePopover();
    });

    picker.addEventListener('emoji-click', (event) => {
        if (!(event instanceof CustomEvent) || !(activeInput instanceof HTMLTextAreaElement)) {
            return;
        }

        const unicode = event.detail?.unicode;
        if (typeof unicode !== 'string' || unicode.length === 0) {
            return;
        }

        insertEmoji(activeInput, unicode);
        positionPopover();
    });

    document.addEventListener('keydown', (event) => {
        if (event.key === 'Escape' && !popover.hidden) {
            closePopover();
        }
    });

    document.addEventListener('submit', () => {
        closePopover();
    });

    window.addEventListener('resize', () => {
        if (!popover.hidden) {
            positionPopover();
        }
    });

    window.addEventListener('scroll', () => {
        if (!popover.hidden) {
            positionPopover();
        }
    }, true);

    function findInputForTrigger(trigger) {
        const form = trigger.closest('form');
        if (!(form instanceof HTMLFormElement)) {
            return null;
        }

        return form.querySelector('[data-emoji-input]');
    }

    function openPopover(trigger, input) {
        syncSelection(input);
        activeTrigger = trigger;
        activeInput = input;

        popover.hidden = false;
        popover.setAttribute('aria-hidden', 'false');
        trigger.classList.add('is-open');
        trigger.setAttribute('aria-expanded', 'true');

        positionPopover();
        window.requestAnimationFrame(positionPopover);
    }

    function closePopover() {
        popover.hidden = true;
        popover.setAttribute('aria-hidden', 'true');

        if (activeTrigger instanceof HTMLButtonElement) {
            activeTrigger.classList.remove('is-open');
            activeTrigger.setAttribute('aria-expanded', 'false');
        }

        activeTrigger = null;
        activeInput = null;
    }

    function positionPopover() {
        if (!(activeTrigger instanceof HTMLButtonElement)) {
            return;
        }

        const padding = 12;
        const offset = 10;
        const triggerRect = activeTrigger.getBoundingClientRect();
        const popoverRect = popover.getBoundingClientRect();
        const viewportWidth = window.innerWidth;
        const viewportHeight = window.innerHeight;

        let left = Math.min(triggerRect.left, viewportWidth - popoverRect.width - padding);
        left = Math.max(padding, left);

        let top = triggerRect.bottom + offset;
        if (top + popoverRect.height > viewportHeight - padding) {
            top = triggerRect.top - popoverRect.height - offset;
        }

        top = Math.max(padding, top);

        popover.style.left = `${Math.round(left)}px`;
        popover.style.top = `${Math.round(top)}px`;
    }

    function insertEmoji(input, emoji) {
        const selection = selectionMap.get(input) ?? {
            start: input.selectionStart ?? input.value.length,
            end: input.selectionEnd ?? input.value.length
        };

        const start = selection.start;
        const end = selection.end;
        input.value = `${input.value.slice(0, start)}${emoji}${input.value.slice(end)}`;

        const caret = start + emoji.length;
        input.focus();
        input.setSelectionRange(caret, caret);
        input.dispatchEvent(new Event('input', { bubbles: true }));

        syncSelection(input);
    }
})();
