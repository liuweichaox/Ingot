import React from "react";
import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { createRegistryBusinessForm, RegistryBusinessEditor } from "../src/components/RegistryBusinessEditor";
import { ProductionRecordForm } from "../src/pages/ProductionRecordForm";
import { productionResources } from "../src/pages/manufacturingResources";
import { Field, Input } from "../src/ui/components";

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
