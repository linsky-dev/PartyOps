import { flushPromises, mount } from "@vue/test-utils";
import ArcoVue from "@arco-design/web-vue";
import { beforeEach, describe, expect, it, vi } from "vitest";
import AccountView from "./AccountView.vue";

const { patch, post, deleteBody, replace, clearSession, session } = vi.hoisted(() => ({
  patch: vi.fn(),
  post: vi.fn(),
  deleteBody: vi.fn(),
  replace: vi.fn(),
  clearSession: vi.fn(),
  session: { user: { username: "staff", role: "staff" } },
}));
vi.mock("../api", () => ({ api: { patch, post, deleteBody } }));
vi.mock("../stores/session", () => ({ useSessionStore: () => ({ ...session, clearSession }) }));
vi.mock("vue-router", () => ({ useRouter: () => ({ replace }) }));

beforeEach(() => {
  vi.resetAllMocks();
  session.user.role = "staff";
});

describe("账号设置", () => {
  it("用户名更新成功后清除浏览器会话并回到登录页", async () => {
    patch.mockResolvedValue({});
    const wrapper = mount(AccountView, { global: { plugins: [ArcoVue] } });
    await wrapper.get('input[autocomplete="current-password"]').setValue("合成当前密码");
    await wrapper.get('input[autocomplete="off"]').setValue("new-user");
    await wrapper.get("form").trigger("submit");
    await flushPromises();
    expect(patch).toHaveBeenCalledWith("/auth/credentials", {
      current_password: "合成当前密码", username: "new-user", new_password: undefined,
    });
    expect(clearSession).toHaveBeenCalledOnce();
    expect(replace).toHaveBeenCalledWith("/login");
    expect(wrapper.text()).not.toContain("管理员离线恢复码");
    wrapper.unmount();
  });

  it("修改失败时保留已登录会话", async () => {
    patch.mockRejectedValue(new Error("验证失败"));
    const wrapper = mount(AccountView, { global: { plugins: [ArcoVue] } });
    await wrapper.get('input[autocomplete="current-password"]').setValue("合成错误密码");
    await wrapper.get('input[autocomplete="new-password"]').setValue("合成新密码");
    await wrapper.get("form").trigger("submit");
    await flushPromises();
    expect(patch).toHaveBeenCalledOnce();
    expect(clearSession).not.toHaveBeenCalled();
    expect(replace).not.toHaveBeenCalled();
    wrapper.unmount();
  });

  it("管理员凭当前密码生成离线码，结果只保留到关闭", async () => {
    session.user.role = "admin";
    post.mockResolvedValue({ credential: "synthetic-offline-code" });
    const wrapper = mount(AccountView, { global: { plugins: [ArcoVue] } });
    const forms = wrapper.findAll("form");
    await forms[1].get("input").setValue("合成管理员密码");
    await forms[1].trigger("submit");
    await flushPromises();
    expect(post).toHaveBeenCalledWith("/auth/recovery/offline", { current_password: "合成管理员密码" });
    expect(wrapper.get("code").text()).toBe("synthetic-offline-code");
    expect(forms[1].get("input").element.value).toBe("");
    const close = wrapper.findAll("button").find((button) => button.text() === "关闭恢复码");
    await close?.trigger("click");
    expect(wrapper.find("code").exists()).toBe(false);
    wrapper.unmount();
  });
});
