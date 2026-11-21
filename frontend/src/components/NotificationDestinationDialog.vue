<script setup lang="ts">
import { useRouter } from "vue-router";
import { noticeDialog } from "../utils/notificationNavigation";

const router = useRouter();

async function openRelated() {
  const destination = noticeDialog.value?.destination;
  if (!destination) return;
  noticeDialog.value = null;
  await router.push(destination);
}
</script>

<template>
  <a-modal
    :visible="Boolean(noticeDialog)"
    title="通知详情"
    :footer="false"
    @cancel="noticeDialog = null"
  >
    <template v-if="noticeDialog">
      <h3>{{ noticeDialog.item.title }}</h3>
      <p class="notice-body">{{ noticeDialog.item.body }}</p>
      <a-alert type="warning">{{ noticeDialog.message }}</a-alert>
      <a-space class="notice-dialog-actions">
        <a-button v-if="noticeDialog.destination" type="primary" @click="openRelated">查看关联对象</a-button>
        <a-button v-if="noticeDialog.retry" type="primary" @click="noticeDialog.retry">重试定位</a-button>
        <a-button @click="noticeDialog = null">关闭</a-button>
      </a-space>
    </template>
  </a-modal>
</template>

<style scoped>
.notice-body { white-space: pre-wrap; overflow-wrap: anywhere; }
.notice-dialog-actions { margin-top: 16px; }
</style>
