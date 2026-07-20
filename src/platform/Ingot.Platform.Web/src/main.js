import { createApp } from "vue";
import App from "./App.vue";
import router from "./router";
import {
  ElAlert,
  ElButton,
  ElButtonGroup,
  ElCard,
  ElCol,
  ElCollapse,
  ElCollapseItem,
  ElConfigProvider,
  ElContainer,
  ElDialog,
  ElEmpty,
  ElForm,
  ElFormItem,
  ElHeader,
  ElIcon,
  ElInput,
  ElLink,
  ElLoading,
  ElMain,
  ElMenu,
  ElMenuItem,
  ElOption,
  ElPagination,
  ElPopover,
  ElRow,
  ElSelect,
  ElStatistic,
  ElSwitch,
  ElTable,
  ElTableColumn,
  ElTag,
  ElText,
  ElTimeline,
  ElTimelineItem,
} from "element-plus";
import "element-plus/dist/index.css";
import "./styles/global.css";

const app = createApp(App);

[
  ElAlert,
  ElButton,
  ElButtonGroup,
  ElCard,
  ElCol,
  ElCollapse,
  ElCollapseItem,
  ElConfigProvider,
  ElContainer,
  ElDialog,
  ElEmpty,
  ElForm,
  ElFormItem,
  ElHeader,
  ElIcon,
  ElInput,
  ElLink,
  ElMain,
  ElMenu,
  ElMenuItem,
  ElOption,
  ElPagination,
  ElPopover,
  ElRow,
  ElSelect,
  ElStatistic,
  ElSwitch,
  ElTable,
  ElTableColumn,
  ElTag,
  ElText,
  ElTimeline,
  ElTimelineItem,
].forEach((component) => app.use(component));

app.use(router);
app.use(ElLoading);

app.mount("#app");
