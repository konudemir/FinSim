import { useState } from 'react'
import { useAuth } from './auth'
import { useLang } from './lang'
import GateLayout from './Gate'
import { IconArrowRight, IconEye, IconEyeOff, IconLock, IconMail, IconSpinner, IconUser } from './icons'

type Mode = 'login' | 'register' | 'forgot'

export default function Login({ onSuccess }: { onSuccess: () => void }) {
  const { login, register, forgotPassword } = useAuth()
  const { t, tServer } = useLang()
  const [mode, setMode] = useState<Mode>('login')
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [showPw, setShowPw] = useState(false)
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

  const title =
    mode === 'login' ? t('gate.login')
    : mode === 'register' ? t('gate.register')
    : t('gate.toForgot')

  return (
    <GateLayout>
      <div className="gate-card">
        <div className="gate-card-head">
          {mode === 'forgot' ? (
            <button className="gate-back" type="button" onClick={() => go('login')}>
              <IconArrowRight size={13} /> {t('gate.toLogin')}
            </button>
          ) : (
            <div className="gate-tabs" role="tablist">
              <button type="button" role="tab" aria-selected={mode === 'login'} onClick={() => go('login')}>
                {t('gate.login')}
              </button>
              <button type="button" role="tab" aria-selected={mode === 'register'} onClick={() => go('register')}>
                {t('gate.register')}
              </button>
            </div>
          )}

          <h2 className="gate-card-title">{title}</h2>
          {mode === 'forgot' && <p className="gate-card-hint">{t('gate.forgotHint')}</p>}
        </div>

        <form onSubmit={submit}>
          {note && <div className={`gate-note${noteOk ? ' ok' : ''}`}>{note}</div>}

          {showCredentials && (
            <div>
              <label className="field-label" htmlFor="u">{t('gate.username')}</label>
              <div className="field-shell">
                <span className="field-icon"><IconUser /></span>
                <input
                  id="u"
                  className="field-input has-icon"
                  value={username}
                  onChange={e => setUsername(e.target.value)}
                  autoComplete="username"
                  required
                />
              </div>
            </div>
          )}

          {showCredentials && (
            <div>
              <label className="field-label" htmlFor="p">{t('gate.password')}</label>
              <div className="field-shell">
                <span className="field-icon"><IconLock /></span>
                <input
                  id="p"
                  className="field-input has-icon has-eye"
                  type={showPw ? 'text' : 'password'}
                  value={password}
                  onChange={e => setPassword(e.target.value)}
                  autoComplete={mode === 'login' ? 'current-password' : 'new-password'}
                  required
                />
                <button
                  type="button"
                  className="field-eye"
                  onClick={() => setShowPw(v => !v)}
                  aria-label={showPw ? t('gate.hidePassword') : t('gate.showPassword')}
                >
                  {showPw ? <IconEyeOff /> : <IconEye />}
                </button>
              </div>
              {mode === 'register' && (
                <div className="gate-card-hint" style={{ marginTop: 6 }}>
                  {t('gate.pwHint')}
                </div>
              )}
            </div>
          )}

          {showEmail && (
            <div>
              <label className="field-label" htmlFor="e">{t('gate.email')}</label>
              <div className="field-shell">
                <span className="field-icon"><IconMail /></span>
                <input
                  id="e"
                  className="field-input has-icon"
                  type="email"
                  value={email}
                  onChange={e => setEmail(e.target.value)}
                  autoComplete="email"
                  required
                />
              </div>
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
            {busy ? <IconSpinner /> : <>{submitLabel}<IconArrowRight className="arrow" /></>}
          </button>

          {mode === 'login' && (
            <div className="gate-links">
              <button className="gate-switch" type="button" onClick={() => go('forgot')}>
                {t('gate.toForgot')}
              </button>
            </div>
          )}
        </form>
      </div>
    </GateLayout>
  )
}
