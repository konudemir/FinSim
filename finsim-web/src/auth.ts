import { useState, useCallback, useEffect } from 'react'
import api from './api'

export function useAuth() {
  const [loggedIn, setLoggedIn] = useState(false)
  const [checking, setChecking] = useState(true)

  // on mount, ask the server whether the cookie is still good
  useEffect(() => {
    let cancelled = false
    api.get('/api/auth/me')
      .then(() => { if (!cancelled) setLoggedIn(true) })
      .catch(() => { if (!cancelled) setLoggedIn(false) })
      .finally(() => { if (!cancelled) setChecking(false) })
    return () => { cancelled = true }
  }, [])

  const login = useCallback(async (username: string, password: string) => {
    await api.post('/api/auth/login', { username, password })
    setLoggedIn(true)
  }, [])

  const register = useCallback(
    async (username: string, password: string, email: string, firstName: string, lastName: string) => {
      await api.post('/api/auth/register', { username, password, email, firstName, lastName })
    },
    []
  )

  const forgotPassword = useCallback(async (email: string) => {
    await api.post('/api/auth/forgot-password', { email })
  }, [])

  const resetPassword = useCallback(
    async (email: string, token: string, password: string) => {
      await api.post('/api/auth/reset-password', { email, token, password })
    },
    []
  )

  const logout = useCallback(async () => {
    await api.post('/api/auth/logout')
    setLoggedIn(false)
  }, [])

  return { loggedIn, checking, login, register, forgotPassword, resetPassword, logout }
}