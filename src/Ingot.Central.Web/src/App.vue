<template>
  <el-config-provider :locale="zhCn">
    <el-container class="app-container">
      <el-header class="app-header">
        <div class="header-content">
          <div class="logo">
            <el-icon :size="24"><Box /></el-icon>
            <span class="title">Ingot 工厂数据与工艺分析平台</span>
          </div>
          <el-menu
            mode="horizontal"
            :default-active="$route.path"
            router
            class="header-menu"
          >
            <el-menu-item index="/edges">
              <el-icon><Connection /></el-icon>
              <span>数据接入节点</span>
            </el-menu-item>
            <el-menu-item index="/events">
              <el-icon><List /></el-icon>
              <span>生产事件</span>
            </el-menu-item>
            <el-menu-item index="/inspections">
              <el-icon><DocumentChecked /></el-icon>
              <span>检测录入</span>
            </el-menu-item>
            <el-menu-item index="/chat">
              <el-icon><ChatDotRound /></el-icon>
              <span>Ingot Chat</span>
            </el-menu-item>
            <el-menu-item index="/metrics">
              <el-icon><DataAnalysis /></el-icon>
              <span>平台指标</span>
            </el-menu-item>
            <el-menu-item index="/logs">
              <el-icon><Document /></el-icon>
              <span>运行日志</span>
            </el-menu-item>
            <el-menu-item index="/metrics" @click="openMetrics">
              <el-icon><Link /></el-icon>
              <span>原始指标</span>
            </el-menu-item>
          </el-menu>
        </div>
      </el-header>
      <el-main class="app-main">
        <router-view />
      </el-main>
    </el-container>
  </el-config-provider>
</template>

<script setup>
import { Box, ChatDotRound, Connection, DataAnalysis, Document, DocumentChecked, Link, List } from "@element-plus/icons-vue";
import zhCn from "element-plus/dist/locale/zh-cn.mjs";
const openMetrics = () => {
  window.open("/metrics", "_blank");
};
</script>

<style scoped>
.app-container {
  min-height: 100vh;
  background: #f5f7fa;
}

.app-header {
  background: #fff;
  border-bottom: 1px solid #e4e7ed;
  padding: 0;
  height: 64px !important;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.05);
}

.header-content {
  display: flex;
  align-items: center;
  justify-content: space-between;
  height: 100%;
  max-width: 1600px;
  margin: 0 auto;
  padding: 0 24px;
}

.logo {
  display: flex;
  align-items: center;
  gap: 12px;
  color: #409eff;
  font-weight: 600;
  font-size: 18px;
}

.title {
  user-select: none;
}

.header-menu {
  border-bottom: none;
  background: transparent;
}

.header-menu .el-menu-item {
  height: 64px;
  line-height: 64px;
}

.app-main {
  max-width: 1600px;
  margin: 0 auto;
  padding: 24px;
  width: 100%;
}

:deep(.el-menu--horizontal .el-menu-item) {
  border-bottom: 2px solid transparent;
}

:deep(.el-menu--horizontal .el-menu-item.is-active) {
  border-bottom-color: #409eff;
  color: #409eff;
}
</style>
