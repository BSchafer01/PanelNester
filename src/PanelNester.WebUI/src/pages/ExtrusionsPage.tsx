import { useEffect, useMemo, useRef, useState } from 'react';
import type {
  ExtrusionEdgeAssignment,
  ExtrusionGroupLayout,
  ExtrusionLayoutState,
  ExtrusionPanelInstance,
  ExtrusionSegmentDetail,
  PartRow,
} from '../types/contracts';

interface ExtrusionsPageProps {
  importedRows: PartRow[];
  layout: ExtrusionLayoutState;
  statusMessage: string;
  busy: boolean;
  canExportPdf: boolean;
  canExportExcel: boolean;
  onLayoutChange: (layout: ExtrusionLayoutState) => void;
  onExportPdf: () => Promise<void>;
  onExportExcel: () => Promise<void>;
}

const edges = ['top', 'right', 'bottom', 'left'] as const;

export function ExtrusionsPage({
  importedRows,
  layout,
  statusMessage,
  busy,
  canExportPdf,
  canExportExcel,
  onLayoutChange,
  onExportPdf,
  onExportExcel,
}: ExtrusionsPageProps) {
  const panels = useMemo(() => expandPanels(importedRows), [importedRows]);
  const normalizedLayout = useMemo(
    () => normalizeLayout(layout, panels),
    [layout, panels],
  );
  const [activeGroupName, setActiveGroupName] = useState<string>();
  const activeGroup =
    normalizedLayout.groups.find((group) => group.groupName === activeGroupName) ??
    normalizedLayout.groups[0];
  const [selectedInstanceId, setSelectedInstanceId] = useState<string>();
  const activePanels = panels.filter((panel) => panel.groupName === activeGroup?.groupName);
  const segments = useMemo(
    () => buildSegments(normalizedLayout, panels),
    [normalizedLayout, panels],
  );
  const activeSummary = summarizeSegments(
    segments.filter((segment) => segment.groupName === activeGroup?.groupName),
  );

  useEffect(() => {
    if (!activeGroup && normalizedLayout.groups[0]) {
      setActiveGroupName(normalizedLayout.groups[0].groupName);
      return;
    }

    if (
      activeGroupName &&
      !normalizedLayout.groups.some((group) => group.groupName === activeGroupName)
    ) {
      setActiveGroupName(normalizedLayout.groups[0]?.groupName);
    }
  }, [activeGroup, activeGroupName, normalizedLayout.groups]);

  useEffect(() => {
    if (JSON.stringify(layout) !== JSON.stringify(normalizedLayout)) {
      onLayoutChange(normalizedLayout);
    }
  }, [layout, normalizedLayout, onLayoutChange]);

  const updateLayout = (next: ExtrusionLayoutState) => {
    onLayoutChange(normalizeLayout(next, panels));
  };

  const updateActiveGroup = (nextGroup: ExtrusionGroupLayout) => {
    updateLayout({
      ...normalizedLayout,
      groups: normalizedLayout.groups.map((group) =>
        group.groupName === nextGroup.groupName ? nextGroup : group,
      ),
    });
  };

  const selectedPanel = panels.find((panel) => panel.instanceId === selectedInstanceId);
  const selectedCell = activeGroup?.cells.find((cell) => cell.instanceId === selectedInstanceId);
  const selectedEdges =
    activeGroup?.edgeAssignments.filter(
      (assignment) => assignment.instanceId === selectedInstanceId,
    ) ?? [];
  const selectedSides =
    selectedPanel && activeGroup
      ? getPanelSides(activeGroup, selectedPanel.instanceId)
      : [];

  const movePanel = (instanceId: string, row: number, column: number) => {
    if (!activeGroup) {
      return;
    }

    const sourceCell = activeGroup.cells.find((cell) => cell.instanceId === instanceId);
    const targetCell = activeGroup.cells.find(
      (cell) => cell.row === row && cell.column === column,
    );
    updateActiveGroup({
      ...activeGroup,
      cells: activeGroup.cells.map((cell) =>
        cell.instanceId === instanceId
          ? { ...cell, row, column }
          : targetCell && sourceCell && cell.instanceId === targetCell.instanceId
            ? {
                ...cell,
                row: sourceCell.row,
                column: sourceCell.column,
              }
            : cell,
      ),
    });
  };

  return (
    <div className="extrusions-page">
      <aside className="extrusions-sidebar">
        <div className="module-panel">
          <p className="eyebrow">Extrusions</p>
          <h2>Elevation Layout</h2>
          <p className="section-note">{statusMessage}</p>
          <div className="form-grid">
            <label className="field">
              <span>Panel-to-panel</span>
              <input
                value={normalizedLayout.panelToPanelExtrusionName}
                onChange={(event) =>
                  updateLayout({
                    ...normalizedLayout,
                    panelToPanelExtrusionName: event.target.value,
                  })
                }
              />
            </label>
            <label className="field">
              <span>Panel joint stick ft</span>
              <input
                min={1}
                step={0.5}
                type="number"
                value={normalizedLayout.panelToPanelStickLengthFeet ?? 20}
                onChange={(event) =>
                  updateLayout({
                    ...normalizedLayout,
                    panelToPanelStickLengthFeet: Math.max(1, Number(event.target.value) || 20),
                  })
                }
              />
            </label>
            <label className="field">
              <span>Edge</span>
              <input
                value={normalizedLayout.edgeExtrusionName}
                onChange={(event) =>
                  updateLayout({
                    ...normalizedLayout,
                    edgeExtrusionName: event.target.value,
                  })
                }
              />
            </label>
            <label className="field">
              <span>Edge stick ft</span>
              <input
                min={1}
                step={0.5}
                type="number"
                value={normalizedLayout.edgeStickLengthFeet ?? 20}
                onChange={(event) =>
                  updateLayout({
                    ...normalizedLayout,
                    edgeStickLengthFeet: Math.max(1, Number(event.target.value) || 20),
                  })
                }
              />
            </label>
          </div>
          <div className="form-actions">
            <button
              className="primary-button"
              disabled={busy || !canExportPdf}
              onClick={() => void onExportPdf()}
              type="button"
            >
              {busy ? 'Exporting...' : 'Export PDF'}
            </button>
            <button
              className="secondary-button"
              disabled={busy || !canExportExcel}
              onClick={() => void onExportExcel()}
              type="button"
            >
              Export Excel
            </button>
          </div>
        </div>

        <div className="module-panel extrusions-groups">
          <div className="results-sidebar__section-head">
            <span>Groups</span>
            <small>{panels.length} panels</small>
          </div>
          {normalizedLayout.groups.map((group) => {
            const count = panels.filter((panel) => panel.groupName === group.groupName).length;
            return (
              <button
                className={
                  group.groupName === activeGroup?.groupName
                    ? 'extrusions-group-button extrusions-group-button--active'
                    : 'extrusions-group-button'
                }
                key={group.groupName}
                onClick={() => setActiveGroupName(group.groupName)}
                type="button"
              >
                <strong>{group.groupName}</strong>
                <span>
                  {count} panels · {group.rows} x {group.columns}
                </span>
              </button>
            );
          })}
        </div>
      </aside>

      <main className="extrusions-workspace">
        {activeGroup ? (
          <>
            <section className="module-panel extrusions-toolbar">
              <div>
                <p className="eyebrow">{activeGroup.groupName}</p>
                <h3>Group Layout</h3>
              </div>
              <label className="field">
                <span>Rows</span>
                <input
                  min={1}
                  type="number"
                  value={activeGroup.rows}
                  onChange={(event) =>
                    updateActiveGroup({
                      ...activeGroup,
                      rows: Math.max(1, Number(event.target.value) || 1),
                    })
                  }
                />
              </label>
              <label className="field">
                <span>Columns</span>
                <input
                  min={1}
                  type="number"
                  value={activeGroup.columns}
                  onChange={(event) =>
                    updateActiveGroup({
                      ...activeGroup,
                      columns: Math.max(1, Number(event.target.value) || 1),
                    })
                  }
                />
              </label>
              <button
                className="secondary-button extrusions-toolbar-button"
                onClick={() =>
                  updateActiveGroup({
                    ...activeGroup,
                    cells: buildSortedCells(activePanels, activeGroup.columns),
                  })
                }
                type="button"
              >
                Sort by height
              </button>
            </section>

            <section className="extrusions-main">
              <div className="module-panel extrusions-viewer-panel">
                <ExtrusionViewer
                  group={activeGroup}
                  panels={activePanels}
                  selectedInstanceId={selectedInstanceId}
                  onMove={movePanel}
                  onSelect={setSelectedInstanceId}
                />
              </div>

              <aside className="module-panel extrusions-inspector">
                <div className="results-sidebar__section-head">
                  <span>Panel Inspector</span>
                  <small>{selectedPanel?.label ?? 'No selection'}</small>
                </div>
                {selectedPanel ? (
                  <>
                    <div className="extrusions-panel-meta">
                      <strong>{selectedPanel.label}</strong>
                      <span>
                        {formatDimension(selectedPanel.length)} x{' '}
                        {formatDimension(selectedPanel.width)}
                      </span>
                      {selectedCell ? (
                        <span>
                          Row {selectedCell.row + 1}, column {selectedCell.column + 1}
                        </span>
                      ) : null}
                    </div>
                    {selectedCell ? (
                      <div className="extrusions-move-pad" aria-label="Move selected panel">
                        <button
                          disabled={selectedCell.row === 0}
                          onClick={() =>
                            movePanel(selectedPanel.instanceId, selectedCell.row - 1, selectedCell.column)
                          }
                          type="button"
                        >
                          Up
                        </button>
                        <button
                          disabled={selectedCell.column === 0}
                          onClick={() =>
                            movePanel(selectedPanel.instanceId, selectedCell.row, selectedCell.column - 1)
                          }
                          type="button"
                        >
                          Left
                        </button>
                        <button
                          disabled={selectedCell.column >= activeGroup.columns - 1}
                          onClick={() =>
                            movePanel(selectedPanel.instanceId, selectedCell.row, selectedCell.column + 1)
                          }
                          type="button"
                        >
                          Right
                        </button>
                        <button
                          disabled={selectedCell.row >= activeGroup.rows - 1}
                          onClick={() =>
                            movePanel(selectedPanel.instanceId, selectedCell.row + 1, selectedCell.column)
                          }
                          type="button"
                        >
                          Down
                        </button>
                      </div>
                    ) : null}
                    <div className="extrusions-edge-editor">
                      {selectedSides.map((side) => {
                        const assignment = selectedEdges.find((item) => item.edge === side.edge);
                        const jointAssignment = activeGroup.jointAssignments.find(
                          (item) => item.jointId === side.jointId,
                        );
                        const isPanelJoint =
                          side.category === 'Panel-to-panel' &&
                          jointAssignment?.isEnabled !== false;
                        const currentName = isPanelJoint
                          ? (jointAssignment?.extrusionName ??
                            normalizedLayout.panelToPanelExtrusionName)
                          : (assignment?.extrusionName ?? normalizedLayout.edgeExtrusionName);
                        const displayedCategory = isPanelJoint ? 'Panel-to-panel' : 'Edge';
                        return (
                          <div className="extrusions-edge-row" key={side.edge}>
                            <strong>{side.edge}</strong>
                            <span
                              className={
                                isPanelJoint
                                  ? 'extrusions-edge-kind extrusions-edge-kind--joint'
                                  : 'extrusions-edge-kind'
                              }
                            >
                              {displayedCategory}
                            </span>
                            <div className="extrusions-side-mode">
                              <button
                                className={!isPanelJoint ? 'is-active' : undefined}
                                onClick={() =>
                                  updateActiveGroup({
                                    ...activeGroup,
                                    edgeAssignments: setEdgeExtrusionName(
                                      activeGroup.edgeAssignments,
                                      selectedPanel.instanceId,
                                      side.edge,
                                      assignment?.extrusionName ?? normalizedLayout.edgeExtrusionName,
                                      normalizedLayout.edgeExtrusionName,
                                    ),
                                    jointAssignments: side.jointId
                                      ? setJointEnabled(
                                          activeGroup.jointAssignments,
                                          side,
                                          false,
                                          normalizedLayout.panelToPanelExtrusionName,
                                        )
                                      : activeGroup.jointAssignments,
                                  })
                                }
                                type="button"
                              >
                                Edge
                              </button>
                              <button
                                className={isPanelJoint ? 'is-active' : undefined}
                                disabled={!side.secondInstanceId}
                                onClick={() =>
                                  updateActiveGroup({
                                    ...activeGroup,
                                    edgeAssignments: activeGroup.edgeAssignments.filter(
                                      (item) =>
                                        !(
                                          item.instanceId === selectedPanel.instanceId &&
                                          item.edge === side.edge
                                        ),
                                    ),
                                    jointAssignments: setJointEnabled(
                                      activeGroup.jointAssignments,
                                      side,
                                      true,
                                      normalizedLayout.panelToPanelExtrusionName,
                                    ),
                                  })
                                }
                                type="button"
                              >
                                Joint
                              </button>
                            </div>
                            <input
                              onChange={(event) =>
                                updateActiveGroup({
                                  ...activeGroup,
                                  edgeAssignments: isPanelJoint
                                    ? activeGroup.edgeAssignments
                                    : setEdgeExtrusionName(
                                        activeGroup.edgeAssignments,
                                        selectedPanel.instanceId,
                                        side.edge,
                                        event.target.value,
                                        normalizedLayout.edgeExtrusionName,
                                      ),
                                  jointAssignments: isPanelJoint
                                    ? setJointExtrusionName(
                                        activeGroup.jointAssignments,
                                        side,
                                        event.target.value,
                                        normalizedLayout.panelToPanelExtrusionName,
                                      )
                                    : activeGroup.jointAssignments,
                                })
                              }
                              placeholder={
                                isPanelJoint
                                  ? normalizedLayout.panelToPanelExtrusionName
                                  : normalizedLayout.edgeExtrusionName
                              }
                              type="text"
                              value={currentName}
                            />
                          </div>
                        );
                      })}
                    </div>
                  </>
                ) : (
                  <div className="empty-state">
                    <strong>Select a panel</strong>
                    <span>Use the elevation view or table to inspect edge assignments.</span>
                  </div>
                )}

                <div className="results-sidebar__section-head">
                  <span>Group Summary</span>
                  <small>{activeSummary.length} rows</small>
                </div>
                <div className="extrusions-summary-list">
                  {activeSummary.map((row) => (
                    <div className="extrusions-summary-row" key={row.key}>
                      <span>{row.category}</span>
                      <strong>{row.extrusionName}</strong>
                      <small>
                        {(row.totalLengthInches / 12).toFixed(2)} lf across {row.segmentCount} segments
                      </small>
                    </div>
                  ))}
                </div>
              </aside>
            </section>

            <section className="module-panel extrusions-table-panel">
              <div className="results-inspection__table-shell">
                <table className="results-inspection__table">
                  <thead>
                    <tr>
                      <th>Panel</th>
                      <th>Material</th>
                      <th>Size</th>
                      <th>Cell</th>
                    </tr>
                  </thead>
                  <tbody>
                    {activePanels.map((panel) => {
                      const cell = activeGroup.cells.find(
                        (item) => item.instanceId === panel.instanceId,
                      );
                      return (
                        <tr
                          className={
                            panel.instanceId === selectedInstanceId
                              ? 'table-row--active'
                              : undefined
                          }
                          key={panel.instanceId}
                          onClick={() => setSelectedInstanceId(panel.instanceId)}
                        >
                          <td>{panel.label}</td>
                          <td>{panel.materialName}</td>
                          <td>
                            {formatDimension(panel.length)} x {formatDimension(panel.width)}
                          </td>
                          <td>
                            {cell ? `${cell.row + 1}, ${cell.column + 1}` : 'Unplaced'}
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            </section>
          </>
        ) : (
          <div className="module-panel">
            <div className="empty-state">
              <strong>No panels imported</strong>
              <span>Import valid panel rows before creating extrusion layouts.</span>
            </div>
          </div>
        )}
      </main>
    </div>
  );
}

function ExtrusionViewer({
  group,
  panels,
  selectedInstanceId,
  onMove,
  onSelect,
}: {
  group: ExtrusionGroupLayout;
  panels: ExtrusionPanelInstance[];
  selectedInstanceId?: string;
  onMove: (instanceId: string, row: number, column: number) => void;
  onSelect: (instanceId: string) => void;
}) {
  const [zoom, setZoom] = useState(1);
  const [pan, setPan] = useState({ x: 0, y: 0 });
  const panDragRef = useRef<{ pointerId: number; x: number; y: number; panX: number; panY: number } | null>(null);
  const panelById = useMemo(
    () => new Map(panels.map((panel) => [panel.instanceId, panel])),
    [panels],
  );
  const cellByPosition = useMemo(
    () => new Map(group.cells.map((cell) => [`${cell.row}:${cell.column}`, cell])),
    [group.cells],
  );
  const columnTracks = useMemo(
    () => buildColumnTracks(group, panelById),
    [group, panelById],
  );
  const rowTracks = useMemo(
    () => buildRowTracks(group, panelById),
    [group, panelById],
  );

  return (
    <div className="extrusions-viewer">
      <div
        className="extrusions-viewer-scroll"
        onContextMenu={(event) => event.preventDefault()}
        onPointerDown={(event) => {
          if (event.button !== 1 && event.button !== 2) {
            return;
          }

          event.preventDefault();
          panDragRef.current = {
            pointerId: event.pointerId,
            x: event.clientX,
            y: event.clientY,
            panX: pan.x,
            panY: pan.y,
          };
          event.currentTarget.setPointerCapture(event.pointerId);
          event.currentTarget.classList.add('is-panning');
        }}
        onPointerMove={(event) => {
          const drag = panDragRef.current;
          if (!drag || drag.pointerId !== event.pointerId) {
            return;
          }

          event.preventDefault();
          setPan({
            x: drag.panX + event.clientX - drag.x,
            y: drag.panY + event.clientY - drag.y,
          });
        }}
        onPointerUp={(event) => {
          if (panDragRef.current?.pointerId === event.pointerId) {
            panDragRef.current = null;
            event.currentTarget.classList.remove('is-panning');
          }
        }}
        onPointerCancel={(event) => {
          if (panDragRef.current?.pointerId === event.pointerId) {
            panDragRef.current = null;
            event.currentTarget.classList.remove('is-panning');
          }
        }}
        onWheel={(event) => {
          event.preventDefault();
          const rect = event.currentTarget.getBoundingClientRect();
          const nextZoom = Math.min(2.5, Math.max(0.35, zoom * (event.deltaY < 0 ? 1.08 : 0.92)));
          const zoomRatio = nextZoom / zoom;
          const pointerX = event.clientX - rect.left;
          const pointerY = event.clientY - rect.top;
          setPan({
            x: pointerX - (pointerX - pan.x) * zoomRatio,
            y: pointerY - (pointerY - pan.y) * zoomRatio,
          });
          setZoom(nextZoom);
        }}
      >
        <div
          className="extrusions-viewer-grid"
          style={{
            gridTemplateColumns: columnTracks,
            gridTemplateRows: rowTracks,
            transform: `translate(${pan.x}px, ${pan.y}px) scale(${zoom})`,
          }}
        >
          {Array.from({ length: group.rows * group.columns }, (_, index) => {
        const row = Math.floor(index / group.columns);
        const column = index % group.columns;
        const cell = cellByPosition.get(`${row}:${column}`);
        const panel = cell ? panelById.get(cell.instanceId) : undefined;
        const sides = panel ? getPanelSides(group, panel.instanceId) : [];
        return (
          <div
            className={panel?.instanceId === selectedInstanceId ? 'extrusions-cell is-selected' : 'extrusions-cell'}
            key={`${row}:${column}`}
            onDragOver={(event) => event.preventDefault()}
            onDrop={(event) => {
              const instanceId = event.dataTransfer.getData('text/plain');
              if (instanceId) {
                onMove(instanceId, row, column);
              }
            }}
          >
            {panel ? (
              <button
                className="extrusions-panel-tile"
                draggable
                onClick={() => onSelect(panel.instanceId)}
                onDragStart={(event) => {
                  event.dataTransfer.effectAllowed = 'move';
                  event.dataTransfer.setData('text/plain', panel.instanceId);
                  onSelect(panel.instanceId);
                }}
                style={{
                  aspectRatio: `${Math.max(panel.length, 1)} / ${Math.max(panel.width, 1)}`,
                  height: 'calc(100% - 12px)',
                  width: 'calc(100% - 12px)',
                }}
                type="button"
              >
                {sides.map((side) => (
                  <span
                    aria-hidden="true"
                    className={
                      side.category === 'Panel-to-panel' &&
                      group.jointAssignments.find((item) => item.jointId === side.jointId)?.isEnabled !== false
                        ? `extrusions-panel-edge extrusions-panel-edge--${side.edge} extrusions-panel-edge--joint`
                        : `extrusions-panel-edge extrusions-panel-edge--${side.edge}`
                    }
                    key={side.edge}
                  />
                ))}
                <span className="extrusions-panel-label">{panel.label}</span>
              </button>
            ) : null}
          </div>
        );
          })}
        </div>
      </div>
    </div>
  );
}

function expandPanels(parts: PartRow[]): ExtrusionPanelInstance[] {
  return parts
    .filter((part) => part.validationStatus !== 'error' && part.quantity > 0 && part.length > 0 && part.width > 0)
    .flatMap((part, partIndex) => {
      const groupName = part.group?.trim() || 'Ungrouped';
      const sourceRowId = part.rowId || part.importedId || `panel-${partIndex + 1}`;
      const labelBase = part.importedId || part.rowId || `Panel ${partIndex + 1}`;
      return Array.from({ length: part.quantity }, (_, index) => ({
        instanceId: `${sourceRowId}#${index + 1}`,
        sourceRowId,
        importedId: part.importedId,
        quantityIndex: index + 1,
        label: `${labelBase}#${index + 1}`,
        materialName: part.materialName,
        groupName,
        length: part.length,
        width: part.width,
        isStale: false,
      }));
    });
}

function normalizeLayout(
  layout: ExtrusionLayoutState,
  panels: ExtrusionPanelInstance[],
): ExtrusionLayoutState {
  const existing = new Map(layout.groups.map((group) => [group.groupName, group]));
  const groups = Array.from(new Set(panels.map((panel) => panel.groupName))).sort();
  return {
    panelToPanelExtrusionName: layout.panelToPanelExtrusionName || 'Panel Joint',
    edgeExtrusionName: layout.edgeExtrusionName || 'Perimeter Edge',
    panelToPanelStickLengthFeet: normalizeStickLength(layout.panelToPanelStickLengthFeet),
    edgeStickLengthFeet: normalizeStickLength(layout.edgeStickLengthFeet),
    groups: groups.map((groupName) => normalizeGroup(groupName, panels, existing.get(groupName))),
  };
}

function normalizeGroup(
  groupName: string,
  panels: ExtrusionPanelInstance[],
  existing?: ExtrusionGroupLayout,
): ExtrusionGroupLayout {
  const groupPanels = panels.filter((panel) => panel.groupName === groupName);
  const columns = Math.max(existing?.columns ?? Math.ceil(Math.sqrt(groupPanels.length || 1)), 1);
  const rows = Math.max(existing?.rows ?? Math.ceil((groupPanels.length || 1) / columns), 1);
  const cells = [...(existing?.cells ?? [])].filter((cell) =>
    groupPanels.some((panel) => panel.instanceId === cell.instanceId),
  );
  sortPanelsForRows(groupPanels).forEach((panel) => {
    if (cells.some((cell) => cell.instanceId === panel.instanceId)) {
      return;
    }
    const index = cells.length;
    cells.push({
      instanceId: panel.instanceId,
      row: Math.floor(index / columns),
      column: index % columns,
    });
  });

  return {
    groupName,
    rows: Math.max(rows, ...cells.map((cell) => cell.row + 1), 1),
    columns: Math.max(columns, ...cells.map((cell) => cell.column + 1), 1),
    cells,
    edgeAssignments: (existing?.edgeAssignments ?? []).filter((edge) =>
      groupPanels.some((panel) => panel.instanceId === edge.instanceId),
    ),
    jointAssignments: existing?.jointAssignments ?? [],
  };
}

function sortPanelsForRows(panels: ExtrusionPanelInstance[]): ExtrusionPanelInstance[] {
  return [...panels].sort((first, second) => {
    const heightDelta = getPanelVisualHeight(second) - getPanelVisualHeight(first);
    if (Math.abs(heightDelta) > 0.001) {
      return heightDelta;
    }

    const widthDelta = getPanelVisualWidth(second) - getPanelVisualWidth(first);
    if (Math.abs(widthDelta) > 0.001) {
      return widthDelta;
    }

    return first.label.localeCompare(second.label, undefined, { numeric: true });
  });
}

function buildSortedCells(
  panels: ExtrusionPanelInstance[],
  columns: number,
): ExtrusionGroupLayout['cells'] {
  const safeColumns = Math.max(columns, 1);
  return sortPanelsForRows(panels).map((panel, index) => ({
    instanceId: panel.instanceId,
    row: Math.floor(index / safeColumns),
    column: index % safeColumns,
  }));
}

function buildColumnTracks(
  group: ExtrusionGroupLayout,
  panelById: Map<string, ExtrusionPanelInstance>,
): string {
  const scale = getViewerScale(panelById);
  const minimum = 34;
  const padding = 14;
  return Array.from({ length: group.columns }, (_, column) => {
    const maxWidth = Math.max(
      0,
      ...group.cells
        .filter((cell) => cell.column === column)
        .map((cell) => {
          const panel = panelById.get(cell.instanceId);
          return panel ? getPanelVisualWidth(panel) : 0;
        }),
    );
    return `${Math.max(minimum, Math.round(maxWidth * scale + padding))}px`;
  }).join(' ');
}

function buildRowTracks(
  group: ExtrusionGroupLayout,
  panelById: Map<string, ExtrusionPanelInstance>,
): string {
  const scale = getViewerScale(panelById);
  const minimum = 34;
  const padding = 14;
  return Array.from({ length: group.rows }, (_, row) => {
    const maxHeight = Math.max(
      0,
      ...group.cells
        .filter((cell) => cell.row === row)
        .map((cell) => {
          const panel = panelById.get(cell.instanceId);
          return panel ? getPanelVisualHeight(panel) : 0;
        }),
    );
    return `${Math.max(minimum, Math.round(maxHeight * scale + padding))}px`;
  }).join(' ');
}

function getViewerScale(panelById: Map<string, ExtrusionPanelInstance>): number {
  const maxDimension = Math.max(
    1,
    ...Array.from(panelById.values()).map((panel) =>
      Math.max(getPanelVisualWidth(panel), getPanelVisualHeight(panel)),
    ),
  );
  return Math.min(4, Math.max(1.4, 170 / maxDimension));
}

function getPanelVisualWidth(panel: ExtrusionPanelInstance): number {
  return Math.max(panel.length, 1);
}

function getPanelVisualHeight(panel: ExtrusionPanelInstance): number {
  return Math.max(panel.width, 1);
}

interface PanelSide {
  edge: ExtrusionEdgeAssignment['edge'];
  category: 'Edge' | 'Panel-to-panel';
  firstInstanceId: string;
  secondInstanceId?: string;
  jointId?: string;
}

function getPanelSides(group: ExtrusionGroupLayout, instanceId: string): PanelSide[] {
  const cell = group.cells.find((item) => item.instanceId === instanceId);
  if (!cell) {
    return edges.map((edge) => ({
      edge,
      category: 'Edge',
      firstInstanceId: instanceId,
    }));
  }

  const byPosition = new Map(
    group.cells.map((item) => [`${item.row}:${item.column}`, item]),
  );

  return edges.map((edge) => {
    const neighborPosition =
      edge === 'top'
        ? `${cell.row - 1}:${cell.column}`
        : edge === 'right'
          ? `${cell.row}:${cell.column + 1}`
          : edge === 'bottom'
            ? `${cell.row + 1}:${cell.column}`
            : `${cell.row}:${cell.column - 1}`;
    const neighbor = byPosition.get(neighborPosition);
    if (!neighbor) {
      return {
        edge,
        category: 'Edge',
        firstInstanceId: instanceId,
      };
    }

    return {
      edge,
      category: 'Panel-to-panel',
      firstInstanceId: instanceId,
      secondInstanceId: neighbor.instanceId,
      jointId: buildJointId(instanceId, neighbor.instanceId),
    };
  });
}

function setJointExtrusionName(
  assignments: ExtrusionGroupLayout['jointAssignments'],
  side: PanelSide,
  extrusionName: string,
  fallbackName: string,
): ExtrusionGroupLayout['jointAssignments'] {
  if (!side.secondInstanceId || !side.jointId) {
    return assignments;
  }

  const nextName = extrusionName.trim() || fallbackName;
  const exists = assignments.some((assignment) => assignment.jointId === side.jointId);
  if (!exists) {
    return [
      ...assignments,
      {
        jointId: side.jointId,
        firstInstanceId: side.firstInstanceId,
        secondInstanceId: side.secondInstanceId,
        edge: side.edge,
        extrusionName: nextName,
        isEnabled: true,
      },
    ];
  }

  return assignments.map((assignment) =>
    assignment.jointId === side.jointId
      ? { ...assignment, extrusionName, isEnabled: true }
      : assignment,
  );
}

function setJointEnabled(
  assignments: ExtrusionGroupLayout['jointAssignments'],
  side: PanelSide,
  isEnabled: boolean,
  fallbackName: string,
): ExtrusionGroupLayout['jointAssignments'] {
  if (!side.secondInstanceId || !side.jointId) {
    return assignments;
  }

  const exists = assignments.some((assignment) => assignment.jointId === side.jointId);
  if (!exists) {
    return [
      ...assignments,
      {
        jointId: side.jointId,
        firstInstanceId: side.firstInstanceId,
        secondInstanceId: side.secondInstanceId,
        edge: side.edge,
        extrusionName: fallbackName,
        isEnabled,
      },
    ];
  }

  return assignments.map((assignment) =>
    assignment.jointId === side.jointId ? { ...assignment, isEnabled } : assignment,
  );
}

function setEdgeExtrusionName(
  assignments: ExtrusionEdgeAssignment[],
  instanceId: string,
  edge: ExtrusionEdgeAssignment['edge'],
  extrusionName: string,
  fallbackName: string,
): ExtrusionEdgeAssignment[] {
  const exists = assignments.some(
    (assignment) => assignment.instanceId === instanceId && assignment.edge === edge,
  );

  if (!exists) {
    return [
      ...assignments,
      {
        instanceId,
        edge,
        extrusionName: extrusionName.trim() || fallbackName,
      },
    ];
  }

  return assignments.map((assignment) =>
    assignment.instanceId === instanceId && assignment.edge === edge
      ? { ...assignment, extrusionName }
      : assignment,
  );
}

function buildSegments(
  layout: ExtrusionLayoutState,
  panels: ExtrusionPanelInstance[],
): ExtrusionSegmentDetail[] {
  const panelById = new Map(panels.map((panel) => [panel.instanceId, panel]));
  return layout.groups.flatMap((group) => [
    ...detectVisibleEdges(group, panels, layout.edgeExtrusionName).flatMap((edge) => {
      const panel = panelById.get(edge.instanceId);
      if (!panel) {
        return [];
      }
      const assignment = group.edgeAssignments.find(
        (item) => item.instanceId === edge.instanceId && item.edge === edge.edge,
      );
      return [{
        groupName: group.groupName,
        category: 'Edge',
        extrusionName: assignment?.extrusionName || layout.edgeExtrusionName,
        location: `${panel.label} ${edge.edge}`,
        lengthInches: edge.edge === 'top' || edge.edge === 'bottom' ? panel.length : panel.width,
      }];
    }),
    ...detectJoints(group).flatMap((joint) => {
      const first = panelById.get(joint.first);
      const second = panelById.get(joint.second);
      if (!first || !second) {
        return [];
      }
      const jointId = buildJointId(joint.first, joint.second);
      const assignment = group.jointAssignments.find((item) => item.jointId === jointId);
      if (assignment?.isEnabled === false) {
        return [];
      }

      return [{
        groupName: group.groupName,
        category: 'Panel-to-panel',
        extrusionName: assignment?.extrusionName || layout.panelToPanelExtrusionName,
        location: `${first.label} / ${second.label}`,
        lengthInches: joint.edge === 'right'
          ? Math.min(first.width, second.width)
          : Math.min(first.length, second.length),
      }];
    }),
  ]);
}

function detectVisibleEdges(
  group: ExtrusionGroupLayout,
  panels: ExtrusionPanelInstance[],
  extrusionName: string,
): ExtrusionEdgeAssignment[] {
  const panelIds = new Set(panels.map((panel) => panel.instanceId));
  const byPosition = new Map(group.cells.map((cell) => [`${cell.row}:${cell.column}`, cell]));
  const seen = new Set<string>();
  return group.cells.flatMap((cell) => {
    if (!panelIds.has(cell.instanceId)) {
      return [];
    }

    return edges.flatMap((edge) => {
      const neighborPosition =
        edge === 'top'
          ? `${cell.row - 1}:${cell.column}`
          : edge === 'right'
            ? `${cell.row}:${cell.column + 1}`
            : edge === 'bottom'
              ? `${cell.row + 1}:${cell.column}`
              : `${cell.row}:${cell.column - 1}`;
      const neighbor = byPosition.get(neighborPosition);
      const jointId = neighbor ? buildJointId(cell.instanceId, neighbor.instanceId) : undefined;
      const disabledJoint = Boolean(
        jointId && group.jointAssignments.find((item) => item.jointId === jointId)?.isEnabled === false,
      );
      const explicitEdge = group.edgeAssignments.some(
        (item) => item.instanceId === cell.instanceId && item.edge === edge,
      );
      const key = `${cell.instanceId}|${edge}`;
      if ((neighbor && !disabledJoint && !explicitEdge) || seen.has(key)) {
        return [];
      }

      seen.add(key);
      return [{ instanceId: cell.instanceId, edge, extrusionName }];
    });
  });
}

function detectJoints(group: ExtrusionGroupLayout): Array<{ first: string; second: string; edge: string }> {
  const byPosition = new Map(group.cells.map((cell) => [`${cell.row}:${cell.column}`, cell]));
  return group.cells.flatMap((cell) => {
    const right = byPosition.get(`${cell.row}:${cell.column + 1}`);
    const bottom = byPosition.get(`${cell.row + 1}:${cell.column}`);
    return [
      right ? { first: cell.instanceId, second: right.instanceId, edge: 'right' } : null,
      bottom ? { first: cell.instanceId, second: bottom.instanceId, edge: 'bottom' } : null,
    ].filter((item): item is { first: string; second: string; edge: string } => Boolean(item));
  });
}

function buildJointId(first: string, second: string): string {
  return first.localeCompare(second) <= 0 ? `${first}|${second}` : `${second}|${first}`;
}

function summarizeSegments(segments: ExtrusionSegmentDetail[]) {
  const map = new Map<string, { key: string; category: string; extrusionName: string; totalLengthInches: number; segmentCount: number }>();
  segments.forEach((segment) => {
    const key = `${segment.category}|${segment.extrusionName}`;
    const current = map.get(key);
    if (current) {
      current.segmentCount += 1;
      current.totalLengthInches += segment.lengthInches;
    } else {
      map.set(key, {
        key,
        category: segment.category,
        extrusionName: segment.extrusionName,
        totalLengthInches: segment.lengthInches,
        segmentCount: 1,
      });
    }
  });
  return Array.from(map.values());
}

function normalizeStickLength(value: number | undefined): number {
  return value && value > 0 ? value : 20;
}

function formatDimension(value: number): string {
  return `${Number.isInteger(value) ? value : value.toFixed(2).replace(/0+$/, '').replace(/\.$/, '')}"`;
}
