// Keep shared, UI, and Edge protocol registration surfaces aligned.
import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { protocolDescriptor, protocolOptions } from "../src/acquisition/protocolRegistry.js";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../..");
const readRepositoryFile = relativePath => readFileSync(path.join(repositoryRoot, relativePath), "utf8");

function registeredProtocolIds(source, registrationPattern, implementationDirectory, implementationSuffix) {
  const registrations = [...source.matchAll(registrationPattern)].map(match => match[1]);
  const implementationFiles = readdirSync(path.join(repositoryRoot, implementationDirectory))
    .filter(name => name.endsWith(implementationSuffix));
  const implementations = new Map(implementationFiles.flatMap(name => {
    const implementation = readRepositoryFile(path.join(implementationDirectory, name));
    const type = implementation.match(/(?:public|internal) sealed class (\w+)/)?.[1];
    const protocolName = implementation.match(/public string Protocol => AcquisitionProtocols\.(\w+);/)?.[1];
    return type && protocolName ? [[type, protocolName]] : [];
  }));
  const constants = new Map([...readRepositoryFile("src/shared/Ingot.Contracts/Acquisition/AcquisitionConfigurationContracts.cs")
    .match(/public static class AcquisitionProtocols\s*\{([\s\S]*?)\n\}/)?.[1]
    ?.matchAll(/public const string (\w+) = "([^"]+)";/g) || []]
    .map(match => [match[1], match[2]]));
  return registrations.map(type => {
    assert.ok(implementations.has(type), `Registered protocol type ${type} must declare its protocol.`);
    assert.ok(constants.has(implementations.get(type)), `${type} must use a declared protocol constant.`);
    return constants.get(implementations.get(type));
  });
}

function sharedProtocolIds() {
  const contract = readRepositoryFile("src/shared/Ingot.Contracts/Acquisition/AcquisitionConfigurationContracts.cs");
  const body = contract.match(/public static class AcquisitionProtocols\s*\{([\s\S]*?)\n\}/)?.[1] || "";
  return [...body.matchAll(/public const string \w+ = "([^"]+)";/g)].map(match => match[1]);
}

test("shared, UI, Edge runner, and prober protocol registrations stay in sync", () => {
  const protocolIds = sharedProtocolIds();
  const edgeProgram = readRepositoryFile("src/edge/Ingot.Edge.ConnectorHost/Program.cs");
  const uiProtocolIds = protocolOptions.map(([id]) => id);
  const runnerIds = registeredProtocolIds(
    edgeProgram,
    /AddSingleton<IAcquisitionProtocolRunner,\s*(\w+)>\(\)/g,
    "src/edge/Ingot.Edge.ConnectorHost/Acquisition",
    "Runner.cs",
  );
  const proberIds = registeredProtocolIds(
    edgeProgram,
    /AddSingleton<IProtocolProber,\s*(\w+)>\(\)/g,
    "src/edge/Ingot.Edge.ConnectorHost/Acquisition/Probers",
    "ProtocolProber.cs",
  );
  const sorted = values => [...values].sort();

  assert.equal(new Set(protocolIds).size, protocolIds.length, "Shared protocol IDs must be unique.");
  assert.equal(protocolIds.length, 16);
  assert.deepEqual(sorted(uiProtocolIds), sorted(protocolIds));
  assert.deepEqual(sorted(runnerIds), sorted(protocolIds.filter(id => id !== "http-polling")));
  assert.deepEqual(sorted(proberIds), sorted(protocolIds));
  assert.match(edgeProgram, /AddHostedService<HttpPollingAcquisitionHostedService>\(\)/);
});

const validateHttpConnection = snapshotPath => protocolDescriptor("http-polling").validateConnection({
  baseUrl: "http://192.168.1.10",
  snapshotPath,
  pollIntervalMs: 1000,
});

test("HTTP polling accepts a relative snapshot path", () => {
  assert.equal(validateHttpConnection("/api/v1/snapshot").snapshotPath, undefined);
});

test("HTTP polling rejects absolute, protocol-relative, and multiline snapshot paths", () => {
  for (const snapshotPath of ["https://other.example/snapshot", "//other.example/snapshot", "/snapshot\nheader:value"]) {
    assert.match(validateHttpConnection(snapshotPath).snapshotPath, /安全路径/);
  }
});
