<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import Select from 'primevue/select'
import ToggleSwitch from 'primevue/toggleswitch'
import Message from 'primevue/message'
import { api, type ApiKeyRow, type Department, type MailboxStatus, type Role, type UserRow, type WebhookEndpoint } from '../api'
import { formatDate, formatDateTime, toDate } from '../components/Badges'
import { useAuth } from '../stores/auth'
import { useDepartmentName, useDepartments } from '../stores/departments'
import WebhooksPanel from '../components/WebhooksPanel.vue'
import MailboxPanel from '../components/MailboxPanel.vue'
import DepartmentDialog from '../components/DepartmentDialog.vue'

defineEmits<{ upload: [] }>()

const { t, locale } = useI18n()
const toast = useToast()
const auth = useAuth()
const route = useRoute()
const router = useRouter()
const departments = useDepartments()
const departmentName = useDepartmentName()

// ── Sections: an overview of cards, each opening its section. The URL hash is the section (/admin#mailbox). ─
type Section = 'overview' | 'users' | 'departments' | 'mailbox' | 'api-keys' | 'webhooks'
const SECTION_OF: Record<string, Section> = {
  '#users': 'users', '#people': 'users',
  '#departments': 'departments', '#signatures': 'departments', '#codes': 'departments',
  '#mailbox': 'mailbox', '#api-keys': 'api-keys', '#webhooks': 'webhooks',
}
const section = computed<Section>(() => SECTION_OF[route.hash] ?? 'overview')
/** The department column the card was about (emails, signature, code), highlighted in the table. */
const focus = computed(() => ({ '#departments': 'contacts', '#signatures': 'signature', '#codes': 'code' })[route.hash] ?? null)

function open(hash: string) {
  router.push({ hash })
}
watch(() => route.hash, () => window.scrollTo({ top: 0 }))

const roleOptions = computed(() => (['Admin', 'Clerk', 'Department'] as Role[]).map((r) => ({ label: t(`auth.roles.${r}`), value: r })))
const departmentOptions = computed(() => departments.items.map((d) => ({ label: departmentName(d.id), value: d.id })))

// ── People ────────────────────────────────────────────────────────
const users = ref<UserRow[]>([])
const usersError = ref<string | null>(null)
const busy = ref(false)
const search = ref('')
type PeopleFilter = 'all' | 'pending' | 'Admin' | 'Clerk' | 'Department' | 'disabled'
const peopleFilter = ref<PeopleFilter>('all')

async function loadUsers() {
  try {
    users.value = await api.users()
    usersError.value = null
  } catch (e) {
    usersError.value = (e as Error).message
  }
}

const matches = (u: UserRow, f: PeopleFilter) =>
  f === 'all' ? true : f === 'pending' ? !u.role && !u.disabled : f === 'disabled' ? u.disabled : u.role === f && !u.disabled
const peopleFilters = computed(() =>
  (['all', 'pending', 'Admin', 'Clerk', 'Department', 'disabled'] as PeopleFilter[]).map((f) => ({
    value: f,
    label: f === 'all' ? t('admin.filterAll') : f === 'pending' ? t('admin.pending') : f === 'disabled' ? t('admin.disabled') : t(`auth.roles.${f}`),
    count: users.value.filter((u) => matches(u, f)).length,
  })),
)
/** People waiting for a role come first: they can sign in but see nothing yet. */
const shownUsers = computed(() => {
  const q = search.value.trim().toLowerCase()
  return users.value
    .filter((u) => matches(u, peopleFilter.value))
    .filter((u) => !q || u.displayName.toLowerCase().includes(q) || (u.email ?? '').toLowerCase().includes(q))
    .sort((a, b) => Number(!!a.role) - Number(!!b.role) || a.displayName.localeCompare(b.displayName))
})
const staffOf = (departmentId: number) => users.value.filter((u) => u.role === 'Department' && u.departmentId === departmentId && !u.disabled).length

const editing = ref<UserRow | null>(null)
const dialog = ref(false)
const form = reactive({ displayName: '', email: '', password: '', role: 'Clerk' as Role | null, departmentId: null as number | null, disabled: false })
const formError = ref<string | null>(null)
const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/
const formProblem = computed(() => {
  if (!form.displayName.trim()) return t('admin.needName')
  if (!editing.value && !EMAIL.test(form.email.trim())) return t('admin.invalidEmail')
  if (form.role === 'Department' && !form.departmentId) return t('admin.needDepartment')
  return null
})
const isMe = computed(() => !!editing.value && editing.value.id === auth.me?.id)

function openCreate() {
  editing.value = null
  Object.assign(form, { displayName: '', email: '', password: '', role: 'Clerk', departmentId: null, disabled: false })
  formError.value = null
  dialog.value = true
}

function openEdit(u: UserRow) {
  editing.value = u
  // A person waiting for a role: suggest the department role, the most common one.
  Object.assign(form, { displayName: u.displayName, email: u.email ?? '', password: '', role: u.role ?? 'Department', departmentId: u.departmentId, disabled: u.disabled })
  formError.value = null
  dialog.value = true
}

async function save() {
  if (formProblem.value) return
  busy.value = true
  formError.value = null
  try {
    const departmentId = form.role === 'Department' ? form.departmentId : null
    if (editing.value) {
      await api.updateUser(editing.value.id, { displayName: form.displayName.trim(), role: form.role, departmentId, disabled: form.disabled })
      if (form.password) await api.setPassword(editing.value.id, form.password)
    } else {
      await api.createUser({ displayName: form.displayName.trim(), email: form.email.trim(), password: form.password || null, role: form.role!, departmentId })
    }
    toast.add({ severity: 'success', summary: t('admin.saved'), life: 2000 })
    dialog.value = false
    await loadUsers()
  } catch (e) {
    formError.value = (e as Error).message
  } finally {
    busy.value = false
  }
}

// ── Departments ───────────────────────────────────────────────────
const editingDepartment = ref<Department | null>(null)
const hasSignature = (d: Department) => !!d.signatoryTitleEn && !!d.signatoryTitleAr

// ── API keys ──────────────────────────────────────────────────────
const keys = ref<ApiKeyRow[]>([])
const keysError = ref<string | null>(null)
const keyDialog = ref(false)
const keyForm = reactive({ name: '', role: 'Clerk' as Role, departmentId: null as number | null, expiresOn: '' })
const keyError = ref<string | null>(null)
const newSecret = ref<string | null>(null)
const copied = ref(false)
const revoking = ref<ApiKeyRow | null>(null)
const SOON_DAYS = 14

const keyState = (k: ApiKeyRow) =>
  k.revokedAt ? 'revoked' : k.expiresAt && toDate(k.expiresAt) <= new Date() ? 'expired' : 'active'
const expiresSoon = (k: ApiKeyRow) =>
  keyState(k) === 'active' && !!k.expiresAt && toDate(k.expiresAt).getTime() - Date.now() < SOON_DAYS * 86_400_000
const activeKeys = computed(() => keys.value.filter((k) => keyState(k) === 'active'))
/** Active keys first, then expired, then revoked. */
const shownKeys = computed(() => {
  const order = { active: 0, expired: 1, revoked: 2 }
  return [...keys.value].sort((a, b) => order[keyState(a)] - order[keyState(b)])
})

async function loadKeys() {
  try {
    keys.value = await api.apiKeys()
    keysError.value = null
  } catch (e) {
    keysError.value = (e as Error).message
  }
}

function openCreateKey() {
  Object.assign(keyForm, { name: '', role: 'Clerk', departmentId: null, expiresOn: '' })
  keyError.value = null
  keyDialog.value = true
}

async function createKey() {
  if (!keyForm.name.trim() || (keyForm.role === 'Department' && !keyForm.departmentId)) return
  busy.value = true
  keyError.value = null
  try {
    const created = await api.createApiKey({
      name: keyForm.name.trim(),
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

// ── Setup check ───────────────────────────────────────────────────
const mailbox = ref<MailboxStatus | null>(null)
const webhooks = ref<WebhookEndpoint[]>([])

async function loadConnections() {
  const [m, w] = await Promise.allSettled([api.mailboxStatus(), api.webhooks()])
  mailbox.value = m.status === 'fulfilled' ? m.value : null
  webhooks.value = w.status === 'fulfilled' ? w.value : []
}

type Check = { key: string; tone: 'warn' | 'error' | 'info'; text: string; hash: string; action?: () => void }
const checks = computed<Check[]>(() => {
  const list: Check[] = []
  const pending = users.value.filter((u) => !u.role && !u.disabled).length
  if (pending) list.push({ key: 'pending', tone: 'warn', text: t('admin.health.pending', { n: pending }), hash: '#users', action: () => (peopleFilter.value = 'pending') })
  const admins = users.value.filter((u) => u.role === 'Admin' && !u.disabled).length
  if (users.value.length && admins === 1) list.push({ key: 'oneAdmin', tone: 'info', text: t('admin.health.oneAdmin'), hash: '#users' })
  const noEmail = departments.items.filter((d) => !d.email)
  if (noEmail.length) list.push({ key: 'noEmail', tone: 'warn', text: t('admin.health.noEmail', { list: joinNames(noEmail.map((d) => d.id)) }), hash: '#departments' })
  const noSignature = departments.items.filter((d) => !hasSignature(d))
  if (noSignature.length) list.push({ key: 'noSignature', tone: 'info', text: t('admin.health.noSignature', { list: joinNames(noSignature.map((d) => d.id)) }), hash: '#signatures' })
  for (const k of keys.value.filter(expiresSoon))
    list.push({ key: `key-${k.id}`, tone: 'warn', text: t('admin.health.expiring', { name: k.name, when: formatDate(k.expiresAt, locale.value) }), hash: '#api-keys' })
  if (mailbox.value?.configured && mailbox.value.lastError)
    list.push({ key: 'mailbox', tone: 'error', text: t('admin.health.mailbox', { error: mailbox.value.lastError }), hash: '#mailbox' })
  else if (mailbox.value?.configured && !mailbox.value.enabled)
    list.push({ key: 'mailboxOff', tone: 'info', text: t('admin.health.mailboxOff'), hash: '#mailbox' })
  const failing = webhooks.value.filter((w) => !w.enabled || w.consecutiveFailures > 0).length
  if (failing) list.push({ key: 'webhooks', tone: 'warn', text: t('admin.health.webhooks', { n: failing }), hash: '#webhooks' })
  return list
})
const joinNames = (ids: number[]) => ids.map((id) => departmentName(id)).join(locale.value === 'ar' ? '، ' : ', ')
const CHECK_ICON = { error: 'pi pi-times-circle', warn: 'pi pi-exclamation-triangle', info: 'pi pi-info-circle' }

function fix(c: Check) {
  c.action?.()
  open(c.hash)
}

// ── Overview cards: what each area is, a live count, and whether it needs attention ─
type Badge = { tone: 'ok' | 'warn' | 'error' | 'info'; text: string }
type Card = { hash: string; icon: string; title: string; text: string; stat: string; badge?: Badge }
const cardGroups = computed(() => {
  const total = departments.items.length
  const active = users.value.filter((u) => u.role && !u.disabled).length
  const pending = users.value.filter((u) => !u.role && !u.disabled).length
  const withInbox = departments.items.filter((d) => d.email).length
  const withCode = departments.items.filter((d) => d.code).length
  const withSignature = departments.items.filter(hasSignature).length
  const soon = keys.value.filter(expiresSoon).length
  const failing = webhooks.value.filter((w) => !w.enabled || w.consecutiveFailures > 0).length
  const m = mailbox.value
  const ofAll = (n: number): Badge => (n === total ? { tone: 'ok', text: t('admin.cards.allSet') } : { tone: 'warn', text: t('admin.cards.missing', { n: total - n }) })

  const team: Card[] = [
    {
      hash: '#users', icon: 'pi pi-users', title: t('admin.users'), text: t('admin.cards.usersText'),
      stat: t('admin.cards.usersStat', { n: active }),
      badge: pending ? { tone: 'warn', text: t('admin.cards.pending', { n: pending }) } : undefined,
    },
    {
      hash: '#departments', icon: 'pi pi-envelope', title: t('admin.cards.emails'), text: t('admin.cards.emailsText'),
      stat: t('admin.cards.ofDepartments', { n: withInbox, total }), badge: ofAll(withInbox),
    },
    {
      hash: '#mailbox', icon: 'pi pi-inbox', title: t('mailbox.title'), text: t('admin.cards.mailboxText'),
      stat: !m?.configured ? t('admin.cards.notSetUp') : m.enabled ? t('admin.cards.checking', { s: m.pollSeconds }) : t('admin.cards.manualOnly'),
      badge: m?.configured ? (m.lastError ? { tone: 'error', text: t('admin.cards.problem') } : { tone: 'ok', text: t('admin.cards.healthy') }) : undefined,
    },
    {
      hash: '#codes', icon: 'pi pi-hashtag', title: t('admin.codes'), text: t('admin.cards.codesText'),
      stat: t('admin.cards.ofDepartments', { n: withCode, total }),
    },
  ]
  const integrations: Card[] = [
    {
      hash: '#signatures', icon: 'pi pi-id-card', title: t('admin.signatures'), text: t('admin.cards.signaturesText'),
      stat: t('admin.cards.ofDepartments', { n: withSignature, total }), badge: ofAll(withSignature),
    },
    {
      hash: '#api-keys', icon: 'pi pi-key', title: t('admin.apiKeys'), text: t('admin.cards.keysText'),
      stat: t('admin.cards.keysStat', { n: activeKeys.value.length }),
      badge: soon ? { tone: 'warn', text: t('admin.cards.expiring', { n: soon }) } : undefined,
    },
    {
      hash: '#webhooks', icon: 'pi pi-bolt', title: t('webhooks.title'), text: t('admin.cards.webhooksText'),
      stat: t('admin.cards.endpoints', { n: webhooks.value.length }),
      badge: !webhooks.value.length ? undefined : failing ? { tone: 'warn', text: t('admin.cards.failing', { n: failing }) } : { tone: 'ok', text: t('admin.cards.healthy') },
    },
  ]
  return [
    { key: 'team', title: t('admin.cards.team'), hint: t('admin.cards.teamHint'), cards: team },
    { key: 'integrations', title: t('admin.cards.integrations'), hint: t('admin.cards.integrationsHint'), cards: integrations },
  ]
})

const loaded = ref(false)
onMounted(async () => {
  await Promise.all([loadUsers(), loadKeys(), departments.load(), loadConnections()])
  loaded.value = true
})
</script>

<template>
  <!-- ── Overview ───────────────────────────────────────────────── -->
  <template v-if="section === 'overview'">
    <header class="page-head head-row">
      <div>
        <h1>{{ t('admin.title') }}</h1>
        <p class="muted">{{ t('admin.subtitle') }}</p>
      </div>
      <span class="pill neutral access"><i class="pi pi-shield" /> {{ t('admin.cards.access') }}</span>
    </header>

    <!-- Setup check: what needs an admin's attention, with a way to fix it -->
    <section v-if="loaded" class="card health" :class="{ ok: !checks.length }">
      <div class="health-head">
        <i :class="checks.length ? 'pi pi-list-check' : 'pi pi-check-circle'" />
        <h2>{{ t('admin.health.title') }}</h2>
        <span v-if="checks.length" class="pill neutral">{{ checks.length }}</span>
      </div>
      <p v-if="!checks.length" class="muted small">{{ t('admin.health.ok') }}</p>
      <ul v-else>
        <li v-for="c in checks" :key="c.key" :class="c.tone">
          <i :class="CHECK_ICON[c.tone]" />
          <span class="grow" dir="auto">{{ c.text }}</span>
          <Button :label="t('admin.health.fix')" size="small" text @click="fix(c)" />
        </li>
      </ul>
    </section>

    <section v-for="g in cardGroups" :key="g.key" class="group">
      <h2 class="group-title">{{ g.title }} <span class="muted">{{ g.hint }}</span></h2>
      <div class="cards" :class="`n${g.cards.length}`">
        <RouterLink v-for="c in g.cards" :key="c.hash" :to="{ hash: c.hash }" class="card admin-card">
          <div class="card-top">
            <span class="card-icon"><i :class="c.icon" /></span>
            <h3>{{ c.title }}</h3>
            <i class="pi pi-arrow-up-right go flip-rtl" />
          </div>
          <p class="muted small">{{ c.text }}</p>
          <div class="card-foot">
            <span class="faint small">{{ loaded ? c.stat : '…' }}</span>
            <span v-if="loaded && c.badge" class="badge" :class="c.badge.tone">{{ c.badge.text }}</span>
          </div>
        </RouterLink>
      </div>
    </section>
  </template>

  <!-- ── One section ────────────────────────────────────────────── -->
  <RouterLink v-else :to="{ hash: '' }" class="back small"><i class="pi pi-arrow-left flip-rtl" /> {{ t('admin.title') }}</RouterLink>

  <!-- ── People ─────────────────────────────────────────────────── -->
  <section v-if="section === 'users'" id="users" class="card block">
    <div class="block-head">
      <div>
        <h2>{{ t('admin.users') }}</h2>
        <p class="small muted hint">{{ t('admin.usersHint') }}</p>
      </div>
      <Button :label="t('admin.add')" icon="pi pi-user-plus" size="small" @click="openCreate" />
    </div>
    <div class="toolbar">
      <InputText v-model="search" :placeholder="t('admin.search')" class="search" :aria-label="t('admin.search')" />
      <div class="chips" role="group">
        <button
          v-for="f in peopleFilters"
          :key="f.value"
          type="button"
          class="chip"
          :class="{ active: peopleFilter === f.value, warn: f.value === 'pending' && f.count }"
          :aria-pressed="peopleFilter === f.value"
          @click="peopleFilter = f.value"
        >
          {{ f.label }} <span class="faint">{{ f.count }}</span>
        </button>
      </div>
    </div>
    <Message v-if="usersError" severity="error" class="inline-msg">
      {{ usersError }} <Button :label="t('actions.retry')" size="small" text @click="loadUsers" />
    </Message>
    <p v-else-if="!shownUsers.length" class="muted empty">{{ t('admin.noUsersMatch') }}</p>
    <div v-else class="table-wrap">
      <table class="table">
        <thead>
          <tr>
            <th>{{ t('admin.name') }}</th>
            <th>{{ t('admin.role') }}</th>
            <th>{{ t('admin.signIn') }}</th>
            <th>{{ t('admin.added') }}</th>
            <th>{{ t('admin.lastSeen') }}</th>
            <th />
          </tr>
        </thead>
        <tbody>
          <tr v-for="u in shownUsers" :key="u.id" :class="{ disabled: u.disabled, pending: !u.role && !u.disabled }">
            <td>
              <strong dir="auto">{{ u.displayName }}</strong>
              <span v-if="u.id === auth.me?.id" class="pill neutral you">{{ t('admin.you') }}</span>
              <div class="faint ltr small">{{ u.email }}</div>
            </td>
            <td>
              <span v-if="u.role" class="pill neutral">{{ t(`auth.roles.${u.role}`) }}</span>
              <span v-else class="pill p-High">{{ t('admin.pending') }}</span>
              <div v-if="u.role === 'Department'" class="small muted">{{ departmentName(u.departmentId) }}</div>
              <div v-if="u.disabled" class="small danger">{{ t('admin.disabled') }}</div>
            </td>
            <td class="small">
              <span v-if="u.hasPassword" class="tag"><i class="pi pi-key" /> {{ t('admin.local') }}</span>
              <span v-for="l in u.logins" :key="l" class="tag"><i class="pi pi-microsoft" /> {{ l }}</span>
            </td>
            <td class="small muted nowrap">{{ formatDate(u.createdAt, locale) }}</td>
            <td class="small muted nowrap">{{ u.lastSignInAt ? formatDateTime(u.lastSignInAt, locale) : t('admin.never') }}</td>
            <td class="end nowrap">
              <Button v-if="!u.role && !u.disabled" :label="t('admin.assignRole')" icon="pi pi-user-edit" size="small" @click="openEdit(u)" />
              <Button v-else :label="t('admin.edit')" icon="pi pi-pencil" size="small" text @click="openEdit(u)" />
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </section>

  <!-- ── Departments ────────────────────────────────────────────── -->
  <section v-if="section === 'departments'" id="departments" class="card block" :class="focus ? `focus-${focus}` : ''">
    <div class="block-head">
      <div>
        <h2>{{ t('admin.departments') }}</h2>
        <p class="small muted hint">{{ t('admin.departmentsHint') }}</p>
      </div>
      <RouterLink :to="{ name: 'register' }" class="small">{{ t('register.title') }} <i class="pi pi-arrow-right flip-rtl" /></RouterLink>
    </div>
    <div class="table-wrap">
      <table class="table">
        <thead>
          <tr>
            <th>{{ t('admin.department') }}</th>
            <th class="col-contacts">{{ t('admin.notifications') }}</th>
            <th class="col-signature">{{ t('admin.signature') }}</th>
            <th class="col-code">{{ t('admin.code') }}</th>
            <th>{{ t('admin.staffCol') }}</th>
            <th />
          </tr>
        </thead>
        <tbody>
          <tr v-for="d in departments.items" :key="d.id">
            <td>
              <strong>{{ departmentName(d.id) }}</strong>
              <div class="faint small" dir="auto">{{ locale === 'ar' ? d.nameEn : d.nameAr }}</div>
            </td>
            <td class="small col-contacts">
              <div v-if="d.email" class="ltr">{{ d.email }}</div>
              <div v-else class="missing"><i class="pi pi-exclamation-triangle" /> {{ t('admin.noInbox') }}</div>
              <div class="faint">
                {{ t('admin.headEmail') }}: <span class="ltr">{{ d.headEmail ?? t('admin.deskFallback') }}</span>
              </div>
            </td>
            <td class="small col-signature">
              <template v-if="hasSignature(d)">
                <div dir="auto">{{ locale === 'ar' ? d.signatoryTitleAr : d.signatoryTitleEn }}</div>
                <div v-if="d.signatoryNameEn || d.signatoryNameAr" class="faint" dir="auto">{{ locale === 'ar' ? d.signatoryNameAr ?? d.signatoryNameEn : d.signatoryNameEn ?? d.signatoryNameAr }}</div>
              </template>
              <div v-else class="missing soft"><i class="pi pi-info-circle" /> {{ t('admin.noSignature') }}</div>
            </td>
            <td class="small col-code"><code v-if="d.code">{{ d.code }}</code><span v-else class="faint">—</span></td>
            <td class="small">
              <RouterLink :to="{ name: 'department', params: { departmentId: d.id } }">{{ staffOf(d.id) ? t('admin.staff', { n: staffOf(d.id) }) : t('admin.noStaff') }}</RouterLink>
            </td>
            <td class="end"><Button :label="t('admin.edit')" icon="pi pi-pencil" size="small" text @click="editingDepartment = d" /></td>
          </tr>
        </tbody>
      </table>
    </div>
  </section>

  <!-- ── Connections ────────────────────────────────────────────── -->
  <template v-if="section === 'api-keys'">
    <section id="api-keys" class="card block">
      <div class="block-head">
        <div>
          <h2>{{ t('admin.apiKeys') }} <span class="faint small">· {{ t('admin.activeKeys', { n: activeKeys.length }) }}</span></h2>
          <p class="small muted hint">{{ t('admin.apiKeysHint') }}</p>
        </div>
        <Button :label="t('admin.newKey')" icon="pi pi-key" size="small" @click="openCreateKey" />
      </div>
      <Message v-if="keysError" severity="error" class="inline-msg">
        {{ keysError }} <Button :label="t('actions.retry')" size="small" text @click="loadKeys" />
      </Message>
      <p v-else-if="!keys.length" class="muted empty">{{ t('admin.noKeys') }}</p>
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
            <tr v-for="k in shownKeys" :key="k.id" :class="{ disabled: keyState(k) !== 'active' }">
              <td>
                <strong dir="auto">{{ k.name }}</strong>
                <div class="faint ltr small"><code>{{ k.prefix }}_…</code></div>
              </td>
              <td>
                <span class="pill neutral">{{ t(`auth.roles.${k.role}`) }}</span>
                <div v-if="k.role === 'Department'" class="small muted">{{ departmentName(k.departmentId) }}</div>
                <div v-if="keyState(k) !== 'active'" class="small danger">{{ t(`admin.${keyState(k)}`) }}</div>
              </td>
              <td class="small muted">
                {{ formatDateTime(k.createdAt, locale) }}
                <div v-if="k.createdBy">{{ k.createdBy }}</div>
              </td>
              <td class="small muted">{{ k.lastUsedAt ? formatDateTime(k.lastUsedAt, locale) : t('admin.never') }}</td>
              <td class="small">
                <span :class="expiresSoon(k) ? 'soon' : 'muted'">{{ k.expiresAt ? formatDate(k.expiresAt, locale) : t('admin.noExpiry') }}</span>
                <div v-if="expiresSoon(k)" class="soon"><i class="pi pi-clock" /> {{ t('admin.expiresSoon') }}</div>
              </td>
              <td class="end">
                <Button v-if="!k.revokedAt" :label="t('admin.revoke')" icon="pi pi-ban" size="small" text severity="danger" @click="revoking = k" />
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>

  </template>

  <MailboxPanel v-if="section === 'mailbox'" />

  <WebhooksPanel v-if="section === 'webhooks'" />

  <DepartmentDialog :department="editingDepartment" @close="editingDepartment = null" @saved="departments.items = [...departments.items]" />

  <Dialog v-model:visible="keyDialog" modal :header="t('admin.newKey')" :style="{ width: 'min(480px, 94vw)' }" :draggable="false">
    <form class="form" @submit.prevent="createKey">
      <label>
        <span>{{ t('admin.keyName') }}</span>
        <InputText v-model="keyForm.name" required fluid dir="auto" maxlength="100" :placeholder="t('admin.keyNamePlaceholder')" />
      </label>
      <label>
        <span>{{ t('admin.role') }} <small class="faint">· {{ t('admin.keyRoleHint') }}</small></span>
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
      <Button
        :label="t('admin.create')"
        icon="pi pi-check"
        :loading="busy"
        :disabled="!keyForm.name.trim() || (keyForm.role === 'Department' && !keyForm.departmentId)"
        @click="createKey"
      />
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
    <p v-if="revoking?.lastUsedAt" class="small muted">{{ t('admin.revokeLastUsed', { when: formatDateTime(revoking.lastUsedAt, locale) }) }}</p>
    <template #footer>
      <Button :label="t('actions.cancel')" severity="secondary" text @click="revoking = null" />
      <Button :label="t('admin.revoke')" icon="pi pi-ban" severity="danger" :loading="busy" @click="revokeKey" />
    </template>
  </Dialog>

  <Dialog v-model:visible="dialog" modal :header="editing ? editing.displayName : t('admin.add')" :style="{ width: 'min(480px, 94vw)' }" :draggable="false">
    <form class="form" @submit.prevent="save">
      <Message v-if="editing && !editing.role" severity="info" :closable="false">{{ t('admin.pendingHint') }}</Message>
      <label>
        <span>{{ t('admin.name') }}</span>
        <InputText v-model="form.displayName" required fluid dir="auto" maxlength="200" />
      </label>
      <label>
        <span>{{ t('auth.email') }}</span>
        <InputText v-model="form.email" type="email" required fluid dir="ltr" :disabled="!!editing" />
        <small v-if="editing" class="faint">{{ t('admin.emailFixed') }}</small>
      </label>
      <label>
        <span>{{ t('admin.role') }}</span>
        <Select v-model="form.role" :options="roleOptions" option-label="label" option-value="value" fluid :disabled="isMe" />
        <small class="faint">{{ isMe ? t('admin.ownRole') : form.role ? t(`admin.roleHint.${form.role}`) : '' }}</small>
      </label>
      <label v-if="form.role === 'Department'">
        <span>{{ t('admin.department') }}</span>
        <Select v-model="form.departmentId" :options="departmentOptions" option-label="label" option-value="value" fluid :placeholder="t('admin.needDepartment')" />
      </label>
      <label>
        <span>{{ editing ? t('admin.newPassword') : t('admin.password') }}</span>
        <Password v-model="form.password" toggle-mask :feedback="false" fluid autocomplete="new-password" input-class="ltr-input" />
        <small class="faint">{{ editing ? t('admin.newPasswordHint') : t('admin.passwordHint') }}</small>
      </label>
      <template v-if="editing && !isMe">
        <label class="switch">
          <ToggleSwitch v-model="form.disabled" />
          <span>{{ t('admin.disabled') }}</span>
        </label>
        <Message v-if="form.disabled && !editing.disabled" severity="warn" :closable="false">{{ t('admin.disableWarn') }}</Message>
      </template>
      <Message v-if="formError" severity="error">{{ formError }}</Message>
    </form>
    <template #footer>
      <Button :label="t('actions.cancel')" severity="secondary" text @click="dialog = false" />
      <Button :label="t('admin.save')" icon="pi pi-check" :loading="busy" :disabled="!!formProblem" :title="formProblem ?? ''" @click="save" />
    </template>
  </Dialog>
</template>

<style scoped>
.page-head {
  margin-bottom: 16px;
}
.page-head h1 {
  font-size: 1.6rem;
  font-weight: 700;
}
.page-head p {
  margin: 4px 0 0;
}
.health {
  padding: 14px 20px;
  margin-bottom: 16px;
  border-inline-start: 4px solid var(--high);
}
.health.ok {
  border-inline-start-color: var(--ok);
}
.health-head {
  display: flex;
  align-items: center;
  gap: 8px;
}
.health-head h2 {
  font-size: 1rem;
  margin: 0;
}
.health-head .pi {
  color: var(--high);
}
.health.ok .health-head .pi {
  color: var(--ok);
}
.health ul {
  list-style: none;
  margin: 8px 0 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.health li {
  display: flex;
  align-items: center;
  gap: 10px;
  font-size: 0.88rem;
}
.health li.warn .pi {
  color: var(--high);
}
.health li.error .pi {
  color: var(--urgent);
}
.health li.info .pi {
  color: var(--brand);
}
.health p {
  margin: 4px 0 0;
}
.head-row {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
}
.access {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  white-space: nowrap;
}
.group {
  margin-bottom: 22px;
}
.group-title {
  font-size: 0.95rem;
  font-weight: 700;
  margin: 0 0 10px;
}
.group-title .muted {
  font-size: 0.8rem;
  font-weight: 400;
  margin-inline-start: 8px;
}
.cards {
  display: grid;
  gap: 14px;
  grid-template-columns: repeat(2, minmax(0, 1fr));
}
.cards.n3 {
  grid-template-columns: repeat(3, minmax(0, 1fr));
}
@media (max-width: 900px) {
  .cards,
  .cards.n3 {
    grid-template-columns: 1fr;
  }
}
.admin-card {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 18px 20px;
  color: inherit;
  text-decoration: none;
  transition: border-color 0.15s, transform 0.15s, box-shadow 0.15s;
}
.admin-card:hover,
.admin-card:focus-visible {
  border-color: var(--brand);
  transform: translateY(-1px);
}
.card-top {
  display: flex;
  align-items: center;
  gap: 12px;
}
.card-top h3 {
  flex: 1;
  margin: 0;
  font-size: 1rem;
  font-weight: 600;
}
.card-icon {
  width: 38px;
  height: 38px;
  border-radius: 10px;
  display: grid;
  place-items: center;
  background: var(--brand-soft);
  color: var(--brand);
  flex: none;
}
.go {
  color: var(--faint);
  font-size: 0.8rem;
}
.admin-card p {
  margin: 0;
  flex: 1;
}
.card-foot {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  margin-top: 6px;
}
.badge {
  font-size: 0.72rem;
  font-weight: 600;
  padding: 2px 9px;
  border-radius: 999px;
  white-space: nowrap;
}
.badge.ok {
  background: var(--ok-soft);
  color: var(--ok);
}
.badge.warn {
  background: var(--high-soft);
  color: var(--high);
}
.badge.error {
  background: var(--urgent-soft);
  color: var(--urgent);
}
.badge.info {
  background: var(--brand-soft);
  color: var(--brand);
}
.back {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  margin-bottom: 12px;
  text-decoration: none;
}
.focus-contacts .col-contacts,
.focus-signature .col-signature,
.focus-code .col-code {
  background: var(--brand-soft);
}
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
.toolbar {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  align-items: center;
  padding: 12px 20px;
  border-bottom: 1px solid var(--border);
}
.search {
  flex: 1 1 220px;
  max-width: 320px;
}
.chips {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
.chip {
  border: 1px solid var(--border);
  background: var(--surface);
  color: var(--text);
  border-radius: 999px;
  padding: 4px 12px;
  font-size: 0.82rem;
  cursor: pointer;
}
.chip.active {
  border-color: var(--brand);
  background: var(--brand-soft);
  color: var(--brand);
}
.chip.warn:not(.active) {
  border-color: var(--high);
}
.inline-msg {
  margin: 12px 20px;
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
.table tr.pending td {
  background: var(--high-soft);
}
.you {
  margin-inline-start: 6px;
  font-size: 0.7rem;
}
.end {
  text-align: end;
}
.nowrap {
  white-space: nowrap;
}
.small {
  font-size: 0.8rem;
}
.danger {
  color: var(--urgent);
}
.soon {
  color: var(--high);
}
.missing {
  color: var(--high);
  display: flex;
  align-items: center;
  gap: 5px;
}
.missing.soft {
  color: var(--muted);
}
.tag {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  margin-inline-end: 10px;
  color: var(--muted);
}
.grow {
  flex: 1;
  min-width: 0;
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
</style>
