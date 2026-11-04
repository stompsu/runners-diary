import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { supportsWebGl2 } from '@/components/map/webgl'
import { getFakeApi, resetFakeApi } from '@/test/fake'
import { faults, typeId, type RunWriteRequest } from '@/test/fakeApi'
import { resetFakeMap } from '@/test/fakeMapState'
import { renderRoute } from '@/test/render'
import { recordRequests, storedRuns } from '@/test/requests'

// The picker is a lazy chunk with the map view; load it during collection (no test timeout applies).
await import('./LocationPickerDialog')

vi.mock('@/components/map/webgl', () => ({ supportsWebGl2: vi.fn<() => boolean>(() => true) }))
vi.mock('@/components/map/maplibre/MapLibreMap', () => import('@/test/fakeMapProvider'))

beforeEach(() => {
  resetFakeMap()
  vi.mocked(supportsWebGl2).mockReturnValue(true)
})

const loaded = (dialog: HTMLElement) =>
  waitFor(() => expect(dialog.querySelector('[data-slot="skeleton"]')).not.toBeInTheDocument())

async function openNew() {
  const user = userEvent.setup()
  renderRoute('/diary/2026/11/runs/new?date=2026-11-20')
  const form = await screen.findByRole('dialog', { name: 'Add run' })
  await within(form).findByLabelText('Title')
  return { user, form }
}

const WITH_PLACE: RunWriteRequest = {
  date: '2026-11-02',
  status: 'Completed',
  runTypeId: typeId('easy'),
  title: 'Easy morning',
  location: { latitude: 50.294862123, longitude: 18.676642987, name: 'Gliwice' },
}

async function openEdit(seed: RunWriteRequest = WITH_PLACE, options: { geocoding?: boolean } = {}) {
  resetFakeApi({ guestSeed: [seed], geocodingEnabled: options.geocoding })
  const user = userEvent.setup()
  const result = renderRoute('/diary/2026/11/day/2026-11-02')
  const day = await screen.findByRole('dialog', { name: 'Monday, November 2, 2026' })
  await loaded(day)
  await user.click(within(day).getByRole('link', { name: 'Edit' }))
  const form = await screen.findByRole('dialog', { name: 'Edit run' })
  await loaded(form)
  await within(form).findByLabelText('Title')
  return { user, form, ...result }
}

const picker = () => screen.findByRole('dialog', { name: 'Choose a location' })

describe('Location in the run form', () => {
  it('starts without a location and offers the map and no name field', async () => {
    const { form } = await openNew()
    const group = within(form).getByRole('group', { name: 'Location' })
    expect(within(group).getByText('No location set.')).toBeInTheDocument()
    expect(within(group).getByRole('button', { name: 'Pick on map' })).toBeInTheDocument()
    expect(within(group).queryByLabelText('Place name')).not.toBeInTheDocument()
    expect(within(group).queryByRole('button', { name: 'Clear location' })).not.toBeInTheDocument()
  })

  it('picking on the map fills the fields, then Use this location puts the pin into the form and the request', async () => {
    const requests = recordRequests()
    const { user, form } = await openNew()
    await user.click(within(form).getByRole('button', { name: 'Pick on map' }))
    const dialog = await picker()

    expect(within(dialog).getByRole('region', { name: 'Map for choosing a location' })).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Use this location' })).toBeDisabled()
    expect(within(dialog).getByRole('status')).toHaveTextContent('No position selected yet.')

    await user.click(await within(dialog).findByRole('button', { name: 'Pick here' }))
    expect(within(dialog).getByLabelText('Latitude')).toHaveValue('50.3')
    expect(within(dialog).getByLabelText('Longitude')).toHaveValue('18.7')
    expect(within(dialog).getByTestId('fake-map')).toHaveAttribute('data-marker', '50.3,18.7')
    expect(within(dialog).getByRole('status')).toHaveTextContent('Selected position: 50.3, 18.7')

    await user.click(within(dialog).getByRole('button', { name: 'Use this location' }))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Choose a location' })).not.toBeInTheDocument())
    expect(within(form).getByTestId('location-coordinates')).toHaveTextContent('50.30000, 18.70000')
    await user.type(within(form).getByLabelText('Place name'), 'Meeting point')
    await user.click(within(form).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(storedRuns().filter((run) => run.date === '2026-11-20')).toHaveLength(1))
    const post = requests.find((r) => r.method === 'POST' && r.path === '/api/runs')!
    expect(post.body).toMatchObject({ location: { latitude: 50.3, longitude: 18.7, name: 'Meeting point' } })
  })

  it('has a keyboard alternative: latitude and longitude fields, validated, confirmed with Enter', async () => {
    const { user, form } = await openNew()
    await user.click(within(form).getByRole('button', { name: 'Pick on map' }))
    const dialog = await picker()
    const lat = within(dialog).getByLabelText('Latitude')
    const lon = within(dialog).getByLabelText('Longitude')
    const confirm = within(dialog).getByRole('button', { name: 'Use this location' })

    await user.type(lat, '91')
    expect(lat).toBeInvalid()
    expect(lat).toHaveAccessibleDescription('Latitude must be a number from -90 to 90.')
    await user.type(lon, '18,6714') // a comma is fine
    expect(lon).toBeValid()
    expect(confirm).toBeDisabled()

    await user.clear(lat)
    await user.type(lat, '50.2945')
    expect(lat).toBeValid()
    expect(confirm).toBeEnabled()
    expect(within(dialog).getByTestId('fake-map')).toHaveAttribute('data-marker', '50.2945,18.6714')

    await user.clear(lon)
    await user.type(lon, '181')
    expect(lon).toHaveAccessibleDescription('Longitude must be a number from -180 to 180.')
    expect(confirm).toBeDisabled()
    await user.clear(lon)
    await user.type(lon, '-18.5')
    await user.type(lon, '{Enter}')

    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Choose a location' })).not.toBeInTheDocument())
    expect(within(form).getByTestId('location-coordinates')).toHaveTextContent('50.29450, -18.50000')
    // Enter in the picker did not submit the run form behind it.
    expect(storedRuns().filter((run) => run.date === '2026-11-20')).toHaveLength(0)
    // The focus is back on the button that opened the picker.
    expect(within(form).getByRole('button', { name: 'Change on map' })).toHaveFocus()
  })

  it('rejects text that is not a number', async () => {
    const { user, form } = await openNew()
    await user.click(within(form).getByRole('button', { name: 'Pick on map' }))
    const dialog = await picker()
    await user.type(within(dialog).getByLabelText('Latitude'), '50N')
    await user.type(within(dialog).getByLabelText('Longitude'), '18')
    expect(within(dialog).getByLabelText('Latitude')).toBeInvalid()
    expect(within(dialog).getByRole('button', { name: 'Use this location' })).toBeDisabled()
  })

  it('Cancel leaves the form as it was and returns the focus', async () => {
    const { user, form } = await openNew()
    await user.click(within(form).getByRole('button', { name: 'Pick on map' }))
    const dialog = await picker()
    await user.click(await within(dialog).findByRole('button', { name: 'Pick here' }))
    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Choose a location' })).not.toBeInTheDocument())
    expect(within(form).getByText('No location set.')).toBeInTheDocument()
    expect(within(form).getByRole('button', { name: 'Pick on map' })).toHaveFocus()
  })

  it('opens at the existing pin; a later picker opens where the last one ended', async () => {
    const { user, form } = await openEdit()
    await user.click(within(form).getByRole('button', { name: 'Change on map' }))
    const dialog = await picker()
    expect(within(dialog).getByTestId('fake-map')).toHaveAttribute('data-marker', '50.294862,18.676643')
    expect(within(dialog).getByLabelText('Latitude')).toHaveValue('50.294862')
    expect(within(dialog).getByTestId('fake-map')).toHaveAttribute('data-center', '50.294862123,18.676642987')
  })

  it('opens a picker for a run without a pin at the last place picked in this tab', async () => {
    sessionStorage.setItem('rd.lastMapPick', JSON.stringify({ latitude: 49.5, longitude: 19.5 }))
    const { user, form } = await openNew()
    await user.click(within(form).getByRole('button', { name: 'Pick on map' }))
    const dialog = await picker()
    expect(await within(dialog).findByTestId('fake-map')).toHaveAttribute('data-center', '49.5,19.5')
    expect(within(dialog).getByTestId('fake-map')).toHaveAttribute('data-marker', '')
  })

  it('remembers a confirmed pick for the next picker', async () => {
    const { user, form } = await openNew()
    await user.click(within(form).getByRole('button', { name: 'Pick on map' }))
    let dialog = await picker()
    await user.click(await within(dialog).findByRole('button', { name: 'Pick here' }))
    await user.click(within(dialog).getByRole('button', { name: 'Use this location' }))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Choose a location' })).not.toBeInTheDocument())
    expect(JSON.parse(sessionStorage.getItem('rd.lastMapPick')!)).toEqual({ latitude: 50.3, longitude: 18.7 })
    await user.click(within(form).getByRole('button', { name: 'Clear location' }))
    await user.click(within(form).getByRole('button', { name: 'Pick on map' }))
    dialog = await picker()
    expect(await within(dialog).findByTestId('fake-map')).toHaveAttribute('data-center', '50.3,18.7')
  })

  it('works without WebGL: the map is replaced by a notice and the coordinates can still be typed', async () => {
    vi.mocked(supportsWebGl2).mockReturnValue(false)
    const { user, form } = await openNew()
    await user.click(within(form).getByRole('button', { name: 'Pick on map' }))
    const dialog = await picker()
    expect(within(dialog).getByRole('region', { name: 'Map for choosing a location' })).toHaveAttribute(
      'data-map-state',
      'unavailable',
    )
    await user.type(within(dialog).getByLabelText('Latitude'), '50.1')
    await user.type(within(dialog).getByLabelText('Longitude'), '18.1')
    await user.click(within(dialog).getByRole('button', { name: 'Use this location' }))
    expect(await within(form).findByTestId('location-coordinates')).toHaveTextContent('50.10000, 18.10000')
  })

  it('is a bottom sheet on a phone', async () => {
    const { setPhoneViewport } = await import('@/test/viewport')
    const { user, form } = await openNew()
    setPhoneViewport(true)
    await user.click(within(form).getByRole('button', { name: 'Pick on map' }))
    expect(await picker()).toHaveAttribute('data-variant', 'sheet')
  })
})

describe('editing an existing location', () => {
  it('shows the stored place, and saving another field sends the exact coordinates back', async () => {
    const requests = recordRequests()
    const { user, form } = await openEdit()
    expect(within(form).getByTestId('location-coordinates')).toHaveTextContent('50.29486, 18.67664')
    expect(within(form).getByLabelText('Place name')).toHaveValue('Gliwice')

    await user.clear(within(form).getByLabelText('Title'))
    await user.type(within(form).getByLabelText('Title'), 'Renamed')
    await user.click(within(form).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(requests.some((r) => r.method === 'PUT')).toBe(true))
    expect(requests.find((r) => r.method === 'PUT')!.body).toMatchObject({
      title: 'Renamed',
      location: { latitude: 50.294862123, longitude: 18.676642987, name: 'Gliwice' },
    })
  })

  it('a new name keeps the pin; a new pick keeps the name', async () => {
    const requests = recordRequests()
    const { user, form } = await openEdit()
    await user.clear(within(form).getByLabelText('Place name'))
    await user.type(within(form).getByLabelText('Place name'), 'Rynek')
    await user.click(within(form).getByRole('button', { name: 'Change on map' }))
    const dialog = await picker()
    await user.click(await within(dialog).findByRole('button', { name: 'Pick here' }))
    await user.click(within(dialog).getByRole('button', { name: 'Use this location' }))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Choose a location' })).not.toBeInTheDocument())
    await user.click(within(form).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(requests.some((r) => r.method === 'PUT')).toBe(true))
    expect(requests.find((r) => r.method === 'PUT')!.body).toMatchObject({
      location: { latitude: 50.3, longitude: 18.7, name: 'Rynek' },
    })
  })

  it('a blank name is saved as no name', async () => {
    const requests = recordRequests()
    const { user, form } = await openEdit()
    await user.clear(within(form).getByLabelText('Place name'))
    await user.click(within(form).getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(requests.some((r) => r.method === 'PUT')).toBe(true))
    expect(requests.find((r) => r.method === 'PUT')!.body).toMatchObject({
      location: { latitude: 50.294862123, longitude: 18.676642987, name: null },
    })
  })

  it('Clear removes the location (sent as null) and returns the focus to the pick button', async () => {
    const requests = recordRequests()
    const { user, form } = await openEdit()
    await user.click(within(form).getByRole('button', { name: 'Clear location' }))
    expect(within(form).getByText('No location set.')).toBeInTheDocument()
    expect(within(form).getByRole('button', { name: 'Pick on map' })).toHaveFocus()
    await user.click(within(form).getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(requests.some((r) => r.method === 'PUT')).toBe(true))
    expect(requests.find((r) => r.method === 'PUT')!.body).toMatchObject({ location: null })
    await waitFor(() => expect(storedRuns()[0]!.location).toBeNull())
  })

  it('a run without a location keeps none when it is saved untouched', async () => {
    const requests = recordRequests()
    const { user, form } = await openEdit({ ...WITH_PLACE, location: undefined })
    await user.click(within(form).getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(requests.some((r) => r.method === 'PUT')).toBe(true))
    expect(requests.find((r) => r.method === 'PUT')!.body).toMatchObject({ location: null })
  })

  it('a server error on the place name lands on its field', async () => {
    const { user, form } = await openEdit()
    getFakeApi().addFault(
      faults.problem(
        { method: 'PUT', path: /\/api\/runs\// },
        400,
        'https://tools.ietf.org/html/rfc9110#section-15.5.1',
        {
          members: { errors: { 'location.name': ['Name is too long.'] } },
          times: 1,
        },
      ),
    )
    await user.click(within(form).getByRole('button', { name: 'Save' }))
    expect(await within(form).findByText('Name is too long.')).toBeInTheDocument()
    expect(within(form).getByLabelText('Place name')).toBeInvalid()
  })
})

describe('place search', () => {
  it('is hidden while geocoding is disabled (client-config maps.geocodingEnabled = false)', async () => {
    const { form } = await openNew()
    expect(within(form).queryByLabelText('Search for a place')).not.toBeInTheDocument()
  })

  it('searches on request, lists the results as buttons and fills the location from the chosen one', async () => {
    const { user, form } = await openEdit({ ...WITH_PLACE, location: undefined }, { geocoding: true })
    const requests = recordRequests()
    const input = await within(form).findByLabelText('Search for a place')

    await user.type(input, 'gliwice')
    // Typing alone searches nothing.
    expect(requests.filter((r) => r.path === '/api/geocoding/search')).toHaveLength(0)
    await user.click(within(form).getByRole('button', { name: 'Search' }))

    const results = await within(form).findByRole('list', { name: 'Search results' })
    expect(within(results).getAllByRole('button')).toHaveLength(2)
    expect(within(form).getByText('2 places found.')).toBeInTheDocument()
    expect(requests.filter((r) => r.path === '/api/geocoding/search')).toHaveLength(1)

    await user.click(within(results).getByRole('button', { name: /Gliwice Rynek/ }))
    expect(within(form).getByTestId('location-coordinates')).toHaveTextContent('50.29450, 18.67140')
    expect(within(form).getByLabelText('Place name')).toHaveValue('Gliwice Rynek')
    expect(within(form).getByLabelText('Place name')).toHaveFocus()
    expect(within(form).queryByRole('list', { name: 'Search results' })).not.toBeInTheDocument()
  })

  it('Enter searches and never submits the run form', async () => {
    const { user, form } = await openEdit(undefined, { geocoding: true })
    const requests = recordRequests()
    await user.type(await within(form).findByLabelText('Search for a place'), 'zabrze{Enter}')
    expect(await within(form).findByRole('list', { name: 'Search results' })).toBeInTheDocument()
    expect(requests.filter((r) => r.method === 'PUT')).toHaveLength(0)
    expect(screen.getByRole('dialog', { name: 'Edit run' })).toBeInTheDocument()
  })

  it('asks for at least two characters and says when nothing was found', async () => {
    const { user, form } = await openEdit(undefined, { geocoding: true })
    const input = await within(form).findByLabelText('Search for a place')
    await user.type(input, 'g{Enter}')
    expect(within(form).getByText('Type at least 2 characters.')).toBeInTheDocument()
    expect(input).toBeInvalid()
    await user.clear(input)
    await user.type(input, 'nowhere{Enter}')
    expect(await within(form).findByText('No places found.')).toBeInTheDocument()
  })

  it('says so when the provider is unavailable (503 feature-unavailable) or the search fails', async () => {
    const { user, form } = await openEdit(undefined, { geocoding: true })
    getFakeApi().addFault(
      faults.problem({ path: '/api/geocoding/search' }, 503, 'urn:runnersdiary:problem:feature-unavailable', {
        times: 1,
      }),
    )
    const input = await within(form).findByLabelText('Search for a place')
    await user.type(input, 'gliwice{Enter}')
    expect(await within(form).findByText('Place search is not available right now.')).toBeInTheDocument()

    getFakeApi().addFault(faults.rateLimited({ path: '/api/geocoding/search' }, 20, 1))
    await user.clear(input)
    await user.type(input, 'katowice{Enter}')
    expect(await within(form).findByText(/try again in 20 s/)).toBeInTheDocument()
  })
})
