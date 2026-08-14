import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'

export type Lang = 'tr' | 'en'

const KEY = 'finsim_lang'

const tr = {
  'app.tagline': 'Borsa Simülasyonu',
  'app.market': 'Market',
  'app.logout': 'Çıkış',
  'app.toDay': 'Gündüz moduna geç',
  'app.toNight': 'Gece moduna geç',
  'app.close': 'Kapat',

  'strip.equity': 'Hesap Değeri',
  'strip.openPL': 'Açık pozisyon',
  'strip.free': 'Serbest',
  'strip.locked': 'Kilitli',
  'strip.position': 'Pozisyon',

  'board.title': 'Tahta',
  'board.note': '{n} enstrüman · emir için seç',
  'board.lots': '{n} lot',
  'board.locked': '{n} kilitli',
  'board.noPosition': 'pozisyon yok',
  'board.closed': 'işleme kapalı',

  'ledger.title': 'Emir Defteri',
  'ledger.note': 'son 50 kayıt',
  'ledger.empty': 'Henüz emir yok. Tahtadan bir hisse seç, aşağıdan adet gir.',
  'ledger.symbol': 'Hisse',
  'ledger.type': 'Tip',
  'ledger.side': 'Yön',
  'ledger.qty': 'Adet',
  'ledger.price': 'Fiyat',
  'ledger.status': 'Durum',
  'ledger.cancel': 'iptal et',

  'order.market': 'Piyasa',
  'order.limit': 'Limit',
  'order.buy': 'Alış',
  'order.sell': 'Satış',
  'status.pending': 'Bekliyor',
  'status.filled': 'Gerçekleşti',
  'status.cancelled': 'İptal',

  'ticket.instrument': 'Enstrüman',
  'ticket.pick': 'Tahtadan seç',
  'ticket.orderType': 'Emir tipi',
  'ticket.qty': 'Adet',
  'ticket.limitPrice': 'Limit fiyatı',
  'ticket.buy': 'Al',
  'ticket.sell': 'Sat',

  'err.minQty': 'Adet en az 1 olmalı.',
  'err.minPrice': 'Limit fiyatı 0’dan büyük olmalı.',
  'err.orderFailed': 'Emir geçmedi.',
  'err.cancelFailed': 'İptal geçmedi.',

  'gate.tag': 'Financial Terminal',
  'gate.username': 'Kullanıcı adı',
  'gate.password': 'Parola',
  'gate.email': 'E-posta',
  'gate.firstName': 'Ad',
  'gate.lastName': 'Soyad',
  'gate.pwHint': 'En az 8 karakter, bir büyük harf, bir rakam ve bir sembol.',
  'gate.login': 'Giriş yap',
  'gate.register': 'Hesap aç',
  'gate.sendReset': 'Sıfırlama bağlantısı gönder',
  'gate.toRegister': 'Hesabın yok mu? Hesap aç',
  'gate.toForgot': 'Parolamı unuttum',
  'gate.toLogin': 'Girişe dön',
  'gate.forgotHint': 'Hesabının e-posta adresini gir, sıfırlama bağlantısını gönderelim.',
  'gate.registered': 'Hesap açıldı. Şimdi giriş yapabilirsin.',
  'gate.resetSent': 'Bu adres kayıtlıysa sıfırlama bağlantısı gönderildi.',
  'gate.noConnection': 'Bağlantı kurulamadı.',

  'reset.tag': 'Parola Sıfırlama',
  'reset.account': 'Hesap:',
  'reset.newPassword': 'Yeni parola',
  'reset.newPasswordAgain': 'Yeni parola (tekrar)',
  'reset.submit': 'Parolayı güncelle',
  'reset.mismatch': 'Parolalar eşleşmiyor.',
  'reset.done': 'Parolan güncellendi. Girişe yönlendiriliyorsun…',
  'reset.invalid': 'Bağlantı geçersiz veya süresi dolmuş.',
}

const en: typeof tr = {
  'app.tagline': 'Stock Market Simulator',
  'app.market': 'Market',
  'app.logout': 'Sign out',
  'app.toDay': 'Switch to light mode',
  'app.toNight': 'Switch to dark mode',
  'app.close': 'Close',

  'strip.equity': 'Account Value',
  'strip.openPL': 'Open position',
  'strip.free': 'Free',
  'strip.locked': 'Locked',
  'strip.position': 'Holdings',

  'board.title': 'Board',
  'board.note': '{n} instruments · select one to trade',
  'board.lots': '{n} lots',
  'board.locked': '{n} locked',
  'board.noPosition': 'no position',
  'board.closed': 'not tradeable',

  'ledger.title': 'Order Book',
  'ledger.note': 'last 50 records',
  'ledger.empty': 'No orders yet. Pick a stock from the board and enter a quantity below.',
  'ledger.symbol': 'Symbol',
  'ledger.type': 'Type',
  'ledger.side': 'Side',
  'ledger.qty': 'Qty',
  'ledger.price': 'Price',
  'ledger.status': 'Status',
  'ledger.cancel': 'cancel',

  'order.market': 'Market',
  'order.limit': 'Limit',
  'order.buy': 'Buy',
  'order.sell': 'Sell',
  'status.pending': 'Pending',
  'status.filled': 'Filled',
  'status.cancelled': 'Cancelled',

  'ticket.instrument': 'Instrument',
  'ticket.pick': 'Select from board',
  'ticket.orderType': 'Order type',
  'ticket.qty': 'Quantity',
  'ticket.limitPrice': 'Limit price',
  'ticket.buy': 'Buy',
  'ticket.sell': 'Sell',

  'err.minQty': 'Quantity must be at least 1.',
  'err.minPrice': 'Limit price must be greater than 0.',
  'err.orderFailed': 'Order was rejected.',
  'err.cancelFailed': 'Cancel was rejected.',

  'gate.tag': 'Financial Terminal',
  'gate.username': 'Username',
  'gate.password': 'Password',
  'gate.email': 'Email',
  'gate.firstName': 'First name',
  'gate.lastName': 'Last name',
  'gate.pwHint': 'At least 8 characters, one uppercase letter, one digit and one symbol.',
  'gate.login': 'Sign in',
  'gate.register': 'Create account',
  'gate.sendReset': 'Send reset link',
  'gate.toRegister': "Don't have an account? Sign up",
  'gate.toForgot': 'Forgot my password',
  'gate.toLogin': 'Back to sign in',
  'gate.forgotHint': 'Enter your account email and we will send a reset link.',
  'gate.registered': 'Account created. You can sign in now.',
  'gate.resetSent': 'If that address is registered, a reset link has been sent.',
  'gate.noConnection': 'Could not reach the server.',

  'reset.tag': 'Password Reset',
  'reset.account': 'Account:',
  'reset.newPassword': 'New password',
  'reset.newPasswordAgain': 'New password (again)',
  'reset.submit': 'Update password',
  'reset.mismatch': 'Passwords do not match.',
  'reset.done': 'Password updated. Redirecting to sign in…',
  'reset.invalid': 'This link is invalid or has expired.',
}

export type LangKey = keyof typeof tr

const dictionaries: Record<Lang, typeof tr> = { tr, en }

function initial(): Lang {
  const saved = localStorage.getItem(KEY)
  if (saved === 'tr' || saved === 'en') return saved
  return navigator.language.startsWith('tr') ? 'tr' : 'en'
}

type LangValue = {
  lang: Lang
  toggle: () => void
  t: (key: LangKey, vars?: Record<string, string | number>) => string
}

const LangContext = createContext<LangValue | null>(null)

export function LangProvider({ children }: { children: ReactNode }) {
  const [lang, setLang] = useState<Lang>(initial)

  useEffect(() => {
    document.documentElement.lang = lang
    localStorage.setItem(KEY, lang)
  }, [lang])

  const t = (key: LangKey, vars?: Record<string, string | number>) => {
    let out = dictionaries[lang][key]
    if (vars) {
      for (const [k, v] of Object.entries(vars)) {
        out = out.replace(`{${k}}`, String(v))
      }
    }
    return out
  }

  const toggle = () => setLang(l => (l === 'tr' ? 'en' : 'tr'))

  return (
    <LangContext.Provider value={{ lang, toggle, t }}>
      {children}
    </LangContext.Provider>
  )
}

export function useLang(): LangValue {
  const ctx = useContext(LangContext)
  if (!ctx) throw new Error('useLang must be used inside <LangProvider>')
  return ctx
}
