-- 一个实验只能生成一个正式结果。进程内锁不能阻止多实例并发写入，
-- 因此该不变量必须由数据库承担。
CREATE UNIQUE INDEX IF NOT EXISTS ux_research_experiment_results_experiment
    ON research_experiment_results (experiment_id);
