import { nextTick, shallowRef } from "vue";
import type { RouteLocationRaw, Router } from "vue-router";
import { api } from "../api";
import type { NotificationItem, NotificationResolution } from "../types";

interface NoticeDialog {
  item: NotificationItem;
  message: string;
  destination?: RouteLocationRaw;
  retry?: () => Promise<void>;
}

export const noticeDialog = shallowRef<NoticeDialog | null>(null);
let latestAttempt = 0;

export function notificationLocation(target: NonNullable<NotificationResolution["target"]>): RouteLocationRaw {
  const noticeTarget = target.child_id || target.id;
  switch (target.kind) {
    case "task":
      return { path: `/tasks/${encodeURIComponent(target.id)}`, query: { noticeTarget, noticeSection: target.section } };
    case "development":
      return {
        path: "/party-development",
        query: { tab: "cases", noticeCase: target.id, noticeTarget, noticeArchived: target.archived ? "1" : undefined },
      };
    case "transfer":
      return { path: target.section === "inbox" ? "/fleet/inbox" : "/fleet/transfers", query: { noticeTarget } };
    case "root_approval":
      return { path: "/fleet/grants", query: { noticeTarget } };
    case "workspace":
      return { path: "/workspace", query: { noticeRoot: target.id, noticeTarget } };
  }
}

const unavailableMessages = {
  unavailable: "具体目标已失效或当前账号无权访问。",
  archived: "关联对象已归档，可查看保留的记录。",
  revoked: "这条通知已撤销，关联对象仍按当前权限提供。",
};

export async function openNotification(router: Router, item: NotificationItem): Promise<void> {
  const attempt = ++latestAttempt;
  noticeDialog.value = null;
  try {
    if (!item.read_at) {
      const marked = await api.post<NotificationItem>(`/notifications/${item.id}/read`);
      item.read_at = marked.read_at;
    }
    const resolution = await api.get<NotificationResolution>(`/notifications/${item.id}/target`);
    if (attempt !== latestAttempt) return;
    const destination = resolution.target ? notificationLocation(resolution.target) : undefined;
    if (resolution.status === "ready" && destination) {
      await router.push(destination);
      return;
    }
    noticeDialog.value = {
      item,
      message: resolution.status === "ready" ? unavailableMessages.unavailable : unavailableMessages[resolution.status],
      destination,
    };
  } catch {
    if (attempt === latestAttempt) {
      noticeDialog.value = {
        item,
        message: "暂时无法打开关联对象，请检查主机连接后重试。",
        retry: () => openNotification(router, item),
      };
    }
  }
}

let highlighted: HTMLElement | null = null;

export async function focusNotificationTarget(anchor: string) {
  await nextTick();
  const element = document.getElementById(anchor);
  if (!element) return;
  highlighted?.classList.remove("notification-focus");
  highlighted = element;
  element.classList.add("notification-focus");
  element.focus({ preventScroll: true });
  element.scrollIntoView?.({ block: "center" });
}
