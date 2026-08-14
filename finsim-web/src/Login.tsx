import { useState } from 'react'
import { useAuth } from './auth'
import { useLang } from './lang'
import { useTheme } from './theme'

type Mode = 'login' | 'register' | 'forgot'

export default function Login({ onSuccess }: { onSuccess: () => void }) {
  const { login, register, forgotPassword } = useAuth()
  const { lang, toggle: toggleLang, t, tServer } = useLang()
  const { theme, toggle: toggleTheme } = useTheme()
  const [mode, setMode] = useState<Mode>('login')
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [email, setEmail] = useState('')
  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [note, setNote] = useState('')
  const [noteOk, setNoteOk] = useState(false)
  const [busy, setBusy] = useState(false)

  const fail = (e: any) =>
    e.response ? tServer(e.response.data) : t('gate.noConnection')

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    setNote('')
    setBusy(true)
    try {
      if (mode === 'login') {
        await login(username, password)
        onSuccess()
      } else if (mode === 'register') {
        await register(username, password, email, firstName, lastName)
        setMode('login')
        setNoteOk(true)
        setNote(t('srv.AccountCreated'))
      } else {
        await forgotPassword(email)
        setNoteOk(true)
        setNote(t('srv.ResetLinkSent'))
      }
    } catch (err: any) {
      setNoteOk(false)
      setNote(fail(err))
    } finally {
      setBusy(false)
    }
  }

  const go = (next: Mode) => {
    setMode(next)
    setNote('')
  }

  const showCredentials = mode !== 'forgot'
  const showEmail = mode === 'register' || mode === 'forgot'

  const submitLabel =
    mode === 'login' ? t('gate.login')
    : mode === 'register' ? t('gate.register')
    : t('gate.sendReset')

  return (
    <div className="gate">
      <div className="gate-card">
        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginBottom: 14 }}>
          <button
            className="ghost-btn"
            type="button"
            onClick={toggleTheme}
            aria-label={theme === 'night' ? t('app.toDay') : t('app.toNight')}
          >
            {theme === 'night' ? '☀' : '☾'}
          </button>
          <button className="ghost-btn" type="button" onClick={toggleLang} aria-label="Language">
            {lang === 'tr' ? 'EN' : 'TR'}
          </button>
        </div>

        <div className="gate-mark">Fin<em>Sim</em></div>
        <div className="gate-tag">{t('gate.tag')}</div>

        <form onSubmit={submit}>
          {note && <div className={`gate-note${noteOk ? ' ok' : ''}`}>{note}</div>}

          {mode === 'forgot' && (
            <div style={{ fontSize: 12, color: 'var(--mute)', lineHeight: 1.5 }}>
              {t('gate.forgotHint')}
            </div>
          )}

          {showCredentials && (
            <div>
              <label className="field-label" htmlFor="u">{t('gate.username')}</label>
              <input
                id="u"
                className="field-input"
                value={username}
                onChange={e => setUsername(e.target.value)}
                autoComplete="username"
                required
              />
            </div>
          )}

          {showCredentials && (
            <div>
              <label className="field-label" htmlFor="p">{t('gate.password')}</label>
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
                  {t('gate.pwHint')}
                </div>
              )}
            </div>
          )}

          {showEmail && (
            <div>
              <label className="field-label" htmlFor="e">{t('gate.email')}</label>
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
          )}

          {mode === 'register' && (
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
              <div>
                <label className="field-label" htmlFor="fn">{t('gate.firstName')}</label>
                <input
                  id="fn"
                  className="field-input"
                  value={firstName}
                  onChange={e => setFirstName(e.target.value)}
                  required
                />
              </div>
              <div>
                <label className="field-label" htmlFor="ln">{t('gate.lastName')}</label>
                <input
                  id="ln"
                  className="field-input"
                  value={lastName}
                  onChange={e => setLastName(e.target.value)}
                  required
                />
              </div>
            </div>
          )}

          <button className="gate-submit" type="submit" disabled={busy}>
            {busy ? '···' : submitLabel}
          </button>

          {mode === 'login' && (
            <>
              <button className="gate-switch" type="button" onClick={() => go('register')}>
                {t('gate.toRegister')}
              </button>
              <button className="gate-switch" type="button" onClick={() => go('forgot')}>
                {t('gate.toForgot')}
              </button>
            </>
          )}

          {mode !== 'login' && (
            <button className="gate-switch" type="button" onClick={() => go('login')}>
              {t('gate.toLogin')}
            </button>
          )}
        </form>
      </div>
    </div>
  )
}