export const escapeHtml = (value) => `${value ?? ''}`
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');

export const getInitials = (displayName, fallback) => {
    const source = `${displayName || fallback || '?'}`.trim();
    const parts = source.split(/\s+/).filter(Boolean);
    if (parts.length <= 1) {
        return (parts[0] || '?').slice(0, 1).toUpperCase();
    }
    return `${parts[0].slice(0, 1)}${parts[parts.length - 1].slice(0, 1)}`.toUpperCase();
};

export const getRequestVerificationToken = () => {
    const tokenMeta = document.querySelector('meta[name="request-verification-token"]');
    if (tokenMeta instanceof HTMLMetaElement && tokenMeta.content) {
        return tokenMeta.content;
    }
    const tokenField = document.querySelector('input[name="__RequestVerificationToken"]');
    return tokenField instanceof HTMLInputElement ? tokenField.value : '';
};

export const renderAntiForgeryTokenInput = () => {
    const token = getRequestVerificationToken();
    return token
        ? `<input type="hidden" name="__RequestVerificationToken" value="${escapeHtml(token)}" />`
        : '';
};

export const buildRequestBody = (payload = {}) => {
    const bodyParams = new URLSearchParams();
    const token = getRequestVerificationToken();
    if (token) {
        bodyParams.append('__RequestVerificationToken', token);
    }
    Object.entries(payload).forEach(([key, value]) => {
        if (value === null || value === undefined) return;
        bodyParams.append(key, `${value}`);
    });
    return bodyParams;
};

export const fetchJson = async (url, options = {}) => {
    const response = await fetch(url, options);
    const data = await response.json().catch(() => null);
    if (!response.ok || !data) {
        throw new Error(data?.message || 'Khong the xu ly yeu cau luc nay.');
    }
    return data;
};

export const postSocialAction = async (url, payload = {}) => {
    const bodyParams = buildRequestBody(payload);
    return await fetchJson(url, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8',
            'X-Requested-With': 'XMLHttpRequest'
        },
        body: bodyParams.toString()
    });
};

export const getSocialData = async (url, query = {}) => {
    const requestUrl = new URL(url, window.location.origin);
    Object.entries(query).forEach(([key, value]) => {
        if (value === null || value === undefined || `${value}`.length === 0) return;
        requestUrl.searchParams.set(key, `${value}`);
    });
    return await fetchJson(requestUrl.toString(), {
        headers: { 'X-Requested-With': 'XMLHttpRequest' }
    });
};
