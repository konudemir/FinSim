import { useEffect, useState } from 'react'

export type Theme = 'night' | 'day'

const KEY = 'finsim_theme'

function initial(): Theme {
  const saved = localStorage.getItem(KEY)
  if (saved === 'night' || saved === 'day') return saved
  return window.matchMedia('(prefers-color-scheme: light)').matches ? 'day' : 'night'
}

export function useTheme() {
  const [theme, setTheme] = useState<Theme>(initial)

  useEffect(() => {
    document.documentElement.dataset.theme = theme
    localStorage.setItem(KEY, theme)
  }, [theme])

  const toggle = () => setTheme(t => (t === 'night' ? 'day' : 'night'))
  return { theme, toggle }
}