import type { OptimizationGroup, ProjectRecord } from './types/contracts';

function optimizationInputs(group: OptimizationGroup) {
  return {
    stockLength: group.stockLength,
    requiredPieces: group.requiredPieces,
  };
}

function hasSameOptimizationInputs(
  startedGroup: OptimizationGroup,
  currentGroup: OptimizationGroup,
): boolean {
  return JSON.stringify(optimizationInputs(startedGroup)) ===
    JSON.stringify(optimizationInputs(currentGroup));
}

export interface CutPlanGenerationReconciliation {
  project: ProjectRecord;
  discardedOptimizationGroupIds: string[];
}

export function reconcileCutPlanGenerationResponse(
  startedProject: ProjectRecord,
  currentProject: ProjectRecord,
  responseProject: ProjectRecord,
  targetOptimizationGroupIds: readonly string[],
): CutPlanGenerationReconciliation {
  const targetIds = new Set(targetOptimizationGroupIds);
  const discardedOptimizationGroupIds: string[] = [];
  const projectChanged = startedProject.projectId !== currentProject.projectId;
  const kerfChanged = startedProject.settings.kerfWidth !== currentProject.settings.kerfWidth;
  const startedGroups = new Map(startedProject.state.optimizationGroups.map(
    (group) => [group.optimizationGroupId, group],
  ));
  const responseGroups = new Map(responseProject.state.optimizationGroups.map(
    (group) => [group.optimizationGroupId, group],
  ));

  const optimizationGroups = currentProject.state.optimizationGroups.map((currentGroup) => {
    if (!targetIds.has(currentGroup.optimizationGroupId)) {
      return currentGroup;
    }

    const startedGroup = startedGroups.get(currentGroup.optimizationGroupId);
    const responseGroup = responseGroups.get(currentGroup.optimizationGroupId);
    if (
      projectChanged ||
      kerfChanged ||
      !startedGroup ||
      !responseGroup ||
      !hasSameOptimizationInputs(startedGroup, currentGroup)
    ) {
      discardedOptimizationGroupIds.push(currentGroup.optimizationGroupId);
      return currentGroup;
    }

    return {
      ...currentGroup,
      lastStockLengthOptimizationResult: responseGroup.lastStockLengthOptimizationResult,
      lastStockLengthGenerationError: responseGroup.lastStockLengthGenerationError,
      resultStatus: responseGroup.resultStatus,
    };
  });

  for (const targetId of targetIds) {
    if (!currentProject.state.optimizationGroups.some(
      (group) => group.optimizationGroupId === targetId,
    )) {
      discardedOptimizationGroupIds.push(targetId);
    }
  }

  return {
    project: {
      ...currentProject,
      state: {
        ...currentProject.state,
        optimizationGroups,
      },
    },
    discardedOptimizationGroupIds,
  };
}
