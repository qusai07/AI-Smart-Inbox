<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import { api, type ApiKeyRow, type WebhookEndpoint } from '../api'
import { formatDateTime } from '../components/Badges'

defineEmits<{ upload: [] }>()

const { t, locale } = useI18n()
const toast = useToast()

const DOCS = 'https://github.com/qusai07/AI-Smart-Inbox/blob/main/docs'
const origin = window.location.origin

const keys = ref<ApiKeyRow[] | null>(null)
const hooks = ref<WebhookEndpoint[] | null>(null)

onMounted(async () => {
  const [k, w] = await Promise.allSettled([api.apiKeys(), api.webhooks()])
  keys.value = k.status === 'fulfilled' ? k.value : []
  hooks.value = w.status === 'fulfilled' ? w.value : []
})

const activeKeys = computed(() => (keys.value ?? []).filter((k) => !k.revokedAt && !(k.expiresAt && new Date(k.expiresAt) <= new Date())))
const lastUsed = computed(() =>
  activeKeys.value.reduce<string | null>((latest, k) => (k.lastUsedAt && (!latest || k.lastUsedAt > latest) ? k.lastUsedAt : latest), null),
)
const enabledHooks = computed(() => (hooks.value ?? []).filter((h) => h.enabled))
const delivered7d = computed(() => (hooks.value ?? []).reduce((n, h) => n + h.deliveries7d, 0))
const successRate = computed(() => {
  const withData = (hooks.value ?? []).filter((h) => h.successRate7d !== null && h.deliveries7d > 0)
  if (!withData.length) return null
  const total = withData.reduce((n, h) => n + h.deliveries7d, 0)
  return withData.reduce((n, h) => n + h.successRate7d! * h.deliveries7d, 0) / total
})

const mcpInstall = 'dotnet tool install -g SmartInbox.Mcp'
const mcpAdd = computed(() => `claude mcp add smart-inbox -e SMARTINBOX_URL=${origin} -e SMARTINBOX_API_KEY=sik_... -- smartinbox-mcp`)
const sdkInstall = 'dotnet add package SmartInbox.Client'
const sdkSnippet = computed(() => `builder.Services.AddSmartInboxClient(o =>
{
    o.BaseUrl = new Uri("${origin}");
    o.ApiKey  = builder.Configuration["SmartInbox:ApiKey"];
});

var doc = await inbox.Documents.UploadAsync(File.OpenRead("letter.pdf"), "letter.pdf");`)

async function copy(text: string) {
  await navigator.clipboard.writeText(text)
  toast.add({ severity: 'success', summary: t('reply.copied'), life: 1500 })
}
</script>

<template>
  <header class="page-head">
    <h1>{{ t('integrations.title') }}</h1>
    <p class="muted">{{ t('integrations.subtitle') }}</p>
  </header>

  <div class="grid">
    <!-- API keys -->
    <section class="card card-pad tile">
      <div class="tile-head">
        <span class="icon"><i class="pi pi-key" /></span>
        <div>
          <h2>{{ t('integrations.keys.title') }}</h2>
          <p class="small muted">{{ t('integrations.keys.what') }}</p>
        </div>
      </div>
      <div class="status">
        <template v-if="keys">
          <span class="pill" :class="activeKeys.length ? 'ok' : 'neutral'">{{ t('integrations.keys.active', { n: activeKeys.length }) }}</span>
          <span v-if="lastUsed" class="small muted">{{ t('integrations.keys.lastUsed', { when: formatDateTime(lastUsed, locale) }) }}</span>
        </template>
        <i v-else class="pi pi-spin pi-spinner muted" />
      </div>
      <ul class="points small">
        <li>{{ t('integrations.keys.p1') }}</li>
        <li>{{ t('integrations.keys.p2') }}</li>
      </ul>
      <div class="foot">
        <RouterLink :to="{ name: 'admin', hash: '#api-keys' }"><Button :label="t('integrations.keys.manage')" icon="pi pi-arrow-right" icon-pos="right" size="small" /></RouterLink>
      </div>
    </section>

    <!-- Webhooks -->
    <section class="card card-pad tile">
      <div class="tile-head">
        <span class="icon"><i class="pi pi-bolt" /></span>
        <div>
          <h2>{{ t('integrations.hooks.title') }}</h2>
          <p class="small muted">{{ t('integrations.hooks.what') }}</p>
        </div>
      </div>
      <div class="status">
        <template v-if="hooks">
          <span class="pill" :class="enabledHooks.length ? 'ok' : 'neutral'">{{ t('integrations.hooks.endpoints', { n: enabledHooks.length }) }}</span>
          <span v-if="delivered7d" class="small muted">
            {{ t('integrations.hooks.delivered', { n: delivered7d }) }}<template v-if="successRate !== null"> · {{ Math.round(successRate * 100) }}%</template>
          </span>
        </template>
        <i v-else class="pi pi-spin pi-spinner muted" />
      </div>
      <ul class="points small">
        <li>{{ t('integrations.hooks.p1') }}</li>
        <li>{{ t('integrations.hooks.p2') }}</li>
      </ul>
      <div class="foot">
        <RouterLink :to="{ name: 'admin', hash: '#webhooks' }"><Button :label="t('integrations.hooks.manage')" icon="pi pi-arrow-right" icon-pos="right" size="small" /></RouterLink>
        <a :href="`${DOCS}/WEBHOOKS.md`" target="_blank" rel="noopener" class="doc-link">{{ t('integrations.docs') }} <i class="pi pi-external-link" /></a>
      </div>
    </section>

    <!-- Claude (MCP) -->
    <section class="card card-pad tile">
      <div class="tile-head">
        <span class="icon ai"><i class="pi pi-sparkles" /></span>
        <div>
          <h2>{{ t('integrations.mcp.title') }}</h2>
          <p class="small muted">{{ t('integrations.mcp.what') }}</p>
        </div>
      </div>
      <ul class="points small">
        <li>{{ t('integrations.mcp.p1') }}</li>
        <li>{{ t('integrations.mcp.p2') }}</li>
        <li>{{ t('integrations.mcp.p3') }}</li>
      </ul>
      <div class="steps small">
        <span class="step">1</span><span>{{ t('integrations.mcp.s1') }}</span>
      </div>
      <div class="steps small">
        <span class="step">2</span><span>{{ t('integrations.mcp.s2') }}</span>
      </div>
      <div class="code"><code class="ltr">{{ mcpInstall }}</code><Button icon="pi pi-copy" text size="small" :aria-label="t('admin.copy')" @click="copy(mcpInstall)" /></div>
      <div class="steps small">
        <span class="step">3</span><span>{{ t('integrations.mcp.s3') }}</span>
      </div>
      <div class="code"><code class="ltr">{{ mcpAdd }}</code><Button icon="pi pi-copy" text size="small" :aria-label="t('admin.copy')" @click="copy(mcpAdd)" /></div>
      <div class="foot">
        <a :href="`${DOCS}/MCP.md`" target="_blank" rel="noopener" class="doc-link">{{ t('integrations.docs') }} <i class="pi pi-external-link" /></a>
      </div>
    </section>

    <!-- .NET SDK -->
    <section class="card card-pad tile">
      <div class="tile-head">
        <span class="icon"><i class="pi pi-code" /></span>
        <div>
          <h2>{{ t('integrations.sdk.title') }}</h2>
          <p class="small muted">{{ t('integrations.sdk.what') }}</p>
        </div>
      </div>
      <ul class="points small">
        <li>{{ t('integrations.sdk.p1') }}</li>
        <li>{{ t('integrations.sdk.p2') }}</li>
      </ul>
      <div class="code"><code class="ltr">{{ sdkInstall }}</code><Button icon="pi pi-copy" text size="small" :aria-label="t('admin.copy')" @click="copy(sdkInstall)" /></div>
      <div class="code block"><pre class="ltr">{{ sdkSnippet }}</pre><Button icon="pi pi-copy" text size="small" :aria-label="t('admin.copy')" @click="copy(sdkSnippet)" /></div>
      <div class="foot">
        <a href="https://github.com/qusai07/AI-Smart-Inbox/blob/main/sdk/dotnet/README.md" target="_blank" rel="noopener" class="doc-link">{{ t('integrations.docs') }} <i class="pi pi-external-link" /></a>
      </div>
    </section>
  </div>

  <p class="small faint free"><i class="pi pi-check-circle" /> {{ t('integrations.free') }}</p>
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
.grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(360px, 1fr));
  gap: 16px;
}
@media (max-width: 640px) {
  .grid {
    grid-template-columns: 1fr;
  }
}
.tile {
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-width: 0;
}
.tile-head {
  display: flex;
  gap: 12px;
  align-items: flex-start;
}
.tile-head h2 {
  font-size: 1.05rem;
  margin: 0;
}
.tile-head p {
  margin: 2px 0 0;
}
.icon {
  flex: none;
  display: grid;
  place-items: center;
  width: 38px;
  height: 38px;
  border-radius: 10px;
  background: var(--brand-soft);
  color: var(--brand);
}
.icon.ai {
  background: var(--ai-soft);
  color: var(--ai);
}
.status {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
}
.pill.ok {
  background: var(--ok-soft);
  color: var(--ok);
}
.points {
  margin: 0;
  padding-inline-start: 18px;
  color: var(--muted);
}
.points li + li {
  margin-top: 2px;
}
.steps {
  display: flex;
  align-items: center;
  gap: 8px;
}
.step {
  display: grid;
  place-items: center;
  width: 20px;
  height: 20px;
  border-radius: 50%;
  background: var(--surface-2);
  border: 1px solid var(--border);
  font-size: 0.72rem;
  font-weight: 700;
}
.code {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px 6px 6px 10px;
  border-radius: 8px;
  background: var(--surface-2);
  border: 1px solid var(--border);
}
.code code,
.code pre {
  flex: 1;
  min-width: 0;
  margin: 0;
  font-size: 0.78rem;
  word-break: break-all;
  white-space: pre-wrap;
}
.code.block {
  align-items: flex-start;
}
.foot {
  margin-top: auto;
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
  padding-top: 4px;
}
.doc-link {
  font-size: 0.85rem;
}
.small {
  font-size: 0.82rem;
}
.free {
  margin-top: 16px;
  display: flex;
  align-items: center;
  gap: 6px;
}
</style>
