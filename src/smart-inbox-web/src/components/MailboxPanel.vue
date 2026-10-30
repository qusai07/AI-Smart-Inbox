<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import Select from 'primevue/select'
import Message from 'primevue/message'
import { api, type InboundEmail, type InboundEmailStatus, type MailboxStatus } from '../api'
import { formatDateTime } from './Badges'

const { t, locale } = useI18n()
const toast = useToast()

const status = ref<MailboxStatus | null>(null)
const imports = ref<InboundEmail[]>([])
const total = ref(0)
const page = ref(1)
const filter = ref<InboundEmailStatus | null>(null)
const testing = ref(false)
const checking = ref(false)
const retrying = ref<string | null>(null)
const testResult = ref<{ ok: boolean; message: string } | null>(null)

const filterOptions = computed(() => [
  { label: t('mailbox.all'), value: null },
  ...(['Imported', 'Skipped', 'Failed'] as InboundEmailStatus[]).map((s) => ({ label: t(`mailbox.status.${s}`), value: s })),
])

async function loadStatus() {
  try {
    status.value = await api.mailboxStatus()
  } catch {
    status.value = null
  }
}

async function loadImports(reset = true) {
  if (reset) page.value = 1
  const result = await api.mailboxImports(page.value, filter.value)
  imports.value = reset ? result.items : [...imports.value, ...result.items]
  total.value = result.total
}

async function more() {
  page.value++
  await loadImports(false)
}

async function test() {
  testing.value = true
  testResult.value = null
  try {
    testResult.value = await api.mailboxTest()
  } catch (e) {
    testResult.value = { ok: false, message: (e as Error).message }
  } finally {
    testing.value = false
  }
}

async function check() {
  checking.value = true
  try {
    const r = await api.mailboxCheck()
    toast.add({
      severity: r.failed ? 'warn' : 'success',
      summary: t('mailbox.checked', { imported: r.imported, skipped: r.skipped, failed: r.failed }),
      life: 4000,
    })
    await Promise.all([loadStatus(), loadImports()])
  } catch (e) {
    toast.add({ severity: 'error', summary: t('errors.generic'), detail: (e as Error).message, life: 6000 })
    await loadStatus()
  } finally {
    checking.value = false
  }
}

async function retry(row: InboundEmail) {
  retrying.value = row.id
  try {
    await api.mailboxRetry(row.id)
    toast.add({ severity: 'success', summary: t('mailbox.retryQueued'), life: 3000 })
    await loadImports()
  } catch (e) {
    toast.add({ severity: 'error', summary: t('errors.generic'), detail: (e as Error).message, life: 5000 })
  } finally {
    retrying.value = null
  }
}

watch(filter, () => loadImports())
onMounted(() => Promise.all([loadStatus(), loadImports().catch(() => {})]))
</script>

<template>
  <section id="mailbox" class="card block">
    <div class="block-head">
      <div>
        <h2>{{ t('mailbox.title') }}</h2>
        <p class="small muted hint">{{ t('mailbox.hint') }}</p>
      </div>
      <div v-if="status?.configured" class="row">
        <Button :label="t('mailbox.test')" icon="pi pi-wifi" size="small" severity="secondary" outlined :loading="testing" @click="test" />
        <Button :label="t('mailbox.check')" icon="pi pi-download" size="small" :loading="checking" @click="check" />
      </div>
    </div>

    <div class="body">
      <Message v-if="status && !status.configured" severity="info" :closable="false">
        {{ t('mailbox.notConfigured') }}
        <code class="ltr">Mailbox:Host · Mailbox:Username · Mailbox:Password · Mailbox:Enabled</code>
      </Message>

      <template v-if="status?.configured">
        <dl class="facts small">
          <div>
            <dt>{{ t('mailbox.state') }}</dt>
            <dd>
              <span class="pill" :class="status.enabled ? 'ok' : 'neutral'">{{ status.enabled ? t('mailbox.on', { s: status.pollSeconds }) : t('mailbox.off') }}</span>
            </dd>
          </div>
          <div><dt>{{ t('mailbox.address') }}</dt><dd class="ltr">{{ status.address }}</dd></div>
          <div><dt>{{ t('mailbox.server') }}</dt><dd class="ltr">{{ status.host }} · {{ status.folder }} → {{ status.processedFolder ?? t('mailbox.flagOnly') }}</dd></div>
          <div>
            <dt>{{ t('mailbox.lastCheck') }}</dt>
            <dd>
              <template v-if="status.lastCheckAt">
                {{ formatDateTime(status.lastCheckAt, locale) }}
                <span v-if="status.lastResult" class="muted">· {{ t('mailbox.checked', { imported: status.lastResult.imported, skipped: status.lastResult.skipped, failed: status.lastResult.failed }) }}</span>
              </template>
              <template v-else>{{ t('admin.never') }}</template>
            </dd>
          </div>
          <div v-if="status.nextCheckAt"><dt>{{ t('mailbox.nextCheck') }}</dt><dd>{{ formatDateTime(status.nextCheckAt, locale) }}</dd></div>
        </dl>
        <Message v-if="status.lastError" severity="error" :closable="false">{{ status.lastError }}</Message>
        <Message v-if="testResult" :severity="testResult.ok ? 'success' : 'error'" :closable="false">{{ testResult.message }}</Message>
      </template>

      <div class="imports-head">
        <h3>{{ t('mailbox.recent') }}</h3>
        <Select v-model="filter" :options="filterOptions" option-label="label" option-value="value" size="small" />
      </div>
      <p v-if="!imports.length" class="muted small">{{ t('mailbox.none') }}</p>
      <div v-else class="table-wrap">
        <table class="table">
          <thead>
            <tr>
              <th>{{ t('mailbox.received') }}</th>
              <th>{{ t('mailbox.from') }}</th>
              <th>{{ t('mailbox.subject') }}</th>
              <th>{{ t('mailbox.result') }}</th>
              <th />
            </tr>
          </thead>
          <tbody>
            <tr v-for="e in imports" :key="e.id">
              <td class="small muted nowrap">{{ formatDateTime(e.receivedAt, locale) }}</td>
              <td class="small">
                <div dir="auto">{{ e.fromName ?? e.fromAddress ?? '—' }}</div>
                <div v-if="e.fromName" class="faint ltr">{{ e.fromAddress }}</div>
              </td>
              <td class="small" dir="auto">{{ e.subject ?? '—' }}</td>
              <td class="small">
                <span class="pill" :class="`m-${e.status}`">{{ t(`mailbox.status.${e.status}`) }}</span>
                <div v-if="e.status === 'Imported'" class="docs">
                  <RouterLink v-for="(id, i) in e.documentIds" :key="id" :to="{ name: 'document', params: { id } }">
                    {{ t('mailbox.document', { n: i + 1 }) }}
                  </RouterLink>
                </div>
                <div v-if="e.reason" class="muted">{{ e.reason }}</div>
                <div v-if="e.skippedAttachments.length" class="faint">{{ t('mailbox.notImported') }}: {{ e.skippedAttachments.join(', ') }}</div>
              </td>
              <td class="end">
                <Button v-if="e.status === 'Failed'" :label="t('mailbox.retry')" icon="pi pi-refresh" size="small" text :loading="retrying === e.id" @click="retry(e)" />
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <Button v-if="imports.length < total" :label="t('webhooks.more')" severity="secondary" text size="small" @click="more" />
    </div>
  </section>
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
.row {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}
.body {
  padding: 14px 20px 18px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.facts {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 10px 20px;
  margin: 0;
}
.facts dt {
  font-weight: 600;
  color: var(--faint);
  font-size: 0.75rem;
}
.facts dd {
  margin: 2px 0 0;
}
.imports-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-top: 4px;
}
.imports-head h3 {
  font-size: 0.9rem;
  margin: 0;
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
.docs {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 4px;
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
.pill.ok,
.pill.m-Imported {
  background: var(--ok-soft);
  color: var(--ok);
}
.pill.m-Skipped {
  background: var(--surface-2);
  color: var(--muted);
}
.pill.m-Failed {
  background: var(--urgent-soft);
  color: var(--urgent);
}
</style>
