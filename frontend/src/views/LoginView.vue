<script setup lang="ts">
import { computed, nextTick, onMounted, reactive, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import {
  IconEye,
  IconEyeInvisible,
  IconLock,
  IconSettings,
  IconUser,
} from "@arco-design/web-vue/es/icon";
import { Message } from "@arco-design/web-vue";
import { useSessionStore } from "../stores/session";
import { api, ApiError } from "../api";
import { orientalDateLabel } from "../utils/lunar";
import { validateLoginForm, type LoginFieldErrors } from "../utils/onboarding";
import OrientalArtLayer from "../components/OrientalArtLayer.vue";
import { sceneConfigForPath } from "../theme/oriental";

const router = useRouter();
const route = useRoute();
const session = useSessionStore();
const loading = ref(false);
const showPassword = ref(false);
const showConnection = ref(false);
const showRecovery = ref(false);
const showHostRecoveryHelp = ref(false);
const recoveryBusy = ref(false);
const recoveryResult = ref("");
const recoveryForm = reactive({ credential: "", username: "", newPassword: "" });
const configured = ref(true);
const usernameInput = ref<{ focus?: () => void } | null>(null);
const passwordInput = ref<{ focus?: () => void } | null>(null);
const displayNameInput = ref<{ focus?: () => void } | null>(null);
const form = reactive({
  username: "",
  password: "",
  displayName: "",
});
const fieldErrors = reactive<LoginFieldErrors>({});
const orientalDate = orientalDateLabel();
const artConfig = sceneConfigForPath("/login");
const connectionLabel = computed(() => {
  const bootstrap = session.bootstrap;
  if (!bootstrap) return "正在检测主机";
  return `${bootstrap.configured ? "主机已连接" : "主机服务已就绪"} · ${bootstrap.host}:${bootstrap.port}`;
});

onMounted(async () => {
  try {
    const status = await session.loadBootstrap();
    configured.value = status.configured;
    if (await session.ensure()) await router.replace("/");
  } catch (error) {
    Message.error(error instanceof Error ? error.message : "无法连接 PartyOps 主机，请检查主机服务是否已启动");
  }
});

function clearFieldError(field: keyof LoginFieldErrors) {
  delete fieldErrors[field];
}

function closeRecovery() {
  if (!recoveryBusy.value) {
    showRecovery.value = false;
    showHostRecoveryHelp.value = false;
    recoveryResult.value = "";
    Object.assign(recoveryForm, { credential: "", username: "", newPassword: "" });
  }
}

async function recoverAccount() {
  if (recoveryBusy.value || !recoveryForm.credential.trim()) return;
  recoveryBusy.value = true;
  try {
    const account = await api.post<{ username: string }>("/auth/recovery/redeem", {
      credential: recoveryForm.credential.trim(),
      username: recoveryForm.username.trim() || undefined,
      new_password: recoveryForm.newPassword || undefined,
    });
    recoveryResult.value = account.username;
    form.username = account.username;
    form.password = "";
    Object.assign(recoveryForm, { credential: "", username: "", newPassword: "" });
  } catch (error) {
    Message.error(error instanceof Error ? error.message : "恢复失败，请重试或联系管理员重新签发。");
  } finally {
    recoveryBusy.value = false;
  }
}

async function focusFirstError() {
  await nextTick();
  if (fieldErrors.displayName) displayNameInput.value?.focus?.();
  else if (fieldErrors.username) usernameInput.value?.focus?.();
  else if (fieldErrors.password) passwordInput.value?.focus?.();
}

async function submit() {
  Object.keys(fieldErrors).forEach((key) => delete fieldErrors[key as keyof LoginFieldErrors]);
  Object.assign(fieldErrors, validateLoginForm(form, configured.value));
  if (Object.keys(fieldErrors).length) {
    await focusFirstError();
    return;
  }
  loading.value = true;
  try {
    if (configured.value) {
      await session.login(form.username, form.password);
    } else {
      await session.setup(form.username, form.displayName, form.password);
    }
    const redirect = typeof route.query.redirect === "string" ? route.query.redirect : "/";
    await router.replace(redirect);
  } catch (error) {
    if (error instanceof ApiError) {
      const mapping: Record<string, keyof LoginFieldErrors> = {
        display_name: "displayName",
        username: "username",
        password: "password",
      };
      Object.entries(error.fields).forEach(([key, value]) => {
        const field = mapping[key];
        if (field) fieldErrors[field] = value;
      });
      if (Object.keys(fieldErrors).length) await focusFirstError();
    }
    // 网络层失败（fetch 抛 TypeError / 连接被拒）单独给出可操作提示，
    // 不展示浏览器底层的 “Failed to fetch” 或 “urlopen error” 原文。
    const isConnectionFailure = error instanceof TypeError
      || (error instanceof Error && /failed to fetch|networkerror|10061|积极拒绝/i.test(error.message));
    Message.error(
      isConnectionFailure
        ? "无法连接到 PartyOps 主机服务。请确认主机服务已完全启动后再试，必要时稍等片刻重试。"
        : (error instanceof Error ? error.message : "无法进入系统"),
    );
  } finally {
    loading.value = false;
  }
}
</script>

<template>
  <main class="login-screen">
    <OrientalArtLayer
      :config="artConfig"
      :solar-term="orientalDate.solarTerm"
      :active-solar-term="orientalDate.activeSolarTerm"
      standalone
    />
    <section class="identity-pane">
      <div class="identity-content">
        <h1><span>党建</span>智办</h1>
        <div class="brand-rule"><b></b><i></i></div>
        <p class="subtitle">基层党建工作闭环协同系统</p>
        <p class="partyops">PartyOps</p>
        <p class="oriental-date">{{ orientalDate.gregorian }}　{{ orientalDate.weekday }}　{{ orientalDate.lunar }}<b v-if="orientalDate.solarTerm">{{ orientalDate.solarTerm }}</b></p>
      </div>
      <img class="motif" src="../assets/launch-motif.png" alt="" aria-hidden="true" />
    </section>
    <section class="login-pane">
      <div class="login-form-wrap">
        <div class="connection-status">
          <span class="connection-dot"></span>
          <strong>{{ connectionLabel }}</strong>
        </div>
        <div class="form-rule"></div>
        <div v-if="!configured" class="setup-stage">
          <span>主机配置 · 第 2 步</span>
          <strong>创建首位系统管理员</strong>
          <p>此账号负责添加成员、协同电脑、备份和更新。创建成功后再从“设备协同”邀请其他电脑。</p>
        </div>
        <form @submit.prevent="submit">
          <label v-if="!configured">
            <span class="field-label">管理员姓名</span>
            <a-input ref="displayNameInput" v-model="form.displayName" size="large" placeholder="请输入姓名" :error="Boolean(fieldErrors.displayName)" @input="clearFieldError('displayName')">
              <template #prefix><IconUser /></template>
            </a-input>
            <small v-if="fieldErrors.displayName" class="field-error" role="alert">{{ fieldErrors.displayName }}</small>
          </label>
          <label>
            <span class="field-label">用户名</span>
            <a-input ref="usernameInput" v-model="form.username" size="large" placeholder="例如：admin" autocomplete="username" :error="Boolean(fieldErrors.username)" @input="clearFieldError('username')">
              <template #prefix><IconUser /></template>
            </a-input>
            <small v-if="fieldErrors.username" class="field-error" role="alert">{{ fieldErrors.username }}</small>
          </label>
          <label>
            <span class="field-label">密码</span>
            <a-input
              v-model="form.password"
              ref="passwordInput"
              size="large"
              :type="showPassword ? 'text' : 'password'"
              placeholder="请输入密码"
              :autocomplete="configured ? 'current-password' : 'new-password'"
              :error="Boolean(fieldErrors.password)"
              @input="clearFieldError('password')"
            >
              <template #prefix><IconLock /></template>
              <template #suffix>
                <button
                  class="eye-button"
                  type="button"
                  :aria-label="showPassword ? '隐藏密码' : '显示密码'"
                  @click="showPassword = !showPassword"
                >
                  <IconEyeInvisible v-if="showPassword" />
                  <IconEye v-else />
                </button>
              </template>
            </a-input>
            <small v-if="fieldErrors.password" class="field-error" role="alert">{{ fieldErrors.password }}</small>
          </label>
          <a-button html-type="submit" type="primary" size="large" long :loading="loading">
            {{ configured ? "进入系统" : "完成主机配置并进入" }}
          </a-button>
        </form>
        <div class="form-rule bottom"></div>
        <div class="login-secondary-actions">
          <button class="connection-settings" type="button" @click="showConnection = true">
            <IconSettings /> 连接设置
          </button>
          <button v-if="configured" class="connection-settings" type="button" @click="showRecovery = true">
            恢复账号
          </button>
        </div>
        <p v-if="!configured" class="setup-note">业务数据只保存在这台主机；首位管理员创建后请立即配置备份。</p>
      </div>
    </section>
    <a-modal :visible="showRecovery" title="恢复账号" :footer="false" :mask-closable="false" @cancel="closeRecovery">
      <div v-if="recoveryResult">
        <a-alert type="success">账号用户名为 <strong>{{ recoveryResult }}</strong>。恢复凭据已经使用。</a-alert>
        <a-button class="recovery-action" type="primary" @click="closeRecovery">返回登录</a-button>
      </div>
      <template v-else>
        <p>普通成员请联系管理员核验身份后取得一次性恢复凭据；管理员可以使用事先离线保管的恢复码。</p>
        <a-button type="text" class="host-recovery-help-toggle" @click="showHostRecoveryHelp = !showHostRecoveryHelp">
          {{ showHostRecoveryHelp ? "收起主机应急恢复说明" : "我是唯一管理员，忘记密码且没有恢复码" }}
        </a-button>
        <div v-if="showHostRecoveryHelp" class="host-recovery-help">
          <a-alert type="warning">没有预存恢复码时，不能仅凭网页或用户名重置密码。请在实际存放业务数据库的主机本机，使用操作系统管理员权限完成应急恢复；协同电脑和远程网页不能执行。</a-alert>
          <p>先备份主机业务数据并停止 PartyOps 主机服务；在提升权限的本机交互终端执行以下命令。将数据目录替换为该主机实际使用的目录，不得指向新建空目录。</p>
          <p><strong>UOS、麒麟及其他 Linux：</strong></p>
          <code>sudo /opt/partyops/partyops --account-recovery emergency --data-dir /var/lib/partyops</code>
          <p><strong>Windows：</strong>以管理员身份打开 PowerShell，在实际安装目录运行：</p>
          <code>&amp; ".&#92;PartyOps.exe" --account-recovery emergency --data-dir "实际主机数据目录"</code>
          <p>确认后工具只显示一次 30 分钟有效恢复凭据。重启主机服务后，在上方“恢复凭据”和“新密码”输入框完成重置；旧账号、业务数据及权限均保留，旧会话将失效。</p>
          <p>如有其他有效管理员，应由该管理员使用正式后台流程操作，不应使用本机应急模式。</p>
        </div>
        <a-form :model="recoveryForm" layout="vertical" class="recovery-form" @submit-success="recoverAccount">
          <a-form-item label="恢复凭据" required>
            <a-input-password v-model="recoveryForm.credential" autocomplete="off" />
          </a-form-item>
          <a-form-item label="新用户名（选填）">
            <a-input v-model="recoveryForm.username" autocomplete="off" />
          </a-form-item>
          <a-form-item label="新密码（选填）">
            <a-input-password v-model="recoveryForm.newPassword" autocomplete="new-password" />
          </a-form-item>
          <p>留空两项只找回用户名，同样会消耗恢复凭据。</p>
          <a-button type="primary" html-type="submit" :disabled="!recoveryForm.credential.trim()" :loading="recoveryBusy">验证并恢复</a-button>
        </a-form>
      </template>
    </a-modal>
    <a-modal v-model:visible="showConnection" title="局域网连接设置" :footer="false">
      <div class="connection-modal">
        <span class="connection-dot"></span>
        <div>
          <strong>当前连接正常：{{ session.bootstrap?.service_url }}</strong>
          <p v-if="session.bootstrap?.lan_candidates.length">本机检测到的局域网地址：{{ session.bootstrap.lan_candidates.join("、") }}。协同终端应使用主机明确绑定的地址。</p>
          <p v-else>当前仅检测到本机访问地址。若要多设备协同，请运行配置向导并选择主机局域网 IP。</p>
        </div>
      </div>
      <div class="role-connection-grid">
        <article><strong>这台电脑是主机</strong><p>保持当前页面，完成管理员创建或直接登录。协同设备、备份和系统更新只由获授权管理员设置。</p></article>
        <article><strong>这台电脑应是协同机</strong><p>关闭此页，从开始菜单重新打开“党建智办”，在首次配置向导选择“这是协同机”，先测试主机地址再输入 10 分钟入网码。</p></article>
      </div>
    </a-modal>
  </main>
</template>

<style scoped>
.login-screen {
  display: grid;
  min-height: 100vh;
  grid-template-columns: 55% 45%;
  overflow: hidden;
  background-color: #f7f1e7;
  background-image: url("../assets/paper-texture.png");
  background-size: 760px;
}

.identity-pane,
.login-pane {
  position: relative;
  min-height: 100vh;
}

.identity-content {
  position: relative;
  z-index: 2;
  width: min(590px, 76%);
  margin: 20vh 0 0 7.9vw;
}

h1 {
  margin: 0;
  font-family: "Noto Serif CJK SC", "Source Han Serif SC", STSong, SimSun, serif;
  font-size: clamp(76px, 8.6vw, 124px);
  font-weight: 500;
  line-height: 1;
  letter-spacing: 0.025em;
}

h1 span {
  color: var(--cinnabar);
}

.brand-rule {
  display: flex;
  align-items: center;
  width: 100%;
  margin: 24px 0 30px;
}

.brand-rule b {
  width: 38px;
  height: 5px;
  background: var(--cinnabar);
}

.brand-rule i {
  height: 1px;
  flex: 1;
  background: rgba(180, 35, 24, 0.48);
}

.subtitle {
  margin: 0;
  font-family: "Noto Serif CJK SC", "Source Han Serif SC", STSong, SimSun, serif;
  font-size: clamp(30px, 3vw, 45px);
  letter-spacing: 0.12em;
  white-space: nowrap;
}

.partyops {
  margin: 22px 0 0;
  color: var(--cinnabar);
  font-family: Georgia, "Times New Roman", serif;
  font-size: 28px;
  letter-spacing: 0.05em;
}

.oriental-date {
  display: flex;
  align-items: center;
  gap: 9px;
  margin: 20px 0 0;
  color: var(--muted);
  font-size: 12px;
  letter-spacing: 0.06em;
}

.oriental-date b {
  padding: 2px 6px;
  color: var(--cinnabar);
  font-family: var(--serif);
  font-weight: 500;
  border: 1px solid rgba(180, 35, 24, 0.5);
}

.motif {
  position: absolute;
  bottom: 0;
  left: 0;
  z-index: 1;
  width: min(58vw, 820px);
  height: auto;
  mix-blend-mode: multiply;
  pointer-events: none;
}

.login-pane {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 40px 7.8vw 40px 4vw;
}

.login-form-wrap {
  width: min(500px, 100%);
  transform: translateY(3.7vh);
}

.connection-status {
  display: flex;
  align-items: center;
  gap: 12px;
  font-size: 17px;
  letter-spacing: 0.04em;
}

.connection-dot {
  width: 14px;
  height: 14px;
  flex: 0 0 14px;
  border-radius: 50%;
  background: var(--cinnabar);
}

.form-rule {
  height: 1px;
  margin: 43px 0 37px;
  background: var(--line);
}

.form-rule.bottom {
  margin: 30px 0 22px;
}

form {
  display: flex;
  flex-direction: column;
  gap: 35px;
}

.setup-stage {
  margin: -18px 0 24px;
  padding: 14px 16px;
  background: rgba(180, 35, 24, 0.055);
  border-left: 3px solid var(--cinnabar);
}

.setup-stage span,
.setup-stage strong {
  display: block;
}

.setup-stage span {
  color: var(--cinnabar);
  font-size: 11px;
  letter-spacing: 0.08em;
}

.setup-stage strong {
  margin: 3px 0;
  font-family: var(--serif);
  font-size: 17px;
}

.setup-stage p,
.field-error {
  margin: 0;
  font-size: 12px;
}

.setup-stage p {
  color: var(--muted);
  line-height: 1.6;
}

.field-error {
  display: block;
  margin-top: 7px;
  color: var(--cinnabar);
}

.field-label {
  display: block;
  margin-bottom: 11px;
  font-size: 16px;
}

:deep(.arco-input-wrapper) {
  height: 72px;
  padding: 0 18px;
  font-size: 16px;
  background: rgba(255, 255, 255, 0.38) !important;
}

:deep(.arco-btn-size-large) {
  height: 78px;
  margin-top: 8px;
  border-radius: 2px;
  font-size: 20px;
  font-weight: 600;
  letter-spacing: 0.12em;
}

.eye-button {
  display: grid;
  padding: 5px;
  color: #79736c;
  background: transparent;
  border: 0;
  cursor: pointer;
  place-items: center;
}

.connection-settings {
  display: flex;
  align-items: center;
  gap: 7px;
  margin: 0 auto;
  padding: 8px 12px;
  color: var(--cinnabar);
  background: transparent;
  border: 0;
  cursor: pointer;
}

.setup-note {
  margin: 18px 0 0;
  color: var(--muted);
  font-size: 12px;
  line-height: 1.6;
  text-align: center;
}

.login-secondary-actions { display: flex; justify-content: center; gap: 10px; }
.login-secondary-actions .connection-settings { margin: 0; }
.recovery-form { display: block; }
.recovery-form :deep(.arco-input-wrapper) { height: auto; padding: 0 8px; font-size: 14px; }
.recovery-action { margin-top: 16px; }
.host-recovery-help-toggle { margin: 4px 0 12px; padding-left: 0; }
.host-recovery-help { padding: 12px; border: 1px solid var(--line); background: rgba(247, 241, 231, 0.6); }
.host-recovery-help p { margin: 10px 0 6px; font-size: 12px; line-height: 1.65; }
.host-recovery-help code { display: block; padding: 8px; overflow-wrap: anywhere; user-select: all; background: rgba(255,255,255,0.76); font-size: 12px; }


.connection-modal {
  display: flex;
  gap: 14px;
  padding: 12px 4px 4px;
}

.connection-modal p {
  color: var(--muted);
  line-height: 1.7;
}

.role-connection-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 10px;
  margin-top: 14px;
}

.role-connection-grid article {
  padding: 14px;
  background: rgba(247, 241, 231, 0.65);
  border: 1px solid var(--line);
}

.role-connection-grid p {
  margin: 6px 0 0;
  color: var(--muted);
  font-size: 12px;
  line-height: 1.7;
}

@media (max-height: 800px) {
  .identity-content {
    margin-top: 13vh;
  }

  h1 {
    font-size: 72px;
  }

  .login-form-wrap {
    transform: translateY(1vh) scale(0.9);
  }
}
</style>
