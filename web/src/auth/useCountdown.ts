import { useEffect, useState } from 'react'

/** Seconds left until `expiresAt` (epoch ms), re-evaluated every second; calls `onExpire` once at zero. */
export function useCountdown(expiresAt: number, onExpire: () => void): number {
  const [now, setNow] = useState(() => Date.now())

  useEffect(() => {
    const tick = () => {
      const current = Date.now()
      setNow(current)
      if (current >= expiresAt) onExpire()
    }
    tick()
    const timer = setInterval(tick, 1000)
    return () => clearInterval(timer)
  }, [expiresAt, onExpire])

  return Math.max(0, Math.ceil((expiresAt - now) / 1000))
}
