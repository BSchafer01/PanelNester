import type { ProjectKind } from './types/contracts';

export type AppRoute = 'overview' | 'import' | 'materials' | 'extrusions' | 'results';

export const projectKindLabels: Record<ProjectKind, string> = {
  sheet: 'Sheet Project',
  stockLength: 'Stock-Length Project',
};

export function isProjectRouteAllowed(projectKind: ProjectKind, route: AppRoute): boolean {
  return (
    projectKind === 'sheet' ||
    (route !== 'materials' && route !== 'extrusions')
  );
}

export function guardProjectRoute(projectKind: ProjectKind, route: AppRoute): AppRoute {
  return isProjectRouteAllowed(projectKind, route) ? route : 'overview';
}

export function projectKindSupportsStiffeners(projectKind: ProjectKind): boolean {
  return projectKind === 'sheet';
}
