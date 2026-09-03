import React from "react";
import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { createRegistryBusinessForm, RegistryBusinessEditor } from "../src/components/RegistryBusinessEditor";
import { ProductionRecordForm } from "../src/pages/ProductionRecordForm";
import { productionResources } from "../src/pages/manufacturingResources";
import { Field, Input } from "../src/ui/components";

vi.mock("../src/hooks/useApi", async importOriginal => ({
  ...await importOriginal(),
  useApi: url => ({
    data: url.includes("data-reliability/baseline")
      ? { analyzedRunCount: 20, contextFields: [{ field: "material_lot_ref", coverage: 0.85, presentRunCount: 17, runCount: 20 }] }
      : [],
    error: "",
  }),
}));

afterEach(cleanup);

describe("关键操作说明", () => {
  it("保留常规说明默认隐藏，同时允许字段显式展示操作规则", () => {
    render(<>
      <Field label="常规字段" hint="常规辅助说明"><Input /></Field>
      <Field label="关键字段" hint="影响操作的规则" hintVisible error="请检查输入"><Input /></Field>
    </>);
    expect(screen.getByText("常规辅助说明")).toHaveClass("sr-only");
    expect(screen.getByText("影响操作的规则")).not.toHaveClass("sr-only");
    expect(screen.getByRole("alert")).toHaveTextContent("请检查输入");
  });

  it("工艺配置显示真实覆盖率、分析准入规则和因素重叠条件", () => {
    const form = createRegistryBusinessForm("scenarioPackage", {
      contextFields: [{ fieldCode: "material_lot_ref", name: "材料批次", mode: "required-for-analysis", minimumCoverage: 0.95 }],
    });
    render(<RegistryBusinessEditor kind="scenarioPackage" form={form} onChange={() => {}} />);
    for (const text of [
      "当前覆盖：85%（17/20）",
      "分析必需会排除缺失该字段的运行；进入建模还要求经过因素重叠验证。",
      "只有把该字段作为分层/混杂因素时才填写；0.5 表示至少覆盖一半组合。",
    ]) {
      expect(screen.getByText(text)).not.toHaveClass("sr-only");
    }
  });

  it("分析方案显示多字段输入规则", () => {
    render(<RegistryBusinessEditor kind="analysisPlan" form={createRegistryBusinessForm("analysisPlan")} onChange={() => {}} />);
    expect(screen.getByText("多个字段用逗号分隔。")).not.toHaveClass("sr-only");
  });

  it("生产上下文显示批次一致性和校准过期说明，常规示例保持隐藏", () => {
    render(<ProductionRecordForm resource={productionResources.context} editor={{}} editorMode="create" onChange={() => {}} />);
    expect(screen.getByText("同一批产品经过多台设备时，各设备填写相同批次号")).not.toHaveClass("sr-only");
    expect(screen.getByText("例如 valid、due；到期后运行快照会强制标记 expired")).not.toHaveClass("sr-only");
    expect(screen.getByText("例如 LENS-A、轴类零件")).toHaveClass("sr-only");
  });
});
