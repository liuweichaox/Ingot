import { createRouter, createWebHistory } from "vue-router";

const routes = [
  { path: "/", redirect: "/workbench" },
  {
    path: "/workbench",
    name: "workbench",
    component: () => import("../views/WorkbenchView.vue"),
  },
  {
    path: "/explorer",
    name: "object-explorer",
    component: () => import("../views/ObjectExplorerView.vue"),
  },
  {
    path: "/edges",
    name: "edges",
    component: () => import("../views/EdgesView.vue"),
  },
  {
    path: "/configuration/acquisition-profiles",
    name: "acquisition-profiles",
    component: () => import("../views/AcquisitionProfilesView.vue"),
  },
  {
    path: "/profiles",
    redirect: "/configuration/process-data-models",
  },
  {
    path: "/configuration/process-data-models",
    name: "process-data-models",
    component: () => import("../views/ProcessDataModelsView.vue"),
  },
  {
    path: "/configuration/recipe-versions",
    name: "recipe-versions",
    component: () => import("../views/RecipeVersionsView.vue"),
  },
  {
    path: "/configuration/process-analysis-plans",
    name: "process-analysis-plans",
    component: () => import("../views/ProcessAnalysisPlansView.vue"),
  },
  {
    path: "/production-setup",
    redirect: "/production/changeover",
  },
  {
    path: "/production/changeover",
    name: "production-changeover",
    component: () => import("../views/ProductionSetupView.vue"),
    props: { section: "context" },
  },
  {
    path: "/production/tooling-installations",
    name: "tooling-installations",
    component: () => import("../views/ProductionSetupView.vue"),
    props: { section: "installation" },
  },
  {
    path: "/configuration/component-types",
    name: "component-types",
    component: () => import("../views/ProductionSetupView.vue"),
    props: { section: "componentType" },
  },
  {
    path: "/configuration/components",
    name: "tooling-components",
    component: () => import("../views/ProductionSetupView.vue"),
    props: { section: "component" },
  },
  {
    path: "/configuration/tooling-types",
    name: "tooling-types",
    component: () => import("../views/ProductionSetupView.vue"),
    props: { section: "type" },
  },
  {
    path: "/configuration/tooling-assemblies",
    name: "tooling-assemblies",
    component: () => import("../views/ProductionSetupView.vue"),
    props: { section: "assembly" },
  },
  {
    path: "/quality-plans",
    redirect: "/configuration/quality-plans",
  },
  {
    path: "/configuration/inspection-definitions",
    name: "inspection-definitions",
    component: () => import("../views/InspectionDefinitionsView.vue"),
  },
  {
    path: "/configuration/quality-plans",
    name: "quality-plans",
    component: () => import("../views/QualityPlansView.vue"),
  },
  {
    path: "/cycles",
    name: "cycles",
    component: () => import("../views/CyclesView.vue"),
  },
  {
    path: "/events",
    name: "events",
    component: () => import("../views/EventsView.vue"),
  },
  {
    path: "/inspections",
    name: "inspections",
    component: () => import("../views/InspectionsView.vue"),
  },
  {
    path: "/quality-analysis",
    name: "quality-analysis",
    component: () => import("../views/QualityAnalysisView.vue"),
  },
  {
    path: "/data-quality",
    name: "data-quality",
    component: () => import("../views/DataQualityView.vue"),
  },
  {
    path: "/comparisons",
    name: "comparisons",
    component: () => import("../views/CycleComparisonView.vue"),
  },
  {
    path: "/chat",
    name: "chat",
    component: () => import("../views/ChatView.vue"),
  },
  {
    path: "/platform-metrics",
    name: "metrics",
    component: () => import("../views/MetricsView.vue"),
  },
  {
    path: "/logs",
    name: "logs",
    component: () => import("../views/LogsView.vue"),
  },
  {
    path: "/subscriptions",
    name: "subscriptions",
    component: () => import("../views/SubscriptionsView.vue"),
  },
];

export default createRouter({
  history: createWebHistory(),
  routes,
});
