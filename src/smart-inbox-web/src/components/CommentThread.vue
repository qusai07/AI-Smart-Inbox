<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import Textarea from 'primevue/textarea'
import { api, type Comment } from '../api'
import { formatDateTime } from './Badges'
import { useAuth } from '../stores/auth'

const props = defineProps<{ documentId: string; comments: Comment[] }>()
const emit = defineEmits<{ added: [] }>()

const { t, locale } = useI18n()
const toast = useToast()
const auth = useAuth()
const text = ref('')
const busy = ref(false)
const MAX = 2000

async function send() {
  if (!text.value.trim()) return
  busy.value = true
  try {
    await api.comment(props.documentId, text.value)
    text.value = ''
    emit('added')
  } catch (e) {
    toast.add({ severity: 'error', summary: t('errors.generic'), detail: (e as Error).message, life: 5000 })
  } finally {
    busy.value = false
  }
}

const initial = (name: string) => name.trim().charAt(0).toUpperCase()
</script>

<template>
  <ol v-if="comments.length" class="thread">
    <li v-for="c in comments" :key="c.id" :class="{ mine: c.authorId === auth.me?.id }">
      <span class="avatar">{{ initial(c.authorName) }}</span>
      <div class="bubble">
        <div class="meta">
          <strong>{{ c.authorId === auth.me?.id ? t('work.me') : c.authorName }}</strong>
          <time class="faint">{{ formatDateTime(c.createdAt, locale) }}</time>
        </div>
        <p dir="auto">{{ c.text }}</p>
      </div>
    </li>
  </ol>
  <p v-else class="muted small empty">{{ t('work.noComments') }}</p>

  <form class="composer" @submit.prevent="send">
    <Textarea
      v-model="text"
      :placeholder="t('work.commentPlaceholder')"
      rows="2"
      auto-resize
      fluid
      dir="auto"
      :maxlength="MAX"
      @keydown.ctrl.enter="send"
      @keydown.meta.enter="send"
    />
    <div class="row">
      <small class="faint">{{ text.length }}/{{ MAX }} · Ctrl+Enter</small>
      <span class="grow" />
      <Button type="submit" :label="t('work.send')" icon="pi pi-send" size="small" :loading="busy" :disabled="!text.trim()" />
    </div>
  </form>
</template>

<style scoped>
.thread {
  list-style: none;
  margin: 0 0 14px;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.thread li {
  display: flex;
  gap: 10px;
  align-items: flex-start;
}
.avatar {
  width: 30px;
  height: 30px;
  flex: none;
  border-radius: 50%;
  display: grid;
  place-items: center;
  font-size: 0.8rem;
  font-weight: 700;
  background: var(--surface-2);
  color: var(--muted);
  border: 1px solid var(--border);
}
.mine .avatar {
  background: var(--brand-soft);
  color: var(--brand);
  border-color: transparent;
}
.bubble {
  flex: 1;
  min-width: 0;
  background: var(--surface-2);
  border: 1px solid var(--border);
  border-radius: 10px;
  padding: 8px 12px;
}
.mine .bubble {
  background: var(--brand-soft);
  border-color: transparent;
}
.meta {
  display: flex;
  justify-content: space-between;
  gap: 10px;
  font-size: 0.8rem;
}
.meta time {
  font-size: 0.72rem;
  white-space: nowrap;
}
.bubble p {
  margin: 4px 0 0;
  font-size: 0.92rem;
  line-height: 1.6;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
.empty {
  margin: 0 0 12px;
}
.composer {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.small {
  font-size: 0.86rem;
}
</style>
