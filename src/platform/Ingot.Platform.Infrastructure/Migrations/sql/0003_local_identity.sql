-- 0003_local_identity.sql
-- 内置本地账户体系：消除"生产环境必须自带 OIDC IdP"这一最大部署摩擦。
-- 生产 Authentication:Mode=Local（默认）时启用；Mode=Oidc 时这些表存在但不使用。
-- 口令以 ASP.NET Core PasswordHasher（PBKDF2-HMAC-SHA256，加盐、高迭代）存储，不落明文。

CREATE TABLE IF NOT EXISTS users (
  user_id        UUID PRIMARY KEY,
  username       TEXT NOT NULL,
  username_lower TEXT NOT NULL,           -- 小写规范化，保证账号大小写不敏感唯一
  display_name   TEXT NOT NULL DEFAULT '',
  password_hash  TEXT NOT NULL,
  roles          TEXT[] NOT NULL DEFAULT '{}',
  disabled       BOOLEAN NOT NULL DEFAULT FALSE,
  created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at     TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_users_username_lower ON users (username_lower);

-- 会话令牌：只存不透明令牌的 SHA-256，撤销即删除。单实例自托管下用有状态会话最易审计。
CREATE TABLE IF NOT EXISTS user_sessions (
  token_hash    TEXT PRIMARY KEY,
  user_id       UUID NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
  created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  expires_at    TIMESTAMPTZ NOT NULL,
  last_seen_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_user_sessions_user ON user_sessions (user_id);
CREATE INDEX IF NOT EXISTS idx_user_sessions_expiry ON user_sessions (expires_at);
