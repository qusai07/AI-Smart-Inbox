<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import MultiSelect from 'primevue/multiselect'
import Select from 'primevue/select'
import ToggleSwitch from 'primevue/toggleswitch'
import Message from 'primevue/message'
import { api, type WebhookDelivery, type WebhookEndpoint } from '../api'
import { formatDateTime } from './Badges'
import { useDepartmentName, useDepartments } from '../stores/departments'

const { t, locale } = useI18n()
const toast = useToast()
const departments = useDepartments()
const departmentName = useDepartmentName()

const endpoints = ref<WebhookEndpoint[]>([])
const catalog = ref<string[]>([])
const busy = ref(false)
const error = ref<string | null>(null)

const departmentOptions = computed(() => [
  { label: t('webhooks.anyDepartment'), value: null },
  ...departments.items.map((d) => ({ label: departmentName(d.id), value: d.id })),
])

const eventsLabel = (e: WebhookEndpoint) => (e.events.includes('*') ? t('webhooks.allEvents') : t('webhooks.someEvents', { n: e.events.length }))
const percent = (rate: number | null) => (rate === null ? '—' : `${Math.round(rate * 100)}%`)
const fail = (e: unknown) => toast.add({ severity: 'error', summary: t('errors.generic'), detail: (e as Error).message, life: 5000 })

async function load() {
  try {
    endpoints.value = await api.webhooks()
  } catch (e) {
    error.value = (e as Error).message
  }
}

// ── Create / edit ─────────────────────────────────────────────────
const editing = ref<WebhookEndpoint | null>(null)
const dialog = ref(false)
const form = reactive({ url: '', description: '', allEvents: true, events: [] as string[], departmentId: null as number | null, enabled: true })
const formError = ref<string | null>(null)

function openCreate() {
  editing.value = null
  Object.assign(form, { url: '', description: '', allEvents: true, events: [], departmentId: null, enabled: true })
  formError.value = null
  dialog.value = true
}

function openEdit(e: WebhookEndpoint) {
  editing.value = e
  const all = e.events.includes('*')
  Object.assign(form, { url: e.url, description: e.description ?? '', allEvents: all, events: all ? [] : [...e.events], departmentId: e.departmentId, enabled: e.enabled })
  formError.value = null
  dialog.value = true
}

async function save() {
  busy.value = true
  formError.value = null
  try {
    const body = {
      url: form.url.trim(),
      description: form.description.trim() || null,
      events: form.allEvents || form.events.length === 0 ? null : form.events,
      departmentId: form.departmentId,
      enabled: form.enabled,
    }
    if (editing.value) {
      await api.updateWebhook(editing.value.id, body)
    } else {
      const created = await api.createWebhook(body)
      secret.value = { value: created.secret, rotated: false }
    }
    dialog.value = false
    await load()
  } catch (e) {
    formError.value = (e as Error).message
  } finally {
    busy.value = false
  }
}

// ── Secret (shown once) ───────────────────────────────────────────
const secret = ref<{ value: string; rotated: boolean } | null>(null)
const copied = ref(false)

async function copySecret() {
  if (!secret.value) return
  await navigator.clipboard.writeText(secret.value.value)
  copied.value = true
}

function closeSecret() {
  secret.value = null
  copied.value = false
}

// ── Confirmations: rotate / delete ────────────────────────────────
const confirming = ref<{ kind: 'rotate' | 'delete'; endpoint: WebhookEndpoint } | null>(null)

async function confirmAction() {
  if (!confirming.value) return
  const { kind, endpoint } = confirming.value
  busy.value = true
  try {
    if (kind === 'rotate') {
      const rotated = await api.rotateWebhookSecret(endpoint.id)
      secret.value = { value: rotated.secret, rotated: true }
    } else {
      await api.deleteWebhook(endpoint.id)
    }
    confirming.value = null
    await load()
  } catch (e) {
    fail(e)
  } finally {
    busy.value = false
  }
}

// ── Test ping ─────────────────────────────────────────────────────
const pinging = ref<number | null>(null)

async function ping(e: WebhookEndpoint) {
  pinging.value = e.id
  try {
    const d = await api.pingWebhook(e.id)
    if (d.status === 'Delivered') toast.add({ severity: 'success', summary: t('webhooks.testOk', { code: d.lastStatusCode }), life: 3000 })
    else toast.add({ severity: 'error', summary: t('webhooks.testFailed'), detail: d.lastError ?? '', life: 6000 })
    await load()
    if (log.value?.endpoint.id === e.id) await openLog(e)
  } catch (err) {
    fail(err)
  } finally {
    pinging.value = null
  }
}

// ── Delivery log ──────────────────────────────────────────────────
const log = ref<{ endpoint: WebhookEndpoint; items: WebhookDelivery[]; total: number; page: number } | null>(null)
const expanded = ref<number | null>(null)
const redelivering = ref<number | null>(null)

async function openLog(e: WebhookEndpoint) {
  try {
    const page = await api.webhookDeliveries(e.id, 1)
    log.value = { endpoint: e, items: page.items, total: page.total, page: 1 }
    expanded.value = null
  } catch (err) {
    fail(err)
  }
}

async function more() {
  if (!log.value) return
  const next = await api.webhookDeliveries(log.value.endpoint.id, log.value.page + 1)
  log.value.items.push(...next.items)
  log.value.page++
}

async function redeliver(d: WebhookDelivery) {
  if (!log.value) return
  redelivering.value = d.id
  try {
    const again = await api.redeliver(d.id)
    toast.add({
      severity: again.status === 'Delivered' ? 'success' : 'error',
      summary: again.status === 'Delivered' ? t('webhooks.redelivered') : t('webhooks.testFailed'),
      detail: again.lastError ?? undefined,
      life: 4000,
    })
    await openLog(log.value.endpoint)
    await load()
  } catch (err) {
    fail(err)
  } finally {
    redelivering.value = null
  }
}

const pretty = (json: string) => {
  try {
    return JSON.stringify(JSON.parse(json), null, 2)
  } catch {
    return json
  }
}

onMounted(async () => {
  await Promise.all([load(), api.webhookEvents().then((e) => (catalog.value = e)).catch(() => {}), departments.load()])
})
</script>

<template>
  <section id="webhooks" class="card block">
    <div class="block-head">
      <div>
        <h2>{{ t('webhooks.title') }}</h2>
        <p class="small muted hint">{{ t('webhooks.hint') }}</p>
      </div>
      <Button :label="t('webhooks.add')" icon="pi pi-plus" size="small" @click="openCreate" />
    </div>
    <Message v-if="error" severity="error">{{ error }}</Message>
    <p v-if="!endpoints.length" class="muted empty">{{ t('webhooks.none') }}</p>
    <div v-else class="table-wrap">
      <table class="table">
        <thead>
          <tr>
            <th>{{ t('webhooks.url') }}</th>
            <th>{{ t('webhooks.events') }}</th>
            <th>{{ t('webhooks.status') }}</th>
            <th>{{ t('webhooks.lastDelivery') }}</th>
            <th>{{ t('webhooks.success') }}</th>
            <th />
          </tr>
        </thead>
        <tbody>
          <tr v-for="e in endpoints" :key="e.id" :class="{ disabled: !e.enabled }">
            <td class="url-cell">
              <strong class="ltr url">{{ e.url }}</strong>
              <div v-if="e.description" class="small muted" dir="auto">{{ e.description }}</div>
              <div v-if="e.rotationInProgress" class="small faint"><i class="pi pi-sync" /> {{ t('webhooks.rotating') }}</div>
            </td>
            <td class="small">
              {{ eventsLabel(e) }}
              <div class="muted">{{ e.departmentId ? departmentName(e.departmentId) : t('webhooks.anyDepartment') }}</div>
            </td>
            <td class="small">
              <span class="pill" :class="e.enabled ? 'd-Delivered' : 'd-Failed'">{{ e.enabled ? t('webhooks.enabled') : t('webhooks.disabled') }}</span>
              <div v-if="e.disabledReason" class="small reason">{{ e.disabledReason }}</div>
            </td>
            <td class="small muted">
              <template v-if="e.lastDelivery">
                <span class="pill" :class="`d-${e.lastDelivery.status}`">{{ e.lastDelivery.statusCode ?? t(`webhooks.deliveryStatus.${e.lastDelivery.status}`) }}</span>
                <div>{{ formatDateTime(e.lastDelivery.at, locale) }}</div>
              </template>
              <template v-else>{{ t('webhooks.noDeliveries') }}</template>
            </td>
            <td class="small tabular">{{ percent(e.successRate7d) }} <span class="faint">· {{ e.deliveries7d }}</span></td>
            <td class="end actions">
              <Button :label="t('webhooks.test')" icon="pi pi-send" size="small" text :loading="pinging === e.id" @click="ping(e)" />
              <Button :label="t('webhooks.log')" icon="pi pi-list" size="small" text @click="openLog(e)" />
              <Button :title="t('webhooks.edit')" icon="pi pi-pencil" size="small" text :aria-label="t('webhooks.edit')" @click="openEdit(e)" />
              <Button :title="t('webhooks.rotate')" icon="pi pi-key" size="small" text :aria-label="t('webhooks.rotate')" @click="confirming = { kind: 'rotate', endpoint: e }" />
              <Button :title="t('webhooks.delete')" icon="pi pi-trash" size="small" text severity="danger" :aria-label="t('webhooks.delete')" @click="confirming = { kind: 'delete', endpoint: e }" />
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </section>

  <Dialog v-model:visible="dialog" modal :header="editing ? t('webhooks.edit') : t('webhooks.add')" :style="{ width: 'min(520px, 94vw)' }" :draggable="false">
    <form class="form" @submit.prevent="save">
      <label>
        <span>{{ t('webhooks.url') }}</span>
        <InputText v-model="form.url" type="url" required fluid dir="ltr" placeholder="https://example.com/hooks/smart-inbox" />
      </label>
      <label>
        <span>{{ t('webhooks.description') }}</span>
        <InputText v-model="form.description" fluid dir="auto" maxlength="200" />
      </label>
      <label class="switch">
        <ToggleSwitch v-model="form.allEvents" />
        <span>{{ t('webhooks.allEvents') }}</span>
      </label>
      <label v-if="!form.allEvents">
        <span>{{ t('webhooks.events') }}</span>
        <MultiSelect v-model="form.events" :options="catalog" display="chip" fluid class="ltr-input" />
      </label>
      <label>
        <span>{{ t('webhooks.department') }}</span>
        <Select v-model="form.departmentId" :options="departmentOptions" option-label="label" option-value="value" fluid />
      </label>
      <label v-if="editing" class="switch">
        <ToggleSwitch v-model="form.enabled" />
        <span>{{ t('webhooks.enabled') }}</span>
      </label>
      <Message v-if="formError" severity="error">{{ formError }}</Message>
    </form>
    <template #footer>
      <Button :label="t('actions.cancel')" severity="secondary" text @click="dialog = false" />
      <Button :label="editing ? t('webhooks.save') : t('webhooks.create')" icon="pi pi-check" :loading="busy" :disabled="!form.url.trim()" @click="save" />
    </template>
  </Dialog>

  <Dialog :visible="!!secret" modal :closable="false" :header="t('webhooks.secretTitle')" :style="{ width: 'min(560px, 94vw)' }" :draggable="false">
    <Message severity="warn" :closable="false">{{ t('webhooks.secretHint') }}</Message>
    <p v-if="secret?.rotated" class="small muted">{{ t('webhooks.rotatedHint') }}</p>
    <div class="secret">
      <code class="ltr">{{ secret?.value }}</code>
      <Button :label="copied ? t('admin.copied') : t('admin.copy')" :icon="copied ? 'pi pi-check' : 'pi pi-copy'" size="small" outlined @click="copySecret" />
    </div>
    <template #footer>
      <Button :label="t('admin.done')" @click="closeSecret" />
    </template>
  </Dialog>

  <Dialog
    :visible="!!confirming"
    modal
    :header="confirming?.kind === 'rotate' ? t('webhooks.rotateTitle') : t('webhooks.deleteTitle')"
    :style="{ width: 'min(460px, 94vw)' }"
    :draggable="false"
    @update:visible="(v: boolean) => { if (!v) confirming = null }"
  >
    <p v-if="confirming?.kind === 'rotate'">{{ t('webhooks.rotateText') }}</p>
    <p v-else>{{ t('webhooks.deleteText', { url: confirming?.endpoint.url }) }}</p>
    <template #footer>
      <Button :label="t('actions.cancel')" severity="secondary" text @click="confirming = null" />
      <Button
        :label="confirming?.kind === 'rotate' ? t('webhooks.rotate') : t('webhooks.delete')"
        :severity="confirming?.kind === 'delete' ? 'danger' : undefined"
        :loading="busy"
        @click="confirmAction"
      />
    </template>
  </Dialog>

  <Dialog
    :visible="!!log"
    modal
    :header="`${t('webhooks.log')} · ${log?.endpoint.url ?? ''}`"
    :style="{ width: 'min(920px, 96vw)' }"
    :draggable="false"
    @update:visible="(v: boolean) => { if (!v) log = null }"
  >
    <p v-if="!log?.items.length" class="muted">{{ t('webhooks.noDeliveries') }}</p>
    <div v-else class="table-wrap">
      <table class="table compact">
        <thead>
          <tr>
            <th>{{ t('webhooks.event') }}</th>
            <th>{{ t('webhooks.status') }}</th>
            <th>{{ t('webhooks.code') }}</th>
            <th>{{ t('webhooks.duration') }}</th>
            <th>{{ t('webhooks.attempts') }}</th>
            <th>{{ t('webhooks.at') }}</th>
            <th />
          </tr>
        </thead>
        <tbody>
          <template v-for="d in log!.items" :key="d.id">
            <tr class="clickable" @click="expanded = expanded === d.id ? null : d.id">
              <td class="ltr"><code>{{ d.eventType }}</code></td>
              <td><span class="pill" :class="`d-${d.status}`">{{ t(`webhooks.deliveryStatus.${d.status}`) }}</span></td>
              <td class="tabular">{{ d.lastStatusCode ?? '—' }}</td>
              <td class="tabular">{{ d.durationMs !== null ? `${d.durationMs} ms` : '—' }}</td>
              <td class="tabular">{{ d.attempts }}</td>
              <td class="muted">{{ formatDateTime(d.createdAt, locale) }}</td>
              <td class="end">
                <Button :label="t('webhooks.redeliver')" icon="pi pi-replay" size="small" text :loading="redelivering === d.id" @click.stop="redeliver(d)" />
              </td>
            </tr>
            <tr v-if="expanded === d.id" class="detail">
              <td colspan="7">
                <div class="small muted ltr">{{ d.eventId }}</div>
                <div v-if="d.lastError" class="small reason">{{ t('webhooks.error') }}: {{ d.lastError }}</div>
                <div v-if="d.nextAttemptAt" class="small muted">{{ t('webhooks.nextAttempt') }}: {{ formatDateTime(d.nextAttemptAt, locale) }}</div>
                <div class="small col-head">{{ t('webhooks.payload') }}</div>
                <pre class="ltr">{{ pretty(d.payload) }}</pre>
                <template v-if="d.lastResponseSnippet">
                  <div class="small col-head">{{ t('webhooks.response') }}</div>
                  <pre class="ltr">{{ d.lastResponseSnippet }}</pre>
                </template>
              </td>
            </tr>
          </template>
        </tbody>
      </table>
    </div>
    <template #footer>
      <Button v-if="log && log.items.length < log.total" :label="t('webhooks.more')" severity="secondary" text @click="more" />
    </template>
  </Dialog>
</template>

<style scoped>
.block {
  margin-bottom: 16px;
  overflow: hidden;
}
.block-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 14px 20px;
  border-bottom: 1px solid var(--border);
}
.block-head h2 {
  font-size: 1rem;
}
.hint {
  margin: 2px 0 0;
}
.empty {
  padding: 16px 20px;
  margin: 0;
}
.table-wrap {
  overflow-x: auto;
}
.table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.92rem;
}
.table th {
  text-align: start;
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--faint);
  padding: 10px 20px;
  background: var(--surface-2);
  border-bottom: 1px solid var(--border);
}
.table td {
  padding: 12px 20px;
  border-bottom: 1px solid var(--border);
  vertical-align: top;
}
.table.compact th,
.table.compact td {
  padding: 8px 12px;
}
.table tr:last-child td {
  border-bottom: 0;
}
.table tr.disabled td {
  opacity: 0.7;
}
.clickable {
  cursor: pointer;
}
.clickable:hover td {
  background: var(--surface-2);
}
.detail td {
  background: var(--surface-2);
}
.url-cell {
  max-width: 320px;
}
.url {
  display: block;
  word-break: break-all;
  font-weight: 500;
}
.reason {
  color: var(--urgent);
  margin-top: 4px;
  max-width: 260px;
}
.actions {
  white-space: nowrap;
}
.end {
  text-align: end;
}
.small {
  font-size: 0.8rem;
}
.col-head {
  font-weight: 600;
  color: var(--muted);
  margin-top: 10px;
}
pre {
  margin: 4px 0 0;
  padding: 10px 12px;
  max-height: 260px;
  overflow: auto;
  font-size: 0.78rem;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: 8px;
  white-space: pre-wrap;
  word-break: break-all;
}
.pill.d-Delivered {
  background: var(--ok-soft);
  color: var(--ok);
}
.pill.d-Pending {
  background: var(--normal-soft);
  color: var(--normal);
}
.pill.d-Failed {
  background: var(--urgent-soft);
  color: var(--urgent);
}
.form {
  display: flex;
  flex-direction: column;
  gap: 14px;
}
.form label {
  display: flex;
  flex-direction: column;
  gap: 6px;
  font-size: 0.85rem;
  font-weight: 500;
}
.form label.switch {
  flex-direction: row;
  align-items: center;
}
.secret {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-top: 14px;
  padding: 10px 12px;
  background: var(--surface-2);
  border: 1px solid var(--border);
  border-radius: 8px;
}
.secret code {
  flex: 1;
  min-width: 0;
  word-break: break-all;
  font-size: 0.85rem;
}
:deep(.ltr-input) {
  direction: ltr;
}
</style>
