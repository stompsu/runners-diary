import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { getFakeApi, resetFakeApi } from '@/test/fake'
import { faults, PLAN_PROBLEMS, typeId, type RunWriteRequest } from '@/test/fakeApi'
import { renderRoute, signInForTest } from '@/test/render'
import { recordRequests } from '@/test/requests'
import { stubResizeObserver } from '@/test/resizeObserver'
import '@/i18n/lazyBundles'
import { CANCEL_REFRESH_DELAY_MS } from './PlanSection'

beforeEach(() => stubResizeObserver())

// Default fake: today 2026-11-03, a guest. History: a completed 10K race (50:00) and three easy runs.
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

async function open(path: string, options: Parameters<typeof resetFakeApi>[0] = {}) {
  resetFakeApi({ goalsEnabled: true, guestSeed: HISTORY, ...options })
  const token = signInForTest('guest')
  const user = userEvent.setup()
  const result = renderRoute(path, { session: 'none' })
  return { user, token, ...result }
}

const generateButton = () => screen.findByRole('button', { name: /^(Generate suggestions|Regenerate)$/ })
const alert = () => screen.findByRole('alert')

describe('Goals list', () => {
  it('shows the empty state and the New goal link', async () => {
    await open('/goals')
    expect(await screen.findByRole('heading', { level: 1, name: 'Goals' })).toBeInTheDocument()
    expect(await screen.findByText(/You have no goals yet/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'New goal' })).toHaveAttribute('href', '/goals/new')
  })

  it('groups goals by status and derives the short- or long-term label', async () => {
    const { token } = await open('/goals')
    const fake = getFakeApi()
    fake.addGoal(token, { title: 'Near 10K', targetDate: '2026-12-20' })
    fake.addGoal(token, { title: 'Far half', targetDate: '2027-06-20', distanceMeters: 21_098 })
    fake.addGoal(token, { title: 'Old one', status: 'Achieved' })
    // The list was already loaded empty: a fresh render reads the goals.
    const { unmount } = renderRoute('/goals', { session: 'none' })
    const active = await screen.findAllByRole('region', { name: 'Active goals' })
    const near = within(active[0]!).getByText('Near 10K').closest('li')!
    expect(within(near).getByText('Short-term')).toBeInTheDocument()
    const far = within(active[0]!).getByText('Far half').closest('li')!
    expect(within(far).getByText('Long-term')).toBeInTheDocument()
    expect(within(far).getByText('No plan yet')).toBeInTheDocument()
    expect(screen.getAllByRole('region', { name: 'Achieved goals' }).length).toBeGreaterThan(0)
    unmount()
  })

  it('says so when the feature is off', async () => {
    await open('/goals', { goalsEnabled: false })
    expect(await screen.findByText('Goals are not available right now.')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'New goal' })).toBeNull()
  })
})

describe('Goal form', () => {
  it('creates a goal from a preset with the realistic-target hint and Use as target', async () => {
    const { user, token } = await open('/goals/new')
    const requests = recordRequests()
    expect(await screen.findByRole('heading', { level: 1, name: 'New goal' })).toBeInTheDocument()

    await user.click(screen.getByRole('radio', { name: 'Half marathon' }))
    expect(screen.getByLabelText('Distance (km)')).toHaveValue('21.098')
    expect(screen.getByLabelText('Title')).toHaveValue('Half marathon')
    await user.click(screen.getByRole('radio', { name: '10K' }))
    expect(screen.getByLabelText('Title')).toHaveValue('10K')

    const hint = await screen.findByTestId('estimate-hint')
    await waitFor(() => expect(hint).toHaveTextContent(/about 0:50:00 looks realistic/))
    await user.click(within(hint).getByRole('button', { name: 'Use as target' }))
    expect(screen.getByLabelText('Target time (h:mm:ss)')).toHaveValue('0:50:00')

    await user.click(screen.getByRole('button', { name: 'Save goal' }))
    await waitFor(() => expect(getFakeApi().goalsOf(token)).toHaveLength(1))
    expect(getFakeApi().goalsOf(token)[0]).toMatchObject({
      type: 'Performance',
      distanceMeters: 10_000,
      targetDurationSeconds: 3000,
      title: '10K',
    })
    const post = requests.find((r) => r.method === 'POST' && r.path === '/api/goals')
    expect(post?.body).toMatchObject({ distanceMeters: 10_000, targetDurationSeconds: 3000, status: 'Active' })
    expect(await screen.findByRole('heading', { level: 1, name: '10K' })).toBeInTheDocument()
  })

  it('shows warnings politely and never blocks saving', async () => {
    const { user, token } = await open('/goals/new')
    await user.clear(await screen.findByLabelText('Target time (h:mm:ss)'))
    await user.type(screen.getByLabelText('Target time (h:mm:ss)'), '0:40:00')
    const hint = await screen.findByTestId('estimate-hint')
    await waitFor(() => expect(hint).toHaveTextContent(/faster than your recent form/))
    await user.click(screen.getByRole('button', { name: 'Save goal' }))
    await waitFor(() => expect(getFakeApi().goalsOf(token)).toHaveLength(1))
  })

  it('validates like the server and keeps the form on a client error', async () => {
    const { user, token } = await open('/goals/new')
    fireEvent.change(await screen.findByLabelText('Target date'), { target: { value: '2026-11-05' } })
    await user.click(screen.getByRole('button', { name: 'Save goal' }))
    expect(await screen.findByText('The target date must be at least 7 days from today.')).toBeInTheDocument()
    expect(screen.getByText('A performance goal needs a target time.')).toBeInTheDocument()
    expect(getFakeApi().goalsOf(token)).toHaveLength(0)
  })

  it('switches type: Race asks for the event name, Distance has no target time', async () => {
    const { user } = await open('/goals/new')
    await user.click(await screen.findByRole('radio', { name: 'Race' }))
    expect(screen.getByLabelText('Event name')).toBeInTheDocument()
    expect(screen.getByLabelText('Linked race run (optional)')).toBeInTheDocument()
    await user.click(screen.getByRole('radio', { name: 'Distance' }))
    expect(screen.queryByLabelText(/Target time/)).toBeNull()
    expect(screen.queryByTestId('estimate-hint')).toBeNull()
  })

  it('maps a server 409 goal-limit to a message', async () => {
    const { user } = await open('/goals/new')
    getFakeApi().addFault(
      faults.problem({ method: 'POST', path: '/api/goals' }, 409, PLAN_PROBLEMS.goalLimit, { times: 1 }),
    )
    await user.type(await screen.findByLabelText('Target time (h:mm:ss)'), '0:55:00')
    await user.click(screen.getByRole('button', { name: 'Save goal' }))
    expect(await alert()).toHaveTextContent(/maximum number of goals/)
  })
})

describe('Goal detail and plan section', () => {
  async function detail(options: Parameters<typeof resetFakeApi>[0] = {}, patch = {}) {
    const ctx = await open('/goals', options)
    const goal = getFakeApi().addGoal(ctx.token, { title: 'Autumn 10K', ...patch })
    ctx.router.navigate(`/goals/${goal.id}`)
    await screen.findByRole('heading', { level: 1, name: goal.title })
    return { ...ctx, goal }
  }

  it('generates suggestions by week, shows the dropped count and then offers Regenerate', async () => {
    const { user, token } = await detail()
    await user.click(await generateButton())
    expect(await screen.findByRole('button', { name: 'Regenerate' })).toBeInTheDocument()
    await screen.findAllByRole('region', { name: /^Week \d+, from/ })
    expect(getFakeApi().suggestionsOf(token).length).toBeGreaterThan(5)
    expect(screen.getByText('AI-generated')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Accept' }).length).toBeGreaterThan(5)
    expect(screen.getAllByRole('link', { name: 'Edit & accept' })[0]).toHaveAttribute(
      'href',
      expect.stringMatching(/^\/diary\/\d{4}\/\d{2}\/suggestions\/.+\/accept$/),
    )
  })

  it('sends the UI language with Generate and shows no language hint when the plan matches it', async () => {
    const { user } = await detail()
    const requests = recordRequests()
    await user.click(await generateButton())
    await screen.findByRole('button', { name: 'Regenerate' })
    expect(requests.find((r) => r.path.endsWith('/plan'))?.body).toEqual({ lang: 'en' })
    expect(screen.queryByTestId('plan-language-hint')).not.toBeInTheDocument()
  })

  it('hints that a plan generated in Polish is not in the current language, and Regenerate asks for English', async () => {
    resetFakeApi({ goalsEnabled: true, guestSeed: HISTORY })
    const token = signInForTest('guest')
    const goal = getFakeApi().addGoal(token, { title: 'Autumn 10K' })
    await getFakeApi().handle(
      new Request(`http://localhost/api/goals/${goal.id}/plan`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
        body: JSON.stringify({ lang: 'pl' }),
      }),
    )
    const user = userEvent.setup()
    renderRoute(`/goals/${goal.id}`, { session: 'none' })
    const hint = await screen.findByTestId('plan-language-hint')
    expect(hint).toHaveTextContent('This plan was generated in Polish. Regenerate to get it in the current language.')
    const requests = recordRequests()
    await user.click(await generateButton())
    await waitFor(() => expect(screen.queryByTestId('plan-language-hint')).not.toBeInTheDocument())
    expect(requests.find((r) => r.path.endsWith('/plan'))?.body).toEqual({ lang: 'en' })
  })

  it('shows a progress state while generating and cancel shows the limit hint and refreshes', async () => {
    const { user, token, goal } = await detail()
    getFakeApi().addFault(faults.delay({ method: 'POST', path: /\/plan$/ }, 200))
    await user.click(await generateButton())
    const progress = await screen.findByTestId('plan-progress')
    expect(progress).toHaveTextContent('This can take up to two minutes.')
    expect(within(progress).getByRole('progressbar', { name: 'Generating suggestions…' })).toBeInTheDocument()
    expect(screen.getByTestId('plan-cancel-hint')).toHaveTextContent(/still counts toward your plan limit/)
    const requests = recordRequests()
    await user.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(screen.queryByTestId('plan-progress')).toBeNull()
    // Goal list/detail and suggestions are refetched, as after a success (a plan may have been committed anyway).
    await waitFor(() => {
      const gets = requests.filter((r) => r.method === 'GET').map((r) => r.path)
      expect(gets).toContain(`/api/goals/${goal.id}`)
      expect(gets).toContain('/api/goals')
      expect(gets).toContain('/api/training-suggestions')
    })
    expect(screen.getAllByText(/Cancelled\. If the plan was already finished/).length).toBeGreaterThan(0)
    // The answer of the abandoned request is ignored.
    await new Promise((resolve) => setTimeout(resolve, 300))
    expect(screen.queryByRole('region', { name: /^Week \d+, from/ })).toBeNull()
    expect(screen.getByRole('button', { name: 'Generate suggestions' })).toBeEnabled()
    expect(token).toBeTruthy()
  })

  describe('the delayed second refresh after Cancel', () => {
    afterEach(() => vi.useRealTimers())

    // Only setTimeout is faked; real time still passes so waitFor and the fake API keep working.
    const fakeTimeouts = () => vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'], shouldAdvanceTime: true })
    const goalGets = (requests: ReturnType<typeof recordRequests>, id: string) =>
      requests.filter((r) => r.method === 'GET' && r.path === `/api/goals/${id}`).length

    it('refreshes again after the delay, once', async () => {
      fakeTimeouts()
      const { user, goal } = await detail()
      getFakeApi().addFault(faults.delay({ method: 'POST', path: /\/plan$/ }, 60_000))
      await user.click(await generateButton())
      await screen.findByTestId('plan-progress')
      const requests = recordRequests()
      await user.click(screen.getByRole('button', { name: 'Cancel' }))
      await waitFor(() => expect(goalGets(requests, goal.id)).toBe(1))

      await vi.advanceTimersByTimeAsync(CANCEL_REFRESH_DELAY_MS - 100)
      expect(goalGets(requests, goal.id)).toBe(1)
      await vi.advanceTimersByTimeAsync(200)
      await waitFor(() => expect(goalGets(requests, goal.id)).toBe(2))
      await vi.advanceTimersByTimeAsync(CANCEL_REFRESH_DELAY_MS * 3)
      expect(goalGets(requests, goal.id)).toBe(2)
    })

    it('a regenerate before the delay drops the pending refresh', async () => {
      fakeTimeouts()
      const { user, goal } = await detail()
      getFakeApi().addFault(faults.delay({ method: 'POST', path: /\/plan$/ }, 60_000, 1))
      await user.click(await generateButton())
      await screen.findByTestId('plan-progress')
      await user.click(screen.getByRole('button', { name: 'Cancel' }))
      await user.click(await generateButton())
      await screen.findByRole('button', { name: 'Regenerate' })
      const requests = recordRequests()
      await vi.advanceTimersByTimeAsync(CANCEL_REFRESH_DELAY_MS * 2)
      expect(goalGets(requests, goal.id)).toBe(0)
    })

    it('leaving the page drops the pending refresh', async () => {
      fakeTimeouts()
      const { user, goal, router } = await detail()
      getFakeApi().addFault(faults.delay({ method: 'POST', path: /\/plan$/ }, 60_000))
      await user.click(await generateButton())
      await screen.findByTestId('plan-progress')
      await user.click(screen.getByRole('button', { name: 'Cancel' }))
      await router.navigate('/goals')
      await screen.findByRole('heading', { level: 1, name: 'Goals' })
      const requests = recordRequests()
      await vi.advanceTimersByTimeAsync(CANCEL_REFRESH_DELAY_MS * 2)
      expect(goalGets(requests, goal.id)).toBe(0)
    })
  })

  it('disables Generate with a reason when plan generation is off', async () => {
    await detail({ planGeneration: false })
    const button = await generateButton()
    expect(button).toBeDisabled()
    expect(screen.getByText('AI suggestions are not available right now.')).toBeInTheDocument()
  })

  it('explains an insufficient history (422)', async () => {
    resetFakeApi({ goalsEnabled: true, guestSeed: [] })
    const token = signInForTest('guest')
    const goal = getFakeApi().addGoal(token, { title: 'Autumn 10K' })
    const user = userEvent.setup()
    renderRoute(`/goals/${goal.id}`, { session: 'none' })
    await user.click(await generateButton())
    expect(await alert()).toHaveTextContent('Log at least 3 runs in the last 12 weeks')
  })

  it.each([
    ['ai-quota', 429, { scope: 'account', limit: 5, resetsAt: '2026-11-04T15:40:00Z' }, /demo session are used up/],
    ['ai-quota', 429, { scope: 'global', limit: 200, resetsAt: null }, /used up for today/],
    ['rate-limited', 429, {}, /Too many requests/],
    ['plan-in-progress', 409, {}, /already being generated/],
    ['concurrency-conflict', 409, {}, /changed while the plan was being made/],
    ['plan-failed', 502, { reason: 'invalidOutput' }, /could not produce a safe plan/],
    ['plan-failed', 502, { reason: 'timeout' }, /took too long/],
    ['plan-failed', 502, { reason: 'provider' }, /AI service had a problem/],
    ['feature-unavailable', 503, {}, /not available right now/],
    ['ai-busy', 503, {}, /AI is busy/],
  ])('shows a friendly message for %s (%i) %j', async (type, status, members, expected) => {
    const { user } = await detail()
    getFakeApi().addFault(
      faults.problem({ method: 'POST', path: /\/plan$/ }, status, `urn:runnersdiary:problem:${type}`, {
        members,
        times: 1,
      }),
    )
    await user.click(await generateButton())
    expect(await alert()).toHaveTextContent(expected)
    expect(screen.getByRole('button', { name: 'Generate suggestions' })).toBeEnabled()
  })

  it('asks before replacing another goal, then retries with replaceOtherGoals', async () => {
    resetFakeApi({ goalsEnabled: true, guestSeed: HISTORY })
    const token = signInForTest('guest')
    const goal = getFakeApi().addGoal(token, { title: 'Autumn 10K' })
    const other = getFakeApi().addGoal(token, { title: 'Spring half', targetDate: '2027-03-20' })
    // Give the other goal pending suggestions through the API.
    await getFakeApi().handle(
      new Request(`http://localhost/api/goals/${other.id}/plan`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${token}` },
      }),
    )
    expect(
      getFakeApi()
        .suggestionsOf(token)
        .every((s) => s.goalId === other.id),
    ).toBe(true)
    const user = userEvent.setup()
    renderRoute(`/goals/${goal.id}`, { session: 'none' })
    const requests = recordRequests()
    await user.click(await generateButton())
    const dialog = await screen.findByRole('alertdialog')
    expect(dialog).toHaveTextContent('Generating replaces the suggestions of "Spring half"')
    await user.click(within(dialog).getByRole('button', { name: 'Replace and generate' }))
    await waitFor(() =>
      expect(
        getFakeApi()
          .suggestionsOf(token)
          .every((s) => s.goalId === goal.id),
      ).toBe(true),
    )
    const plans = requests.filter((r) => r.path.endsWith('/plan'))
    expect(plans.at(-1)?.body).toEqual({ replaceOtherGoals: true, lang: 'en' })
  })

  it('opens the same dialog from a 409 plan-other-goal-active', async () => {
    const { user, goal } = await detail()
    getFakeApi().addFault(
      faults.problem({ method: 'POST', path: /\/plan$/ }, 409, PLAN_PROBLEMS.otherGoalActive, {
        members: { goalId: 'unknown' },
        times: 1,
      }),
    )
    await user.click(await generateButton())
    const dialog = await screen.findByRole('alertdialog')
    expect(dialog).toHaveTextContent('Generating replaces the suggestions of your other goal')
    await user.click(within(dialog).getByRole('button', { name: 'Replace and generate' }))
    expect(await screen.findByRole('button', { name: 'Regenerate' })).toBeInTheDocument()
    expect(goal.id).toBeTruthy()
  })

  it('accepts, edits and dismisses suggestions; dismissed ones come back with Accept anyway', async () => {
    const { user, token } = await detail()
    await user.click(await generateButton())
    await screen.findAllByRole('region', { name: /^Week \d+, from/ })
    const before = getFakeApi().suggestionsOf(token).length

    const first = document.querySelector<HTMLElement>('li[data-suggestion-id]')!
    const firstId = first.dataset.suggestionId!
    await user.click(within(first).getByRole('button', { name: 'Accept' }))
    await waitFor(() => expect(getFakeApi().suggestionsOf(token)).toHaveLength(before - 1))
    expect(
      getFakeApi()
        .runsOf(token)
        .find((r) => r.goalId)?.status,
    ).toBe('Planned')

    const second = document.querySelector<HTMLElement>('li[data-suggestion-id]')!
    const secondId = second.dataset.suggestionId!
    expect(secondId).not.toBe(firstId)
    await user.click(within(second).getByRole('button', { name: 'Dismiss' }))
    await waitFor(() => expect(document.querySelector(`li[data-suggestion-id="${secondId}"]`)).toBeNull())
    expect(
      getFakeApi()
        .suggestionsOf(token)
        .find((s) => s.id === secondId)?.status,
    ).toBe('Dismissed')

    await user.click(screen.getByRole('switch', { name: 'Show dismissed' }))
    const back = await waitFor(() => {
      const el = document.querySelector<HTMLElement>(`li[data-suggestion-id="${secondId}"]`)
      expect(el).not.toBeNull()
      return el!
    })
    expect(within(back).queryByRole('button', { name: 'Dismiss' })).toBeNull()
    await user.click(within(back).getByRole('button', { name: 'Accept anyway' }))
    await waitFor(() =>
      expect(
        getFakeApi()
          .suggestionsOf(token)
          .find((s) => s.id === secondId),
      ).toBeUndefined(),
    )
  })

  it('shows the outdated notice after the goal changed and the date-changed banner of a linked race', async () => {
    const { token, goal, user } = await detail()
    await user.click(await generateButton())
    await screen.findAllByRole('region', { name: /^Week \d+, from/ })
    // Edit the distance behind the UI's back.
    const current = getFakeApi().goalsOf(token)[0]!
    await getFakeApi().handle(
      new Request(`http://localhost/api/goals/${goal.id}`, {
        method: 'PUT',
        headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
        body: JSON.stringify({
          type: current.type,
          title: current.title,
          targetDate: current.targetDate,
          distanceMeters: 15_000,
          status: 'Active',
          version: current.version,
        }),
      }),
    )
    // A fresh render reads the changed goal.
    renderRoute(`/goals/${goal.id}`, { session: 'none' })
    expect((await screen.findAllByTestId('plan-outdated'))[0]).toHaveTextContent(
      'The goal changed since this plan was made',
    )
  })

  it('deletes a goal after confirmation', async () => {
    const { user, token } = await detail()
    await user.click(screen.getByRole('button', { name: 'Delete goal' }))
    const dialog = await screen.findByRole('alertdialog')
    await user.click(within(dialog).getByRole('button', { name: 'Delete goal' }))
    await waitFor(() => expect(getFakeApi().goalsOf(token)).toHaveLength(0))
    expect(await screen.findByText(/You have no goals yet/)).toBeInTheDocument()
  })

  it('answers an unknown goal with a not-found state', async () => {
    await open('/goals/00000000-0000-4000-8000-00000000ffff')
    expect(await screen.findByText('This goal does not exist.')).toBeInTheDocument()
  })
})

describe('Navigation', () => {
  it('shows the Goals nav entry only when the feature is on', async () => {
    await open('/diary/2026/11')
    expect(await screen.findByRole('link', { name: 'Goals' })).toHaveAttribute('href', '/goals')
  })
  it('hides the entry when the feature is off', async () => {
    await open('/diary/2026/11', { goalsEnabled: false })
    await screen.findByRole('grid', { hidden: true })
    expect(screen.queryByRole('link', { name: 'Goals' })).toBeNull()
  })
})
