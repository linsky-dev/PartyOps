<script setup lang="ts">
import { computed } from "vue";
import type { DevelopmentTimelineEntry } from "../types";
import { beijingNow, formatServerTime } from "../utils/datetime";

const props = defineProps<{ entries: DevelopmentTimelineEntry[]; noticeAnchors?: boolean }>();
const groups = computed(() => [
  { key: "actual", title: "实际进度", items: props.entries.filter((entry) => entry.actual), empty: "暂无已录入的实际节点。" },
  { key: "plan", title: "参考计划", items: props.entries.filter((entry) => !entry.actual), empty: "暂无未完成的参考节点。" },
]);

function showDate(value: string) {
  return formatServerTime(value, "YYYY-MM-DD");
}

function past(value: string | null) {
  return value !== null && showDate(value) < beijingNow().format("YYYY-MM-DD");
}
</script>

<template>
  <section v-for="group in groups" :key="group.key" class="development-track" :aria-label="group.title">
    <h2>{{ group.title }}</h2>
    <article
      v-for="entry in group.items"
      :id="noticeAnchors ? `notice-milestone-${entry.id}` : undefined"
      :key="entry.id"
      :tabindex="noticeAnchors ? -1 : undefined"
      :class="{ 'actual-node': entry.actual }"
    >
      <small v-if="entry.phase">{{ entry.phase }}</small>
      <h3>{{ entry.title }}</h3>
      <dl v-if="entry.actual || entry.reference || entry.legalEarliest || entry.legalDeadline">
        <div v-if="entry.actual"><dt>实际发生</dt><dd>{{ showDate(entry.actual) }}</dd></div>
        <div v-if="entry.reference">
          <dt>建议计划</dt>
          <dd>{{ showDate(entry.reference) }}<template v-if="entry.referenceEnd && entry.referenceEnd !== entry.reference"> 至 {{ showDate(entry.referenceEnd) }}</template></dd>
        </div>
        <div v-if="entry.legalEarliest"><dt>法规最早</dt><dd>{{ showDate(entry.legalEarliest) }}</dd></div>
        <div v-if="entry.legalDeadline"><dt>法规截止</dt><dd>{{ showDate(entry.legalDeadline) }}</dd></div>
      </dl>
      <p v-else class="muted">等待组织确认日期。</p>
      <p v-if="!entry.actual && past(entry.legalDeadline)" class="legal-risk">已过法规截止日期，待核实实际办理情况。</p>
      <p v-else-if="!entry.actual && past(entry.referenceEnd || entry.reference)" class="plan-risk">已过参考计划日期。</p>
      <p v-if="entry.evidence">{{ entry.evidence }}</p>
      <p v-if="entry.legalBasis">{{ entry.legalBasis }}</p>
      <p v-if="entry.planningBasis">测算依据 {{ entry.planningBasis }}</p>
      <p v-if="entry.ruleVersion || entry.provisional" class="muted"><template v-if="entry.ruleVersion">规则 {{ entry.ruleVersion }}</template><template v-if="entry.provisional"> · 工作日暂算</template></p>
      <slot name="detail" :entry="entry" :group="group.key" />
    </article>
    <div v-if="!group.items.length" class="empty-state">{{ group.empty }}</div>
  </section>
</template>

<style scoped>
.development-track { margin-top: 24px; }
.development-track h2 { margin: 0 0 12px; font-family: var(--serif); font-size: 20px; }
.development-track article { padding: 14px 16px; border: 1px solid var(--line); border-left: 3px solid var(--amber); background: var(--paper); }
.development-track article + article { margin-top: 8px; }
.development-track article.actual-node { border-left-color: var(--green); }
.development-track h3 { margin: 4px 0 10px; color: var(--ink); }
.development-track p, .development-track small { margin-block: 5px; color: var(--muted); font-size: 12px; }
.development-track dl { display: flex; flex-wrap: wrap; gap: 8px 18px; margin-block: 8px; }
.development-track dt { color: var(--muted); font-size: 11px; }
.development-track dd { margin: 2px 0 0; font-size: 14px; font-weight: 600; }
.development-track .legal-risk { color: var(--danger); }
.development-track .plan-risk { color: var(--amber); }
</style>
