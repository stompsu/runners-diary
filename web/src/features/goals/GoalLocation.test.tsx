import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { supportsWebGl2 } from '@/components/map/webgl'
import { getFakeApi, resetFakeApi } from '@/test/fake'
import { resetFakeMap } from '@/test/fakeMapState'
import { renderRoute, signInForTest } from '@/test/render'
import { recordRequests } from '@/test/requests'
import { stubResizeObserver } from '@/test/resizeObserver'
import '@/i18n/lazyBundles'

// The picker is a lazy chunk with the map view; load it during collection (no test timeout applies).
await import('@/features/runs/LocationPickerDialog')

vi.mock('@/components/map/webgl', () => ({ supportsWebGl2: vi.fn<() => boolean>(() => true) }))
vi.mock('@/components/map/maplibre/MapLibreMap', () => import('@/test/fakeMapProvider'))

beforeEach(() => {
  stubResizeObserver()
  resetFakeMap()
  vi.mocked(supportsWebGl2).mockReturnValue(true)
})

async function open(path: string) {
  resetFakeApi({ goalsEnabled: true })
  const token = signInForTest('guest')
  const user = userEvent.setup()
  const result = renderRoute(path, { session: 'none' })
  return { user, token, ...result }
}

async function pickHere(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: 'Pick on map' }))
  const dialog = await screen.findByRole('dialog', { name: 'Choose a location' })
  await user.click(await within(dialog).findByRole('button', { name: 'Pick here' }))
  await user.click(within(dialog).getByRole('button', { name: 'Use this location' }))
  await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Choose a location' })).not.toBeInTheDocument())
}

describe('Goal location', () => {
  it('creates a goal with a location, shows it on the card and the detail, and sends null without one', async () => {
    const { user, token } = await open('/goals/new')
    const requests = recordRequests()
    await user.type(await screen.findByLabelText('Target time (h:mm:ss)'), '0:55:00')
    expect(screen.getByText('No location set.')).toBeInTheDocument()
    await pickHere(user)
    expect(screen.getByTestId('location-coordinates')).toHaveTextContent('50.30000, 18.70000')
    await user.type(screen.getByLabelText('Place name'), 'Gliwice, Nowe Gliwice')
    await user.click(screen.getByRole('button', { name: 'Save goal' }))

    await waitFor(() => expect(getFakeApi().goalsOf(token)).toHaveLength(1))
    const post = requests.find((r) => r.method === 'POST' && r.path === '/api/goals')!
    expect(post.body).toMatchObject({ location: { latitude: 50.3, longitude: 18.7, name: 'Gliwice, Nowe Gliwice' } })
    expect(await screen.findByRole('heading', { level: 1, name: '10K' })).toBeInTheDocument()
    expect(screen.getByText(/Gliwice, Nowe Gliwice/)).toBeInTheDocument()
    expect(screen.getByTestId('goal-location-coordinates')).toHaveTextContent('50.30000, 18.70000')
  })

  it('edits and clears the location of an existing goal', async () => {
    const { user, token, router } = await open('/goals')
    const goal = getFakeApi().addGoal(token, {
      title: 'Autumn 10K',
      location: { latitude: 50.29, longitude: 18.67, name: 'Gliwice' },
    })
    expect(await screen.findByText('Gliwice')).toBeInTheDocument() // the card
    void router.navigate(`/goals/${goal.id}/edit`)
    await screen.findByRole('heading', { level: 1, name: 'Edit goal' })
    expect(screen.getByTestId('location-coordinates')).toHaveTextContent('50.29000, 18.67000')
    const name = screen.getByLabelText('Place name')
    expect(name).toHaveValue('Gliwice')

    await user.clear(name)
    await user.type(name, 'Start line')
    await user.click(screen.getByRole('button', { name: 'Save goal' }))
    await waitFor(() => expect(getFakeApi().goalsOf(token)[0]?.location?.name).toBe('Start line'))
    // The save navigates to the detail page; wait for it so it cannot override the next navigation.
    expect(await screen.findByText(/Start line/)).toBeInTheDocument()

    void router.navigate(`/goals/${goal.id}/edit`)
    await user.click(await screen.findByRole('button', { name: 'Clear location' }))
    expect(screen.getByText('No location set.')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Save goal' }))
    await waitFor(() => expect(getFakeApi().goalsOf(token)[0]?.location).toBeNull())
    await screen.findByRole('heading', { level: 1, name: 'Autumn 10K' })
    expect(screen.queryByTestId('goal-location-coordinates')).toBeNull()
  })
})
