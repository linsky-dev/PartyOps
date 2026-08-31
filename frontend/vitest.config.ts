import { defineConfig } from "vitest/config";
import vue from "@vitejs/plugin-vue";

export default defineConfig({
  plugins: [vue()],
  test: {
    include: ["src/**/*.test.ts"],
    environment: "jsdom",
    setupFiles: ["src/test/setup.ts"],
    // 发布机可能同时驻留本地模型。单 Worker 避免 V8 并发预留耗尽
    // Windows 提交上限，导致用例未执行便出现 IPC/VirtualAlloc 失败。
    minWorkers: 1,
    maxWorkers: 1,
    // 路由用例会真实加载全部懒加载页面；低性能 Win7 构建机上不应因默认 10 秒误报。
    hookTimeout: 30_000,
    coverage: {
      provider: "v8",
      // 发布门槛必须统计所有业务页面、组件、路由和状态管理，不能只统计
      // 已有测试的少量文件而形成虚假的 100% 覆盖率。
      include: ["src/**/*.{ts,vue}"],
      exclude: ["src/**/*.test.ts", "src/**/*.d.ts", "src/main.ts"],
      reporter: ["text", "html", "json-summary"],
      thresholds: {
        // 以 rc.3 全量真实行为测试达到的稳定峰值为基线，禁止后续用例或功能回退。
        // Vue 模板会生成大量框架函数，因此函数门禁按可重复达到的实测值单独设置。
        lines: 95,
        functions: 46,
        statements: 95,
        branches: 93,
      },
    },
  },
});
