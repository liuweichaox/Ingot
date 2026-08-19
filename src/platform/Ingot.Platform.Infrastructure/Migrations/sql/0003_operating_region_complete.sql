-- Week 1: 工艺操作域完整实现
-- 第1部分：参数边界定义表

CREATE TABLE IF NOT EXISTS research_operating_region_parameter_bounds (
    -- 主键
    parameter_bounds_id TEXT NOT NULL PRIMARY KEY,

    -- 外键
    operating_region_id TEXT NOT NULL,

    -- 参数定义
    parameter_name TEXT NOT NULL,

    -- 边界值
    min_value DECIMAL NOT NULL,
    max_value DECIMAL NOT NULL,

    -- 单位
    unit_of_measure TEXT,

    -- 关键程度（1-5）
    criticality_level SMALLINT DEFAULT 3,

    -- 相关性描述（与其他参数的约束）
    correlation_notes TEXT,

    -- 审计字段
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

-- 索引
CREATE INDEX IF NOT EXISTS idx_region_parameter_bounds_region
    ON research_operating_region_parameter_bounds(operating_region_id);

CREATE UNIQUE INDEX IF NOT EXISTS idx_region_parameter_bounds_unique
    ON research_operating_region_parameter_bounds(operating_region_id, parameter_name);

-- 第2部分：验证历史（用于凸包计算）
CREATE TABLE IF NOT EXISTS research_operating_region_validation_history (
    -- 主键
    validation_history_id TEXT NOT NULL PRIMARY KEY,

    -- 外键
    operating_region_id TEXT NOT NULL,
    experiment_id TEXT NOT NULL,

    -- 运行号和时间
    execution_id TEXT NOT NULL,
    validation_timestamp TIMESTAMP WITH TIME ZONE NOT NULL,

    -- 参数值（JSON 格式：{"param_name": value, ...}）
    parameter_values JSONB NOT NULL,

    -- 验证结果
    outcome_status TEXT NOT NULL, -- PASSED, FAILED, UNCERTAIN
    quality_score DECIMAL, -- 0-100
    notes TEXT,

    -- 审计字段
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

-- 索引
CREATE INDEX IF NOT EXISTS idx_validation_region
    ON research_operating_region_validation_history(operating_region_id, validation_timestamp DESC);

CREATE INDEX IF NOT EXISTS idx_validation_experiment
    ON research_operating_region_validation_history(experiment_id);

-- 第3部分：操作域扩展日志
CREATE TABLE IF NOT EXISTS research_operating_region_extensions (
    -- 主键
    extension_id TEXT NOT NULL PRIMARY KEY,

    -- 外键
    operating_region_id TEXT NOT NULL,

    -- 扩展原因
    triggering_experiment_id TEXT NOT NULL,
    out_of_bounds_parameter_name TEXT NOT NULL,
    out_of_bounds_value DECIMAL NOT NULL,

    -- 原始边界 vs 新边界
    original_min_value DECIMAL NOT NULL,
    original_max_value DECIMAL NOT NULL,
    extended_min_value DECIMAL NOT NULL,
    extended_max_value DECIMAL NOT NULL,

    -- 扩展决策
    extension_approved BOOLEAN DEFAULT FALSE,
    approved_by TEXT,
    approval_timestamp TIMESTAMP WITH TIME ZONE,
    approval_notes TEXT,

    -- 审计字段
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

-- 索引
CREATE INDEX IF NOT EXISTS idx_extension_region
    ON research_operating_region_extensions(operating_region_id, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_extension_approval
    ON research_operating_region_extensions(extension_approved)
    WHERE NOT extension_approved;

-- 第4部分：操作域置信度和质量指标
ALTER TABLE research_operating_regions
    ADD COLUMN IF NOT EXISTS confidence_level TEXT DEFAULT 'INCOMPLETE', -- INCOMPLETE, PROVISIONAL, VALIDATED, MATURE
    ADD COLUMN IF NOT EXISTS boundary_calculation_method TEXT DEFAULT 'MIN_MAX', -- MIN_MAX, CONVEX_HULL, ML_MODEL
    ADD COLUMN IF NOT EXISTS last_boundary_update TIMESTAMP WITH TIME ZONE,
    ADD COLUMN IF NOT EXISTS validated_experiment_count INTEGER DEFAULT 0,
    ADD COLUMN IF NOT EXISTS boundary_quality_score DECIMAL DEFAULT 0; -- 0-100，基于验证点密度

-- 第5部分：约束关系（参数间耦合）
CREATE TABLE IF NOT EXISTS research_operating_region_parameter_constraints (
    -- 主键
    constraint_id TEXT NOT NULL PRIMARY KEY,

    -- 外键
    operating_region_id TEXT NOT NULL,

    -- 约束定义
    constraint_type TEXT NOT NULL, -- COUPLED_MIN, COUPLED_MAX, RATIO, PRODUCT, CUSTOM
    parameter_name_a TEXT NOT NULL,
    parameter_name_b TEXT NOT NULL,

    -- 约束表达式
    constraint_expression TEXT NOT NULL, -- e.g., "temp > pressure * 2"
    constraint_description TEXT,

    -- 审计字段
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

-- 索引
CREATE INDEX IF NOT EXISTS idx_constraint_region
    ON research_operating_region_parameter_constraints(operating_region_id);

CREATE INDEX IF NOT EXISTS idx_constraint_type
    ON research_operating_region_parameter_constraints(constraint_type);

-- 自动更新 updated_at 触发器函数和触发器（对已有的 operating_regions 表）
CREATE OR REPLACE FUNCTION update_operating_regions_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_operating_regions_updated_at ON research_operating_regions;
CREATE TRIGGER trg_operating_regions_updated_at
    BEFORE UPDATE ON research_operating_regions
    FOR EACH ROW
    EXECUTE FUNCTION update_operating_regions_updated_at();
