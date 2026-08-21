
export const platformRoleOptions = [
  ["platform.admin", "平台管理员", "用户、权限和系统配置"],
  ["quality.inspector", "质量检验员", "质量结果录入"],
  ["quality.reviewer", "质量复核员", "质量结果复核"],
  ["process.engineer", "工艺工程师", "分析、调查、模型和改进建议"],
];

const roleLabels = new Map(platformRoleOptions.map(([role, label]) => [role, label]));

export function formatRoleSummary(roles) {
  return (roles || []).map(role => roleLabels.get(role) || role).join("、") || "未分配岗位";
}

export function formatSiteScope(siteIds, roles) {
  if ((roles || []).includes("platform.admin")) return "全部站点（管理员）";
  return (siteIds || []).join("、") || "未授权生产站点";
}
