import { describe, expect, it } from 'vitest'
import { createFakeApi } from '.'

const BASE = 'http://localhost'
const PATH = '/api/races/interests'

type Api = ReturnType<typeof createFakeApi>

const call = (api: Api, method: string, token: string | null, body?: unknown) =>
  api.handle(
    new Request(BASE + PATH, {
      method,
      headers: {
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
    }),
  )

const valid = {
  kinds: ['Road'],
  regionCodes: [] as string[],
  center: null,
  radiusMeters: null,
  minDistanceMeters: null,
  maxDistanceMeters: null,
  horizonDays: 120,
  keywords: null,
  showInCalendar: true,
}

async function errorsOf(api: Api, token: string, overrides: Record<string, unknown>) {
  const response = await call(api, 'PUT', token, { ...valid, ...overrides })
  expect(response.status).toBe(400)
  return ((await response.json()) as { errors: Record<string, string[]> }).errors
}

describe('fake API: race interests', () => {
  it('needs a session', async () => {
    const api = createFakeApi()
    expect((await call(api, 'GET', null)).status).toBe(401)
    expect((await call(api, 'PUT', null, valid)).status).toBe(401)
  })

  it('answers configured:false with the defaults for a user and the demo profile for a guest', async () => {
    const api = createFakeApi()
    const user = await (await call(api, 'GET', api.issueToken('User'))).json()
    expect(user).toMatchObject({
      configured: false,
      kinds: ['Road', 'Trail'],
      regionCodes: [],
      center: null,
      minDistanceMeters: 5000,
      maxDistanceMeters: 42_195,
      horizonDays: 120,
      showInCalendar: true,
      updatedAt: null,
    })
    const guest = await (await call(api, 'GET', api.issueToken('Guest'))).json()
    expect(guest).toMatchObject({
      configured: true,
      kinds: ['Road', 'Trail', 'Mountain'],
      regionCodes: ['PL-24'],
      center: { latitude: 50.2945, longitude: 18.6714 },
      radiusMeters: 60_000,
      horizonDays: 120,
    })
  })

  it('can start guests without a profile', async () => {
    const api = createFakeApi({ guestRaceProfile: false })
    expect(await (await call(api, 'GET', api.issueToken('Guest'))).json()).toMatchObject({ configured: false })
  })

  it('replaces the profile per account and normalises it', async () => {
    const api = createFakeApi()
    const a = api.issueToken('User')
    const b = api.issueToken('User')
    const response = await call(api, 'PUT', a, {
      ...valid,
      kinds: ['Ultra'],
      center: { latitude: 50, longitude: 19, name: '  Gliwice ' },
      radiusMeters: 5000,
      keywords: '  night   run ',
    })
    expect(response.status).toBe(200)
    expect(await response.json()).toMatchObject({
      configured: true,
      kinds: ['Ultra'],
      center: { name: 'Gliwice' },
      keywords: 'night run',
    })
    expect(await (await call(api, 'GET', a)).json()).toMatchObject({ configured: true, kinds: ['Ultra'] })
    expect(await (await call(api, 'GET', b)).json()).toMatchObject({ configured: false, kinds: ['Road', 'Trail'] })
    expect(api.raceInterestsOf(a)?.kinds).toEqual(['Ultra'])
    expect(api.raceInterestsOf(b)).toBeNull()
  })

  it('is a 503 feature-unavailable while race proposals are disabled, and client-config says so', async () => {
    const api = createFakeApi({ racesEnabled: false })
    expect((await call(api, 'GET', api.issueToken('User'))).status).toBe(503)
    api.setRacesEnabled(true)
    expect((await call(api, 'GET', api.issueToken('User'))).status).toBe(200)
    const config = await api.handle(new Request(`${BASE}/api/client-config`))
    expect(await config.json()).toMatchObject({ races: { enabled: true, sources: [{ id: 'running-life' }] } })
  })

  describe('validation (ADR 0037 §1, same keys as the Domain rules)', () => {
    const api = createFakeApi()
    const token = api.issueToken('User')
    const place = { latitude: 50, longitude: 19, name: null }

    it('accepts the edges', async () => {
      for (const overrides of [
        { kinds: ['Road', 'Trail', 'Mountain', 'Ultra', 'CrossCountry', 'Ocr', 'Other'] },
        { center: place, radiusMeters: 5000 },
        { center: place, radiusMeters: 300_000 },
        { minDistanceMeters: 1000, maxDistanceMeters: 250_000 },
        { minDistanceMeters: 10_000, maxDistanceMeters: 10_000 },
        { horizonDays: 7 },
        { horizonDays: 365 },
        { keywords: 'aa bb cc dd ee' },
        { keywords: 'x'.repeat(30) },
      ]) {
        expect([overrides, (await call(api, 'PUT', token, { ...valid, ...overrides })).status]).toEqual([
          overrides,
          200,
        ])
      }
    })

    it('rejects kinds that are missing or unknown', async () => {
      expect(await errorsOf(api, token, { kinds: [] })).toHaveProperty('kinds')
      expect(await errorsOf(api, token, { kinds: ['Swim'] })).toHaveProperty('kinds')
    })

    it('rejects unknown and duplicate region codes', async () => {
      expect(await errorsOf(api, token, { regionCodes: ['PL-03'] })).toHaveProperty('regionCodes')
      expect(await errorsOf(api, token, { regionCodes: ['PL-24', 'PL-24'] })).toHaveProperty('regionCodes')
    })

    it('takes a place and a radius together or not at all', async () => {
      expect(await errorsOf(api, token, { center: place })).toHaveProperty('radiusMeters')
      expect(await errorsOf(api, token, { radiusMeters: 5000 })).toHaveProperty('center')
      expect(await errorsOf(api, token, { center: place, radiusMeters: 4999 })).toHaveProperty('radiusMeters')
      expect(await errorsOf(api, token, { center: place, radiusMeters: 300_001 })).toHaveProperty('radiusMeters')
      expect(await errorsOf(api, token, { center: { ...place, latitude: 91 }, radiusMeters: 5000 })).toHaveProperty(
        'center',
      )
      expect(
        await errorsOf(api, token, { center: { ...place, name: 'x'.repeat(201) }, radiusMeters: 5000 }),
      ).toHaveProperty('centerName')
    })

    it('checks the distance range and order', async () => {
      expect(await errorsOf(api, token, { minDistanceMeters: 999 })).toHaveProperty('minDistanceMeters')
      expect(await errorsOf(api, token, { maxDistanceMeters: 250_001 })).toHaveProperty('maxDistanceMeters')
      expect(await errorsOf(api, token, { minDistanceMeters: 20_000, maxDistanceMeters: 10_000 })).toHaveProperty(
        'maxDistanceMeters',
      )
    })

    it('checks the horizon and keywords', async () => {
      expect(await errorsOf(api, token, { horizonDays: 6 })).toHaveProperty('horizonDays')
      expect(await errorsOf(api, token, { horizonDays: 366 })).toHaveProperty('horizonDays')
      expect(await errorsOf(api, token, { keywords: 'aa bb cc dd ee ff' })).toHaveProperty('keywords')
      expect(await errorsOf(api, token, { keywords: 'a' })).toHaveProperty('keywords')
      expect(await errorsOf(api, token, { keywords: Array(5).fill('x'.repeat(20)).join(' ') })).toHaveProperty(
        'keywords',
      )
    })

    it('rejects unknown members and a missing flag', async () => {
      expect(await errorsOf(api, token, { extra: 1 })).toHaveProperty('extra')
      expect(await errorsOf(api, token, { showInCalendar: undefined })).toHaveProperty('showInCalendar')
    })
  })
})
