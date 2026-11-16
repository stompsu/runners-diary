import type { components } from '@/api/generated/schema'

type S = components['schemas']

export type GoalDto = S['GoalDto']
export type GoalWriteRequest = S['GoalWriteRequest']
export type GoalType = S['GoalType']
export type GoalStatus = S['GoalStatus']
export type GoalTerm = S['GoalTerm']
export type GoalPlanDto = S['GoalPlanDto']
export type GoalEstimate = S['GoalEstimateResponse']
export type EstimateGoalRequest = S['EstimateGoalRequest']
export type TrainingSuggestion = S['TrainingSuggestionDto']
export type TrainingIntensity = S['TrainingIntensity']
export type TrainingPhase = S['TrainingPhase']
export type TrainingPlanResponse = S['TrainingPlanResponse']
export type Surface = NonNullable<S['Surface']>

export const GOAL_TYPES = ['Race', 'Performance', 'Distance'] as const satisfies readonly GoalType[]
export const GOAL_STATUSES = ['Active', 'Achieved', 'Archived'] as const satisfies readonly GoalStatus[]
