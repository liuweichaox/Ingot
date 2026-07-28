"""FX3U 光学模压 数字孪生(过程仿真源)。

目的:在没有真机的情况下,把**整条价值链**跑通,并做到**与真机无缝切换**——
仿真源产出与真实 FX3U 完全一致的寄存器格式(D100.. 缩放整数),采集/特征/优化路径不变;
上线时把 SimulatedFx3u 换成读真机 MC 1E 的 Fx3uMcSource 即可,上层一行不用改。

真实寄存器映射(来自珲场采集配置 fx3u-molding):
    D100 int16  mold_temp_upper  ×0.1  ℃
    D101 int16  mold_temp_lower  ×0.1  ℃
    D102 int32  press_force      ×0.1  kN
    D104 int16  position         ×0.01 mm
    D200 int16  recipe_step      (10 预热/20 保温/30 压制/40 退火/50 冷却)
    D210 int32  cycle_id
质量结果(面形误差/缺陷率)来自检测,不在 PLC 寄存器里 —— 对应 Inspection 录入。
"""
from __future__ import annotations

from abc import ABC, abstractmethod
from dataclasses import dataclass
import math
import numpy as np

# 寄存器映射(与真机一致);scale 把 PLC 放大整数还原为工程量
REGISTERS = {
    "mold_temp_upper": ("D100", "int16", 0.1),
    "mold_temp_lower": ("D101", "int16", 0.1),
    "press_force":     ("D102", "int32", 0.1),
    "position":        ("D104", "int16", 0.01),
    "recipe_step":     ("D200", "int16", 1),
    "cycle_id":        ("D210", "int32", 1),
}
PHASES = [("preheat", 10, 4), ("soak", 20, 3), ("press", 30, 3), ("anneal", 40, 3), ("cool", 50, 3)]


@dataclass
class CycleResult:
    cycle_id: int
    recipe: dict                 # 本炉所用配方(工程师设定的 setpoint)
    samples: list                # 每次轮询的原始寄存器读数(与真机同格式)
    outcomes: dict               # 检测得到的质量结果(面形/缺陷…)


class MoldingSource(ABC):
    """模压过程源。真机与仿真共用此接口 —— 换源即换实现,上层不变。"""
    @abstractmethod
    def run_cycle(self, recipe: dict) -> CycleResult: ...


# ---- 隐藏真值(工程师看不到):配方 -> 质量结果。含最优窗口、交互、噪声 ----
_OPT = np.array([0.62, 0.55, 0.35, 0.70])   # 隐藏最优工艺窗口(归一化)
_W = np.array([1.4, 1.1, 1.6, 0.7])          # 各变量灵敏度
_BOUNDS = {  # 配方变量的物理范围(= campaign 决策变量范围)
    "soak_temp":   (320.0, 360.0),
    "press_force": (400.0, 600.0),
    "press_speed": (2.0, 10.0),
    "anneal_rate": (1.0, 5.0),
}
_ORDER = ["soak_temp", "press_force", "press_speed", "anneal_rate"]


def _norm(recipe: dict) -> np.ndarray:
    return np.array([(recipe[k] - _BOUNDS[k][0]) / (_BOUNDS[k][1] - _BOUNDS[k][0]) for k in _ORDER])


class SimulatedFx3u(MoldingSource):
    """FX3U 光学模压数字孪生。够真:仿真的过程信号与质量响应都贴合模压物理,
    且以真实寄存器格式产出;换真机只需把 run_cycle 换成写 setpoint→触发→轮询 D100.. 。"""

    def __init__(self, seed: int = 0, poll_hz: float = 1.0):
        if not math.isfinite(poll_hz) or poll_hz <= 0:
            raise ValueError("poll_hz must be positive and finite")
        self.rng = np.random.default_rng(seed)
        self._cycle = 0
        self.poll_hz = poll_hz

    def _outcomes(self, recipe: dict) -> dict:
        # 隐藏真值:配方→质量,含最优窗口、温度-压力交互、噪声。in-spec 区在 4 维里很小,
        # 盲目试错大多调不出;结构化(GP+贝叶斯优化)能稳定收敛。变量序见 _ORDER。
        u = np.clip(_norm(recipe), 0, 1)
        dq = ((u - _OPT) * _W) ** 2
        interaction = 1.2 * (u[0] - u[1]) ** 2       # 保温温度 × 合模力 交互
        surface = 0.15 + 6.5 * dq.sum() + interaction + self.rng.normal(0, 0.015)
        defect = 0.30 + 12.0 * ((u[2] - 0.35) ** 2 + (u[3] - 0.70) ** 2) + self.rng.normal(0, 0.08)
        return {"surface_form_error": round(max(surface, 0.05), 3),
                "defect_rate": round(max(defect, 0.0), 3)}

    def _emit(self, code: str, value: float) -> int:
        """把工程量按寄存器 scale 编码成 PLC 原始整数(与真机读到的一致)。"""
        scale = REGISTERS[code][2]
        return int(round(value / scale))

    def run_cycle(self, recipe: dict) -> CycleResult:
        if set(recipe) != set(_ORDER):
            raise ValueError(f"recipe must contain exactly {_ORDER}")
        for code, (low, high) in _BOUNDS.items():
            value = recipe[code]
            if not math.isfinite(value) or value < low or value > high:
                raise ValueError(f"{code} must be within [{low}, {high}]")
        self._cycle += 1
        soak_t = recipe["soak_temp"]; force_sp = recipe["press_force"]
        speed = recipe["press_speed"]; anneal = recipe["anneal_rate"]
        samples, t_upper, pos = [], 60.0, 0.0
        for name, step, n in PHASES:
            for _ in range(n):
                # 过程动态:温度朝该阶段目标爬升,压制阶段力升到 setpoint,位置随伺服推进
                target_t = {"preheat": soak_t * 0.55, "soak": soak_t, "press": soak_t,
                            "anneal": soak_t * 1.15, "cool": 130.0}[name]
                t_upper += (target_t - t_upper) * 0.5 + self.rng.normal(0, 1.5)
                t_lower = t_upper - self.rng.uniform(1.0, 4.0)
                force = force_sp if name in ("press", "anneal") else (force_sp * 0.02)
                force += self.rng.normal(0, force_sp * 0.01)
                pos += (speed if name == "press" else 0.4) * (1.0 / self.poll_hz)
                samples.append({
                    "D100": self._emit("mold_temp_upper", t_upper),
                    "D101": self._emit("mold_temp_lower", t_lower),
                    "D102": self._emit("press_force", max(force, 0)),
                    "D104": self._emit("position", pos),
                    "D200": step,
                    "D210": self._cycle,
                })
        return CycleResult(self._cycle, dict(recipe), samples, self._outcomes(recipe))


# 换真机时的骨架(接口不变,上层零改动):
# class Fx3uMcSource(MoldingSource):
#     def run_cycle(self, recipe):
#         write_setpoints(recipe)          # 把配方写进 PLC setpoint 寄存器
#         trigger_cycle(); wait_complete()  # 触发一炉、等 cycle.completed
#         samples = poll_registers("D100","D101","D102","D104","D200","D210")  # MC 1E 轮询
#         outcomes = read_inspection(self.cycle_id)  # 检测结果(面形/缺陷)由检验录入
#         return CycleResult(...)
