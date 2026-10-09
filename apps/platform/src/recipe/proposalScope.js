// 下一版提案的范围来自已发布配方和它已经完成的运行，不在页面上另填一套条件。

export function runsForRecipe(runs, specification) {
  return runs.filter(run =>
    run?.processSpecificationId === specification.processSpecificationId &&
    String(run.processSpecificationVersion) === String(specification.version));
}

export function dominant(runs, key) {
  const counts = new Map();
  for (const run of runs) {
    const value = String(run?.[key] || "").trim();
    if (!value) continue;
    counts.set(value, (counts.get(value) || 0) + 1);
  }
  return [...counts.entries()].sort((left, right) => right[1] - left[1] || left[0].localeCompare(right[0]))[0]?.[0] || "";
}

export function inferObjective(characteristic) {
  const lower = Number(characteristic?.lowerLimit);
  const upper = Number(characteristic?.upperLimit);
  const hasLower = Number.isFinite(lower);
  const hasUpper = Number.isFinite(upper);
  if (hasLower && lower === 0 && hasUpper) return { direction: "minimize", target: 0 };
  if (hasLower && hasUpper) return { direction: "target", target: (lower + upper) / 2 };
  if (hasUpper) return { direction: "minimize", target: upper };
  if (hasLower) return { direction: "maximize", target: lower };
  return null;
}

function planCovers(plan, specification) {
  const scoped = plan?.scope?.processSpecificationId;
  return !scoped || scoped === specification.processSpecificationId;
}

export function chooseCharacteristic(plans, definitions, specification) {
  const published = (plans || []).filter(plan => plan?.status === "published" && planCovers(plan, specification));
  const specific = published.filter(plan => plan?.scope?.processSpecificationId === specification.processSpecificationId);
  const chosen = specific.length ? specific : published;
  for (const plan of chosen) {
    for (const item of plan.items || []) {
      const definition = (definitions || []).find(candidate =>
        candidate.code === item.definitionCode &&
        Number(candidate.version) === Number(item.definitionVersion || candidate.version));
      for (const characteristic of definition?.characteristics || []) {
        if ((characteristic.inputType || "numeric") !== "numeric") continue;
        if (!inferObjective(characteristic)) continue;
        return { ...characteristic, definitionName: definition.name || definition.code };
      }
    }
  }
  return null;
}

export function briefForRecipe({ specification, model, runs, characteristic, siteCode }) {
  if (!siteCode) throw new Error("还没有登记现场节点，也没有该配方的已完成运行。");
  if (!characteristic) throw new Error("已发布质量方案里没有可用于这版配方的数值质量指标。");
  const objective = inferObjective(characteristic);
  if (!objective || !characteristic.unit) throw new Error("质量指标需要单位和判定范围，才能作为优化目标。");
  const variables = (model?.controlParameters || [])
    .filter(item => ["double", "integer"].includes(item.dataType || "double") && item.changeAllowed !== false)
    .map(item => ({
      code: item.code,
      name: item.displayName || item.code,
      role: "control",
      unit: item.unit || "",
      lowerLimit: Number(item.minimum),
      upperLimit: Number(item.maximum),
    }));
  if (!variables.length || variables.some(item => !item.unit || !Number.isFinite(item.lowerLimit) || !Number.isFinite(item.upperLimit) || item.lowerLimit >= item.upperLimit)) {
    throw new Error("这版配方没有带单位和上下界的可调参数。");
  }
  const scoped = runsForRecipe(runs, specification);
  const context = {
    product_family_code: dominant(scoped, "productFamilyCode"),
    product_code: dominant(scoped, "productCode"),
    equipment_id: dominant(scoped, "equipmentId"),
    process_specification_version: String(specification.version),
    lookback_days: "365",
  };
  return {
    siteCode,
    processSpecificationId: specification.processSpecificationId,
    name: `${specification.processSpecificationId} 第 ${specification.version} 版`,
    objectives: [{
      code: characteristic.code,
      name: characteristic.name || characteristic.code,
      unit: characteristic.unit,
      direction: objective.direction,
      target: objective.target,
      dataSource: `inspection:${characteristic.code}`,
    }],
    variables,
    context: Object.fromEntries(Object.entries(context).filter(([, value]) => value)),
  };
}

export function revisionOverrides(specification, parameters) {
  const current = new Map((specification?.values || []).map(item => [item.code, item.value]));
  return (parameters || []).flatMap(parameter => {
    const value = parameter.value;
    if (String(current.get(parameter.variableCode) ?? "") === String(value ?? "")) return [];
    return [{
      code: parameter.variableCode,
      value: String(value),
      dataType: parameter.dataType || "double",
    }];
  });
}

export function nextRunAfterDecision(runs, specificationId, version, decidedAt) {
  const decided = Date.parse(decidedAt || "");
  if (!Number.isFinite(decided) || !version) return null;
  return (runs || [])
    .filter(run => run?.processSpecificationId === specificationId && String(run.processSpecificationVersion) === String(version))
    .filter(run => {
      const started = Date.parse(run.startedAt || "");
      return Number.isFinite(started) && started > decided;
    })
    .sort((left, right) => Date.parse(left.startedAt) - Date.parse(right.startedAt))[0] || null;
}

const openStates = new Set(["pending-decision", "pending-execution", "pending-outcome"]);

export function proposalStillOpen(flow) {
  return openStates.has(flow?.state);
}
