import { useState } from 'react'
import { useAuth } from './auth'
import { useLang } from './lang'

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
  const { lang, toggle: toggleLang, t } = useLang()
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
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
      setNote(t('reset.done'))
      setTimeout(onDone, 1500)
    } catch (err: any) {
      const d = err.response?.data
      setNoteOk(false)
      setNote(
        typeof d === 'string' ? d
        : Array.isArray(d) ? d.join(' ')
        : t('reset.invalid')
      )
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="gate">
      <div className="gate-card">
        <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: 12 }}>
          <button className="ghost-btn" type="button" onClick={toggleLang} aria-label="Language">
            {lang === 'tr' ? 'EN' : 'TR'}
          </button>
        </div>

        <div className="gate-mark">Fin<em>Sim</em></div>
        <div className="gate-tag">{t('reset.tag')}</div>

        <form onSubmit={submit}>
          {note && <div className={`gate-note${noteOk ? ' ok' : ''}`}>{note}</div>}

          <div style={{ fontSize: 12, color: 'var(--mute)' }}>
            <span style={{ color: 'var(--faint)' }}>{t('reset.account')} </span>
            {email}
          </div>

          <div>
            <label className="field-label" htmlFor="np">{t('reset.newPassword')}</label>
            <input
              id="np"
              className="field-input"
              type="password"
              value={password}
              onChange={e => setPassword(e.target.value)}
              autoComplete="new-password"
              required
            />
            <div style={{ fontSize: 11, color: 'var(--faint)', marginTop: 6 }}>
              {t('gate.pwHint')}
            </div>
          </div>

          <div>
            <label className="field-label" htmlFor="np2">{t('reset.newPasswordAgain')}</label>
            <input
              id="np2"
              className="field-input"
              type="password"
              value={confirm}
              onChange={e => setConfirm(e.target.value)}
              autoComplete="new-password"
              required
            />
          </div>

          <button className="gate-submit" type="submit" disabled={busy}>
            {busy ? '···' : t('reset.submit')}
          </button>

          <button className="gate-switch" type="button" onClick={onDone}>
            {t('gate.toLogin')}
          </button>
        </form>
      </div>
    </div>
  )
}
