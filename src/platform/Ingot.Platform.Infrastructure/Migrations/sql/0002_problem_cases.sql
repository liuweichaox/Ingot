-- 0002_problem_cases.sql
-- 证据定级主轴：问题档案（problem_cases）+ 定级评估记录（case_level_evaluations）。
-- 系统主对象从"事件"转为"具名工艺问题"；等级由数据自动评定、诚实降级。
-- L3+ 的入口门控（试验/建议必须挂 case 且 case 等级达标）在后续迁移接线。

CREATE TABLE IF NOT EXISTS problem_cases (
  case_id              UUID PRIMARY KEY,
  title                TEXT NOT NULL,
  description          TEXT NOT NULL DEFAULT '',
  status               TEXT NOT NULL DEFAULT 'open',

  -- 绑定范围：解析为 production_events 的 subject 与 context 过滤 + 时间窗
  subject_type         TEXT,
  subject_id           TEXT,
  context_filter       JSONB NOT NULL DEFAULT '{}'::jsonb,
  comparison_key       TEXT,             -- L2 同类分组的 context 键，如 'mold_id'
  window_from          TIMESTAMPTZ,
  window_to            TIMESTAMPTZ,
  target_metric        TEXT NOT NULL DEFAULT '',

  -- 证据定级状态
  current_level        TEXT NOT NULL DEFAULT 'L0-pending',
  feature_set_ratified BOOLEAN NOT NULL DEFAULT FALSE,   -- L2 人工门：特征集经工艺工程师核定
  ratified_by          TEXT,
  ratified_at          TIMESTAMPTZ,

  owner                TEXT,
  created_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at           TIMESTAMPTZ NOT NULL DEFAULT now(),

  CONSTRAINT ck_problem_cases_status CHECK (status IN ('open', 'resolved', 'archived'))
);

CREATE INDEX IF NOT EXISTS idx_problem_cases_status
  ON problem_cases (status, updated_at DESC);

CREATE TABLE IF NOT EXISTS case_level_evaluations (
  evaluation_id  UUID PRIMARY KEY,
  case_id        UUID NOT NULL REFERENCES problem_cases(case_id) ON DELETE CASCADE,
  evaluated_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  level          TEXT NOT NULL,            -- 本次评定达到的最高等级：L0..L2.5
  gates          JSONB NOT NULL,           -- [{name, measured, threshold, passed}] 逐门槛证据
  window_days    INTEGER NOT NULL DEFAULT 14
);

CREATE INDEX IF NOT EXISTS idx_case_level_eval_case_time
  ON case_level_evaluations (case_id, evaluated_at DESC);
