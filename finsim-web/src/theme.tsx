import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'

export type Theme = 'night' | 'day'

const KEY = 'finsim_theme'

function initial(): Theme {
  const saved = localStorage.getItem(KEY)
  if (saved === 'night' || saved === 'day') return saved
  return window.matchMedia('(prefers-color-scheme: light)').matches ? 'day' : 'night'
}

type ThemeValue = {
  theme: Theme
  toggle: () => void
}

const ThemeContext = createContext<ThemeValue | null>(null)

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setTheme] = useState<Theme>(initial)

  useEffect(() => {
    document.documentElement.dataset.theme = theme
    localStorage.setItem(KEY, theme)
  }, [theme])

  const toggle = () => setTheme(t => (t === 'night' ? 'day' : 'night'))

  return (
    <ThemeContext.Provider value={{ theme, toggle }}>
      {children}
    </ThemeContext.Provider>
  )
}

export function useTheme(): ThemeValue {
  const ctx = useContext(ThemeContext)
  if (!ctx) throw new Error('useTheme must be used inside <ThemeProvider>')
  return ctx
}