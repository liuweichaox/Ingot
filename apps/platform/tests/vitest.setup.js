// 初始化前端渲染测试共享的 DOM 断言与清理行为。

import "@testing-library/jest-dom/vitest";
import React from "react";

globalThis.React = React;
