// 为生产配置路由选择对应的专用页面。
import { ProductionRecordsPage } from "./ProductionRecordsPage";
import { ToolingAssembliesPage } from "./ToolingAssembliesPage";

export function ProductionSetupPage({ section, canWrite = true }) {
  return section === "assembly"
    ? <ToolingAssembliesPage canWrite={canWrite} />
    : <ProductionRecordsPage key={section} section={section} canWrite={canWrite} />;
}
