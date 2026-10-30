<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import Select from 'primevue/select'
import Textarea from 'primevue/textarea'
import ConfidenceRing from '../components/ConfidenceRing.vue'
import DeadlineChip from '../components/DeadlineChip.vue'
import HandlingActions from '../components/HandlingActions.vue'
import ActivityTimeline from '../components/ActivityTimeline.vue'
import CommentThread from '../components/CommentThread.vue'
import ReplyPanel from '../components/ReplyPanel.vue'
import type { Member } from '../api'
import { formatDate, formatDateTime } from '../components/Badges'
import { api, type DocumentDetail } from '../api'
import { onStatusChanged } from '../realtime'
import { useDepartmentName, useDepartments } from '../stores/departments'
import { useStats } from '../stores/stats'
import { useAuth } from '../stores/auth'

const props = defineProps<{ id: string }>()
defineEmits<{ upload: [] }>()

const { t, locale } = useI18n()
const toast = useToast()
const departments = useDepartments()
const departmentName = useDepartmentName()
const stats = useStats()
const auth = useAuth()

const doc = ref<DocumentDetail | null>(null)
const error = ref<string | null>(null)
const busy = ref(false)
const choosing = ref(false)
const selectedDepartment = ref<number | null>(null)
const note = ref('')
const summaryLang = ref<'en' | 'ar'>(locale.value === 'ar' ? 'ar' : 'en')
const view = ref<'file' | 'text'>('file')

const analysis = computed(() => doc.value?.analysis ?? null)
const isProcessing = computed(() => doc.value?.status === 'Processing' || doc.value?.status === 'Uploaded')
const isImage = computed(() => doc.value?.contentType.startsWith('image/'))
const isPdf = computed(() => doc.value?.contentType === 'application/pdf')
const hasPreview = computed(() => isPdf.value || isImage.value)
const lowConfidence = computed(() => !!analysis.value && analysis.value.confidence < 0.6)
const suggested = computed(() => analysis.value?.suggestedDepartmentId ?? null)
const routed = computed(() => doc.value?.routing ?? null)
const departmentOptions = computed(() => departments.items.map((d) => ({ label: departmentName(d.id), value: d.id })))
const summaryText = computed(() => (summaryLang.value === 'ar' ? analysis.value?.summaryAr : analysis.value?.summaryEn))
const title = computed(() => analysis.value?.sender ?? doc.value?.fileName ?? '')
const fileIcon = computed(() => {
  const type = doc.value?.contentType ?? ''
  if (type === 'application/pdf') return 'pi pi-file-pdf'
  if (type.startsWith('image/')) return 'pi pi-image'
  if (type.includes('wordprocessing')) return 'pi pi-file-word'
  if (type === 'message/rfc822') return 'pi pi-envelope'
  return 'pi pi-file'
})
// ── Assignment ────────────────────────────────────────────────────
const members = ref<Member[]>([])
const membersFor = ref<number | null>(null)
const assigning = ref(false)
const canAssign = computed(
  () => !!doc.value?.handling && (auth.isDesk || auth.me?.departmentId === doc.value.handling.departmentId),
)
const memberOptions = computed(() => {
  const list = members.value.map((m) => ({ label: m.id === auth.me?.id ? `${m.displayName} (${t('work.me')})` : m.displayName, value: m.id }))
  // Keep showing a current assignee who is no longer active.
  const a = doc.value?.assignee
  if (a && !list.some((o) => o.value === a.id)) list.unshift({ label: a.name, value: a.id })
  return list
})

watch(
  () => (canAssign.value ? doc.value?.handling?.departmentId : null),
  async (dep) => {
    if (!dep || membersFor.value === dep) return
    membersFor.value = dep
    try {
      members.value = await api.members(dep)
    } catch {
      members.value = []
    }
  },
)

async function assign(userId: string | null) {
  if (!doc.value || userId === (doc.value.assignee?.id ?? null)) return
  assigning.value = true
  try {
    await api.assign(doc.value.id, userId)
    const name = members.value.find((m) => m.id === userId)?.displayName
    toast.add({ severity: 'success', summary: userId ? t('work.assigned', { name }) : t('work.unassignedDone'), life: 2500 })
    stats.refreshSoon()
    await load()
  } catch (e) {
    toast.add({ severity: 'error', summary: t('errors.generic'), detail: (e as Error).message, life: 5000 })
  } finally {
    assigning.value = false
  }
}

const latestEmail = computed(
  () => doc.value?.notifications.find((n) => n.kind === 'Routed' && n.departmentId === doc.value?.handling?.departmentId) ?? null,
)
// Report-back email to the router, shown while the document is closed.
const closedEmail = computed(() =>
  doc.value?.handling?.status === 'Closed' ? (doc.value.notifications.find((n) => n.kind === 'Closed') ?? null) : null,
)
// The outbox worker sends within seconds; poll briefly while an email is still queued.
let emailPoll: ReturnType<typeof setTimeout> | undefined
watch([latestEmail, closedEmail], (emails) => {
  clearTimeout(emailPoll)
  if (emails.some((n) => n?.status === 'Pending' && n.attempts === 0)) emailPoll = setTimeout(load, 4000)
})
onUnmounted(() => clearTimeout(emailPoll))
const engine = computed(() => {
  const model = analysis.value?.modelUsed ?? ''
  if (model.startsWith('rules')) return { icon: 'pi pi-sitemap', label: t('engine.rules'), kind: 'rules' }
  if (model.startsWith('claude')) return { icon: 'pi pi-cloud', label: `${t('engine.cloud')} · ${model}`, kind: 'cloud' }
  return { icon: 'pi pi-desktop', label: `${t('engine.local')} · ${model}`, kind: 'local' }
})
const languageName = computed(() => {
  const l = analysis.value?.language
  if (l === 'ar') return locale.value === 'ar' ? 'العربية' : 'Arabic'
  if (l === 'en') return locale.value === 'ar' ? 'الإنجليزية' : 'English'
  if (l === 'mixed') return locale.value === 'ar' ? 'مختلطة' : 'Mixed'
  return '—'
})

async function load() {
  try {
    doc.value = await api.get(props.id)
    error.value = null
    selectedDepartment.value = doc.value.routing?.finalDepartmentId ?? doc.value.analysis?.suggestedDepartmentId ?? null
    if (!hasPreview.value) view.value = 'text'
    // Unsure or no suggestion: open the picker straight away (desk only).
    choosing.value = auth.isDesk && !doc.value.routing && !!doc.value.analysis && (lowConfidence.value || !suggested.value)
  } catch (e) {
    error.value = (e as Error).message
  }
}

async function route(departmentId: number | null) {
  if (!departmentId) return
  busy.value = true
  try {
    await api.route(props.id, departmentId, note.value || null)
    toast.add({ severity: 'success', summary: `${t('detail.routedTo')} ${departmentName(departmentId)}`, life: 3000 })
    note.value = ''
    stats.refreshSoon()
    await load()
    choosing.value = false
  } catch (e) {
    toast.add({ severity: 'error', summary: t('errors.generic'), detail: (e as Error).message, life: 5000 })
  } finally {
    busy.value = false
  }
}

async function reanalyze() {
  busy.value = true
  try {
    await api.reanalyze(props.id)
    await load()
  } finally {
    busy.value = false
  }
}

const unsubscribe = onStatusChanged((e) => {
  if (e.documentId === props.id) load()
})
onUnmounted(unsubscribe)
watch(() => props.id, load, { immediate: true })
</script>

<template>
  <RouterLink :to="auth.home" class="back"><i class="pi pi-arrow-left flip-rtl" /> {{ t('actions.back') }}</RouterLink>

  <div v-if="error" class="card card-pad error">{{ t('errors.generic') }}: {{ error }}</div>

  <!-- Skeleton while the first load is in flight -->
  <div v-else-if="!doc" class="head">
    <span class="skeleton" style="width: 40%; height: 28px" />
    <span class="skeleton" style="width: 25%; height: 14px; margin-top: 10px" />
  </div>

  <template v-else>
    <header class="head">
      <div class="head-main">
        <h1 dir="auto">{{ title }}</h1>
        <div class="row pills">
          <span class="pill" :class="`s-${doc.status}`">
            <i v-if="isProcessing" class="pi pi-spin pi-spinner" />{{ t(`status.${doc.status}`) }}
          </span>
          <span v-if="analysis" class="pill" :class="`p-${analysis.priority}`"><span class="dot" />{{ t(`priority.${analysis.priority}`) }}</span>
          <span v-if="analysis" class="pill neutral">{{ t(`category.${analysis.category}`) }}</span>
          <DeadlineChip v-if="analysis" :date="analysis.deadline" :done="!!routed" />
        </div>
      </div>
      <Button
        v-if="!isProcessing && auth.isDesk"
        :label="t('actions.reanalyze')"
        icon="pi pi-refresh"
        severity="secondary"
        outlined
        size="small"
        :loading="busy && !choosing"
        @click="reanalyze"
      />
    </header>

    <div class="layout">
      <!-- ── AI analysis ───────────────────────────────────────── -->
      <div class="insights">
        <!-- Processing -->
        <section v-if="isProcessing" class="card card-pad processing">
          <div class="ai-head">
            <span class="ai-badge"><i class="pi pi-sparkles" /></span>
            <div>
              <strong>{{ t('detail.processing') }}</strong>
              <p class="muted">{{ t('detail.processingHint') }}</p>
            </div>
          </div>
          <span class="skeleton" style="height: 14px; width: 90%" />
          <span class="skeleton" style="height: 14px; width: 75%" />
          <span class="skeleton" style="height: 14px; width: 60%" />
        </section>

        <!-- Failed -->
        <section v-else-if="doc.status === 'Failed'" class="card card-pad failed">
          <i class="pi pi-exclamation-triangle" />
          <div class="grow">
            <strong>{{ t('detail.failed') }}</strong>
            <p class="muted">{{ doc.errorMessage ?? t('errors.generic') }}</p>
          </div>
          <Button v-if="auth.isDesk" :label="t('actions.retry')" icon="pi pi-refresh" size="small" :loading="busy" @click="reanalyze" />
        </section>

        <template v-if="analysis && !isProcessing">
          <!-- Decision -->
          <section class="card decision" :class="{ done: !!routed, unsure: !routed && lowConfidence }">
            <!-- Already routed -->
            <div v-if="routed && !choosing" class="decision-body">
              <span class="decision-icon ok"><i class="pi pi-check" /></span>
              <div class="grow">
                <span class="eyebrow">{{ t('detail.routedTo') }}</span>
                <h2>{{ departmentName(routed.finalDepartmentId) }}</h2>
                <p class="muted small">
                  {{ routed.wasOverridden ? t('detail.overridden') : t('detail.acceptedAi') }} · {{ formatDateTime(routed.decidedAt, locale) }}
                </p>
                <p v-if="routed.note" class="note" dir="auto">“{{ routed.note }}”</p>
              </div>
              <Button v-if="auth.isDesk" :label="t('detail.reroute')" severity="secondary" text size="small" @click="choosing = true" />
            </div>

            <!-- Department handling -->
            <div v-if="routed && !choosing && doc.handling" class="handling">
              <div class="handling-row">
                <span class="muted small">{{ t('handling.stateLabel') }}</span>
                <span class="pill" :class="`h-${doc.handling.status}`">{{ t(`handling.${doc.handling.status}`) }}</span>
                <span v-if="doc.handling.updatedAt" class="faint small">
                  <template v-if="doc.handling.by">{{ doc.handling.by }} · </template>{{ formatDateTime(doc.handling.updatedAt, locale) }}
                </span>
                <span class="grow" />
                <RouterLink :to="{ name: 'department', params: { departmentId: doc.handling.departmentId } }" class="small dept-link">
                  {{ t('handling.openInbox') }} <i class="pi pi-arrow-right flip-rtl" />
                </RouterLink>
              </div>
              <div class="assignee-row">
                <span class="muted small">{{ t('work.assignee') }}</span>
                <Select
                  v-if="canAssign"
                  :model-value="doc.assignee?.id ?? null"
                  :options="memberOptions"
                  option-label="label"
                  option-value="value"
                  :placeholder="t('work.assignPlaceholder')"
                  show-clear
                  size="small"
                  class="assignee-select"
                  :loading="assigning"
                  :disabled="assigning"
                  @update:model-value="assign"
                />
                <strong v-else class="small">{{ doc.assignee?.name ?? t('work.unassigned') }}</strong>
              </div>
              <p v-if="doc.handling.note" class="note" dir="auto">
                <span class="muted">{{ t('handling.closedNote') }}:</span> {{ doc.handling.note }}
              </p>
              <HandlingActions :id="doc.id" :status="doc.handling.status" size="small" @changed="load" />
              <p v-if="latestEmail" class="email-status small" :class="latestEmail.status">
                <i :class="latestEmail.status === 'Sent' ? 'pi pi-envelope' : latestEmail.status === 'Failed' ? 'pi pi-exclamation-triangle' : 'pi pi-clock'" />
                <template v-if="latestEmail.status === 'Sent'">{{ t('notify.sent', { to: latestEmail.to }) }} · {{ formatDateTime(latestEmail.sentAt!, locale) }}</template>
                <template v-else-if="latestEmail.status === 'Failed'">{{ t('notify.failed', { to: latestEmail.to }) }}: {{ latestEmail.error }}</template>
                <template v-else>
                  {{ t('notify.pending', { to: latestEmail.to }) }}
                  <span v-if="latestEmail.attempts" class="faint">({{ t('notify.retrying', { n: latestEmail.attempts }) }}: {{ latestEmail.error }})</span>
                </template>
              </p>
              <p v-if="closedEmail" class="email-status small" :class="closedEmail.status">
                <i :class="closedEmail.status === 'Sent' ? 'pi pi-reply' : closedEmail.status === 'Failed' ? 'pi pi-exclamation-triangle' : 'pi pi-clock'" />
                <template v-if="closedEmail.status === 'Sent'">{{ t('notify.closedSent', { to: closedEmail.to }) }}</template>
                <template v-else-if="closedEmail.status === 'Failed'">{{ t('notify.closedFailed', { to: closedEmail.to }) }}: {{ closedEmail.error }}</template>
                <template v-else>{{ t('notify.closedPending', { to: closedEmail.to }) }}</template>
              </p>
            </div>

            <!-- AI suggestion -->
            <div v-else class="decision-body">
              <template v-if="suggested">
                <ConfidenceRing :value="analysis.confidence" :size="56" :stroke="5" />
                <div class="grow">
                  <span class="eyebrow"><i class="pi pi-sparkles ai" /> {{ t('detail.suggestion') }}</span>
                  <h2>{{ departmentName(suggested) }}</h2>
                  <p v-if="analysis.reasoning" class="reasoning" dir="auto">{{ analysis.reasoning }}</p>
                </div>
              </template>
              <template v-else>
                <span class="decision-icon warn"><i class="pi pi-question" /></span>
                <div class="grow">
                  <h2 class="small-h">{{ t('detail.noSuggestion') }}</h2>
                </div>
              </template>
            </div>

            <div v-if="!routed && lowConfidence && suggested" class="unsure-note">
              <i class="pi pi-exclamation-circle" /> {{ t('detail.lowConfidence') }}
            </div>

            <!-- Actions -->
            <div v-if="auth.isDesk && (!routed || choosing)" class="decision-actions">
              <template v-if="!choosing">
                <Button :label="t('actions.accept')" icon="pi pi-check" :loading="busy" @click="route(suggested)" />
                <Button :label="t('actions.override')" severity="secondary" outlined @click="choosing = true" />
              </template>
              <div v-else class="picker">
                <Select
                  v-model="selectedDepartment"
                  :options="departmentOptions"
                  option-label="label"
                  option-value="value"
                  :placeholder="t('filters.department')"
                  fluid
                >
                  <template #option="{ option }">
                    <span class="opt">
                      {{ option.label }}
                      <span v-if="option.value === suggested" class="opt-ai"><i class="pi pi-sparkles" /> AI</span>
                    </span>
                  </template>
                </Select>
                <Textarea v-model="note" :placeholder="t('detail.note')" rows="2" fluid auto-resize dir="auto" />
                <div class="row">
                  <Button :label="t('actions.route')" icon="pi pi-send" :loading="busy" :disabled="!selectedDepartment" @click="route(selectedDepartment)" />
                  <Button v-if="suggested || routed" :label="t('actions.cancel')" severity="secondary" text @click="choosing = false" />
                </div>
              </div>
            </div>
          </section>

          <!-- Reply draft: for whoever handles the routed document -->
          <ReplyPanel v-if="canAssign" :document-id="doc.id" @changed="load" />

          <!-- Summary -->
          <section class="card card-pad">
            <div class="section-head">
              <h3 class="eyebrow">{{ t('detail.summary') }}</h3>
              <div class="lang-toggle">
                <button type="button" :class="{ active: summaryLang === 'en' }" @click="summaryLang = 'en'">EN</button>
                <button type="button" :class="{ active: summaryLang === 'ar' }" @click="summaryLang = 'ar'">ع</button>
              </div>
            </div>
            <p class="summary" :dir="summaryLang === 'ar' ? 'rtl' : 'ltr'" :lang="summaryLang">{{ summaryText }}</p>
          </section>

          <!-- Key details -->
          <section class="card card-pad">
            <h3 class="eyebrow">{{ t('detail.fields') }}</h3>
            <dl class="fields">
              <div><dt>{{ t('detail.sender') }}</dt><dd dir="auto">{{ analysis.sender ?? '—' }}</dd></div>
              <div><dt>{{ t('detail.recipient') }}</dt><dd dir="auto">{{ analysis.recipient ?? '—' }}</dd></div>
              <div><dt>{{ t('detail.reference') }}</dt><dd class="ltr mono">{{ analysis.referenceNumber ?? '—' }}</dd></div>
              <div><dt>{{ t('detail.date') }}</dt><dd>{{ formatDate(analysis.documentDate, locale) }}</dd></div>
              <div><dt>{{ t('detail.deadline') }}</dt><dd :class="{ strong: analysis.deadline }">{{ formatDate(analysis.deadline, locale) }}</dd></div>
              <div><dt>{{ t('detail.language') }}</dt><dd>{{ languageName }}</dd></div>
            </dl>
          </section>

          <!-- Action items -->
          <section class="card card-pad">
            <h3 class="eyebrow">{{ t('detail.actionItems') }}</h3>
            <ol v-if="doc.actionItems.length" class="todo">
              <li v-for="(item, i) in doc.actionItems" :key="item.id">
                <span class="num">{{ i + 1 }}</span>
                <span class="grow" dir="auto">{{ item.description }}</span>
                <DeadlineChip v-if="item.dueDate" :date="item.dueDate" :done="!!routed" />
              </li>
            </ol>
            <p v-else class="muted small">{{ t('detail.noActionItems') }}</p>
          </section>

          <!-- Internal comments -->
          <section class="card card-pad">
            <h3 class="eyebrow">{{ t('work.comments') }} <span v-if="doc.comments.length" class="faint">· {{ doc.comments.length }}</span></h3>
            <CommentThread :document-id="doc.id" :comments="doc.comments" @added="load" />
          </section>

          <!-- Activity -->
          <section v-if="doc.timeline.length" class="card card-pad">
            <h3 class="eyebrow">{{ t('timeline.title') }}</h3>
            <ActivityTimeline :events="doc.timeline" />
          </section>

          <p class="model faint">
            <span class="engine" :class="engine.kind"><i :class="engine.icon" /> {{ engine.label }}</span>
            <span v-if="analysis.tokensUsed" class="ltr">· {{ analysis.tokensUsed.toLocaleString() }} {{ t('detail.tokens') }}</span>
            <span v-else>· {{ t('engine.free') }}</span>
          </p>
        </template>
      </div>

      <!-- ── Original document ─────────────────────────────────── -->
      <section class="card original">
        <div class="original-head">
          <i :class="fileIcon" class="file-icon" />
          <div class="grow file-meta">
            <strong class="ltr">{{ doc.fileName }}</strong>
            <small class="faint">{{ t('detail.received') }} {{ formatDateTime(doc.uploadedAt, locale) }}</small>
          </div>
          <div v-if="hasPreview && doc.extractedText" class="lang-toggle">
            <button type="button" :class="{ active: view === 'file' }" @click="view = 'file'">{{ t('detail.file') }}</button>
            <button type="button" :class="{ active: view === 'text' }" @click="view = 'text'">{{ t('detail.extractedText') }}</button>
          </div>
          <a :href="api.fileUrl(doc.id)" target="_blank" rel="noopener" class="open-link" :title="t('actions.open')"><i class="pi pi-external-link" /></a>
        </div>
        <div class="original-body">
          <template v-if="view === 'file' && hasPreview">
            <iframe v-if="isPdf" :src="api.fileUrl(doc.id)" class="preview" :title="doc.fileName" />
            <img v-else :src="api.fileUrl(doc.id)" class="preview-img" :alt="doc.fileName" />
          </template>
          <pre v-else-if="doc.extractedText" class="paper" dir="auto">{{ doc.extractedText }}</pre>
          <p v-else class="muted empty-preview">{{ t('detail.noPreview') }}</p>
        </div>
      </section>
    </div>
  </template>
</template>

<style scoped>
.back {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  text-decoration: none;
  color: var(--muted);
  font-size: 0.88rem;
  font-weight: 500;
  margin-bottom: 14px;
}
.back:hover {
  color: var(--brand);
}
.error {
  color: var(--urgent);
}

.head {
  display: flex;
  align-items: flex-start;
  gap: 16px;
  margin-bottom: 22px;
  flex-direction: row;
}
.head:has(.skeleton) {
  flex-direction: column;
  gap: 0;
}
.head-main {
  flex: 1;
  min-width: 0;
}
.head h1 {
  font-size: 1.55rem;
  font-weight: 700;
  letter-spacing: -0.01em;
  margin-bottom: 10px;
}
.pills {
  gap: 8px 10px;
}

.layout {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1.05fr);
  gap: 20px;
  align-items: start;
}
.insights {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

/* Processing / failed */
.processing {
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.ai-head {
  display: flex;
  gap: 12px;
  margin-bottom: 6px;
}
.ai-head p {
  margin: 2px 0 0;
  font-size: 0.88rem;
}
.ai-badge {
  width: 36px;
  height: 36px;
  flex: none;
  border-radius: 10px;
  display: grid;
  place-items: center;
  background: var(--ai-soft);
  color: var(--ai);
  animation: breathe 1.8s ease-in-out infinite;
}
@keyframes breathe {
  50% { transform: scale(1.08); }
}
.failed {
  display: flex;
  align-items: center;
  gap: 14px;
  border-color: var(--urgent);
  background: var(--urgent-soft);
}
.failed > .pi {
  color: var(--urgent);
  font-size: 1.3rem;
}
.failed p {
  margin: 2px 0 0;
  font-size: 0.88rem;
}

/* Decision card */
.decision {
  overflow: hidden;
  border-color: var(--brand);
  box-shadow: 0 0 0 3px var(--brand-soft);
}
.decision.unsure {
  border-color: var(--high);
  box-shadow: 0 0 0 3px var(--high-soft);
}
.decision.done {
  border-color: var(--border);
  box-shadow: var(--shadow-sm);
}
.decision-body {
  display: flex;
  gap: 16px;
  align-items: flex-start;
  padding: 20px 20px 4px;
}
.decision.done .decision-body {
  padding-bottom: 18px;
}
.decision h2 {
  font-size: 1.3rem;
  font-weight: 700;
  margin: 2px 0 6px;
}
.decision h2.small-h {
  font-size: 1rem;
  margin-top: 8px;
}
.decision .eyebrow {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  margin: 0;
}
.eyebrow .ai {
  color: var(--ai);
}
.decision-icon {
  width: 48px;
  height: 48px;
  flex: none;
  border-radius: 50%;
  display: grid;
  place-items: center;
  font-size: 1.2rem;
}
.decision-icon.ok {
  background: var(--ok-soft);
  color: var(--ok);
}
.decision-icon.warn {
  background: var(--high-soft);
  color: var(--high);
}
.reasoning {
  margin: 0;
  color: var(--muted);
  font-size: 0.9rem;
  line-height: 1.6;
}
.note {
  margin: 8px 0 0;
  padding: 8px 12px;
  border-inline-start: 3px solid var(--border-strong);
  background: var(--surface-2);
  border-radius: 4px;
  font-size: 0.9rem;
}
.handling {
  border-top: 1px solid var(--border);
  padding: 14px 20px 18px;
  display: flex;
  flex-direction: column;
  gap: 10px;
  background: var(--surface-2);
}
.handling-row {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
}
.assignee-row {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
}
.assignee-select {
  min-width: 220px;
}
.dept-link {
  color: var(--brand);
  text-decoration: none;
  font-weight: 500;
}
.handling .note {
  margin: 0;
}
.email-status {
  margin: 0;
  display: flex;
  gap: 6px;
  align-items: baseline;
  color: var(--muted);
}
.email-status.Sent .pi {
  color: var(--ok);
}
.email-status.Failed {
  color: var(--urgent);
}
.unsure-note {
  margin: 12px 20px 0;
  padding: 10px 12px;
  border-radius: var(--radius-sm);
  background: var(--high-soft);
  color: var(--high);
  font-size: 0.86rem;
  font-weight: 500;
  display: flex;
  gap: 8px;
  align-items: center;
}
.decision-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  padding: 16px 20px 20px;
}
.picker {
  display: flex;
  flex-direction: column;
  gap: 10px;
  width: 100%;
}
.opt {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
}
.opt-ai {
  margin-inline-start: auto;
  font-size: 0.72rem;
  font-weight: 600;
  color: var(--ai);
  background: var(--ai-soft);
  padding: 1px 8px;
  border-radius: 10px;
}

/* Sections */
.section-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 10px;
}
.section-head .eyebrow {
  margin: 0;
}
.summary {
  margin: 0;
  font-size: 1rem;
  line-height: 1.75;
}
.lang-toggle {
  display: inline-flex;
  padding: 2px;
  border-radius: 7px;
  background: var(--surface-2);
  border: 1px solid var(--border);
}
.lang-toggle button {
  border: 0;
  background: none;
  font: inherit;
  font-size: 0.78rem;
  font-weight: 600;
  color: var(--muted);
  padding: 3px 10px;
  border-radius: 5px;
  cursor: pointer;
}
.lang-toggle button.active {
  background: var(--surface);
  color: var(--text);
  box-shadow: var(--shadow);
}

.fields {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 14px 20px;
  margin: 0;
}
.fields dt {
  font-size: 0.78rem;
  color: var(--faint);
  margin-bottom: 2px;
}
.fields dd {
  margin: 0;
  font-weight: 500;
  overflow-wrap: anywhere;
}
.fields .strong {
  color: var(--urgent);
  font-weight: 600;
}
.mono {
  font-family: ui-monospace, 'Cascadia Code', Consolas, monospace;
  font-size: 0.88rem;
}

.todo {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.todo li {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  padding: 8px 0;
  border-bottom: 1px dashed var(--border);
  font-size: 0.93rem;
}
.todo li:last-child {
  border-bottom: 0;
}
.num {
  width: 22px;
  height: 22px;
  flex: none;
  border-radius: 50%;
  display: grid;
  place-items: center;
  font-size: 0.72rem;
  font-weight: 700;
  background: var(--brand-soft);
  color: var(--brand);
  margin-top: 1px;
}
.model {
  font-size: 0.78rem;
  margin: 0;
  display: flex;
  align-items: center;
  gap: 6px;
}
.engine {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 2px 10px;
  border-radius: 999px;
  background: var(--surface);
  border: 1px solid var(--border);
  color: var(--muted);
  font-weight: 500;
}
.engine.cloud {
  color: var(--ai);
}
.engine.local {
  color: var(--brand);
}
.small {
  font-size: 0.86rem;
  margin: 0;
}

/* Original */
.original {
  position: sticky;
  top: 20px;
  overflow: hidden;
  display: flex;
  flex-direction: column;
  max-height: calc(100vh - 40px);
}
.original-head {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px 16px;
  border-bottom: 1px solid var(--border);
  background: var(--surface-2);
}
.file-icon {
  font-size: 1.3rem;
  color: var(--brand);
}
.file-meta {
  min-width: 0;
  display: flex;
  flex-direction: column;
}
.file-meta strong {
  font-size: 0.9rem;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.file-meta small {
  font-size: 0.75rem;
}
.open-link {
  color: var(--muted);
  padding: 6px;
}
.open-link:hover {
  color: var(--brand);
}
.original-body {
  overflow: auto;
  background: var(--bg);
  padding: 20px;
  min-height: 420px;
}
.preview {
  width: 100%;
  height: calc(100vh - 170px);
  min-height: 520px;
  border: 0;
  border-radius: 6px;
  background: #fff;
}
.preview-img {
  display: block;
  max-width: 100%;
  margin: 0 auto;
  border-radius: 4px;
  box-shadow: var(--shadow);
}
/* Text documents render as a sheet of paper */
.paper {
  margin: 0 auto;
  max-width: 680px;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: 4px;
  box-shadow: var(--shadow);
  padding: 40px 44px;
  white-space: pre-wrap;
  font-family: inherit;
  font-size: 0.93rem;
  line-height: 1.8;
  color: var(--text);
}
.empty-preview {
  text-align: center;
  padding: 60px 0;
}

@media (max-width: 1080px) {
  .layout {
    grid-template-columns: 1fr;
  }
  .original {
    position: static;
    max-height: none;
  }
}
@media (max-width: 560px) {
  .head {
    flex-direction: column;
  }
  .fields {
    grid-template-columns: 1fr;
  }
  .paper {
    padding: 24px 20px;
  }
  .original-body {
    padding: 12px;
  }
}
</style>
