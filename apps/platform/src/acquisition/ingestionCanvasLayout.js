// Build a browser layout key only for a site-resolved, existing task version.
export function ingestionCanvasLayoutStorageKey({
  taskId,
  version,
  mode,
  siteId,
  siteResolved,
  siteError,
  edgeScopeMismatch,
  isNew,
}) {
  const taskVersion = Number(version);
  if (isNew || mode !== "maintain" || !siteResolved || siteError || edgeScopeMismatch ||
      !siteId || !taskId || !Number.isInteger(taskVersion) || taskVersion < 1)
    return "";
  return `ingot:acquisition-canvas-layout:v1:${encodeURIComponent(siteId)}:${encodeURIComponent(taskId)}:v${taskVersion}`;
}
