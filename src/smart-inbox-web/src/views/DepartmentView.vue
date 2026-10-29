<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import Paginator, { type PageState } from 'primevue/paginator'
import Message from 'primevue/message'
import DeadlineChip from '../components/DeadlineChip.vue'
import HandlingActions from '../components/HandlingActions.vue'
import { timeAgo } from '../components/Badges'
import { api, type DocumentListItem, type HandlingStatus } from '../api'
import { onStatusChanged } from '../realtime'
import { useDepartments } from '../stores/departments'
import { useStats } from '../stores/stats'

const props = defineProps<{ departmentId: number }>()
defineEmits<{ upload: [] }>()

const { t, locale } = useI18n()
const departments = useDepartments()
const stats = useStats()

const handling = ref<HandlingStatus | null>('New')
const page = ref(1)
const pageSize = 20
const rows = ref<DocumentListItem[]>([])
const total = ref(0)
const loading = ref(true)
const error = ref<string | null>(null)

const department = computed(() => departments.byId.get(props.departmentId))
const name = computed(() => (department.value ? (locale.value === 'ar' ? department.value.nameAr : department.value.nameEn) : ''))
const otherName = computed(() => (department.value ? (locale.value === 'ar' ? department.value.nameEn : department.value.nameAr) : ''))
const workload = computed(() => stats.workloadFor(props.departmentId))
const tabs = computed(() => [
  { value: 'New' as const, label: t('handling.New'), count: workload.value.new },
  { value: 'Acknowledged' as const, label: t('handling.Acknowledged'), count: workload.value.acknowledged },
  { value: 'Closed' as const, label: t('handling.Closed'), count: workload.value.closed },
  { value: null, label: t('handling.all'), count: workload.value.new + workload.value.acknowledged + workload.value.closed },
])

async function load() {
  error.value = null
  try {
    const result = await api.list({ assigned: props.departmentId, handling: handling.value, page: page.value, pageSize })
    rows.value = result.items
    total.value = result.total
  } catch (e) {
    error.value = (e as Error).message
  } finally {
    loading.value = false
  }
}

function setTab(value: HandlingStatus | null) {
  handling.value = value
  page.value = 1
  loading.value = true
  load()
}

function onPage(e: PageState) {
  page.value = e.page + 1
  load()
}

function summary(row: DocumentListItem) {
  return (locale.value === 'ar' ? row.summaryAr : row.summaryEn) ?? row.summaryEn ?? row.fileName
}

let timer: ReturnType<typeof setTimeout> | undefined
const unsubscribe = onStatusChanged(() => {
  clearTimeout(timer)
  timer = setTimeout(load, 400)
})

watch(() => props.departmentId, () => setTab('New'))
onMounted(() => {
  load()
  stats.load()
})
onUnmounted(() => {
  unsubscribe()
  clearTimeout(timer)
})
</script>

<template>
  <header class="page-head">
    <span class="eyebrow">{{ t('handling.inbox') }}</span>
    <h1>{{ name }}</h1>
    <p class="muted">
      <span dir="auto">{{ otherName }}</span>
      <template v-if="department?.description"> · {{ department.description }}</template>
    </p>
    <p v-if="department?.email" class="faint small-email"><i class="pi pi-envelope" /> <span class="ltr">{{ department.email }}</span></p>
    <span v-if="workload.overdue" class="pill p-Urgent overdue"><i class="pi pi-clock" /> {{ t('handling.overdue', { n: workload.overdue }) }}</span>
  </header>

  <div class="tabs" role="tablist">
    <button
      v-for="tab in tabs"
      :key="tab.label"
      type="button"
      role="tab"
      class="tab"
      :class="{ active: handling === tab.value }"
      :aria-selected="handling === tab.value"
      @click="setTab(tab.value)"
    >
      {{ tab.label }}
      <span v-if="tab.count" class="tab-count">{{ tab.count }}</span>
    </button>
  </div>

  <Message v-if="error" severity="error">{{ t('errors.generic') }}: {{ error }}</Message>

  <section class="card list">
    <template v-if="loading">
      <div v-for="i in 4" :key="i" class="item">
        <span class="skeleton" style="width: 50%; height: 14px" />
        <span class="skeleton" style="width: 85%; height: 12px; margin-top: 8px" />
      </div>
    </template>

    <div v-else-if="!rows.length" class="empty">
      <span class="empty-icon"><i class="pi pi-check-circle" /></span>
      <p class="muted">{{ handling === 'New' ? t('handling.emptyNew') : t('handling.empty') }}</p>
    </div>

    <RouterLink
      v-for="row in rows"
      v-else
      :key="row.id"
      :to="{ name: 'document', params: { id: row.id } }"
      class="item"
      :class="`pr-${row.priority ?? 'none'}`"
    >
      <div class="line1">
        <strong class="title" dir="auto">{{ row.sender ?? row.fileName }}</strong>
        <span v-if="row.priority && row.priority !== 'Normal'" class="pill" :class="`p-${row.priority}`"><span class="dot" />{{ t(`priority.${row.priority}`) }}</span>
        <span v-if="row.category" class="pill neutral">{{ t(`category.${row.category}`) }}</span>
        <span v-if="row.handling && handling === null" class="pill" :class="`h-${row.handling}`">{{ t(`handling.${row.handling}`) }}</span>
        <span class="grow" />
        <span class="time faint">{{ timeAgo(row.uploadedAt, locale) }}</span>
      </div>
      <p class="summary" dir="auto">{{ summary(row) }}</p>
      <div class="line3">
        <DeadlineChip :date="row.deadline" :done="row.handling === 'Closed'" />
        <span class="grow" />
        <HandlingActions v-if="row.handling" :id="row.id" :status="row.handling" size="small" @changed="load" />
      </div>
    </RouterLink>

    <Paginator v-if="total > pageSize" :rows="pageSize" :total-records="total" :first="(page - 1) * pageSize" class="pager" @page="onPage" />
  </section>
</template>

<style scoped>
.page-head {
  margin-bottom: 18px;
  position: relative;
}
.page-head .eyebrow {
  margin: 0 0 4px;
  display: block;
}
.page-head h1 {
  font-size: 1.6rem;
  font-weight: 700;
}
.page-head p {
  margin: 4px 0 0;
}
.small-email {
  font-size: 0.82rem;
  display: flex;
  align-items: center;
  gap: 6px;
}
.overdue {
  position: absolute;
  top: 0;
  inset-inline-end: 0;
}

.tabs {
  display: flex;
  gap: 4px;
  border-bottom: 1px solid var(--border);
  margin-bottom: 16px;
  overflow-x: auto;
  scrollbar-width: none;
}
.tab {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 10px 12px;
  border: 0;
  background: none;
  font: inherit;
  font-weight: 500;
  color: var(--muted);
  border-bottom: 2px solid transparent;
  margin-bottom: -1px;
  cursor: pointer;
  white-space: nowrap;
}
.tab.active {
  color: var(--brand);
  border-bottom-color: var(--brand);
  font-weight: 600;
}
.tab-count {
  font-size: 0.72rem;
  font-weight: 600;
  padding: 1px 7px;
  border-radius: 10px;
  background: var(--surface-2);
  border: 1px solid var(--border);
  color: var(--muted);
}
.tab.active .tab-count {
  background: var(--brand-soft);
  border-color: transparent;
  color: var(--brand);
}

.list {
  overflow: hidden;
}
.item {
  position: relative;
  display: flex;
  flex-direction: column;
  padding: 16px 20px;
  border-bottom: 1px solid var(--border);
  text-decoration: none;
  transition: background 0.12s;
}
.item:last-of-type {
  border-bottom: 0;
}
.item:hover {
  background: var(--surface-hover);
}
.item::before {
  content: '';
  position: absolute;
  inset-block: 10px;
  inset-inline-start: 0;
  width: 3px;
  background: transparent;
}
.item.pr-Urgent::before { background: var(--urgent); }
.item.pr-High::before { background: var(--high); }
.line1 {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}
.title {
  font-weight: 600;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  min-width: 0;
}
.time {
  font-size: 0.78rem;
  white-space: nowrap;
}
.summary {
  margin: 4px 0 10px;
  color: var(--muted);
  font-size: 0.9rem;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
.line3 {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}
.empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 56px 24px;
  gap: 8px;
  text-align: center;
}
.empty-icon {
  width: 52px;
  height: 52px;
  border-radius: 50%;
  display: grid;
  place-items: center;
  background: var(--ok-soft);
  color: var(--ok);
  font-size: 1.3rem;
}
.pager {
  border-top: 1px solid var(--border);
}
@media (max-width: 640px) {
  .overdue {
    position: static;
    margin-top: 8px;
  }
}
</style>
