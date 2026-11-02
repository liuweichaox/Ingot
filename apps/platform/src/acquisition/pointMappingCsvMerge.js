// Merge imported point mappings by normalized process-variable code in linear time.

export function normalizePointMappingTarget(value) {
  return String(value ?? "").trim().toLocaleLowerCase("en");
}

export function mergePointMappingRows(existingRows, importedRows, providedColumns) {
  const next = [...existingRows];
  const indexByTarget = new Map();
  for (let index = 0; index < next.length; index += 1) {
    const target = normalizePointMappingTarget(next[index]?.dataItemCode);
    if (target && !indexByTarget.has(target)) indexByTarget.set(target, index);
  }

  for (let rowIndex = 0; rowIndex < importedRows.length; rowIndex += 1) {
    const imported = importedRows[rowIndex];
    const target = normalizePointMappingTarget(imported.dataItemCode);
    const existingIndex = target ? indexByTarget.get(target) : undefined;
    const updates = Object.fromEntries((providedColumns[rowIndex] || []).map(column => [column, imported[column]]));
    if (existingIndex !== undefined) {
      next[existingIndex] = {
        ...next[existingIndex],
        ...updates,
        dataItemCode: imported.dataItemCode,
        required: imported.required,
      };
    } else {
      if (target) indexByTarget.set(target, next.length);
      next.push(imported);
    }
  }

  return next;
}
