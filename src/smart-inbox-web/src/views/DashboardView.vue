<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import ConfidenceRing from '../components/ConfidenceRing.vue'
import type { CountByKey } from '../api'
import { useStats } from '../stores/stats'
import { useAuth } from '../stores/auth'
import type { RouteLocationRaw } from 'vue-router'

defineEmits<{ upload: [] }>()

const { t, locale } = useI18n()
const store = useStats()
const stats = computed(() => store.stats)

onMounted(store.load)

const pct = (v: number | null | undefined) => (v == null ? '—' : `${Math.round(v * 100)}%`)
const num = (v: number) => v.toLocaleString(locale.value === 'ar' ? 'ar-AE' : 'en-US')

const tiles = computed(() => {
  const s = stats.value
  if (!s) return []
  return [
    { icon: 'pi pi-inbox', label: t('dashboard.total'), value: num(s.totalDocuments), tone: 'brand' },
    { icon: 'pi pi-exclamation-circle', label: t('dashboard.needsReview'), value: num(s.needsReview), tone: 'warn' },
    { icon: 'pi pi-send', label: t('status.Analyzed'), value: num(store.countByStatus.Analyzed ?? 0), tone: 'brand' },
    { icon: 'pi pi-check-circle', label: t('dashboard.routed'), value: num(s.routed), tone: 'ok' },
    { icon: 'pi pi-gauge', label: t('dashboard.avgConfidence'), value: pct(s.averageConfidence), tone: 'ai' },
    { icon: 'pi pi-bolt', label: t('dashboard.tokens'), value: num(s.totalTokens), tone: 'neutral' },
  ]
})

const groups = computed(() => {
  const s = stats.value
  if (!s) return []
  return [
    { title: t('dashboard.byPriority'), items: s.byPriority, label: (k: string) => t(`priority.${k}`), color: (k: string) => `var(--${k.toLowerCase()})` },
    { title: t('dashboard.byCategory'), items: s.byCategory, label: (k: string) => t(`category.${k}`), color: () => 'var(--brand)' },
    { title: t('dashboard.byDepartment'), items: s.byDepartment, label: (k: string) => k, color: () => 'var(--ok)' },
    { title: t('dashboard.byStatus'), items: s.byStatus, label: (k: string) => t(`status.${k}`), color: () => 'var(--ai)' },
  ]
})

const auth = useAuth()

/** Each tile opens the list it counts. */
const followUp = computed(() => {
  const f = stats.value?.followUp
  if (!f) return []
  const tiles: { key: string; icon: string; tone: string; value: number; to: RouteLocationRaw }[] = [
    { key: 'awaitingApproval', icon: 'pi pi-pen-to-square', tone: 'brand', value: f.awaitingApproval, to: { name: 'inbox', query: { awaiting: 'true' } } },
    { key: 'possibleDuplicates', icon: 'pi pi-exclamation-triangle', tone: 'warn', value: f.possibleDuplicates, to: { name: 'inbox', query: { duplicates: 'true' } } },
    { key: 'outgoingNotSent', icon: 'pi pi-send', tone: 'ai', value: f.outgoingNotSent, to: { name: 'register', query: { status: 'Issued' } } },
    { key: 'outgoingThisYear', icon: 'pi pi-hashtag', tone: 'ok', value: f.outgoingThisYear, to: { name: 'register' } },
  ]
  if (auth.isAdmin) tiles.push({ key: 'mailFailed', icon: 'pi pi-envelope', tone: f.mailFailed ? 'warn' : 'neutral', value: f.mailFailed, to: { name: 'admin', hash: '#mailbox' } })
  return tiles
})

const max = (items: CountByKey[]) => Math.max(1, ...items.map((i) => i.count))
</script>

<template>
  <header class="page-head">
    <h1>{{ t('dashboard.title') }}</h1>
    <p class="muted">{{ t('dashboard.subtitle') }}</p>
  </header>

  <template v-if="stats">
    <div class="top">
      <!-- Headline: AI routing accuracy -->
      <section class="card hero">
        <ConfidenceRing :value="stats.routingAccuracy ?? 0" :size="112" :stroke="9" />
        <div>
          <span class="eyebrow"><i class="pi pi-sparkles ai" /> {{ t('dashboard.accuracy') }}</span>
          <div class="hero-value ltr">{{ pct(stats.routingAccuracy) }}</div>
          <p class="muted">
            {{ stats.decisions
              ? t('dashboard.accuracyHint', { accepted: stats.acceptedSuggestions, total: stats.decisions })
              : t('dashboard.accuracyEmpty') }}
          </p>
        </div>
      </section>

      <div class="tiles">
        <div v-for="tile in tiles" :key="tile.label" class="card tile">
          <span class="tile-icon" :class="tile.tone"><i :class="tile.icon" /></span>
          <span class="tile-value tabular">{{ tile.value }}</span>
          <span class="tile-label muted">{{ tile.label }}</span>
        </div>
      </div>
    </div>

    <section v-if="followUp.length" class="follow-up">
      <h2 class="eyebrow">{{ t('dashboard.followUp') }}</h2>
      <div class="tiles wide">
        <RouterLink v-for="tile in followUp" :key="tile.key" :to="tile.to" class="card tile link" :class="{ quiet: !tile.value }">
          <span class="tile-icon" :class="tile.tone"><i :class="tile.icon" /></span>
          <span class="tile-value tabular">{{ num(tile.value) }}</span>
          <span class="tile-label muted">{{ t(`dashboard.fu.${tile.key}`) }}</span>
        </RouterLink>
      </div>
    </section>

    <div v-if="stats.totalDocuments === 0" class="card card-pad empty muted">{{ t('dashboard.empty') }}</div>

    <div v-else class="groups">
      <section v-for="g in groups" :key="g.title" class="card card-pad">
        <h2 class="eyebrow">{{ g.title }}</h2>
        <p v-if="!g.items.length" class="faint">—</p>
        <div v-for="item in g.items" :key="item.key" class="bar-row">
          <span class="bar-label">{{ g.label(item.key) }}</span>
          <div class="bar">
            <div class="fill" :style="{ width: `${(item.count / max(g.items)) * 100}%`, background: g.color(item.key) }" />
          </div>
          <span class="bar-value tabular">{{ num(item.count) }}</span>
        </div>
      </section>
    </div>
  </template>

  <div v-else class="top">
    <span class="skeleton" style="height: 160px; border-radius: 12px" />
    <span class="skeleton" style="height: 160px; border-radius: 12px" />
  </div>
</template>

<style scoped>
.page-head {
  margin-bottom: 22px;
}
.page-head h1 {
  font-size: 1.6rem;
  font-weight: 700;
}
.page-head p {
  margin: 4px 0 0;
}

.top {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1.6fr);
  gap: 16px;
  margin-bottom: 16px;
}
.hero {
  display: flex;
  align-items: center;
  gap: 24px;
  padding: 24px;
  background: linear-gradient(135deg, var(--surface) 40%, var(--brand-soft));
}
.hero .eyebrow {
  display: inline-flex;
  gap: 6px;
  align-items: center;
  margin: 0;
}
.ai {
  color: var(--ai);
}
.hero-value {
  font-size: 2.6rem;
  font-weight: 800;
  letter-spacing: -0.02em;
  line-height: 1.1;
  margin: 4px 0;
}
.hero p {
  margin: 0;
  font-size: 0.88rem;
}

.tiles {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 12px;
}
@media (max-width: 560px) {
  .tiles {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}
.tile {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 16px 18px;
}
.tile-icon {
  width: 32px;
  height: 32px;
  border-radius: 9px;
  display: grid;
  place-items: center;
  margin-bottom: 10px;
  font-size: 0.95rem;
}
.tile-icon.brand { background: var(--brand-soft); color: var(--brand); }
.tile-icon.warn { background: var(--high-soft); color: var(--high); }
.tile-icon.ok { background: var(--ok-soft); color: var(--ok); }
.tile-icon.ai { background: var(--ai-soft); color: var(--ai); }
.tile-icon.neutral { background: var(--surface-2); color: var(--muted); border: 1px solid var(--border); }
.tile-value {
  font-size: 1.6rem;
  font-weight: 700;
  line-height: 1.2;
}
.tile-label {
  font-size: 0.82rem;
}

.groups {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(340px, 1fr));
  gap: 16px;
}
.bar-row {
  display: grid;
  grid-template-columns: minmax(90px, 150px) 1fr 40px;
  align-items: center;
  gap: 12px;
  margin-bottom: 10px;
  font-size: 0.9rem;
}
.bar-label {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.bar {
  height: 8px;
  background: var(--surface-2);
  border-radius: 4px;
  overflow: hidden;
}
.fill {
  height: 100%;
  border-radius: 4px;
  transition: width 0.5s ease;
}
.bar-value {
  text-align: end;
  font-weight: 600;
}
.empty {
  text-align: center;
  padding: 48px;
}

@media (max-width: 980px) {
  .top {
    grid-template-columns: 1fr;
  }
}
@media (max-width: 480px) {
  .hero {
    flex-direction: column;
    text-align: center;
  }
  .groups {
    grid-template-columns: 1fr;
  }
}
.follow-up {
  margin-bottom: 16px;
}
.follow-up h2 {
  margin: 0 0 8px;
}
.tiles.wide {
  grid-template-columns: repeat(auto-fit, minmax(170px, 1fr));
}
a.tile {
  color: inherit;
  text-decoration: none;
  transition: border-color 0.15s, transform 0.15s;
}
a.tile:hover {
  border-color: var(--brand);
  transform: translateY(-1px);
}
a.tile.quiet .tile-value {
  color: var(--faint);
}
</style>
