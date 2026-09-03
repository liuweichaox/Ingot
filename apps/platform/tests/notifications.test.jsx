import React from "react";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { notify, ToastHost } from "../src/ui/components";

beforeEach(() => vi.useFakeTimers());
afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

describe("通知队列", () => {
  it("按触发顺序展示连续通知，每条都保留完整阅读时间", () => {
    render(<ToastHost />);
    act(() => {
      notify("配置已保存");
      notify("下发失败", "danger");
      notify("记录已刷新");
    });
    expect(screen.getByRole("status")).toHaveTextContent("配置已保存");
    act(() => vi.advanceTimersByTime(3500));
    expect(screen.getByRole("status")).toHaveTextContent("下发失败");
    act(() => vi.advanceTimersByTime(3499));
    expect(screen.getByRole("status")).toHaveTextContent("下发失败");
    act(() => vi.advanceTimersByTime(1));
    expect(screen.getByRole("status")).toHaveTextContent("记录已刷新");
    act(() => vi.advanceTimersByTime(3500));
    expect(screen.queryByRole("status")).toBeNull();
  });

  it("新通知入队不重置当前通知的计时", () => {
    render(<ToastHost />);
    act(() => notify("第一条"));
    act(() => vi.advanceTimersByTime(3000));
    act(() => notify("第二条"));
    expect(screen.getByRole("status")).toHaveTextContent("第一条");
    act(() => vi.advanceTimersByTime(500));
    expect(screen.getByRole("status")).toHaveTextContent("第二条");
    act(() => vi.advanceTimersByTime(3499));
    expect(screen.getByRole("status")).toHaveTextContent("第二条");
    act(() => vi.advanceTimersByTime(1));
    expect(screen.queryByRole("status")).toBeNull();
  });

  it("相同文案仍作为两次独立通知展示", () => {
    render(<ToastHost />);
    act(() => {
      notify("保存成功");
      notify("保存成功");
    });
    const firstNotice = screen.getByRole("status");
    act(() => vi.advanceTimersByTime(3500));
    expect(screen.getByRole("status")).toHaveTextContent("保存成功");
    expect(screen.getByRole("status")).not.toBe(firstNotice);
    act(() => vi.advanceTimersByTime(3500));
    expect(screen.queryByRole("status")).toBeNull();
  });

  it("手动关闭只推进一条，旧计时器不会缩短下一条的展示", () => {
    render(<ToastHost />);
    act(() => {
      notify("第一条");
      notify("第二条");
    });
    act(() => vi.advanceTimersByTime(1000));
    fireEvent.click(screen.getByRole("button", { name: "关闭通知" }));
    expect(screen.getByRole("status")).toHaveTextContent("第二条");
    act(() => vi.advanceTimersByTime(3499));
    expect(screen.getByRole("status")).toHaveTextContent("第二条");
    act(() => vi.advanceTimersByTime(1));
    expect(screen.queryByRole("status")).toBeNull();
  });

  it("卸载清理计时器和监听器，重新挂载后只接收新的通知", () => {
    const host = render(<ToastHost />);
    act(() => {
      notify("正在显示");
      notify("尚未显示");
    });
    host.unmount();
    expect(vi.getTimerCount()).toBe(0);
    act(() => notify("卸载时的通知"));
    expect(vi.getTimerCount()).toBe(0);
    render(<ToastHost />);
    expect(screen.queryByRole("status")).toBeNull();
    act(() => notify("重新挂载后的通知"));
    expect(screen.getByRole("status")).toHaveTextContent("重新挂载后的通知");
    act(() => vi.advanceTimersByTime(3500));
    expect(screen.queryByRole("status")).toBeNull();
  });
});
