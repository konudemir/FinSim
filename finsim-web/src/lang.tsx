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
  'app.offline': 'connection lost',

  'strip.equity': 'Hesap Değeri',
  'strip.openPL': 'Açık pozisyon',
  'strip.free': 'Hesap Bakiyesi',
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
  'ledger.locked': 'Kilitli',
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

  // server codes
  'srv.InvalidCredentials': 'Kullanıcı adı veya parola hatalı.',
  'srv.UsernameTaken': 'Bu kullanıcı adı alınmış.',
  'srv.EmailTaken': 'Bu e-posta adresi alınmış.',
  'srv.AccountCreated': 'Hesap açıldı. Şimdi giriş yapabilirsin.',
  'srv.ResetLinkSent': 'Bu adres kayıtlıysa sıfırlama bağlantısı gönderildi.',
  'srv.PasswordUpdated': 'Parolan güncellendi.',
  'srv.OrderCancelled': 'Emir iptal edildi.',
  'srv.UserNotFound': 'Kullanıcı bulunamadı.',
  'srv.InstrumentNotFound': 'Hisse bulunamadı.',
  'srv.OrderNotFound': 'Emir bulunamadı.',
  'srv.InstrumentInactive': 'Bu hisse işleme kapalı.',
  'srv.InsufficientFunds': 'Yeterli bakiyen yok.',
  'srv.NoPosition': 'Bu hissede pozisyonun yok.',
  'srv.InsufficientShares': 'Yeterli lotun yok.',
  'srv.NotCancellable': 'Sadece bekleyen emirler iptal edilebilir.',
  'srv.OrderFailed': 'Emir işlenemedi.',
  'srv.InvalidEmail': 'Geçerli bir e-posta adresi gir.',
  'srv.PasswordTooShort': 'Parola en az 8 karakter olmalı.',
  'srv.PasswordRequiresDigit': 'Parola en az bir rakam içermeli.',
  'srv.PasswordRequiresUpper': 'Parola en az bir büyük harf içermeli.',
  'srv.PasswordRequiresLower': 'Parola en az bir küçük harf içermeli.',
  'srv.PasswordRequiresNonAlphanumeric': 'Parola en az bir sembol içermeli.',
  'srv.PasswordRequiresUniqueChars': 'Parola daha fazla farklı karakter içermeli.',
  'srv.PasswordMismatch': 'Mevcut parola hatalı.',
  'srv.InvalidToken': 'Bağlantı geçersiz veya süresi dolmuş.',
  'srv.DuplicateUserName': 'Bu kullanıcı adı alınmış.',
  'srv.DuplicateEmail': 'Bu e-posta adresi alınmış.',
  'srv.unknown': 'Bir hata oluştu.',
  'srv.InvalidQuantity': 'Adet en az 1 olmalı.',
  'srv.InvalidPrice': 'Fiyat 0’dan büyük olmalı.',

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
  'gate.showPassword': 'Parolayı göster',
  'gate.hidePassword': 'Parolayı gizle',

  'landing.headline': 'Rastgele Piyasa Simülasyonu',
  'landing.sub': 'FinSim, BIST enstrümanlarında canlı fiyat akışıyla çalışan bir borsa simülasyonudur. Sanal TL bakiyenle piyasa ve limit emirleri ver, portföyünü anlık izle.',
  'landing.f1': 'Canlı fiyat akışı',
  'landing.f2': 'Piyasa ve limit emirleri',
  'landing.f3': 'Anlık portföy ve kâr/zarar',
  'landing.f4': 'Sanal bakiye, gerçek risk yok',
  'landing.stat1Label': 'enstrüman',
  'landing.stat2Value': 'Piyasa + Limit',
  'landing.stat2Label': 'emir tipi',
  'landing.stat3Value': 'Canlı',
  'landing.stat3Label': 'fiyat akışı',
  'landing.connecting': 'Canlı piyasa verisi bekleniyor…',

  'reset.tag': 'Parola Sıfırlama',
  'reset.account': 'Hesap:',
  'reset.newPassword': 'Yeni parola',
  'reset.newPasswordAgain': 'Yeni parola (tekrar)',
  'reset.submit': 'Parolayı güncelle',
  'reset.mismatch': 'Parolalar eşleşmiyor.',
  'reset.done': 'Parolan güncellendi. Girişe yönlendiriliyorsun…',
  'reset.invalid': 'Bağlantı geçersiz veya süresi dolmuş.',


  'srv.ServerError': 'Sunucuda beklenmeyen bir hata oluştu.',
  'ledger.spent': 'Tutar',

}

const en: typeof tr = {
  'app.tagline': 'Stock Market Simulator',
  'app.market': 'Market',
  'app.logout': 'Sign out',
  'app.toDay': 'Switch to light mode',
  'app.toNight': 'Switch to dark mode',
  'app.close': 'Close',
  'app.offline': 'connection lost',

  'strip.equity': 'Account Value',
  'strip.openPL': 'Open position',
  'strip.free': 'Account Balance',
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
  'ledger.locked': 'Locked',
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

  // server codes
  'srv.InvalidCredentials': 'Username or password is incorrect.',
  'srv.UsernameTaken': 'That username is taken.',
  'srv.EmailTaken': 'That email address is taken.',
  'srv.AccountCreated': 'Account created. You can sign in now.',
  'srv.ResetLinkSent': 'If that address is registered, a reset link has been sent.',
  'srv.PasswordUpdated': 'Your password has been updated.',
  'srv.OrderCancelled': 'Order cancelled.',
  'srv.UserNotFound': 'User not found.',
  'srv.InstrumentNotFound': 'Instrument not found.',
  'srv.OrderNotFound': 'Order not found.',
  'srv.InstrumentInactive': 'That instrument is not tradeable.',
  'srv.InsufficientFunds': 'Not enough cash.',
  'srv.NoPosition': 'You hold no position in that instrument.',
  'srv.InsufficientShares': 'Not enough shares.',
  'srv.NotCancellable': 'Only pending orders can be cancelled.',
  'srv.OrderFailed': 'The order could not be processed.',
  'srv.InvalidEmail': 'Enter a valid email address.',
  'srv.PasswordTooShort': 'Password must be at least 8 characters.',
  'srv.PasswordRequiresDigit': 'Password must contain a digit.',
  'srv.PasswordRequiresUpper': 'Password must contain an uppercase letter.',
  'srv.PasswordRequiresLower': 'Password must contain a lowercase letter.',
  'srv.PasswordRequiresNonAlphanumeric': 'Password must contain a symbol.',
  'srv.PasswordRequiresUniqueChars': 'Password must use more distinct characters.',
  'srv.PasswordMismatch': 'Current password is incorrect.',
  'srv.InvalidToken': 'This link is invalid or has expired.',
  'srv.DuplicateUserName': 'That username is taken.',
  'srv.DuplicateEmail': 'That email address is taken.',
  'srv.unknown': 'Something went wrong.',
  'srv.InvalidQuantity': 'Quantity must be at least 1.',
  'srv.InvalidPrice': 'Price must be greater than 0.',
  

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
  'gate.showPassword': 'Show password',
  'gate.hidePassword': 'Hide password',

  'landing.headline': 'Random Market Simulation',
  'landing.sub': 'FinSim is a stock market simulator running on live BIST-style pricing. Place market and limit orders with a virtual TL balance and watch your portfolio move in real time.',
  'landing.f1': 'Live price feed',
  'landing.f2': 'Market and limit orders',
  'landing.f3': 'Real-time portfolio and P&L',
  'landing.f4': 'Virtual balance, no real risk',
  'landing.stat1Label': 'instruments',
  'landing.stat2Value': 'Market + Limit',
  'landing.stat2Label': 'order types',
  'landing.stat3Value': 'Live',
  'landing.stat3Label': 'price feed',
  'landing.connecting': 'Waiting for live market data…',

  'reset.tag': 'Password Reset',
  'reset.account': 'Account:',
  'reset.newPassword': 'New password',
  'reset.newPasswordAgain': 'New password (again)',
  'reset.submit': 'Update password',
  'reset.mismatch': 'Passwords do not match.',
  'reset.done': 'Password updated. Redirecting to sign in…',
  'reset.invalid': 'This link is invalid or has expired.',

  'srv.ServerError': 'Something went wrong on the server.',
  'ledger.spent': 'Amount',
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
  tServer: (data: unknown) => string
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

  // Turns a server error payload into readable text.
  // The API sends stable codes ("InsufficientFunds"), not sentences.
  const tServer = (data: unknown): string => {
    const one = (code: string) => {
      const key = `srv.${code}` as LangKey
      return key in dictionaries[lang] ? t(key) : t('srv.unknown')
    }

    if (typeof data === 'string') return one(data)
    if (Array.isArray(data)) return data.map(c => one(String(c))).join(' ')

      // our own middleware: { error: "ServerError", traceId: "..." }
    const wrapped = (data as { error?: string; traceId?: string })
    if (wrapped?.error) {
      const text = one(wrapped.error)
      return wrapped.traceId ? `${text} (${wrapped.traceId.slice(-8)})` : text
    }

    // ASP.NET model validation: { errors: { Password: ["PasswordTooShort"] } }
    const errs = (data as { errors?: Record<string, string[]> })?.errors
    if (errs && typeof errs === 'object') {
      return Object.values(errs).flat().map(c => one(String(c))).join(' ')
    }

    return t('srv.unknown')
  }

  const toggle = () => setLang(l => (l === 'tr' ? 'en' : 'tr'))

  return (
    <LangContext.Provider value={{ lang, toggle, t, tServer }}>
      {children}
    </LangContext.Provider>
  )
}

export function useLang(): LangValue {
  const ctx = useContext(LangContext)
  if (!ctx) throw new Error('useLang must be used inside <LangProvider>')
  return ctx
}