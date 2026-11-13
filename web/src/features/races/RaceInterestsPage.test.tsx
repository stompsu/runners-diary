import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { supportsWebGl2 } from '@/components/map/webgl'
import { getFakeApi, resetFakeApi } from '@/test/fake'
import { resetFakeMap } from '@/test/fakeMapState'
import { renderRoute, signInForTest } from '@/test/render'
import { recordRequests, respondOnce } from '@/test/requests'
import { stubResizeObserver } from '@/test/resizeObserver'

// The picker is a lazy chunk with the map view; load it during collection (no test timeout applies).
await import('@/features/runs/LocationPickerDialog')

vi.mock('@/components/map/webgl', () => ({ supportsWebGl2: vi.fn<() => boolean>(() => true) }))
vi.mock('@/components/map/maplibre/MapLibreMap', () => import('@/test/fakeMapProvider'))

beforeEach(() => {
  // Radix checkboxes inside a form measure their hidden input; jsdom has no ResizeObserver.
  stubResizeObserver()
  resetFakeMap()
  vi.mocked(supportsWebGl2).mockReturnValue(true)
})

const PATH = '/settings/races'

/** A real user starts without a profile (`configured: false` + defaults); a guest gets the demo profile. */
async function open(kind: 'user' | 'guest' = 'user', options: Parameters<typeof resetFakeApi>[0] = {}) {
  resetFakeApi(options)
  const token = signInForTest(kind)
  const user = userEvent.setup()
  const result = renderRoute(PATH, { session: 'none' })
  const form = await screen.findByRole('form', { name: 'Race interests' })
  return { user, form, token, ...result }
}

const checkbox = (form: HTMLElement, name: string) => within(form).getByRole('checkbox', { name })
/** Sonner renders the message in the toast and in its live region. */
const savedToast = () => screen.findAllByText('Race interests saved')
const puts = (requests: ReturnType<typeof recordRequests>) =>
  requests.filter((request) => request.method === 'PUT' && request.path === '/api/races/interests')

describe('Race interests page: loading', () => {
  it('shows the defaults of a user without a saved profile and says they are not saved yet', async () => {
    const { form } = await open('user')

    expect(screen.getByRole('heading', { level: 1, name: 'Race interests' })).toBeInTheDocument()
    expect(screen.getByText(/You have not saved any interests yet/)).toBeInTheDocument()
    expect(checkbox(form, 'Road')).toBeChecked()
    expect(checkbox(form, 'Trail')).toBeChecked()
    expect(checkbox(form, 'Mountain')).not.toBeChecked()
    expect(within(form).getByText('0 of 16 voivodeships selected')).toBeInTheDocument()
    expect(within(form).getByLabelText('From (km)')).toHaveValue('5')
    expect(within(form).getByLabelText('To (km)')).toHaveValue('42.195')
    expect(within(form).getByLabelText('Look ahead')).toHaveValue('120')
    expect(within(form).getByLabelText('Keywords (optional)')).toHaveValue('')
    expect(within(form).getByRole('switch', { name: 'Show proposals in the diary' })).toBeChecked()
    expect(within(form).getByLabelText('Radius (km)')).toBeDisabled()
    expect(within(form).queryByRole('region', { name: /Preview of the search area/ })).not.toBeInTheDocument()
  })

  it('pre-fills the demo profile of a guest, with the radius circle on the map', async () => {
    const { form } = await open('guest')

    expect(screen.queryByText(/You have not saved any interests yet/)).not.toBeInTheDocument()
    expect(checkbox(form, 'Road')).toBeChecked()
    expect(checkbox(form, 'Mountain')).toBeChecked()
    expect(checkbox(form, 'Ultra')).not.toBeChecked()
    expect(checkbox(form, 'Silesian (Śląskie)')).toBeChecked()
    expect(within(form).getByText('1 of 16 voivodeships selected')).toBeInTheDocument()
    expect(within(form).getByTestId('location-coordinates')).toHaveTextContent('50.29450, 18.67140')
    expect(within(form).getByLabelText('Place name')).toHaveValue('Gliwice')
    expect(within(form).getByLabelText('Radius (km)')).toHaveValue(60)
    expect(await within(form).findByTestId('fake-map')).toHaveAttribute('data-area', '50.2945,18.6714,60000')
    expect(within(form).getByLabelText('Radius slider (km)')).toHaveValue('60')
  })

  it('shows every section, the Ultra hint and the 16 voivodeships with both names', async () => {
    const { form } = await open('user')

    for (const legend of ['What', 'Where', 'Distance', 'When', 'Keywords', 'Diary', 'Sources']) {
      expect(within(form).getByRole('group', { name: legend })).toBeInTheDocument()
    }
    expect(checkbox(form, 'Ultra')).toHaveAccessibleDescription('Ultra also matches any race longer than a marathon.')
    expect(checkbox(form, 'Lower Silesian (Dolnośląskie)')).toBeInTheDocument()
    expect(checkbox(form, 'West Pomeranian (Zachodniopomorskie)')).toBeInTheDocument()
    const regions = within(form).getByRole('group', { name: 'Voivodeships' })
    expect(within(regions).getAllByRole('checkbox')).toHaveLength(16)
    expect(
      within(form).getByText(/a race matches if either one fits\. With neither, races from all of Poland match/),
    ).toBeInTheDocument()
  })

  it('lists the sources as safe external links with the disclaimer', async () => {
    const { form } = await open('user')

    const sources = within(form).getByRole('group', { name: 'Sources' })
    const link = within(sources).getByRole('link', { name: /running\.life/ })
    expect(link).toHaveAttribute('href', 'https://running.life/')
    expect(link).toHaveAttribute('target', '_blank')
    expect(link).toHaveAttribute('rel', 'noopener noreferrer')
    expect(link).toHaveAttribute('referrerpolicy', 'no-referrer')
    expect(within(sources).getByText(/Race facts come from public calendars/)).toBeInTheDocument()
  })

  it('offers a horizon the server sent even when it is not a preset', async () => {
    resetFakeApi()
    const token = signInForTest('user')
    const user = userEvent.setup()
    // Save 45 days straight through the fake, then open the page.
    await getFakeApi().handle(
      new Request('http://localhost/api/races/interests', {
        method: 'PUT',
        headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
        body: JSON.stringify({
          kinds: ['Road'],
          regionCodes: [],
          horizonDays: 45,
          showInCalendar: true,
        }),
      }),
    )
    renderRoute(PATH, { session: 'none' })
    const form = await screen.findByRole('form', { name: 'Race interests' })
    const select = within(form).getByLabelText('Look ahead')
    expect(select).toHaveValue('45')
    expect(
      within(select)
        .getAllByRole('option')
        .map((option) => option.textContent),
    ).toEqual(['30 days', '45 days', '60 days', '90 days', '120 days', '180 days', '365 days'])
    expect(user).toBeDefined()
  })
})

describe('Race interests page: pickers', () => {
  it('Select all ticks the 16 voivodeships and Clear unticks them, announcing the count', async () => {
    const { user, form } = await open('user')

    await user.click(within(form).getByRole('button', { name: 'Select all' }))
    expect(within(form).getByText('16 of 16 voivodeships selected')).toBeInTheDocument()
    expect(checkbox(form, 'Opole (Opolskie)')).toBeChecked()

    await user.click(checkbox(form, 'Opole (Opolskie)'))
    expect(within(form).getByText('15 of 16 voivodeships selected')).toBeInTheDocument()

    await user.click(within(form).getByRole('button', { name: 'Clear' }))
    expect(within(form).getByText('0 of 16 voivodeships selected')).toBeInTheDocument()
    expect(checkbox(form, 'Silesian (Śląskie)')).not.toBeChecked()
  })

  it('toggles kinds with the keyboard', async () => {
    const { user, form } = await open('user')

    checkbox(form, 'Mountain').focus()
    await user.keyboard(' ')
    expect(checkbox(form, 'Mountain')).toBeChecked()
    await user.keyboard(' ')
    expect(checkbox(form, 'Mountain')).not.toBeChecked()
  })

  it('fills the distance fields from a preset and marks it pressed until the values change', async () => {
    const { user, form } = await open('user')
    const min = within(form).getByLabelText('From (km)')
    const max = within(form).getByLabelText('To (km)')
    const preset = (name: string) => within(form).getByRole('button', { name })

    for (const [name, from, to] of [
      ['5K', '4.5', '5.5'],
      ['10K', '9.5', '10.5'],
      ['Half', '21', '21.5'],
      ['Marathon', '42', '42.5'],
      ['Ultra', '42.2', '250'],
    ] as const) {
      await user.click(preset(name))
      expect([name, min, max].map((x) => (typeof x === 'string' ? x : (x as HTMLInputElement).value))).toEqual([
        name,
        from,
        to,
      ])
      expect(preset(name)).toHaveAttribute('aria-pressed', 'true')
    }
    expect(preset('Half')).toHaveAttribute('aria-pressed', 'false')

    await user.clear(max)
    await user.type(max, '200')
    expect(preset('Ultra')).toHaveAttribute('aria-pressed', 'false')
  })

  it('needs a place before it takes a radius: a place suggests 50 km, shows the circle and clearing it drops the radius', async () => {
    const { user, form } = await open('user')
    const radius = within(form).getByLabelText('Radius (km)')
    expect(radius).toBeDisabled()
    expect(radius).toHaveAccessibleDescription('Choose a place first.')

    await user.click(within(form).getByRole('button', { name: 'Pick on map' }))
    const dialog = await screen.findByRole('dialog', { name: 'Choose a location' })
    await user.click(await within(dialog).findByRole('button', { name: 'Pick here' }))
    await user.click(within(dialog).getByRole('button', { name: 'Use this location' }))

    await waitFor(() => expect(radius).toBeEnabled())
    expect(radius).toHaveValue(50)
    expect(within(form).getByLabelText('Radius slider (km)')).toBeEnabled()
    expect(await within(form).findByTestId('fake-map')).toHaveAttribute('data-area', '50.3,18.7,50000')

    await user.clear(radius)
    await user.type(radius, '120')
    expect(within(form).getByTestId('fake-map')).toHaveAttribute('data-area', '50.3,18.7,120000')

    await user.click(within(form).getByRole('button', { name: 'Clear location' }))
    expect(radius).toBeDisabled()
    expect(radius).toHaveValue(null)
    expect(within(form).queryByTestId('fake-map')).not.toBeInTheDocument()
  })

  it('moves the radius with the slider', async () => {
    const { form } = await open('guest')
    const slider = within(form).getByLabelText('Radius slider (km)')
    const { fireEvent } = await import('@testing-library/react')
    fireEvent.change(slider, { target: { value: '150' } })
    expect(within(form).getByLabelText('Radius (km)')).toHaveValue(150)
    expect(await within(form).findByTestId('fake-map')).toHaveAttribute('data-area', '50.2945,18.6714,150000')
  })
})

describe('Race interests page: validation', () => {
  it('blocks saving without a kind and says why', async () => {
    const requests = recordRequests()
    const { user, form } = await open('user')
    await user.click(checkbox(form, 'Road'))
    await user.click(checkbox(form, 'Trail'))
    await user.click(within(form).getByRole('button', { name: 'Save interests' }))

    expect(await within(form).findByText('Select at least one kind of race.')).toBeInTheDocument()
    expect(puts(requests)).toHaveLength(0)
  })

  it('requires a radius for a chosen place', async () => {
    const requests = recordRequests()
    const { user, form } = await open('guest')
    const radius = within(form).getByLabelText('Radius (km)')
    await user.clear(radius)
    await user.click(within(form).getByRole('button', { name: 'Save interests' }))

    expect(await within(form).findByText('Enter a radius for the chosen place.')).toBeInTheDocument()
    expect(radius).toHaveAttribute('aria-invalid', 'true')
    expect(puts(requests)).toHaveLength(0)

    await user.type(radius, '301')
    expect(await within(form).findByText('Enter a radius from 5 to 300 km.')).toBeInTheDocument()
  })

  it('checks the distance range and its order', async () => {
    const requests = recordRequests()
    const { user, form } = await open('user')
    const min = within(form).getByLabelText('From (km)')
    const max = within(form).getByLabelText('To (km)')
    await user.clear(min)
    await user.type(min, '30')
    await user.clear(max)
    await user.type(max, '10')
    await user.click(within(form).getByRole('button', { name: 'Save interests' }))
    expect(await within(form).findByText('The longest distance must not be shorter than the shortest.')).toBeVisible()

    await user.clear(max)
    await user.type(max, '251')
    expect(await within(form).findByText('Enter a distance from 1 to 250 km.')).toBeVisible()
    expect(puts(requests)).toHaveLength(0)
  })

  it('checks the keywords', async () => {
    const requests = recordRequests()
    const { user, form } = await open('user')
    await user.type(within(form).getByLabelText('Keywords (optional)'), 'aa bb cc dd ee ff')
    await user.click(within(form).getByRole('button', { name: 'Save interests' }))

    expect(await within(form).findByText('Use at most 5 keywords.')).toBeInTheDocument()
    expect(puts(requests)).toHaveLength(0)
    await waitFor(() => expect(within(form).getByLabelText('Keywords (optional)')).toHaveFocus())
  })

  it('moves focus to the first kind checkbox when the kinds are the problem', async () => {
    const { user, form } = await open('user')
    await user.click(checkbox(form, 'Road'))
    await user.click(checkbox(form, 'Trail'))
    await user.click(within(form).getByRole('button', { name: 'Save interests' }))

    await waitFor(() => expect(checkbox(form, 'Road')).toHaveFocus())
  })
})

describe('Race interests page: saving', () => {
  it('saves the edited profile in SI units, confirms with a toast and keeps the saved values', async () => {
    const requests = recordRequests()
    const { user, form, token } = await open('user')

    await user.click(checkbox(form, 'Ultra'))
    await user.click(checkbox(form, 'Trail'))
    await user.click(checkbox(form, 'Silesian (Śląskie)'))
    await user.click(checkbox(form, 'Lesser Poland (Małopolskie)'))
    await user.click(within(form).getByRole('button', { name: 'Marathon' }))
    await user.selectOptions(within(form).getByLabelText('Look ahead'), '180')
    await user.type(within(form).getByLabelText('Keywords (optional)'), '  night   run ')
    await user.click(within(form).getByRole('switch', { name: 'Show proposals in the diary' }))
    await user.click(within(form).getByRole('button', { name: 'Pick on map' }))
    const dialog = await screen.findByRole('dialog', { name: 'Choose a location' })
    await user.click(await within(dialog).findByRole('button', { name: 'Pick here' }))
    await user.click(within(dialog).getByRole('button', { name: 'Use this location' }))
    await user.type(await within(form).findByLabelText('Place name'), 'Katowice')
    await user.click(within(form).getByRole('button', { name: 'Save interests' }))

    expect(await savedToast()).not.toHaveLength(0)
    expect(puts(requests)).toHaveLength(1)
    expect(puts(requests)[0]!.body).toEqual({
      kinds: ['Road', 'Ultra'],
      regionCodes: ['PL-12', 'PL-24'],
      center: { latitude: 50.3, longitude: 18.7, name: 'Katowice' },
      radiusMeters: 50_000,
      minDistanceMeters: 42_000,
      maxDistanceMeters: 42_500,
      horizonDays: 180,
      keywords: 'night run',
      showInCalendar: false,
    })
    expect(getFakeApi().raceInterestsOf(token)).toMatchObject({ configured: true, horizonDays: 180 })
    // The notice for an unsaved profile is gone and the form is clean again.
    expect(screen.queryByText(/You have not saved any interests yet/)).not.toBeInTheDocument()
    expect(within(form).getByRole('button', { name: 'Discard changes' })).toBeDisabled()
    expect(within(form).getByLabelText('Keywords (optional)')).toHaveValue('night run')
  })

  it('saves the untouched defaults of a new user, so proposals can start', async () => {
    const requests = recordRequests()
    const { user, form, token } = await open('user')
    await user.click(within(form).getByRole('button', { name: 'Save interests' }))

    expect(await savedToast()).not.toHaveLength(0)
    expect(puts(requests)[0]!.body).toMatchObject({
      kinds: ['Road', 'Trail'],
      regionCodes: [],
      center: null,
      radiusMeters: null,
      minDistanceMeters: 5000,
      maxDistanceMeters: 42_195,
      horizonDays: 120,
      keywords: null,
    })
    expect(getFakeApi().raceInterestsOf(token)?.configured).toBe(true)
  })

  it('discards edits back to the saved values', async () => {
    const { user, form } = await open('user')
    await user.click(checkbox(form, 'Obstacle course'))
    expect(checkbox(form, 'Obstacle course')).toBeChecked()
    await user.click(within(form).getByRole('button', { name: 'Discard changes' }))
    expect(checkbox(form, 'Obstacle course')).not.toBeChecked()
  })

  it('invalidates the proposals and serves the saved interests from the cache afterwards', async () => {
    const { user, form, queryClient } = await open('user')
    queryClient.setQueryData(['get', '/api/races/proposals', { params: { query: { from: 'a', to: 'b' } } }], {
      items: [],
    })
    await user.click(within(form).getByRole('button', { name: 'Save interests' }))
    await savedToast()

    const state = queryClient.getQueryState([
      'get',
      '/api/races/proposals',
      { params: { query: { from: 'a', to: 'b' } } },
    ])
    expect(state?.isInvalidated).toBe(true)
    const cached = queryClient.getQueryData(['get', '/api/races/interests', {}]) as { configured: boolean }
    expect(cached.configured).toBe(true)
    expect(getFakeApi().requests.filter((request) => request === 'GET /api/races/interests')).toHaveLength(1)
  })
})

describe('Race interests page: server errors (ADR 0018)', () => {
  it('puts each server error on its field and focuses the first one', async () => {
    respondOnce('PUT', '/api/races/interests', 400, {
      title: 'One or more validation errors occurred.',
      errors: {
        kinds: ['Server: pick a kind.'],
        regionCodes: ['Server: bad region.'],
        center: ['Server: place needs radius.'],
        centerName: ['Server: name too long.'],
        radiusMeters: ['Server: bad radius.'],
        minDistanceMeters: ['Server: bad min.'],
        maxDistanceMeters: ['Server: bad max.'],
        horizonDays: ['Server: bad horizon.'],
        keywords: ['Server: bad keywords.'],
      },
    })
    const { user, form } = await open('guest')
    await user.click(within(form).getByRole('button', { name: 'Save interests' }))

    for (const text of [
      'Server: pick a kind.',
      'Server: bad region.',
      'Server: place needs radius.',
      'Server: name too long.',
      'Server: bad radius.',
      'Server: bad min.',
      'Server: bad max.',
      'Server: bad horizon.',
      'Server: bad keywords.',
    ]) {
      expect(await within(form).findByText(text)).toBeInTheDocument()
    }
    expect(within(form).getByLabelText('Radius (km)')).toHaveAccessibleDescription(/Server: bad radius\./)
    expect(within(form).getByLabelText('From (km)')).toHaveAttribute('aria-invalid', 'true')
    expect(within(form).getByLabelText('Keywords (optional)')).toHaveAttribute('aria-invalid', 'true')
    // Everything mapped onto fields, so there is no extra summary.
    expect(within(form).queryByText('Some values are not valid. Check the highlighted fields.')).not.toBeInTheDocument()
    expect(within(form).getByRole('button', { name: 'Save interests' })).toBeEnabled()
    await waitFor(() => expect(checkbox(form, 'Road')).toHaveFocus())
  })

  it('shows errors on unknown fields in a summary that takes the focus', async () => {
    respondOnce('PUT', '/api/races/interests', 400, {
      errors: { showInCalendar: ['Server: required.'], body: ['Server: malformed.'] },
    })
    const { user, form } = await open('user')
    await user.click(within(form).getByRole('button', { name: 'Save interests' }))

    const alert = (await within(form).findByText(/Some values are not valid\./)).closest(
      '[role="alert"]',
    ) as HTMLElement
    expect(alert).toHaveTextContent('Some values are not valid.')
    expect(alert).toHaveTextContent('Server: required.')
    expect(alert).toHaveTextContent('Server: malformed.')
    await waitFor(() => expect(alert).toHaveFocus())
  })

  it('explains a failed save that is not a validation problem and keeps the edits', async () => {
    respondOnce('PUT', '/api/races/interests', 503, {})
    const { user, form } = await open('user')
    await user.click(checkbox(form, 'Obstacle course'))
    await user.click(within(form).getByRole('button', { name: 'Save interests' }))

    expect(await within(form).findByText(/The server is not reachable right now/)).toBeInTheDocument()
    expect(checkbox(form, 'Obstacle course')).toBeChecked()
  })
})

describe('Race interests page: availability', () => {
  it('says proposals are not available when the feature is off, without calling the interests endpoint', async () => {
    resetFakeApi({ racesEnabled: false })
    signInForTest('user')
    renderRoute(PATH, { session: 'none' })

    expect(await screen.findByText('Race proposals are not available right now.')).toBeInTheDocument()
    expect(getFakeApi().requests).not.toContain('GET /api/races/interests')
  })

  it('offers a retry when the interests cannot be loaded', async () => {
    respondOnce('GET', '/api/races/interests', 500, {})
    resetFakeApi()
    signInForTest('user')
    const user = userEvent.setup()
    renderRoute(PATH, { session: 'none' })

    const alert = await screen.findByRole('alert')
    await user.click(within(alert).getByRole('button', { name: 'Try again' }))
    expect(await screen.findByRole('form', { name: 'Race interests' })).toBeInTheDocument()
  })
})
