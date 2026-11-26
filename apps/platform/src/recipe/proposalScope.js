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
  const lower = characteristic?.lowerLimit;
  const upper = characteristic?.upperLimit;
  const hasLower = lower != null && lower !== "" && Number.isFinite(Number(lower));
  const hasUpper = upper != null && upper !== "" && Number.isFinite(Number(upper));
  if (hasLower && hasUpper && Number(lower) < Number(upper)) return { direction: "range", target: (Number(lower) + Number(upper)) / 2, lowerLimit: Number(lower), upperLimit: Number(upper) };
  if (hasLower && hasUpper) return null;
  if (hasUpper) return { direction: "minimize", target: Number(upper), upperLimit: Number(upper) };
  if (hasLower) return { direction: "maximize", target: Number(lower), lowerLimit: Number(lower) };
  return null;
}

export function runScopes(runs) {
  const groups = new Map();
  for (const run of runs) {
    const values = [run.productFamilyCode, run.productCode, run.equipmentId].map(value => String(value || "").trim());
    const key = JSON.stringify(values);
    const group = groups.get(key) || { key, values, count: 0 };
    group.count += 1;
    groups.set(key, group);
  }
  return [...groups.values()].sort((a, b) => b.count - a.count || a.key.localeCompare(b.key));
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

export function briefForRecipe({ specification, model, runs, characteristic, siteCode, selectedScope }) {
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
  const scopes = runScopes(scoped);
  if (!scopes.length) throw new Error("这一版配方没有已完成运行，无法确定工况。");
  const scope = selectedScope ? scopes.find(group => group.key === selectedScope) : scopes[0];
  if (!scope) throw new Error("所选工况已不在最近运行中，请重新选择工况。");
  const missingScopeFields = ["product_family_code", "product_code", "equipment_id"]
    .filter((_, index) => !scope.values[index]);
  const context = {
    product_family_code: scope?.values[0],
    product_code: scope?.values[1],
    equipment_id: scope?.values[2],
    missing_scope_fields: missingScopeFields.join(","),
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
      lowerLimit: objective.lowerLimit,
      upperLimit: objective.upperLimit,
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

// 列出决定之后开始的同配方同版本已完成运行；由工程师确认哪一条实际执行了这轮参数。
export function runsAfterDecision(runs, specificationId, version, decidedAt) {
  const decided = Date.parse(decidedAt || "");
  if (!Number.isFinite(decided) || !version) return [];
  return (runs || [])
    .filter(run => run?.processSpecificationId === specificationId && String(run.processSpecificationVersion) === String(version))
    .filter(run => {
      const started = Date.parse(run.startedAt || "");
      return Number.isFinite(started) && started > decided;
    })
    .sort((left, right) => Date.parse(left.startedAt) - Date.parse(right.startedAt));
}

const openStates = new Set(["pending-decision", "pending-execution", "pending-outcome"]);

export function proposalStillOpen(flow) {
  return openStates.has(flow?.state);
}
