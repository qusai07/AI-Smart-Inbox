import { ref } from 'vue'

export type Theme = 'light' | 'dark'

function initialTheme(): Theme {
  try {
    const saved = localStorage.getItem('theme')
    if (saved === 'light' || saved === 'dark') return saved
  } catch {
    /* storage unavailable */
  }
  return window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
}

export const theme = ref<Theme>(initialTheme())

export function applyTheme(value: Theme) {
  theme.value = value
  document.documentElement.classList.toggle('app-dark', value === 'dark')
  try {
    localStorage.setItem('theme', value)
  } catch {
    /* storage unavailable */
  }
}
