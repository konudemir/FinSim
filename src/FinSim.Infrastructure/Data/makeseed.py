#!/usr/bin/env python3
"""
Throwaway: builds seed.json for FinSim's DbSeeder from tickers.txt.

tickers.txt is tab-separated: SYMBOL<TAB>OFFICIAL NAME

Cleans the official name down to something readable, then fetches a current
price from Yahoo to use as fallbackPrice. Symbols Yahoo doesn't recognise are
dropped, so the output only contains tickers the app can actually poll.

    pip install requests
    python make_seed.py

Delete this file once seed.json is generated.
"""

import json
import re
import sys
import time

import requests

UA = ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
      "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36")

URL = "https://query1.finance.yahoo.com/v8/finance/chart/{}.IS?interval=1d&range=1d"

# Legal forms and boilerplate, longest first so "T.A.Ş." goes before "A.Ş."
NOISE = [
    "SINAİ VE FİNANSAL YATIRIMLAR SANAYİ VE TİCARET",
    "ELEKTRONİK SANAYİ VE TİCARET",
    "SANAYİ VE TİCARET",
    "SANAYİ TİCARET",
    "VE TİCARET",
    "TİCARET VE SANAYİ",
    "MÜHENDİSLİK TİCARET VE SANAYİ",
    "ANONİM TÜRK SİGORTA ŞİRKETİ",
    "T.A.O.", "T.A.Ş.", "A.Ş.", "A.O.",
]

# Leading "I" is ambiguous in Turkish (Işıklar vs İş Bankası), and a few names
# read better shortened. Overrides are matched on the cleaned result.
OVERRIDES = {
    "Turkiye Is Bankasi": "İş Bankası",
    "Is Yatirim Menkul Degerler": "İş Yatırım",
    "Izdemir Enerji Elektrik Uretim": "İzdemir Enerji",
    "Haci Omer Sabanci Holding": "Sabancı Holding",
    "Tupras-Turkiye Petrol Rafinerileri": "Tüpraş",
    "Turkiye Garanti Bankasi": "Garanti BBVA",
    "Turkiye Sise ve Cam Fabrikalari": "Şişecam",
    "Turkiye Vakiflar Bankasi": "VakıfBank",
    "Turkiye Halk Bankasi": "Halkbank",
    "Turkiye Sinai Kalkinma Bankasi": "TSKB",
    "Turk Telekomunikasyon": "Türk Telekom",
    "Eregli Demir ve Celik Fabrikalari": "Erdemir",
    "Kardemir Karabuk Demir Celik": "Kardemir (D)",
    "Mlp Saglik Hizmetleri": "MLP Sağlık",
    "Eis Eczacibasi Ilac": "Eczacıbaşı İlaç",
    "Anadolu Efes Biracilik ve Malt Sanayii": "Anadolu Efes",
    "Anadolu Sigorta": "Anadolu Sigorta",
    "Emlak Konut Gayrimenkul Yatirim Ortakligi": "Emlak Konut GYO",
    "Pasifik Gayrimenkul Yatirim Ortakligi": "Pasifik GYO",
    "Ford Otomotiv Sanayi": "Ford Otosan",
    "Tofas Turk Otomobil Fabrikasi": "Tofaş",
    "Pegasus Hava Tasimaciligi": "Pegasus",
    "Tav Havalimanlari Holding": "TAV Havalimanları",
    "Galatasaray Sportif Sinai ve Ticari Yatirimlar": "Galatasaray Sportif",
    "Fenerbahce Futbol": "Fenerbahçe Futbol",
    "Cw Enerji": "CW Enerji",
    "Cvk Maden Isletmeleri": "CVK Maden",
    "Tr Anadolu Metal Madencilik Isletmeleri": "TR Anadolu Metal",
    "Tr Dogal Enerji Kaynaklari Arastirma ve Uretim": "TR Doğal Enerji",
    "Qua Granite Hayal Yapi ve Urunleri": "Qua Granite",
    "Odine Solutions Teknoloji": "Odine Solutions",
    "Oba Makarnacilik": "Oba Makarna",
    "Gur-Sel Turizm Tasimacilik ve Servis": "Gür-Sel Turizm",
    "Gulermak Agir Sanayi Insaat ve Taahhut": "Gülermak",
    "Girisim Elektrik Sanayi Taahhut": "Girişim Elektrik",
    "Gen Ilac ve Saglik Urunleri": "Gen İlaç",
    "Europen Endustri Insaat": "Europen Endüstri",
    "Europower Enerji ve Otomasyon Teknolojileri": "Europower Enerji",
    "Esenboga Elektrik Uretim": "Esenboğa Elektrik",
    "Enka Insaat ve Sanayi": "Enka İnşaat",
    "Dogan Sirketler Grubu Holding": "Doğan Holding",
    "Dogus Otomotiv Servis": "Doğuş Otomotiv",
    "Destek Finans Faktoring": "Destek Faktoring",
    "Dap Gayrimenkul Gelistirme": "DAP Gayrimenkul",
    "Cimsa Cimento Sanayi": "Çimsa",
    "Can2 Termik": "Çan2 Termik",
    "Baticim Bati Anadolu": "Batıçim",
    "Baticim Cimento Sanayi": "Batısöke Çimento",
    "Borusan Birlesik Boru Fabrikalari": "Borusan Boru",
    "Borusan Yatirim ve Pazarlama": "Borusan Yatırım",
    "Bim Birlesik Magazalar": "BİM",
    "Aksa Akrilik Kimya Sanayii": "Aksa Akrilik",
    "Aksa Enerji Uretim": "Aksa Enerji",
    "Altinay Savunma Teknolojileri": "Altınay Savunma",
    "Sarkuysan Elektrolitik Bakir": "Sarkuysan",
    "Sasa Polyester Sanayi": "Sasa Polyester",
    "Sekerbank": "Şekerbank",
    "Sok Marketler": "ŞOK Marketler",
    "Otokar Otomotiv ve Savunma Sanayi": "Otokar",
    "Oyak Cimento Fabrikalari": "Oyak Çimento",
    "Pasifik Eurasia Lojistik Dis": "Pasifik Eurasia",
    "Petkim Petrokimya Holding": "Petkim",
    "Isiklar Enerji ve Yapi Holding": "Işıklar Enerji",
    "Katilimevim Tasarruf Finansman": "Katılımevim",
    "Margun Enerji Uretim": "Margün Enerji",
    "Mavi Giyim Sanayi": "Mavi Giyim",
    "Mia Teknoloji": "MİA Teknoloji",
    "Migros Ticaret": "Migros",
    "Odas Elektrik Uretim Sanayi": "Odaş Elektrik",
    "Reeder Teknoloji": "Reeder",
    "Ral Yatirim Holding": "RAL Yatırım",
    "Tukas Gida": "Tukaş",
    "Turkcell Iletisim Hizmetleri": "Turkcell",
    "Turk Altin Isletmeleri": "Türk Altın",
    "Turk Hava Yollari": "Türk Hava Yolları",
    "Turkiye Sigorta": "Türkiye Sigorta",
    "Ulker Biskuvi Sanayi": "Ülker",
    "Vestel Elektronik Sanayi": "Vestel",
    "Yapi ve Kredi Bankasi": "Yapı Kredi",
    "Zorlu Enerji Elektrik Uretim": "Zorlu Enerji",
    "Grainturk Holding": "GrainTurk Holding",
    "Efor Yatirim Sanayi": "Efor Yatırım",
    "Enerjisa Enerji": "Enerjisa",
    "Enerya Enerji": "Enerya",
    "Gubre Fabrikalari": "Gübre Fabrikaları",
    "Alarko Holding": "Alarko Holding",
    "Bera Holding": "Bera Holding",
    "Kiler Holding": "Kiler Holding",
    "Koc Holding": "Koç Holding",
    "Kuyas Yatirim": "Kuyaş Yatırım",
    "Pasifik Holding": "Pasifik Holding",
    "Pasifik Teknoloji": "Pasifik Teknoloji",
    "Pahol": "Pasifik Holding",
    "Tekfen Holding": "Tekfen Holding",
    "Hektas Ticaret": "Hektaş",
    "Astor Enerji": "Astor Enerji",
    "Balsu Gida": "Balsu Gıda",
    "Coca-Cola Icecek": "Coca-Cola İçecek",
    "Arcelik": "Arçelik",
    "Aselsan": "Aselsan",
    "Akbank": "Akbank",
    "Anadolu": "Anadolu Sigorta",
    "Cw Enerji Muhendislik": "CW Enerji",
    "Cimsa Cimento": "Çimsa",
    "Efor Yatirim": "Efor Yatırım",
    "Gur-Sel Turizm Tasimacilik ve Servis Ticaret": "Gür-Sel Turizm",
    "Odas Elektrik Uretim": "Odaş Elektrik",
    "Pasifik Eurasia Lojistik Dis Ticaret": "Pasifik Eurasia",
    "Sok Marketler Ticaret": "ŞOK Marketler",
    "Turkiye Vakiflar Bankasi T.": "VakıfBank",
}

def ascii_key(s: str) -> str:
    """Fold Turkish characters to ASCII so OVERRIDES can be matched reliably."""
    table = str.maketrans("çğıİöşüÇĞIÖŞÜ", "cgiIosuCGIOSU")
    return s.translate(table)


def title_case(s: str) -> str:
    """Title-case each word, handling hyphens. Turkish diacritics are dropped
    first by ascii_key on the lookup side, so this only needs to be readable."""
    def cap(word):
        return "-".join(p[:1].upper() + p[1:].lower() for p in word.split("-") if p) or word
    words = [cap(w) for w in s.split()]
    return " ".join("ve" if w == "Ve" else w for w in words)


def clean_name(official: str) -> str:
    name = official.strip()
    for noise in NOISE:
        name = name.replace(noise, "")
    name = re.sub(r"\s+", " ", name).strip(" -,")
    name = title_case(ascii_key(name))
    return OVERRIDES.get(name, name)


def fetch_price(symbol: str, session: requests.Session):
    try:
        r = session.get(URL.format(symbol), timeout=10)
        if r.status_code != 200:
            return None, f"HTTP {r.status_code}"
        data = r.json()
        chart = data.get("chart") or {}
        if chart.get("error"):
            return None, "chart.error"
        result = chart.get("result") or []
        if not result:
            return None, "empty result"
        meta = result[0].get("meta") or {}
        if meta.get("currency") != "TRY":
            return None, f"currency {meta.get('currency')}"
        price = meta.get("regularMarketPrice")
        if not price or price <= 0:
            return None, "no price"
        return float(price), None
    except Exception as exc:
        return None, str(exc)


def main():
    rows = []
    with open("tickers.txt", encoding="utf-8") as f:
        for line in f:
            line = line.rstrip("\n")
            if not line.strip():
                continue
            parts = line.split("\t")
            if len(parts) < 2:
                print(f"skipping malformed line: {line}", file=sys.stderr)
                continue
            rows.append((parts[0].strip(), parts[1].strip()))

    session = requests.Session()
    session.headers.update({"User-Agent": UA})

    out, dropped = [], []
    for i, (symbol, official) in enumerate(rows, 1):
        price, err = fetch_price(symbol, session)
        if price is None:
            dropped.append((symbol, err))
            print(f"[{i}/{len(rows)}] {symbol:<6} DROPPED ({err})")
        else:
            out.append({
                "symbol": symbol,
                "name": clean_name(official),
                "fallbackPrice": round(price, 2),
                "active": True,
            })
            print(f"[{i}/{len(rows)}] {symbol:<6} {price:>10.2f}  {out[-1]['name']}")
        time.sleep(0.5)   # be polite; the endpoint is undocumented and IP-limited

    with open("seed.json", "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=2)

    print(f"\nwrote seed.json with {len(out)} instruments")
    if dropped:
        print(f"dropped {len(dropped)}: " + ", ".join(s for s, _ in dropped))


if __name__ == "__main__":
    main()