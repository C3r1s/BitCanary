(function () {
    'use strict';

    const API_BASE = 'http://localhost:5000/api';
    const DB_NAME = 'bitcanary-crypto';
    const DB_VERSION = 1;
    const KEYS_STORE = 'keys';
    const RATCHET_STORE = 'ratchet';
    const HKDF_BLOCK_SIZE = 32;

    let _db = null;

    function toBase64(uint8) {
        return btoa(String.fromCharCode.apply(null, uint8));
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
        if (typeof sodium === 'undefined') {
            throw new Error('libsodium-wrappers did not load. Check script order in index.html.');
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

    async function idbGet(storeName, key) {
        const db = await openDb();
        return new Promise(function (resolve, reject) {
            const tx = db.transaction(storeName, 'readonly');
            const req = tx.objectStore(storeName).get(key);
            req.onsuccess = function (e) { resolve(e.target.result); };
            req.onerror = function (e) { reject(e.target.error); };
        });
    }

    function hkdfSha256(ikm, salt, info, length) {
        ensureSodiumGlobal();
        const saltKey = (salt && salt.length) ? salt : new Uint8Array(HKDF_BLOCK_SIZE);
        // libsodium-wrappers order is message first, key second.
        const prk = sodium.crypto_auth_hmacsha256(ikm, saltKey);
        const n = Math.ceil(length / HKDF_BLOCK_SIZE);
        const blocks = [];
        let prev = new Uint8Array(0);
        for (let i = 1; i <= n; i++) {
            const input = concat(prev, info, new Uint8Array([i]));
            prev = sodium.crypto_auth_hmacsha256(input, prk);
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
                    id: opkAssignedIds[i],
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
        return new TextEncoder().encode(senderId + ':' + recipientId);
    }

    const DR_INFO = new TextEncoder().encode('DR-RK');
    const X3DH_INFO = new TextEncoder().encode('X3DH');

    function kdfRk(rootKey, dhOutput) {
        return hkdfSha256(dhOutput, rootKey, DR_INFO, 64);
    }

    function x3dhInitiate(localBundle, remoteBundleDto) {
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

        let ikm;
        if (remoteBundleDto.opkPublic) {
            const remoteOpk = fromBase64(remoteBundleDto.opkPublic);
            const dh4 = sodium.crypto_scalarmult(ekPair.privateKey, remoteOpk);
            ikm = concat(dh1, dh2, dh3, dh4);
        } else {
            ikm = concat(dh1, dh2, dh3);
        }

        const sk = hkdfSha256(ikm, new Uint8Array(32), X3DH_INFO, 32);
        return {
            sharedSecret: sk,
            ekPublic: ekPair.publicKey,
            header: {
                ek_pub: toBase64(ekPair.publicKey),
                opk_id: remoteBundleDto.opkId || null,
                ik_pub: toBase64(localIkPub)
            }
        };
    }

    function x3dhRespond(localBundle, localOpkPrivateBytes, incomingHeader) {
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

    function drInitInitiator(sharedSecret, remoteSpkPublic) {
        const dhPair = sodium.crypto_box_keypair();
        const dhOutput = sodium.crypto_scalarmult(dhPair.privateKey, remoteSpkPublic);
        const kdf = kdfRk(sharedSecret, dhOutput);
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

    function drEncrypt(state, plaintext, associatedData) {
        if (!state.sendingChainKey) {
            throw new Error('Sending chain key not initialized');
        }
        const messageKey = sodium.crypto_auth_hmacsha256(new Uint8Array([0x01]), state.sendingChainKey);
        state.sendingChainKey = sodium.crypto_auth_hmacsha256(new Uint8Array([0x02]), state.sendingChainKey);
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

    function drDecrypt(state, ciphertext, ratchetPublic, previousChainLength, messageNumber, associatedData) {
        const isDhRatchetNeeded = !state.dhReceivingPublic || !uint8Equal(state.dhReceivingPublic, ratchetPublic);

        if (isDhRatchetNeeded) {
            state.previousSendingChainLength = state.sendMessageNumber;
            state.sendMessageNumber = 0;
            state.receiveMessageNumber = 0;
            state.dhReceivingPublic = new Uint8Array(ratchetPublic);

            const dhOut1 = sodium.crypto_scalarmult(state.dhSendingPrivate, ratchetPublic);
            const kdf1 = kdfRk(state.rootKey, dhOut1);
            state.rootKey = kdf1.slice(0, 32);
            state.receivingChainKey = kdf1.slice(32, 64);

            const newDh = sodium.crypto_box_keypair();
            const dhOut2 = sodium.crypto_scalarmult(newDh.privateKey, ratchetPublic);
            const kdf2 = kdfRk(state.rootKey, dhOut2);
            state.rootKey = kdf2.slice(0, 32);
            state.sendingChainKey = kdf2.slice(32, 64);
            state.dhSendingPrivate = newDh.privateKey;
            state.dhSendingPublic = newDh.publicKey;
        }

        while (state.receiveMessageNumber < messageNumber) {
            state.receivingChainKey = sodium.crypto_auth_hmacsha256(new Uint8Array([0x02]), state.receivingChainKey);
            state.receiveMessageNumber += 1;
        }

        if (!state.receivingChainKey) {
            throw new Error('Receiving chain key not initialized');
        }
        const messageKey = sodium.crypto_auth_hmacsha256(new Uint8Array([0x01]), state.receivingChainKey);
        state.receivingChainKey = sodium.crypto_auth_hmacsha256(new Uint8Array([0x02]), state.receivingChainKey);
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
            previousSendingChainLength: s.previousSendingChainLength
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
            previousSendingChainLength: o.previousSendingChainLength
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
        const sessionId = (currentUserId + ':' + recipientUserId).toLowerCase();
        const localBundle = await loadBundle(currentUserId);
        if (!localBundle) {
            throw new Error('No local key bundle - call ensureKeyBundlePublished first');
        }

        let state = await loadRatchetState(sessionId);
        let x3dhMetadata = null;

        if (!state) {
            const remoteBundle = await apiGet(token, '/keys/' + recipientUserId);
            if (!remoteBundle) {
                throw new Error('Recipient has no published key bundle');
            }
            const x3dh = x3dhInitiate(localBundle, remoteBundle);
            state = drInitInitiator(x3dh.sharedSecret, fromBase64(remoteBundle.spkPublic));
            x3dhMetadata = x3dh.header;
        }

        const ad = buildAssociatedData(currentUserId, recipientUserId);
        const enc = drEncrypt(state, new TextEncoder().encode(plaintext), ad);
        await saveRatchetState(sessionId, state);

        const drMeta = {
            rk_pub: toBase64(enc.ratchetPublic),
            pn: enc.previousChainLength,
            n: enc.messageNumber
        };
        const metadataJson = x3dhMetadata
            ? JSON.stringify({ x3dh: x3dhMetadata, dr: drMeta })
            : JSON.stringify({ dr: drMeta });

        return {
            encryptedPayload: toBase64(enc.ciphertext),
            encryptionAlgorithm: 'signal-protocol-v1',
            keyEnvelope: sessionId,
            metadataJson: metadataJson,
            protocolVersion: 1
        };
    }

    async function decryptMessage(currentUserId, message) {
        try {
            await sodium.ready;
            if (message.protocolVersion === 2) {
                return message.encryptedPayload;
            }
            if (message.protocolVersion === 0 || !message.metadataJson) {
                return '[Unable to decrypt]';
            }
            const sessionId = (message.senderId + ':' + currentUserId).toLowerCase();
            const meta = JSON.parse(message.metadataJson);
            const localBundle = await loadBundle(currentUserId);
            if (!localBundle || !meta.dr) {
                return '[Unable to decrypt]';
            }

            let state = await loadRatchetState(sessionId);
            if (meta.x3dh) {
                const opkId = meta.x3dh.opk_id;
                let opkPriv = null;
                if (opkId && localBundle.otpPrivates) {
                    const found = localBundle.otpPrivates.find(function (o) { return o.id === opkId; });
                    if (found) {
                        opkPriv = fromBase64(found.priv);
                    }
                }
                const sharedSecret = x3dhRespond(localBundle, opkPriv, meta.x3dh);
                state = drInitResponder(
                    sharedSecret,
                    fromBase64(localBundle.spkPrivate),
                    fromBase64(localBundle.spkPublic));
            }
            if (!state) {
                return '[Unable to decrypt]';
            }

            const ratchetPublic = fromBase64(meta.dr.rk_pub);
            const ad = buildAssociatedData(message.senderId, currentUserId);
            const ciphertext = fromBase64(message.encryptedPayload);
            const plaintext = drDecrypt(state, ciphertext, ratchetPublic, meta.dr.pn, meta.dr.n, ad);
            await saveRatchetState(sessionId, state);
            return new TextDecoder().decode(plaintext);
        } catch (err) {
            console.error('[Crypto25519] decryptMessage failed:', err);
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
        openDb: openDb,
        getStores: getStores,
        API_BASE: API_BASE,
        DB_NAME: DB_NAME,
        DB_VERSION: DB_VERSION
    };
})();
