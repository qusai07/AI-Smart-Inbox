<script setup lang="ts">
import { computed, onUnmounted, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Select from 'primevue/select'
import Textarea from 'primevue/textarea'
import Message from 'primevue/message'
import { api, NUMBER_MARKERS, type ReplyDraft, type ReplyTemplate } from '../api'
import { formatDateTime } from './Badges'
import { onDraftChanged } from '../realtime'

const props = defineProps<{ documentId: string }>()
const emit = defineEmits<{ changed: [] }>()

const { t, locale } = useI18n()
const toast = useToast()

const PLACEHOLDER = '[●]'
const countGaps = (...texts: (string | null | undefined)[]) => texts.reduce((n, s) => n + (s ? s.split(PLACEHOLDER).length - 1 : 0), 0)

const drafts = ref<ReplyDraft[]>([])
const templates = ref<ReplyTemplate[]>([])
const viewingId = ref<string | null>(null) // null = latest
const busy = ref(false)
const loaded = ref(false)

const latest = computed(() => drafts.value[0] ?? null)
const current = computed(() => drafts.value.find((d) => d.id === viewingId.value) ?? latest.value)
const isOld = computed(() => !!current.value && !current.value.isLatest)
const preparing = computed(() => latest.value?.status === 'Requested' || latest.value?.status === 'Generating')
const editable = computed(() => current.value?.status === 'Ready' && current.value.isLatest)

const templateName = (key: string | null) => {
  const tpl = templates.value.find((x) => x.key === key)
  return tpl ? (locale.value === 'ar' ? tpl.nameAr : tpl.nameEn) : key ?? ''
}
const templateOptions = computed(() => [
  { label: t('reply.automatic'), value: null as string | null },
  ...templates.value.map((x) => ({ label: locale.value === 'ar' ? x.nameAr : x.nameEn, value: x.key as string | null })),
])
const versionOptions = computed(() =>
  drafts.value.map((d) => ({ label: `${t('reply.version', { v: d.version })}${d.isLatest ? ` · ${t('reply.latest')}` : ''}`, value: d.id })),
)
const byline = computed(() => {
  const d = current.value
  if (!d) return ''
  if (d.engine === 'Edited') return t('reply.by.Edited', { who: d.editedBy ?? '' })
  return `${t('reply.by.Templates')} · ${templateName(d.templateKey)}`
})

// ── Loading & live updates ───────────────────────────────────────
async function load() {
  try {
    drafts.value = await api.drafts(props.documentId)
    if (viewingId.value && !drafts.value.some((d) => d.id === viewingId.value)) viewingId.value = null
  } catch (e) {
    toast.add({ severity: 'error', summary: t('errors.generic'), detail: (e as Error).message, life: 5000 })
  } finally {
    loaded.value = true
  }
}

async function loadTemplates() {
  if (templates.value.length) return
  try {
    templates.value = await api.replyTemplates()
  } catch {
    /* the picker just shows "automatic" */
  }
}

const unsubscribe = onDraftChanged((e) => {
  if (e.documentId !== props.documentId) return
  load()
  emit('changed')
})
// Live updates normally arrive over SignalR; poll gently while a draft is being prepared in case they don't.
let poll: ReturnType<typeof setTimeout> | undefined
watch(preparing, (p) => {
  clearTimeout(poll)
  if (p) poll = setTimeout(async function tick() {
    await load()
    if (preparing.value) poll = setTimeout(tick, 2500)
    else emit('changed')
  }, 2500)
})
onUnmounted(() => {
  unsubscribe()
  clearTimeout(poll)
})
watch(() => props.documentId, () => {
  viewingId.value = null
  load()
  loadTemplates()
}, { immediate: true })

// ── New draft ─────────────────────────────────────────────────────
const composing = ref(false)
const composer = reactive({ templateKey: null as string | null, instructions: '' })

function openComposer() {
  composer.templateKey = null
  composer.instructions = ''
  composing.value = true
}

async function requestDraft(templateKey: string | null = composer.templateKey, instructions: string | null = composer.instructions.trim() || null) {
  busy.value = true
  try {
    await api.requestDraft(props.documentId, templateKey, instructions)
    composing.value = false
    viewingId.value = null
    await load()
    emit('changed')
  } catch (e) {
    toast.add({ severity: 'error', summary: t('errors.generic'), detail: (e as Error).message, life: 5000 })
  } finally {
    busy.value = false
  }
}

// ── Editing ───────────────────────────────────────────────────────
const form = reactive({ subjectEn: '', bodyEn: '', subjectAr: '', bodyAr: '' })
watch(current, (d) => {
  Object.assign(form, { subjectEn: d?.subjectEn ?? '', bodyEn: d?.bodyEn ?? '', subjectAr: d?.subjectAr ?? '', bodyAr: d?.bodyAr ?? '' })
}, { immediate: true })

const dirty = computed(() => {
  const d = current.value
  return !!d && (form.subjectEn !== (d.subjectEn ?? '') || form.bodyEn !== (d.bodyEn ?? '') || form.subjectAr !== (d.subjectAr ?? '') || form.bodyAr !== (d.bodyAr ?? ''))
})
const gaps = computed(() => countGaps(form.subjectEn, form.bodyEn, form.subjectAr, form.bodyAr))
const awaitsNumber = computed(() => NUMBER_MARKERS.some((m) => [form.subjectEn, form.bodyEn, form.subjectAr, form.bodyAr].some((x) => x.includes(m))))
const canApprove = computed(() => editable.value && !dirty.value && gaps.value === 0 && !!form.bodyEn.trim() && !!form.bodyAr.trim())

async function save() {
  if (!current.value) return
  busy.value = true
  try {
    const saved = await api.updateDraft(current.value.id, { ...form })
    toast.add({ severity: 'success', summary: t('reply.saved', { v: saved.version }), life: 2500 })
    viewingId.value = null
    await load()
    emit('changed')
  } catch (e) {
    toast.add({ severity: 'error', summary: t('errors.generic'), detail: (e as Error).message, life: 5000 })
  } finally {
    busy.value = false
  }
}

async function approve() {
  if (!current.value) return
  busy.value = true
  try {
    const approved = await api.approveDraft(current.value.id)
    toast.add({
      severity: 'success',
      summary: approved.outgoingNumber ? t('reply.approvedNumber', { number: approved.outgoingNumber }) : t('reply.approved'),
      life: approved.outgoingNumber ? 6000 : 2500,
    })
    await load()
    emit('changed')
  } catch (e) {
    toast.add({ severity: 'error', summary: t('errors.generic'), detail: (e as Error).message, life: 5000 })
  } finally {
    busy.value = false
  }
}

const rejecting = ref(false)
const rejectReason = ref('')

async function reject() {
  if (!current.value) return
  busy.value = true
  try {
    await api.rejectDraft(current.value.id, rejectReason.value.trim() || null)
    rejecting.value = false
    rejectReason.value = ''
    await load()
    emit('changed')
  } catch (e) {
    toast.add({ severity: 'error', summary: t('errors.generic'), detail: (e as Error).message, life: 5000 })
  } finally {
    busy.value = false
  }
}

async function copy(lang: 'en' | 'ar') {
  const text = lang === 'en' ? `${form.subjectEn}\n\n${form.bodyEn}` : `${form.subjectAr}\n\n${form.bodyAr}`
  await navigator.clipboard.writeText(text)
  toast.add({ severity: 'success', summary: t('reply.copied'), life: 1500 })
}
</script>

<template>
  <section class="card card-pad reply">
    <div class="head">
      <h3 class="eyebrow">{{ t('reply.title') }}</h3>
      <div class="head-tools">
        <Select
          v-if="drafts.length > 1"
          :model-value="current?.id"
          :options="versionOptions"
          option-label="label"
          option-value="value"
          size="small"
          @update:model-value="(id: string) => (viewingId = id === latest?.id ? null : id)"
        />
        <Button
          v-if="drafts.length && !preparing && !composing"
          :label="t('reply.newDraft')"
          icon="pi pi-plus"
          size="small"
          text
          @click="openComposer"
        />
      </div>
    </div>

    <!-- Nothing yet, or starting a new draft -->
    <div v-if="loaded && (!drafts.length || composing)" class="composer">
      <p v-if="!drafts.length" class="muted small intro">{{ t('reply.intro') }}</p>
      <label>
        <span>{{ t('reply.type') }}</span>
        <Select v-model="composer.templateKey" :options="templateOptions" option-label="label" option-value="value" fluid />
      </label>
      <label>
        <span>{{ t('reply.instructions') }} <small class="faint">· {{ t('reply.instructionsHint') }}</small></span>
        <Textarea v-model="composer.instructions" rows="2" auto-resize fluid dir="auto" maxlength="1000" />
      </label>
      <div class="row">
        <Button :label="t('reply.draft')" icon="pi pi-pencil" :loading="busy" @click="requestDraft()" />
        <Button v-if="drafts.length" :label="t('actions.cancel')" severity="secondary" text @click="composing = false" />
      </div>
    </div>

    <template v-else-if="current">
      <!-- Being prepared -->
      <div v-if="preparing && !isOld" class="state">
        <i class="pi pi-spin pi-spinner" /> {{ t('reply.preparing') }}
      </div>

      <!-- Failed -->
      <div v-else-if="current.status === 'Failed'" class="state">
        <Message severity="error" :closable="false">{{ t('reply.failed') }}: {{ current.error }}</Message>
        <Button :label="t('reply.retry')" icon="pi pi-refresh" size="small" :loading="busy" @click="requestDraft(current.templateKey, current.instructions)" />
      </div>

      <!-- A draft to read or work on -->
      <template v-else>
        <div v-if="isOld" class="old">
          <i class="pi pi-history" /> {{ t('reply.viewing') }}
          <Button :label="t('reply.backToLatest')" size="small" text @click="viewingId = null" />
        </div>

        <div class="meta small muted">
          <span class="pill neutral">{{ byline }}</span>
          <span v-if="current.status === 'Approved'" class="pill d-ok"><i class="pi pi-check-circle" /> {{ t('reply.approvedBy', { who: current.decidedBy, when: formatDateTime(current.decidedAt!, locale) }) }}</span>
          <RouterLink v-if="current.outgoingNumber" :to="{ name: 'register', query: { q: current.outgoingNumber } }" class="pill d-ok number-link ltr">
            <i class="pi pi-hashtag" /> {{ current.outgoingNumber }}
          </RouterLink>
          <span v-else-if="current.status === 'Rejected'" class="pill d-bad"><i class="pi pi-times-circle" /> {{ t('reply.rejected') }}</span>
          <span v-if="current.requestedBy">{{ t('reply.requestedBy', { who: current.requestedBy }) }}</span>
        </div>
        <p v-if="current.instructions" class="notes small"><strong>{{ t('reply.notes') }}:</strong> <span dir="auto">{{ current.instructions }}</span></p>

        <div v-if="editable" class="gaps" :class="{ none: gaps === 0 }">
          <i :class="gaps ? 'pi pi-exclamation-circle' : 'pi pi-check'" />
          {{ gaps ? t('reply.gaps', { n: gaps }) : t('reply.noGaps') }}
          <span v-if="awaitsNumber" class="muted"> · <i class="pi pi-hashtag" /> {{ t('reply.numberOnApproval') }}</span>
        </div>

        <div class="letters">
          <div class="letter">
            <div class="letter-head"><strong>{{ t('reply.english') }}</strong>
              <Button :label="t('reply.copyEn')" icon="pi pi-copy" size="small" text @click="copy('en')" />
            </div>
            <label class="field"><span>{{ t('reply.subject') }}</span>
              <InputText v-model="form.subjectEn" :readonly="!editable" fluid dir="ltr" />
            </label>
            <Textarea v-model="form.bodyEn" :readonly="!editable" auto-resize rows="14" fluid dir="ltr" class="body" />
          </div>
          <div class="letter" dir="rtl" lang="ar">
            <div class="letter-head"><strong>{{ t('reply.arabic') }}</strong>
              <Button :label="t('reply.copyAr')" icon="pi pi-copy" size="small" text @click="copy('ar')" />
            </div>
            <label class="field"><span>الموضوع</span>
              <InputText v-model="form.subjectAr" :readonly="!editable" fluid dir="rtl" />
            </label>
            <Textarea v-model="form.bodyAr" :readonly="!editable" auto-resize rows="14" fluid dir="rtl" class="body" />
          </div>
        </div>

        <div v-if="current.missingInfo.length || current.commitments.length" class="lists">
          <div v-if="current.missingInfo.length && editable">
            <h4>{{ t('reply.missing') }}</h4>
            <ul><li v-for="m in current.missingInfo" :key="m">{{ m }}</li></ul>
          </div>
          <div v-if="current.commitments.length">
            <h4>{{ t('reply.commitments') }}</h4>
            <ul><li v-for="c in current.commitments" :key="c">{{ c }}</li></ul>
          </div>
        </div>

        <div v-if="current.status === 'Approved' || (editable && !dirty)" class="download">
          <a :href="api.draftDocxUrl(current.id)" class="p-button p-button-sm p-button-outlined" download>
            <i class="pi pi-file-word" /> {{ t('reply.word') }}
          </a>
        </div>

        <div v-if="editable" class="actions">
          <Button :label="t('reply.save')" icon="pi pi-save" severity="secondary" :disabled="!dirty" :loading="busy && dirty" @click="save" />
          <Button
            :label="t('reply.approve')"
            icon="pi pi-check"
            :disabled="!canApprove"
            :loading="busy && !dirty"
            :title="dirty ? t('reply.unsaved') : gaps ? t('reply.gaps', { n: gaps }) : ''"
            @click="approve"
          />
          <Button v-if="!rejecting" :label="t('reply.reject')" icon="pi pi-times" severity="danger" text @click="rejecting = true" />
          <span v-if="dirty" class="small muted">{{ t('reply.unsaved') }}</span>
        </div>
        <div v-if="editable && rejecting" class="reject">
          <Textarea v-model="rejectReason" rows="2" auto-resize fluid dir="auto" :placeholder="t('reply.rejectReason')" maxlength="1000" />
          <div class="row">
            <Button :label="t('reply.confirmReject')" severity="danger" size="small" :loading="busy" @click="reject" />
            <Button :label="t('actions.cancel')" severity="secondary" size="small" text @click="rejecting = false" />
          </div>
        </div>
      </template>
    </template>
  </section>
</template>

<style scoped>
.head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 10px;
}
.head-tools {
  display: flex;
  align-items: center;
  gap: 6px;
}
.intro {
  margin: 0 0 4px;
}
.composer,
.state,
.reject {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.state {
  flex-direction: row;
  align-items: center;
  flex-wrap: wrap;
  color: var(--muted);
}
.composer label,
.field {
  display: flex;
  flex-direction: column;
  gap: 6px;
  font-size: 0.85rem;
  font-weight: 500;
}
.row,
.actions {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
}
.actions {
  margin-top: 14px;
}
.old {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px 10px;
  margin-bottom: 10px;
  border-radius: 8px;
  background: var(--surface-2);
  font-size: 0.85rem;
}
.meta {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  margin-bottom: 8px;
}
.pill.d-ok {
  background: var(--ok-soft);
  color: var(--ok);
}
.pill.d-bad {
  background: var(--urgent-soft);
  color: var(--urgent);
}
.notes {
  margin: 0 0 8px;
}
.gaps {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  margin-bottom: 10px;
  padding: 4px 10px;
  border-radius: 999px;
  font-size: 0.8rem;
  font-weight: 600;
  background: var(--high-soft);
  color: var(--high);
}
.number-link {
  text-decoration: none;
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
}
.gaps.none {
  background: var(--ok-soft);
  color: var(--ok);
}
.letters {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
}
@media (max-width: 1100px) {
  .letters {
    grid-template-columns: 1fr;
  }
}
.letter {
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-width: 0;
}
.letter-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.body {
  font-size: 0.9rem;
  line-height: 1.6;
}
.lists {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 12px;
  margin-top: 12px;
}
.lists h4 {
  margin: 0 0 4px;
  font-size: 0.8rem;
  color: var(--muted);
}
.lists ul {
  margin: 0;
  padding-inline-start: 18px;
  font-size: 0.85rem;
}
.download {
  margin-top: 12px;
}
.download a {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  text-decoration: none;
}
.small {
  font-size: 0.8rem;
}
</style>
