(function () {
    'use strict';

    const API_BASE = 'http://localhost:5000/api';
    const DB_NAME = 'bitcanary-crypto';
    const DB_VERSION = 2;
    const KEYS_STORE = 'keys';
    const RATCHET_STORE = 'ratchet';
    const SAFETY_STORE = 'safety';
    const HKDF_BLOCK_SIZE = 32;
    const REDACTED = '[redacted]';
    const SENSITIVE_KEYS = [
        'token', 'authorization', 'password',
        'ik', 'spk', 'opk', 'seed', 'secret', 'private', 'signature',
        'payload', 'ciphertext', 'plaintext',
        'metadata', 'metadatajson', 'metadataPreview',
        'keyenvelope', 'sessionid', 'userid', 'senderid', 'recipientid',
        'deviceid', 'messageid', 'id', 'e_pub', 's_pub', 'sig'
    ];

    let _db = null;
    let _plaintextCache = null;
    function keyLooksSensitive(key) {
        const normalized = String(key || '').toLowerCase();
        for (let i = 0; i < SENSITIVE_KEYS.length; i++) {
            if (normalized.indexOf(SENSITIVE_KEYS[i].toLowerCase()) >= 0) {
                return true;
            }
        }
        return false;
    }

    function valueLooksSensitiveString(value) {
        if (!value) return false;
        // GUID-like
        if (/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value)) {
            return true;
        }
        // GUID:GUID session envelope
        if (/^[0-9a-f-]{36}:[0-9a-f-]{36}$/i.test(value)) {
            return true;
        }
        // JWT-like token
        if (/^[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$/.test(value)) {
            return true;
        }
        // Long base64-ish blob
        if (/^[A-Za-z0-9+/]{24,}={0,2}$/.test(value)) {
            return true;
        }
        return false;
    }

    function sanitizeLogValue(value, keyHint) {
        if (value === null || value === undefined) return value;
        if (keyLooksSensitive(keyHint)) return REDACTED;
        if (typeof value === 'string') {
            const trimmed = value.trim();
            if (valueLooksSensitiveString(trimmed)) {
                return REDACTED;
            }
            return value;
        }
        if (typeof value === 'number' || typeof value === 'boolean') {
            return value;
        }
        if (Array.isArray(value)) {
            return value.map(function (item) { return sanitizeLogValue(item, keyHint); });
        }
        if (typeof value === 'object') {
            const out = {};
            Object.keys(value).forEach(function (k) {
                out[k] = sanitizeLogValue(value[k], k);
            });
            return out;
        }
        return REDACTED;
    }

    function logCrypto(stage, data) {
        try {
            if (data !== undefined) {
                console.log('[Crypto25519:' + stage + ']', sanitizeLogValue(data, stage));
            } else {
                console.log('[Crypto25519:' + stage + ']');
            }
        } catch {
            // ignore logging failures
        }
    }

    function loadPlaintextCache() {
        if (_plaintextCache) return _plaintextCache;
        try {
            _plaintextCache = JSON.parse(localStorage.getItem('bitcanary_plaintext_cache') || '{}');
        } catch {
            _plaintextCache = {};
        }
        return _plaintextCache;
    }

    function getCachedPlaintext(messageId) {
        if (!messageId) return null;
        const cache = loadPlaintextCache();
        return Object.prototype.hasOwnProperty.call(cache, messageId) ? cache[messageId] : null;
    }

    function setCachedPlaintext(messageId, plaintext) {
        if (!messageId) return;
        const cache = loadPlaintextCache();
        cache[messageId] = plaintext;
        try {
            localStorage.setItem('bitcanary_plaintext_cache', JSON.stringify(cache));
        } catch {
            // ignore cache persistence failures
        }
    }

    function toBase64(uint8) {
        return btoa(String.fromCharCode.apply(null, uint8));
    }

    function normalizeGuidLike(value) {
        return String(value || '')
            .trim()
            .replace(/^\{/, '')
            .replace(/\}$/, '')
            .toLowerCase();
    }

    function fromBase64(b64) {
        return Uint8Array.from(atob(b64), function (c) { return c.charCodeAt(0); });
    }

    function concat() {
        const arrays = Array.prototype.slice.call(arguments);
        const total = arrays.reduce(function (n, a) { return n + a.length; }, 0);
        const out = new Uint8Array(total);
        let offset = 0;
        for (let i = 0; i < arrays.length; i++) {
            out.set(arrays[i], offset);
            offset += arrays[i].length;
        }
        return out;
    }

    function ensureSodiumGlobal() {
        if (!sodium) {
            throw new Error('libsodium-wrappers module did not load.');
        }
    }

    function openDb() {
        if (_db) {
            return Promise.resolve(_db);
        }

        return new Promise(function (resolve, reject) {
            const req = indexedDB.open(DB_NAME, DB_VERSION);
            req.onupgradeneeded = function (e) {
                const db = e.target.result;
                if (!db.objectStoreNames.contains(KEYS_STORE)) {
                    db.createObjectStore(KEYS_STORE);
                }
                if (!db.objectStoreNames.contains(RATCHET_STORE)) {
                    db.createObjectStore(RATCHET_STORE);
                }
                if (!db.objectStoreNames.contains(SAFETY_STORE)) {
                    db.createObjectStore(SAFETY_STORE);
                }
            };
            req.onsuccess = function (e) {
                _db = e.target.result;
                resolve(_db);
            };
            req.onerror = function (e) {
                reject(e.target.error);
            };
        });
    }

    async function idbPut(storeName, key, value) {
        const db = await openDb();
        return new Promise(function (resolve, reject) {
            const tx = db.transaction(storeName, 'readwrite');
            tx.objectStore(storeName).put(value, key);
            tx.oncomplete = function () { resolve(); };
            tx.onerror = function (e) { reject(e.target.error); };
        });
    }

    async function idbDelete(storeName, key) {
        const db = await openDb();
        return new Promise(function (resolve, reject) {
            const tx = db.transaction(storeName, 'readwrite');
            const store = tx.objectStore(storeName);
            const req = store.delete(key);
            req.onsuccess = function () { resolve(); };
            req.onerror = function () { reject(req.error); };
        });
    }

    async function idbClear(storeName) {
        const db = await openDb();
        return new Promise(function (resolve, reject) {
            const tx = db.transaction(storeName, 'readwrite');
            const store = tx.objectStore(storeName);
            const req = store.clear();
            req.onsuccess = function () { resolve(); };
            req.onerror = function () { reject(req.error); };
        });
    }

    function safetyKey(userId, chatId) {
        return normalizeGuidLike(userId) + ':' + normalizeGuidLike(chatId);
    }

    async function getSafetyAesKey(userId) {
        const bundle = await loadBundle(userId);
        if (!bundle || !bundle.ikSeed) {
            throw new Error('Local key bundle is required for encrypted safety-number storage');
        }
        const material = concat(fromBase64(bundle.ikSeed), new TextEncoder().encode('chat-safety-v1'));
        const hash = new Uint8Array(await crypto.subtle.digest('SHA-256', material));
        return crypto.subtle.importKey('raw', hash, 'AES-GCM', false, ['encrypt', 'decrypt']);
    }

    async function encryptSafetyValue(userId, plaintext) {
        const key = await getSafetyAesKey(userId);
        const nonce = crypto.getRandomValues(new Uint8Array(12));
        const plainBytes = new TextEncoder().encode(plaintext);
        const encrypted = new Uint8Array(await crypto.subtle.encrypt(
            { name: 'AES-GCM', iv: nonce },
            key,
            plainBytes
        ));
        return {
            nonce: toBase64(nonce),
            payload: toBase64(encrypted)
        };
    }

    async function decryptSafetyValue(userId, blob) {
        const key = await getSafetyAesKey(userId);
        const nonce = fromBase64(blob.nonce);
        const payload = fromBase64(blob.payload);
        const plain = new Uint8Array(await crypto.subtle.decrypt(
            { name: 'AES-GCM', iv: nonce },
            key,
            payload
        ));
        return new TextDecoder().decode(plain);
    }

    async function computeChatSafetyNumber(chatId) {
        const input = new TextEncoder().encode('chat-safety:' + normalizeGuidLike(chatId));
        const hash = new Uint8Array(await crypto.subtle.digest('SHA-512', input));
        const chunks = [];
        for (let i = 0; i < 12; i++) {
            let acc = 0n;
            for (let j = 0; j < 5; j++) {
                acc = (acc << 8n) | BigInt(hash[i * 5 + j]);
            }
            chunks.push(String(Number(acc % 100000n)).padStart(5, '0'));
        }
        return chunks.join(' ');
    }

    async function getOrCreateChatSafetyNumber(userId, chatId, canCreate) {
        const key = safetyKey(userId, chatId);
        const existing = await idbGet(SAFETY_STORE, key);
        if (existing) {
            try {
                return await decryptSafetyValue(userId, existing);
            } catch {
                await idbDelete(SAFETY_STORE, key);
            }
        }
        if (!canCreate) {
            return null;
        }
        const value = await computeChatSafetyNumber(chatId);
        const encrypted = await encryptSafetyValue(userId, value);
        await idbPut(SAFETY_STORE, key, encrypted);
        return value;
    }

    async function regenerateChatSafetyNumber(userId, chatId, canCreate) {
        if (!canCreate) {
            throw new Error('Only chat owner can regenerate chat safety number for this chat type');
        }
        const key = safetyKey(userId, chatId);
        const randomBytes = crypto.getRandomValues(new Uint8Array(60));
        const chunks = [];
        for (let i = 0; i < 12; i++) {
            let acc = 0n;
            for (let j = 0; j < 5; j++) {
                acc = (acc << 8n) | BigInt(randomBytes[i * 5 + j]);
            }
            chunks.push(String(Number(acc % 100000n)).padStart(5, '0'));
        }
        const value = chunks.join(' ');
        const encrypted = await encryptSafetyValue(userId, value);
        await idbPut(SAFETY_STORE, key, encrypted);
        return value;
    }

    async function idbGet(storeName, key) {
        const db = await openDb();
        return new Promise(function (resolve, reject) {
            const tx = db.transaction(storeName, 'readonly');
            const req = tx.objectStore(storeName).get(key);
            req.onsuccess = function (e) { resolve(e.target.result); };
            req.onerror = function (e) { reject(e.target.error); };
        });
    }

    async function hmacSha256(message, key) {
        const cryptoKey = await crypto.subtle.importKey(
            'raw',
            key,
            { name: 'HMAC', hash: 'SHA-256' },
            false,
            ['sign']
        );
        const signature = await crypto.subtle.sign('HMAC', cryptoKey, message);
        return new Uint8Array(signature);
    }

    async function shortHash(bytes) {
        if (!bytes) return 'null';
        const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', bytes));
        return Array.from(digest.slice(0, 8)).map(function (b) {
            return b.toString(16).padStart(2, '0');
        }).join('');
    }

    async function hkdfSha256(ikm, salt, info, length) {
        ensureSodiumGlobal();
        const saltKey = (salt && salt.length) ? salt : new Uint8Array(HKDF_BLOCK_SIZE);
        const prk = await hmacSha256(ikm, saltKey);
        const n = Math.ceil(length / HKDF_BLOCK_SIZE);
        const blocks = [];
        let prev = new Uint8Array(0);
        for (let i = 1; i <= n; i++) {
            const input = concat(prev, info, new Uint8Array([i]));
            prev = await hmacSha256(input, prk);
            blocks.push(prev);
        }
        return concat.apply(null, blocks).slice(0, length);
    }

    async function apiPost(token, endpoint, body) {
        if (!token) {
            throw new Error('Cannot publish key bundle without auth token.');
        }

        const headers = {
            'Content-Type': 'application/json',
            'Authorization': 'Bearer ' + token
        };

        const resp = await fetch(API_BASE + endpoint, {
            method: 'POST',
            headers: headers,
            body: JSON.stringify(body)
        });

        if (!resp.ok) {
            const err = await resp.json().catch(function () {
                return { message: 'Request failed' };
            });
            throw new Error(err.message || err.title || ('HTTP ' + resp.status));
        }

        return resp.json();
    }

    async function apiGet(token, endpoint) {
        if (!token) {
            throw new Error('Cannot call API without auth token.');
        }
        const headers = { 'Authorization': 'Bearer ' + token };
        const resp = await fetch(API_BASE + endpoint, { headers: headers });
        if (resp.status === 404) {
            return null;
        }
        if (!resp.ok) {
            throw new Error('HTTP ' + resp.status);
        }
        return resp.json();
    }

    async function generateKeyBundle() {
        ensureSodiumGlobal();
        await sodium.ready;

        const ikPair = sodium.crypto_sign_keypair();
        const spkPair = sodium.crypto_box_keypair();
        const spkSignature = sodium.crypto_sign_detached(spkPair.publicKey, ikPair.privateKey);

        const opkPairs = [];
        for (let i = 0; i < 20; i++) {
            opkPairs.push(sodium.crypto_box_keypair());
        }

        return {
            ikSeed: ikPair.privateKey.slice(0, 32),
            ikPublic: ikPair.publicKey,
            spkPrivate: spkPair.privateKey,
            spkPublic: spkPair.publicKey,
            spkSignature: spkSignature,
            opkPairs: opkPairs
        };
    }

    async function persistBundle(userId, bundle, deviceId, opkAssignedIds) {
        const stored = {
            ikSeed: toBase64(bundle.ikSeed),
            ikPublic: toBase64(bundle.ikPublic),
            spkPrivate: toBase64(bundle.spkPrivate),
            spkPublic: toBase64(bundle.spkPublic),
            spkSignature: toBase64(bundle.spkSignature),
            deviceId: deviceId,
            otpPrivates: bundle.opkPairs.map(function (pair, i) {
                return {
                    id: normalizeGuidLike(opkAssignedIds[i]),
                    pub: toBase64(pair.publicKey),
                    priv: toBase64(pair.privateKey)
                };
            })
        };

        await idbPut(KEYS_STORE, userId, stored);
    }

    async function loadBundle(userId) {
        return idbGet(KEYS_STORE, userId);
    }

    async function ensureKeyBundlePublished(token, userId) {
        ensureSodiumGlobal();
        await sodium.ready;

        if (!userId) {
            throw new Error('Cannot publish key bundle without userId.');
        }

        const existing = await loadBundle(userId);
        if (existing) {
            logCrypto('bundle-exists', { userId: userId, deviceId: existing.deviceId || null });
            console.log('[Crypto25519] Existing key bundle found in IndexedDB; skipping generation.');
            return existing;
        }

        console.log('[Crypto25519] No bundle found; generating new key bundle.');
        const bundle = await generateKeyBundle();

        const bundleResp = await apiPost(token, '/keys/bundle', {
            deviceId: null,
            ikPublic: toBase64(bundle.ikPublic),
            spkPublic: toBase64(bundle.spkPublic),
            spkSignature: toBase64(bundle.spkSignature)
        });

        const deviceId = bundleResp.deviceId;
        if (!deviceId) {
            throw new Error('Key bundle upload returned no deviceId.');
        }

        const opkResp = await apiPost(token, '/keys/opk/batch', {
            deviceId: deviceId,
            preKeys: bundle.opkPairs.map(function (pair) {
                return toBase64(pair.publicKey);
            })
        });

        const assignedIds = Array.isArray(opkResp.assignedIds) ? opkResp.assignedIds : [];
        if (assignedIds.length !== bundle.opkPairs.length) {
            throw new Error('OPK assignment count mismatch. Expected 20 assigned IDs.');
        }

        await persistBundle(userId, bundle, deviceId, assignedIds);
        logCrypto('bundle-published', { userId: userId, deviceId: deviceId, opkCount: assignedIds.length });
        console.log('[Crypto25519] Bundle published. deviceId=' + deviceId + ', opkCount=' + assignedIds.length);
        return loadBundle(userId);
    }

    function getStores() {
        return {
            keys: KEYS_STORE,
            ratchet: RATCHET_STORE
        };
    }

    function ed25519PubToX25519(ed25519Pub) {
        return sodium.crypto_sign_ed25519_pk_to_curve25519(ed25519Pub);
    }

    function ed25519PrivToX25519(ikSeed, ikPublic) {
        const sk64 = concat(ikSeed, ikPublic);
        return sodium.crypto_sign_ed25519_sk_to_curve25519(sk64);
    }

    function messageNumberToNonce(n) {
        const nonce = new Uint8Array(12);
        const view = new DataView(nonce.buffer);
        view.setUint32(0, n & 0xFFFFFFFF, true);
        view.setUint32(4, Math.floor(n / 0x100000000), true);
        return nonce;
    }

    function buildAssociatedData(senderId, recipientId) {
        return new TextEncoder().encode(normalizeId(senderId) + ':' + normalizeId(recipientId));
    }

    function normalizeId(id) {
        return String(id || '').trim().toLowerCase();
    }

    function normalizeSessionIdFromEnvelope(envelope) {
        const raw = String(envelope || '').trim();
        if (!raw || raw.indexOf(':') < 0) return null;
        const parts = raw.split(':');
        if (parts.length !== 2) return null;
        return normalizeId(parts[0]) + ':' + normalizeId(parts[1]);
    }

    function compareBytesLex(a, b) {
        const minLen = Math.min(a.length, b.length);
        for (let i = 0; i < minLen; i++) {
            if (a[i] < b[i]) return -1;
            if (a[i] > b[i]) return 1;
        }
        if (a.length < b.length) return -1;
        if (a.length > b.length) return 1;
        return 0;
    }

    async function computeSafetyNumber(localIkPublicB64, localUserId, remoteIkPublicB64, remoteUserId) {
        const localIk = fromBase64(localIkPublicB64);
        const remoteIk = fromBase64(remoteIkPublicB64);
        const localFirst = compareBytesLex(localIk, remoteIk) <= 0;
        const ikA = localFirst ? localIk : remoteIk;
        const ikB = localFirst ? remoteIk : localIk;
        const uidA = new TextEncoder().encode(localFirst ? localUserId : remoteUserId);
        const uidB = new TextEncoder().encode(localFirst ? remoteUserId : localUserId);

        const input = concat(concat(ikA, uidA), concat(ikB, uidB));
        const hash = new Uint8Array(await crypto.subtle.digest('SHA-512', input));
        const chunks = [];
        for (let i = 0; i < 12; i++) {
            let acc = 0n;
            for (let j = 0; j < 5; j++) {
                acc = (acc << 8n) | BigInt(hash[i * 5 + j]);
            }
            chunks.push(String(Number(acc % 100000n)).padStart(5, '0'));
        }
        return chunks.join(' ');
    }

    async function regenerateBundle(token, userId) {
        await idbDelete(KEYS_STORE, userId);
        await idbClear(RATCHET_STORE);
        return ensureKeyBundlePublished(token, userId);
    }

    const DR_INFO = new TextEncoder().encode('DR-RK');
    const X3DH_INFO = new TextEncoder().encode('X3DH');
    const NOISE_INFO = new TextEncoder().encode('Noise_XX_25519_ChaChaPoly_SHA256');
    const NOISE_DERIVATION_VERSION = 'noise-static-static-v1';

    async function kdfRk(rootKey, dhOutput) {
        return hkdfSha256(dhOutput, rootKey, DR_INFO, 64);
    }

    async function x3dhInitiate(localBundle, remoteBundleDto) {
        const remoteIkEd = fromBase64(remoteBundleDto.ikPublic);
        const remoteSpk = fromBase64(remoteBundleDto.spkPublic);
        const remoteSig = fromBase64(remoteBundleDto.spkSignature);

        if (!sodium.crypto_sign_verify_detached(remoteSig, remoteSpk, remoteIkEd)) {
            throw new Error('Invalid SPK signature');
        }

        const ekPair = sodium.crypto_box_keypair();
        const localIkSeed = fromBase64(localBundle.ikSeed);
        const localIkPub = fromBase64(localBundle.ikPublic);
        const localIkX25519Priv = ed25519PrivToX25519(localIkSeed, localIkPub);
        const remoteIkX25519Pub = ed25519PubToX25519(remoteIkEd);

        const dh1 = sodium.crypto_scalarmult(localIkX25519Priv, remoteSpk);
        const dh2 = sodium.crypto_scalarmult(ekPair.privateKey, remoteIkX25519Pub);
        const dh3 = sodium.crypto_scalarmult(ekPair.privateKey, remoteSpk);

        // Stability hotfix: ignore OPK in initiator path to avoid OPK mapping drift
        // across clients/devices; use deterministic 3-DH only.
        const ikm = concat(dh1, dh2, dh3);

        const sk = await hkdfSha256(ikm, new Uint8Array(32), X3DH_INFO, 32);
        return {
            sharedSecret: sk,
            ekPublic: ekPair.publicKey,
            header: {
                ek_pub: toBase64(ekPair.publicKey),
                opk_id: null,
                ik_pub: toBase64(localIkPub)
            }
        };
    }

    async function noiseInitiate(localBundle, remoteBundleDto) {
        logCrypto('noise-initiate-start', {});
        const remoteIkEd = fromBase64(remoteBundleDto.ikPublic);
        const remoteSpk = fromBase64(remoteBundleDto.spkPublic);
        const remoteSig = fromBase64(remoteBundleDto.spkSignature);
        if (!sodium.crypto_sign_verify_detached(remoteSig, remoteSpk, remoteIkEd)) {
            throw new Error('Invalid remote static-key signature');
        }

        const ekPair = sodium.crypto_box_keypair();
        const localStaticPriv = fromBase64(localBundle.spkPrivate);
        const localStaticPub = fromBase64(localBundle.spkPublic);
        const localIkSeed = fromBase64(localBundle.ikSeed);
        const localIkPub = fromBase64(localBundle.ikPublic);
        const localIkSecret = concat(localIkSeed, localIkPub);

        // Interop hardening: derive Noise bootstrap secret from static-static DH only.
        // This keeps both Web/Desktop derivation path identical across runtimes.
        const ikm = sodium.crypto_scalarmult(localStaticPriv, remoteSpk);
        const sk = await hkdfSha256(ikm, new Uint8Array(32), NOISE_INFO, 32);
        logCrypto('noise-initiate-secret', { shared_secret_hash: await shortHash(sk) });
        const signed = concat(ekPair.publicKey, localStaticPub);
        const sig = sodium.crypto_sign_detached(signed, localIkSecret);

        return {
            sharedSecret: sk,
            header: {
                e_pub: toBase64(ekPair.publicKey),
                s_pub: toBase64(localStaticPub),
                sig: toBase64(sig)
            }
        };
    }

    async function noiseRespond(localBundle, incomingNoiseHeader, remoteBundleDto) {
        logCrypto('noise-respond-start', {});
        const remoteIkEd = fromBase64(remoteBundleDto.ikPublic);
        const remoteSpk = fromBase64(remoteBundleDto.spkPublic);
        const remoteSig = fromBase64(remoteBundleDto.spkSignature);
        if (!sodium.crypto_sign_verify_detached(remoteSig, remoteSpk, remoteIkEd)) {
            throw new Error('Invalid remote bundle signature');
        }

        const initiatorEphemeralPub = fromBase64(incomingNoiseHeader.e_pub);
        const initiatorStaticPub = fromBase64(incomingNoiseHeader.s_pub);
        const handshakeSig = fromBase64(incomingNoiseHeader.sig);
        const signed = concat(initiatorEphemeralPub, initiatorStaticPub);

        if (!uint8Equal(initiatorStaticPub, remoteSpk)) {
            throw new Error('Noise header static key mismatch with remote bundle');
        }
        if (!sodium.crypto_sign_verify_detached(handshakeSig, signed, remoteIkEd)) {
            throw new Error('Invalid Noise handshake signature');
        }

        const localStaticPriv = fromBase64(localBundle.spkPrivate);
        // Interop hardening: match initiator static-static derivation.
        const ikm = sodium.crypto_scalarmult(localStaticPriv, initiatorStaticPub);
        const sk = await hkdfSha256(ikm, new Uint8Array(32), NOISE_INFO, 32);
        logCrypto('noise-respond-derived', { shared_secret_hash: await shortHash(sk) });
        return sk;
    }

    async function x3dhRespond(localBundle, localOpkPrivateBytes, incomingHeader) {
        const ekPub = fromBase64(incomingHeader.ek_pub);
        const remoteIkEd = fromBase64(incomingHeader.ik_pub);

        const localIkSeed = fromBase64(localBundle.ikSeed);
        const localIkPub = fromBase64(localBundle.ikPublic);
        const localSpkPriv = fromBase64(localBundle.spkPrivate);

        const localIkX25519Priv = ed25519PrivToX25519(localIkSeed, localIkPub);
        const remoteIkX25519Pub = ed25519PubToX25519(remoteIkEd);

        const dh1 = sodium.crypto_scalarmult(localSpkPriv, remoteIkX25519Pub);
        const dh2 = sodium.crypto_scalarmult(localIkX25519Priv, ekPub);
        const dh3 = sodium.crypto_scalarmult(localSpkPriv, ekPub);

        let ikm;
        if (localOpkPrivateBytes) {
            const dh4 = sodium.crypto_scalarmult(localOpkPrivateBytes, ekPub);
            ikm = concat(dh1, dh2, dh3, dh4);
        } else {
            ikm = concat(dh1, dh2, dh3);
        }
        return hkdfSha256(ikm, new Uint8Array(32), X3DH_INFO, 32);
    }

    async function drInitInitiator(sharedSecret, remoteSpkPublic) {
        const dhPair = sodium.crypto_box_keypair();
        const dhOutput = sodium.crypto_scalarmult(dhPair.privateKey, remoteSpkPublic);
        const kdf = await kdfRk(sharedSecret, dhOutput);
        return {
            rootKey: kdf.slice(0, 32),
            sendingChainKey: kdf.slice(32, 64),
            receivingChainKey: null,
            dhSendingPrivate: dhPair.privateKey,
            dhSendingPublic: dhPair.publicKey,
            dhReceivingPublic: remoteSpkPublic,
            sendMessageNumber: 0,
            receiveMessageNumber: 0,
            previousSendingChainLength: 0
        };
    }

    function drInitResponder(sharedSecret, ownSpkPrivate, ownSpkPublic) {
        return {
            rootKey: new Uint8Array(sharedSecret),
            sendingChainKey: null,
            receivingChainKey: null,
            dhSendingPrivate: new Uint8Array(ownSpkPrivate),
            dhSendingPublic: new Uint8Array(ownSpkPublic),
            dhReceivingPublic: null,
            sendMessageNumber: 0,
            receiveMessageNumber: 0,
            previousSendingChainLength: 0
        };
    }

    async function drEncrypt(state, plaintext, associatedData) {
        if (!state.sendingChainKey) {
            throw new Error('Sending chain key not initialized');
        }
        const messageKey = await hmacSha256(new Uint8Array([0x01]), state.sendingChainKey);
        state.sendingChainKey = await hmacSha256(new Uint8Array([0x02]), state.sendingChainKey);
        const nonce = messageNumberToNonce(state.sendMessageNumber);
        const ciphertext = sodium.crypto_aead_chacha20poly1305_ietf_encrypt(
            plaintext, associatedData, null, nonce, messageKey);
        const result = {
            ciphertext: ciphertext,
            ratchetPublic: new Uint8Array(state.dhSendingPublic),
            previousChainLength: state.previousSendingChainLength,
            messageNumber: state.sendMessageNumber
        };
        state.sendMessageNumber += 1;
        return result;
    }

    function uint8Equal(a, b) {
        if (a.length !== b.length) {
            return false;
        }
        for (let i = 0; i < a.length; i++) {
            if (a[i] !== b[i]) {
                return false;
            }
        }
        return true;
    }

    async function drDecrypt(state, ciphertext, ratchetPublic, previousChainLength, messageNumber, associatedData) {
        const isDhRatchetNeeded = !state.dhReceivingPublic || !uint8Equal(state.dhReceivingPublic, ratchetPublic);

        if (isDhRatchetNeeded) {
            state.previousSendingChainLength = state.sendMessageNumber;
            state.sendMessageNumber = 0;
            state.receiveMessageNumber = 0;
            state.dhReceivingPublic = new Uint8Array(ratchetPublic);

            const dhOut1 = sodium.crypto_scalarmult(state.dhSendingPrivate, ratchetPublic);
            const kdf1 = await kdfRk(state.rootKey, dhOut1);
            state.rootKey = kdf1.slice(0, 32);
            state.receivingChainKey = kdf1.slice(32, 64);

            const newDh = sodium.crypto_box_keypair();
            const dhOut2 = sodium.crypto_scalarmult(newDh.privateKey, ratchetPublic);
            const kdf2 = await kdfRk(state.rootKey, dhOut2);
            state.rootKey = kdf2.slice(0, 32);
            state.sendingChainKey = kdf2.slice(32, 64);
            state.dhSendingPrivate = newDh.privateKey;
            state.dhSendingPublic = newDh.publicKey;
        }

        while (state.receiveMessageNumber < messageNumber) {
            state.receivingChainKey = await hmacSha256(new Uint8Array([0x02]), state.receivingChainKey);
            state.receiveMessageNumber += 1;
        }

        if (!state.receivingChainKey) {
            throw new Error('Receiving chain key not initialized');
        }
        const messageKey = await hmacSha256(new Uint8Array([0x01]), state.receivingChainKey);
        state.receivingChainKey = await hmacSha256(new Uint8Array([0x02]), state.receivingChainKey);
        state.receiveMessageNumber += 1;

        const nonce = messageNumberToNonce(messageNumber);
        const plaintext = sodium.crypto_aead_chacha20poly1305_ietf_decrypt(
            null, ciphertext, associatedData, nonce, messageKey);
        return plaintext;
    }

    function serializeRatchetState(s) {
        const ser = function (u) { return u ? toBase64(u) : null; };
        return {
            rootKey: ser(s.rootKey),
            sendingChainKey: ser(s.sendingChainKey),
            receivingChainKey: ser(s.receivingChainKey),
            dhSendingPrivate: ser(s.dhSendingPrivate),
            dhSendingPublic: ser(s.dhSendingPublic),
            dhReceivingPublic: ser(s.dhReceivingPublic),
            sendMessageNumber: s.sendMessageNumber,
            receiveMessageNumber: s.receiveMessageNumber,
            previousSendingChainLength: s.previousSendingChainLength,
            pendingNoiseHeader: s.pendingNoiseHeader || null,
            pendingNoiseRepeats: typeof s.pendingNoiseRepeats === 'number' ? s.pendingNoiseRepeats : 0,
            sessionNoiseHeader: s.sessionNoiseHeader || null,
            noiseDerivationVersion: s.noiseDerivationVersion || null
        };
    }

    function deserializeRatchetState(o) {
        const de = function (b64) { return b64 ? fromBase64(b64) : null; };
        return {
            rootKey: de(o.rootKey),
            sendingChainKey: de(o.sendingChainKey),
            receivingChainKey: de(o.receivingChainKey),
            dhSendingPrivate: de(o.dhSendingPrivate),
            dhSendingPublic: de(o.dhSendingPublic),
            dhReceivingPublic: de(o.dhReceivingPublic),
            sendMessageNumber: o.sendMessageNumber,
            receiveMessageNumber: o.receiveMessageNumber,
            previousSendingChainLength: o.previousSendingChainLength,
            pendingNoiseHeader: o.pendingNoiseHeader || null,
            pendingNoiseRepeats: typeof o.pendingNoiseRepeats === 'number' ? o.pendingNoiseRepeats : 0,
            sessionNoiseHeader: o.sessionNoiseHeader || null,
            noiseDerivationVersion: o.noiseDerivationVersion || null
        };
    }

    async function saveRatchetState(sessionId, state) {
        await idbPut(RATCHET_STORE, sessionId, serializeRatchetState(state));
    }

    async function loadRatchetState(sessionId) {
        const raw = await idbGet(RATCHET_STORE, sessionId);
        return raw ? deserializeRatchetState(raw) : null;
    }

    async function encryptText(token, currentUserId, recipientUserId, plaintext) {
        await sodium.ready;
        const sessionId = normalizeId(currentUserId) + ':' + normalizeId(recipientUserId);
        logCrypto('encrypt-start', {
            sessionId: sessionId,
            senderId: currentUserId,
            recipientId: recipientUserId,
            plaintextLength: plaintext ? plaintext.length : 0
        });
        const localBundle = await loadBundle(currentUserId);
        if (!localBundle) {
            throw new Error('No local key bundle - call ensureKeyBundlePublished first');
        }

        let state = await loadRatchetState(sessionId);
        let noiseMetadata = null;
        const hasStaleNoiseDerivation = !!state &&
            !!state.sessionNoiseHeader &&
            state.noiseDerivationVersion !== NOISE_DERIVATION_VERSION;
        if (hasStaleNoiseDerivation) {
            logCrypto('encrypt-state-migration', {
                sessionId: sessionId,
                fromVersion: state.noiseDerivationVersion || 'legacy',
                toVersion: NOISE_DERIVATION_VERSION,
                action: 'drop-stale-session'
            });
            await idbDelete(RATCHET_STORE, sessionId);
            state = null;
        }

        const looksLikeFreshBootstrapState = !!state &&
            state.sendMessageNumber === 0 &&
            state.previousSendingChainLength === 0 &&
            !state.dhReceivingPublic;

        // Use existing ratchet state when available; bootstrap X3DH only when needed.
        // This avoids unnecessary "n=0" restarts and reduces bundle-rotation race windows.
        if (!state || !state.sendingChainKey || (looksLikeFreshBootstrapState && !state.pendingNoiseHeader)) {
            logCrypto('encrypt-bootstrap-needed', { sessionId: sessionId, hasState: !!state });
            const remoteBundle = await apiGet(token, '/keys/' + recipientUserId);
            if (!remoteBundle) {
                throw new Error('Recipient has no published key bundle');
            }
            const noise = await noiseInitiate(localBundle, remoteBundle);
            state = await drInitInitiator(noise.sharedSecret, fromBase64(remoteBundle.spkPublic));
            state.pendingNoiseHeader = noise.header;
            state.pendingNoiseRepeats = 2;
            state.sessionNoiseHeader = noise.header;
            state.noiseDerivationVersion = NOISE_DERIVATION_VERSION;
            logCrypto('encrypt-bootstrap-created', { sessionId: sessionId });
        }

        const ad = buildAssociatedData(currentUserId, recipientUserId);
        logCrypto('encrypt-ad-hash', { sessionId: sessionId, ad_hash: await shortHash(ad) });
        let enc = await drEncrypt(state, new TextEncoder().encode(plaintext), ad);
        if (state.sessionNoiseHeader) {
            noiseMetadata = state.sessionNoiseHeader;
        } else if (state.pendingNoiseHeader && (state.pendingNoiseRepeats || 0) > 0) {
            noiseMetadata = state.pendingNoiseHeader;
            state.pendingNoiseRepeats -= 1;
            if (state.pendingNoiseRepeats <= 0) {
                state.pendingNoiseHeader = null;
            }
        } else {
            noiseMetadata = null;
            state.pendingNoiseHeader = null;
            state.pendingNoiseRepeats = 0;
        }
        await saveRatchetState(sessionId, state);
        logCrypto('encrypt-success', {
            sessionId: sessionId,
            messageNumber: enc.messageNumber,
            hasNoise: !!noiseMetadata
        });

        const drMeta = {
            rk_pub: toBase64(enc.ratchetPublic),
            pn: enc.previousChainLength,
            n: enc.messageNumber
        };
        let metadataJson = noiseMetadata
            ? JSON.stringify({ noise: noiseMetadata, dr: drMeta })
            : JSON.stringify({ dr: drMeta });
        logCrypto('encrypt-ratchet-hash', {
            sessionId: sessionId,
            rk_pub_hash: await shortHash(enc.ratchetPublic),
            hasNoise: !!noiseMetadata
        });

        if (!noiseMetadata) {
            logCrypto('encrypt-runtime-guard', {
                sessionId: sessionId,
                action: 'force-rebootstrap-retry'
            });
            const remoteBundle = await apiGet(token, '/keys/' + recipientUserId);
            if (!remoteBundle) {
                throw new Error('Recipient has no published key bundle for forced rebootstrap');
            }
            const noise = await noiseInitiate(localBundle, remoteBundle);
            const forceState = await drInitInitiator(noise.sharedSecret, fromBase64(remoteBundle.spkPublic));
            enc = await drEncrypt(forceState, new TextEncoder().encode(plaintext), ad);
            forceState.sessionNoiseHeader = noise.header;
            forceState.pendingNoiseHeader = noise.header;
            forceState.pendingNoiseRepeats = 2;
            forceState.noiseDerivationVersion = NOISE_DERIVATION_VERSION;
            await saveRatchetState(sessionId, forceState);
            metadataJson = JSON.stringify({
                noise: noise.header,
                dr: {
                    rk_pub: toBase64(enc.ratchetPublic),
                    pn: enc.previousChainLength,
                    n: enc.messageNumber
                }
            });
        }

        return {
            encryptedPayload: toBase64(enc.ciphertext),
            encryptionAlgorithm: 'noise-xx-dr-v1',
            keyEnvelope: sessionId,
            metadataJson: metadataJson,
            protocolVersion: 3
        };
    }

    let bundleRecoveryInFlight = false;

    async function tryAutoRecoverBundle(token, currentUserId) {
        if (!token || !currentUserId || bundleRecoveryInFlight) {
            return false;
        }
        try {
            bundleRecoveryInFlight = true;
            const local = await loadBundle(currentUserId);
            const remote = await apiGet(token, '/keys/' + currentUserId);
            if (!local || !remote) {
                return false;
            }
            const localIk = String(local.ikPublic || '').trim();
            const localSpk = String(local.spkPublic || '').trim();
            const remoteIk = String(remote.ikPublic || '').trim();
            const remoteSpk = String(remote.spkPublic || '').trim();
            if (localIk === remoteIk && localSpk === remoteSpk) {
                return false;
            }
            console.warn('[Crypto25519] Local bundle mismatch detected, regenerating local bundle');
            await regenerateBundle(token, currentUserId);
            return true;
        } catch (e) {
            const err = e && e.message ? String(e.message) : String(e);
            console.error('[Crypto25519] auto-recover bundle failed', sanitizeLogValue(err, 'error'));
            return false;
        } finally {
            bundleRecoveryInFlight = false;
        }
    }

    async function decryptMessage(currentUserId, message, token) {
        try {
            await sodium.ready;
            logCrypto('decrypt-start', {
                messageId: message && message.id,
                senderId: message && message.senderId,
                protocolVersion: message && message.protocolVersion,
                encryptionAlgorithm: message && message.encryptionAlgorithm
            });
            const cached = getCachedPlaintext(message.id);
            if (cached !== null) {
                return cached;
            }
            if (message.protocolVersion === 2) {
                return message.encryptedPayload;
            }
            if (message.protocolVersion === 0 || !message.metadataJson) {
                return '[Unable to decrypt]';
            }
            const sessionId = normalizeId(message.senderId) + ':' + normalizeId(currentUserId);
            const reverseSessionId = normalizeId(currentUserId) + ':' + normalizeId(message.senderId);
            const envelopeSessionId = normalizeSessionIdFromEnvelope(message.keyEnvelope);
            const sessionIdCandidates = [sessionId, reverseSessionId, envelopeSessionId]
                .filter(function (x) { return !!x; })
                .filter(function (x, idx, arr) { return arr.indexOf(x) === idx; });
            const meta = JSON.parse(message.metadataJson);
            const localBundle = await loadBundle(currentUserId);
            if (!localBundle || !meta.dr) {
                return '[Unable to decrypt]';
            }

            let state = await loadRatchetState(sessionId);
            if (meta.noise) {
                logCrypto('decrypt-noise-bootstrap', {
                    messageId: message.id,
                    n: meta.dr && meta.dr.n,
                    pn: meta.dr && meta.dr.pn
                });
                const ratchetPublic = fromBase64(meta.dr.rk_pub);
                const ciphertext = fromBase64(message.encryptedPayload);
                const adPrimary = buildAssociatedData(message.senderId, currentUserId);
                const adReversed = buildAssociatedData(currentUserId, message.senderId);
                logCrypto('decrypt-ad-hash', {
                    messageId: message.id,
                    ad_hash: await shortHash(adPrimary),
                    rk_pub_hash: await shortHash(ratchetPublic)
                });
                const senderBundle = token ? await apiGet(token, '/keys/' + message.senderId) : null;
                if (!senderBundle) {
                    throw new Error('Cannot verify Noise sender bundle');
                }

                let decryptedFromX3dh = null;
                let successfulState = null;
                let lastErr = null;
                try {
                    const sharedSecretCandidate = await noiseRespond(localBundle, meta.noise, senderBundle);
                    const candidateStatePrimary = drInitResponder(
                        sharedSecretCandidate,
                        fromBase64(localBundle.spkPrivate),
                        fromBase64(localBundle.spkPublic));
                    try {
                        const pt = await drDecrypt(candidateStatePrimary, ciphertext, ratchetPublic, meta.dr.pn, meta.dr.n, adPrimary);
                        decryptedFromX3dh = pt;
                        successfulState = candidateStatePrimary;
                    } catch (primaryErr) {
                        const candidateStateReversed = drInitResponder(
                            sharedSecretCandidate,
                            fromBase64(localBundle.spkPrivate),
                            fromBase64(localBundle.spkPublic));
                        const pt2 = await drDecrypt(candidateStateReversed, ciphertext, ratchetPublic, meta.dr.pn, meta.dr.n, adReversed);
                        decryptedFromX3dh = pt2;
                        successfulState = candidateStateReversed;
                        console.warn('[Crypto25519] decryptMessage used reverse AD fallback after Noise bootstrap');
                    }
                } catch (candidateErr) {
                    lastErr = candidateErr;
                }

                if (!successfulState || !decryptedFromX3dh) {
                    if (meta.dr && meta.dr.n === 0 && meta.dr.pn === 0) {
                        await tryAutoRecoverBundle(token, currentUserId);
                    }
                    throw lastErr || new Error('Failed to initialize responder session from Noise bootstrap');
                }

                await saveRatchetState(sessionId, successfulState);
                const decryptedText = new TextDecoder().decode(decryptedFromX3dh);
                setCachedPlaintext(message.id, decryptedText);
                logCrypto('decrypt-success-bootstrap', { messageId: message.id });
                return decryptedText;
            }
            let stateSessionId = sessionId;
            if (!state) {
                for (let i = 0; i < sessionIdCandidates.length; i++) {
                    const loaded = await loadRatchetState(sessionIdCandidates[i]);
                    if (loaded) {
                        state = loaded;
                        stateSessionId = sessionIdCandidates[i];
                        break;
                    }
                }
            }
            if (!state) {
                return '[Unable to decrypt]';
            }

            const ratchetPublic = fromBase64(meta.dr.rk_pub);
            const ad = buildAssociatedData(message.senderId, currentUserId);
            const ciphertext = fromBase64(message.encryptedPayload);
            try {
                const plaintext = await drDecrypt(state, ciphertext, ratchetPublic, meta.dr.pn, meta.dr.n, ad);
                await saveRatchetState(stateSessionId, state);
                const text = new TextDecoder().decode(plaintext);
                setCachedPlaintext(message.id, text);
                logCrypto('decrypt-success', {
                    messageId: message.id,
                    sessionId: stateSessionId,
                    n: meta.dr && meta.dr.n,
                    pn: meta.dr && meta.dr.pn
                });
                return text;
            } catch (primaryError) {
                // Compatibility fallback: some historical messages may have AD order mismatch.
                // Try reverse AD before giving up.
                const adReversed = buildAssociatedData(currentUserId, message.senderId);
                const reloaded = await loadRatchetState(stateSessionId);
                if (reloaded) {
                    const plaintext2 = await drDecrypt(reloaded, ciphertext, ratchetPublic, meta.dr.pn, meta.dr.n, adReversed);
                    await saveRatchetState(stateSessionId, reloaded);
                    const text2 = new TextDecoder().decode(plaintext2);
                    setCachedPlaintext(message.id, text2);
                    logCrypto('decrypt-success-reverse-ad', { messageId: message.id, sessionId: stateSessionId });
                    console.warn('[Crypto25519] decryptMessage used reverse AD fallback', {
                        n: meta.dr && meta.dr.n,
                        pn: meta.dr && meta.dr.pn
                    });
                    return text2;
                }
                for (let i = 0; i < sessionIdCandidates.length; i++) {
                    if (sessionIdCandidates[i] === stateSessionId) continue;
                    const alt = await loadRatchetState(sessionIdCandidates[i]);
                    if (!alt) continue;
                    try {
                        const plaintext3 = await drDecrypt(alt, ciphertext, ratchetPublic, meta.dr.pn, meta.dr.n, ad);
                        await saveRatchetState(sessionIdCandidates[i], alt);
                        const text3 = new TextDecoder().decode(plaintext3);
                        setCachedPlaintext(message.id, text3);
                        logCrypto('decrypt-success-alt-session', {
                            messageId: message.id,
                            sessionId: sessionIdCandidates[i]
                        });
                        console.warn('[Crypto25519] decryptMessage used alternate session-id fallback');
                        return text3;
                    } catch {
                        // keep trying candidates
                    }
                }
                throw primaryError;
            }
        } catch (err) {
            console.error('[Crypto25519] decryptMessage failed:', sanitizeLogValue({
                error: err && err.message ? err.message : String(err),
                messageId: message && message.id,
                senderId: message && message.senderId,
                protocolVersion: message && message.protocolVersion,
                encryptionAlgorithm: message && message.encryptionAlgorithm,
                hasMetadata: !!(message && message.metadataJson),
                metadataPreview: message && message.metadataJson ? String(message.metadataJson).slice(0, 180) : null
            }, 'decryptMessage-failed'));
            return '[Unable to decrypt]';
        }
    }

    window.Crypto25519 = {
        ensureKeyBundlePublished: ensureKeyBundlePublished,
        loadBundle: loadBundle,
        encryptText: encryptText,
        decryptMessage: decryptMessage,
        toBase64: toBase64,
        fromBase64: fromBase64,
        concat: concat,
        hkdfSha256: hkdfSha256,
        idbGet: idbGet,
        idbPut: idbPut,
        apiPost: apiPost,
        apiGet: apiGet,
        computeSafetyNumber: computeSafetyNumber,
        regenerateBundle: regenerateBundle,
        getOrCreateChatSafetyNumber: getOrCreateChatSafetyNumber,
        regenerateChatSafetyNumber: regenerateChatSafetyNumber,
        openDb: openDb,
        getStores: getStores,
        API_BASE: API_BASE,
        DB_NAME: DB_NAME,
        DB_VERSION: DB_VERSION
    };
})();
