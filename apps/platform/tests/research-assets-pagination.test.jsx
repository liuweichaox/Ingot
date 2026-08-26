// 验证研发资产工作区使用有界游标分页且不会丢失项目上下文。
import React from "react";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, expect, it, vi } from "vitest";

const { getJson } = vi.hoisted(() => ({ getJson: vi.fn() }));
vi.mock("../src/api/http", () => ({
  getJson,
  postJson: vi.fn(),
  postForm: vi.fn(),
}));

import { ResearchAssetsPage } from "../src/pages/ResearchAssetsPage";

beforeEach(() => {
  cleanup();
  getJson.mockReset();
});

it("按 cursor 加载更多并按稳定业务键去重", async () => {
  getJson.mockImplementation(async url => {
    url = String(url || "");
    if (url === "/api/v1/research-projects?limit=100")
      return { data: [{ projectId: "project-1", name: "项目一" }] };
    if (url.startsWith("/api/v1/training-datasets") && url.includes("cursor="))
      return { data: [{ datasetId: "dataset-b", version: 1, name: "数据集 B" }], nextCursor: null };
    if (url.startsWith("/api/v1/training-datasets"))
      return { data: [{ datasetId: "dataset-a", version: 1, name: "数据集 A" }], nextCursor: "next-page" };
    return { data: [], nextCursor: null };
  });

  render(<ResearchAssetsPage />);
  expect(await screen.findByText("数据集 A")).toBeInTheDocument();
  await userEvent.click(screen.getByRole("button", { name: "加载更多" }));

  await waitFor(() => expect(screen.getByText("数据集 B")).toBeInTheDocument());
  expect(getJson).toHaveBeenCalledWith(expect.stringContaining("cursor=next-page"));
  expect(screen.queryByRole("button", { name: "加载更多" })).toBeNull();
});

it("没有可访问项目时不显示知识上传工作台", async () => {
  getJson.mockImplementation(async url => {
    if (String(url || "") === "/api/v1/research-projects?limit=100")
      return { data: [] };
    return { data: [], nextCursor: null };
  });

  render(<ResearchAssetsPage />);

  expect(await screen.findByText("请先创建研发项目")).toBeInTheDocument();
  expect(screen.getByText("知识来源必须归属一个可访问的研发项目；创建项目后即可上传并提取文件。")).toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "上传并提取" })).toBeNull();
});
