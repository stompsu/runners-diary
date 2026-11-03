import { waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { getFakeApi } from '@/test/fake'
import { faults } from '@/test/fakeApi'
import { renderRoute, signInForTest } from '@/test/render'

const putCount = () => getFakeApi().requests.filter((r) => r === 'PUT /api/me').length

function browserZoneIs(zone: string) {
  const original = Intl.DateTimeFormat.prototype.resolvedOptions
  vi.spyOn(Intl.DateTimeFormat.prototype, 'resolvedOptions').mockImplementation(function (this: Intl.DateTimeFormat) {
    return { ...original.call(this), timeZone: zone }
  })
}

describe('time zone sync', () => {
  afterEach(() => vi.restoreAllMocks())

  it('writes the browser zone to the profile once for a real user with the default zone', async () => {
    browserZoneIs('America/New_York')
    renderRoute('/insights', { session: 'user' })

    await waitFor(() => expect(putCount()).toBe(1))
    await waitFor(() => expect(getFakeApi().requests.filter((r) => r === 'GET /api/me').length).toBeGreaterThan(1))
    expect(putCount()).toBe(1)
  })

  it('sends the zone together with the unchanged profile fields', async () => {
    browserZoneIs('America/New_York')
    const bodies: unknown[] = []
    const fake = getFakeApi()
    const handle = fake.handle
    fake.handle = async (request) => {
      if (request.method === 'PUT' && new URL(request.url).pathname === '/api/me')
        bodies.push(await request.clone().json())
      return handle(request)
    }
    renderRoute('/insights', { session: 'user' })

    await waitFor(() => expect(bodies).toHaveLength(1))
    expect(bodies[0]).toEqual({ displayName: 'Test Runner', timeZoneId: 'America/New_York', unitSystem: 'Metric' })
  })

  it('does not touch guests', async () => {
    browserZoneIs('America/New_York')
    renderRoute('/insights', { session: 'guest' })

    await waitFor(() => expect(getFakeApi().requests).toContain('GET /api/me'))
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(putCount()).toBe(0)
  })

  it('does not overwrite a zone the user has already chosen', async () => {
    browserZoneIs('America/New_York')
    const fake = getFakeApi()
    const token = signInForTest('user')
    const chosen = await fake.handle(
      new Request('http://localhost/api/me', {
        method: 'PUT',
        headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
        body: JSON.stringify({ displayName: 'Test Runner', timeZoneId: 'Asia/Tokyo', unitSystem: 'Metric' }),
      }),
    )
    expect(chosen.status).toBe(200)
    const putsBefore = putCount()

    renderRoute('/insights', { session: 'none' }) // the session started above stays active
    await waitFor(() => expect(fake.requests).toContain('GET /api/me'))
    await new Promise((resolve) => setTimeout(resolve, 50))

    expect(putCount()).toBe(putsBefore)
  })

  it('stays silent and does not retry when the update fails', async () => {
    browserZoneIs('America/New_York')
    getFakeApi().addFault(faults.status({ method: 'PUT', path: '/api/me' }, 500))
    renderRoute('/insights', { session: 'user' })

    await waitFor(() => expect(putCount()).toBe(1))
    await new Promise((resolve) => setTimeout(resolve, 100))
    expect(putCount()).toBe(1)
  })
})
