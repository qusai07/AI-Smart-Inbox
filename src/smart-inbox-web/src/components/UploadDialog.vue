<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import Dialog from 'primevue/dialog'
import Button from 'primevue/button'
import Textarea from 'primevue/textarea'
import Message from 'primevue/message'
import { api } from '../api'

const visible = defineModel<boolean>('visible', { required: true })
const emit = defineEmits<{ uploaded: [id: string] }>()

const { t } = useI18n()
const toast = useToast()
const router = useRouter()

const mode = ref<'file' | 'text'>('file')
const file = ref<File | null>(null)
const text = ref('')
const busy = ref(false)
const error = ref<string | null>(null)
const dragging = ref(false)
const input = ref<HTMLInputElement>()

const canSubmit = computed(() => (mode.value === 'file' ? !!file.value : text.value.trim().length > 0))
const fileIcon = computed(() => {
  const name = file.value?.name.toLowerCase() ?? ''
  if (name.endsWith('.pdf')) return 'pi pi-file-pdf'
  if (/\.(png|jpe?g)$/.test(name)) return 'pi pi-image'
  if (name.endsWith('.docx')) return 'pi pi-file-word'
  if (name.endsWith('.eml')) return 'pi pi-envelope'
  return 'pi pi-file'
})
const fileSize = computed(() => (file.value ? `${(file.value.size / 1024).toFixed(file.value.size < 10240 ? 1 : 0)} KB` : ''))

function pick(e: Event) {
  file.value = (e.target as HTMLInputElement).files?.[0] ?? null
}

function drop(e: DragEvent) {
  dragging.value = false
  file.value = e.dataTransfer?.files?.[0] ?? null
}

function reset() {
  file.value = null
  text.value = ''
  error.value = null
  if (input.value) input.value.value = ''
}

async function submit() {
  busy.value = true
  error.value = null
  try {
    const { id } = mode.value === 'file' ? await api.uploadFile(file.value!) : await api.uploadText(text.value)
    toast.add({ severity: 'success', summary: t('upload.success'), life: 3000 })
    emit('uploaded', id)
    reset()
    visible.value = false
    router.push({ name: 'document', params: { id } })
  } catch (e) {
    error.value = (e as Error).message
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <Dialog v-model:visible="visible" modal :style="{ width: 'min(560px, 94vw)' }" :draggable="false" @hide="reset">
    <template #header>
      <div class="head">
        <span class="head-icon"><i class="pi pi-sparkles" /></span>
        <div>
          <h2>{{ t('upload.title') }}</h2>
          <p class="muted">{{ t('upload.subtitle') }}</p>
        </div>
      </div>
    </template>

    <div class="modes" role="tablist">
      <button type="button" role="tab" :class="{ active: mode === 'file' }" :aria-selected="mode === 'file'" @click="mode = 'file'">
        <i class="pi pi-upload" />{{ t('upload.fileTab') }}
      </button>
      <button type="button" role="tab" :class="{ active: mode === 'text' }" :aria-selected="mode === 'text'" @click="mode = 'text'">
        <i class="pi pi-align-left" />{{ t('upload.textTab') }}
      </button>
    </div>

    <template v-if="mode === 'file'">
      <div v-if="file" class="picked">
        <i :class="fileIcon" class="picked-icon" />
        <div class="picked-text">
          <strong class="ltr">{{ file.name }}</strong>
          <small class="faint ltr">{{ fileSize }}</small>
        </div>
        <Button icon="pi pi-times" text rounded severity="secondary" :aria-label="t('upload.change')" @click="reset" />
      </div>
      <div
        v-else
        class="dropzone"
        :class="{ dragging }"
        role="button"
        tabindex="0"
        @click="input?.click()"
        @keydown.enter="input?.click()"
        @dragover.prevent="dragging = true"
        @dragleave="dragging = false"
        @drop.prevent="drop"
      >
        <span class="drop-icon"><i class="pi pi-cloud-upload" /></span>
        <strong>{{ t('upload.drop') }}</strong>
        <small class="faint">{{ t('upload.types') }}</small>
      </div>
      <input ref="input" type="file" hidden accept=".pdf,.docx,.jpg,.jpeg,.png,.eml,.txt" @change="pick" />
    </template>

    <Textarea v-else v-model="text" rows="10" fluid :placeholder="t('upload.textPlaceholder')" dir="auto" class="paste" />

    <Message v-if="error" severity="error" class="mt">{{ error }}</Message>

    <template #footer>
      <Button :label="t('actions.cancel')" severity="secondary" text @click="visible = false" />
      <Button :label="t('upload.title')" icon="pi pi-sparkles" :loading="busy" :disabled="!canSubmit" @click="submit" />
    </template>
  </Dialog>
</template>

<style scoped>
.head {
  display: flex;
  gap: 12px;
  align-items: flex-start;
}
.head h2 {
  font-size: 1.1rem;
}
.head p {
  margin: 2px 0 0;
  font-size: 0.85rem;
}
.head-icon {
  width: 36px;
  height: 36px;
  flex: none;
  border-radius: 10px;
  display: grid;
  place-items: center;
  background: var(--ai-soft);
  color: var(--ai);
}
.modes {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 4px;
  padding: 4px;
  border-radius: 10px;
  background: var(--surface-2);
  border: 1px solid var(--border);
  margin-bottom: 14px;
}
.modes button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  height: 34px;
  border: 0;
  border-radius: 7px;
  background: transparent;
  color: var(--muted);
  font: inherit;
  font-weight: 500;
  cursor: pointer;
}
.modes button.active {
  background: var(--surface);
  color: var(--text);
  box-shadow: var(--shadow);
  font-weight: 600;
}
.dropzone {
  border: 1.5px dashed var(--border-strong);
  border-radius: var(--radius);
  padding: 36px 16px;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
  cursor: pointer;
  text-align: center;
  transition: border-color 0.15s, background 0.15s;
}
.dropzone:hover,
.dropzone.dragging,
.dropzone:focus-visible {
  border-color: var(--brand);
  background: var(--brand-soft);
  outline: none;
}
.drop-icon {
  width: 48px;
  height: 48px;
  border-radius: 50%;
  display: grid;
  place-items: center;
  background: var(--brand-soft);
  color: var(--brand);
  font-size: 1.3rem;
  margin-bottom: 6px;
}
.picked {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 14px 16px;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  background: var(--surface-2);
}
.picked-icon {
  font-size: 1.6rem;
  color: var(--brand);
}
.picked-text {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
}
.picked-text strong {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.paste {
  line-height: 1.7;
}
.mt {
  margin-top: 12px;
}
</style>
