CREATE TABLE IF NOT EXISTS leads (
  id TEXT PRIMARY KEY,
  -- Legacy column name: email HMAC, or HMAC of whatsapp:+55... for phone-only leads.
  email_hash TEXT NOT NULL UNIQUE,
  encrypted_contact TEXT NOT NULL,
  consent_version TEXT NOT NULL,
  created_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS request_limits (bucket TEXT PRIMARY KEY, attempts INTEGER NOT NULL, expires_at INTEGER NOT NULL);
CREATE INDEX IF NOT EXISTS ix_request_limits_expiry ON request_limits(expires_at);
