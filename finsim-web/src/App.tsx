import { useEffect, useState } from 'react'
import axios from 'axios'
import * as signalR from '@microsoft/signalr'


type Instrument = {
  id: string
  symbol: string
  name: string
  currentPrice: number
  isActive: boolean
}

export default function App() {
  const [instruments, setInstruments] = useState<Instrument[]>([])

  useEffect(() => {
    axios.get<Instrument[]>('http://localhost:5209/api/instruments')
      .then(res => setInstruments(res.data))
      .catch(err => console.error(err))
  }, [])
  

  useEffect(() => {
  const conn = new signalR.HubConnectionBuilder()
    .withUrl('http://localhost:5209/hubs/prices')
    .withAutomaticReconnect()
    .build()

  conn.on('PriceUpdate', (updates: { symbol: string; currentPrice: number }[]) => {
    setInstruments(prev =>
      prev.map(i => {
        const u = updates.find(x => x.symbol === i.symbol)
        return u ? { ...i, currentPrice: u.currentPrice } : i
      })
    )
  })

  conn.start().catch(console.error)

  return () => { conn.stop() }
}, [])

  return (
    <div className="p-8">
      <h1 className="text-2xl font-bold mb-4">Borsa Tahtası</h1>
      <ul>
        {instruments.map(i => (
          <li key={i.id}>{i.symbol} — {i.currentPrice}</li>
        ))}
      </ul>
    </div>
  )
}