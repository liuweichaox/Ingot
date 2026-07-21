<template>
  <el-config-provider :locale="zhCn">
    <el-container class="app-shell" :style="{ '--app-sidebar-width': sidebarWidth }">
      <button
        v-if="isMobile && !sidebarCollapsed"
        type="button"
        class="sidebar-backdrop"
        aria-label="关闭导航"
        @click="sidebarCollapsed = true"
      />

      <el-aside
        :width="sidebarWidth"
        class="app-sidebar"
        :class="{ 'is-collapsed': sidebarCollapsed, 'is-mobile': isMobile }"
      >
        <div class="sidebar-brand">
          <div class="brand-icon"><el-icon :size="22"><Box /></el-icon></div>
          <div v-show="!sidebarCollapsed" class="brand-copy">
            <strong>Ingot</strong>
            <span>制造数据平台</span>
          </div>
        </div>

        <nav class="sidebar-nav" aria-label="主导航">
          <div class="nav-section">
            <div v-show="!sidebarCollapsed" class="nav-section-label">日常工作</div>
            <el-menu
              :default-active="$route.path"
              :collapse="sidebarCollapsed"
              :collapse-transition="false"
              router
              class="sidebar-menu"
            >
              <el-menu-item index="/chat" aria-label="Ingot Chat">
                <el-icon><ChatDotRound /></el-icon>
                <template #title>Ingot Chat</template>
              </el-menu-item>
              <el-menu-item index="/cycles" aria-label="生产周期">
                <el-icon><List /></el-icon>
                <template #title>生产周期</template>
              </el-menu-item>
              <el-menu-item index="/inspections" aria-label="质量检验">
                <el-icon><DocumentChecked /></el-icon>
                <template #title>质量检验</template>
              </el-menu-item>
            </el-menu>
          </div>

          <div class="nav-section">
            <div v-show="!sidebarCollapsed" class="nav-section-label">分析与治理</div>
            <el-menu
              :default-active="$route.path"
              :collapse="sidebarCollapsed"
              :collapse-transition="false"
              router
              class="sidebar-menu"
            >
              <el-menu-item index="/comparisons" aria-label="历史对比">
                <el-icon><TrendCharts /></el-icon>
                <template #title>历史对比</template>
              </el-menu-item>
              <el-menu-item index="/data-quality" aria-label="数据质量">
                <el-icon><CircleCheck /></el-icon>
                <template #title>数据质量</template>
              </el-menu-item>
            </el-menu>
          </div>

          <div class="nav-section">
            <div v-show="!sidebarCollapsed" class="nav-section-label">配置管理</div>
            <el-menu
              :default-active="$route.path"
              :collapse="sidebarCollapsed"
              :collapse-transition="false"
              router
              class="sidebar-menu"
            >
              <el-menu-item index="/profiles" aria-label="工艺配置">
                <el-icon><Setting /></el-icon>
                <template #title>工艺配置</template>
              </el-menu-item>
              <el-menu-item index="/quality-plans" aria-label="质量方案">
                <el-icon><Finished /></el-icon>
                <template #title>质量方案</template>
              </el-menu-item>
              <el-menu-item index="/edges" aria-label="数据接入节点">
                <el-icon><Connection /></el-icon>
                <template #title>数据接入节点</template>
              </el-menu-item>
            </el-menu>
          </div>

          <div class="nav-section">
            <div v-show="!sidebarCollapsed" class="nav-section-label">系统运维</div>
            <el-menu
              :default-active="$route.path"
              :collapse="sidebarCollapsed"
              :collapse-transition="false"
              router
              class="sidebar-menu"
            >
              <el-menu-item index="/events" aria-label="事件查询">
                <el-icon><Tickets /></el-icon>
                <template #title>事件查询</template>
              </el-menu-item>
              <el-menu-item index="/metrics" aria-label="平台指标">
                <el-icon><DataAnalysis /></el-icon>
                <template #title>平台指标</template>
              </el-menu-item>
              <el-menu-item index="/logs" aria-label="运行日志">
                <el-icon><Document /></el-icon>
                <template #title>运行日志</template>
              </el-menu-item>
            </el-menu>
          </div>
        </nav>

        <button type="button" class="raw-metrics-link" aria-label="原始指标" @click="openMetrics">
          <el-icon><Link /></el-icon>
          <span v-show="!sidebarCollapsed">原始指标</span>
        </button>
      </el-aside>

      <el-container class="workspace-shell">
        <el-header class="workspace-header">
          <div class="page-heading">
            <el-button
              text
              circle
              :icon="sidebarCollapsed ? Expand : Fold"
              :aria-label="sidebarCollapsed ? '展开导航' : '收起导航'"
              @click="sidebarCollapsed = !sidebarCollapsed"
            />
            <div>
              <strong>{{ currentPage.title }}</strong>
              <span>{{ currentPage.description }}</span>
            </div>
          </div>
          <el-tag effect="plain" round>{{ currentPage.badge || "只读分析平台" }}</el-tag>
        </el-header>

        <el-main class="app-main">
          <router-view />
        </el-main>
      </el-container>
    </el-container>
  </el-config-provider>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, ref, watch } from "vue";
import { useRoute } from "vue-router";
import {
  Box,
  ChatDotRound,
  CircleCheck,
  Connection,
  DataAnalysis,
  Document,
  DocumentChecked,
  Expand,
  Fold,
  Finished,
  Link,
  List,
  Setting,
  TrendCharts,
  Tickets,
} from "@element-plus/icons-vue";
import zhCn from "element-plus/dist/locale/zh-cn.mjs";

const route = useRoute();
const isMobile = ref(window.innerWidth <= 800);
const sidebarCollapsed = ref(window.innerWidth <= 1100);

const pageDetails = {
  "/chat": { title: "Ingot Chat", description: "查询与分析已保存的生产数据" },
  "/cycles": { title: "生产周期", description: "按周期查看生产、数据质量与质检状态" },
  "/events": { title: "事件查询", description: "面向诊断与追溯查询原始生产事件" },
  "/inspections": { title: "质量检验", description: "处理视觉检查、人工质检与原图复核" },
  "/comparisons": { title: "历史对比", description: "比较同产品系列的完整模压周期" },
  "/data-quality": { title: "数据质量", description: "检查采样、阶段和生产信息完整性" },
  "/profiles": { title: "工艺配置", description: "配置采集、配方与阶段模型", badge: "配置管理" },
  "/quality-plans": { title: "质量方案", description: "配置产品适用的检测项目与复核规则", badge: "配置管理" },
  "/edges": { title: "数据接入节点", description: "查看现场数据适配器状态" },
  "/metrics": { title: "平台指标", description: "查看平台运行指标" },
  "/logs": { title: "运行日志", description: "查询平台运行记录" },
};

const currentPage = computed(() => pageDetails[route.path] || { title: "Ingot", description: "制造数据平台" });
const sidebarWidth = computed(() => {
  if (isMobile.value && sidebarCollapsed.value) return "0px";
  return sidebarCollapsed.value ? "72px" : "236px";
});

function handleResize() {
  const wasMobile = isMobile.value;
  isMobile.value = window.innerWidth <= 800;
  if (isMobile.value && !wasMobile) sidebarCollapsed.value = true;
}

function openMetrics() {
  window.open("/metrics", "_blank", "noopener,noreferrer");
}

watch(() => route.path, () => {
  if (isMobile.value) sidebarCollapsed.value = true;
});

onMounted(() => window.addEventListener("resize", handleResize));
onBeforeUnmount(() => window.removeEventListener("resize", handleResize));
</script>

<style scoped>
.app-shell {
  min-height: 100vh;
  background: #f5f7fa;
}

.app-sidebar {
  position: fixed;
  z-index: 40;
  top: 0;
  bottom: 0;
  left: 0;
  display: flex;
  overflow: hidden;
  flex-direction: column;
  border-right: 1px solid #e6eaf0;
  background: #fff;
  box-shadow: 4px 0 20px rgba(31, 48, 78, .035);
  transition: width .2s ease, transform .2s ease;
}

.sidebar-brand {
  display: flex;
  align-items: center;
  min-height: 72px;
  gap: 11px;
  padding: 0 18px;
  border-bottom: 1px solid #edf0f4;
}

.brand-icon {
  display: inline-flex;
  width: 36px;
  height: 36px;
  flex: 0 0 auto;
  align-items: center;
  justify-content: center;
  border-radius: 11px;
  color: #fff;
  background: linear-gradient(135deg, #409eff, #6878ff);
}

.brand-copy {
  display: grid;
  min-width: 130px;
  gap: 2px;
}

.brand-copy strong { color: #182238; font-size: 18px; }
.brand-copy span { color: #919bab; font-size: 11px; }
.sidebar-nav { flex: 1; overflow-y: auto; padding: 15px 10px; }
.nav-section + .nav-section { margin-top: 18px; }
.nav-section-label { padding: 0 12px 7px; color: #a0a8b5; font-size: 11px; font-weight: 600; letter-spacing: .08em; }
.sidebar-menu { border-right: 0; background: transparent; }
.sidebar-menu:not(.el-menu--collapse) { width: 216px; }

:deep(.sidebar-menu .el-menu-item) {
  height: 44px;
  margin: 2px 0;
  border-radius: 10px;
  color: #596476;
  line-height: 44px;
}

:deep(.sidebar-menu .el-menu-item:hover) { color: #2e6fac; background: #f2f7fd; }
:deep(.sidebar-menu .el-menu-item.is-active) { color: #2878c8; background: #eaf4ff; font-weight: 600; }
:deep(.sidebar-menu.el-menu--collapse) { width: 52px; }
:deep(.sidebar-menu.el-menu--collapse .el-menu-item) { justify-content: center; padding: 0 !important; }

.raw-metrics-link {
  display: flex;
  min-height: 44px;
  align-items: center;
  gap: 12px;
  margin: 8px 10px 14px;
  padding: 0 16px;
  border: 0;
  border-radius: 10px;
  color: #7d8796;
  background: transparent;
  cursor: pointer;
  white-space: nowrap;
}

.raw-metrics-link:hover { color: #2e6fac; background: #f2f7fd; }
.is-collapsed .raw-metrics-link { justify-content: center; padding: 0; }
.workspace-shell { min-width: 0; margin-left: var(--app-sidebar-width); transition: margin-left .2s ease; }
.workspace-header {
  position: sticky;
  z-index: 25;
  top: 0;
  display: flex;
  height: 64px !important;
  align-items: center;
  justify-content: space-between;
  padding: 0 24px;
  border-bottom: 1px solid #e5e9ef;
  background: rgba(255, 255, 255, .94);
  backdrop-filter: blur(12px);
}

.page-heading { display: flex; align-items: center; gap: 10px; }
.page-heading > div { display: grid; gap: 2px; }
.page-heading strong { color: #182238; font-size: 16px; }
.page-heading span { color: #8d97a7; font-size: 11px; }
.app-main { width: 100%; max-width: 1600px; margin: 0 auto; padding: 24px; }
.sidebar-backdrop { display: none; }

@media (max-width: 800px) {
  .app-sidebar.is-mobile { box-shadow: 10px 0 34px rgba(24, 38, 63, .16); }
  .app-sidebar.is-mobile.is-collapsed { transform: translateX(-100%); }
  .workspace-shell { margin-left: 0; }
  .workspace-header { padding: 0 12px; }
  .workspace-header > .el-tag { display: none; }
  .app-main { padding: 14px 10px; }
  .sidebar-backdrop {
    position: fixed;
    z-index: 35;
    inset: 0;
    display: block;
    border: 0;
    background: rgba(17, 28, 47, .28);
  }
}
</style>
