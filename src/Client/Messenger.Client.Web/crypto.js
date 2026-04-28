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

    window.Crypto25519 = {
        ensureKeyBundlePublished: ensureKeyBundlePublished,
        loadBundle: loadBundle,
        toBase64: toBase64,
        fromBase64: fromBase64,
        concat: concat,
        hkdfSha256: hkdfSha256,
        idbGet: idbGet,
        idbPut: idbPut,
        apiPost: apiPost,
        openDb: openDb,
        getStores: getStores,
        API_BASE: API_BASE,
        DB_NAME: DB_NAME,
        DB_VERSION: DB_VERSION
    };
})();
