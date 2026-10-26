// 验证中文输入组合按键、兼容事件和消息发送快捷键。
import React from "react";
import { cleanup, createEvent, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router";
import { ChatAnswer, ChatPage, MarkdownContent } from "../src/pages/ConversationPages";

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

async function renderChat() {
  const submitted = vi.fn();
  vi.stubGlobal("fetch", vi.fn().mockImplementation((url, options = {}) => {
    if (options.method === "POST") {
      submitted(String(url), JSON.parse(options.body));
      return new Promise(() => {});
    }
    const payload = String(url).includes("/capabilities")
      ? { enabled: true, modes: ["quick"] }
      : { items: [] };
    return Promise.resolve(new Response(JSON.stringify(payload), {
      headers: { "Content-Type": "application/json" },
    }));
  }));
  render(<MemoryRouter initialEntries={["/chat"]}><ChatPage /></MemoryRouter>);
  const input = screen.getByRole("textbox", { name: "提交问题" });
  await waitFor(() => expect(input).toBeEnabled());
  fireEvent.change(input, { target: { value: "核对运行记录" } });
  return { input, submitted };
}

describe("AI 助手输入快捷键", () => {
  it("普通回答使用对话气泡而不是查询结果卡片", () => {
    render(<ChatAnswer answer={{ answerKind: "conversation", summary: "这是普通回答。", limitations: ["回答只基于平台记录"] }} onFollowUp={vi.fn()} />);

    expect(screen.getByText("这是普通回答。")).toBeInTheDocument();
    expect(screen.queryByText("核对结果")).not.toBeInTheDocument();
    expect(screen.queryByText("证据边界")).not.toBeInTheDocument();
  });

  it("将助手内容按 Markdown 渲染为加粗和列表", () => {
    render(<MarkdownContent content={"**重点**\n\n- 项目"} />);

    expect(screen.getByText("重点").tagName).toBe("STRONG");
    expect(screen.getByText("项目").tagName).toBe("LI");
  });

  it("将查询结论与来源编号关联展示", () => {
    render(<MemoryRouter><ChatAnswer
      answer={{
        answerKind: "data_lookup",
        summary: "查询完成。",
        findings: [{
          statement: "该次运行记录完整。",
          strength: "observation",
          evidenceReferences: [{ kind: "process-execution", id: "run-1", label: "运行 run-1" }],
        }],
        relatedRecords: [{ kind: "process-execution", id: "run-1", label: "运行 run-1" }],
      }}
      onFollowUp={vi.fn()}
    /></MemoryRouter>);

    expect(screen.getAllByText("[1]").length).toBeGreaterThan(0);
    expect(screen.getByText("来源")).toBeInTheDocument();
    expect(screen.getAllByText("运行 run-1").length).toBeGreaterThan(0);
  });

  it.each([
    ["组合输入中的 Enter", { isComposing: true }],
    ["仍使用 229 标记的输入法 Enter", { isComposing: false, keyCode: 229 }],
  ])("不会因%s发送消息", async (_name, properties) => {
    const { input, submitted } = await renderChat();
    const event = createEvent.keyDown(input, { key: "Enter", code: "Enter", ...properties });
    fireEvent(input, event);

    expect(submitted).not.toHaveBeenCalled();
    expect(event.defaultPrevented).toBe(false);
    expect(input).toHaveValue("核对运行记录");
  });

  it("在普通 Enter 后发送当前消息", async () => {
    const { input, submitted } = await renderChat();
    const event = createEvent.keyDown(input, { key: "Enter", code: "Enter", keyCode: 13 });
    fireEvent(input, event);

    expect(event.defaultPrevented).toBe(true);
    expect(submitted).toHaveBeenCalledExactlyOnceWith("/api/v1/chat/conversations", expect.objectContaining({
      text: "核对运行记录",
      mode: "auto",
    }));
  });

  it("保留 Shift+Enter 换行且不发送消息", async () => {
    const user = userEvent.setup();
    const { input, submitted } = await renderChat();
    await user.click(input);
    await user.keyboard("{End}{Shift>}{Enter}{/Shift}检查质量结果");

    expect(input).toHaveValue("核对运行记录\n检查质量结果");
    expect(submitted).not.toHaveBeenCalled();
  });
});
