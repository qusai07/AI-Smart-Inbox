<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import Button from 'primevue/button'
import { useAuth } from '../stores/auth'

defineEmits<{ upload: [] }>()

const { t } = useI18n()
const auth = useAuth()
const router = useRouter()

async function signOut() {
  await auth.logout()
  router.replace({ name: 'login' })
}
</script>

<template>
  <section class="card pending">
    <span class="icon"><i class="pi pi-user-plus" /></span>
    <h1>{{ t('auth.pendingTitle') }}</h1>
    <p class="muted">{{ t('auth.pendingText') }}</p>
    <p class="faint ltr">{{ auth.me?.email }}</p>
    <Button :label="t('auth.signOut')" icon="pi pi-sign-out" severity="secondary" outlined @click="signOut" />
  </section>
</template>

<style scoped>
.pending {
  max-width: 520px;
  margin: 10vh auto 0;
  padding: 36px;
  text-align: center;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
}
.icon {
  width: 56px;
  height: 56px;
  border-radius: 50%;
  display: grid;
  place-items: center;
  background: var(--brand-soft);
  color: var(--brand);
  font-size: 1.4rem;
  margin-bottom: 8px;
}
h1 {
  font-size: 1.3rem;
}
p {
  margin: 0 0 8px;
  line-height: 1.6;
}
</style>
