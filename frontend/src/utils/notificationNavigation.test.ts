import { beforeEach, describe, expect, it, vi } from "vitest";
import type { Router } from "vue-router";
import type { NotificationItem, NotificationResolution } from "../types";
import { focusNotificationTarget, noticeDialog, notificationLocation, openNotification } from "./notificationNavigation";

const { get, post } = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }));
vi.mock("../api", () => ({ api: { get, post } }));

const notice: NotificationItem = {
  id: "notice-1",
  notification_type: "comment",
  title: "事项评论",
  body: "合成通知正文",
  entity_type: "task",
  entity_id: "task-1",
  read_at: null,
  created_at: "2026-11-19T00:00:00Z",
};
const child = {
  kind: "task",
  id: "task-1",
  section: "comments",
  child_id: "comment-1",
  archived: false,
} as const satisfies NonNullable<NotificationResolution["target"]>;
const push = vi.fn();
const router = { push } as unknown as Router;

beforeEach(() => {
  vi.resetAllMocks();
  noticeDialog.value = null;
});

describe("通知进入业务对象", () => {
  it("标为已读后使用服务端确认的评论目标", async () => {
    post.mockResolvedValue({ read_at: "2026-11-19T01:00:00Z" });
    get.mockResolvedValue({ status: "ready", target: child });
    const item = { ...notice };
    await openNotification(router, item);
    expect(post).toHaveBeenCalledWith("/notifications/notice-1/read");
    expect(get).toHaveBeenCalledWith("/notifications/notice-1/target");
    expect(push).toHaveBeenCalledWith({ path: "/tasks/task-1", query: { noticeTarget: "comment-1", noticeSection: "comments" } });
    expect(item.read_at).toBe("2026-11-19T01:00:00Z");
  });

  it("撤销时展示原文和仍可访问的关联对象", async () => {
    get.mockResolvedValue({ status: "revoked", target: { ...child, child_id: null } });
    await openNotification(router, { ...notice, read_at: notice.created_at });
    expect(push).not.toHaveBeenCalled();
    expect(noticeDialog.value).toMatchObject({
      item: { body: notice.body },
      destination: { path: "/tasks/task-1", query: { noticeTarget: "task-1" } },
    });
  });

  it("失效目标不会从通知原对象字段推断可访问路径", async () => {
    get.mockResolvedValue({ status: "unavailable", target: null });
    await openNotification(router, { ...notice, read_at: notice.created_at });
    expect(push).not.toHaveBeenCalled();
    expect(noticeDialog.value?.destination).toBeUndefined();
    expect(noticeDialog.value?.item.body).toBe(notice.body);
  });

  it("归档目标仅在用户查看关联对象时提供入口", async () => {
    get.mockResolvedValue({ status: "archived", target: { ...child, child_id: null, archived: true } });
    await openNotification(router, { ...notice, read_at: notice.created_at });
    expect(push).not.toHaveBeenCalled();
    expect(noticeDialog.value?.message).toContain("已归档");
    expect(noticeDialog.value?.destination).toMatchObject({ path: "/tasks/task-1" });
  });

  it("定位请求失败后可重试，不重复写入已读", async () => {
    post.mockResolvedValue({ read_at: notice.created_at });
    get.mockRejectedValueOnce(new TypeError("离线")).mockResolvedValueOnce({ status: "ready", target: child });
    await openNotification(router, { ...notice });
    expect(noticeDialog.value?.retry).toBeTypeOf("function");
    await noticeDialog.value?.retry?.();
    expect(post).toHaveBeenCalledTimes(1);
    expect(push).toHaveBeenCalledTimes(1);
  });

  it("按目标类型构造归档时间轴、传输、审批和共享文件入口", () => {
    expect(notificationLocation({ kind: "development", id: "case", child_id: "milestone", section: "timeline", archived: true }))
      .toEqual({ path: "/party-development", query: { tab: "cases", noticeCase: "case", noticeTarget: "milestone", noticeArchived: "1" } });
    expect(notificationLocation({ kind: "transfer", id: "transfer", child_id: null, section: "inbox", archived: false }))
      .toEqual({ path: "/fleet/inbox", query: { noticeTarget: "transfer" } });
    expect(notificationLocation({ kind: "transfer", id: "transfer", child_id: null, section: "transfers", archived: false }))
      .toEqual({ path: "/fleet/transfers", query: { noticeTarget: "transfer" } });
    expect(notificationLocation({ kind: "root_approval", id: "root", child_id: null, section: "grants", archived: false }))
      .toEqual({ path: "/fleet/grants", query: { noticeTarget: "root" } });
    expect(notificationLocation({ kind: "workspace", id: "root", child_id: "file", section: "files", archived: false }))
      .toEqual({ path: "/workspace", query: { noticeRoot: "root", noticeTarget: "file" } });
  });

  it("聚焦目标并替换上一个高亮元素", async () => {
    const first = document.createElement("button");
    const second = document.createElement("button");
    first.id = "notice-first";
    second.id = "notice-second";
    document.body.append(first, second);
    await focusNotificationTarget(first.id);
    expect(first.classList.contains("notification-focus")).toBe(true);
    await focusNotificationTarget(second.id);
    expect(first.classList.contains("notification-focus")).toBe(false);
    expect(document.activeElement).toBe(second);
    first.remove();
    second.remove();
  });
});
