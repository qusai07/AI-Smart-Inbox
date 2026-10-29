<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import type { DocumentEvent } from '../api'
import { formatDateTime } from './Badges'
import { useDepartmentName } from '../stores/departments'

defineProps<{ events: DocumentEvent[] }>()

const { t, locale } = useI18n()
const departmentName = useDepartmentName()

const icons: Record<DocumentEvent['type'], string> = {
  Uploaded: 'pi pi-upload',
  Analyzed: 'pi pi-sparkles',
  AnalysisFailed: 'pi pi-exclamation-triangle',
  Routed: 'pi pi-send',
  Acknowledged: 'pi pi-check',
  Closed: 'pi pi-lock',
  Reopened: 'pi pi-replay',
  EmailSent: 'pi pi-envelope',
  EmailFailed: 'pi pi-envelope',
  Escalated: 'pi pi-angle-double-up',
  Assigned: 'pi pi-user',
}

function actor(e: DocumentEvent) {
  if (!e.actor || e.actor === 'system') return t('timeline.system')
  if (e.actor.startsWith('rules')) return t('engine.rules')
  return e.actor
}
</script>

<template>
  <ol class="timeline">
    <li v-for="(e, i) in events" :key="i" :class="`ev-${e.type}`">
      <span class="dot"><i :class="icons[e.type]" /></span>
      <div class="body">
        <div class="line">
          <strong>{{ t(`timeline.${e.type}`, { dept: departmentName(e.departmentId) }) }}</strong>
          <time class="faint">{{ formatDateTime(e.at, locale) }}</time>
        </div>
        <div class="who muted">{{ actor(e) }}</div>
        <p v-if="e.note" class="note" :class="{ ltr: e.type === 'EmailSent' || e.type === 'Analyzed' || e.type === 'Escalated' }" dir="auto">{{ e.note }}</p>
      </div>
    </li>
  </ol>
</template>

<style scoped>
.timeline {
  list-style: none;
  margin: 0;
  padding: 0;
}
.timeline li {
  position: relative;
  display: flex;
  gap: 12px;
  padding-bottom: 16px;
}
.timeline li:last-child {
  padding-bottom: 0;
}
/* Connecting line between dots */
.timeline li:not(:last-child)::before {
  content: '';
  position: absolute;
  top: 28px;
  bottom: 0;
  inset-inline-start: 13px;
  width: 2px;
  background: var(--border);
}
.dot {
  width: 28px;
  height: 28px;
  flex: none;
  border-radius: 50%;
  display: grid;
  place-items: center;
  font-size: 0.75rem;
  background: var(--surface-2);
  border: 1px solid var(--border);
  color: var(--muted);
}
.ev-Analyzed .dot { background: var(--ai-soft); color: var(--ai); border-color: transparent; }
.ev-Routed .dot { background: var(--brand-soft); color: var(--brand); border-color: transparent; }
.ev-Acknowledged .dot { background: var(--normal-soft); color: var(--normal); border-color: transparent; }
.ev-Closed .dot { background: var(--ok-soft); color: var(--ok); border-color: transparent; }
.ev-AnalysisFailed .dot,
.ev-EmailFailed .dot,
.ev-Escalated .dot { background: var(--urgent-soft); color: var(--urgent); border-color: transparent; }
.body {
  flex: 1;
  min-width: 0;
  padding-top: 3px;
}
.line {
  display: flex;
  justify-content: space-between;
  gap: 10px;
  flex-wrap: wrap;
  font-size: 0.9rem;
}
time {
  font-size: 0.75rem;
  white-space: nowrap;
}
.who {
  font-size: 0.8rem;
}
.note {
  margin: 6px 0 0;
  padding: 6px 10px;
  border-radius: 6px;
  background: var(--surface-2);
  font-size: 0.85rem;
  overflow-wrap: anywhere;
}
</style>
