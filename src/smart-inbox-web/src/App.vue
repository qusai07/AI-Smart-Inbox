<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import Toast from 'primevue/toast'
import Button from 'primevue/button'
import UploadDialog from './components/UploadDialog.vue'
import WhatsNew from './components/WhatsNew.vue'
import { applyLocale } from './i18n'
import { applyTheme, theme } from './theme'
import { connected } from './realtime'
import { useAuth } from './stores/auth'
import { useDepartments } from './stores/departments'
import { useStats } from './stores/stats'

const { t, locale } = useI18n()
const route = useRoute()
const router = useRouter()
const auth = useAuth()
const stats = useStats()
const showUpload = ref(false)
const whatsNew = ref<InstanceType<typeof WhatsNew> | null>(null)
const menuOpen = ref(false)
const departments = useDepartments()

// Data needs a session: load once signed in (and again after switching user).
watch(
  () => auth.me?.id,
  (id) => {
    if (!id || auth.isPending) return
    departments.load()
    stats.load()
  },
  { immediate: true },
)

const showShell = computed(() => !route.meta.public && !!auth.me && !auth.isPending)

async function signOut() {
  await auth.logout()
  router.replace({ name: 'login' })
}

const departmentNav = computed(() =>
  departments.items
    .filter((d) => auth.isDesk || d.id === auth.me?.departmentId)
    .map((d) => ({
    id: d.id,
    label: locale.value === 'ar' ? d.nameAr : d.nameEn,
    count: stats.workloadFor(d.id).new,
    overdue: stats.workloadFor(d.id).overdue,
    active: route.name === 'department' && Number(route.params.departmentId) === d.id,
  })),
)
watch(() => route.fullPath, () => (menuOpen.value = false))

const counts = computed(() => stats.countByStatus)
const nav = computed(() => [
  ...(auth.isDesk
    ? [
        { to: { name: 'inbox' }, icon: 'pi pi-inbox', label: t('nav.inbox'), count: stats.stats?.totalDocuments, active: route.name === 'inbox' && !route.query.status },
        { to: { name: 'inbox', query: { status: 'NeedsReview' } }, icon: 'pi pi-exclamation-circle', label: t('nav.needsReview'), count: counts.value.NeedsReview, active: route.query.status === 'NeedsReview', tone: 'warn' },
        { to: { name: 'inbox', query: { status: 'Analyzed' } }, icon: 'pi pi-send', label: t('nav.ready'), count: counts.value.Analyzed, active: route.query.status === 'Analyzed' },
        { to: { name: 'dashboard' }, icon: 'pi pi-chart-bar', label: t('nav.dashboard'), active: route.name === 'dashboard' },
      ]
    : []),
  ...(auth.role
    ? [{ to: { name: 'register' }, icon: 'pi pi-hashtag', label: t('nav.register'), active: route.name === 'register' }]
    : []),
  ...(auth.isAdmin
    ? [
        { to: { name: 'admin' }, icon: 'pi pi-users', label: t('admin.nav'), active: route.name === 'admin' },
        { to: { name: 'integrations' }, icon: 'pi pi-share-alt', label: t('integrations.nav'), active: route.name === 'integrations' },
      ]
    : []),
] as { to: object; icon: string; label: string; count?: number; active: boolean; tone?: string }[])

function onUploaded() {
  stats.refreshSoon()
  window.dispatchEvent(new CustomEvent('inbox:refresh'))
}
</script>

<template>
  <Toast :position="locale === 'ar' ? 'bottom-left' : 'bottom-right'" />

  <!-- Login / pending pages render without the app shell -->
  <RouterView v-if="!showShell" />

  <div v-else class="shell">
    <aside class="sidebar" :class="{ open: menuOpen }">
      <div class="brand">
        <span class="mark" aria-hidden="true"><i class="pi pi-envelope" /><i class="pi pi-sparkles spark" /></span>
        <span class="brand-text">
          <strong>{{ t('app.title') }}</strong>
          <small>{{ t('app.subtitle') }}</small>
        </span>
        <Button class="menu-toggle" icon="pi pi-bars" text rounded severity="secondary" :aria-label="'Menu'" @click="menuOpen = !menuOpen" />
      </div>

      <div class="nav-wrap">
        <Button v-if="auth.isDesk" :label="t('actions.upload')" icon="pi pi-plus" class="new-btn" @click="showUpload = true" />

        <nav v-if="nav.length" class="nav">
          <RouterLink v-for="item in nav" :key="item.label" :to="item.to" class="nav-item" :class="{ active: item.active }">
            <i :class="item.icon" />
            <span class="grow">{{ item.label }}</span>
            <span v-if="item.count" class="count" :class="item.tone">{{ item.count }}</span>
          </RouterLink>
        </nav>

        <h2 class="nav-section">{{ t('handling.section') }}</h2>
        <nav class="nav departments">
          <RouterLink
            v-for="d in departmentNav"
            :key="d.id"
            :to="{ name: 'department', params: { departmentId: d.id } }"
            class="nav-item dept"
            :class="{ active: d.active }"
          >
            <span class="dept-dot" :class="{ alert: d.overdue }" />
            <span class="grow ellipsis">{{ d.label }}</span>
            <span v-if="d.count" class="count new">{{ d.count }}</span>
          </RouterLink>
        </nav>

        <div class="sidebar-foot">
          <div class="user">
            <span class="avatar">{{ (auth.me?.displayName ?? '?').trim().charAt(0).toUpperCase() }}</span>
            <span class="user-text">
              <strong class="ellipsis">{{ auth.me?.displayName }}</strong>
              <small class="ellipsis">{{ auth.role ? t(`auth.roles.${auth.role}`) : '' }}</small>
            </span>
            <Button icon="pi pi-sign-out" text rounded severity="secondary" size="small" :aria-label="t('auth.signOut')" :title="t('auth.signOut')" @click="signOut" />
          </div>
          <div class="toggles">
            <button class="seg" type="button" :title="t('nav.language')" @click="applyLocale(locale === 'ar' ? 'en' : 'ar')">
              <i class="pi pi-globe" />{{ locale === 'ar' ? 'English' : 'العربية' }}
            </button>
            <button class="seg" type="button" :title="t('nav.theme')" @click="applyTheme(theme === 'dark' ? 'light' : 'dark')">
              <i :class="theme === 'dark' ? 'pi pi-sun' : 'pi pi-moon'" />{{ theme === 'dark' ? t('nav.light') : t('nav.dark') }}
            </button>
          </div>
          <span class="live" :class="{ on: connected }"><span class="pulse" />{{ connected ? t('app.live') : t('app.offline') }}</span>
          <button type="button" class="whats-new-link" @click="whatsNew?.open()"><i class="pi pi-sparkles" /> {{ t('whatsNew.link') }}</button>
        </div>
      </div>
    </aside>

    <main class="content">
      <RouterView v-slot="{ Component }">
        <Transition name="fade" mode="out-in">
          <component :is="Component" :key="route.path" @upload="showUpload = true" />
        </Transition>
      </RouterView>
    </main>
  </div>

  <UploadDialog v-model:visible="showUpload" @uploaded="onUploaded" />
  <WhatsNew v-if="showShell" ref="whatsNew" />
</template>

<style scoped>
.whats-new-link {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  margin-top: 8px;
  padding: 0;
  border: 0;
  background: none;
  color: var(--muted);
  font: inherit;
  font-size: 0.8rem;
  cursor: pointer;
}
.whats-new-link:hover {
  color: var(--brand);
}
.shell {
  display: grid;
  grid-template-columns: var(--sidebar-w) minmax(0, 1fr);
  min-height: 100vh;
}

.sidebar {
  position: sticky;
  top: 0;
  height: 100vh;
  display: flex;
  flex-direction: column;
  background: var(--surface);
  border-inline-end: 1px solid var(--border);
  padding: 18px 14px;
}

.brand {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 0 6px 18px;
}
.mark {
  position: relative;
  display: grid;
  place-items: center;
  width: 38px;
  height: 38px;
  border-radius: 10px;
  background: linear-gradient(135deg, var(--brand), var(--brand-strong));
  color: var(--brand-contrast);
  flex: none;
}
.mark .pi-envelope {
  font-size: 1.05rem;
}
.mark .spark {
  position: absolute;
  top: -5px;
  inset-inline-end: -5px;
  font-size: 0.7rem;
  color: var(--ai);
  background: var(--surface);
  border-radius: 50%;
  padding: 3px;
  box-shadow: 0 0 0 1px var(--border);
}
.brand-text {
  min-width: 0;
}
.brand-text strong {
  display: block;
  font-size: 1rem;
}
.brand-text small {
  display: block;
  font-size: 0.72rem;
  line-height: 1.3;
  color: var(--faint);
}
.menu-toggle {
  display: none;
  margin-inline-start: auto;
}

.nav-wrap {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 0;
}
.new-btn {
  width: 100%;
  margin-bottom: 18px;
}

.nav {
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.nav-item {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 9px 10px;
  border-radius: var(--radius-sm);
  text-decoration: none;
  color: var(--muted);
  font-weight: 500;
  transition: background 0.12s, color 0.12s;
}
.nav-item .pi {
  font-size: 1rem;
  width: 18px;
  text-align: center;
}
.nav-item:hover {
  background: var(--surface-2);
  color: var(--text);
}
.nav-item.active {
  background: var(--brand-soft);
  color: var(--brand);
  font-weight: 600;
}
.count {
  min-width: 22px;
  height: 20px;
  padding: 0 7px;
  border-radius: 10px;
  display: inline-grid;
  place-items: center;
  font-size: 0.72rem;
  font-weight: 600;
  background: var(--surface-2);
  color: var(--muted);
  border: 1px solid var(--border);
}
.count.warn {
  background: var(--high-soft);
  color: var(--high);
  border-color: transparent;
}
.count.new {
  background: var(--normal-soft);
  color: var(--normal);
  border-color: transparent;
}
.nav-section {
  font-size: 0.7rem;
  font-weight: 600;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--faint);
  margin: 20px 10px 6px;
}
[dir='rtl'] .nav-section {
  letter-spacing: 0;
  font-size: 0.78rem;
}
.departments {
  overflow-y: auto;
  min-height: 0;
}
.nav-item.dept {
  padding: 6px 10px;
  font-size: 0.88rem;
}
.dept-dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: var(--border-strong);
  flex: none;
  margin-inline: 5px;
}
.dept-dot.alert {
  background: var(--urgent);
}
.nav-item.active .dept-dot {
  background: var(--brand);
}
.ellipsis {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  min-width: 0;
}

.sidebar-foot {
  margin-top: auto;
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding-top: 16px;
  border-top: 1px solid var(--border);
}
.user {
  display: flex;
  align-items: center;
  gap: 10px;
  min-width: 0;
}
.user .avatar {
  width: 32px;
  height: 32px;
  flex: none;
  border-radius: 50%;
  display: grid;
  place-items: center;
  font-weight: 700;
  font-size: 0.85rem;
  background: var(--brand-soft);
  color: var(--brand);
}
.user-text {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  line-height: 1.25;
}
.user-text strong {
  font-size: 0.86rem;
}
.user-text small {
  font-size: 0.72rem;
  color: var(--faint);
}
.toggles {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 6px;
}
.seg {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  height: 32px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface);
  color: var(--muted);
  font: inherit;
  font-size: 0.8rem;
  cursor: pointer;
}
.seg:hover {
  color: var(--text);
  border-color: var(--border-strong);
}
.live {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  font-size: 0.75rem;
  color: var(--faint);
  padding: 0 4px;
}
.pulse {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--faint);
}
.live.on .pulse {
  background: var(--ok);
  box-shadow: 0 0 0 0 var(--ok);
  animation: pulse 2s infinite;
}
@keyframes pulse {
  0% { box-shadow: 0 0 0 0 rgb(21 128 61 / 0.45); }
  70% { box-shadow: 0 0 0 6px rgb(21 128 61 / 0); }
  100% { box-shadow: 0 0 0 0 rgb(21 128 61 / 0); }
}

.content {
  padding: 28px 32px 48px;
  max-width: 1360px;
  width: 100%;
  min-width: 0;
}

.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.12s ease;
}
.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}

/* Mobile: sidebar becomes a top bar with a collapsible menu */
@media (max-width: 900px) {
  .shell {
    grid-template-columns: minmax(0, 1fr);
  }
  .sidebar {
    position: sticky;
    height: auto;
    z-index: 20;
    padding: 10px 16px;
    border-inline-end: 0;
    border-bottom: 1px solid var(--border);
  }
  .brand {
    padding: 0;
  }
  .menu-toggle {
    display: inline-flex;
  }
  .nav-wrap {
    display: none;
    padding-top: 14px;
  }
  .sidebar.open .nav-wrap {
    display: flex;
  }
  .content {
    padding: 20px 16px 40px;
  }
}
</style>
