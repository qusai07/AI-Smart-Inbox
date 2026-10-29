<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import Select from 'primevue/select'
import ToggleSwitch from 'primevue/toggleswitch'
import Message from 'primevue/message'
import { api, type Role, type UserRow } from '../api'
import { formatDateTime } from '../components/Badges'
import { useAuth } from '../stores/auth'
import { useDepartmentName, useDepartments } from '../stores/departments'

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

onMounted(async () => {
  await Promise.all([load(), departments.load()])
  for (const d of departments.items) {
    emails[d.id] = d.email ?? ''
    heads[d.id] = d.headEmail ?? ''
  }
})
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
</style>
