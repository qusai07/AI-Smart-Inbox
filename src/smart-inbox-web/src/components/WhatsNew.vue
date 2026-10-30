<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRouter, type RouteLocationRaw } from 'vue-router'
import { useI18n } from 'vue-i18n'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import { useAuth } from '../stores/auth'

/** Bump when there's a new set of features to announce: everyone sees the window once more. */
const RELEASE = '2026-11'

const { t } = useI18n()
const auth = useAuth()
const router = useRouter()
const visible = ref(false)

const storageKey = computed(() => `smartinbox.whatsNew.${RELEASE}.${auth.me?.id ?? 'anon'}`)

function seen() {
  try {
    return localStorage.getItem(storageKey.value) === '1'
  } catch {
    return true // storage blocked: don't show it on every visit
  }
}

function markSeen() {
  try {
    localStorage.setItem(storageKey.value, '1')
  } catch {
    /* nothing to remember it in */
  }
}

// Once per user and release, shortly after sign-in.
watch(
  () => auth.me?.id,
  (id) => {
    if (id && auth.role && !seen()) setTimeout(() => (visible.value = true), 600)
  },
  { immediate: true },
)

type Item = { key: string; icon: string; tone?: string; to?: RouteLocationRaw }

const items = computed<Item[]>(() => {
  const list: Item[] = []
  if (auth.isAdmin) list.push({ key: 'mailbox', icon: 'pi pi-envelope', to: { name: 'admin', hash: '#mailbox' } })
  else if (auth.isDesk) list.push({ key: 'mailboxDesk', icon: 'pi pi-envelope', to: { name: 'inbox', query: { source: 'Email' } } })
  list.push({ key: 'replies', icon: 'pi pi-pen-to-square', to: auth.home })
  if (auth.isAdmin) {
    list.push(
      { key: 'claude', icon: 'pi pi-sparkles', tone: 'ai', to: { name: 'integrations' } },
      { key: 'keys', icon: 'pi pi-key', to: { name: 'integrations' } },
      { key: 'webhooks', icon: 'pi pi-bolt', to: { name: 'integrations' } },
      { key: 'sdk', icon: 'pi pi-code', to: { name: 'integrations' } },
      { key: 'signatures', icon: 'pi pi-id-card', to: { name: 'admin', hash: '#signatures' } },
    )
  } else {
    list.push({ key: 'claudeUser', icon: 'pi pi-sparkles', tone: 'ai' })
  }
  return list
})

function go(item: Item) {
  close()
  if (item.to) router.push(item.to)
}

function close() {
  visible.value = false
  markSeen()
}

defineExpose({ open: () => (visible.value = true) })
</script>

<template>
  <Dialog
    :visible="visible"
    modal
    :header="t('whatsNew.title')"
    :style="{ width: 'min(620px, 94vw)' }"
    :draggable="false"
    @update:visible="(v: boolean) => { if (!v) close() }"
  >
    <p class="intro muted">{{ t('whatsNew.intro') }}</p>
    <ul class="items">
      <li v-for="item in items" :key="item.key">
        <span class="icon" :class="item.tone"><i :class="item.icon" /></span>
        <div class="text">
          <strong>{{ t(`whatsNew.items.${item.key}.title`) }}</strong>
          <span class="muted">{{ t(`whatsNew.items.${item.key}.text`) }}</span>
        </div>
        <Button v-if="item.to" :label="t('whatsNew.show')" size="small" text @click="go(item)" />
      </li>
    </ul>
    <p class="free small faint"><i class="pi pi-check-circle" /> {{ t('whatsNew.free') }}</p>
    <template #footer>
      <Button :label="t('whatsNew.done')" icon="pi pi-check" @click="close" />
    </template>
  </Dialog>
</template>

<style scoped>
.intro {
  margin: 0 0 12px;
}
.items {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.items li {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 10px 4px;
  border-bottom: 1px solid var(--border);
}
.items li:last-child {
  border-bottom: 0;
}
.icon {
  flex: none;
  display: grid;
  place-items: center;
  width: 36px;
  height: 36px;
  border-radius: 10px;
  background: var(--brand-soft);
  color: var(--brand);
}
.icon.ai {
  background: var(--ai-soft);
  color: var(--ai);
}
.text {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
  font-size: 0.88rem;
}
.free {
  display: flex;
  align-items: center;
  gap: 6px;
  margin: 12px 0 0;
}
.small {
  font-size: 0.8rem;
}
</style>
