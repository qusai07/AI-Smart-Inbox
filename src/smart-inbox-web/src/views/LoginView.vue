<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import Message from 'primevue/message'
import { api, ApiError } from '../api'
import { applyLocale } from '../i18n'
import { useAuth } from '../stores/auth'

const { t, locale } = useI18n()
const route = useRoute()
const router = useRouter()
const auth = useAuth()

const email = ref('')
const password = ref('')
const busy = ref(false)
const error = ref<string | null>(
  route.query.error === 'entra' ? t('auth.entraError') : route.query.error === 'locked' ? t('auth.locked') : null,
)
const returnUrl = computed(() => (typeof route.query.returnUrl === 'string' && route.query.returnUrl.startsWith('/') ? route.query.returnUrl : null))
const isDev = import.meta.env.DEV

async function submit() {
  busy.value = true
  error.value = null
  try {
    await auth.login(email.value.trim(), password.value)
    router.replace(returnUrl.value ?? auth.home)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : t('auth.failed')
  } finally {
    busy.value = false
  }
}

function microsoft() {
  window.location.href = api.entraLoginUrl(returnUrl.value ?? '/')
}
</script>

<template>
  <div class="login">
    <section class="panel card">
      <div class="brand">
        <span class="mark"><i class="pi pi-envelope" /></span>
        <div>
          <strong>{{ t('app.title') }}</strong>
          <small class="faint">{{ t('app.subtitle') }}</small>
        </div>
        <button type="button" class="lang" @click="applyLocale(locale === 'ar' ? 'en' : 'ar')">
          {{ locale === 'ar' ? 'English' : 'العربية' }}
        </button>
      </div>

      <h1>{{ t('auth.title') }}</h1>
      <p class="muted">{{ t('auth.subtitle') }}</p>

      <Message v-if="error" severity="error" class="mb">{{ error }}</Message>

      <form class="form" @submit.prevent="submit">
        <label>
          <span>{{ t('auth.email') }}</span>
          <InputText v-model="email" type="email" autocomplete="username" required fluid dir="ltr" autofocus />
        </label>
        <label>
          <span>{{ t('auth.password') }}</span>
          <Password v-model="password" :feedback="false" toggle-mask autocomplete="current-password" required fluid input-class="ltr-input" />
        </label>
        <Button type="submit" :label="t('auth.signIn')" icon="pi pi-sign-in" :loading="busy" fluid />
      </form>

      <template v-if="auth.providers.entra">
        <div class="divider"><span>{{ t('auth.or') }}</span></div>
        <Button severity="secondary" outlined fluid class="ms" @click="microsoft">
          <svg width="16" height="16" viewBox="0 0 21 21" aria-hidden="true">
            <rect x="1" y="1" width="9" height="9" fill="#f25022" /><rect x="11" y="1" width="9" height="9" fill="#7fba00" />
            <rect x="1" y="11" width="9" height="9" fill="#00a4ef" /><rect x="11" y="11" width="9" height="9" fill="#ffb900" />
          </svg>
          {{ t('auth.microsoft') }}
        </Button>
      </template>

      <details v-if="isDev" class="demo">
        <summary>{{ t('auth.demo') }}</summary>
        <ul class="ltr">
          <li><code>admin@mds.demo</code> · Admin</li>
          <li><code>clerk@mds.demo</code> · Clerk</li>
          <li><code>legal.user@mds.demo</code> · Legal Affairs</li>
          <li><code>hr.user@mds.demo</code> · Human Resources</li>
          <li>Password: <code>Demo#2026</code></li>
        </ul>
      </details>
    </section>
  </div>
</template>

<style scoped>
.login {
  min-height: 100vh;
  display: grid;
  place-items: center;
  padding: 24px 16px;
  background: radial-gradient(circle at 20% 0%, var(--brand-soft), transparent 50%), var(--bg);
}
.panel {
  width: min(420px, 100%);
  padding: 28px;
}
.brand {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 28px;
}
.brand strong {
  display: block;
}
.brand small {
  display: block;
  font-size: 0.75rem;
}
.mark {
  width: 38px;
  height: 38px;
  border-radius: 10px;
  display: grid;
  place-items: center;
  background: linear-gradient(135deg, var(--brand), var(--brand-strong));
  color: var(--brand-contrast);
}
.lang {
  margin-inline-start: auto;
  border: 1px solid var(--border);
  background: var(--surface);
  color: var(--muted);
  border-radius: 8px;
  padding: 4px 10px;
  font: inherit;
  font-size: 0.8rem;
  cursor: pointer;
}
h1 {
  font-size: 1.4rem;
  margin-bottom: 4px;
}
.panel > p {
  margin: 0 0 20px;
}
.mb {
  margin-bottom: 14px;
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
:deep(.ltr-input) {
  direction: ltr;
}
.divider {
  display: flex;
  align-items: center;
  gap: 12px;
  margin: 18px 0;
  color: var(--faint);
  font-size: 0.8rem;
}
.divider::before,
.divider::after {
  content: '';
  flex: 1;
  height: 1px;
  background: var(--border);
}
.ms :deep(svg) {
  margin-inline-end: 8px;
}
.demo {
  margin-top: 22px;
  font-size: 0.82rem;
  color: var(--muted);
}
.demo summary {
  cursor: pointer;
}
.demo ul {
  margin: 8px 0 0;
  padding-inline-start: 18px;
  line-height: 1.8;
}
</style>
