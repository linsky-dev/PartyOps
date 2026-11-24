<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from "vue";
import { useRoute } from "vue-router";
import { IconDelete, IconDownload, IconPlus, IconRefresh, IconUpload } from "@arco-design/web-vue/es/icon";
import { Message, Modal } from "@arco-design/web-vue";
import { api, saveBlobDownload } from "../api";
import LedgerImportWizard from "../components/LedgerImportWizard.vue";
import PageHelp from "../components/PageHelp.vue";
import DevelopmentTimeline from "../components/DevelopmentTimeline.vue";
import type { DevelopmentTimelineEntry } from "../types";
import { formatServerTime } from "../utils/datetime";
import { focusNotificationTarget } from "../utils/notificationNavigation";

interface Milestone { id: string; milestone_type: string; actual_at: string | null; legal_earliest_at: string | null; legal_deadline_at: string | null; planned_at: string | null; adjusted_at: string | null; legal_basis: string; planning_basis?: string; rule_version?: string; plan_kind: string; reminder_days: number[]; version: number; }
interface ProgressEvent { id: string; milestone_type: string; actual_at: string; evidence_note: string; status: string; version: number; }
interface ProgressHistory extends ProgressEvent { created_at: string; voided_at?: string | null; }
interface TimelineItem extends Milestone { title_zh?: string; progress_event?: ProgressEvent | null; related_plan_ids?: string[]; }
interface DevelopmentCase { id: string; party_committee: string; party_branch: string; name: string; gender: string; ethnicity: string; birth_date: string | null; education: string; application_at: string; activist_at: string | null; training_contacts: string[]; introducers: string[]; development_object_at: string | null; probationary_at: string | null; converted_at: string | null; stage: string; status: string; rule_version: string; version: number; milestones: Milestone[]; progress_events: ProgressEvent[]; extra_fields: Record<string, unknown>; }
interface Statistics { total: number; stage_counts: Record<string, number>; upcoming_60_days: number; legal_overdue?: number; reference_overdue?: number; }
interface DeletionImpact { milestones: number; progress_events: number; active_notifications: number; message: string; }

const cases = ref<DevelopmentCase[]>([]);
const route = useRoute();
const statistics = ref<Statistics>({ total: 0, stage_counts: {}, upcoming_60_days: 0 });
const selectedId = ref(typeof route?.query.noticeCase === "string" ? route.query.noticeCase : "");
const caseStatus = ref<"active" | "archived">(route?.query.noticeArchived === "1" ? "archived" : "active");
const createVisible = ref(false);
const creating = ref(false);
const importVisible = ref(false);
const timelineLoading = ref(false);
const timeline = ref<TimelineItem[]>([]);
const history = ref<ProgressHistory[]>([]);
const progressVisible = ref(false);
const progressSaving = ref(false);
const editingProgress = ref<ProgressEvent | null>(null);
const deletionVisible = ref(false);
const deletionImpact = ref<DeletionImpact | null>(null);
const lifecycleReason = ref("");
const selected = computed(() => cases.value.find((item) => item.id === selectedId.value) || null);
const form = reactive({ party_committee: "", party_branch: "", name: "", gender: "", ethnicity: "", birth_date: "", education: "", application_date: "", activist_date: "", training_contacts_text: "", introducers_text: "", development_object_date: "", probationary_date: "", converted_date: "" });
const progressForm = reactive({ milestone_type: "conversation", actual_date: "", evidence_note: "" });

const milestoneLabels: Record<string, string> = {
  application: "提交入党申请书", conversation_window: "谈话建议窗口", conversation_deadline: "谈话截止",
  conversation_target: "派人谈话参考目标", activist_reference: "确定入党积极分子参考时间",
  activist_date: "确定入党积极分子", first_half_year_assessment: "首次半年考察", development_object_earliest: "列为发展对象最早日期",
  first_half_year_assessment_reference: "首次半年考察参考时间", development_object_reference: "列为发展对象参考时间",
  development_object_publicity: "发展对象公示", development_object_date: "确定发展对象", political_review: "政治审查",
  training: "集中培训", pre_review_approved: "上级党委预审", branch_acceptance_deadline: "支部大会讨论截止",
  branch_acceptance: "接收预备党员", committee_approval: "党委审批期限", oath_deadline: "入党宣誓",
  probation_end: "预备期满", transition_application: "提交转正申请", transition_branch_meeting: "转正支部大会",
  transition_approval_deadline: "转正审批截止", archive: "材料归档",
  conversation: "派人谈话", activist_publicity_start: "积极分子公示", training_completed: "集中培训完成",
  political_review_completed: "政治审查完成", committee_approval_actual: "党委审批完成", oath: "入党宣誓",
  probationary_status: "列为预备党员", transition_approval: "转正审批完成",
};
const progressStatusLabels: Record<string, string> = { confirmed: "当前记录", superseded: "已纠正", voided: "已作废" };
const timelineItems = computed(() => new Map(timeline.value.map((item) => [item.id, item])));
const entries = computed<DevelopmentTimelineEntry[]>(() => timeline.value.map((item) => ({
  id: item.id,
  title: item.title_zh || milestoneLabels[item.milestone_type] || "待确认节点",
  actual: item.actual_at,
  reference: item.adjusted_at || item.planned_at,
  legalEarliest: item.legal_earliest_at,
  legalDeadline: item.legal_deadline_at,
  legalBasis: item.legal_basis,
  planningBasis: item.planning_basis,
  ruleVersion: item.rule_version || selected.value?.rule_version,
  evidence: item.progress_event?.evidence_note,
})));

const factOptions = [
  ["conversation", "派人谈话"], ["activist_date", "确定入党积极分子"], ["activist_publicity_start", "积极分子公示"],
  ["development_object_date", "确定发展对象"], ["training_completed", "集中培训完成"], ["political_review_completed", "政治审查完成"],
  ["pre_review_approved", "上级党委预审合格"], ["branch_acceptance", "支部大会接收预备党员"], ["committee_approval_actual", "党委审批完成"],
  ["oath", "入党宣誓"], ["transition_application", "提交转正申请"], ["transition_branch_meeting", "转正支部大会"], ["transition_approval", "转正审批完成"],
];

function splitPeople(value: string) { return [...new Set(value.split(/[、,，\s]+/).map((item) => item.trim()).filter(Boolean))]; }
function cleanPayload() {
  return {
    party_committee: form.party_committee, party_branch: form.party_branch, name: form.name, gender: form.gender,
    ethnicity: form.ethnicity, birth_date: form.birth_date || null, education: form.education,
    application_date: form.application_date, activist_date: form.activist_date || null,
    training_contacts: splitPeople(form.training_contacts_text), introducers: splitPeople(form.introducers_text),
    development_object_date: form.development_object_date || null, probationary_date: form.probationary_date || null,
    converted_date: form.converted_date || null,
  };
}

async function load() {
  try {
    [cases.value, statistics.value] = await Promise.all([api.get<DevelopmentCase[]>(`/party-development/cases?case_status=${caseStatus.value}`), api.get<Statistics>("/party-development/statistics")]);
    if (!selectedId.value || !cases.value.some((item) => item.id === selectedId.value)) selectedId.value = cases.value[0]?.id || "";
    await loadTimeline();
  } catch (error) {
    Message.error(error instanceof Error ? error.message : "发展档案读取失败");
  }
}

async function loadTimeline() {
  const id = selectedId.value;
  if (!id) {
    timeline.value = [];
    history.value = [];
    return;
  }
  timelineLoading.value = true;
  try {
    const result = await api.get<{ timeline: TimelineItem[]; progress_history?: ProgressHistory[] }>(`/party-development/cases/${encodeURIComponent(id)}/timeline`);
    if (selectedId.value !== id) return;
    timeline.value = result.timeline;
    history.value = result.progress_history || [];
    await focusCaseNotification();
  } catch (error) {
    Message.error(error instanceof Error ? error.message : "发展时间轴读取失败");
  } finally {
    timelineLoading.value = false;
  }
}

async function focusCaseNotification() {
  const target = route?.query.noticeTarget;
  if (route?.query.noticeCase !== selectedId.value || typeof target !== "string") return;
  const entry = timeline.value.find((item) => item.id === target || item.related_plan_ids?.includes(target));
  await focusNotificationTarget(entry ? `notice-milestone-${entry.id}` : `notice-case-${selectedId.value}`);
}

function locateCaseNotification() {
  const target = route?.query.noticeCase;
  if (typeof target !== "string") return;
  const status = route?.query.noticeArchived === "1" ? "archived" : "active";
  selectedId.value = target;
  if (caseStatus.value !== status) caseStatus.value = status;
  else if (cases.value.some((item) => item.id === target)) void loadTimeline();
}

async function createCase() {
  if (!form.party_committee.trim() || !form.party_branch.trim() || !form.name.trim() || !form.application_date) {
    Message.warning("请填写党委、党支部、姓名和申请书日期");
    return;
  }
  creating.value = true;
  try {
    const created = await api.post<DevelopmentCase>("/party-development/cases", cleanPayload());
    await api.post(`/party-development/cases/${created.id}/generate-milestones`);
    selectedId.value = created.id;
    createVisible.value = false;
    Message.success("发展档案和参考节点已生成；组织决定仍需人工补录");
    await load();
  } catch (error) {
    Message.error(error instanceof Error ? error.message : "发展档案创建失败");
  } finally {
    creating.value = false;
  }
}

function dateValue(value: string | null | undefined) { return value ? String(value).slice(0, 10) : ""; }

function openProgress(item?: TimelineItem) {
  if (!selected.value || selected.value.status !== "active") return;
  editingProgress.value = item?.progress_event || null;
  Object.assign(progressForm, {
    milestone_type: item?.milestone_type || "conversation",
    actual_date: dateValue(item?.actual_at),
    evidence_note: item?.progress_event?.evidence_note || "",
  });
  progressVisible.value = true;
}

async function saveProgress() {
  if (!selected.value || !progressForm.actual_date || !progressForm.evidence_note.trim()) {
    Message.warning("请填写真实发生日期和事实说明");
    return;
  }
  progressSaving.value = true;
  try {
    if (editingProgress.value) {
      await api.post(`/party-development/progress-events/${editingProgress.value.id}/correct`, {
        actual_date: progressForm.actual_date,
        evidence_note: progressForm.evidence_note,
      }, { "If-Match": String(editingProgress.value.version) });
    } else {
      await api.post(`/party-development/cases/${selected.value.id}/progress-events`, {
        milestone_type: progressForm.milestone_type,
        actual_date: progressForm.actual_date,
        evidence_note: progressForm.evidence_note,
      }, { "If-Match": String(selected.value.version) });
    }
    progressVisible.value = false;
    Message.success(editingProgress.value ? "纠正记录已留痕" : "真实进度已写入时间轴");
    await load();
  } catch (error) {
    Message.error(error instanceof Error ? error.message : "真实进度保存失败");
  } finally {
    progressSaving.value = false;
  }
}

function voidProgress(event: ProgressEvent) {
  Modal.confirm({
    title: "作废这条真实进度？",
    content: "原记录和纠正链会保留，系统将恢复上一条有效事实并重算未来计划。",
    okText: "确认作废",
    hideCancel: false,
    onOk: async () => {
      await api.post(`/party-development/progress-events/${event.id}/void`, { reason: "经办人确认该事实记录无效" }, { "If-Match": String(event.version) });
      Message.success("真实进度已作废并留痕");
      await load();
    },
  });
}

function voidTimelineEntry(id: string) {
  const event = timelineItems.value.get(id)?.progress_event;
  if (event) voidProgress(event);
}

async function refreshTimeline() {
  if (!selected.value) return;
  try {
    await api.post(`/party-development/cases/${selected.value.id}/generate-milestones`);
    await load();
    Message.success("法定边界和未来参考节点已重新计算；真实事实未被覆盖");
  } catch (error) {
    Message.error(error instanceof Error ? error.message : "时间轴刷新失败");
  }
}

async function openDeletion() {
  if (!selected.value) return;
  try {
    deletionImpact.value = await api.get<DeletionImpact>(`/party-development/cases/${selected.value.id}/deletion-impact`);
    lifecycleReason.value = "";
    deletionVisible.value = true;
  } catch (error) {
    Message.error(error instanceof Error ? error.message : "影响范围读取失败");
  }
}

async function archiveSelected() {
  if (!selected.value || lifecycleReason.value.trim().length < 2) {
    Message.warning("请填写作废原因");
    return;
  }
  try {
    await api.deleteBody(`/party-development/cases/${selected.value.id}`, { reason: lifecycleReason.value }, { "If-Match": String(selected.value.version) });
    deletionVisible.value = false;
    Message.success("档案已归档，节点和审计历史保留");
    await load();
  } catch (error) {
    Message.error(error instanceof Error ? error.message : "档案归档失败");
  }
}

async function restoreSelected() {
  if (!selected.value) return;
  try {
    await api.post(`/party-development/cases/${selected.value.id}/restore`, { reason: "经办人核对后恢复继续办理" }, { "If-Match": String(selected.value.version) });
    Message.success("发展档案已恢复");
    await load();
  } catch (error) {
    Message.error(error instanceof Error ? error.message : "档案恢复失败");
  }
}

async function exportCases(format: "docx" | "xlsx") {
  try {
    const blob = await api.get<Blob>(`/party-development/cases/export.${format}`);
    saveBlobDownload(blob, `党员发展情况统计表.${format}`);
    Message.success(`${format.toUpperCase()} 汇总表已导出`);
  } catch (error) {
    Message.error(error instanceof Error ? error.message : "发展档案导出失败");
  }
}

watch([() => route?.query.noticeCase, () => route?.query.noticeTarget, () => route?.query.noticeArchived], locateCaseNotification);
watch(caseStatus, load);
onMounted(load);
</script>

<template>
  <div class="page case-page">
    <header class="page-header">
      <div><p class="page-kicker">工作 · 全周期发展档案</p><h1 class="page-title">党员发展档案与时间轴</h1><p class="page-description">录入真实进度、自动重算未发生节点；事实、法规边界和参考计划在同一条时间轴中区分显示。</p></div>
      <a-space wrap>
        <PageHelp title="党员发展档案" :tips="['可从本地台账导入人员和真实进度，提交前会全量校验。', '绿色为真实完成，红色为逾期，橙色为即将到期，蓝灰为未来计划。', '删除采用可恢复归档，不物理删除节点、材料和审计记录。']" help-query="党员发展 台账导入 真实进度 时间轴" />
        <a-button @click="exportCases('docx')"><template #icon><IconDownload /></template>导出 Word</a-button>
        <a-button @click="exportCases('xlsx')"><template #icon><IconDownload /></template>导出 Excel</a-button>
        <a-button @click="importVisible = true"><template #icon><IconUpload /></template>导入本地台账</a-button>
        <a-button type="primary" @click="createVisible = true"><template #icon><IconPlus /></template>新增发展档案</a-button>
      </a-space>
    </header>
    <section class="case-stats"><article><span>在办人员</span><strong>{{ statistics.total }}</strong></article><article><span>60 天内节点</span><strong>{{ statistics.upcoming_60_days }}</strong></article><article :class="{ danger: statistics.legal_overdue }"><span>已过法规截止</span><strong>{{ statistics.legal_overdue ?? 0 }}</strong></article><article><span>已过参考计划</span><strong>{{ statistics.reference_overdue ?? 0 }}</strong></article></section>
    <div class="ledger-toolbar"><a-radio-group v-model="caseStatus" type="button"><a-radio value="active">在办台账</a-radio><a-radio value="archived">已归档</a-radio></a-radio-group><span>已归档记录保留事实、材料和审计，可恢复。</span></div>
    <section class="case-workspace">
      <aside><button v-for="item in cases" :key="item.id" type="button" :class="{ active: selectedId === item.id }" @click="selectedId = item.id; loadTimeline()"><span>{{ item.party_branch }}</span><strong>{{ item.name }}</strong><small>{{ item.stage }} · {{ formatServerTime(item.application_at, "YYYY-MM-DD") }}</small></button><div v-if="!cases.length" class="empty-state">{{ caseStatus === "active" ? "尚无在办发展档案，可新增或导入台账。" : "暂无已归档档案。" }}</div></aside>
      <main v-if="selected">
        <header :id="`notice-case-${selected.id}`" tabindex="-1" class="person-header">
          <div><span>{{ selected.party_committee }} / {{ selected.party_branch }}</span><h2>{{ selected.name }}</h2><p>{{ selected.gender || "性别未填" }} · {{ selected.ethnicity || "民族未填" }} · {{ selected.education || "文化程度未填" }}</p></div>
          <a-space v-if="selected.status === 'active'" wrap><a-button @click="openProgress()">录入真实进度</a-button><a-button @click="refreshTimeline"><template #icon><IconRefresh /></template>重算未来节点</a-button><a-button status="danger" @click="openDeletion"><template #icon><IconDelete /></template>归档</a-button></a-space>
          <a-button v-else type="primary" @click="restoreSelected">恢复继续办理</a-button>
        </header>
        <a-alert type="info">真实进度必须有事实依据；未来预测不会写成实际发生。修改上游事实只重算未发生节点。</a-alert>
        <a-spin :loading="timelineLoading">
          <DevelopmentTimeline :entries="entries" notice-anchors>
            <template #detail="{ entry }">
              <a-space v-if="timelineItems.get(entry.id)?.progress_event && selected.status === 'active'">
                <a-button size="mini" type="text" @click="openProgress(timelineItems.get(entry.id))">纠正</a-button>
                <a-button size="mini" type="text" status="danger" @click="voidTimelineEntry(entry.id)">作废</a-button>
              </a-space>
            </template>
          </DevelopmentTimeline>
          <details v-if="history.length" class="progress-history">
            <summary>事实记录与纠正历史（{{ history.length }} 条）</summary>
            <ul>
              <li v-for="record in history" :key="record.id">
                <strong>{{ milestoneLabels[record.milestone_type] || "发展节点" }}</strong>
                · {{ formatServerTime(record.actual_at, "YYYY-MM-DD") }} · {{ progressStatusLabels[record.status] || "历史记录" }}
                <p>{{ record.evidence_note }}</p>
                <small>入账 {{ formatServerTime(record.created_at, "YYYY-MM-DD HH:mm") }}<template v-if="record.voided_at"> · 作废 {{ formatServerTime(record.voided_at, "YYYY-MM-DD HH:mm") }}</template></small>
              </li>
            </ul>
          </details>
        </a-spin>
      </main>
      <main v-else class="empty-state">选择人员查看完整时间轴。</main>
    </section>

    <a-modal v-model:visible="createVisible" title="新增党员发展档案" :ok-loading="creating" @ok="createCase"><a-form :model="form" layout="vertical"><div class="form-grid"><a-form-item label="所属党委" required><a-input v-model="form.party_committee" /></a-form-item><a-form-item label="所属党支部" required><a-input v-model="form.party_branch" /></a-form-item><a-form-item label="姓名" required><a-input v-model="form.name" /></a-form-item><a-form-item label="性别"><a-input v-model="form.gender" /></a-form-item><a-form-item label="民族"><a-input v-model="form.ethnicity" /></a-form-item><a-form-item label="出生年月日"><a-date-picker v-model="form.birth_date" value-format="YYYY-MM-DD" /></a-form-item><a-form-item label="文化程度"><a-input v-model="form.education" /></a-form-item><a-form-item label="提交入党申请时间" required><a-date-picker v-model="form.application_date" value-format="YYYY-MM-DD" /></a-form-item><a-form-item label="列为积极分子时间"><a-date-picker v-model="form.activist_date" value-format="YYYY-MM-DD" /></a-form-item><a-form-item label="列为发展对象时间"><a-date-picker v-model="form.development_object_date" value-format="YYYY-MM-DD" /></a-form-item><a-form-item label="列为预备党员时间"><a-date-picker v-model="form.probationary_date" value-format="YYYY-MM-DD" /></a-form-item><a-form-item label="预备党员转正时间"><a-date-picker v-model="form.converted_date" value-format="YYYY-MM-DD" /></a-form-item><a-form-item label="培养联系人"><a-input v-model="form.training_contacts_text" placeholder="多人用顿号分隔" /></a-form-item><a-form-item label="入党介绍人"><a-input v-model="form.introducers_text" placeholder="多人用顿号分隔" /></a-form-item></div></a-form></a-modal>
    <a-modal v-model:visible="progressVisible" :title="editingProgress ? '纠正真实进度' : '录入真实进度'" :ok-loading="progressSaving" @ok="saveProgress"><a-alert type="warning">只录入已经真实发生且可以由材料或会议记录佐证的事实。</a-alert><a-form :model="progressForm" layout="vertical" class="progress-form"><a-form-item label="进度节点" required><a-select v-model="progressForm.milestone_type" :disabled="Boolean(editingProgress)"><a-option v-for="option in factOptions" :key="option[0]" :value="option[0]">{{ option[1] }}</a-option></a-select></a-form-item><a-form-item label="实际发生日期" required><a-date-picker v-model="progressForm.actual_date" value-format="YYYY-MM-DD" /></a-form-item><a-form-item label="事实说明/材料依据" required><a-textarea v-model="progressForm.evidence_note" :auto-size="{ minRows: 3, maxRows: 6 }" /></a-form-item></a-form></a-modal>
    <a-modal v-model:visible="deletionVisible" title="归档发展党员台账" ok-text="确认归档" @ok="archiveSelected"><a-alert type="warning">这不是物理删除。归档后从在办列表和活动提醒中移除，但事实、节点、材料和审计全部保留。</a-alert><div v-if="deletionImpact" class="impact-grid"><span>计划节点 <b>{{ deletionImpact.milestones }}</b></span><span>事实记录 <b>{{ deletionImpact.progress_events }}</b></span><span>活动提醒 <b>{{ deletionImpact.active_notifications }}</b></span></div><a-form-item label="归档原因" required><a-textarea v-model="lifecycleReason" /></a-form-item></a-modal>
    <LedgerImportWizard v-model:visible="importVisible" target-type="party_development" target-label="发展党员人员与真实进度" @completed="load" />
  </div>
</template>

<style scoped>
.progress-history { margin-top: 18px; padding: 12px; border: 1px solid var(--line); }
.progress-history summary { color: var(--cinnabar); cursor: pointer; }
.progress-history p { margin: 4px 0; }
.progress-history small { color: var(--muted); }
.case-page{max-width:1540px}.case-stats{display:grid;grid-template-columns:repeat(4,1fr);margin:18px 0;border:1px solid var(--line);background:var(--line);gap:1px}.case-stats article{padding:20px 24px;background:#fffaf0}.case-stats span{display:block;color:var(--muted);font-size:11px}.case-stats strong{display:block;margin-top:7px;color:#4d382c;font-family:Georgia,"Noto Serif SC",serif;font-size:30px}.case-stats article.danger strong{color:#b42318}.ledger-toolbar{display:flex;align-items:center;justify-content:space-between;margin:0 0 12px;color:var(--muted);font-size:12px}.case-workspace{display:grid;grid-template-columns:260px 1fr;min-height:690px;border:1px solid var(--line);background:#fffaf0}.case-workspace>aside{border-right:1px solid var(--line);background:#f8f0e2}.case-workspace>aside button{display:grid;width:100%;gap:5px;padding:17px;border:0;border-bottom:1px solid var(--line);background:transparent;text-align:left;cursor:pointer}.case-workspace>aside button.active{background:#fffdf8;box-shadow:inset 4px 0 #9b2b24}.case-workspace>aside span{color:#9b2b24;font-size:11px}.case-workspace>aside strong{color:#4d382c;font-size:17px}.case-workspace>aside small{color:var(--muted)}.case-workspace>main{min-width:0;padding:26px}.person-header{display:flex;justify-content:space-between;align-items:start;margin-bottom:18px}.person-header span{color:#9b2b24}.person-header h2{margin:6px 0;color:#493328;font-family:"Noto Serif SC","Songti SC",serif;font-size:28px}.person-header p{color:var(--muted)}.progress-form{margin-top:16px}.impact-grid{display:grid;grid-template-columns:repeat(3,1fr);gap:1px;margin:16px 0;background:var(--line);border:1px solid var(--line)}.impact-grid span{padding:14px;background:#fffaf0;color:var(--muted);font-size:11px}.impact-grid b{display:block;margin-top:5px;color:var(--charcoal);font:24px Georgia,serif}.form-grid{display:grid;grid-template-columns:1fr 1fr;gap:0 14px}@media(max-width:1000px){.case-workspace{grid-template-columns:1fr}.case-workspace>aside{max-height:230px;overflow:auto}}@media(max-width:720px){.case-stats,.form-grid,.impact-grid{grid-template-columns:1fr}.ledger-toolbar,.person-header{align-items:stretch;flex-direction:column;gap:10px}}
</style>
