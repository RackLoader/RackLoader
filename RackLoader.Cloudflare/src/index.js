const encoder = new TextEncoder();
const MOD_ID_PATTERN = /^[A-Za-z0-9._-]{1,128}$/;
const MAX_MANIFEST_BYTES = 1024 * 1024;

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (!url.pathname.startsWith("/api/")) {
      const assetResponse = await env.ASSETS.fetch(request);
      const headers = new Headers(assetResponse.headers);
      headers.set("Content-Security-Policy", "default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self' data:; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'");
      headers.set("X-Content-Type-Options", "nosniff");
      headers.set("Referrer-Policy", "no-referrer");
      headers.set("X-Frame-Options", "DENY");
      return new Response(assetResponse.body, { status: assetResponse.status, statusText: assetResponse.statusText, headers });
    }

    try {
      if (url.pathname === "/api/health" && request.method === "GET")
        return json({ ok: true, service: "RackLoader signing service" });
      if (url.pathname === "/api/trusted-keys" && request.method === "GET")
        return await trustedKeys(env);
      if (url.pathname === "/api/enroll" && request.method === "POST")
        return await enroll(request, env);
      if (url.pathname === "/api/sign" && request.method === "POST")
        return await signManifest(request, env);
      if (url.pathname === "/api/admin/invites" && request.method === "POST") {
        requireAdmin(request, env);
        return await createInvite(request, env);
      }
      if (url.pathname === "/api/admin/developers" && request.method === "GET") {
        requireAdmin(request, env);
        return await listDevelopers(env);
      }
      if (url.pathname === "/api/admin/revoke" && request.method === "POST") {
        requireAdmin(request, env);
        return await revokeDeveloper(request, env);
      }
      return json({ error: "Endpoint not found." }, 404);
    } catch (error) {
      if (error instanceof HttpError)
        return json({ error: error.message }, error.status);
      console.error("RackLoader Worker request failed", error);
      return json({ error: "The service could not complete the request." }, 500);
    }
  }
};

async function trustedKeys(env) {
  const result = await env.DB.prepare(
    "SELECT public_key_id, public_key_pem, display_name, allowed_mod_id FROM developers WHERE revoked_at IS NULL ORDER BY created_at"
  ).all();
  return json({
    keys: result.results.map((row) => ({
      keyId: row.public_key_id,
      publicKeyPem: row.public_key_pem,
      developerName: row.display_name,
      allowedModId: row.allowed_mod_id
    }))
  }, 200, { "Cache-Control": "public, max-age=300, stale-while-revalidate=300" });
}

async function createInvite(request, env) {
  const body = await readJson(request, 16 * 1024);
  const displayName = cleanText(body.displayName, 100, "Developer label");
  const allowedModId = optionalModId(body.allowedModId);
  const requestedDays = Number(body.expiresInDays ?? 7);
  const expiresInDays = Number.isInteger(requestedDays) ? Math.min(Math.max(requestedDays, 1), 30) : 7;
  const code = `rki_${base64Url(crypto.getRandomValues(new Uint8Array(24)))}`;
  const codeHash = await sha256Hex(code);
  const now = Date.now();
  const expiresAt = now + expiresInDays * 24 * 60 * 60 * 1000;
  const inviteId = crypto.randomUUID();
  const activationUrl = new URL("/", request.url);
  activationUrl.searchParams.set("activate", code);
  await env.DB.prepare(
    "INSERT INTO invites (id, code_hash, display_name, allowed_mod_id, created_at, expires_at) VALUES (?, ?, ?, ?, ?, ?)"
  ).bind(inviteId, codeHash, displayName, allowedModId, now, expiresAt).run();
  return json({ activationUrl: activationUrl.toString(), expiresAt: new Date(expiresAt).toISOString(), allowedModId });
}

async function enroll(request, env) {
  const body = await readJson(request, 16 * 1024);
  const code = cleanText(body.code, 256, "Invite code");
  const displayName = typeof body.displayName === "string" ? body.displayName.trim() : "";
  if (displayName.length > 100)
    throw new HttpError(400, "Developer name must not exceed 100 characters.");
  const codeHash = await sha256Hex(code);
  const now = Date.now();
  const inviteResult = await env.DB.prepare(
    "UPDATE invites SET used_at = ? WHERE code_hash = ? AND used_at IS NULL AND expires_at > ? RETURNING id, display_name, allowed_mod_id"
  ).bind(now, codeHash, now).first();
  if (!inviteResult)
    throw new HttpError(400, "This invite code is invalid, expired, or already used.");
  const publisherName = displayName || inviteResult.display_name;

  const keyPair = await crypto.subtle.generateKey({
    name: "RSA-PSS",
    modulusLength: 3072,
    publicExponent: new Uint8Array([1, 0, 1]),
    hash: "SHA-256"
  }, true, ["sign", "verify"]);
  const publicKeyBytes = new Uint8Array(await crypto.subtle.exportKey("spki", keyPair.publicKey));
  const privateKeyBytes = new Uint8Array(await crypto.subtle.exportKey("pkcs8", keyPair.privateKey));
  const publicKeyId = await sha256Hex(publicKeyBytes);
  const publicKeyPem = pem("PUBLIC KEY", publicKeyBytes);
  const developerId = crypto.randomUUID();
  const developerToken = `rkd_${base64Url(crypto.getRandomValues(new Uint8Array(32)))}`;
  const tokenHash = await sha256Hex(developerToken);
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const encryptionKey = await importEncryptionKey(env.KEY_ENCRYPTION_KEY);
  const encryptedPrivateKey = new Uint8Array(await crypto.subtle.encrypt({
    name: "AES-GCM",
    iv,
    additionalData: encoder.encode(developerId),
    tagLength: 128
  }, encryptionKey, privateKeyBytes));

  await env.DB.prepare(
    "INSERT INTO developers (id, display_name, allowed_mod_id, token_hash, public_key_id, public_key_pem, private_key_ciphertext, private_key_iv, created_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)"
  ).bind(
    developerId,
    publisherName,
    inviteResult.allowed_mod_id,
    tokenHash,
    publicKeyId,
    publicKeyPem,
    toBase64(encryptedPrivateKey),
    toBase64(iv),
    now
  ).run();

  privateKeyBytes.fill(0);
  return json({
    developerId,
    developerToken,
    displayName: publisherName,
    keyId: publicKeyId,
    publicKeyPem,
    allowedModId: inviteResult.allowed_mod_id
  }, 201, { "Cache-Control": "no-store" });
}

async function signManifest(request, env) {
  const developerToken = bearerToken(request);
  const tokenHash = await sha256Hex(developerToken);
  const developer = await env.DB.prepare(
    "SELECT id, allowed_mod_id, public_key_id, public_key_pem, private_key_ciphertext, private_key_iv FROM developers WHERE token_hash = ? AND revoked_at IS NULL"
  ).bind(tokenHash).first();
  if (!developer)
    throw new HttpError(401, "Developer key is invalid or revoked.");

  const body = await readJson(request, Math.ceil(MAX_MANIFEST_BYTES * 1.5));
  if (typeof body.manifestBase64 !== "string" || body.manifestBase64.length > Math.ceil(MAX_MANIFEST_BYTES * 1.5))
    throw new HttpError(400, "manifestBase64 is missing or too large.");
  let manifestBytes;
  try {
    manifestBytes = Uint8Array.from(atob(body.manifestBase64), (character) => character.charCodeAt(0));
  } catch {
    throw new HttpError(400, "manifestBase64 is not valid Base64.");
  }
  if (manifestBytes.byteLength === 0 || manifestBytes.byteLength > MAX_MANIFEST_BYTES)
    throw new HttpError(400, "The manifest size is outside the supported limit.");

  let manifest;
  try {
    manifest = JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(manifestBytes));
  } catch {
    throw new HttpError(400, "The manifest is not valid UTF-8 JSON.");
  }
  if (manifest?.format !== "RackMod" || manifest?.formatVersion !== 1 ||
      typeof manifest.id !== "string" || !MOD_ID_PATTERN.test(manifest.id) ||
      typeof manifest.name !== "string" || manifest.name.length < 1 || manifest.name.length > 200 ||
      typeof manifest.version !== "string" || manifest.version.length < 1 || manifest.version.length > 128 ||
      typeof manifest.entryPoint?.module !== "string" || typeof manifest.entryPoint?.type !== "string")
    throw new HttpError(400, "The manifest does not match RackMod format version 1.");
  if (developer.allowed_mod_id && manifest.id.toLowerCase() !== developer.allowed_mod_id.toLowerCase())
    throw new HttpError(403, `This developer key may only sign mod '${developer.allowed_mod_id}'.`);

  const encryptionKey = await importEncryptionKey(env.KEY_ENCRYPTION_KEY);
  const privateKeyBytes = await crypto.subtle.decrypt({
    name: "AES-GCM",
    iv: fromBase64(developer.private_key_iv),
    additionalData: encoder.encode(developer.id),
    tagLength: 128
  }, encryptionKey, fromBase64(developer.private_key_ciphertext));
  const privateKey = await crypto.subtle.importKey("pkcs8", privateKeyBytes, {
    name: "RSA-PSS",
    hash: "SHA-256"
  }, false, ["sign"]);
  const signature = new Uint8Array(await crypto.subtle.sign({ name: "RSA-PSS", saltLength: 32 }, privateKey, manifestBytes));
  return json({
    algorithm: "RSA-PSS-SHA256",
    publicKeyId: developer.public_key_id,
    publicKeyPem: developer.public_key_pem,
    signature: toBase64(signature)
  }, 200, { "Cache-Control": "no-store" });
}

async function listDevelopers(env) {
  const result = await env.DB.prepare(
    "SELECT id, display_name, allowed_mod_id, public_key_id, created_at, revoked_at FROM developers ORDER BY created_at DESC"
  ).all();
  return json({ developers: result.results.map((row) => ({
    id: row.id,
    displayName: row.display_name,
    allowedModId: row.allowed_mod_id,
    keyId: row.public_key_id,
    createdAt: new Date(row.created_at).toISOString(),
    revokedAt: row.revoked_at ? new Date(row.revoked_at).toISOString() : null
  })) });
}

async function revokeDeveloper(request, env) {
  const body = await readJson(request, 16 * 1024);
  const developerId = cleanText(body.developerId, 80, "Developer ID");
  const result = await env.DB.prepare(
    "UPDATE developers SET revoked_at = ? WHERE id = ? AND revoked_at IS NULL"
  ).bind(Date.now(), developerId).run();
  if (!result.meta.changes)
    throw new HttpError(404, "Active developer was not found.");
  return json({ ok: true });
}

function requireAdmin(request, env) {
  const expected = env.ADMIN_TOKEN;
  const actual = request.headers.get("Authorization") || "";
  if (!expected || actual !== `Bearer ${expected}`)
    throw new HttpError(401, "Admin authentication is required.");
}

function bearerToken(request) {
  const header = request.headers.get("Authorization") || "";
  const match = /^Bearer (rkd_[A-Za-z0-9_-]{30,100})$/.exec(header);
  if (!match)
    throw new HttpError(401, "A developer key is required in the Authorization header.");
  return match[1];
}

async function readJson(request, maxBytes) {
  const declaredSize = Number(request.headers.get("Content-Length") || 0);
  if (declaredSize > maxBytes)
    throw new HttpError(413, "Request body is too large.");
  const text = await request.text();
  if (encoder.encode(text).byteLength > maxBytes)
    throw new HttpError(413, "Request body is too large.");
  try {
    return JSON.parse(text);
  } catch {
    throw new HttpError(400, "Request body must be valid JSON.");
  }
}

function cleanText(value, maxLength, label) {
  if (typeof value !== "string")
    throw new HttpError(400, `${label} is required.`);
  const cleaned = value.trim();
  if (!cleaned || cleaned.length > maxLength)
    throw new HttpError(400, `${label} must be between 1 and ${maxLength} characters.`);
  return cleaned;
}

function optionalModId(value) {
  if (value === undefined || value === null || value === "") return null;
  const modId = cleanText(value, 128, "Mod ID");
  if (!MOD_ID_PATTERN.test(modId))
    throw new HttpError(400, "Mod ID may contain letters, numbers, dots, underscores, and hyphens.");
  return modId;
}

async function importEncryptionKey(value) {
  if (!value) throw new Error("KEY_ENCRYPTION_KEY is not configured.");
  const raw = fromBase64(value);
  if (raw.byteLength !== 32) throw new Error("KEY_ENCRYPTION_KEY must decode to exactly 32 bytes.");
  return crypto.subtle.importKey("raw", raw, "AES-GCM", false, ["encrypt", "decrypt"]);
}

async function sha256Hex(value) {
  const bytes = typeof value === "string" ? encoder.encode(value) : value;
  return Array.from(new Uint8Array(await crypto.subtle.digest("SHA-256", bytes)))
    .map((part) => part.toString(16).padStart(2, "0")).join("");
}

function pem(label, bytes) {
  const base64 = toBase64(bytes).match(/.{1,64}/g).join("\n");
  return `-----BEGIN ${label}-----\n${base64}\n-----END ${label}-----\n`;
}

function toBase64(bytes) {
  let binary = "";
  for (let offset = 0; offset < bytes.length; offset += 0x8000)
    binary += String.fromCharCode(...bytes.subarray(offset, offset + 0x8000));
  return btoa(binary);
}

function fromBase64(value) {
  return Uint8Array.from(atob(value), (character) => character.charCodeAt(0));
}

function base64Url(bytes) {
  return toBase64(bytes).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
}

function json(body, status = 200, extraHeaders = {}) {
  return new Response(JSON.stringify(body), {
    status,
    headers: {
      "Content-Type": "application/json; charset=utf-8",
      "X-Content-Type-Options": "nosniff",
      "Cache-Control": "no-store",
      ...extraHeaders
    }
  });
}

class HttpError extends Error {
  constructor(status, message) {
    super(message);
    this.status = status;
  }
}
