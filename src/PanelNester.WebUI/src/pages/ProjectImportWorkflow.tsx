import type {
  ImportMappingSession,
  Material,
  OptimizationGroup,
  ProjectKind,
} from '../types/contracts';
import { SheetProjectImportWorkflow } from './SheetProjectImportWorkflow';
import { StockLengthImportWorkflow } from './StockLengthImportWorkflow';

export interface ProjectImportWorkflowProps {
  projectKind: ProjectKind;
  session: ImportMappingSession;
  groups: OptimizationGroup[];
  materials: Material[];
  busy: boolean;
  message?: string;
  onReplaceFile: () => void | Promise<void>;
  onUpdateSession: (session: ImportMappingSession) => void;
  onPreview: (session?: ImportMappingSession, worksheetNames?: string[]) => void | Promise<void>;
  onFinalize: () => void | Promise<void>;
  onCancel: () => void | Promise<void>;
}

export function ProjectImportWorkflow(props: ProjectImportWorkflowProps) {
  if (props.projectKind === 'sheet') {
    return <SheetProjectImportWorkflow
      busy={props.busy}
      groups={props.groups}
      materials={props.materials}
      onCancel={props.onCancel}
      onFinalize={props.onFinalize}
      onPreview={props.onPreview}
      onReplaceFile={props.onReplaceFile}
      onUpdateSession={props.onUpdateSession}
      session={props.session}
    />;
  }

  return <StockLengthImportWorkflow
    busy={props.busy}
    groups={props.groups}
    message={props.message}
    onCancel={props.onCancel}
    onFinalize={props.onFinalize}
    onPreview={props.onPreview}
    onReplaceFile={props.onReplaceFile}
    onUpdateSession={props.onUpdateSession}
    session={props.session}
  />;
}
