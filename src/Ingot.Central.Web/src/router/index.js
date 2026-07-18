import { createRouter, createWebHistory } from "vue-router";

const routes = [
  { path: "/", redirect: "/chat" },
  {
    path: "/edges",
    name: "edges",
    component: () => import("../views/EdgesView.vue"),
  },
  {
    path: "/events",
    name: "events",
    component: () => import("../views/EventsView.vue"),
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
