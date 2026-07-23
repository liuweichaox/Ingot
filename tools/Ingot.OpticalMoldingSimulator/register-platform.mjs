import {
  acquisitionProfile,
  analysisPlan,
  processDataModel,
  recipeVersion,
} from "./platform-configuration.mjs";

const protocols = ["http-polling", "mqtt", "opc-ua", "modbus-tcp"];
const valueArg = (name, fallback) => {
  const prefix = `--${name}=`;
  return process.argv.find((item) => item.startsWith(prefix))?.slice(prefix.length) || fallback;
};
const api = valueArg("api", "http://127.0.0.1:8000").replace(/\/$/, "");
const protocol = valueArg("protocol", "http-polling");
const edgeId = valueArg("edge", "EDGE-DEMO-001");
if (!protocols.includes(protocol)) {
  throw new Error(`--protocol must be one of: ${protocols.join(", ")}`);
}

async function get(path) {
  const response = await fetch(`${api}${path}`);
  if (!response.ok) throw new Error(`${path} ${response.status}: ${await response.text()}`);
  return response.json();
}

async function post(path, value) {
  const response = await fetch(`${api}${path}`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify(value),
  });
  if (!response.ok) throw new Error(`${path} ${response.status}: ${await response.text()}`);
  return response.json();
}

async function ensure(path, listPath, idName, value) {
  const payload = await get(listPath);
  const items = payload.data || payload;
  const found = items.find((item) =>
    item[idName] === value[idName] && item.version === value.version);
  if (!found) return post(path, value);
  if (found.status === "published") return found;
  return post(path, { ...found, ...value, status: "published" });
}

await ensure(
  "/api/v1/process-data-models",
  "/api/v1/process-data-models",
  "modelId",
  processDataModel,
);
await ensure(
  "/api/v1/recipe-versions",
  "/api/v1/recipe-versions",
  "recipeId",
  recipeVersion,
);
await ensure(
  "/api/v1/process-analysis-plans",
  "/api/v1/process-analysis-plans",
  "planId",
  analysisPlan,
);

const profilePayload = await get("/api/v1/acquisition-profiles");
const profiles = profilePayload.data || profilePayload;
const previousVersions = profiles
  .filter((item) => item.profileId === "optical-molding-simulator")
  .map((item) => item.version);
const version = Math.max(0, ...previousVersions) + 1;
const profile = acquisitionProfile(protocol, version, edgeId);
await post("/api/v1/acquisition-profiles", profile);

console.log(JSON.stringify({
  platform: api,
  edgeId,
  activeProfile: `${profile.profileId}@${profile.version}`,
  protocol,
  dataModel: `${processDataModel.modelId}@${processDataModel.version}`,
  recipe: `${recipeVersion.recipeId}@${recipeVersion.version}`,
}, null, 2));
