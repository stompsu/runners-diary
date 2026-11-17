import { act, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it } from 'vitest'
import { setLanguage } from '@/i18n/language'
import { getFakeApi, resetFakeApi } from '@/test/fake'
import { typeId, type RunWriteRequest } from '@/test/fakeApi'
import { renderRoute, signInForTest } from '@/test/render'
import { stubResizeObserver } from '@/test/resizeObserver'
import '@/i18n/lazyBundles'

// Every key these screens use must exist in pl/goals.json (and runs, calendar, ...): the test setup fails on a missing key.
beforeEach(() => stubResizeObserver())

const HISTORY: RunWriteRequest[] = [
  {
    date: '2026-10-10',
    status: 'Completed',
    runTypeId: typeId('roadRace'),
    distanceMeters: 10_000,
    durationSeconds: 3000,
    race: { name: 'Autumn 10K', distanceCategory: 'TenK', resultTimeSeconds: 3000 },
  },
  { date: '2026-10-15', status: 'Completed', runTypeId: typeId('easy'), distanceMeters: 8000, durationSeconds: 2700 },
  { date: '2026-10-20', status: 'Completed', runTypeId: typeId('easy'), distanceMeters: 8000, durationSeconds: 2700 },
  { date: '2026-10-25', status: 'Completed', runTypeId: typeId('long'), distanceMeters: 16_000, durationSeconds: 5700 },
]

async function open(path: string, seed?: (token: string) => string | void) {
  resetFakeApi({ goalsEnabled: true, guestSeed: HISTORY })
  const token = signInForTest('guest')
  const goalId = seed?.(token)
  await act(() => setLanguage('pl'))
  const user = userEvent.setup()
  renderRoute(goalId ? path.replace(':id', goalId) : path, { session: 'none' })
  return { user, token }
}

describe('Goals in Polish', () => {
  it('shows the empty state and the Polish list', async () => {
    await open('/goals')
    expect(await screen.findByRole('heading', { level: 1, name: 'Cele' })).toBeInTheDocument()
    expect(await screen.findByText(/Nie masz jeszcze żadnych celów/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Nowy cel' })).toHaveAttribute('href', '/goals/new')
  })

  it('groups goals by status with Polish labels and a days-to-go plural', async () => {
    await open('/goals', (token) => {
      getFakeApi().addGoal(token, { title: 'Blisko 10K', targetDate: '2026-12-20' })
      getFakeApi().addGoal(token, { title: 'Daleko maraton', targetDate: '2027-04-18' })
    })
    const region = await screen.findByRole('region', { name: 'Aktywne cele' })
    expect(within(region).getByText('Blisko 10K')).toBeInTheDocument()
    expect(within(region).getAllByText('Brak planu').length).toBe(2)
    expect(within(region).getByText('Krótkoterminowy')).toBeInTheDocument()
  })

  it('opens the goal detail with the plan section and the Polish generate flow', async () => {
    const { user } = await open(
      '/goals/:id',
      (token) => getFakeApi().addGoal(token, { title: 'Jesienna dycha', targetDate: '2026-12-20' }).id,
    )

    expect(await screen.findByRole('heading', { level: 1, name: /Jesienna dycha/ })).toBeInTheDocument()
    expect(await screen.findByText('Sugerowane treningi')).toBeInTheDocument()
    await user.click(await screen.findByRole('button', { name: 'Wygeneruj sugestie' }))
    expect((await screen.findAllByText('Sugestie gotowe.')).length).toBeGreaterThan(0)
    expect(screen.getByRole('button', { name: 'Wygeneruj ponownie' })).toBeInTheDocument()
    expect(screen.getAllByText('Sugestia').length).toBeGreaterThan(0)
    expect(screen.getAllByRole('button', { name: /Akceptuj/ }).length).toBeGreaterThan(0)
  })

  it('renders the goal form with Polish labels, a comma decimal for presets and the generic estimate hint', async () => {
    const { user } = await open('/goals/new')

    expect(await screen.findByRole('heading', { level: 1, name: 'Nowy cel' })).toBeInTheDocument()
    await user.click(screen.getByRole('radio', { name: 'Półmaraton' }))
    expect(screen.getByRole('radio', { name: 'Półmaraton' })).toBeChecked()
    expect(screen.getByLabelText('Dystans (km)')).toHaveValue('21,098')
    expect(screen.getByLabelText('Tytuł')).toHaveValue('Półmaraton')
    expect(await screen.findByText(/Realny czas to około/)).toBeInTheDocument()
    expect(
      within(screen.getByRole('form', { name: 'Nowy cel' })).getByRole('button', { name: 'Zapisz cel' }),
    ).toBeEnabled()
  })
})
