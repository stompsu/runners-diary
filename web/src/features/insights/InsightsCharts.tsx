import type { InsightsReport } from './insightsTypes'
import AcwrChart from './charts/AcwrChart'
import LoadChart from './charts/LoadChart'
import PaceTrendChart from './charts/PaceTrendChart'
import TypeDistributionChart from './charts/TypeDistributionChart'
import WeeklyVolumeChart from './charts/WeeklyVolumeChart'

/** The five charts in the plan's order; this module and Recharts (the `chart-vendor` chunk) are only reached through the lazy insights route. */
export function InsightsCharts({ report }: { report: InsightsReport }) {
  return (
    <div className="grid gap-3 lg:grid-cols-2">
      <div className="lg:col-span-2">
        <WeeklyVolumeChart report={report} />
      </div>
      <LoadChart report={report} />
      <AcwrChart report={report} />
      <TypeDistributionChart report={report} />
      <PaceTrendChart report={report} />
    </div>
  )
}
