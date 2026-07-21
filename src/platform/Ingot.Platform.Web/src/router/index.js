import { createRouter, createWebHistory } from "vue-router";

const routes = [
  { path: "/", redirect: "/chat" },
  {
    path: "/edges",
    name: "edges",
    component: () => import("../views/EdgesView.vue"),
  },
  {
    path: "/profiles",
    name: "profiles",
    component: () => import("../views/ProfileConfigView.vue"),
  },
  {
    path: "/quality-plans",
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
    path: "/metrics",
    name: "metrics",
    component: () => import("../views/MetricsView.vue"),
  },
  {
    path: "/logs",
    name: "logs",
    component: () => import("../views/LogsView.vue"),
  },
];

export default createRouter({
  history: createWebHistory(),
  routes,
});
