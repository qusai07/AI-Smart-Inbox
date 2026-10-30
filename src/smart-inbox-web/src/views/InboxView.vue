<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import Button from 'primevue/button'
import Select from 'primevue/select'
import InputText from 'primevue/inputtext'
import IconField from 'primevue/iconfield'
import InputIcon from 'primevue/inputicon'
import Paginator, { type PageState } from 'primevue/paginator'
import Message from 'primevue/message'
import ConfidenceRing from '../components/ConfidenceRing.vue'
import DeadlineChip from '../components/DeadlineChip.vue'
import { timeAgo } from '../components/Badges'
import { api, PRIORITIES, type DocumentFilter, type DocumentListItem, type DocumentStatus, type HandlingStatus, type DocumentSource } from '../api'
import { onStatusChanged } from '../realtime'
import { useDepartmentName, useDepartments } from '../stores/departments'
import { useStats } from '../stores/stats'

defineEmits<{ upload: [] }>()

const { t, locale } = useI18n()
const route = useRoute()
const router = useRouter()
const departments = useDepartments()
const departmentName = useDepartmentName()
const stats = useStats()

const filter = reactive<DocumentFilter>({
  status: (route.query.status as DocumentStatus) || null,
  handling: (route.query.handling as HandlingStatus) || null,
  priority: null,
  department: null,
  source: (route.query.source as DocumentSource) || null,
  related: route.query.related === 'true' ? true : route.query.related === 'false' ? false : null,
  duplicates: route.query.duplicates === 'true' || null,
  awaitingApproval: route.query.awaiting === 'true' || null,
  q: '',
  page: 1,
  pageSize: 20,
})
const rows = ref<DocumentListItem[]>([])
const total = ref(0)
const loading = ref(true)
const error = ref<string | null>(null)

interface Tab { status: DocumentStatus | null; handling: HandlingStatus | null; label: string; count?: number; group?: boolean }
const tabs = computed<Tab[]>(() => {
  const c = stats.countByStatus
  const h = stats.workload.reduce(
    (sum, w) => ({ New: sum.New + w.new, Acknowledged: sum.Acknowledged + w.acknowledged, Closed: sum.Closed + w.closed }),
    { New: 0, Acknowledged: 0, Closed: 0 },
  )
  return [
    { status: null, handling: null, label: t('inbox.all'), count: stats.stats?.totalDocuments },
    { status: 'NeedsReview', handling: null, label: t('status.NeedsReview'), count: c.NeedsReview },
    { status: 'Analyzed', handling: null, label: t('status.Analyzed'), count: c.Analyzed },
    { status: 'Processing', handling: null, label: t('status.Processing'), count: c.Processing },
    // After routing: follow the department's progress
    { status: 'Routed', handling: 'New', label: t('inbox.waiting'), count: h.New, group: true },
    { status: 'Routed', handling: 'Acknowledged', label: t('handling.Acknowledged'), count: h.Acknowledged },
    { status: 'Routed', handling: 'Closed', label: t('handling.Closed'), count: h.Closed },
    { status: 'Failed', handling: null, label: t('status.Failed'), count: c.Failed, group: true },
  ]
})
const isActive = (tab: Tab) => filter.status === tab.status && (filter.handling ?? null) === tab.handling
const priorityOptions = computed(() => PRIORITIES.map((p) => ({ label: t(`priority.${p}`), value: p })))
const departmentOptions = computed(() => departments.items.map((d) => ({ label: departmentName(d.id), value: d.id })))
const hasFilters = computed(() =>
  !!(filter.status || filter.priority || filter.department || filter.source || filter.related != null || filter.duplicates || filter.awaitingApproval || filter.q))
type RelatedChoice = 'linked' | 'duplicates' | 'alone'
const relatedOptions = computed(() => [
  { label: t('related.filterLinked'), value: 'linked' },
  { label: t('related.filterDuplicates'), value: 'duplicates' },
  { label: t('related.filterAlone'), value: 'alone' },
])
const relatedChoice = computed<RelatedChoice | null>({
  get: () => (filter.duplicates ? 'duplicates' : filter.related === true ? 'linked' : filter.related === false ? 'alone' : null),
  set: (v) => {
    filter.duplicates = v === 'duplicates' || null
    filter.related = v === 'linked' ? true : v === 'alone' ? false : null
  },
})
const sourceOptions = computed(() => (['Email', 'Upload', 'Api'] as const).map((s) => ({ label: t(`source.${s}`), value: s })))
const resultLabel = computed(() => (total.value === 1 ? t('inbox.results1') : t('inbox.results', { n: total.value })))

async function load() {
  error.value = null
  try {
    const result = await api.list(filter)
    rows.value = result.items
    total.value = result.total
  } catch (e) {
    error.value = (e as Error).message
  } finally {
    loading.value = false
  }
}

function setTab(tab: Pick<Tab, 'status' | 'handling'>) {
  const query: Record<string, string> = {}
  if (tab.status) query.status = tab.status
  if (tab.handling) query.handling = tab.handling
  router.replace({ name: 'inbox', query })
}

// The sidebar links and tabs drive the filters through the URL.
watch(() => [route.query.status, route.query.handling], ([s, h]) => {
  filter.status = (s as DocumentStatus) || null
  filter.handling = (h as HandlingStatus) || null
})
// Links from the dashboard and What's new set these; changing tabs keeps them.
watch(() => route.query.source, (v) => { if (v) filter.source = v as DocumentSource })
watch(() => route.query.related, (v) => { if (v) filter.related = v === 'true' })
watch(() => route.query.duplicates, (v) => { if (v) filter.duplicates = true })
watch(() => route.query.awaiting, (v) => { if (v) filter.awaitingApproval = true })

let searchTimer: ReturnType<typeof setTimeout> | undefined
watch(() => [filter.status, filter.handling, filter.priority, filter.department, filter.source, filter.related, filter.duplicates, filter.awaitingApproval], () => {
  filter.page = 1
  loading.value = true
  load()
})
watch(() => filter.q, () => {
  clearTimeout(searchTimer)
  searchTimer = setTimeout(() => {
    filter.page = 1
    load()
  }, 300)
})

function onPage(e: PageState) {
  filter.page = e.page + 1
  filter.pageSize = e.rows
  load()
  window.scrollTo({ top: 0, behavior: 'smooth' })
}

function clearFilters() {
  Object.assign(filter, { priority: null, department: null, source: null, related: null, duplicates: null, awaitingApproval: null, q: '' })
  setTab({ status: null, handling: null })
}

function summary(row: DocumentListItem) {
  return (locale.value === 'ar' ? row.summaryAr : row.summaryEn) ?? row.summaryEn
}

function title(row: DocumentListItem) {
  return row.sender ?? row.fileName
}

let refreshTimer: ReturnType<typeof setTimeout> | undefined
function refreshSoon() {
  clearTimeout(refreshTimer)
  refreshTimer = setTimeout(load, 400)
}
const unsubscribe = onStatusChanged((e) => {
  const row = rows.value.find((r) => r.id === e.documentId)
  if (row) row.status = e.status
  refreshSoon()
})

onMounted(() => {
  load()
  window.addEventListener('inbox:refresh', refreshSoon)
})
onUnmounted(() => {
  unsubscribe()
  window.removeEventListener('inbox:refresh', refreshSoon)
  clearTimeout(searchTimer)
  clearTimeout(refreshTimer)
})
</script>

<template>
  <header class="page-head">
    <div>
      <h1>{{ t('inbox.title') }}</h1>
      <p class="muted">{{ t('inbox.subtitle') }}</p>
    </div>
  </header>

  <div class="tabs" role="tablist">
    <button
      v-for="tab in tabs"
      :key="tab.label"
      type="button"
      role="tab"
      class="tab"
      :class="{ active: isActive(tab), group: tab.group }"
      :aria-selected="isActive(tab)"
      @click="setTab(tab)"
    >
      {{ tab.label }}
      <span v-if="tab.count" class="tab-count">{{ tab.count }}</span>
    </button>
  </div>

  <div class="toolbar">
    <IconField class="search">
      <InputIcon class="pi pi-search" />
      <InputText v-model="filter.q" :placeholder="t('filters.search')" fluid />
    </IconField>
    <Select v-model="filter.priority" :options="priorityOptions" option-label="label" option-value="value" :placeholder="t('filters.priority')" show-clear class="filter" />
    <Select v-model="filter.department" :options="departmentOptions" option-label="label" option-value="value" :placeholder="t('filters.department')" show-clear class="filter wide" />
    <Select v-model="filter.source" :options="sourceOptions" option-label="label" option-value="value" :placeholder="t('filters.source')" show-clear class="filter" />
    <Select v-model="relatedChoice" :options="relatedOptions" option-label="label" option-value="value" :placeholder="t('related.filter')" show-clear class="filter" />
    <Button
      :label="t('inbox.toApprove')"
      icon="pi pi-pen-to-square"
      size="small"
      :severity="filter.awaitingApproval ? undefined : 'secondary'"
      :outlined="!filter.awaitingApproval"
      :aria-pressed="!!filter.awaitingApproval"
      @click="filter.awaitingApproval = filter.awaitingApproval ? null : true"
    />
    <Button v-if="hasFilters" :label="t('actions.clear')" icon="pi pi-filter-slash" severity="secondary" text @click="clearFilters" />
  </div>

  <Message v-if="error" severity="error" class="mb">{{ t('errors.generic') }}: {{ error }}</Message>

  <section class="card list">
    <div v-if="!loading && rows.length" class="list-meta faint">{{ resultLabel }}</div>

    <!-- Loading skeleton -->
    <template v-if="loading">
      <div v-for="i in 5" :key="i" class="item skeleton-row">
        <span class="skeleton" style="width: 40px; height: 40px; border-radius: 50%" />
        <div class="body">
          <span class="skeleton" style="width: 35%; height: 14px" />
          <span class="skeleton" style="width: 80%; height: 12px; margin-top: 8px" />
        </div>
      </div>
    </template>

    <!-- Empty -->
    <div v-else-if="!rows.length" class="empty">
      <span class="empty-icon"><i :class="hasFilters ? 'pi pi-search' : 'pi pi-inbox'" /></span>
      <h3>{{ hasFilters ? t('empty.noResultsTitle') : t('empty.inboxTitle') }}</h3>
      <p class="muted">{{ hasFilters ? t('empty.noResults') : t('empty.inbox') }}</p>
      <Button v-if="hasFilters" :label="t('actions.clear')" severity="secondary" outlined @click="clearFilters" />
      <Button v-else :label="t('actions.upload')" icon="pi pi-plus" @click="$emit('upload')" />
    </div>

    <!-- Rows -->
    <RouterLink
      v-for="row in rows"
      v-else
      :key="row.id"
      :to="{ name: 'document', params: { id: row.id } }"
      class="item"
      :class="[`pr-${row.priority ?? 'none'}`, { processing: row.status === 'Processing' }]"
    >
      <span class="avatar" :class="`s-${row.status}`">
        <i v-if="row.status === 'Processing'" class="pi pi-spin pi-spinner" />
        <i v-else-if="row.status === 'Failed'" class="pi pi-exclamation-triangle" />
        <i v-else-if="row.status === 'Routed'" class="pi pi-check" />
        <template v-else>{{ title(row).trim().charAt(0).toUpperCase() }}</template>
      </span>

      <div class="body">
        <div class="line1">
          <strong class="title" dir="auto">{{ title(row) }}</strong>
          <i v-if="row.source === 'Email'" class="pi pi-envelope faint" :title="t('source.Email')" :aria-label="t('source.Email')" />
          <i v-if="row.possibleDuplicate" class="pi pi-exclamation-triangle dup" :title="t('related.possibleDuplicate')" :aria-label="t('related.possibleDuplicate')" />
          <i v-else-if="row.hasRelated" class="pi pi-link faint" :title="t('related.hasRelated')" :aria-label="t('related.hasRelated')" />
          <span v-if="row.priority && row.priority !== 'Normal'" class="pill" :class="`p-${row.priority}`"><span class="dot" />{{ t(`priority.${row.priority}`) }}</span>
          <span v-if="row.category" class="pill neutral">{{ t(`category.${row.category}`) }}</span>
          <span v-if="row.reply && row.reply !== 'Rejected' && row.reply !== 'Failed'" class="pill" :class="`r-${row.reply}`"><i class="pi pi-pen-to-square" /> {{ t(`reply.row.${row.reply}`) }}</span>
          <span class="grow" />
          <span class="time faint" :title="row.uploadedAt">{{ timeAgo(row.uploadedAt, locale) }}</span>
        </div>
        <p class="summary" dir="auto">
          <template v-if="row.status === 'Processing'"><span class="skeleton inline" /></template>
          <template v-else-if="row.status === 'Failed' && !summary(row)">{{ t('detail.failed') }} · {{ t('actions.retry') }}</template>
          <template v-else>{{ summary(row) ?? row.fileName }}</template>
        </p>
        <div class="line3">
          <span class="pill" :class="`s-${row.status}`">{{ t(`status.${row.status}`) }}</span>
          <span v-if="row.handling" class="pill" :class="`h-${row.handling}`">{{ t(`handling.${row.handling}`) }}</span>
          <span v-if="row.assigneeName" class="assignee"><i class="pi pi-user" /> {{ row.assigneeName }}</span>
          <DeadlineChip :date="row.deadline" :done="row.status === 'Routed'" />
        </div>
      </div>

      <div class="route">
        <template v-if="row.finalDepartmentId">
          <span class="route-label faint">{{ t('detail.routedTo') }}</span>
          <span class="route-dept routed"><i class="pi pi-check-circle" /> {{ departmentName(row.finalDepartmentId) }}</span>
        </template>
        <template v-else-if="row.suggestedDepartmentId">
          <ConfidenceRing :value="row.confidence ?? 0" :size="38" />
          <span class="route-text">
            <span class="route-label faint"><i class="pi pi-sparkles ai" /> {{ t('detail.suggestion') }}</span>
            <span class="route-dept">{{ departmentName(row.suggestedDepartmentId) }}</span>
          </span>
        </template>
      </div>

      <i class="pi pi-angle-right chevron flip-rtl" />
    </RouterLink>

    <Paginator
      v-if="total > filter.pageSize!"
      :rows="filter.pageSize"
      :total-records="total"
      :first="(filter.page! - 1) * filter.pageSize!"
      :rows-per-page-options="[10, 20, 50]"
      class="pager"
      @page="onPage"
    />
  </section>
</template>

<style scoped>
.page-head {
  margin-bottom: 20px;
}
.page-head h1 {
  font-size: 1.6rem;
  font-weight: 700;
  letter-spacing: -0.01em;
}
.page-head p {
  margin: 4px 0 0;
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
.tab:hover {
  color: var(--text);
}
/* Visual separator before a new group of tabs (department progress, failures) */
.tab.group {
  margin-inline-start: 10px;
  position: relative;
}
.tab.group::before {
  content: '';
  position: absolute;
  inset-inline-start: -7px;
  top: 12px;
  bottom: 12px;
  width: 1px;
  background: var(--border);
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

.toolbar {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-bottom: 16px;
}
.search {
  flex: 1 1 280px;
}
.filter {
  flex: 0 1 160px;
}
.filter.wide {
  flex-basis: 220px;
}
.mb {
  margin-bottom: 12px;
}

.list {
  overflow: hidden;
}
.list-meta {
  padding: 10px 20px;
  font-size: 0.78rem;
  border-bottom: 1px solid var(--border);
  background: var(--surface-2);
}

.item {
  position: relative;
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) minmax(170px, 230px) auto;
  align-items: center;
  gap: 16px;
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
/* Priority strip on the leading edge */
.item::before {
  content: '';
  position: absolute;
  inset-block: 10px;
  inset-inline-start: 0;
  width: 3px;
  border-radius: 0 3px 3px 0;
  background: transparent;
}
[dir='rtl'] .item::before {
  border-radius: 3px 0 0 3px;
}
.item.pr-Urgent::before { background: var(--urgent); }
.item.pr-High::before { background: var(--high); }

.avatar {
  width: 40px;
  height: 40px;
  border-radius: 50%;
  display: grid;
  place-items: center;
  font-weight: 700;
  font-size: 0.95rem;
  background: var(--surface-2);
  color: var(--muted);
  border: 1px solid var(--border);
}
.avatar.s-Processing { background: var(--ai-soft); color: var(--ai); border-color: transparent; }
.avatar.s-Failed { background: var(--urgent-soft); color: var(--urgent); border-color: transparent; }
.avatar.s-Routed { background: var(--ok-soft); color: var(--ok); border-color: transparent; }
.avatar.s-NeedsReview { background: var(--high-soft); color: var(--high); border-color: transparent; }
.avatar.s-Analyzed { background: var(--brand-soft); color: var(--brand); border-color: transparent; }

.body {
  min-width: 0;
  display: flex;
  flex-direction: column;
}
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
  margin: 4px 0 8px;
  color: var(--muted);
  font-size: 0.9rem;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
.skeleton.inline {
  display: inline-block;
  width: 70%;
  height: 12px;
  vertical-align: middle;
}
.line3 {
  display: flex;
  align-items: center;
  gap: 12px;
}

.assignee {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  font-size: 0.8rem;
  color: var(--muted);
}
.route {
  display: flex;
  align-items: center;
  gap: 10px;
  min-width: 0;
}
.route-text {
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.route-label {
  font-size: 0.72rem;
  white-space: nowrap;
}
.route-label .ai {
  color: var(--ai);
  font-size: 0.7rem;
}
.route-dept {
  font-weight: 600;
  font-size: 0.88rem;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.route > .route-label {
  display: none;
}
.route-dept.routed {
  color: var(--ok);
}
.chevron {
  color: var(--faint);
}

.skeleton-row {
  grid-template-columns: auto 1fr;
}

.empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  padding: 64px 24px;
  gap: 6px;
}
.empty-icon {
  width: 56px;
  height: 56px;
  border-radius: 50%;
  display: grid;
  place-items: center;
  background: var(--brand-soft);
  color: var(--brand);
  font-size: 1.4rem;
  margin-bottom: 8px;
}
.empty h3 {
  font-size: 1.05rem;
}
.empty p {
  margin: 0 0 14px;
  max-width: 380px;
}

.pager {
  border-top: 1px solid var(--border);
}

@media (max-width: 760px) {
  .item {
    grid-template-columns: auto minmax(0, 1fr);
    gap: 12px;
    padding: 14px 16px;
  }
  .route {
    grid-column: 2;
  }
  .chevron {
    display: none;
  }
  .filter,
  .filter.wide {
    flex: 1 1 140px;
  }
}
.dup {
  color: var(--high);
}
</style>
