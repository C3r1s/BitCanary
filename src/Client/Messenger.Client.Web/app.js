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
    messages: []
};

const elements = {
    loginScreen: document.getElementById('login-screen'),
    mainScreen: document.getElementById('main-screen'),
    loginForm: document.getElementById('login-form'),
    loginError: document.getElementById('login-error'),
    usernameInput: document.getElementById('username'),
    passwordInput: document.getElementById('password'),
    currentUser: document.getElementById('current-user'),
    logoutBtn: document.getElementById('logout-btn'),
    chatsContainer: document.getElementById('chats'),
    chatTitle: document.getElementById('chat-title'),
    messagesContainer: document.getElementById('messages'),
    messageForm: document.getElementById('message-form'),
    messageInput: document.getElementById('message-input')
};

function getRecipientUserId(chat) {
    if (!chat) return null;

    const chatType = chat.chatType !== undefined ? chat.chatType : chat.type;
    if (chatType !== undefined && chatType !== 0 && chatType !== 'Direct') {
        return null;
    }

    if (chat.members && Array.isArray(chat.members)) {
        const other = chat.members.find(function (m) { return m.userId !== state.userId; });
        return other ? other.userId : null;
    }
    if (chat.otherUserId) return chat.otherUserId;
    if (chat.peerUserId) return chat.peerUserId;
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
    
    return response.json();
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
        state.token = response.accessToken;
        state.userId = response.userId;
        state.username = username;
        
        localStorage.setItem('bitcanary_token', state.token);
        localStorage.setItem('bitcanary_user', username);
        localStorage.setItem('bitcanary_userid', state.userId);
        
        showScreen(elements.mainScreen);
        elements.currentUser.textContent = `@${username}`;

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
    state = { token: null, userId: null, username: null, chats: [], selectedChatId: null, messages: [] };
    localStorage.removeItem('bitcanary_token');
    localStorage.removeItem('bitcanary_user');
    localStorage.removeItem('bitcanary_userid');
    showScreen(elements.loginScreen);
}

async function loadChats() {
    try {
        state.chats = await api('/chats');
        renderChats();
    } catch (error) {
        console.error('Failed to load chats:', error);
    }
}

function renderChats() {
    elements.chatsContainer.innerHTML = state.chats.map(chat => `
        <div class="chat-item ${chat.id === state.selectedChatId ? 'active' : ''}" data-id="${chat.id}">
            <div class="title">${escapeHtml(chat.title)}</div>
            <div class="preview">${escapeHtml(chat.lastMessage?.encryptedPayload || '')}</div>
            <div class="time">${formatTime(chat.lastMessage?.createdAtUtc)}</div>
        </div>
    `).join('');
    
    elements.chatsContainer.querySelectorAll('.chat-item').forEach(item => {
        item.addEventListener('click', () => selectChat(item.dataset.id));
    });
}

async function selectChat(chatId) {
    state.selectedChatId = chatId;
    renderChats();
    
    const chat = state.chats.find(c => c.id === chatId);
    elements.chatTitle.textContent = chat?.title || 'Select a chat';
    
    elements.messageInput.disabled = false;
    elements.messageForm.querySelector('button').disabled = false;
    
    await loadMessages();
}

async function loadMessages() {
    if (!state.selectedChatId) return;
    
    try {
        state.messages = await api(`/chats/${state.selectedChatId}/messages`);
        await decryptAllMessages();
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

async function onMessageReceived(message) {
    if (message.chatId === state.selectedChatId) {
        state.messages.push(message);
        await decryptAllMessages();
        renderMessages();
    }
    loadChats();
}

async function decryptAllMessages() {
    for (let i = 0; i < state.messages.length; i++) {
        const msg = state.messages[i];
        if (msg._plaintextCached !== undefined) continue;
        if (msg.senderId === state.userId && msg.encryptionAlgorithm === 'signal-protocol-v1') {
            msg._plaintextCached = '[sent]';
            continue;
        }
        msg._plaintextCached = await window.Crypto25519.decryptMessage(state.userId, msg);
    }
}

function renderMessages() {
    elements.messagesContainer.innerHTML = state.messages.map(msg => {
        const text = msg._plaintextCached !== undefined ? msg._plaintextCached : '[decrypting...]';
        const isOutgoing = msg.senderId === state.userId;
        return `
            <div class="message ${isOutgoing ? 'outgoing' : 'incoming'}">
                ${!isOutgoing ? `<div class="sender">@${escapeHtml(msg.senderDisplayName)}</div>` : ''}
                <div class="text">${escapeHtml(text)}</div>
                <div class="time">${formatTime(msg.createdAtUtc)}</div>
            </div>
        `;
    }).join('');
    
    elements.messagesContainer.scrollTop = elements.messagesContainer.scrollHeight;
}

async function sendMessage(text) {
    if (!text.trim() || !state.selectedChatId) return;

    const chat = state.chats.find(function (c) { return c.id === state.selectedChatId; });
    const recipientUserId = getRecipientUserId(chat);
    let payload;

    if (recipientUserId) {
        try {
            const enc = await window.Crypto25519.encryptText(
                state.token, state.userId, recipientUserId, text.trim());
            payload = {
                chatId: state.selectedChatId,
                clientMessageId: crypto.randomUUID(),
                kind: 1,
                encryptedPayload: enc.encryptedPayload,
                encryptionAlgorithm: enc.encryptionAlgorithm,
                keyEnvelope: enc.keyEnvelope,
                metadataJson: enc.metadataJson,
                protocolVersion: 1
            };
        } catch (err) {
            console.error('Encryption failed, falling back to plaintext:', err);
            payload = {
                chatId: state.selectedChatId,
                clientMessageId: crypto.randomUUID(),
                kind: 1,
                encryptedPayload: text.trim(),
                encryptionAlgorithm: 'plaintext',
                keyEnvelope: '',
                metadataJson: null,
                protocolVersion: 2
            };
        }
    } else {
        console.warn('Group chat encryption not supported in web client yet (Phase 26).');
        return;
    }

    try {
        await api('/messages', 'POST', payload);
        elements.messageInput.value = '';
        await loadMessages();
        for (let i = 0; i < state.messages.length; i++) {
            const msg = state.messages[i];
            if (msg.senderId === state.userId && msg._plaintextCached === undefined &&
                msg.encryptionAlgorithm === 'signal-protocol-v1') {
                msg._plaintextCached = '[sent]';
            }
        }
        renderMessages();
        await loadChats();
    } catch (error) {
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
        elements.currentUser.textContent = `@${username}`;
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

elements.logoutBtn.addEventListener('click', () => logout());

elements.messageForm.addEventListener('submit', async (e) => {
    e.preventDefault();
    await sendMessage(elements.messageInput.value);
});

(async () => {
    await sodium.ready;
    await checkSession();
})();