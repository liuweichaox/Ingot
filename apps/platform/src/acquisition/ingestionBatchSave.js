// Classify batch draft write outcomes before deciding whether a retry is safe.

export function classifyIngestionDraftWriteOutcome({
  expectedFingerprint,
  storedFingerprint = "",
  saveError = null,
  verificationError = null,
}) {
  if (storedFingerprint && storedFingerprint === expectedFingerprint)
    return { kind: "saved", error: "", verificationRequired: false };
  if (storedFingerprint)
    return {
      kind: "conflict",
      error: "服务器已有同编号版本，但内容与本次草稿不同；请核对后更换任务编号。",
      verificationRequired: false,
    };
  if (saveError?.status === 409)
    return {
      kind: "conflict",
      error: saveError.message || "服务器检测到任务版本冲突；请核对后更换任务编号。",
      verificationRequired: false,
    };
  if ([400, 422].includes(saveError?.status))
    return {
      kind: "validation",
      error: saveError.message || "服务器校验未通过；请检查配置后再保存。",
      verificationRequired: false,
    };
  if (saveError?.status >= 400 && saveError.status < 500 && saveError.status !== 429)
    return {
      kind: "rejected",
      error: saveError.message || `服务器拒绝了本次保存（状态 ${saveError.status}）。`,
      verificationRequired: false,
    };
  if (saveError?.status === 429)
    return {
      kind: "request",
      error: saveError.message || "请求过于频繁；稍后可以重试保存。",
      verificationRequired: false,
    };
  if (verificationError?.status === 404)
    return {
      kind: "request",
      error: saveError?.message || "服务器未找到此任务版本，可以重试保存。",
      verificationRequired: false,
    };
  const messages = [saveError?.message, verificationError?.message].filter(Boolean);
  return {
    kind: "unknown",
    error: `提交结果暂时无法核实${messages.length ? `：${messages.join("；")}` : ""}`,
    verificationRequired: true,
  };
}
