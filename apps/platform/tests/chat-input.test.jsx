// 验证中文输入组合按键、兼容事件和消息发送快捷键。
import React from "react";
import { cleanup, createEvent, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router";
import { ChatPage } from "../src/pages/ConversationPages";

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
  const input = screen.getByRole("textbox", { name: "给工艺分析助手发送消息" });
  await waitFor(() => expect(input).toBeEnabled());
  fireEvent.change(input, { target: { value: "核对运行记录" } });
  return { input, submitted };
}

describe("Chat 输入快捷键", () => {
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
      mode: "quick",
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
