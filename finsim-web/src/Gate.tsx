import { useEffect, useRef, useState, type ReactNode } from 'react'
import * as signalR from '@microsoft/signalr'
import api, { API } from './api'
import { useLang, type LangKey } from './lang'
import { useTheme } from './theme'
import { IconCheck, Logomark } from './icons'

type LiveInstrument = { symbol: string; currentPrice: number }
type PriceUpdate = { marketMove: number; indexValue: number; prices: { symbol: string; currentPrice: number }[] }

const fmt = (n: number) =>
  n.toLocaleString('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 })

/**
 * Both /api/instruments and the /hubs/prices SignalR feed are unauthenticated
 * on purpose (public market data), so the marketing rail can show the real,
 * live board instead of a mocked-up one — no login required to read prices.
 */
function useLiveMarket() {
  const [instruments, setInstruments] = useState<LiveInstrument[] | null>(null)
  const [indexValue, setIndexValue] = useState<number | null>(null)
  const [indexHistory, setIndexHistory] = useState<number[]>([])
  const baseline = useRef<Record<string, number>>({})

  useEffect(() => {
    let cancelled = false
    api.get<LiveInstrument[]>('/api/instruments')
      .then(res => {
        if (cancelled) return
        setInstruments(res.data)
        for (const i of res.data) baseline.current[i.symbol] ??= i.currentPrice
      })
      .catch(() => {})
    api.get<number[]>('/api/instruments/index-history?points=30')
      .then(res => {
        if (cancelled) return
        setIndexHistory(prev => [...res.data, ...prev].slice(-30))
        if (res.data.length > 0) {
          setIndexValue(prev => prev ?? res.data[res.data.length - 1])
        }
      })
      .catch(() => {})
    return () => { cancelled = true }
  }, [])

  useEffect(() => {
    const conn = new signalR.HubConnectionBuilder()
      .withUrl(`${API}/hubs/prices`)
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .build()

    conn.on('PriceUpdate', (payload: PriceUpdate) => {
      setIndexValue(payload.indexValue)
      setIndexHistory(prev => [...prev, payload.indexValue].slice(-30))
      setInstruments(prev => {
        if (!prev) return prev
        return prev.map(i => {
          const u = payload.prices.find(x => x.symbol === i.symbol)
          return u ? { ...i, currentPrice: u.currentPrice } : i
        })
      })
    })

    conn.start().catch(() => {})
    return () => { conn.stop() }
  }, [])

  return { instruments, indexValue, indexHistory, baseline }
}

function HeroChart({ data, rising }: { data: number[]; rising: boolean }) {
  const w = 480
  const h = 220
  const pad = 6
  const max = Math.max(...data)
  const min = Math.min(...data)
  const range = max - min || 1
  const last = data.length - 1

  const pts = data.map((v, i) => {
    const x = (i / last) * (w - pad * 2) + pad
    const y = h - pad - ((v - min) / range) * (h - pad * 2)
    return [x, y] as const
  })
  const line = pts.map(([x, y]) => `${x.toFixed(1)},${y.toFixed(1)}`).join(' ')
  const [lx, ly] = pts[last]
  const stroke = rising ? 'var(--rise)' : 'var(--fall)'

  return (
    <svg className="hero-chart" viewBox={`0 0 ${w} ${h}`} preserveAspectRatio="none" aria-hidden="true">
      <defs>
        <linearGradient id="heroFill" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor={stroke} stopOpacity="0.32" />
          <stop offset="100%" stopColor={stroke} stopOpacity="0" />
        </linearGradient>
      </defs>
      {[0.22, 0.5, 0.78].map(f => (
        <line key={f} x1="0" x2={w} y1={h * f} y2={h * f} className="hero-grid" />
      ))}
      <polygon points={`${pad},${h - pad} ${line} ${w - pad},${h - pad}`} fill="url(#heroFill)" />
      <polyline
        points={line}
        pathLength={1}
        className="hero-line"
        fill="none"
        stroke={stroke}
        style={{ filter: `drop-shadow(0 0 5px ${stroke})` }}
      />
      <circle cx={lx} cy={ly} r="9" className="hero-dot-halo" style={{ fill: stroke }} />
      <circle cx={lx} cy={ly} r="4" className="hero-dot" style={{ fill: stroke }} />
    </svg>
  )
}

function GateTape({ instruments, baseline }: { instruments: LiveInstrument[] | null; baseline: Record<string, number> }) {
  if (!instruments || instruments.length === 0) return null

  const items = instruments.map(i => {
    const base = baseline[i.symbol] ?? i.currentPrice
    const pct = base ? ((i.currentPrice - base) / base) * 100 : 0
    return { symbol: i.symbol, price: fmt(i.currentPrice), pct }
  })

  return (
    <div className="gate-tape">
      <div className="gate-tape-run">
        {[0, 1].map(copy => (
          <div key={copy} style={{ display: 'flex' }}>
            {items.map(i => (
              <span className="gate-tape-item" key={`${copy}-${i.symbol}`}>
                <span className="gate-tape-sym">{i.symbol}</span>
                <span className="gate-tape-px">{i.price}</span>
                <span style={{ color: i.pct >= 0 ? 'var(--rise)' : 'var(--fall)' }}>
                  {i.pct >= 0 ? '▲' : '▼'} {Math.abs(i.pct).toFixed(2)}%
                </span>
              </span>
            ))}
          </div>
        ))}
      </div>
    </div>
  )
}

/** Split-screen shell shared by the login/register/forgot/reset screens. */
export default function GateLayout({ children }: { children: ReactNode }) {
  const { lang, toggle: toggleLang, t } = useLang()
  const { theme, toggle: toggleTheme } = useTheme()
  const { instruments, indexValue, indexHistory, baseline } = useLiveMarket()

  const features: LangKey[] = ['landing.f1', 'landing.f2', 'landing.f3', 'landing.f4']

  const first = indexHistory[0]
  const marketMove = indexHistory.length >= 2 && first ? indexHistory[indexHistory.length - 1] / first - 1 : 0

  return (
    <div className="gate-shell">
      <aside className="gate-aside">
        <div className="gate-aside-in">
          <div className="gate-brand">
            <Logomark size={30} />
            <div>
              <div className="gate-wordmark">Fin<em>Sim</em></div>
              <div className="gate-badge">{t('gate.tag')}</div>
            </div>
          </div>

          <h1 className="gate-headline">{t('landing.headline')}</h1>
          <p className="gate-sub">{t('landing.sub')}</p>

          <div className="hero-panel">
            {indexValue !== null && indexHistory.length >= 2 ? (
              <>
                <div className="hero-index">
                  <span className="hero-index-name">SIM100</span>
                  <span className="hero-index-value">{fmt(indexValue)}</span>
                  <span className="hero-index-delta" style={{ color: marketMove >= 0 ? 'var(--rise)' : 'var(--fall)' }}>
                    {marketMove >= 0 ? '▲' : '▼'} {(Math.abs(marketMove) * 100).toFixed(2)}%
                  </span>
                </div>
                <HeroChart data={indexHistory} rising={marketMove >= 0} />
              </>
            ) : null}
          </div>

          <ul className="gate-features">
            {features.map(k => (
              <li key={k} className="gate-feature">
                <span className="gate-feature-icon"><IconCheck /></span>
                {t(k)}
              </li>
            ))}
          </ul>

          <div className="gate-stats">
            <div className="gate-stat">
              <span className="gate-stat-value">{instruments ? instruments.length : '—'}</span>
              <span className="gate-stat-label">{t('landing.stat1Label')}</span>
            </div>
            <div className="gate-stat">
              <span className="gate-stat-value">{t('landing.stat2Value')}</span>
              <span className="gate-stat-label">{t('landing.stat2Label')}</span>
            </div>
            <div className="gate-stat">
              <span className="gate-stat-value">{t('landing.stat3Value')}</span>
              <span className="gate-stat-label">{t('landing.stat3Label')}</span>
            </div>
          </div>
        </div>

        <GateTape instruments={instruments} baseline={baseline.current} />
      </aside>

      <main className="gate-main">
        <div className="gate-toolbar">
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

        <div className="gate-mobile-brand">
          <Logomark size={26} />
          <span className="gate-wordmark sm">Fin<em>Sim</em></span>
        </div>

        {children}
      </main>
    </div>
  )
}
