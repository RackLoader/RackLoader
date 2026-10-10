CREATE TABLE IF NOT EXISTS invites (
  id TEXT PRIMARY KEY NOT NULL,
  code_hash TEXT NOT NULL UNIQUE,
  display_name TEXT NOT NULL,
  allowed_mod_id TEXT,
  created_at INTEGER NOT NULL,
  expires_at INTEGER NOT NULL,
  used_at INTEGER
);

CREATE INDEX IF NOT EXISTS invites_expiration_idx ON invites(expires_at, used_at);

CREATE TABLE IF NOT EXISTS developers (
  id TEXT PRIMARY KEY NOT NULL,
  display_name TEXT NOT NULL,
  allowed_mod_id TEXT,
  token_hash TEXT NOT NULL UNIQUE,
  public_key_id TEXT NOT NULL UNIQUE,
  public_key_pem TEXT NOT NULL,
  private_key_ciphertext TEXT NOT NULL,
  private_key_iv TEXT NOT NULL,
  created_at INTEGER NOT NULL,
  revoked_at INTEGER
);

CREATE INDEX IF NOT EXISTS developers_active_idx ON developers(revoked_at, created_at);
