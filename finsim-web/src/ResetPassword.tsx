import { useState } from 'react'
import { useAuth } from './auth'
import { useLang } from './lang'
import GateLayout from './Gate'
import { IconEye, IconEyeOff, IconLock, IconSpinner } from './icons'

export default function ResetPassword({
  email,
  token,
  onDone,
}: {
  email: string
  token: string
  onDone: () => void
}) {
  const { resetPassword } = useAuth()
  const { t, tServer } = useLang()
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [showPw, setShowPw] = useState(false)
  const [note, setNote] = useState('')
  const [noteOk, setNoteOk] = useState(false)
  const [busy, setBusy] = useState(false)

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    setNote('')

    if (password !== confirm) {
      setNoteOk(false)
      setNote(t('reset.mismatch'))
      return
    }

    setBusy(true)
    try {
      await resetPassword(email, token, password)
      setNoteOk(true)
      setNote(t('srv.PasswordUpdated'))
      setTimeout(onDone, 1500)
    } catch (err: any) {
      setNoteOk(false)
      setNote(err.response ? tServer(err.response.data) : t('reset.invalid'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <GateLayout>
      <div className="gate-card">
        <div className="gate-card-head">
          <h2 className="gate-card-title">{t('reset.tag')}</h2>
          <p className="gate-card-hint">
            <span style={{ color: 'var(--faint)' }}>{t('reset.account')} </span>
            {email}
          </p>
        </div>

        <form onSubmit={submit}>
          {note && <div className={`gate-note${noteOk ? ' ok' : ''}`}>{note}</div>}

          <div>
            <label className="field-label" htmlFor="np">{t('reset.newPassword')}</label>
            <div className="field-shell">
              <span className="field-icon"><IconLock /></span>
              <input
                id="np"
                className="field-input has-icon has-eye"
                type={showPw ? 'text' : 'password'}
                value={password}
                onChange={e => setPassword(e.target.value)}
                autoComplete="new-password"
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
            <div className="gate-card-hint" style={{ marginTop: 6 }}>
              {t('gate.pwHint')}
            </div>
          </div>

          <div>
            <label className="field-label" htmlFor="np2">{t('reset.newPasswordAgain')}</label>
            <div className="field-shell">
              <span className="field-icon"><IconLock /></span>
              <input
                id="np2"
                className="field-input has-icon"
                type={showPw ? 'text' : 'password'}
                value={confirm}
                onChange={e => setConfirm(e.target.value)}
                autoComplete="new-password"
                required
              />
            </div>
          </div>

          <button className="gate-submit" type="submit" disabled={busy}>
            {busy ? <IconSpinner /> : t('reset.submit')}
          </button>

          <div className="gate-links">
            <button className="gate-switch" type="button" onClick={onDone}>
              {t('gate.toLogin')}
            </button>
          </div>
        </form>
      </div>
    </GateLayout>
  )
}
