import React from "react";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, expect, it, vi } from "vitest";

const { getJson } = vi.hoisted(() => ({ getJson: vi.fn() }));
vi.mock("../src/api/http", () => ({
  getJson,
  postJson: vi.fn(),
  postForm: vi.fn(),
}));

import { ResearchAssetsPage } from "../src/pages/ResearchAssetsPage";

beforeEach(() => getJson.mockReset());

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
