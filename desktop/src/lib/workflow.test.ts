import { describe, expect, it } from "vitest";
import type { AgentRunSnapshot, ConnectorRequirement } from "../types";
import {
  buildConnectorQuestion,
  connectorRequirementIsComplete,
  formatBytes,
  normalizeCentralUrl,
  packageFileName,
  workspaceIdsFromRun
} from "./workflow";

const completeRequirement: ConnectorRequirement = {
  name: "Line PLC",
  sourceCode: "PLC-01",
  protocol: "OPC UA",
  endpoint: "opc.tcp://10.0.0.5:4840",
  authentication: "certificate",
  dataContract: "temperature: double, cycleId: string",
  samplingPolicy: "1 second",
  successCriteria: "fixture emits valid ProductionEvent JSONL",
  allowedNetworkTargets: "10.0.0.5:4840",
  notes: "read-only"
};

describe("connector workflow helpers", () => {
  it("builds a governed code-generation task from required fields", () => {
    const question = buildConnectorQuestion(completeRequirement);
    expect(question).toContain("生成可构建、可测试的 Ingot 采集连接器代码");
    expect(question).toContain("协议或 SDK：OPC UA");
    expect(question).toContain("测试通过后停止在等待操作者批准打包状态");
    expect(connectorRequirementIsComplete(completeRequirement)).toBe(true);
    expect(connectorRequirementIsComplete({ ...completeRequirement, endpoint: " " })).toBe(false);
  });

  it("extracts unique connector workspace evidence only", () => {
    const run = {
      answer: {
        evidence: [
          { kind: "connector-workspace", id: "ws-1", label: "one" },
          { kind: "agent-artifact", id: "a-1", label: "spec" }
        ]
      },
      toolInvocations: [
        {
          evidence: [
            { kind: "connector-workspace", id: "ws-1", label: "one" },
            { kind: "connector-workspace", id: "ws-2", label: "two" }
          ]
        }
      ]
    } as AgentRunSnapshot;

    expect(workspaceIdsFromRun(run)).toEqual(["ws-1", "ws-2"]);
  });

  it("normalizes addresses and formats immutable package metadata", () => {
    expect(normalizeCentralUrl(" https://central.example.com/// ")).toBe("https://central.example.com");
    expect(formatBytes(1536)).toBe("1.50 KB");
    expect(packageFileName("artifact/source-abcd.zip", "source", "abcd")).toBe("source-abcd.zip");
    expect(packageFileName("", "source", "abcd")).toBe("source-abcd.zip");
  });
});

