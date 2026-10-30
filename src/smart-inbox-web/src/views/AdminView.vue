<script setup lang="ts">
import { computed, nextTick, onMounted, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import Select from 'primevue/select'
import ToggleSwitch from 'primevue/toggleswitch'
import Message from 'primevue/message'
import { api, type ApiKeyRow, type Role, type UserRow } from '../api'
import { formatDateTime, toDate } from '../components/Badges'
import { useAuth } from '../stores/auth'
import { useDepartmentName, useDepartments } from '../stores/departments'
import WebhooksPanel from '../components/WebhooksPanel.vue'
import MailboxPanel from '../components/MailboxPanel.vue'

defineEmits<{ upload: [] }>()

const { t, locale } = useI18n()
const toast = useToast()
const auth = useAuth()
const departments = useDepartments()
const departmentName = useDepartmentName()

const users = ref<UserRow[]>([])
const error = ref<string | null>(null)
const busy = ref(false)

const roleOptions = computed(() => (['Admin', 'Clerk', 'Department'] as Role[]).map((r) => ({ label: t(`auth.roles.${r}`), value: r })))
const departmentOptions = computed(() => departments.items.map((d) => ({ label: departmentName(d.id), value: d.id })))

async function load() {
  try {
    users.value = await api.users()
  } catch (e) {
    error.value = (e as Error).message
  }
}

// ── Create / edit user ────────────────────────────────────────────
const editing = ref<UserRow | null>(null)
const dialog = ref(false)
const form = reactive({ displayName: '', email: '', password: '', role: 'Clerk' as Role | null, departmentId: null as number | null, disabled: false })
const formError = ref<string | null>(null)

function openCreate() {
  editing.value = null
  Object.assign(form, { displayName: '', email: '', password: '', role: 'Clerk', departmentId: null, disabled: false })
  formError.value = null
  dialog.value = true
}

function openEdit(u: UserRow) {
  editing.value = u
  Object.assign(form, { displayName: u.displayName, email: u.email ?? '', password: '', role: u.role, departmentId: u.departmentId, disabled: u.disabled })
  formError.value = null
  dialog.value = true
}

async function save() {
  busy.value = true
  formError.value = null
  try {
    const departmentId = form.role === 'Department' ? form.departmentId : null
    if (editing.value) {
      await api.updateUser(editing.value.id, { displayName: form.displayName, role: form.role, departmentId, disabled: form.disabled })
      if (form.password) await api.setPassword(editing.value.id, form.password)
    } else {
      await api.createUser({ displayName: form.displayName, email: form.email, password: form.password || null, role: form.role!, departmentId })
    }
    toast.add({ severity: 'success', summary: t('admin.saved'), life: 2000 })
    dialog.value = false
    await load()
  } catch (e) {
    formError.value = (e as Error).message
  } finally {
    busy.value = false
  }
}

// ── Department emails ─────────────────────────────────────────────
const emails = reactive<Record<number, string>>({})
const heads = reactive<Record<number, string>>({})
const savingEmail = ref<number | null>(null)

const changed = (id: number) => {
  const d = departments.items.find((x) => x.id === id)
  return (emails[id] ?? '') !== (d?.email ?? '') || (heads[id] ?? '') !== (d?.headEmail ?? '')
}

async function saveEmail(id: number) {
  savingEmail.value = id
  try {
    const email = emails[id]?.trim() || null
    const head = heads[id]?.trim() || null
    await api.setDepartmentContacts(id, email, head)
    const d = departments.items.find((x) => x.id === id)
    if (d) {
      d.email = email
      d.headEmail = head
    }
    toast.add({ severity: 'success', summary: t('admin.saved'), life: 2000 })
  } catch (e) {
    toast.add({ severity: 'error', summary: t('errors.generic'), detail: (e as Error).message, life: 5000 })
  } finally {
    savingEmail.value = null
  }
}

// ── API keys ──────────────────────────────────────────────────────
const keys = ref<ApiKeyRow[]>([])
const keyDialog = ref(false)
const keyForm = reactive({ name: '', role: 'Clerk' as Role, departmentId: null as number | null, expiresOn: '' })
const keyError = ref<string | null>(null)
const newSecret = ref<string | null>(null)
const copied = ref(false)
const revoking = ref<ApiKeyRow | null>(null)

const keyState = (k: ApiKeyRow) =>
  k.revokedAt ? 'revoked' : k.expiresAt && toDate(k.expiresAt) <= new Date() ? 'expired' : 'active'

async function loadKeys() {
  try {
    keys.value = await api.apiKeys()
  } catch (e) {
    error.value = (e as Error).message
  }
}

function openCreateKey() {
  Object.assign(keyForm, { name: '', role: 'Clerk', departmentId: null, expiresOn: '' })
  keyError.value = null
  keyDialog.value = true
}

async function createKey() {
  busy.value = true
  keyError.value = null
  try {
    const created = await api.createApiKey({
      name: keyForm.name,
      role: keyForm.role,
      departmentId: keyForm.role === 'Department' ? keyForm.departmentId : null,
      // The key works through the whole chosen day.
      expiresAt: keyForm.expiresOn ? `${keyForm.expiresOn}T23:59:59Z` : null,
    })
    keyDialog.value = false
    copied.value = false
    newSecret.value = created.secret
    await loadKeys()
  } catch (e) {
    keyError.value = (e as Error).message
  } finally {
    busy.value = false
  }
}

async function copySecret() {
  if (!newSecret.value) return
  await navigator.clipboard.writeText(newSecret.value)
  copied.value = true
}

async function revokeKey() {
  if (!revoking.value) return
  busy.value = true
  try {
    await api.revokeApiKey(revoking.value.id)
    revoking.value = null
    await loadKeys()
  } catch (e) {
    toast.add({ severity: 'error', summary: t('errors.generic'), detail: (e as Error).message, life: 5000 })
  } finally {
    busy.value = false
  }
}

// ── Reply signatures ──────────────────────────────────────────────
type Signatory = { nameEn: string; nameAr: string; titleEn: string; titleAr: string }
const signatories = reactive<Record<number, Signatory>>({})
const savingSignatory = ref<number | null>(null)

const signatoryOf = (id: number): Signatory => {
  const d = departments.items.find((x) => x.id === id)
  return { nameEn: d?.signatoryNameEn ?? '', nameAr: d?.signatoryNameAr ?? '', titleEn: d?.signatoryTitleEn ?? '', titleAr: d?.signatoryTitleAr ?? '' }
}
const signatoryChanged = (id: number) => {
  const saved = signatoryOf(id)
  const now = signatories[id]
  return !!now && (Object.keys(saved) as (keyof Signatory)[]).some((k) => now[k].trim() !== saved[k])
}

async function saveSignatory(id: number) {
  savingSignatory.value = id
  try {
    const s = signatories[id]
    const body = { nameEn: s.nameEn.trim() || null, nameAr: s.nameAr.trim() || null, titleEn: s.titleEn.trim() || null, titleAr: s.titleAr.trim() || null }
    await api.setDepartmentSignatory(id, body)
    const d = departments.items.find((x) => x.id === id)
    if (d) Object.assign(d, { signatoryNameEn: body.nameEn, signatoryNameAr: body.nameAr, signatoryTitleEn: body.titleEn, signatoryTitleAr: body.titleAr })
    toast.add({ severity: 'success', summary: t('admin.saved'), life: 2000 })
  } catch (e) {
    toast.add({ severity: 'error', summary: t('errors.generic'), detail: (e as Error).message, life: 5000 })
  } finally {
    savingSignatory.value = null
  }
}

// ── Department codes (outgoing numbers) ──────────────────────────
const codes = reactive<Record<number, string>>({})
const savingCode = ref<number | null>(null)
const codeChanged = (id: number) =>
  (codes[id] ?? '').trim().toUpperCase() !== (departments.items.find((x) => x.id === id)?.code ?? '')

async function saveCode(id: number) {
  savingCode.value = id
  try {
    const code = codes[id].trim().toUpperCase() || null
    await api.setDepartmentCode(id, code)
    const d = departments.items.find((x) => x.id === id)
    if (d) d.code = code
    codes[id] = code ?? ''
    toast.add({ severity: 'success', summary: t('admin.saved'), life: 2000 })
  } catch (e) {
    toast.add({ severity: 'error', summary: t('errors.generic'), detail: (e as Error).message, life: 5000 })
  } finally {
    savingCode.value = null
  }
}

onMounted(async () => {
  await Promise.all([load(), loadKeys(), departments.load()])
  for (const d of departments.items) {
    emails[d.id] = d.email ?? ''
    heads[d.id] = d.headEmail ?? ''
    signatories[d.id] = signatoryOf(d.id)
    codes[d.id] = d.code ?? ''
  }
  await scrollToHash()
})

// Links such as /admin#webhooks (from the Integrations page and What's new) land on the right section.
const route = useRoute()
async function scrollToHash() {
  await nextTick()
  if (route.hash) document.querySelector(route.hash)?.scrollIntoView({ behavior: 'smooth', block: 'start' })
}
watch(() => route.hash, scrollToHash)
</script>

<template>
  <header class="page-head">
    <h1>{{ t('admin.title') }}</h1>
    <p class="muted">{{ t('admin.subtitle') }}</p>
  </header>

  <Message v-if="error" severity="error">{{ error }}</Message>

  <section class="card block">
    <div class="block-head">
      <h2>{{ t('admin.users') }}</h2>
      <Button :label="t('admin.add')" icon="pi pi-user-plus" size="small" @click="openCreate" />
    </div>
    <div class="table-wrap">
      <table class="table">
        <thead>
          <tr>
            <th>{{ t('admin.name') }}</th>
            <th>{{ t('admin.role') }}</th>
            <th>{{ t('admin.signIn') }}</th>
            <th>{{ t('admin.lastSeen') }}</th>
            <th />
          </tr>
        </thead>
        <tbody>
          <tr v-for="u in users" :key="u.id" :class="{ disabled: u.disabled }">
            <td>
              <strong>{{ u.displayName }}</strong>
              <div class="faint ltr small">{{ u.email }}</div>
            </td>
            <td>
              <span v-if="u.role" class="pill neutral">{{ t(`auth.roles.${u.role}`) }}</span>
              <span v-else class="pill p-High">{{ t('admin.pending') }}</span>
              <div v-if="u.role === 'Department'" class="small muted">{{ departmentName(u.departmentId) }}</div>
              <div v-if="u.disabled" class="small" style="color: var(--urgent)">{{ t('admin.disabled') }}</div>
            </td>
            <td class="small">
              <span v-if="u.hasPassword" class="tag"><i class="pi pi-key" /> {{ t('admin.local') }}</span>
              <span v-for="l in u.logins" :key="l" class="tag"><i class="pi pi-microsoft" /> {{ l }}</span>
            </td>
            <td class="small muted">{{ u.lastSignInAt ? formatDateTime(u.lastSignInAt, locale) : t('admin.never') }}</td>
            <td class="end">
              <Button :label="t('admin.edit')" icon="pi pi-pencil" size="small" text @click="openEdit(u)" />
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </section>

  <section class="card block">
    <div class="block-head">
      <h2>{{ t('admin.departments') }}</h2>
    </div>
    <div class="emails">
      <div class="email-row head-row">
        <span class="dept" />
        <span class="grow col-head">{{ t('admin.deptEmail') }} <small class="faint">· {{ t('admin.deptEmailHint') }}</small></span>
        <span class="grow col-head">{{ t('admin.headEmail') }} <small class="faint">· {{ t('admin.headEmailHint') }}</small></span>
        <span class="save-col" />
      </div>
      <div v-for="d in departments.items" :key="d.id" class="email-row">
        <span class="dept">{{ departmentName(d.id) }}</span>
        <InputText v-model="emails[d.id]" type="email" :placeholder="t('admin.emailPlaceholder')" dir="ltr" class="grow" :aria-label="t('admin.deptEmail')" />
        <InputText v-model="heads[d.id]" type="email" :placeholder="t('admin.headPlaceholder')" dir="ltr" class="grow" :aria-label="t('admin.headEmail')" />
        <Button :label="t('admin.save')" size="small" outlined class="save-col" :loading="savingEmail === d.id" :disabled="!changed(d.id)" @click="saveEmail(d.id)" />
      </div>
    </div>
  </section>

  <section id="signatures" class="card block">
    <div class="block-head">
      <div>
        <h2>{{ t('admin.signatures') }}</h2>
        <p class="small muted hint">{{ t('admin.signaturesHint') }}</p>
      </div>
    </div>
    <div class="emails">
      <div class="email-row head-row">
        <span class="dept" />
        <span class="grow col-head">{{ t('admin.titleEn') }}</span>
        <span class="grow col-head">{{ t('admin.titleAr') }}</span>
        <span class="grow col-head">{{ t('admin.nameEn') }} <small class="faint">· {{ t('admin.optional') }}</small></span>
        <span class="grow col-head">{{ t('admin.nameAr') }} <small class="faint">· {{ t('admin.optional') }}</small></span>
        <span class="save-col" />
      </div>
      <div v-for="d in departments.items" :key="d.id" class="email-row">
        <span class="dept">{{ departmentName(d.id) }}</span>
        <template v-if="signatories[d.id]">
          <InputText v-model="signatories[d.id].titleEn" dir="ltr" class="grow" maxlength="150" :placeholder="t('admin.titleEn')" :aria-label="t('admin.titleEn')" />
          <InputText v-model="signatories[d.id].titleAr" dir="rtl" class="grow" maxlength="150" :placeholder="t('admin.titleAr')" :aria-label="t('admin.titleAr')" />
          <InputText v-model="signatories[d.id].nameEn" dir="ltr" class="grow" maxlength="150" :placeholder="t('admin.nameEn')" :aria-label="t('admin.nameEn')" />
          <InputText v-model="signatories[d.id].nameAr" dir="rtl" class="grow" maxlength="150" :placeholder="t('admin.nameAr')" :aria-label="t('admin.nameAr')" />
        </template>
        <Button :label="t('admin.save')" size="small" outlined class="save-col" :loading="savingSignatory === d.id" :disabled="!signatoryChanged(d.id)" @click="saveSignatory(d.id)" />
      </div>
    </div>
  </section>

  <section id="codes" class="card block">
    <div class="block-head">
      <div>
        <h2>{{ t('admin.codes') }}</h2>
        <p class="small muted hint">{{ t('admin.codesHint') }}</p>
      </div>
      <RouterLink :to="{ name: 'register' }" class="small">{{ t('register.title') }} <i class="pi pi-arrow-right flip-rtl" /></RouterLink>
    </div>
    <div class="codes">
      <div v-for="d in departments.items" :key="d.id" class="code-row">
        <span class="dept">{{ departmentName(d.id) }}</span>
        <InputText v-model="codes[d.id]" dir="ltr" maxlength="10" class="code-input" placeholder="LEG" :aria-label="t('admin.code')" @keyup.enter="codeChanged(d.id) && saveCode(d.id)" />
        <Button :label="t('admin.save')" size="small" outlined :loading="savingCode === d.id" :disabled="!codeChanged(d.id)" @click="saveCode(d.id)" />
      </div>
    </div>
  </section>

  <section id="api-keys" class="card block">
    <div class="block-head">
      <div>
        <h2>{{ t('admin.apiKeys') }}</h2>
        <p class="small muted hint">{{ t('admin.apiKeysHint') }}</p>
      </div>
      <Button :label="t('admin.newKey')" icon="pi pi-key" size="small" @click="openCreateKey" />
    </div>
    <p v-if="!keys.length" class="muted empty">{{ t('admin.noKeys') }}</p>
    <div v-else class="table-wrap">
      <table class="table">
        <thead>
          <tr>
            <th>{{ t('admin.keyName') }}</th>
            <th>{{ t('admin.role') }}</th>
            <th>{{ t('admin.created') }}</th>
            <th>{{ t('admin.lastUsed') }}</th>
            <th>{{ t('admin.expires') }}</th>
            <th />
          </tr>
        </thead>
        <tbody>
          <tr v-for="k in keys" :key="k.id" :class="{ disabled: keyState(k) !== 'active' }">
            <td>
              <strong>{{ k.name }}</strong>
              <div class="faint ltr small"><code>{{ k.prefix }}_…</code></div>
            </td>
            <td>
              <span class="pill neutral">{{ t(`auth.roles.${k.role}`) }}</span>
              <div v-if="k.role === 'Department'" class="small muted">{{ departmentName(k.departmentId) }}</div>
              <div v-if="keyState(k) !== 'active'" class="small" style="color: var(--urgent)">{{ t(`admin.${keyState(k)}`) }}</div>
            </td>
            <td class="small muted">
              {{ formatDateTime(k.createdAt, locale) }}
              <div v-if="k.createdBy">{{ k.createdBy }}</div>
            </td>
            <td class="small muted">{{ k.lastUsedAt ? formatDateTime(k.lastUsedAt, locale) : t('admin.never') }}</td>
            <td class="small muted">{{ k.expiresAt ? formatDateTime(k.expiresAt, locale) : t('admin.noExpiry') }}</td>
            <td class="end">
              <Button v-if="!k.revokedAt" :label="t('admin.revoke')" icon="pi pi-ban" size="small" text severity="danger" @click="revoking = k" />
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </section>

  <MailboxPanel />

  <WebhooksPanel />

  <Dialog v-model:visible="keyDialog" modal :header="t('admin.newKey')" :style="{ width: 'min(480px, 94vw)' }" :draggable="false">
    <form class="form" @submit.prevent="createKey">
      <label>
        <span>{{ t('admin.keyName') }}</span>
        <InputText v-model="keyForm.name" required fluid dir="auto" maxlength="100" :placeholder="t('admin.keyNamePlaceholder')" />
      </label>
      <label>
        <span>{{ t('admin.role') }}</span>
        <Select v-model="keyForm.role" :options="roleOptions" option-label="label" option-value="value" fluid />
      </label>
      <label v-if="keyForm.role === 'Department'">
        <span>{{ t('admin.department') }}</span>
        <Select v-model="keyForm.departmentId" :options="departmentOptions" option-label="label" option-value="value" fluid />
      </label>
      <label>
        <span>{{ t('admin.expires') }} <small class="faint">· {{ t('admin.expiresHint') }}</small></span>
        <InputText v-model="keyForm.expiresOn" type="date" fluid dir="ltr" />
      </label>
      <Message v-if="keyError" severity="error">{{ keyError }}</Message>
    </form>
    <template #footer>
      <Button :label="t('actions.cancel')" severity="secondary" text @click="keyDialog = false" />
      <Button :label="t('admin.create')" icon="pi pi-check" :loading="busy" :disabled="!keyForm.name.trim()" @click="createKey" />
    </template>
  </Dialog>

  <Dialog :visible="!!newSecret" modal :closable="false" :header="t('admin.keyCreated')" :style="{ width: 'min(560px, 94vw)' }" :draggable="false">
    <Message severity="warn" :closable="false">{{ t('admin.keyCreatedHint') }}</Message>
    <div class="secret">
      <code class="ltr">{{ newSecret }}</code>
      <Button :label="copied ? t('admin.copied') : t('admin.copy')" :icon="copied ? 'pi pi-check' : 'pi pi-copy'" size="small" outlined @click="copySecret" />
    </div>
    <template #footer>
      <Button :label="t('admin.done')" @click="newSecret = null" />
    </template>
  </Dialog>

  <Dialog :visible="!!revoking" modal :header="t('admin.revokeTitle')" :style="{ width: 'min(440px, 94vw)' }" :draggable="false" @update:visible="(v: boolean) => { if (!v) revoking = null }">
    <p>{{ t('admin.revokeText', { name: revoking?.name }) }}</p>
    <template #footer>
      <Button :label="t('actions.cancel')" severity="secondary" text @click="revoking = null" />
      <Button :label="t('admin.revoke')" icon="pi pi-ban" severity="danger" :loading="busy" @click="revokeKey" />
    </template>
  </Dialog>

  <Dialog v-model:visible="dialog" modal :header="editing ? editing.displayName : t('admin.add')" :style="{ width: 'min(480px, 94vw)' }" :draggable="false">
    <form class="form" @submit.prevent="save">
      <label>
        <span>{{ t('admin.name') }}</span>
        <InputText v-model="form.displayName" required fluid dir="auto" />
      </label>
      <label>
        <span>{{ t('auth.email') }}</span>
        <InputText v-model="form.email" type="email" required fluid dir="ltr" :disabled="!!editing" />
      </label>
      <label>
        <span>{{ t('admin.role') }}</span>
        <Select v-model="form.role" :options="roleOptions" option-label="label" option-value="value" fluid :disabled="editing?.id === auth.me?.id" />
      </label>
      <label v-if="form.role === 'Department'">
        <span>{{ t('admin.department') }}</span>
        <Select v-model="form.departmentId" :options="departmentOptions" option-label="label" option-value="value" fluid />
      </label>
      <label>
        <span>{{ editing ? t('admin.newPassword') : t('admin.password') }}</span>
        <Password v-model="form.password" toggle-mask :feedback="false" fluid autocomplete="new-password" input-class="ltr-input" />
        <small class="faint">{{ t('admin.passwordHint') }}</small>
      </label>
      <label v-if="editing && editing.id !== auth.me?.id" class="switch">
        <ToggleSwitch v-model="form.disabled" />
        <span>{{ t('admin.disabled') }}</span>
      </label>
      <Message v-if="formError" severity="error">{{ formError }}</Message>
    </form>
    <template #footer>
      <Button :label="t('actions.cancel')" severity="secondary" text @click="dialog = false" />
      <Button :label="t('admin.save')" icon="pi pi-check" :loading="busy" @click="save" />
    </template>
  </Dialog>
</template>

<style scoped>
.page-head {
  margin-bottom: 20px;
}
.page-head h1 {
  font-size: 1.6rem;
  font-weight: 700;
}
.page-head p {
  margin: 4px 0 0;
}
.block {
  margin-bottom: 16px;
  overflow: hidden;
}
.block-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 14px 20px;
  border-bottom: 1px solid var(--border);
}
.block-head h2 {
  font-size: 1rem;
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
.table tr:last-child td {
  border-bottom: 0;
}
.table tr.disabled td {
  opacity: 0.6;
}
.end {
  text-align: end;
}
.small {
  font-size: 0.8rem;
}
.tag {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  margin-inline-end: 10px;
  color: var(--muted);
}
.emails {
  padding: 8px 20px 16px;
}
.email-row {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 8px 0;
}
.dept {
  width: 180px;
  flex: none;
  font-weight: 500;
}
.grow {
  flex: 1;
  min-width: 0;
}
.col-head {
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--muted);
}
.head-row {
  padding-bottom: 0;
}
.save-col {
  width: 72px;
  flex: none;
}
@media (max-width: 900px) {
  .head-row {
    display: none;
  }
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
.hint {
  margin: 2px 0 0;
}
.empty {
  padding: 16px 20px;
  margin: 0;
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
@media (max-width: 640px) {
  .email-row {
    flex-wrap: wrap;
  }
  .dept {
    width: 100%;
  }
}
.codes {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
  gap: 8px 24px;
  padding: 14px 20px 18px;
}
.code-row {
  display: flex;
  align-items: center;
  gap: 8px;
}
.code-row .dept {
  flex: 1;
  font-size: 0.88rem;
}
.code-input {
  width: 90px;
  text-transform: uppercase;
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
}
</style>
