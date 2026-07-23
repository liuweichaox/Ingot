<template>
  <el-config-provider :locale="zhCn">
    <div class="app-shell">
      <header class="global-header">
        <button type="button" class="brand" aria-label="返回工作台" @click="router.push('/workbench')">
          <span class="brand-icon"><img src="/ingot-mark.svg" alt=""></span>
          <span class="brand-copy"><strong>Ingot</strong><small>制造数据平台</small></span>
        </button>

        <nav class="global-nav" aria-label="全局导航">
          <button
            v-for="section in navigationSections"
            :key="section.id"
            type="button"
            :class="{ active: currentSection.id === section.id }"
            :aria-current="currentSection.id === section.id ? 'page' : undefined"
            @click="openSection(section)"
          >
            <el-icon><component :is="section.icon" /></el-icon>
            <span>{{ section.label }}</span>
          </button>
        </nav>

        <button type="button" class="global-search" @click="router.push('/explorer')">
          <el-icon><Search /></el-icon><span>搜索数据</span>
        </button>
      </header>

      <div class="body-shell" :class="{ 'without-context-nav': !hasContextNavigation }">
        <button
          v-if="isMobile && contextNavigationOpen"
          type="button"
          class="context-backdrop"
          aria-label="关闭导航"
          @click="contextNavigationOpen = false"
        />

        <aside
          v-if="hasContextNavigation"
          class="context-sidebar"
          :class="{ 'is-open': contextNavigationOpen }"
        >
          <div class="context-title">
            <el-icon><component :is="currentSection.icon" /></el-icon>
            <strong>{{ currentSection.label }}</strong>
          </div>
          <nav aria-label="当前模块导航">
            <RouterLink
              v-for="item in currentSection.items"
              :key="item.path"
              :to="item.path"
              :class="{ active: route.path === item.path }"
              @click="contextNavigationOpen = false"
            >
              <span>{{ item.label }}</span>
            </RouterLink>
          </nav>
        </aside>

        <section class="workspace-shell">
          <header class="workspace-header">
            <button
              v-if="isMobile && hasContextNavigation"
              type="button"
              class="context-trigger"
              aria-label="打开当前模块导航"
              @click="contextNavigationOpen = true"
            >
              <el-icon><Menu /></el-icon>
            </button>
            <div class="page-heading">
              <strong>{{ currentPage.title }}</strong>
              <span>{{ currentPage.description }}</span>
            </div>
          </header>

          <main class="app-main">
            <router-view />
          </main>
        </section>
      </div>
    </div>
  </el-config-provider>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import {
  ChatDotRound,
  DataAnalysis,
  DocumentChecked,
  FolderOpened,
  Guide,
  HomeFilled,
  Menu,
  Search,
  Setting,
  Tools,
} from "@element-plus/icons-vue";
import zhCn from "element-plus/dist/locale/zh-cn.mjs";

const route = useRoute();
const router = useRouter();
const isMobile = ref(window.innerWidth <= 900);
const contextNavigationOpen = ref(false);

const navigationSections = [
  { id: "workbench", label: "工作台", icon: HomeFilled, path: "/workbench", items: [] },
  { id: "chat", label: "AI 助手", icon: ChatDotRound, path: "/chat", items: [] },
  {
    id: "operations", label: "运行与追溯", icon: Guide, path: "/cycles", items: [
      { path: "/cycles", label: "运行记录" },
      { path: "/events", label: "生产事件" },
      { path: "/production/changeover", label: "生产切换" },
      { path: "/production/tooling-installations", label: "工装装卸" },
    ],
  },
  {
    id: "quality", label: "质量管理", icon: DocumentChecked, path: "/inspections", items: [
      { path: "/inspections", label: "质量任务" },
      { path: "/quality-analysis", label: "质量分析" },
      { path: "/configuration/inspection-definitions", label: "检测定义" },
      { path: "/configuration/quality-plans", label: "质量方案" },
    ],
  },
  {
    id: "analysis", label: "分析中心", icon: DataAnalysis, path: "/comparisons", items: [
      { path: "/comparisons", label: "历史对比" },
      { path: "/data-quality", label: "数据健康" },
      { path: "/configuration/process-analysis-plans", label: "分析方案" },
    ],
  },
  {
    id: "data", label: "数据资产", icon: FolderOpened, path: "/explorer", items: [
      { path: "/explorer", label: "对象目录" },
      { path: "/configuration/process-data-models", label: "工艺数据模型" },
      { path: "/configuration/recipe-versions", label: "配方版本" },
      { path: "/configuration/acquisition-profiles", label: "采集任务" },
      { path: "/edges", label: "采集节点" },
    ],
  },
  {
    id: "tooling", label: "工装管理", icon: Tools, path: "/configuration/components", items: [
      { path: "/configuration/component-types", label: "组件类型" },
      { path: "/configuration/components", label: "组件台账" },
      { path: "/configuration/tooling-types", label: "工装类型" },
      { path: "/configuration/tooling-assemblies", label: "工装组合" },
    ],
  },
  {
    id: "administration", label: "系统管理", icon: Setting, path: "/platform-metrics", items: [
      { path: "/platform-metrics", label: "平台指标" },
      { path: "/subscriptions", label: "事件订阅" },
      { path: "/logs", label: "运行日志" },
    ],
  },
];

const pageDetails = {
  "/workbench": { title: "工作台", description: "生产、质量与数据状态" },
  "/chat": { title: "Ingot Chat", description: "查询与分析已保存的生产数据" },
  "/explorer": { title: "对象目录", description: "从运行对象进入数据、上下文与关联关系" },
  "/cycles": { title: "运行记录", description: "查看生产周期及其数据、工艺与质量上下文" },
  "/production/changeover": { title: "生产切换", description: "让设备、产品、配方和已装工装对接下来的周期生效" },
  "/production/tooling-installations": { title: "工装装卸", description: "记录工装组合版本在设备上的装入与卸下区间" },
  "/configuration/component-types": { title: "组件类型", description: "配置组件台账的分类来源" },
  "/configuration/components": { title: "组件台账", description: "登记可更换、复用和追溯的物理组件" },
  "/configuration/tooling-types": { title: "工装类型", description: "配置装配位置及允许的组件类型" },
  "/configuration/tooling-assemblies": { title: "工装组合", description: "维护工装身份与不可变组件组合版本" },
  "/events": { title: "生产事件", description: "查询、追溯并关联运行上下文" },
  "/inspections": { title: "质量任务", description: "处理视觉检查、人工质检与原图复核" },
  "/quality-analysis": { title: "质量分析", description: "按产品、配方、运行对象和分析范围查看质量结果" },
  "/comparisons": { title: "历史对比", description: "比较同类生产周期、运行段或时间窗口" },
  "/data-quality": { title: "数据健康", description: "检查运行对象的数据范围、采样连续性与周期完整性" },
  "/configuration/process-data-models": { title: "工艺数据模型", description: "定义采集数据项、配方参数结构和工艺阶段" },
  "/configuration/recipe-versions": { title: "配方版本", description: "维护引用数据模型的完整配方有效值" },
  "/configuration/acquisition-profiles": { title: "采集任务", description: "管理数据源连接、采集对象、字段映射与发布版本" },
  "/configuration/process-analysis-plans": { title: "分析方案", description: "配置分析范围、对齐方式、质量分组和数据项" },
  "/configuration/inspection-definitions": { title: "检测定义", description: "定义要检测的特性、录入类型和判定规则" },
  "/configuration/quality-plans": { title: "质量方案", description: "配置产品适用的检测项目与复核规则" },
  "/edges": { title: "采集节点", description: "查看现场采集节点及运行状态" },
  "/platform-metrics": { title: "平台指标", description: "查看平台与边缘节点运行指标" },
  "/subscriptions": { title: "事件订阅", description: "维护向外部系统投递的事件订阅" },
  "/logs": { title: "运行日志", description: "查询平台运行记录" },
};

const currentSection = computed(() => navigationSections.find(section => (
  route.path === section.path || section.items.some(item => item.path === route.path)
)) || navigationSections[0]);
const currentPage = computed(() => pageDetails[route.path] || { title: "Ingot", description: "制造数据平台" });
const hasContextNavigation = computed(() => currentSection.value.items.length > 0);

function openSection(section) {
  contextNavigationOpen.value = false;
  router.push(section.path);
}

function handleResize() {
  isMobile.value = window.innerWidth <= 900;
  if (!isMobile.value) contextNavigationOpen.value = false;
}

watch(() => route.path, () => { contextNavigationOpen.value = false; });
onMounted(() => window.addEventListener("resize", handleResize));
onBeforeUnmount(() => window.removeEventListener("resize", handleResize));
</script>

<style scoped>
.app-shell { min-height: 100vh; background: #f5f7fa; }
.global-header {
  position: fixed;
  z-index: 60;
  inset: 0 0 auto;
  display: flex;
  height: 60px;
  align-items: stretch;
  border-bottom: 1px solid #e5e9ef;
  background: rgba(255, 255, 255, .97);
  backdrop-filter: blur(12px);
}
.brand {
  display: flex;
  width: 216px;
  flex: 0 0 216px;
  align-items: center;
  gap: 10px;
  padding: 0 18px;
  border: 0;
  border-right: 1px solid #edf0f4;
  background: transparent;
  cursor: pointer;
  text-align: left;
}
.brand-icon { display: inline-flex; width: 34px; height: 34px; align-items: center; justify-content: center; border-radius: 9px; background: #fff7e6; box-shadow: inset 0 0 0 1px rgba(232, 137, 26, .18); }
.brand-icon img { display: block; width: 27px; height: 27px; }
.brand-copy { display: grid; gap: 1px; }
.brand-copy strong { color: #182238; font-size: 17px; }
.brand-copy small { color: #919bab; font-size: 10px; }
.global-nav { display: flex; min-width: 0; flex: 1; align-items: stretch; overflow-x: auto; scrollbar-width: none; }
.global-nav::-webkit-scrollbar { display: none; }
.global-nav button, .global-search {
  position: relative;
  display: inline-flex;
  flex: 0 0 auto;
  align-items: center;
  justify-content: center;
  gap: 6px;
  padding: 0 15px;
  border: 0;
  background: transparent;
  color: #5e697a;
  cursor: pointer;
  font: inherit;
  font-size: 13px;
}
.global-nav button::after { position: absolute; right: 16px; bottom: 0; left: 16px; height: 2px; border-radius: 2px 2px 0 0; background: transparent; content: ""; }
.global-nav button:hover, .global-nav button.active { color: #2878c8; background: #f7faff; }
.global-nav button.active { font-weight: 600; }
.global-nav button.active::after { background: #2878c8; }
.global-search { min-width: 112px; border-left: 1px solid #edf0f4; }
.global-search:hover { color: #2878c8; background: #f7faff; }
.body-shell { min-height: 100vh; padding-top: 60px; }
.context-sidebar {
  position: fixed;
  z-index: 40;
  top: 60px;
  bottom: 0;
  left: 0;
  width: 216px;
  border-right: 1px solid #e6eaf0;
  background: #fff;
}
.context-title { display: flex; height: 64px; align-items: center; gap: 9px; padding: 0 20px; border-bottom: 1px solid #edf0f4; color: #27344a; }
.context-title strong { font-size: 14px; }
.context-sidebar nav { display: grid; gap: 3px; padding: 14px 10px; }
.context-sidebar a { display: flex; height: 40px; align-items: center; padding: 0 15px; border-radius: 8px; color: #5d6879; font-size: 13px; text-decoration: none; }
.context-sidebar a:hover { color: #2e6fac; background: #f3f7fc; }
.context-sidebar a.active { color: #2878c8; background: #eaf4ff; font-weight: 600; }
.workspace-shell { min-width: 0; margin-left: 216px; }
.without-context-nav .workspace-shell { margin-left: 0; }
.workspace-header {
  position: sticky;
  z-index: 25;
  top: 60px;
  display: flex;
  height: 64px;
  align-items: center;
  gap: 10px;
  padding: 0 24px;
  border-bottom: 1px solid #e5e9ef;
  background: rgba(255, 255, 255, .94);
  backdrop-filter: blur(12px);
}
.page-heading { display: grid; gap: 2px; }
.page-heading strong { color: #182238; font-size: 16px; }
.page-heading span { color: #8d97a7; font-size: 11px; }
.context-trigger { display: none; width: 34px; height: 34px; align-items: center; justify-content: center; border: 0; border-radius: 8px; background: transparent; color: #5e697a; }
.app-main { width: 100%; max-width: 1600px; margin: 0 auto; padding: 24px; }
.context-backdrop { display: none; }

@media (max-width: 1180px) {
  .brand { width: 176px; flex-basis: 176px; }
  .global-nav button { padding: 0 11px; }
  .global-nav button .el-icon { display: none; }
}

@media (max-width: 900px) {
  .global-header { height: 56px; }
  .brand { width: 62px; flex-basis: 62px; justify-content: center; padding: 0; }
  .brand-copy { display: none; }
  .global-nav button { padding: 0 12px; }
  .global-nav button span { white-space: nowrap; }
  .global-search { min-width: 48px; padding: 0 14px; }
  .global-search span { display: none; }
  .body-shell { padding-top: 56px; }
  .context-sidebar { z-index: 55; top: 56px; transform: translateX(-100%); box-shadow: 10px 0 34px rgba(24, 38, 63, .16); transition: transform .2s ease; }
  .context-sidebar.is-open { transform: translateX(0); }
  .workspace-shell { margin-left: 0; }
  .workspace-header { top: 56px; height: 60px; padding: 0 12px; }
  .context-trigger { display: inline-flex; flex: 0 0 auto; }
  .app-main { padding: 14px 10px; }
  .context-backdrop { position: fixed; z-index: 50; inset: 56px 0 0; display: block; border: 0; background: rgba(17, 28, 47, .28); }
}
</style>
