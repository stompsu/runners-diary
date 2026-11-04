import { QueryClientProvider } from '@tanstack/react-query'
import { act, render, renderHook, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { createQueryClient } from '@/app/queryClient'
import { useInvalidateRuns } from '@/features/runs/runQueries'
import { getFakeApi, resetFakeApi } from '@/test/fake'
import { faults, type FakeApiOptions, type SummaryPostOutcome, type SummaryScenario } from '@/test/fakeApi'
import { recordRequests } from '@/test/requests'
import { signInForTest } from '@/test/render'
import { SummaryCard } from './SummaryCard'
import type { SummaryRange } from './summaryQueries'

const RANGE: SummaryRange = { from: '2026-08-17', to: '2026-11-08', compare: 'previous' }
const GET = 'GET /api/insights/summary'
const SUMMARY_PATH = '/api/insights/summary'

function open(
  scenario: SummaryScenario = 'none',
  options: { post?: SummaryPostOutcome; session?: 'guest' | 'user'; slowMs?: number } & FakeApiOptions = {},
) {
  const { post, session = 'guest', slowMs, ...rest } = options
  resetFakeApi({ summary: { scenario, post, slowMs }, ...rest })
  signInForTest(session)
  const queryClient = createQueryClient()
  const view = render(
    <QueryClientProvider client={queryClient}>
      <SummaryCard range={RANGE} />
    </QueryClientProvider>,
  )
  return { ...view, queryClient }
}

const card = () => screen.getByTestId('summary-card')
const inCard = () => within(card())
const status = () => screen.getByTestId('summary-status')

describe('SummaryCard states', () => {
  it('offers Generate with the remaining count for a guest when nothing is stored', async () => {
    open('none')

    expect(await inCard().findByRole('heading', { level: 3, name: 'Get a short summary of this period' })).toBeVisible()
    expect(inCard().getByText(/never sees your notes/)).toBeVisible()
    expect(inCard().getByRole('button', { name: 'Generate summary' })).toBeEnabled()
    expect(inCard().getByTestId('summary-remaining')).toHaveTextContent('3 of 3 left')
    expect(inCard().getByRole('heading', { level: 2, name: 'AI summary' })).toBeVisible()
    expect(screen.getByRole('region', { name: 'AI summary' })).toBeInTheDocument()
  })

  it('shows the number of generations left in 24 hours for a user', async () => {
    open('none', { session: 'user' })

    await inCard().findByRole('button', { name: 'Generate summary' })
    expect(inCard().getByTestId('summary-remaining')).toHaveTextContent('10 of 10 left in 24 hours')
  })

  it('shows a skeleton while the stored summary loads', async () => {
    open('none', {})
    getFakeApi().addFault(faults.delay({ method: 'GET', path: SUMMARY_PATH }, 100, 1))
    expect(inCard().getByLabelText('Loading the summary')).toBeInTheDocument()
    await inCard().findByRole('button', { name: 'Generate summary' })
  })

  it('shows a fresh model summary with formatted values, the footer and the disclosures', async () => {
    open('fresh')

    const headline = await inCard().findByRole('heading', { level: 3 })
    expect(headline).toHaveTextContent(
      'A steady block with more distance than before and a personal best at the half marathon.',
    )
    expect(inCard().getByTestId('summary-badge')).toHaveTextContent('AI-generated')
    expect(inCard().getByText('You covered 382.6 km in 39 runs, +14.2% on the previous period.')).toBeVisible()
    expect(inCard().getByText('Your half marathon on Oct 11, 2026 was a personal best in 1:30:32.')).toBeVisible()
    expect(inCard().getByText('Your ACWR peaked at 1.42 on Oct 11, 2026, in the High band.')).toBeVisible()
    expect(inCard().getByText('18% of the load uses estimated effort.')).toBeVisible()
    expect(inCard().getByText('Keep easy days easy and raise weekly load gradually.')).toBeVisible()
    for (const name of ['What went well', 'Watch-outs', 'Suggestion', 'Data notes']) {
      expect(inCard().getByRole('heading', { level: 4, name })).toBeVisible()
    }
    const footer = within(inCard().getByTestId('summary-footer'))
    expect(footer.getByText(/Generated .* · Model gpt-5\.4-mini · Prompt version 1/)).toBeVisible()
    expect(
      footer.getByText(/Written by an AI model.*It can make mistakes\. This is not medical advice\./),
    ).toBeVisible()
    expect(inCard().queryByTestId('summary-stale')).not.toBeInTheDocument()
    expect(inCard().queryByText(/\{|\}/)).not.toBeInTheDocument()
    expect(inCard().getByRole('button', { name: 'Regenerate' })).toBeEnabled()
  })

  it('keeps a stale summary visible with a dated note and a Regenerate button', async () => {
    open('stale')

    await inCard().findByRole('heading', { level: 3 })
    expect(inCard().getByTestId('summary-stale')).toHaveTextContent(
      /Newer data is available\. This summary is from \w{3} \d+, 2026\./,
    )
    expect(inCard().getByTestId('summary-badge')).toHaveTextContent('AI-generated')
    expect(inCard().getByRole('button', { name: 'Regenerate' })).toBeEnabled()
  })

  it('labels the demo example and offers a summary of the own data', async () => {
    open('demo')

    await inCard().findByRole('heading', { level: 3 })
    expect(inCard().getByTestId('summary-badge')).toHaveTextContent('Example summary for the demo data')
    expect(inCard().getByTestId('summary-footer')).toHaveTextContent('An example written for the demo data.')
    expect(inCard().getByTestId('summary-footer')).not.toHaveTextContent('Model')
    expect(inCard().getByRole('button', { name: 'Generate my own summary' })).toBeEnabled()
  })

  it('shows the automatic note without a model mention and without actions', async () => {
    open('template')

    const headline = await inCard().findByRole('heading', { level: 3 })
    expect(headline).toHaveTextContent('There are only 2 runs in this period, too few for a summary.')
    expect(inCard().getByTestId('summary-badge')).toHaveTextContent('Automatic note')
    const footer = inCard().getByTestId('summary-footer')
    expect(footer).toHaveTextContent('This is not medical advice.')
    expect(footer).not.toHaveTextContent(/AI|model/i)
    expect(inCard().queryByRole('button')).not.toBeInTheDocument()
  })

  it('says generation is unavailable when no model is configured, and offers no button', async () => {
    open('notConfigured')

    await inCard().findByRole('heading', { level: 3 })
    expect(inCard().getByTestId('summary-badge')).toHaveTextContent('Example summary')
    expect(inCard().getByText("Generating your own summary isn't available here.")).toBeVisible()
    expect(inCard().queryByRole('button')).not.toBeInTheDocument()
  })

  it('guest quota exhausted: a focusable, aria-disabled button that explains why and does nothing', async () => {
    const user = userEvent.setup()
    open('quotaExhausted')
    const requests = recordRequests()

    const button = await inCard().findByRole('button', { name: 'Generate summary' })
    expect(button).toHaveAttribute('aria-disabled', 'true')
    expect(button).toHaveAccessibleDescription('Demo limit reached: demo accounts can generate 3 summaries.')
    expect(inCard().queryByTestId('summary-remaining')).not.toBeInTheDocument()
    await user.click(button)

    expect(requests.filter((request) => request.method === 'POST')).toEqual([])
  })

  it('user quota exhausted: says when the limit resets, in the user zone', async () => {
    open('quotaExhausted', { session: 'user' })

    const button = await inCard().findByRole('button', { name: 'Generate summary' })
    expect(button).toHaveAccessibleDescription(
      /^Daily limit reached\. You can generate again \w{3} \d+, \d+:\d\d\s(AM|PM)\.$/,
    )
  })

  it('shows a retryable error when the stored summary cannot be loaded', async () => {
    const user = userEvent.setup()
    resetFakeApi({ summary: { scenario: 'none' } })
    getFakeApi().addFault(faults.status({ method: 'GET', path: SUMMARY_PATH }, 500, 1))
    signInForTest('guest')
    render(
      <QueryClientProvider client={createQueryClient()}>
        <SummaryCard range={RANGE} />
      </QueryClientProvider>,
    )

    expect(await inCard().findByRole('alert')).toHaveTextContent("Couldn't load the summary.")
    await user.click(inCard().getByRole('button', { name: 'Try again' }))
    expect(await inCard().findByRole('button', { name: 'Generate summary' })).toBeVisible()
  })
})

describe('generating', () => {
  it('sends the request, announces progress and result, and moves focus to the new headline', async () => {
    const user = userEvent.setup()
    open('none')
    const requests = recordRequests()

    await user.click(await inCard().findByRole('button', { name: 'Generate summary' }))

    await waitFor(() => expect(inCard().getByTestId('summary-badge')).toHaveTextContent('AI-generated'))
    await waitFor(() => expect(inCard().getByRole('heading', { level: 3 })).toHaveFocus())
    expect(status()).toHaveTextContent('The summary is ready.')
    expect(inCard().getByTestId('summary-badge')).toHaveTextContent('AI-generated')
    expect(requests.find((request) => request.method === 'POST')).toEqual({
      method: 'POST',
      path: SUMMARY_PATH,
      body: { from: '2026-08-17', to: '2026-11-08', compare: 'previous', lang: 'en', regenerate: false },
    })
    // The answer replaces the cached GET: no refetch, and the quota in the button row is current.
    expect(getFakeApi().requests.filter((request) => request === GET)).toHaveLength(1)
    expect(inCard().getByTestId('summary-remaining')).toHaveTextContent('2 of 3 left')
  })

  it('shows Cancel, the seconds so far and a status message while the model works; Cancel takes the focus', async () => {
    const user = userEvent.setup()
    open('none', { post: 'slow' })

    await user.click(await inCard().findByRole('button', { name: 'Generate summary' }))

    const cancel = await inCard().findByRole('button', { name: 'Cancel' })
    expect(cancel).toHaveFocus()
    expect(status()).toHaveTextContent('Generating the summary…')
    expect(inCard().getByTestId('summary-elapsed')).toHaveTextContent('0 s so far')
    expect(inCard().queryByRole('button', { name: 'Generate summary' })).not.toBeInTheDocument()
    const busy = card().querySelector('[aria-busy="true"]')
    expect(busy).not.toBeNull()
    // Screen readers may suppress live regions inside aria-busy.
    expect(busy!.contains(status())).toBe(false)
    expect(status().closest('[aria-busy="true"]')).toBeNull()
  })

  it('Cancel aborts the request, announces it, brings the button back with focus and shows no error', async () => {
    const user = userEvent.setup()
    open('none', { post: 'slow' })
    const signals: AbortSignal[] = []
    const inner = vi.mocked(fetch).getMockImplementation()!
    vi.mocked(fetch).mockImplementation((input, init) => {
      if (input instanceof Request && input.method === 'POST') signals.push(input.signal)
      return inner(input, init)
    })

    await user.click(await inCard().findByRole('button', { name: 'Generate summary' }))
    await user.click(await inCard().findByRole('button', { name: 'Cancel' }))

    const again = await inCard().findByRole('button', { name: 'Generate summary' })
    expect(again).toHaveFocus()
    expect(status()).toHaveTextContent('Generation cancelled.')
    expect(signals).toHaveLength(1)
    expect(signals[0]!.aborted).toBe(true)
    expect(inCard().queryByRole('alert')).not.toBeInTheDocument()
    // A cancel is not an error, but the server records it: the remaining count is refetched.
    await waitFor(() => expect(getFakeApi().requests.filter((request) => request === GET)).toHaveLength(2))
  })

  it('aborts a running generation when the card goes away', async () => {
    const user = userEvent.setup()
    const { unmount } = open('none', { post: 'slow' })
    const signals: AbortSignal[] = []
    const inner = vi.mocked(fetch).getMockImplementation()!
    vi.mocked(fetch).mockImplementation((input, init) => {
      if (input instanceof Request && input.method === 'POST') signals.push(input.signal)
      return inner(input, init)
    })

    await user.click(await inCard().findByRole('button', { name: 'Generate summary' }))
    await inCard().findByRole('button', { name: 'Cancel' })
    unmount()

    expect(signals[0]?.aborted).toBe(true)
  })

  it('Regenerate on a stale summary asks for a new one (regenerate: true) and removes the note', async () => {
    const user = userEvent.setup()
    open('stale')
    const requests = recordRequests()

    await user.click(await inCard().findByRole('button', { name: 'Regenerate' }))

    await waitFor(() => expect(inCard().queryByTestId('summary-stale')).not.toBeInTheDocument())
    expect(requests.find((request) => request.method === 'POST')?.body).toMatchObject({ regenerate: true })
    await waitFor(() => expect(inCard().getByRole('heading', { level: 3 })).toHaveFocus())
  })

  it('"Generate my own summary" on the demo example uses the quota and replaces the example', async () => {
    const user = userEvent.setup()
    open('demo')

    await user.click(await inCard().findByRole('button', { name: 'Generate my own summary' }))

    await waitFor(() => expect(inCard().getByTestId('summary-badge')).toHaveTextContent('AI-generated'))
    expect(inCard().getByTestId('summary-remaining')).toHaveTextContent('2 of 3 left')
  })

  it('the third guest generation uses the last one; the button is then disabled with the reason', async () => {
    const user = userEvent.setup()
    open('none')

    for (let i = 0; i < 3; i++) {
      const name = i === 0 ? 'Generate summary' : 'Regenerate'
      await user.click(await inCard().findByRole('button', { name }))
      await waitFor(() => expect(status()).toHaveTextContent('The summary is ready.'))
      await inCard().findByRole('button', { name: 'Regenerate' })
      // A second answer in a row: the status text is the same, so wait for the button state instead.
      await act(async () => {})
    }

    const button = await inCard().findByRole('button', { name: 'Regenerate' })
    await waitFor(() => expect(button).toHaveAttribute('aria-disabled', 'true'))
    expect(button).toHaveAccessibleDescription(/Demo limit reached/)
  })
})

describe('errors', () => {
  async function generateWith(outcome: SummaryPostOutcome, options: Parameters<typeof open>[1] = {}) {
    const user = userEvent.setup()
    open('none', { post: outcome, ...options })
    await user.click(await inCard().findByRole('button', { name: 'Generate summary' }))
    const alert = await inCard().findByRole('alert')
    return { user, alert }
  }

  it.each<[SummaryPostOutcome, RegExp]>([
    ['inProgress', /Another summary is being generated/],
    ['rateLimited', /Too many requests\. Please try again in 30 s\./],
    ['invalidOutput', /answer didn't pass the checks, so nothing was saved\. The attempt counts toward your limit\./],
    ['timeout', /took too long to answer\. The attempt counts toward your limit\./],
    ['provider', /AI service had a problem\. Try again in a moment\./],
    ['unavailable', /AI summaries aren't available right now\./],
    ['busy', /AI service is busy\. Please try again in a few seconds\./],
  ])('%s: a role=alert message with Try again, and the previous state intact', async (outcome, message) => {
    const { alert } = await generateWith(outcome)

    expect(alert).toHaveTextContent(message)
    await waitFor(() => expect(within(alert).getByRole('button', { name: 'Try again' })).toHaveFocus())
    // One action only while the alert offers Try again.
    expect(inCard().queryByRole('button', { name: 'Generate summary' })).not.toBeInTheDocument()
    expect(inCard().getByTestId('summary-none')).toBeVisible()
  })

  it('a guest at the account limit gets a message and no Try again; the card then shows the disabled state', async () => {
    const { alert } = await generateWith('quotaAccount')

    expect(alert).toHaveTextContent('You have reached your generation limit.')
    expect(within(alert).queryByRole('button')).not.toBeInTheDocument()
    expect(alert).toHaveAttribute('data-kind', 'quota')
  })

  it('a user at the account limit sees when it resets', async () => {
    const { alert } = await generateWith('quotaAccount', { session: 'user' })
    expect(alert).toHaveTextContent(/You have reached your generation limit\. It resets \w{3} \d+, \d+:\d\d\s(AM|PM)\./)
  })

  it('the global limit is worded differently and has no Try again', async () => {
    const { alert } = await generateWith('quotaGlobal')

    expect(alert).toHaveTextContent(/The daily AI capacity is used up\. It resets/)
    expect(within(alert).queryByRole('button')).not.toBeInTheDocument()
  })

  it('a network failure is retryable', async () => {
    const user = userEvent.setup()
    open('none')
    getFakeApi().addFault(faults.network({ method: 'POST', path: SUMMARY_PATH }, 1))

    await user.click(await inCard().findByRole('button', { name: 'Generate summary' }))
    const alert = await inCard().findByRole('alert')
    expect(alert).toHaveTextContent('The server is not reachable right now.')

    await user.click(within(alert).getByRole('button', { name: 'Try again' }))
    await waitFor(() => expect(inCard().getByTestId('summary-badge')).toHaveTextContent('AI-generated'))
    await waitFor(() => expect(inCard().getByRole('heading', { level: 3 })).toHaveFocus())
    expect(inCard().queryByRole('alert')).not.toBeInTheDocument()
  })

  it('Try again repeats the same request and succeeds once the cause is gone', async () => {
    const { user } = await generateWith('busy')
    const requests = recordRequests()
    getFakeApi().setSummaryOutcome('ok')

    await user.click(inCard().getByRole('button', { name: 'Try again' }))

    expect(await inCard().findByRole('heading', { level: 3 })).toBeVisible()
    expect(requests.find((request) => request.method === 'POST')?.body).toMatchObject({ regenerate: false })
    expect(inCard().queryByRole('alert')).not.toBeInTheDocument()
  })

  it('a failed generation keeps the old summary on screen and refetches the quota', async () => {
    const user = userEvent.setup()
    open('fresh', { post: 'timeout' })

    await user.click(await inCard().findByRole('button', { name: 'Regenerate' }))
    const alert = await inCard().findByRole('alert')

    expect(alert).toHaveTextContent('took too long')
    expect(inCard().getByTestId('summary-badge')).toHaveTextContent('AI-generated')
    await waitFor(() => expect(getFakeApi().requests.filter((request) => request === GET)).toHaveLength(2))
    expect(inCard().getByRole('heading', { level: 3 })).toBeVisible()
  })

  it('a stored answer for a not configured environment ends in "unavailable"', async () => {
    const user = userEvent.setup()
    open('none')
    await inCard().findByRole('button', { name: 'Generate summary' })
    getFakeApi().setSummaryScenario('none', 'unavailable')

    await user.click(inCard().getByRole('button', { name: 'Generate summary' }))

    expect(await inCard().findByRole('alert')).toHaveTextContent("AI summaries aren't available right now.")
  })
})

describe('invalidation', () => {
  it('a run create/update/delete refetches the summary, which then shows the stale note', async () => {
    const { queryClient } = open('fresh')
    await inCard().findByRole('heading', { level: 3 })
    expect(inCard().queryByTestId('summary-stale')).not.toBeInTheDocument()
    const before = getFakeApi().requests.filter((request) => request === GET).length

    getFakeApi().setSummaryScenario('stale')
    const { result } = renderHook(() => useInvalidateRuns(), {
      wrapper: ({ children }) => <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>,
    })
    await act(async () => {
      await result.current()
    })

    await waitFor(() =>
      expect(getFakeApi().requests.filter((request) => request === GET).length).toBeGreaterThan(before),
    )
    expect(await inCard().findByTestId('summary-stale')).toBeVisible()
  })
})
