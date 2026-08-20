// 组装当前公开路由页面，不在展示层声明未经验证的产品结论。

"use client";

import IngotSite from "../IngotSite";

export default function EnglishHome() {
  return <IngotSite initialLocale="en" />;
}
