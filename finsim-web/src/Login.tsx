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
  const [note, setNote] = useState('')
  const [noteOk, setNoteOk] = useState(false)
  const [busy, setBusy] = useState(false)

  const fail = (e: any) => {
    const d = e.response?.data
    if (typeof d === 'string') return d
    if (Array.isArray(d)) return d.join(' ')
    return 'Bağlantı kurulamadı.'
  }

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    setNote('')
    setBusy(true)
    try {
      if (mode === 'login') {
        await login(username, password)
        onSuccess()
      } else {
        await register(username, password, email, firstName, lastName)
        setMode('login')
        setNoteOk(true)
        setNote('Hesap açıldı. Şimdi giriş yapabilirsin.')
      }
    } catch (err: any) {
      setNoteOk(false)
      setNote(fail(err))
    } finally {
      setBusy(false)
    }
  }

  const swap = () => {
    setMode(mode === 'login' ? 'register' : 'login')
    setNote('')
  }

  return (
    <div className="gate">
      <div className="gate-card">
        <div className="gate-mark">Fin<em>Sim</em></div>
        <div className="gate-tag">Financial Terminal</div>

        <form onSubmit={submit}>
          {note && <div className={`gate-note${noteOk ? ' ok' : ''}`}>{note}</div>}

          <div>
            <label className="field-label" htmlFor="u">Kullanıcı adı</label>
            <input
              id="u"
              className="field-input"
              value={username}
              onChange={e => setUsername(e.target.value)}
              autoComplete="username"
              required
            />
          </div>

          <div>
            <label className="field-label" htmlFor="p">Parola</label>
            <input
              id="p"
              className="field-input"
              type="password"
              value={password}
              onChange={e => setPassword(e.target.value)}
              autoComplete={mode === 'login' ? 'current-password' : 'new-password'}
              required
            />
            {mode === 'register' && (
              <div style={{ fontSize: 11, color: 'var(--faint)', marginTop: 6 }}>
                En az 8 karakter, bir büyük harf, bir rakam ve bir sembol.
              </div>
            )}
          </div>

          {mode === 'register' && (
            <>
              <div>
                <label className="field-label" htmlFor="e">E-posta</label>
                <input
                  id="e"
                  className="field-input"
                  type="email"
                  value={email}
                  onChange={e => setEmail(e.target.value)}
                  autoComplete="email"
                  required
                />
              </div>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
                <div>
                  <label className="field-label" htmlFor="fn">Ad</label>
                  <input
                    id="fn"
                    className="field-input"
                    value={firstName}
                    onChange={e => setFirstName(e.target.value)}
                    required
                  />
                </div>
                <div>
                  <label className="field-label" htmlFor="ln">Soyad</label>
                  <input
                    id="ln"
                    className="field-input"
                    value={lastName}
                    onChange={e => setLastName(e.target.value)}
                    required
                  />
                </div>
              </div>
            </>
          )}

          <button className="gate-submit" type="submit" disabled={busy}>
            {busy ? '···' : mode === 'login' ? 'Giriş yap' : 'Hesap aç'}
          </button>

          <button className="gate-switch" type="button" onClick={swap}>
            {mode === 'login' ? 'Hesabın yok mu? Hesap aç' : 'Zaten hesabın var mı? Giriş yap'}
          </button>
        </form>
      </div>
    </div>
  )
}