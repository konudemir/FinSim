import { useState, useCallback } from 'react'
import api from './api'

type AuthResponse = { token: string; expiry: string }

export function isLoggedIn(): boolean {
  const token = localStorage.getItem('finsim_token')
  const expiry = localStorage.getItem('finsim_expiry')
  if (!token || !expiry) return false
  return new Date(expiry) > new Date()
}

export function useAuth() {
  const [loggedIn, setLoggedIn] = useState(isLoggedIn())

  const login = useCallback(async (username: string, password: string) => {
    const res = await api.post<AuthResponse>('/api/auth/login', { username, password })
    localStorage.setItem('finsim_token', res.data.token)
    localStorage.setItem('finsim_expiry', res.data.expiry)
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

  const logout = useCallback(() => {
    localStorage.removeItem('finsim_token')
    localStorage.removeItem('finsim_expiry')
    setLoggedIn(false)
  }, [])

  return { loggedIn, login, register, forgotPassword, resetPassword, logout }
}