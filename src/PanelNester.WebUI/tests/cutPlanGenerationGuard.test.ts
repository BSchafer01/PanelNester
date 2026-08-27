import { describe, expect, it } from 'vitest';
import { reconcileCutPlanGenerationResponse } from '../src/cutPlanGenerationGuard';
import type { ProjectRecord } from '../src/types/contracts';

const project = {
  projectId: 'project-a',
  settings: { kerfWidth: 0.125 },
  state: {
    optimizationGroups: [{
      optimizationGroupId: 'frames',
      name: 'Frames',
      order: 0,
      origin: 'project',
      stockLength: 240,
      requiredPieces: [{
        requiredPieceId: 'piece-1',
        quantity: 2,
        length: 48,
        profileNumber: 'P-100',
        isManual: true,
        sourceReferences: [],
      }],
      parts: [],
      stockGroups: [],
      lastStockLengthOptimizationResult: null,
      resultStatus: 'stale',
    }],
  },
} as ProjectRecord;

describe('Cut Plan generation response guard', () => {
  it('rejects only targets whose generation inputs changed', () => {
    const changedKerf = {
      ...project,
      settings: { ...project.settings, kerfWidth: 0.25 },
    };
    const changedQuantity = {
      ...project,
      state: {
        ...project.state,
        optimizationGroups: [{
          ...project.state.optimizationGroups[0],
          requiredPieces: [{
            ...project.state.optimizationGroups[0].requiredPieces[0],
            quantity: 3,
          }],
        }],
      },
    };
    const differentProject = { ...project, projectId: 'project-b' };

    const responseProject = {
      ...project,
      state: {
        ...project.state,
        optimizationGroups: [{
          ...project.state.optimizationGroups[0],
          resultStatus: 'valid' as const,
        }],
      },
    };

    expect(reconcileCutPlanGenerationResponse(
      project,
      changedKerf,
      responseProject,
      ['frames'],
    ).discardedOptimizationGroupIds).toEqual(['frames']);
    expect(reconcileCutPlanGenerationResponse(
      project,
      changedQuantity,
      responseProject,
      ['frames'],
    ).project.state.optimizationGroups[0]).toBe(changedQuantity.state.optimizationGroups[0]);
    expect(reconcileCutPlanGenerationResponse(
      project,
      differentProject,
      responseProject,
      ['frames'],
    ).discardedOptimizationGroupIds).toEqual(['frames']);
  });

  it('merges generated output while preserving unrelated and descriptive edits', () => {
    const responseProject = {
      ...project,
      state: {
        ...project.state,
        optimizationGroups: [{
          ...project.state.optimizationGroups[0],
          resultStatus: 'valid' as const,
          lastStockLengthOptimizationResult: {
            optimizationGroupId: 'frames',
            status: 'complete' as const,
            description: 'Generated',
            cutPlans: [],
          },
        }],
      },
    };

    const currentProject = {
      ...project,
      state: {
        ...project.state,
        optimizationGroups: [{
          ...project.state.optimizationGroups[0],
          name: 'Renamed Frames',
        }, {
          ...project.state.optimizationGroups[0],
          optimizationGroupId: 'unrelated',
          name: 'Unrelated edited group',
        }],
      },
    };
    const reconciliation = reconcileCutPlanGenerationResponse(
      project,
      currentProject,
      responseProject,
      ['frames'],
    );

    expect(reconciliation.discardedOptimizationGroupIds).toEqual([]);
    expect(reconciliation.project.state.optimizationGroups[0].name).toBe('Renamed Frames');
    expect(reconciliation.project.state.optimizationGroups[0].resultStatus).toBe('valid');
    expect(reconciliation.project.state.optimizationGroups[1]).toBe(
      currentProject.state.optimizationGroups[1],
    );
  });
});
