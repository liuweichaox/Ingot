import React from "react";
import { cleanup, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { afterEach, expect, it } from "vitest";
import { RecommendationEvidence } from "../src/recipe/RecommendationEvidence";

afterEach(cleanup);

it("links the runs recorded with the recommendation and identifies older records", () => {
  const recommendation = {
    siteCode: "site-1",
    observationCount: 1,
    observationReferences: [{ executionKey: "run-1", sourceContentHash: "a".repeat(64) }],
  };
  const view = render(
    <MemoryRouter>
      <RecommendationEvidence recommendation={recommendation} prediction={{}} />
    </MemoryRouter>,
  );
  expect(screen.getByRole("link", { name: "run-1", hidden: true })).toHaveAttribute(
    "href",
    "/process-executions/run-1?siteId=site-1",
  );
  view.rerender(
    <MemoryRouter>
      <RecommendationEvidence recommendation={{ observationCount: 1 }} prediction={{}} />
    </MemoryRouter>,
  );
  expect(screen.getByText("这条历史建议未保存纳入运行清单。")).toBeInTheDocument();
});
