"""光学模压专用演示：在合成响应面上验证优化机制。

真值(工程师看不到)藏着一个最优工艺窗口。优化器只能通过"做实验→测量"逐步逼近。
对比:序贯贝叶斯优化 vs 随机搜索,报告到达规格所需的试验次数(trials-to-spec)。

真实历史验证使用 README 中的 replay_history_pool；优化器只能选择历史中实际存在且
尚未使用的配方，不能把本合成真值函数替换成最近邻后宣称节省真实试验次数。
"""
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

from ingot_optimizer import Campaign, Variable, Objective
from ingot_optimizer.loop import SequentialOptimizer

campaign = Campaign(
    name="LENS-A 新件工艺研发",
    variables=[
        Variable("soak_temp",   320, 360, "℃"),
        Variable("press_force", 400, 600, "kN"),
        Variable("press_speed",   2,  10, "mm/s"),
        Variable("anneal_rate",   1,   5, "℃/s"),
    ],
    objectives=[
        Objective("surface_form_error", kind="le", threshold=0.5, unit="µm"),
        Objective("defect_rate",        kind="le", threshold=2.0, unit="%"),
    ],
)

_OPT = np.array([0.62, 0.55, 0.35, 0.70])   # 隐藏的最优工艺窗口(归一化)
_W = np.array([1.4, 1.1, 1.6, 0.7])          # 各变量灵敏度


def truth_fn(params: dict) -> dict:
    u = campaign.to_unit(params)
    dq = ((u - _OPT) * _W) ** 2
    interaction = 1.2 * (u[0] - u[1]) ** 2
    surface = 0.15 + 6.5 * dq.sum() + interaction
    defect = 0.30 + 12.0 * ((u[2] - 0.35) ** 2 + (u[3] - 0.70) ** 2)
    return {"surface_form_error": max(surface, 0.05), "defect_rate": max(defect, 0.0)}


def run_bo(budget, seed):
    """序贯BO:命中规格即停;返回(best-so-far 轨迹, 达标所需次数或None)。"""
    opt = SequentialOptimizer(campaign, seed=seed)
    rng = np.random.default_rng(seed)
    curve, hit = [], None
    for t in range(budget):
        if len(opt.X) < 2:
            p = campaign.from_unit(rng.uniform(0, 1, campaign.dim))
        else:
            p = opt.suggest()[0].recommended_params
        opt.observe(p, truth_fn(p))
        curve.append(min(opt.distances))
        if hit is None and opt.in_spec():
            hit = t + 1
            break
    while len(curve) < budget:      # 命中后补齐曲线便于画图
        curve.append(curve[-1])
    return np.array(curve), hit


def run_random(budget, seed):
    rng = np.random.default_rng(seed + 99)
    best, curve, hit = np.inf, [], None
    for t in range(budget):
        p = campaign.from_unit(rng.uniform(0, 1, campaign.dim))
        best = min(best, campaign.distance_to_spec(truth_fn(p)))
        curve.append(best)
        if hit is None and best <= 0:
            hit = t + 1
    return np.array(curve), hit


def stats(hits, n):
    ok = [h for h in hits if h is not None]
    return dict(rate=len(ok) / n,
                median=float(np.median(ok)) if ok else float("nan"),
                mean=float(np.mean(ok)) if ok else float("nan"))


def main():
    budget, n_seeds = 40, 15
    print("=" * 64)
    print("Ingot 优化大脑 —— 合成光学模压响应面 回放验证")
    print(f"决策变量 {campaign.dim} 个;目标:面形≤0.5µm 且 缺陷≤2.0%")
    print(f"预算 {budget} 次试验,{n_seeds} 个随机种子")
    print("=" * 64)

    bo_curves, bo_hits, rd_curves, rd_hits = [], [], [], []
    for s in range(n_seeds):
        c, h = run_bo(budget, s);      bo_curves.append(c); bo_hits.append(h)
        c, h = run_random(budget, s);  rd_curves.append(c); rd_hits.append(h)

    bo, rd = stats(bo_hits, n_seeds), stats(rd_hits, n_seeds)
    def penalized(hits):
        return float(np.mean([h if h is not None else budget for h in hits]))
    bo_pen, rd_pen = penalized(bo_hits), penalized(rd_hits)

    print(f"{'方法':<16}{'达标成功率':<12}{'达标者中位次数':<14}{'期望次数(未达标计满预算)':<12}")
    print(f"{'序贯BO(大脑)':<16}{bo['rate']*100:>5.0f}%       {bo['median']:>6.1f}         {bo_pen:>6.1f}")
    print(f"{'随机搜索(试错)':<16}{rd['rate']*100:>5.0f}%       {rd['median']:>6.1f}         {rd_pen:>6.1f}")
    n_bo_ok = sum(h is not None for h in bo_hits)
    n_rd_ok = sum(h is not None for h in rd_hits)
    print(f"\n>> 大脑:{n_bo_ok}/{n_seeds} 次达标,中位 {bo['median']:.0f} 次试验。"
          f"\n>> 试错:{n_rd_ok}/{n_seeds} 次达标——{n_seeds-n_rd_ok} 次在 {budget} 次试验预算内**根本没碰到规格**。"
          f"\n>> 惩罚化期望:大脑 {bo_pen:.0f} 次 vs 试错 {rd_pen:.0f} 次,"
          f"实际快约 {(1-bo_pen/rd_pen)*100:.0f}%(试错的慢主要来自大量'调不出来')。")

    x = np.arange(1, budget + 1)
    BO, RD = np.array(bo_curves), np.array(rd_curves)
    plt.figure(figsize=(8, 5))
    plt.axhline(0, color="#444", lw=1, ls="--", label="spec reached (d=0)")
    plt.plot(x, BO.mean(0), color="#2563eb", lw=2.2, label="Sequential BO (brain)")
    plt.fill_between(x, np.percentile(BO, 25, 0), np.percentile(BO, 75, 0), color="#2563eb", alpha=0.15)
    plt.plot(x, RD.mean(0), color="#dc2626", lw=2.2, label="Random search (trial-and-error)")
    plt.fill_between(x, np.percentile(RD, 25, 0), np.percentile(RD, 75, 0), color="#dc2626", alpha=0.12)
    plt.xlabel("Number of experiments (molds)"); plt.ylabel("Best distance-to-spec  (<0 = in spec)")
    plt.title("Speed to reach the process spec: optimization brain vs trial-and-error")
    plt.legend(); plt.grid(alpha=0.25); plt.tight_layout()
    plt.savefig("convergence.png", dpi=130)
    print(">> 收敛图已保存:convergence.png")


if __name__ == "__main__":
    main()
