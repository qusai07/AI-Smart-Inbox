<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Select from 'primevue/select'
import Textarea from 'primevue/textarea'
import { api, type OutgoingLetter, type OutgoingStatus, type RegisterFilter, type SentVia } from '../api'
import { useAuth } from '../stores/auth'
import { useDepartments, useDepartmentName } from '../stores/departments'
import { formatDate, formatDateTime } from '../components/Badges'

const { t, locale } = useI18n()
const toast = useToast()
const route = useRoute()
const auth = useAuth()
const departments = useDepartments()
const departmentName = useDepartmentName()

const thisYear = new Date().getFullYear()
const filter = reactive<RegisterFilter>({
  year: thisYear,
  department: null,
  status: null,
  q: typeof route.query.q === 'string' ? route.query.q : '',
})
const letters = ref<OutgoingLetter[]>([])
const total = ref(0)
const page = ref(1)
const loading = ref(false)

const yearOptions = computed(() => [
  { label: t('register.allYears'), value: null },
  ...[0, 1, 2, 3].map((i) => ({ label: String(thisYear - i), value: thisYear - i })),
])
const statusOptions = computed(() => [
  { label: t('register.allStatuses'), value: null },
  ...(['Issued', 'Sent', 'Cancelled'] as OutgoingStatus[]).map((s) => ({ label: t(`register.status.${s}`), value: s })),
])
const departmentOptions = computed(() => [
  { label: t('register.allDepartments'), value: null },
  ...departments.items.map((d) => ({ label: departmentName(d.id), value: d.id })),
])
const csvUrl = computed(() => api.registerCsvUrl(filter))

async function load(reset = true) {
  if (reset) page.value = 1
  loading.value = true
  try {
    const result = await api.register({ ...filter, page: page.value, pageSize: 50 })
    letters.value = reset ? result.items : [...letters.value, ...result.items]
    total.value = result.total
  } catch (e) {
    toast.add({ severity: 'error', summary: t('errors.generic'), detail: (e as Error).message, life: 5000 })
  } finally {
    loading.value = false
  }
}

function more() {
  page.value++
  load(false)
}

let searchTimer: ReturnType<typeof setTimeout> | undefined
watch(() => filter.q, () => {
  clearTimeout(searchTimer)
  searchTimer = setTimeout(() => load(), 300)
})
watch(() => [filter.year, filter.department, filter.status], () => load())

function subject(l: OutgoingLetter) {
  return (locale.value === 'ar' ? l.subjectAr ?? l.subjectEn : l.subjectEn ?? l.subjectAr) ?? '—'
}

function replace(updated: OutgoingLetter) {
  letters.value = letters.value.map((l) => (l.id === updated.id ? updated : l))
}

function fail(e: unknown) {
  toast.add({ severity: 'error', summary: t('errors.generic'), detail: (e as Error).message, life: 6000 })
}

// ── New outgoing letter ──────────────────────────────────────────────────────
const issueOpen = ref(false)
const issuing = ref(false)
const issueForm = reactive({ departmentId: null as number | null, recipient: '', subjectEn: '', subjectAr: '', documentId: null as string | null })

function openIssue() {
  Object.assign(issueForm, {
    departmentId: auth.isDepartment ? auth.me?.departmentId ?? null : null,
    recipient: '',
    subjectEn: '',
    subjectAr: '',
    documentId: typeof route.query.document === 'string' ? route.query.document : null,
  })
  issueOpen.value = true
}

const canIssue = computed(() => !!issueForm.departmentId && !!issueForm.recipient.trim() && !!(issueForm.subjectEn.trim() || issueForm.subjectAr.trim()))

async function issue() {
  issuing.value = true
  try {
    const letter = await api.issueOutgoing({
      departmentId: issueForm.departmentId!,
      recipient: issueForm.recipient.trim(),
      subjectEn: issueForm.subjectEn.trim() || null,
      subjectAr: issueForm.subjectAr.trim() || null,
      documentId: issueForm.documentId,
    })
    issueOpen.value = false
    toast.add({ severity: 'success', summary: t('register.issued', { number: letter.number }), life: 6000 })
    await load()
  } catch (e) {
    fail(e)
  } finally {
    issuing.value = false
  }
}

// ── Mark as sent ─────────────────────────────────────────────────────────────
const sentFor = ref<OutgoingLetter | null>(null)
const saving = ref(false)
const sentForm = reactive({ via: 'Email' as SentVia, date: '', note: '' })
const viaOptions = computed(() => (['Email', 'Post', 'Hand', 'Other'] as SentVia[]).map((v) => ({ label: t(`register.via.${v}`), value: v })))

function openSent(l: OutgoingLetter) {
  Object.assign(sentForm, { via: 'Email', date: new Date().toISOString().slice(0, 10), note: '' })
  sentFor.value = l
}

async function markSent() {
  saving.value = true
  try {
    replace(await api.markSent(sentFor.value!.id, {
      via: sentForm.via,
      sentAt: sentForm.date ? new Date(`${sentForm.date}T12:00:00`).toISOString() : null,
      note: sentForm.note.trim() || null,
    }))
    sentFor.value = null
  } catch (e) {
    fail(e)
  } finally {
    saving.value = false
  }
}

// ── Cancel ───────────────────────────────────────────────────────────────────
const cancelFor = ref<OutgoingLetter | null>(null)
const cancelReason = ref('')

function openCancel(l: OutgoingLetter) {
  cancelReason.value = ''
  cancelFor.value = l
}

async function cancel() {
  saving.value = true
  try {
    replace(await api.cancelOutgoing(cancelFor.value!.id, cancelReason.value.trim()))
    cancelFor.value = null
  } catch (e) {
    fail(e)
  } finally {
    saving.value = false
  }
}

onMounted(async () => {
  await departments.load()
  await load()
  if (route.query.document) openIssue()
})
</script>

<template>
  <header class="page-head head-row">
    <div>
      <h1>{{ t('register.title') }}</h1>
      <p class="muted">{{ auth.isDepartment ? t('register.subtitleDept') : t('register.subtitle') }}</p>
    </div>
    <div class="row">
      <a :href="csvUrl" class="p-button p-button-secondary p-button-outlined p-button-sm" download>
        <i class="pi pi-download" /> {{ t('register.export') }}
      </a>
      <Button :label="t('register.new')" icon="pi pi-plus" size="small" @click="openIssue" />
    </div>
  </header>

  <section class="card">
    <div class="filters">
      <span class="search">
        <i class="pi pi-search" />
        <InputText v-model="filter.q" :placeholder="t('register.search')" class="grow" :aria-label="t('register.search')" />
      </span>
      <Select v-model="filter.year" :options="yearOptions" option-label="label" option-value="value" size="small" :aria-label="t('register.year')" />
      <Select v-if="!auth.isDepartment" v-model="filter.department" :options="departmentOptions" option-label="label" option-value="value" size="small" :aria-label="t('filters.department')" />
      <Select v-model="filter.status" :options="statusOptions" option-label="label" option-value="value" size="small" :aria-label="t('filters.status')" />
      <span class="small muted count">{{ t('register.count', { n: total }) }}</span>
    </div>

    <p v-if="!letters.length && !loading" class="muted empty">{{ t('register.none') }}</p>
    <div v-else class="table-wrap">
      <table class="table">
        <thead>
          <tr>
            <th>{{ t('register.number') }}</th>
            <th>{{ t('register.date') }}</th>
            <th>{{ t('register.recipient') }}</th>
            <th>{{ t('register.subject') }}</th>
            <th>{{ t('filters.department') }}</th>
            <th>{{ t('filters.status') }}</th>
            <th />
          </tr>
        </thead>
        <tbody>
          <tr v-for="l in letters" :key="l.id" :class="{ cancelled: l.status === 'Cancelled' }">
            <td class="number ltr nowrap">{{ l.number }}</td>
            <td class="small nowrap">
              {{ formatDate(l.issuedAt, locale) }}
              <div class="faint">{{ l.issuedBy }}</div>
            </td>
            <td class="small" dir="auto">{{ l.recipient ?? '—' }}</td>
            <td class="small">
              <div dir="auto">{{ subject(l) }}</div>
              <div class="links">
                <RouterLink v-if="l.documentId" :to="{ name: 'document', params: { id: l.documentId } }">
                  <i class="pi pi-inbox" /> {{ l.replyDraftId ? t('register.replyTo') : t('register.linked') }}
                </RouterLink>
              </div>
            </td>
            <td class="small">{{ departmentName(l.departmentId) }}</td>
            <td class="small">
              <span class="pill" :class="`o-${l.status}`">{{ t(`register.status.${l.status}`) }}</span>
              <div v-if="l.status === 'Sent'" class="faint">
                {{ t(`register.via.${l.sentVia}`) }} · {{ formatDate(l.sentAt, locale) }}
                <span v-if="l.sentNote" dir="auto"> · {{ l.sentNote }}</span>
              </div>
              <div v-if="l.status === 'Cancelled'" class="faint" :title="l.cancelledAt ? formatDateTime(l.cancelledAt, locale) : ''">
                <span dir="auto">{{ l.cancelReason }}</span> — {{ l.cancelledBy }}
              </div>
            </td>
            <td class="end nowrap">
              <Button v-if="l.status === 'Issued'" :label="t('register.markSent')" icon="pi pi-send" size="small" text @click="openSent(l)" />
              <Button v-if="auth.isDesk && l.status !== 'Cancelled'" :label="t('register.cancel')" icon="pi pi-ban" size="small" text severity="danger" @click="openCancel(l)" />
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <div v-if="letters.length < total" class="more">
      <Button :label="t('webhooks.more')" severity="secondary" text size="small" :loading="loading" @click="more" />
    </div>
  </section>

  <!-- New outgoing letter -->
  <Dialog v-model:visible="issueOpen" modal :header="t('register.new')" :style="{ width: 'min(520px, 94vw)' }">
    <p class="small muted">{{ t('register.newHint') }}</p>
    <div class="form">
      <label>
        <span>{{ t('filters.department') }}</span>
        <Select v-model="issueForm.departmentId" :options="departmentOptions.slice(1)" option-label="label" option-value="value" :disabled="auth.isDepartment" />
      </label>
      <label>
        <span>{{ t('register.recipient') }}</span>
        <InputText v-model="issueForm.recipient" maxlength="300" dir="auto" />
      </label>
      <label>
        <span>{{ t('register.subjectEn') }}</span>
        <InputText v-model="issueForm.subjectEn" maxlength="300" dir="ltr" />
      </label>
      <label>
        <span>{{ t('register.subjectAr') }}</span>
        <InputText v-model="issueForm.subjectAr" maxlength="300" dir="rtl" />
      </label>
      <p v-if="issueForm.documentId" class="small muted"><i class="pi pi-link" /> {{ t('register.linkedHint') }}</p>
    </div>
    <template #footer>
      <Button :label="t('actions.cancel')" severity="secondary" text @click="issueOpen = false" />
      <Button :label="t('register.issue')" icon="pi pi-hashtag" :disabled="!canIssue" :loading="issuing" @click="issue" />
    </template>
  </Dialog>

  <!-- Mark as sent -->
  <Dialog :visible="!!sentFor" modal :header="t('register.markSentTitle', { number: sentFor?.number })" :style="{ width: 'min(440px, 94vw)' }" @update:visible="sentFor = null">
    <div class="form">
      <label>
        <span>{{ t('register.how') }}</span>
        <Select v-model="sentForm.via" :options="viaOptions" option-label="label" option-value="value" />
      </label>
      <label>
        <span>{{ t('register.when') }}</span>
        <InputText v-model="sentForm.date" type="date" />
      </label>
      <label>
        <span>{{ t('register.note') }}</span>
        <InputText v-model="sentForm.note" maxlength="500" dir="auto" :placeholder="t('register.notePlaceholder')" />
      </label>
    </div>
    <template #footer>
      <Button :label="t('actions.cancel')" severity="secondary" text @click="sentFor = null" />
      <Button :label="t('register.markSent')" icon="pi pi-send" :loading="saving" @click="markSent" />
    </template>
  </Dialog>

  <!-- Cancel -->
  <Dialog :visible="!!cancelFor" modal :header="t('register.cancelTitle', { number: cancelFor?.number })" :style="{ width: 'min(440px, 94vw)' }" @update:visible="cancelFor = null">
    <p class="small muted">{{ t('register.cancelHint') }}</p>
    <Textarea v-model="cancelReason" rows="3" maxlength="500" class="full" dir="auto" :placeholder="t('register.reason')" :aria-label="t('register.reason')" />
    <template #footer>
      <Button :label="t('actions.cancel')" severity="secondary" text @click="cancelFor = null" />
      <Button :label="t('register.cancel')" icon="pi pi-ban" severity="danger" :disabled="!cancelReason.trim()" :loading="saving" @click="cancel" />
    </template>
  </Dialog>
</template>

<style scoped>
.head-row {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
}
.row {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}
.row a {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  text-decoration: none;
}
.filters {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  padding: 12px 16px;
  border-bottom: 1px solid var(--border);
}
.search {
  position: relative;
  display: flex;
  flex: 1 1 240px;
}
.search .pi {
  position: absolute;
  inset-inline-start: 10px;
  top: 50%;
  transform: translateY(-50%);
  color: var(--faint);
}
.search :deep(input) {
  padding-inline-start: 32px;
  width: 100%;
}
.count {
  margin-inline-start: auto;
}
.empty {
  padding: 24px 16px;
}
.table-wrap {
  overflow-x: auto;
}
.table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.9rem;
}
.table th {
  text-align: start;
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--faint);
  padding: 8px 12px;
  background: var(--surface-2);
  border-bottom: 1px solid var(--border);
}
.table td {
  padding: 10px 12px;
  border-bottom: 1px solid var(--border);
  vertical-align: top;
}
.table tr:last-child td {
  border-bottom: 0;
}
.number {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-weight: 600;
}
tr.cancelled .number {
  text-decoration: line-through;
  color: var(--muted);
}
.links {
  margin-top: 4px;
}
.links a {
  font-size: 0.78rem;
}
.nowrap {
  white-space: nowrap;
}
.end {
  text-align: end;
}
.small {
  font-size: 0.82rem;
}
.more {
  padding: 8px 16px;
}
.pill.o-Issued {
  background: var(--brand-soft);
  color: var(--brand);
}
.pill.o-Sent {
  background: var(--ok-soft);
  color: var(--ok);
}
.pill.o-Cancelled {
  background: var(--surface-2);
  color: var(--muted);
}
.form {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.form label {
  display: flex;
  flex-direction: column;
  gap: 4px;
  font-size: 0.82rem;
  font-weight: 600;
}
.full {
  width: 100%;
}
</style>
