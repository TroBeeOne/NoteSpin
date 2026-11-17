// Locking a note with a password, using the browser's own Web Crypto (no library).
//
// Same method and format as the stand-alone encrypt.html page, so a locked note's text can also be
// opened there: the key comes from the password with PBKDF2 (SHA-256, 150,000 rounds, a random 16-byte
// salt), the note is encrypted with AES-256-GCM (a random 12-byte IV), and what's stored is
// base64( salt | iv | ciphertext ). Nothing depends on the PC, so a backup opens anywhere with the password.
// AES-GCM also checks the data, so a wrong password fails cleanly instead of producing garbage.

const ITERATIONS = 150000;
const enc = new TextEncoder();
const dec = new TextDecoder();

async function deriveKey(password, salt) {
  const base = await crypto.subtle.importKey('raw', enc.encode(password), {name: 'PBKDF2'}, false, ['deriveKey']);
  return crypto.subtle.deriveKey({name: 'PBKDF2', salt, iterations: ITERATIONS, hash: 'SHA-256'},
    base, {name: 'AES-GCM', length: 256}, false, ['encrypt', 'decrypt']);
}

function toBase64(u8) {
  let s = '';
  for (let i = 0; i < u8.length; i += 0x8000) s += String.fromCharCode.apply(null, u8.subarray(i, i + 0x8000));
  return btoa(s);
}

function fromBase64(b64) {
  const s = atob(b64);
  const u8 = new Uint8Array(s.length);
  for (let i = 0; i < s.length; i++) u8[i] = s.charCodeAt(i);
  return u8;
}

/**
 * A note being edited while locked keeps its key for as long as it's open, so saving as you type doesn't
 * repeat the slow key step. Every save still gets a fresh IV (never reused with the same key).
 */
export class LockSession {
  constructor(key, salt) { this.key = key; this.salt = salt; }

  /** Starts a session for a new password (a new salt). */
  static async create(password) {
    const salt = crypto.getRandomValues(new Uint8Array(16));
    return new LockSession(await deriveKey(password, salt), salt);
  }

  /** Opens a locked note. Throws WrongPassword when the password doesn't fit. */
  static async open(blob, password) {
    let data;
    try { data = fromBase64(blob.trim()); } catch { throw new Error('The locked note is damaged.'); }
    if (data.length < 16 + 12 + 16) throw new Error('The locked note is damaged.');
    const salt = data.slice(0, 16), iv = data.slice(16, 28), ct = data.slice(28);
    const key = await deriveKey(password, salt);
    let plain;
    try { plain = await crypto.subtle.decrypt({name: 'AES-GCM', iv}, key, ct); }
    catch { throw new WrongPassword(); }
    return {session: new LockSession(key, salt), text: dec.decode(plain)};
  }

  async encrypt(text) {
    const iv = crypto.getRandomValues(new Uint8Array(12));
    const ct = new Uint8Array(await crypto.subtle.encrypt({name: 'AES-GCM', iv}, this.key, enc.encode(text)));
    const out = new Uint8Array(16 + 12 + ct.length);
    out.set(this.salt, 0);
    out.set(iv, 16);
    out.set(ct, 28);
    return toBase64(out);
  }
}

export class WrongPassword extends Error {
  constructor() { super('Wrong password'); this.name = 'WrongPassword'; }
}
