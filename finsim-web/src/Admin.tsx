import { useEffect, useState } from 'react'
import api from './api'
import { useLang, type LangKey } from './lang'
import { fmt } from './format'

type Instrument = {
  id: string
  symbol: string
  name: string
  basePrice: number
  currentPrice: number
  isActive: boolean
}

type LiquidationPreview = {
  affectedUsers: number
  totalShares: number
  price: number
}

type Holding = {
  symbol: string
  name: string
  totalQuantity: number
  lockedQuantity: number
  averageCost: number
  currentPrice: number
  marketValue: number
  profitLoss: number
}

type AdminUser = {
  id: string
  username: string
  email: string
  freeCashBalance: number
  lockedCashBalance: number
  realizedProfitLoss: number
  holdings: Holding[]
}

type BookLevel = { price: number; quantity: number; orderCount: number }
type OrderBook = {
  instrumentId: string; symbol: string; currentPrice: number
  bids: BookLevel[]; asks: BookLevel[]
}

export default function Admin({ onClose }: { onClose: () => void }) {
  const { t, tServer } = useLang()

  const [instruments, setInstruments] = useState<Instrument[]>([])
  const [users, setUsers] = useState<AdminUser[]>([])
  const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState(false)

  const [bookSymbol, setBookSymbol] = useState<string>('')
  const [book, setBook] = useState<OrderBook | null>(null)

  const [pendingDeactivate, setPendingDeactivate] =
    useState<{ instrument: Instrument; preview: LiquidationPreview } | null>(null)

  const [instrumentQuery, setInstrumentQuery] = useState('')
  const [instrumentSort, setInstrumentSort] = useState('symbol-asc')
  const [userQuery, setUserQuery] = useState('')
  const [userSort, setUserSort] = useState('name-asc')

  const [cashDelta, setCashDelta] = useState<Record<string, string>>({})
  const [cashReason, setCashReason] = useState<Record<string, string>>({})
  const [shareInstrument, setShareInstrument] = useState<Record<string, string>>({})
  const [shareQty, setShareQty] = useState<Record<string, string>>({})

  const loadInstruments = () =>
    api.get<Instrument[]>('/api/instruments').then(r => setInstruments(r.data)).catch(console.error)

  const loadUsers = () =>
    api.get<AdminUser[]>('/api/admin/users').then(r => setUsers(r.data)).catch(console.error)

  const reloadPrice = async (i: Instrument) => {
    setNotice(''); setBusy(true)
    try {
      const r = await api.post(`/api/admin/instruments/${i.id}/reload-price`)
      setNotice(`${i.symbol}: ${r.data.outcome} ${fmt(r.data.oldPrice)} → ${fmt(r.data.newPrice)}`)
      loadInstruments()
    } catch (e: any) { fail(e, 'err.orderFailed') } finally { setBusy(false) }
  }
  

  useEffect(() => { loadInstruments(); loadUsers() }, [])

  useEffect(() => {
    if (!bookSymbol) return
    const load = () => api.get<OrderBook>(`/api/admin/book/${bookSymbol}`)
      .then(r => setBook(r.data)).catch(console.error)
    load()
    const h = setInterval(load, 10000)
    return () => clearInterval(h)
  }, [bookSymbol])

  const fail = (e: any, fallback: LangKey) =>
    setNotice(e.response ? tServer(e.response.data) : t(fallback))

  const reactivate = async (i: Instrument) => {
    setNotice('')
    try {
      await api.put(`/api/instruments/${i.id}/active`, { isActive: true })
      setNotice(t('admin.reactivated'))
      loadInstruments()
    } catch (e: any) {
      fail(e, 'err.orderFailed')
    }
  }

  const requestDeactivate = async (i: Instrument) => {
    setNotice('')
    try {
      const r = await api.get<LiquidationPreview>(`/api/instruments/${i.id}/liquidation-preview`)
      setPendingDeactivate({ instrument: i, preview: r.data })
    } catch (e: any) {
      fail(e, 'err.orderFailed')
    }
  }

  const confirmDeactivate = async () => {
    if (!pendingDeactivate) return
    const { instrument } = pendingDeactivate
    setPendingDeactivate(null)
    setNotice('')
    try {
      await api.put(`/api/instruments/${instrument.id}/active`, { isActive: false })
      setNotice(t('admin.deactivated'))
      loadInstruments()
      loadUsers()
    } catch (e: any) {
      fail(e, 'err.orderFailed')
    }
  }

  const applyCash = async (userId: string) => {
    setNotice('')
    const delta = parseFloat((cashDelta[userId] ?? '').replace(',', '.'))
    if (!Number.isFinite(delta) || delta === 0) {
      setNotice(t('err.minPrice'))
      return
    }
    try {
      await api.post(`/api/admin/users/${userId}/cash`, { delta, reason: cashReason[userId] ?? '' })
      setNotice(t('admin.cashApplied'))
      setCashDelta(prev => ({ ...prev, [userId]: '' }))
      setCashReason(prev => ({ ...prev, [userId]: '' }))
      loadUsers()
    } catch (e: any) {
      fail(e, 'err.orderFailed')
    }
  }

  const applyShares = async (userId: string) => {
    setNotice('')
    const instrumentId = shareInstrument[userId]
    const qty = parseInt(shareQty[userId] ?? '', 10)
    if (!instrumentId || !Number.isFinite(qty) || qty === 0) {
      setNotice(t('err.minQty'))
      return
    }
    try {
      await api.post(`/api/admin/users/${userId}/shares`, { instrumentId, quantityDelta: qty })
      setNotice(t('admin.sharesApplied'))
      setShareQty(prev => ({ ...prev, [userId]: '' }))
      loadUsers()
    } catch (e: any) {
      fail(e, 'err.orderFailed')
    }
  }

  const visibleInstruments = (() => {
    const q = instrumentQuery.trim().toLocaleLowerCase('tr')
    const filtered = q
      ? instruments.filter(i =>
          i.symbol.toLocaleLowerCase('tr').includes(q) ||
          i.name.toLocaleLowerCase('tr').includes(q))
      : instruments
    const cmp: Record<string, (a: Instrument, b: Instrument) => number> = {
      'symbol-asc':  (a, b) => a.symbol.localeCompare(b.symbol, 'tr'),
      'symbol-desc': (a, b) => b.symbol.localeCompare(a.symbol, 'tr'),
      'price-asc':   (a, b) => a.currentPrice - b.currentPrice,
      'price-desc':  (a, b) => b.currentPrice - a.currentPrice,
    }
    return [...filtered].sort(cmp[instrumentSort])
  })()

  const visibleUsers = (() => {
    const q = userQuery.trim().toLocaleLowerCase('tr')
    const filtered = q
      ? users.filter(u =>
          u.username.toLocaleLowerCase('tr').includes(q) ||
          u.email.toLocaleLowerCase('tr').includes(q))
      : users
    const cmp: Record<string, (a: AdminUser, b: AdminUser) => number> = {
      'name-asc':  (a, b) => a.username.localeCompare(b.username, 'tr'),
      'name-desc': (a, b) => b.username.localeCompare(a.username, 'tr'),
    }
    return [...filtered].sort(cmp[userSort])
  })()

  return (
    <div className="admin">
      <div className="section-head">
        <h2>{t('admin.title')}</h2>
        <span className="rail-spacer" />
        <button className="ghost-btn" onClick={onClose}>{t('app.close')}</button>
      </div>

      {notice && (
        <div className="notice">
          <span style={{ flex: 1 }}>{notice}</span>
          <button onClick={() => setNotice('')} aria-label={t('app.close')}>×</button>
        </div>
      )}

      {pendingDeactivate && (
        <div className="modal-backdrop" onClick={() => setPendingDeactivate(null)}>
          <div className="modal" onClick={e => e.stopPropagation()}>
            <h3>{t('admin.confirmDeactivateTitle')}</h3>
            <p>
              {pendingDeactivate.preview.affectedUsers > 0
                ? t('admin.confirmDeactivateBody', {
                    symbol: pendingDeactivate.instrument.symbol,
                    users: pendingDeactivate.preview.affectedUsers,
                    shares: pendingDeactivate.preview.totalShares,
                    price: fmt(pendingDeactivate.preview.price)
                  })
                : t('admin.confirmDeactivateNoHoldings', { symbol: pendingDeactivate.instrument.symbol })}
            </p>
            <div className="modal-actions">
              <button className="ghost-btn" onClick={() => setPendingDeactivate(null)}>
                {t('admin.cancel')}
              </button>
              <button className="trade sell" onClick={confirmDeactivate}>{t('admin.confirm')}</button>
            </div>
          </div>
        </div>
      )}

      <div className="panel">
    <div className="section-head">
      <h3>{t('admin.instrumentsTitle')}</h3>
    </div>

    <div className="board-controls" style={{ marginBottom: 16 }}>
      <div className="search-input">
        <input
          className="field-input"
          type="text"
          value={instrumentQuery}
          onChange={e => setInstrumentQuery(e.target.value)}
          placeholder={t('search.placeholder')}
        />
        {instrumentQuery && (
          <button
            className="ghost-btn"
            onClick={() => setInstrumentQuery('')}
            aria-label={t('app.close')}
          >
            ×
          </button>
        )}
      </div>
      <select className="field-input" value={instrumentSort} onChange={e => setInstrumentSort(e.target.value)}>
        <option value="symbol-asc">{t('sort.symbolAsc')}</option>
        <option value="symbol-desc">{t('sort.symbolDesc')}</option>
        <option value="price-desc">{t('sort.priceDesc')}</option>
        <option value="price-asc">{t('sort.priceAsc')}</option>
      </select>
    </div>

    <table className="ledger">
      <thead>
        <tr>
          <th>{t('admin.symbol')}</th>
          <th>{t('admin.name')}</th>
          <th className="num">{t('ledger.price')}</th>
          <th>{t('admin.active')}</th>
          <th />
          <th />
        </tr>
      </thead>
      <tbody>
        {visibleInstruments.map(i => (
          <tr key={i.id}>
            <td className="sym">{i.symbol}</td>
            <td>{i.name}</td>
            <td className="num">{fmt(i.currentPrice)}</td>
            <td>{i.isActive ? t('admin.active') : t('admin.inactive')}</td>
            <td className="num">
              {i.isActive && (
                <button className="link-btn" disabled={busy} onClick={() => reloadPrice(i)}>
                  {t('admin.reloadPrice')}
                </button>
              )}
            </td>
            <td className="num">
              {i.isActive ? (
                <button className="link-btn" onClick={() => requestDeactivate(i)}>
                  {t('admin.deactivate')}
                </button>
              ) : (
                <button className="link-btn" onClick={() => reactivate(i)}>
                  {t('admin.reactivate')}
                </button>
              )}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  </div>

      <div className="panel">
        <div className="section-head">
          <h3>Order Book</h3>
          <select className="field-input" value={bookSymbol} onChange={e => setBookSymbol(e.target.value)}>
            <option value="">—</option>
            {instruments.map(i => (
              <option key={i.id} value={i.id}>{i.symbol}</option>
            ))}
          </select>
        </div>
        {book && (
          <table className="ledger">
            <thead>
              <tr>
                <th>Side</th>
                <th className="num">Price</th>
                <th className="num">Qty</th>
                <th className="num">Orders</th>
              </tr>
            </thead>
            <tbody>
              {[...book.asks].reverse().map(l => (
                <tr key={`a${l.price}`}>
                  <td className="down">Sell</td>
                  <td className="num down">{fmt(l.price)}</td>
                  <td className="num">{l.quantity}</td>
                  <td className="num">{l.orderCount}</td>
                </tr>
              ))}
              <tr>
                <td colSpan={2}>Last</td>
                <td className="num" colSpan={2}>{fmt(book.currentPrice)}</td>
              </tr>
              {book.bids.map(l => (
                <tr key={`b${l.price}`}>
                  <td className="up">Buy</td>
                  <td className="num up">{fmt(l.price)}</td>
                  <td className="num">{l.quantity}</td>
                  <td className="num">{l.orderCount}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      <div className="panel">
        <div className="section-head">
          <h3>{t('admin.usersTitle')}</h3>
        </div>

        <div className="board-controls">
          <div className="search-input">
            <input
              className="field-input"
              type="text"
              value={userQuery}
              onChange={e => setUserQuery(e.target.value)}
              placeholder={t('admin.userSearchPlaceholder')}
            />
            {userQuery && (
              <button
                className="ghost-btn"
                onClick={() => setUserQuery('')}
                aria-label={t('app.close')}
              >
                ×
              </button>
            )}
          </div>
          <select className="field-input" value={userSort} onChange={e => setUserSort(e.target.value)}>
            <option value="name-asc">{t('sort.nameAsc')}</option>
            <option value="name-desc">{t('sort.nameDesc')}</option>
          </select>
        </div>

        {visibleUsers.map(u => (
          <div key={u.id} className="admin-user">
            <div className="admin-user-head">
              <strong>{u.username}</strong>
              <span className="section-note">{u.email}</span>
              <span>{t('admin.free')}: {fmt(u.freeCashBalance)}</span>
              <span>{t('admin.locked')}: {fmt(u.lockedCashBalance)}</span>
              <span>{t('admin.realized')}: {fmt(u.realizedProfitLoss)}</span>
            </div>

            <div className="admin-user-holdings">
              {u.holdings.length === 0 ? (
                <span className="empty">{t('admin.noHoldings')}</span>
              ) : (
                u.holdings.map(h => (
                  <span key={h.symbol}>
                    {h.symbol}: {h.totalQuantity}
                    {h.lockedQuantity > 0 ? ` (${t('board.locked', { n: h.lockedQuantity })})` : ''}
                  </span>
                ))
              )}
            </div>

            <div className="admin-form">
              <input className="field-input" placeholder={t('admin.cashDelta')}
                value={cashDelta[u.id] ?? ''}
                onChange={e => setCashDelta(prev => ({ ...prev, [u.id]: e.target.value }))} />
              <input className="field-input" placeholder={t('admin.reason')}
                value={cashReason[u.id] ?? ''}
                onChange={e => setCashReason(prev => ({ ...prev, [u.id]: e.target.value }))} />
              <button className="ghost-btn" onClick={() => applyCash(u.id)}>
                {t('admin.applyCash')}
              </button>
            </div>

            <div className="admin-form">
              <select className="field-input" value={shareInstrument[u.id] ?? ''}
                onChange={e => setShareInstrument(prev => ({ ...prev, [u.id]: e.target.value }))}>
                <option value="">{t('admin.shareInstrument')}</option>
                {instruments.filter(i => i.isActive).map(i => (
                  <option key={i.id} value={i.id}>{i.symbol}</option>
                ))}
              </select>
              <input className="field-input" placeholder={t('admin.shareQty')}
                value={shareQty[u.id] ?? ''}
                onChange={e => setShareQty(prev => ({ ...prev, [u.id]: e.target.value }))} />
              <button className="ghost-btn" onClick={() => applyShares(u.id)}>
                {t('admin.applyShares')}
              </button>
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}
