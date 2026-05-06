// BitCanary Web Client

const API_BASE = 'http://localhost:5000/api';
const HUB_URL = API_BASE.replace('/api', '') + '/hubs/chat';
let connection = null;
let state = {
    token: null,
    userId: null,
    username: null,
    chats: [],
    selectedChatId: null,
    selectedContextChatId: null,
    messageSearchQuery: '',
    chatFilter: '',
    messages: []
};
let searchDebounceTimer = null;
const outgoingPlaintextByChat = new Map();
const sentPlaintextByMessageId = new Map();
const SENT_PLAINTEXT_CACHE_KEY = 'bitcanary_sent_plaintext_by_message_id';
const decryptFailureLog = [];
let pendingDialogResolve = null;

/** Pending file from [+] picker (cleared after send). */
let pendingAttachmentFile = null;

/** UI state for group info modal (Desktop GroupInfoView parity). */
let groupInfoUi = {
    editing: false,
    chatId: null
};

function loadSentPlaintextCache() {
    try {
        const raw = localStorage.getItem(SENT_PLAINTEXT_CACHE_KEY);
        if (!raw) return;
        const parsed = JSON.parse(raw);
        if (!parsed || typeof parsed !== 'object') return;
        Object.keys(parsed).forEach(function (k) {
            sentPlaintextByMessageId.set(k, parsed[k]);
        });
    } catch {
        // ignore cache read errors
    }
}

function saveSentPlaintextCache() {
    try {
        const obj = Object.fromEntries(sentPlaintextByMessageId.entries());
        localStorage.setItem(SENT_PLAINTEXT_CACHE_KEY, JSON.stringify(obj));
    } catch {
        // ignore cache write errors
    }
}

function enqueueOutgoingPlaintext(chatId, text) {
    if (!outgoingPlaintextByChat.has(chatId)) {
        outgoingPlaintextByChat.set(chatId, []);
    }
    outgoingPlaintextByChat.get(chatId).push(text);
}

function consumeOutgoingPlaintext(chatId) {
    const queue = outgoingPlaintextByChat.get(chatId);
    if (!queue || queue.length === 0) return null;
    return queue.shift() || null;
}

const elements = {
    loginScreen: document.getElementById('login-screen'),
    mainScreen: document.getElementById('main-screen'),
    loginForm: document.getElementById('login-form'),
    loginError: document.getElementById('login-error'),
    usernameInput: document.getElementById('username'),
    passwordInput: document.getElementById('password'),
    currentUser: document.getElementById('current-user'),
    topbarStatus: document.getElementById('topbar-status'),
    topbarRefreshBtn: document.getElementById('topbar-refresh-btn'),
    createGroupBtn: document.getElementById('create-group-btn'),
    sidebarSearchBtn: document.getElementById('sidebar-search-btn'),
    sidebarSettingsBtn: document.getElementById('sidebar-settings-btn'),
    chatFilterInput: document.getElementById('chat-filter-input'),
    settingsLogoutBtn: document.getElementById('settings-logout-btn'),
    chatsContainer: document.getElementById('chats'),
    chatTitle: document.getElementById('chat-title'),
    chatSubtitle: document.getElementById('chat-subtitle'),
    groupInfoBtn: document.getElementById('group-info-btn'),
    messagesContainer: document.getElementById('messages'),
    messageForm: document.getElementById('message-form'),
    messageInput: document.getElementById('message-input'),
    sendBtn: document.getElementById('send-btn'),
    attachBtn: document.getElementById('attach-btn'),
    fileInput: document.getElementById('file-input'),
    attachmentChip: document.getElementById('attachment-chip'),
    attachmentChipName: document.getElementById('attachment-chip-name'),
    attachmentChipClear: document.getElementById('attachment-chip-clear'),
    emojiBtn: document.getElementById('emoji-btn'),
    chatTrustBadge: document.getElementById('chat-trust-badge'),
    safetyNumberBtn: document.getElementById('safety-number-btn'),
    safetyNumberModal: document.getElementById('safety-number-modal'),
    closeSafetyNumberBtn: document.getElementById('close-safety-number-btn'),
    safetyNumberText: document.getElementById('safety-number-text'),
    decryptLog: document.getElementById('decrypt-log'),
    chatContextMenu: document.getElementById('chat-context-menu'),
    ctxDeleteChatBtn: document.getElementById('ctx-delete-chat-btn'),
    ctxClearChatBtn: document.getElementById('ctx-clear-chat-btn'),
    settingsModal: document.getElementById('settings-modal'),
    closeSettingsBtn: document.getElementById('close-settings-btn'),
    clearLocalCacheBtn: document.getElementById('clear-local-cache-btn'),
    resetEncryptionBtn: document.getElementById('reset-encryption-btn'),
    settingsStatus: document.getElementById('settings-status'),
    searchModal: document.getElementById('search-modal'),
    searchModalInput: document.getElementById('search-modal-input'),
    searchModalUserResults: document.getElementById('search-modal-user-results'),
    searchModalStatus: document.getElementById('search-modal-status'),
    searchModalApplyBtn: document.getElementById('search-modal-apply-btn'),
    searchModalClearBtn: document.getElementById('search-modal-clear-btn'),
    searchModalCancelBtn: document.getElementById('search-modal-cancel-btn'),
    groupModal: document.getElementById('group-modal'),
    groupInfoModal: document.getElementById('group-info-modal'),
    groupInfoTitleDisplay: document.getElementById('group-info-title-display'),
    groupInfoTitleEdit: document.getElementById('group-info-title-edit'),
    groupInfoDescDisplay: document.getElementById('group-info-desc-display'),
    groupInfoDescEdit: document.getElementById('group-info-desc-edit'),
    groupInfoEditBtn: document.getElementById('group-info-edit-btn'),
    groupInfoLeaveBtn: document.getElementById('group-info-leave-btn'),
    groupInfoSaveBtn: document.getElementById('group-info-save-btn'),
    groupInfoCancelEditBtn: document.getElementById('group-info-cancel-edit-btn'),
    groupInfoMembers: document.getElementById('group-info-members'),
    groupInfoMemberCount: document.getElementById('group-info-member-count'),
    groupInfoError: document.getElementById('group-info-error'),
    groupInfoCloseBtn: document.getElementById('group-info-close-btn'),
    groupInfoInviteRow: document.getElementById('group-info-invite-row'),
    groupInfoInviteInput: document.getElementById('group-info-invite-input'),
    groupInfoAddMemberBtn: document.getElementById('group-info-add-member-btn'),
    groupTitleInput: document.getElementById('group-title-input'),
    groupMembersInput: document.getElementById('group-members-input'),
    groupModalStatus: document.getElementById('group-modal-status'),
    groupModalCreateBtn: document.getElementById('group-modal-create-btn'),
    groupModalCancelBtn: document.getElementById('group-modal-cancel-btn'),
    dialogModal: document.getElementById('dialog-modal'),
    dialogTitle: document.getElementById('dialog-title'),
    dialogMessage: document.getElementById('dialog-message'),
    dialogOkBtn: document.getElementById('dialog-ok-btn'),
    dialogCancelBtn: document.getElementById('dialog-cancel-btn')
};

function normalizeId(value) {
    return String(value || '').trim().toLowerCase();
}

function pushDecryptLog(entry) {
    decryptFailureLog.unshift(entry);
    if (decryptFailureLog.length > 20) {
        decryptFailureLog.length = 20;
    }
    if (elements.decryptLog) {
        elements.decryptLog.classList.remove('hidden');
        elements.decryptLog.textContent = `[decrypt-auto-log] ${entry}`;
    }
}

function getMetadataHints(message) {
    try {
        const meta = message && message.metadataJson ? JSON.parse(message.metadataJson) : null;
        const hasNoise = !!(meta && meta.noise);
        const n = meta && meta.dr ? meta.dr.n : null;
        const pn = meta && meta.dr ? meta.dr.pn : null;
        return { hasNoise: hasNoise, n: n, pn: pn };
    } catch {
        return { hasNoise: false, n: null, pn: null };
    }
}

function normalizeMessageStatus(status) {
    if (status === null || status === undefined) return 'delivered';
    if (typeof status === 'number') {
        if (status === 0) return 'sending';
        if (status === 1) return 'delivered';
        if (status === 2) return 'read';
        if (status === 3) return 'failed';
    }
    const text = String(status).toLowerCase();
    if (text === 'sending' || text === '0') return 'sending';
    if (text === 'read' || text === '2') return 'read';
    if (text === 'failed' || text === '3') return 'failed';
    return 'delivered';
}

function getOutgoingStatusLabel(status) {
    if (status === 'sending') return '...';
    if (status === 'read') return 'vv';
    if (status === 'failed') return '!';
    return 'v';
}

function getChatType(chat) {
    return chat && (chat.chatType !== undefined ? chat.chatType : chat.type);
}

function isDirectChat(chat) {
    const chatType = getChatType(chat);
    return chatType === 1 || chatType === '1' ||
        (typeof chatType === 'string' && chatType.toLowerCase() === 'direct');
}

function getChatMembers(chat) {
    return Array.isArray(chat && chat.members) ? chat.members :
        (Array.isArray(chat && chat.Members) ? chat.Members : []);
}

function getChatSubtitle(chat) {
    if (!chat) return '';
    if (isDirectChat(chat)) return 'direct session';
    const d = chat.description || chat.Description;
    if (d) return String(d);
    const n = getChatMembers(chat).length;
    return n ? `group · ${n} members` : 'group channel';
}

/** @returns {1|2|3} ChatRole: Owner=1, Admin=2, Member=3 */
function parseChatRole(role) {
    const n = Number(role);
    if (n === 1 || n === 2 || n === 3) return n;
    if (typeof role === 'string') {
        const l = role.toLowerCase();
        if (l === 'owner') return 1;
        if (l === 'admin') return 2;
        if (l === 'member') return 3;
    }
    return 3;
}

function getCallerRoleFromChat(chat) {
    const members = getChatMembers(chat);
    const me = members.find(function (m) {
        return normalizeId(m.userId || m.UserId) === normalizeId(state.userId);
    });
    return me ? parseChatRole(me.role !== undefined ? me.role : me.Role) : 3;
}

function roleBadgeText(role) {
    if (role === 1) return '[ OWNER ]';
    if (role === 2) return '[ ADMIN ]';
    return '[ MEMBER ]';
}

function memberCanRemove(callerRole, targetRole, isSelf) {
    if (isSelf) return false;
    if (callerRole !== 1 && callerRole !== 2) return false;
    return targetRole > callerRole;
}

function hideGroupInfoModal() {
    if (elements.groupInfoModal) elements.groupInfoModal.classList.add('hidden');
    groupInfoUi.editing = false;
    groupInfoUi.chatId = null;
    showGroupInfoError('');
    if (elements.groupInfoInviteInput) elements.groupInfoInviteInput.value = '';
}

function showGroupInfoError(message) {
    if (!elements.groupInfoError) return;
    if (!message) {
        elements.groupInfoError.textContent = '';
        elements.groupInfoError.classList.add('hidden');
        return;
    }
    elements.groupInfoError.textContent = String(message);
    elements.groupInfoError.classList.remove('hidden');
}

function renderGroupInfoModal(chat) {
    if (!chat || !elements.groupInfoTitleDisplay) return;
    showGroupInfoError('');
    const editing = groupInfoUi.editing;
    const callerRole = getCallerRoleFromChat(chat);
    const isPrivileged = callerRole === 1 || callerRole === 2;

    if (!editing) {
        elements.groupInfoTitleDisplay.textContent = chat.title || '';
        elements.groupInfoDescDisplay.textContent = chat.description || chat.Description || '';
        elements.groupInfoTitleDisplay.classList.remove('hidden');
        elements.groupInfoDescDisplay.classList.remove('hidden');
        elements.groupInfoTitleEdit.classList.add('hidden');
        elements.groupInfoDescEdit.classList.add('hidden');
    } else {
        elements.groupInfoTitleDisplay.classList.add('hidden');
        elements.groupInfoDescDisplay.classList.add('hidden');
        elements.groupInfoTitleEdit.classList.remove('hidden');
        elements.groupInfoDescEdit.classList.remove('hidden');
    }

    if (elements.groupInfoEditBtn) {
        elements.groupInfoEditBtn.classList.toggle('hidden', !isPrivileged || editing);
    }
    if (elements.groupInfoLeaveBtn) {
        elements.groupInfoLeaveBtn.classList.toggle('hidden', isPrivileged || editing);
    }
    if (elements.groupInfoSaveBtn) {
        elements.groupInfoSaveBtn.classList.toggle('hidden', !editing || !isPrivileged);
    }
    if (elements.groupInfoCancelEditBtn) {
        elements.groupInfoCancelEditBtn.classList.toggle('hidden', !editing || !isPrivileged);
    }
    if (elements.groupInfoInviteRow) {
        elements.groupInfoInviteRow.classList.toggle('hidden', !isPrivileged);
    }

    const members = getChatMembers(chat).slice().sort(function (a, b) {
        return parseChatRole(a.role !== undefined ? a.role : a.Role) -
            parseChatRole(b.role !== undefined ? b.role : b.Role);
    });
    if (elements.groupInfoMemberCount) {
        elements.groupInfoMemberCount.textContent = String(members.length);
    }

    if (elements.groupInfoMembers) {
        elements.groupInfoMembers.innerHTML = members.map(function (m) {
            const uid = m.userId || m.UserId;
            const displayName = m.displayName || m.DisplayName || 'unknown';
            const role = parseChatRole(m.role !== undefined ? m.role : m.Role);
            const isSelf = normalizeId(uid) === normalizeId(state.userId);
            const badge = roleBadgeText(role);
            let actionsHtml = '';
            if (memberCanRemove(callerRole, role, isSelf)) {
                actionsHtml += `<button type="button" class="btn-remove-member" data-action="remove" data-user-id="${escapeHtml(String(uid))}">[ REMOVE ]</button>`;
            }
            if (!isSelf && callerRole === 1 && role === 3) {
                actionsHtml += `<button type="button" data-action="make-admin" data-user-id="${escapeHtml(String(uid))}">[ MAKE ADMIN ]</button>`;
            }
            if (!isSelf && callerRole === 1 && role === 2) {
                actionsHtml += `<button type="button" data-action="revoke-admin" data-user-id="${escapeHtml(String(uid))}">[ REVOKE ADMIN ]</button>`;
            }
            return `
                <div class="group-member-row">
                    <div class="group-member-line1">
                        <span class="group-member-badge">${badge}</span>
                        <span class="group-member-name">${escapeHtml(String(displayName))}</span>
                    </div>
                    ${actionsHtml ? `<div class="group-member-actions">${actionsHtml}</div>` : ''}
                </div>`;
        }).join('');
    }
}

async function addGroupMemberFromInvite() {
    const raw = String(elements.groupInfoInviteInput && elements.groupInfoInviteInput.value || '').trim();
    if (!raw) {
        showGroupInfoError('enter a username');
        return;
    }
    if (!groupInfoUi.chatId) return;
    showGroupInfoError('');
    try {
        const user = await resolveUserByQuery(raw);
        if (!user) {
            showGroupInfoError('user not found; type exact username');
            return;
        }
        const uid = user.id || user.Id;
        if (normalizeId(uid) === normalizeId(state.userId)) {
            showGroupInfoError('cannot add yourself');
            return;
        }
        const chat = state.chats.find(function (c) { return normalizeId(c.id) === normalizeId(groupInfoUi.chatId); });
        if (!chat) return;
        const already = getChatMembers(chat).some(function (m) {
            return normalizeId(m.userId || m.UserId) === normalizeId(uid);
        });
        if (already) {
            showGroupInfoError('user is already a member');
            return;
        }
        await api(`/chats/${groupInfoUi.chatId}/members`, 'POST', { userId: uid });
        if (elements.groupInfoInviteInput) elements.groupInfoInviteInput.value = '';
        await refreshGroupInfoModalFromServer();
        renderChats();
        const sel = state.chats.find(function (c) { return normalizeId(c.id) === normalizeId(state.selectedChatId); });
        if (elements.chatSubtitle && sel) elements.chatSubtitle.textContent = getChatSubtitle(sel);
    } catch (error) {
        const msg = (error && error.message) ? String(error.message).toLowerCase() : 'request failed';
        showGroupInfoError(msg);
    }
}

async function refreshGroupInfoModalFromServer() {
    const id = groupInfoUi.chatId;
    if (!id) return;
    await loadChats();
    const chat = state.chats.find(function (c) { return normalizeId(c.id) === normalizeId(id); });
    if (!chat || isDirectChat(chat)) {
        hideGroupInfoModal();
        return;
    }
    renderGroupInfoModal(chat);
}

async function openGroupInfoModal() {
    if (!state.selectedChatId) return;
    const id = state.selectedChatId;
    await loadChats();
    const chat = state.chats.find(function (c) { return normalizeId(c.id) === normalizeId(id); });
    if (!chat || isDirectChat(chat)) return;
    groupInfoUi.chatId = chat.id;
    groupInfoUi.editing = false;
    renderGroupInfoModal(chat);
    showModal(elements.groupInfoModal);
}

function isEncryptedProtocolMessage(message) {
    if (!message) return false;
    const algorithm = String(message.encryptionAlgorithm || '').toLowerCase();
    const protocolVersion = String(message.protocolVersion || '');
    return protocolVersion === '1' || protocolVersion === '3' ||
        algorithm === 'signal-protocol-v1' || algorithm === 'noise-xx-dr-v1';
}

function shouldShowEncryptedLock(message, chat) {
    // Regression guard:
    // - direct + encrypted => true
    // - direct + plaintext => false
    // - group + plaintext => false
    return isDirectChat(chat) && isEncryptedProtocolMessage(message);
}

function getDirectSessionCandidates(chat) {
    const peerUserId = getPeerUserIdForDirect(chat);
    if (!peerUserId || !state.userId) return [];
    const me = normalizeId(state.userId);
    const peer = normalizeId(peerUserId);
    const forward = `${me}:${peer}`;
    const reverse = `${peer}:${me}`;
    return forward === reverse ? [forward] : [forward, reverse];
}

async function hasVerifiedDirectSession(chat) {
    if (!chat || !isDirectChat(chat)) return false;
    if (!window.Crypto25519 || typeof window.Crypto25519.idbGet !== 'function' || typeof window.Crypto25519.getStores !== 'function') {
        return false;
    }
    const stores = window.Crypto25519.getStores();
    if (!stores || !stores.ratchet) return false;
    const candidates = getDirectSessionCandidates(chat);
    for (let i = 0; i < candidates.length; i++) {
        const existing = await window.Crypto25519.idbGet(stores.ratchet, candidates[i]);
        if (existing) {
            return true;
        }
    }
    return false;
}

async function deriveTrustBadgeState(chat) {
    if (!chat || !isDirectChat(chat)) return null;
    const hasVerifiedSession = await hasVerifiedDirectSession(chat);
    return hasVerifiedSession ? 'VERIFIED' : 'UNVERIFIED';
}

async function renderTrustBadge(chat) {
    if (!elements.chatTrustBadge) return;
    const trustState = await deriveTrustBadgeState(chat);
    if (!trustState) {
        elements.chatTrustBadge.classList.add('hidden');
        elements.chatTrustBadge.classList.remove('verified', 'unverified');
        elements.chatTrustBadge.textContent = '';
        return;
    }
    elements.chatTrustBadge.classList.remove('hidden');
    elements.chatTrustBadge.classList.toggle('verified', trustState === 'VERIFIED');
    elements.chatTrustBadge.classList.toggle('unverified', trustState === 'UNVERIFIED');
    elements.chatTrustBadge.textContent = trustState;
}

function updateSafetyButtonVisibility(chat) {
    if (!elements.safetyNumberBtn) return;
    const visible = !!chat && isDirectChat(chat);
    elements.safetyNumberBtn.classList.toggle('hidden', !visible);
    elements.safetyNumberBtn.disabled = !visible;
}

function hideSafetyNumberModal() {
    hideModal(elements.safetyNumberModal);
}

function showSafetyNumberModalText(text) {
    if (elements.safetyNumberText) {
        elements.safetyNumberText.textContent = text;
    }
    showModal(elements.safetyNumberModal);
}

function getPeerUserIdForDirect(chat) {
    if (!chat || !isDirectChat(chat)) return null;
    const members = getChatMembers(chat);
    const peer = members.find(function (m) {
        const memberUserId = m.userId || m.UserId;
        return normalizeId(memberUserId) !== normalizeId(state.userId);
    });
    const memberPeer = peer ? (peer.userId || peer.UserId) : null;
    if (memberPeer) return memberPeer;
    return chat.otherUserId || chat.OtherUserId || chat.peerUserId || chat.PeerUserId || null;
}

function findExistingDirectChatByPeerId(peerUserId) {
    const target = normalizeId(peerUserId);
    return state.chats.find(function (chat) {
        if (!isDirectChat(chat)) return false;
        return normalizeId(getPeerUserIdForDirect(chat)) === target;
    }) || null;
}

function clearSearchModalUserResults() {
    if (!elements.searchModalUserResults) return;
    elements.searchModalUserResults.innerHTML = '';
    elements.searchModalUserResults.classList.add('hidden');
}

function hideModal(modal) {
    if (!modal) return;
    modal.classList.add('hidden');
}

function showModal(modal) {
    if (!modal) return;
    modal.classList.remove('hidden');
}

function setModalStatus(element, message, isError) {
    if (!element) return;
    if (!message) {
        element.textContent = '';
        element.classList.add('hidden');
        element.classList.remove('error');
        return;
    }
    element.textContent = String(message);
    element.classList.remove('hidden');
    element.classList.toggle('error', !!isError);
}

function showSearchModal() {
    if (!elements.searchModal) return;
    setModalStatus(elements.searchModalStatus, '', false);
    clearSearchModalUserResults();
    elements.searchModalInput.value = state.messageSearchQuery || '';
    showModal(elements.searchModal);
    elements.searchModalInput.focus();
    elements.searchModalInput.select();
    const v = String(elements.searchModalInput.value || '').trimStart();
    if (v.startsWith('@')) {
        scheduleModalUserSearch(elements.searchModalInput.value);
    }
}

function hideSearchModal() {
    hideModal(elements.searchModal);
}

function showGroupModal() {
    if (!elements.groupModal) return;
    elements.groupTitleInput.value = 'New Group';
    elements.groupMembersInput.value = '';
    setModalStatus(elements.groupModalStatus, '', false);
    showModal(elements.groupModal);
    elements.groupTitleInput.focus();
}

function hideGroupModal() {
    hideModal(elements.groupModal);
}

function hideDialogModal() {
    hideModal(elements.dialogModal);
    pendingDialogResolve = null;
}

function resolveDialog(value) {
    if (!pendingDialogResolve) {
        hideDialogModal();
        return;
    }
    const resolve = pendingDialogResolve;
    hideDialogModal();
    resolve(value);
}

function showConfirmDialog(title, message, okText = 'OK', cancelText = 'Cancel') {
    if (!elements.dialogModal) return Promise.resolve(false);
    elements.dialogTitle.textContent = title;
    elements.dialogMessage.textContent = message;
    elements.dialogOkBtn.textContent = okText;
    elements.dialogCancelBtn.textContent = cancelText;
    elements.dialogCancelBtn.classList.remove('hidden');
    showModal(elements.dialogModal);
    return new Promise(function (resolve) {
        pendingDialogResolve = resolve;
    });
}

function showInfoDialog(title, message, okText = 'OK') {
    if (!elements.dialogModal) return Promise.resolve();
    elements.dialogTitle.textContent = title;
    elements.dialogMessage.textContent = message;
    elements.dialogOkBtn.textContent = okText;
    elements.dialogCancelBtn.classList.add('hidden');
    showModal(elements.dialogModal);
    return new Promise(function (resolve) {
        pendingDialogResolve = function () { resolve(); };
    });
}

function renderUserSearchResults(users, container) {
    const target = container || elements.searchModalUserResults;
    if (!target) return;
    if (!users || users.length === 0) {
        target.innerHTML = '<div class="search-empty">No users found</div>';
        target.classList.remove('hidden');
        return;
    }

    target.innerHTML = users.map(function (user) {
        const userId = user.id || user.Id;
        const userName = user.userName || user.UserName || 'unknown';
        const displayName = user.displayName || user.DisplayName || userName;
        return `
            <button type="button" class="search-result-item" data-user-id="${escapeHtml(String(userId))}" data-username="${escapeHtml(String(userName))}" data-display-name="${escapeHtml(String(displayName))}">
                <span class="name">${escapeHtml(String(displayName))}</span>
                <span class="username">@${escapeHtml(String(userName))}</span>
            </button>
        `;
    }).join('');
    target.classList.remove('hidden');

    target.querySelectorAll('.search-result-item').forEach(function (item) {
        item.addEventListener('click', async function () {
            const selectedUser = {
                id: item.dataset.userId,
                userName: item.dataset.username,
                displayName: item.dataset.displayName
            };
            await openOrCreateDirectChat(selectedUser);
        });
    });
}

function scheduleModalUserSearch(rawInput) {
    const trimmedStart = String(rawInput || '').trimStart();
    if (!trimmedStart.startsWith('@')) {
        clearSearchModalUserResults();
        return;
    }
    const inner = trimmedStart.slice(1).trim();
    if (searchDebounceTimer) {
        clearTimeout(searchDebounceTimer);
    }
    if (inner.length < 2) {
        clearSearchModalUserResults();
        return;
    }
    searchDebounceTimer = setTimeout(function () {
        void searchUsersModal(inner);
    }, 250);
}

async function searchUsersModal(queryAfterAt) {
    const inner = String(queryAfterAt || '').trim();
    if (inner.length < 2) {
        clearSearchModalUserResults();
        return;
    }
    try {
        const users = await api(`/users/search?q=${encodeURIComponent(inner)}`);
        const filtered = users.filter(function (u) {
            return normalizeId(u.id || u.Id) !== normalizeId(state.userId);
        });
        renderUserSearchResults(filtered, elements.searchModalUserResults);
    } catch (error) {
        console.error('User search failed:', error);
        if (elements.searchModalUserResults) {
            elements.searchModalUserResults.innerHTML = '<div class="search-empty">Search failed</div>';
            elements.searchModalUserResults.classList.remove('hidden');
        }
    }
}

async function ensureJoinedChatGroup(chatId) {
    if (!connection || connection.state !== signalR.HubConnectionState.Connected) return;
    try {
        await connection.invoke('JoinChat', chatId);
    } catch (error) {
        console.error('Failed to join chat group:', error);
    }
}

async function leaveChatGroup(chatId) {
    if (!connection || connection.state !== signalR.HubConnectionState.Connected) return;
    try {
        await connection.invoke('LeaveChat', chatId);
    } catch (error) {
        console.error('Failed to leave chat group:', error);
    }
}

function onRemovedFromChat(chatId) {
    const id = normalizeId(chatId);
    void leaveChatGroup(id);
    state.chats = state.chats.filter(function (c) { return normalizeId(c.id) !== id; });
    if (normalizeId(state.selectedChatId) === id) {
        state.selectedChatId = null;
        state.messages = [];
        state.messageSearchQuery = '';
        if (elements.chatTitle) elements.chatTitle.textContent = 'Select a chat';
        if (elements.chatSubtitle) elements.chatSubtitle.textContent = '';
        if (elements.messageInput) elements.messageInput.disabled = true;
        if (elements.sendBtn) elements.sendBtn.disabled = true;
        if (elements.attachBtn) elements.attachBtn.disabled = true;
        if (elements.groupInfoBtn) elements.groupInfoBtn.classList.add('hidden');
        updateSafetyButtonVisibility(null);
    }
    if (groupInfoUi.chatId && normalizeId(groupInfoUi.chatId) === id) {
        hideGroupInfoModal();
    }
    renderChats();
    renderMessages();
}

async function openOrCreateDirectChat(user) {
    const userId = user.id || user.Id;
    if (!userId) return;
    const existing = findExistingDirectChatByPeerId(userId);
    if (existing) {
        clearSearchModalUserResults();
        if (elements.searchModalInput) elements.searchModalInput.value = '';
        hideSearchModal();
        await selectChat(existing.id);
        return;
    }

    const userName = user.userName || user.UserName || 'user';
    const displayName = user.displayName || user.DisplayName || userName;
    const request = {
        title: String(displayName),
        type: 1,
        description: null,
        memberIds: [userId]
    };
    try {
        const newChat = await api('/chats', 'POST', request);
        const duplicate = findExistingDirectChatByPeerId(userId);
        if (!duplicate) {
            state.chats.unshift(newChat);
        }
        await ensureJoinedChatGroup(newChat.id);
        renderChats();
        clearSearchModalUserResults();
        if (elements.searchModalInput) elements.searchModalInput.value = '';
        hideSearchModal();
        await selectChat((duplicate || newChat).id);
        await loadChats();
    } catch (error) {
        console.error('Failed to create/open direct chat:', error);
    }
}

function hideChatContextMenu() {
    if (!elements.chatContextMenu) return;
    elements.chatContextMenu.classList.add('hidden');
    state.selectedContextChatId = null;
}

function showChatContextMenu(chatId, x, y) {
    if (!elements.chatContextMenu) return;
    state.selectedContextChatId = chatId;
    const menu = elements.chatContextMenu;
    menu.style.left = `${x}px`;
    menu.style.top = `${y}px`;
    menu.classList.remove('hidden');
}

async function deleteChat(chatId) {
    if (!chatId) return;
    const chat = state.chats.find(function (x) { return x.id === chatId; });
    const confirmed = await showConfirmDialog(
        'Delete chat',
        `Delete chat "${chat ? chat.title : 'selected chat'}"?`,
        'Delete',
        'Cancel');
    if (!confirmed) return;
    await api(`/chats/${chatId}`, 'DELETE');
    if (normalizeId(state.selectedChatId) === normalizeId(chatId)) {
        state.selectedChatId = null;
        state.messages = [];
        state.messageSearchQuery = '';
        elements.chatTitle.textContent = 'Select a chat';
        if (elements.chatSubtitle) elements.chatSubtitle.textContent = '';
        elements.messageInput.disabled = true;
        if (elements.sendBtn) elements.sendBtn.disabled = true;
        if (elements.attachBtn) elements.attachBtn.disabled = true;
        if (elements.groupInfoBtn) elements.groupInfoBtn.classList.add('hidden');
        updateSafetyButtonVisibility(null);
        renderMessages();
    }
    await loadChats();
}

async function clearChatMessages(chatId) {
    if (!chatId) return;
    const confirmed = await showConfirmDialog(
        'Clear messages',
        'Clear all messages in this chat?',
        'Clear',
        'Cancel');
    if (!confirmed) return;
    await api(`/chats/${chatId}/messages`, 'DELETE');
    if (normalizeId(state.selectedChatId) === normalizeId(chatId)) {
        state.messages = [];
        renderMessages();
    }
    await loadChats();
}

function normalizeUserQuery(value) {
    return String(value || '').trim().toLowerCase();
}

async function resolveUserByQuery(rawQuery) {
    const query = normalizeUserQuery(rawQuery);
    if (!query) return null;
    const users = await api(`/users/search?q=${encodeURIComponent(query)}`);
    const candidates = users.filter(function (u) {
        const id = normalizeId(u.id || u.Id);
        return id !== normalizeId(state.userId);
    });
    if (candidates.length === 0) return null;
    const exact = candidates.find(function (u) {
        const username = normalizeUserQuery(u.userName || u.UserName);
        const displayName = normalizeUserQuery(u.displayName || u.DisplayName);
        return username === query || displayName === query;
    });
    return exact || candidates[0];
}

async function createGroupChatFlow() {
    const title = String(elements.groupTitleInput ? elements.groupTitleInput.value : '').trim();
    const membersRaw = String(elements.groupMembersInput ? elements.groupMembersInput.value : '').trim();

    const tokens = membersRaw
        .split(',')
        .map(function (x) { return x.trim(); })
        .filter(function (x) { return x.length > 0; });

    if (tokens.length === 0) {
        setModalStatus(elements.groupModalStatus, 'Enter at least one username.', true);
        return;
    }

    const resolved = [];
    for (const token of tokens) {
        const user = await resolveUserByQuery(token);
        if (!user) {
            setModalStatus(elements.groupModalStatus, `User not found: ${token}`, true);
            return;
        }
        resolved.push(user);
    }

    const memberIds = Array.from(new Set(
        resolved.map(function (u) { return u.id || u.Id; })
    ));
    if (memberIds.length === 0) {
        setModalStatus(elements.groupModalStatus, 'No members resolved.', true);
        return;
    }

    const created = await api('/chats', 'POST', {
        title: String(title || '').trim() || 'New Group',
        type: 2,
        description: null,
        memberIds: memberIds
    });

    await loadChats();
    await ensureJoinedChatGroup(created.id);
    await selectChat(created.id);
    hideGroupModal();
}

function highlightMessageText(text, query) {
    const raw = String(text || '');
    const needle = String(query || '').trim();
    if (!needle) return escapeHtml(raw);

    const lowerRaw = raw.toLowerCase();
    const lowerNeedle = needle.toLowerCase();
    let from = 0;
    let out = '';

    while (from < raw.length) {
        const idx = lowerRaw.indexOf(lowerNeedle, from);
        if (idx === -1) {
            out += escapeHtml(raw.slice(from));
            break;
        }
        out += escapeHtml(raw.slice(from, idx));
        out += `<span class="text-highlight">${escapeHtml(raw.slice(idx, idx + needle.length))}</span>`;
        from = idx + needle.length;
    }
    return out;
}

function countMessageMatches(query) {
    const needle = String(query || '').trim().toLowerCase();
    if (!needle) return 0;
    let count = 0;
    for (const msg of state.messages) {
        const text = String(msg._plaintextCached || '').toLowerCase();
        if (text.includes(needle)) count++;
    }
    return count;
}

function canCreateChatSafetyNumber(chat) {
    if (!chat) return false;
    const isDirect = isDirectChat(chat);
    if (isDirect) return true;
    const members = getChatMembers(chat);
    const me = members.find(function (m) {
        const id = String(m.userId || m.UserId || '').toLowerCase();
        return id === String(state.userId || '').toLowerCase();
    });
    const role = me ? (me.role !== undefined ? me.role : me.Role) : null;
    return role === 1 || role === '1' || (typeof role === 'string' && role.toLowerCase() === 'owner');
}

function getRecipientUserId(chat) {
    if (!chat) return null;

    const chatType = getChatType(chat);
    const isDirect = isDirectChat(chat);
    if (chatType !== undefined && !isDirect) {
        return null;
    }

    const members = getChatMembers(chat);
    if (members.length > 0) {
        const other = members.find(function (m) {
            const memberUserId = m.userId || m.UserId;
            return memberUserId && String(memberUserId).toLowerCase() !== String(state.userId).toLowerCase();
        });
        const otherUserId = other ? (other.userId || other.UserId) : null;
        if (otherUserId) return otherUserId;
    }

    if (chat.otherUserId || chat.OtherUserId) {
        return chat.otherUserId || chat.OtherUserId;
    }
    if (chat.peerUserId || chat.PeerUserId) {
        return chat.peerUserId || chat.PeerUserId;
    }

    // Fallback for direct chats if explicit peer fields are absent:
    // infer from title in legacy DTOs is unsafe, so return null.
    // Caller will surface group/unsupported warning.
    if (isDirect) {
        console.warn('Direct chat detected but recipient could not be resolved from DTO shape.', chat);
    }
    return null;
}

async function api(endpoint, method = 'GET', body = null) {
    const headers = { 'Content-Type': 'application/json' };
    if (state.token) headers['Authorization'] = `Bearer ${state.token}`;
    
    const options = { method, headers };
    if (body) options.body = JSON.stringify(body);
    
    const response = await fetch(`${API_BASE}${endpoint}`, options);
    
    if (!response.ok) {
        const error = await response.json().catch(() => ({ message: 'Request failed' }));
        console.error('API error', response.status, JSON.stringify(error));
        throw new Error(error.message || error.title || `HTTP ${response.status}`);
    }
    
    if (response.status === 204) {
        return null;
    }
    const contentType = response.headers.get('content-type') || '';
    if (!contentType.toLowerCase().includes('application/json')) {
        return null;
    }
    return response.json();
}

function isImageFileName(name) {
    if (!name || typeof name !== 'string') return false;
    const lower = name.toLowerCase();
    return /\.(png|jpe?g|gif|webp|bmp)$/.test(lower);
}

function isLegacyImageFileLine(text) {
    if (!text || typeof text !== 'string') return false;
    const t = text.trim();
    return /^\[File:\s*(.+\.(?:png|jpg|jpeg|gif|webp|bmp))\s*\]$/i.test(t);
}

function revokeAllMessageMediaUrls(list) {
    if (!list) return;
    for (let i = 0; i < list.length; i++) {
        const msg = list[i];
        if (msg._mediaBlobUrl) {
            try { URL.revokeObjectURL(msg._mediaBlobUrl); } catch (_) { /* noop */ }
            msg._mediaBlobUrl = null;
        }
        if (msg._imagePreviewUrl) {
            try { URL.revokeObjectURL(msg._imagePreviewUrl); } catch (_) { /* noop */ }
            msg._imagePreviewUrl = null;
        }
    }
}

async function uploadMediaFile(file) {
    const form = new FormData();
    form.append('file', file, file.name);
    const res = await fetch(`${API_BASE}/media/upload`, {
        method: 'POST',
        headers: state.token ? { Authorization: `Bearer ${state.token}` } : {},
        body: form
    });
    if (!res.ok) {
        const err = await res.json().catch(() => ({}));
        throw new Error(err.message || err.title || `Upload failed (${res.status})`);
    }
    return await res.json();
}

async function fetchMediaBlob(mediaId) {
    const id = encodeURIComponent(String(mediaId));
    const res = await fetch(`${API_BASE}/media/${id}`, {
        headers: state.token ? { Authorization: `Bearer ${state.token}` } : {}
    });
    if (!res.ok) throw new Error(`Media HTTP ${res.status}`);
    return await res.blob();
}

async function ensureMessageMediaPreviews() {
    const jobs = [];
    for (let i = 0; i < state.messages.length; i++) {
        const msg = state.messages[i];
        const kind = Number(msg.kind ?? msg.Kind ?? 1);
        const mediaId = msg.mediaId || msg.MediaId;
        if (kind !== 2 || !mediaId) continue;
        if (msg._imagePreviewUrl || msg._mediaBlobUrl) continue;
        jobs.push((async function (m, mid) {
            try {
                const blob = await fetchMediaBlob(mid);
                m._mediaBlobUrl = URL.createObjectURL(blob);
            } catch (e) {
                console.warn('Media preview failed', mid, e);
            }
        })(msg, mediaId));
    }
    await Promise.all(jobs);
}

function updateAttachmentChipUI() {
    if (!elements.attachmentChip || !elements.attachmentChipName) return;
    if (!pendingAttachmentFile) {
        elements.attachmentChip.classList.add('hidden');
        elements.attachmentChipName.textContent = '';
        return;
    }
    elements.attachmentChipName.textContent = pendingAttachmentFile.name || 'file';
    elements.attachmentChip.classList.remove('hidden');
}

function clearPendingAttachment() {
    pendingAttachmentFile = null;
    if (elements.fileInput) elements.fileInput.value = '';
    updateAttachmentChipUI();
}

function buildMessageBodyHtml(msg, rawText, searchQuery) {
    const kind = Number(msg.kind ?? msg.Kind ?? 1);
    const mediaId = msg.mediaId || msg.MediaId;
    const imgUrl = msg._imagePreviewUrl || msg._mediaBlobUrl || '';

    let display = String(rawText);
    if (display === '[decrypting...]') {
        return `<span>${escapeHtml(display)}</span>`;
    }
    if (kind === 1 && isLegacyImageFileLine(display)) {
        display = '📷';
    }
    if (kind === 2) {
        display = display.trim();
    }

    const parts = [];
    if (kind === 2) {
        if (imgUrl) {
            parts.push(`<div class="msg-image-wrap"><img class="msg-inline-img" src="${escapeHtml(imgUrl)}" alt="" /></div>`);
        } else if (mediaId) {
            parts.push('<div class="msg-image-placeholder">📷</div>');
        }
        if (display) {
            parts.push(`<span class="msg-caption">${highlightMessageText(display, searchQuery)}</span>`);
        }
    } else {
        parts.push(`<span>${highlightMessageText(display, searchQuery)}</span>`);
    }
    return parts.join('');
}

function showScreen(screen) {
    elements.loginScreen.classList.add('hidden');
    elements.mainScreen.classList.add('hidden');
    screen.classList.remove('hidden');
}

async function login(username, password) {
    try {
        const response = await api('/auth/login', 'POST', { 
            username: username, 
            password: password 
        });
        state.token = response.accessToken || response.AccessToken;
        state.userId = response.userId || response.UserId;
        state.username = username;
        
        localStorage.setItem('bitcanary_token', state.token);
        localStorage.setItem('bitcanary_user', username);
        localStorage.setItem('bitcanary_userid', state.userId);
        
        showScreen(elements.mainScreen);
        elements.currentUser.textContent = username ? `[ @${username} ]` : '';
        if (elements.topbarStatus) {
            elements.topbarStatus.textContent = (response.displayName || response.DisplayName)
                ? `Welcome, ${response.displayName || response.DisplayName}!`
                : '';
        }

        try {
            await window.Crypto25519.ensureKeyBundlePublished(state.token, state.userId);
        } catch (err) {
            console.error('Key bundle publication failed:', err);
            // Non-fatal: chat UI remains usable and publication retries on next session restore.
        }
        
        await loadChats();
        await initSignalR();
    } catch (error) {
        elements.loginError.textContent = error.message;
        elements.loginError.classList.remove('hidden');
    }
}

async function logout() {
    if (connection) {
        await connection.stop();
        connection = null;
    }
    clearPendingAttachment();
    state = { token: null, userId: null, username: null, chats: [], selectedChatId: null, selectedContextChatId: null, messageSearchQuery: '', chatFilter: '', messages: [] };
    localStorage.removeItem('bitcanary_token');
    localStorage.removeItem('bitcanary_user');
    localStorage.removeItem('bitcanary_userid');
    showScreen(elements.loginScreen);
}

async function loadChats() {
    try {
        state.chats = await api('/chats');
        renderChats();
        if (state.selectedChatId && !state.chats.some(function (c) { return c.id === state.selectedChatId; })) {
            state.selectedChatId = null;
            state.messages = [];
            if (elements.messageInput) elements.messageInput.disabled = true;
            if (elements.sendBtn) elements.sendBtn.disabled = true;
            if (elements.attachBtn) elements.attachBtn.disabled = true;
            clearPendingAttachment();
            renderMessages();
        }
    } catch (error) {
        console.error('Failed to load chats:', error);
    }
}

function renderChats() {
    const filter = String(state.chatFilter || '').trim().toLowerCase();
    const list = !filter
        ? state.chats
        : state.chats.filter(function (chat) {
            const title = String(chat.title || '').toLowerCase();
            const preview = String(chat.lastMessage && (chat.lastMessage.encryptedPayload || chat.lastMessage.EncryptedPayload) || '').toLowerCase();
            return title.includes(filter) || preview.includes(filter);
        });

    elements.chatsContainer.innerHTML = list.map(chat => `
        <div class="chat-item ${chat.id === state.selectedChatId ? 'active' : ''}" data-id="${chat.id}">
            <div class="chat-item-main">
                <div class="title">${escapeHtml(chat.title)}</div>
                <div class="preview">${escapeHtml(chat.lastMessage?.encryptedPayload || '')}</div>
            </div>
            <div class="chat-item-meta">
                <div class="time">${formatTime(chat.lastMessage?.createdAtUtc)}</div>
            </div>
        </div>
    `).join('');
    
    elements.chatsContainer.querySelectorAll('.chat-item').forEach(item => {
        item.addEventListener('click', () => selectChat(item.dataset.id));
        item.addEventListener('contextmenu', (e) => {
            e.preventDefault();
            showChatContextMenu(item.dataset.id, e.clientX, e.clientY);
        });
    });
}

async function selectChat(chatId) {
    hideChatContextMenu();
    state.selectedChatId = chatId;
    state.messageSearchQuery = '';
    renderChats();
    
    const chat = state.chats.find(c => c.id === chatId);
    elements.chatTitle.textContent = chat?.title || 'Select a chat';
    if (elements.chatSubtitle) {
        elements.chatSubtitle.textContent = chat ? getChatSubtitle(chat) : '';
    }
    if (elements.groupInfoBtn) {
        if (chat && !isDirectChat(chat)) {
            elements.groupInfoBtn.classList.remove('hidden');
        } else {
            elements.groupInfoBtn.classList.add('hidden');
        }
    }
    
    elements.messageInput.disabled = false;
    if (elements.sendBtn) elements.sendBtn.disabled = false;
    if (elements.attachBtn) elements.attachBtn.disabled = false;
    updateSafetyButtonVisibility(chat);
    hideSafetyNumberModal();
    void renderTrustBadge(chat);
    
    await loadMessages();
    await markCurrentChatRead();
}

async function showSafetyNumber() {
    if (!state.selectedChatId) return;
    const chat = state.chats.find(c => c.id === state.selectedChatId);
    if (!chat) return;
    const canCreate = isDirectChat(chat) || canCreateChatSafetyNumber(chat);
    try {
        const value = await window.Crypto25519.getOrCreateChatSafetyNumber(
            state.userId,
            state.selectedChatId,
            canCreate
        );
        if (!value) {
            showSafetyNumberModalText('Safety number is stored only by chat owner for this chat.');
        } else {
            showSafetyNumberModalText(value);
        }
    } catch (err) {
        const text = (err && err.message) ? err.message : String(err);
        showSafetyNumberModalText('Safety number error: ' + text);
    }
}

async function regenerateSafetyNumber() {
    if (!state.selectedChatId) return;
    const chat = state.chats.find(c => c.id === state.selectedChatId);
    if (!chat) return;
    if (isDirectChat(chat)) {
        // Direct safety number is identity-based; regenerate = recompute.
        await showSafetyNumber();
        return;
    }
    const canCreate = canCreateChatSafetyNumber(chat);
    if (!canCreate) {
        showSafetyNumberModalText('Only chat owner can regenerate safety number for this chat type.');
        return;
    }
    const confirmed = await showConfirmDialog(
        'Regenerate Safety Number',
        'Regenerate chat safety number for this chat?',
        'Regenerate',
        'Cancel');
    if (!confirmed) return;
    try {
        const value = await window.Crypto25519.regenerateChatSafetyNumber(
            state.userId,
            state.selectedChatId,
            canCreate
        );
        showSafetyNumberModalText(value);
    } catch (err) {
        const text = (err && err.message) ? err.message : String(err);
        showSafetyNumberModalText('Regenerate failed: ' + text);
    }
}

async function loadMessages() {
    if (!state.selectedChatId) return;
    
    try {
        revokeAllMessageMediaUrls(state.messages);
        state.messages = await api(`/chats/${state.selectedChatId}/messages`);
        await decryptAllMessages();
        await ensureMessageMediaPreviews();
        renderMessages();
    } catch (error) {
        console.error('Failed to load messages:', error);
    }
}

async function initSignalR() {
    connection = new signalR.HubConnectionBuilder()
        .withUrl(HUB_URL, { accessTokenFactory: () => state.token })
        .withAutomaticReconnect()
        .build();

    connection.on('MessageReceived', onMessageReceived);
    connection.on('MessageDelivered', onMessageDelivered);
    connection.on('MessageRead', onMessageRead);
    connection.on('RemovedFromChat', onRemovedFromChat);

    connection.onreconnected(async () => {
        for (const chat of state.chats) {
            await connection.invoke('JoinChat', chat.id);
        }
    });

    await connection.start();

    for (const chat of state.chats) {
        await connection.invoke('JoinChat', chat.id);
    }
}

function onMessageDelivered(messageId) {
    const normalizedMessageId = normalizeId(messageId);
    for (let i = 0; i < state.messages.length; i++) {
        const msg = state.messages[i];
        if (normalizeId(msg.id) === normalizedMessageId && normalizeId(msg.senderId) === normalizeId(state.userId)) {
            msg.status = 1;
            msg._status = 'delivered';
            renderMessages();
            return;
        }
    }
}

function onMessageRead(chatId, readByUserId) {
    if (normalizeId(chatId) !== normalizeId(state.selectedChatId)) return;
    if (normalizeId(readByUserId) === normalizeId(state.userId)) return;
    let changed = false;
    for (let i = 0; i < state.messages.length; i++) {
        const msg = state.messages[i];
        if (normalizeId(msg.senderId) !== normalizeId(state.userId)) continue;
        if (normalizeMessageStatus(msg._status || msg.status) !== 'read') {
            msg.status = 2;
            msg._status = 'read';
            changed = true;
        }
    }
    if (changed) {
        renderMessages();
    }
}

async function markCurrentChatRead() {
    if (!connection || connection.state !== signalR.HubConnectionState.Connected) return;
    if (!state.selectedChatId) return;
    try {
        await connection.invoke('ReadMessages', state.selectedChatId);
    } catch (error) {
        console.error('ReadMessages invoke failed:', error);
    }
}

async function onMessageReceived(message) {
    if (!state.chats.some(function (chat) { return chat.id === message.chatId; })) {
        await loadChats();
        await ensureJoinedChatGroup(message.chatId);
    }
    if (message.chatId === state.selectedChatId) {
        state.messages.push(message);
        await decryptAllMessages();
        await ensureMessageMediaPreviews();
        renderMessages();
        await markCurrentChatRead();
    }
    loadChats();
}

async function decryptAllMessages() {
    // Important: decrypt in chronological order so X3DH bootstrap message
    // is processed before later DR-only messages.
    const ordered = state.messages
        .map(function (msg, idx) { return { msg: msg, idx: idx }; })
        .sort(function (a, b) {
            const aTs = new Date(a.msg.createdAtUtc || a.msg.CreatedAtUtc || 0).getTime();
            const bTs = new Date(b.msg.createdAtUtc || b.msg.CreatedAtUtc || 0).getTime();
            if (aTs === bTs) return a.idx - b.idx;
            return aTs - bTs;
        });

    for (let i = 0; i < ordered.length; i++) {
        const msg = ordered[i].msg;
        if (msg._plaintextCached !== undefined) continue;
        if (msg.senderId === state.userId &&
            (msg.encryptionAlgorithm === 'signal-protocol-v1' || msg.encryptionAlgorithm === 'noise-xx-dr-v1')) {
            const byId = msg.id ? sentPlaintextByMessageId.get(msg.id) : null;
            msg._plaintextCached = byId || consumeOutgoingPlaintext(state.selectedChatId) || '[encrypted]';
            continue;
        }
        msg._plaintextCached = await window.Crypto25519.decryptMessage(state.userId, msg, state.token);
        if (msg._plaintextCached === '[Unable to decrypt]') {
            const hint = getMetadataHints(msg);
            pushDecryptLog(`message=${msg.id || 'n/a'} sender=${msg.senderId || 'n/a'} chat=${msg.chatId || state.selectedChatId} phase=first-pass keyEnvelope=${msg.keyEnvelope || 'n/a'} pv=${msg.protocolVersion || 'n/a'} hasNoise=${hint.hasNoise} n=${hint.n ?? 'n/a'} pn=${hint.pn ?? 'n/a'}`);
        }
    }

    // Second pass: some DR-only messages may fail on first pass if their
    // bootstrap message appears later in the list returned by API.
    for (let i = 0; i < ordered.length; i++) {
        const msg = ordered[i].msg;
        const isEncryptedProtocol = msg.protocolVersion === 1 || msg.protocolVersion === '1' ||
            msg.protocolVersion === 3 || msg.protocolVersion === '3' ||
            msg.encryptionAlgorithm === 'signal-protocol-v1' || msg.encryptionAlgorithm === 'noise-xx-dr-v1';
        if (!isEncryptedProtocol) continue;
        if (msg.senderId === state.userId) continue;
        if (msg._plaintextCached !== '[Unable to decrypt]') continue;

        const retried = await window.Crypto25519.decryptMessage(state.userId, msg, state.token);
        if (retried !== '[Unable to decrypt]') {
            msg._plaintextCached = retried;
        } else {
            const hint = getMetadataHints(msg);
            pushDecryptLog(`message=${msg.id || 'n/a'} sender=${msg.senderId || 'n/a'} chat=${msg.chatId || state.selectedChatId} phase=retry keyEnvelope=${msg.keyEnvelope || 'n/a'} pv=${msg.protocolVersion || 'n/a'} hasNoise=${hint.hasNoise} n=${hint.n ?? 'n/a'} pn=${hint.pn ?? 'n/a'}`);
        }
    }
}

function renderMessages() {
    const activeChat = state.chats.find(function (chat) { return chat.id === state.selectedChatId; }) || null;
    const searchQuery = String(state.messageSearchQuery || '').trim();
    elements.messagesContainer.innerHTML = state.messages.map(msg => {
        const text = msg._plaintextCached !== undefined ? msg._plaintextCached : '[decrypting...]';
        const showEncryptedLock = shouldShowEncryptedLock(msg, activeChat);
        const isOutgoing = msg.senderId === state.userId;
        const outgoingStatus = normalizeMessageStatus(msg._status || msg.status);
        const outgoingStatusLabel = getOutgoingStatusLabel(outgoingStatus);
        const bodyHtml = buildMessageBodyHtml(msg, text, searchQuery);
        const rawName = msg.senderDisplayName || 'unknown';
        const ircName = rawName.startsWith('<@') ? rawName : `<@${rawName}>`;
        return `
            <div class="message-row ${isOutgoing ? 'outgoing' : 'incoming'}">
                <div class="msg-time">${formatTime(msg.createdAtUtc)}</div>
                <div class="msg-sender">${escapeHtml(ircName)}</div>
                <div class="msg-body">
                    ${bodyHtml}
                    ${showEncryptedLock ? '<span class="msg-lock" title="encrypted">🔒</span>' : ''}
                </div>
                <div class="msg-meta">
                    ${isOutgoing ? `<div class="msg-status status-${outgoingStatus}">${escapeHtml(outgoingStatusLabel)}</div>` : ''}
                </div>
            </div>
        `;
    }).join('');
    
    elements.messagesContainer.scrollTop = elements.messagesContainer.scrollHeight;
    void renderTrustBadge(activeChat);
}

async function sendMessage(text) {
    if (!state.selectedChatId) return;

    const inputTrim = String(text || '').trim();
    let combinedText = inputTrim;
    const hasImagePending = !!(pendingAttachmentFile && isImageFileName(pendingAttachmentFile.name));

    if (pendingAttachmentFile && !hasImagePending) {
        combinedText = inputTrim
            ? `${inputTrim}\n[File: ${pendingAttachmentFile.name}]`
            : `[File: ${pendingAttachmentFile.name}]`;
    }

    if (!String(combinedText).trim() && !hasImagePending) return;

    const plaintextForEncrypt = hasImagePending
        ? (inputTrim || ' ')
        : combinedText;

    const displayCaptionOptimistic = hasImagePending ? inputTrim : combinedText;

    const chat = state.chats.find(function (c) { return c.id === state.selectedChatId; });
    const recipientUserId = getRecipientUserId(chat);
    const clientMessageId = crypto.randomUUID();

    let mediaId = null;
    let messageKind = 1;
    if (hasImagePending && pendingAttachmentFile) {
        try {
            const upload = await uploadMediaFile(pendingAttachmentFile);
            mediaId = upload.mediaId || upload.MediaId;
            messageKind = 2;
        } catch (uploadErr) {
            console.error('Media upload failed:', uploadErr);
            return;
        }
    }

    let payload;

    if (recipientUserId) {
        try {
            let enc = await window.Crypto25519.encryptText(
                state.token, state.userId, recipientUserId, plaintextForEncrypt);
            if ((enc.protocolVersion || 3) === 3) {
                const meta = enc.metadataJson ? JSON.parse(enc.metadataJson) : null;
                if (!meta || !meta.noise) {
                    console.warn('[send-runtime-guard] pv=3 without noise; regenerating bundle and retrying once');
                    await window.Crypto25519.regenerateBundle(state.token, state.userId);
                    enc = await window.Crypto25519.encryptText(
                        state.token, state.userId, recipientUserId, plaintextForEncrypt);
                    const retryMeta = enc.metadataJson ? JSON.parse(enc.metadataJson) : null;
                    if (!retryMeta || !retryMeta.noise) {
                        throw new Error('Runtime guard: pv=3 metadata missing noise after retry');
                    }
                }
            }
            payload = {
                chatId: state.selectedChatId,
                clientMessageId: clientMessageId,
                kind: messageKind,
                encryptedPayload: enc.encryptedPayload,
                encryptionAlgorithm: enc.encryptionAlgorithm,
                keyEnvelope: enc.keyEnvelope,
                metadataJson: enc.metadataJson,
                protocolVersion: enc.protocolVersion || 3
            };
            if (mediaId) payload.mediaId = mediaId;
        } catch (err) {
            console.error('Encryption failed, falling back to plaintext:', err);
            payload = {
                chatId: state.selectedChatId,
                clientMessageId: clientMessageId,
                kind: messageKind,
                encryptedPayload: plaintextForEncrypt,
                encryptionAlgorithm: 'plaintext',
                keyEnvelope: '',
                metadataJson: null,
                protocolVersion: 2
            };
            if (mediaId) payload.mediaId = mediaId;
        }
    } else {
        // Phase 26: group/channel messages in web use plaintext path (no E2E).
        payload = {
            chatId: state.selectedChatId,
            clientMessageId: clientMessageId,
            kind: messageKind,
            encryptedPayload: plaintextForEncrypt,
            encryptionAlgorithm: 'plaintext',
            keyEnvelope: '',
            metadataJson: null,
            protocolVersion: 2
        };
        if (mediaId) payload.mediaId = mediaId;
    }

    let previewUrl = null;
    if (hasImagePending && pendingAttachmentFile) {
        try {
            previewUrl = URL.createObjectURL(pendingAttachmentFile);
        } catch (_) { /* noop */ }
    }

    const optimisticMessage = {
        id: clientMessageId,
        chatId: state.selectedChatId,
        senderId: state.userId,
        senderDisplayName: state.username,
        kind: messageKind,
        mediaId: mediaId || null,
        encryptedPayload: payload.encryptedPayload,
        encryptionAlgorithm: payload.encryptionAlgorithm,
        keyEnvelope: payload.keyEnvelope || '',
        metadataJson: payload.metadataJson || null,
        createdAtUtc: new Date().toISOString(),
        protocolVersion: payload.protocolVersion,
        status: 0,
        _status: 'sending',
        _plaintextCached: displayCaptionOptimistic,
        _imagePreviewUrl: previewUrl || undefined
    };
    state.messages.push(optimisticMessage);
    renderMessages();

    function revokeOptimisticPreview() {
        const found = state.messages.find(function (m) {
            return normalizeId(m.id) === normalizeId(clientMessageId);
        });
        if (found && found._imagePreviewUrl) {
            try { URL.revokeObjectURL(found._imagePreviewUrl); } catch (_) { /* noop */ }
            found._imagePreviewUrl = undefined;
        }
    }

    try {
        const sentMessage = await api('/messages', 'POST', payload);
        elements.messageInput.value = '';
        clearPendingAttachment();
        revokeOptimisticPreview();
        state.messages = state.messages.filter(function (m) {
            return normalizeId(m.id) !== normalizeId(clientMessageId);
        });
        if (payload.protocolVersion === 1 || payload.protocolVersion === 3) {
            enqueueOutgoingPlaintext(state.selectedChatId, plaintextForEncrypt);
            if (sentMessage && sentMessage.id) {
                sentPlaintextByMessageId.set(sentMessage.id, plaintextForEncrypt);
                saveSentPlaintextCache();
            }
        }
        sentMessage._status = normalizeMessageStatus(sentMessage.status);
        await loadMessages();
        await loadChats();
    } catch (error) {
        revokeOptimisticPreview();
        state.messages = state.messages.filter(function (m) {
            return normalizeId(m.id) !== normalizeId(clientMessageId);
        });
        renderMessages();
        console.error('Failed to send message:', error);
    }
}

function escapeHtml(text) {
    if (!text) return '';
    return text.replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}

function formatTime(isoString) {
    if (!isoString) return '';
    const date = new Date(isoString);
    const now = new Date();
    const isToday = date.toDateString() === now.toDateString();
    
    if (isToday) {
        return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    }
    return date.toLocaleDateString([], { month: 'short', day: 'numeric' });
}

async function checkSession() {
    const token = localStorage.getItem('bitcanary_token');
    const username = localStorage.getItem('bitcanary_user');
    const userId = localStorage.getItem('bitcanary_userid');

    if (token && username) {
        state.token = token;
        state.username = username;
        state.userId = userId;
        showScreen(elements.mainScreen);
        elements.currentUser.textContent = username ? `[ @${username} ]` : '';
        if (elements.topbarStatus) {
            elements.topbarStatus.textContent = '';
        }
        try {
            await window.Crypto25519.ensureKeyBundlePublished(state.token, state.userId);
        } catch (err) {
            console.error('Key bundle publication failed:', err);
        }
        await loadChats();
        await initSignalR();
    }
}

elements.loginForm.addEventListener('submit', async (e) => {
    e.preventDefault();
    elements.loginError.classList.add('hidden');
    await login(elements.usernameInput.value, elements.passwordInput.value);
});

if (elements.settingsLogoutBtn) {
    elements.settingsLogoutBtn.addEventListener('click', function () {
        void logout();
        if (elements.settingsModal) elements.settingsModal.classList.add('hidden');
    });
}

elements.messageForm.addEventListener('submit', async (e) => {
    e.preventDefault();
    await sendMessage(elements.messageInput.value);
});
elements.safetyNumberBtn.addEventListener('click', async () => {
    await showSafetyNumber();
});
if (elements.sidebarSearchBtn) {
    elements.sidebarSearchBtn.addEventListener('click', function () {
        showSearchModal();
    });
}
if (elements.topbarRefreshBtn) {
    elements.topbarRefreshBtn.addEventListener('click', async function () {
        await loadChats();
        if (state.selectedChatId) {
            await loadMessages();
        }
    });
}
if (elements.searchModalApplyBtn) {
    elements.searchModalApplyBtn.addEventListener('click', function () {
        const raw = String(elements.searchModalInput ? elements.searchModalInput.value : '').trim();
        if (raw.startsWith('@')) {
            setModalStatus(
                elements.searchModalStatus,
                'Pick a user from the list above, or erase @ to search message text.',
                false);
            return;
        }
        if (!state.selectedChatId) {
            setModalStatus(elements.searchModalStatus, 'Select a chat to search messages in.', true);
            return;
        }
        state.messageSearchQuery = raw;
        renderMessages();
        if (state.messageSearchQuery) {
            const matches = countMessageMatches(state.messageSearchQuery);
            if (matches === 0) {
                setModalStatus(elements.searchModalStatus, 'No matches found.', false);
                return;
            }
        }
        hideSearchModal();
    });
}
if (elements.searchModalClearBtn) {
    elements.searchModalClearBtn.addEventListener('click', function () {
        state.messageSearchQuery = '';
        if (elements.searchModalInput) {
            elements.searchModalInput.value = '';
        }
        clearSearchModalUserResults();
        setModalStatus(elements.searchModalStatus, 'Search cleared.', false);
        renderMessages();
    });
}
if (elements.searchModalCancelBtn) {
    elements.searchModalCancelBtn.addEventListener('click', function () {
        hideSearchModal();
    });
}
if (elements.searchModalInput) {
    elements.searchModalInput.addEventListener('input', function () {
        scheduleModalUserSearch(elements.searchModalInput.value);
    });
    elements.searchModalInput.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') {
            e.preventDefault();
            const raw = String(elements.searchModalInput.value || '').trim();
            if (raw.startsWith('@')) {
                return;
            }
            if (elements.searchModalApplyBtn) elements.searchModalApplyBtn.click();
        }
    });
}
if (elements.chatFilterInput) {
    elements.chatFilterInput.addEventListener('input', function (e) {
        state.chatFilter = e.target.value;
        renderChats();
    });
}
if (elements.groupInfoBtn) {
    elements.groupInfoBtn.addEventListener('click', function () {
        void openGroupInfoModal();
    });
}
if (elements.groupInfoCloseBtn) {
    elements.groupInfoCloseBtn.addEventListener('click', function () {
        hideGroupInfoModal();
    });
}
if (elements.groupInfoAddMemberBtn) {
    elements.groupInfoAddMemberBtn.addEventListener('click', function () {
        void addGroupMemberFromInvite();
    });
}
if (elements.groupInfoInviteInput) {
    elements.groupInfoInviteInput.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') {
            e.preventDefault();
            void addGroupMemberFromInvite();
        }
    });
}
if (elements.groupInfoEditBtn) {
    elements.groupInfoEditBtn.addEventListener('click', function () {
        const chat = state.chats.find(function (c) { return normalizeId(c.id) === normalizeId(groupInfoUi.chatId); });
        if (!chat) return;
        groupInfoUi.editing = true;
        if (elements.groupInfoTitleEdit) elements.groupInfoTitleEdit.value = chat.title || '';
        if (elements.groupInfoDescEdit) elements.groupInfoDescEdit.value = chat.description || chat.Description || '';
        renderGroupInfoModal(chat);
    });
}
if (elements.groupInfoCancelEditBtn) {
    elements.groupInfoCancelEditBtn.addEventListener('click', function () {
        groupInfoUi.editing = false;
        const chat = state.chats.find(function (c) { return normalizeId(c.id) === normalizeId(groupInfoUi.chatId); });
        if (chat) renderGroupInfoModal(chat);
    });
}
if (elements.groupInfoSaveBtn) {
    elements.groupInfoSaveBtn.addEventListener('click', async function () {
        const title = String(elements.groupInfoTitleEdit && elements.groupInfoTitleEdit.value || '').trim();
        const desc = String(elements.groupInfoDescEdit && elements.groupInfoDescEdit.value || '').trim();
        showGroupInfoError('');
        try {
            await api(`/chats/${groupInfoUi.chatId}`, 'PATCH', { title: title || null, description: desc || null });
            groupInfoUi.editing = false;
            await loadChats();
            const chat = state.chats.find(function (c) { return normalizeId(c.id) === normalizeId(groupInfoUi.chatId); });
            if (elements.chatTitle && chat) elements.chatTitle.textContent = chat.title || elements.chatTitle.textContent;
            if (elements.chatSubtitle && chat) elements.chatSubtitle.textContent = getChatSubtitle(chat);
            if (chat) renderGroupInfoModal(chat);
            renderChats();
        } catch (error) {
            const msg = (error && error.message) ? String(error.message).toLowerCase() : 'update failed';
            showGroupInfoError(msg);
        }
    });
}
if (elements.groupInfoLeaveBtn) {
    elements.groupInfoLeaveBtn.addEventListener('click', async function () {
        showGroupInfoError('');
        try {
            await api(`/chats/${groupInfoUi.chatId}/members/${state.userId}`, 'DELETE');
            const leftId = groupInfoUi.chatId;
            hideGroupInfoModal();
            if (normalizeId(state.selectedChatId) === normalizeId(leftId)) {
                state.selectedChatId = null;
                state.messages = [];
                state.messageSearchQuery = '';
                elements.chatTitle.textContent = 'Select a chat';
                if (elements.chatSubtitle) elements.chatSubtitle.textContent = '';
                elements.messageInput.disabled = true;
                if (elements.sendBtn) elements.sendBtn.disabled = true;
                if (elements.attachBtn) elements.attachBtn.disabled = true;
                if (elements.groupInfoBtn) elements.groupInfoBtn.classList.add('hidden');
                updateSafetyButtonVisibility(null);
                renderMessages();
            }
            await loadChats();
        } catch (error) {
            const msg = (error && error.message) ? String(error.message).toLowerCase() : 'leave failed';
            showGroupInfoError(msg);
        }
    });
}
if (elements.groupInfoMembers) {
    elements.groupInfoMembers.addEventListener('click', async function (e) {
        const btn = e.target.closest('button[data-action]');
        if (!btn || !groupInfoUi.chatId) return;
        const action = btn.getAttribute('data-action');
        const userId = btn.getAttribute('data-user-id');
        if (!userId) return;
        showGroupInfoError('');
        try {
            if (action === 'remove') {
                await api(`/chats/${groupInfoUi.chatId}/members/${userId}`, 'DELETE');
            } else if (action === 'make-admin') {
                await api(`/chats/${groupInfoUi.chatId}/members/${userId}/role`, 'PATCH', { role: 2 });
            } else if (action === 'revoke-admin') {
                await api(`/chats/${groupInfoUi.chatId}/members/${userId}/role`, 'PATCH', { role: 3 });
            } else {
                return;
            }
            await refreshGroupInfoModalFromServer();
            renderChats();
            const chat = state.chats.find(function (c) { return normalizeId(c.id) === normalizeId(state.selectedChatId); });
            if (elements.chatSubtitle && chat) elements.chatSubtitle.textContent = getChatSubtitle(chat);
        } catch (error) {
            const msg = (error && error.message) ? String(error.message).toLowerCase() : 'request failed';
            showGroupInfoError(msg);
        }
    });
}
if (elements.emojiBtn && elements.messageInput) {
    elements.emojiBtn.addEventListener('click', function () {
        elements.messageInput.value += '😀';
        elements.messageInput.focus();
    });
}
if (elements.attachBtn && elements.fileInput) {
    elements.attachBtn.addEventListener('click', function () {
        if (elements.attachBtn.disabled) return;
        elements.fileInput.click();
    });
}
if (elements.fileInput) {
    elements.fileInput.addEventListener('change', function () {
        const f = elements.fileInput.files && elements.fileInput.files[0];
        if (f) {
            pendingAttachmentFile = f;
            updateAttachmentChipUI();
        }
    });
}
if (elements.attachmentChipClear) {
    elements.attachmentChipClear.addEventListener('click', function () {
        clearPendingAttachment();
    });
}
if (elements.messageInput) {
    elements.messageInput.addEventListener('keydown', function (e) {
        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            void sendMessage(elements.messageInput.value);
        }
    });
}
if (elements.createGroupBtn) {
    elements.createGroupBtn.addEventListener('click', function () {
        showGroupModal();
    });
}
if (elements.groupModalCreateBtn) {
    elements.groupModalCreateBtn.addEventListener('click', async function () {
        setModalStatus(elements.groupModalStatus, '', false);
        try {
            await createGroupChatFlow();
        } catch (error) {
            console.error('Group creation failed:', error);
            setModalStatus(elements.groupModalStatus, `Group creation failed: ${error.message || error}`, true);
        }
    });
}
if (elements.groupModalCancelBtn) {
    elements.groupModalCancelBtn.addEventListener('click', function () {
        hideGroupModal();
    });
}
if (elements.closeSafetyNumberBtn) {
    elements.closeSafetyNumberBtn.addEventListener('click', function () {
        hideSafetyNumberModal();
    });
}
if (elements.groupMembersInput) {
    elements.groupMembersInput.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') {
            e.preventDefault();
            if (elements.groupModalCreateBtn) elements.groupModalCreateBtn.click();
        }
    });
}
if (elements.sidebarSettingsBtn && elements.settingsModal) {
    elements.sidebarSettingsBtn.addEventListener('click', function () {
        elements.settingsModal.classList.remove('hidden');
    });
}
if (elements.closeSettingsBtn && elements.settingsModal) {
    elements.closeSettingsBtn.addEventListener('click', function () {
        elements.settingsModal.classList.add('hidden');
    });
}
if (elements.clearLocalCacheBtn) {
    elements.clearLocalCacheBtn.addEventListener('click', async function () {
        sentPlaintextByMessageId.clear();
        localStorage.removeItem(SENT_PLAINTEXT_CACHE_KEY);
        setModalStatus(elements.settingsStatus, 'Local decrypted cache cleared.', false);
    });
}
if (elements.resetEncryptionBtn) {
    elements.resetEncryptionBtn.addEventListener('click', async function () {
        const confirmed = await showConfirmDialog(
            'Reset encryption keys',
            'This regenerates your local key bundle and republishes it to the server. ' +
            'You will need to send a new message in each chat to re-establish the secure session. Continue?',
            'Reset',
            'Cancel');
        if (!confirmed) return;
        setModalStatus(elements.settingsStatus, 'Regenerating key bundle...', false);
        try {
            await window.Crypto25519.regenerateBundle(state.token, state.userId);
            sentPlaintextByMessageId.clear();
            localStorage.removeItem(SENT_PLAINTEXT_CACHE_KEY);
            setModalStatus(
                elements.settingsStatus,
                'Encryption keys regenerated. Send a new message to re-establish secure session.',
                false);
        } catch (error) {
            console.error('Reset encryption keys failed:', error);
            setModalStatus(
                elements.settingsStatus,
                'Reset failed: ' + (error.message || error),
                true);
        }
    });
}
if (elements.ctxDeleteChatBtn) {
    elements.ctxDeleteChatBtn.addEventListener('click', async function () {
        const chatId = state.selectedContextChatId;
        hideChatContextMenu();
        if (!chatId) return;
        try {
            await deleteChat(chatId);
        } catch (error) {
            console.error('Delete chat failed:', error);
            await showInfoDialog('Delete chat', `Delete failed: ${error.message || error}`);
        }
    });
}
if (elements.ctxClearChatBtn) {
    elements.ctxClearChatBtn.addEventListener('click', async function () {
        const chatId = state.selectedContextChatId;
        hideChatContextMenu();
        if (!chatId) return;
        try {
            await clearChatMessages(chatId);
        } catch (error) {
            console.error('Clear chat failed:', error);
            await showInfoDialog('Clear messages', `Clear failed: ${error.message || error}`);
        }
    });
}
if (elements.dialogOkBtn) {
    elements.dialogOkBtn.addEventListener('click', function () {
        resolveDialog(true);
    });
}
if (elements.dialogCancelBtn) {
    elements.dialogCancelBtn.addEventListener('click', function () {
        resolveDialog(false);
    });
}
document.addEventListener('click', function (e) {
    if (elements.chatContextMenu && !elements.chatContextMenu.classList.contains('hidden')) {
        if (!elements.chatContextMenu.contains(e.target)) {
            hideChatContextMenu();
        }
    }
    if (e.target === elements.settingsModal) hideModal(elements.settingsModal);
    if (e.target === elements.searchModal) hideSearchModal();
    if (e.target === elements.groupModal) hideGroupModal();
    if (e.target === elements.groupInfoModal) hideGroupInfoModal();
    if (e.target === elements.safetyNumberModal) hideSafetyNumberModal();
    if (e.target === elements.dialogModal) resolveDialog(false);
});
document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') {
        hideChatContextMenu();
        hideModal(elements.settingsModal);
        hideSearchModal();
        hideGroupModal();
        hideGroupInfoModal();
        hideSafetyNumberModal();
        resolveDialog(false);
    }
});

(async () => {
    loadSentPlaintextCache();
    if (typeof window.sodium !== 'undefined') {
        await window.sodium.ready;
    } else {
        console.warn('libsodium global is unavailable; crypto operations will fail until it loads.');
    }
    await checkSession();
})();