<script setup lang="ts">
import { reactive, ref } from "vue";
import { Message } from "@arco-design/web-vue";
import { useRouter } from "vue-router";
import { api } from "../api";
import { useSessionStore } from "../stores/session";

const session = useSessionStore();
const router = useRouter();
const credentials = reactive({ currentPassword: "", username: "", newPassword: "" });
const saving = ref(false);
const offlinePassword = ref("");
const offlineCode = ref("");
const offlineBusy = ref(false);

async function updateCredentials() {
  if (saving.value || !credentials.currentPassword || (!credentials.username.trim() && !credentials.newPassword)) return;
  saving.value = true;
  try {
    await api.patch("/auth/credentials", {
      current_password: credentials.currentPassword,
      username: credentials.username.trim() || undefined,
      new_password: credentials.newPassword || undefined,
    });
    Object.assign(credentials, { currentPassword: "", username: "", newPassword: "" });
    session.clearSession();
    await router.replace("/login");
    Message.success("登录信息已更新，请重新登录。");
  } catch (error) {
    Message.error(error instanceof Error ? error.message : "登录信息更新失败");
  } finally {
    saving.value = false;
  }
}

async function updateOfflineCode(action: "issue" | "revoke") {
  if (!offlinePassword.value || offlineBusy.value) return;
  offlineBusy.value = true;
  offlineCode.value = "";
  const body = { current_password: offlinePassword.value };
  try {
    if (action === "issue") {
      const result = await api.post<{ credential: string }>("/auth/recovery/offline", body);
      offlineCode.value = result.credential;
    } else {
      await api.deleteBody("/auth/recovery/offline", body);
      Message.success("管理员离线恢复码已撤销。");
    }
  } catch (error) {
    Message.error(error instanceof Error ? error.message : "离线恢复码操作失败");
  } finally {
    offlinePassword.value = "";
    offlineBusy.value = false;
  }
}
</script>

<template>
  <div class="page account-page">
    <header class="page-header">
      <div>
        <h1 class="page-title">账号设置</h1>
        <p class="page-description">当前用户名 {{ session.user?.username }}。修改登录信息后，所有设备需要重新登录。</p>
      </div>
    </header>
    <section class="panel account-panel">
      <h2>修改登录信息</h2>
      <a-form :model="credentials" layout="vertical" @submit-success="updateCredentials">
        <a-form-item label="当前密码" required><a-input-password v-model="credentials.currentPassword" autocomplete="current-password" /></a-form-item>
        <a-form-item label="新用户名（选填）"><a-input v-model="credentials.username" autocomplete="off" /></a-form-item>
        <a-form-item label="新密码（选填）"><a-input-password v-model="credentials.newPassword" autocomplete="new-password" /></a-form-item>
        <a-button type="primary" html-type="submit" :loading="saving" :disabled="!credentials.currentPassword || (!credentials.username.trim() && !credentials.newPassword)">保存并重新登录</a-button>
      </a-form>
    </section>
    <section v-if="session.user?.role === 'admin'" class="panel account-panel">
      <h2>管理员离线恢复码</h2>
      <p>离线保管恢复码。生成新码会立即使上一枚失效，每枚仅能使用一次。</p>
      <a-form :model="{ currentPassword: offlinePassword }" layout="vertical" @submit-success="updateOfflineCode('issue')">
        <a-form-item label="当前密码" required><a-input-password v-model="offlinePassword" autocomplete="current-password" /></a-form-item>
        <a-space wrap>
          <a-button type="primary" html-type="submit" :loading="offlineBusy" :disabled="!offlinePassword">生成新恢复码</a-button>
          <a-button status="danger" :disabled="!offlinePassword || offlineBusy" @click="updateOfflineCode('revoke')">撤销现有恢复码</a-button>
        </a-space>
      </a-form>
      <div v-if="offlineCode" class="issued-code" role="status">
        <p>请立即离线保存。关闭后需重新签发。</p>
        <code>{{ offlineCode }}</code>
        <a-button @click="offlineCode = ''">关闭恢复码</a-button>
      </div>
    </section>
  </div>
</template>

<style scoped>
.account-page { max-width: 820px; }
.account-panel { margin-bottom: 16px; }
.account-panel h2 { margin-top: 0; }
.issued-code { margin-top: 16px; padding: 12px; background: var(--paper); border: 1px solid var(--line); }
.issued-code code { display: block; margin-bottom: 12px; overflow-wrap: anywhere; user-select: all; }
</style>
