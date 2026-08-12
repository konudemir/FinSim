import { useState } from 'react'
import { useAuth } from './auth'

export default function Login({ onSuccess }: { onSuccess: () => void }) {
  const { login, register } = useAuth()
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [email, setEmail] = useState('')
  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError('')
    setBusy(true)
    try {
      if (mode === 'login') {
        await login(username, password)
        onSuccess()
      } else {
        await register(username, password, email, firstName, lastName)
        setMode('login')
        setError('Hesap oluşturuldu, şimdi giriş yapabilirsin.')
      }
    } catch (err: any) {
      setError(err.response?.data ?? 'Bir hata oluştu')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="min-h-screen bg-slate-900 text-slate-100 flex items-center justify-center p-4">
      <form onSubmit={submit} className="bg-slate-800 rounded p-6 w-full max-w-sm">
        <h1 className="text-xl font-bold mb-4">
          {mode === 'login' ? 'Giriş Yap' : 'Hesap Oluştur'}
        </h1>

        {error && (
          <div className="text-sm text-red-400 bg-red-950 rounded p-2 mb-3">{error}</div>
        )}

        <label className="block text-sm mb-1">Kullanıcı adı</label>
        <input
          value={username}
          onChange={(e) => setUsername(e.target.value)}
          className="w-full bg-slate-700 px-2 py-1 rounded mb-3"
          required
        />

        <label className="block text-sm mb-1">Şifre</label>
        <input
          type="password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          className="w-full bg-slate-700 px-2 py-1 rounded mb-3"
          required
          minLength={mode === 'register' ? 8 : undefined}
        />

        {mode === 'register' && (
          <>
            <label className="block text-sm mb-1">E-posta</label>
            <input
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              className="w-full bg-slate-700 px-2 py-1 rounded mb-3"
              required
            />
            <label className="block text-sm mb-1">Ad</label>
            <input
              value={firstName}
              onChange={(e) => setFirstName(e.target.value)}
              className="w-full bg-slate-700 px-2 py-1 rounded mb-3"
              required
            />
            <label className="block text-sm mb-1">Soyad</label>
            <input
              value={lastName}
              onChange={(e) => setLastName(e.target.value)}
              className="w-full bg-slate-700 px-2 py-1 rounded mb-3"
              required
            />
          </>
        )}

        <button
          type="submit"
          disabled={busy}
          className="w-full bg-green-600 hover:bg-green-500 disabled:bg-slate-700 rounded py-2 mt-2"
        >
          {busy ? '...' : mode === 'login' ? 'Giriş Yap' : 'Kaydol'}
        </button>

        <button
          type="button"
          onClick={() => { setMode(mode === 'login' ? 'register' : 'login'); setError('') }}
          className="w-full text-sm text-slate-400 hover:text-slate-200 mt-3"
        >
          {mode === 'login' ? 'Hesabın yok mu? Kaydol' : 'Zaten hesabın var mı? Giriş yap'}
        </button>
      </form>
    </div>
  )
}