<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(defineProps<{ value: number; size?: number; stroke?: number }>(), { size: 44, stroke: 4 })

const pct = computed(() => Math.round(Math.min(1, Math.max(0, props.value)) * 100))
const r = computed(() => (props.size - props.stroke) / 2)
const circumference = computed(() => 2 * Math.PI * r.value)
const level = computed(() => (props.value >= 0.8 ? 'high' : props.value >= 0.6 ? 'mid' : 'low'))
</script>

<template>
  <span class="ring" :class="level" :style="{ width: `${size}px`, height: `${size}px` }" role="img" :aria-label="`${pct}%`">
    <svg :width="size" :height="size" :viewBox="`0 0 ${size} ${size}`">
      <circle :cx="size / 2" :cy="size / 2" :r="r" class="track" :stroke-width="stroke" />
      <circle
        :cx="size / 2"
        :cy="size / 2"
        :r="r"
        class="value"
        :stroke-width="stroke"
        :stroke-dasharray="circumference"
        :stroke-dashoffset="circumference * (1 - pct / 100)"
      />
    </svg>
    <span class="label ltr" :style="{ fontSize: `${Math.max(10, size * 0.26)}px` }">{{ pct }}<small>%</small></span>
  </span>
</template>

<style scoped>
.ring {
  position: relative;
  display: inline-grid;
  place-items: center;
  flex: none;
}
svg {
  position: absolute;
  inset: 0;
  transform: rotate(-90deg);
}
circle {
  fill: none;
}
.track {
  stroke: var(--border);
}
.value {
  stroke-linecap: round;
  transition: stroke-dashoffset 0.6s ease;
}
.high .value { stroke: var(--brand); }
.mid .value { stroke: var(--normal); }
.low .value { stroke: var(--high); }
.label {
  font-weight: 700;
  color: var(--text);
}
.label small {
  font-size: 0.7em;
  font-weight: 600;
  color: var(--muted);
}
</style>
