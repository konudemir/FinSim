import { useState } from 'react'
import { useAuth } from './auth'

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
      setNote('Parolalar eşleşmiyor.')
      return
    }

    setBusy(true)
    try {
      await resetPassword(email, token, password)
      setNoteOk(true)
      setNote('Parolan güncellendi. Girişe yönlendiriliyorsun…')
      setTimeout(onDone, 1500)
    } catch (err: any) {
      const d = err.response?.data
      setNoteOk(false)
      setNote(
        typeof d === 'string' ? d
        : Array.isArray(d) ? d.join(' ')
        : 'Bağlantı geçersiz veya süresi dolmuş.'
      )
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="gate">
      <div className="gate-card">
        <div className="gate-mark">Fin<em>Sim</em></div>
        <div className="gate-tag">Parola Sıfırlama</div>

        <form onSubmit={submit}>
          {note && <div className={`gate-note${noteOk ? ' ok' : ''}`}>{note}</div>}

          <div style={{ fontSize: 12, color: 'var(--mute)' }}>
            <span style={{ color: 'var(--faint)' }}>Hesap: </span>
            {email}
          </div>

          <div>
            <label className="field-label" htmlFor="np">Yeni parola</label>
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
              En az 8 karakter, bir büyük harf, bir rakam ve bir sembol.
            </div>
          </div>

          <div>
            <label className="field-label" htmlFor="np2">Yeni parola (tekrar)</label>
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
            {busy ? '···' : 'Parolayı güncelle'}
          </button>

          <button className="gate-switch" type="button" onClick={onDone}>
            Girişe dön
          </button>
        </form>
      </div>
    </div>
  )
}