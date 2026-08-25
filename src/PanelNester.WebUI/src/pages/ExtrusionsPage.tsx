import { useEffect, useMemo, useRef, useState } from 'react';
import type {
  ExtrusionAdditionalLineItem,
  ExtrusionEdgeAssignment,
  ExtrusionGridCell,
  ExtrusionGroupLayout,
  ExtrusionLayoutState,
  ExtrusionLineItemQuantityBasis,
  ExtrusionPanelInstance,
  ExtrusionSegmentDetail,
  OptimizationGroup,
  PartRow,
} from '../types/contracts';

interface ExtrusionsPageProps {
  importedRows: PartRow[];
  optimizationGroups: OptimizationGroup[];
  activeOptimizationGroupId?: string;
  layout: ExtrusionLayoutState;
  statusMessage: string;
  busy: boolean;
  canExportPdf: boolean;
  canExportExcel: boolean;
  onLayoutChange: (layout: ExtrusionLayoutState) => void;
  onLayoutSync: (layout: ExtrusionLayoutState) => void;
  onExportPdf: () => Promise<void>;
  onExportExcel: () => Promise<void>;
}

const edges = ['top', 'right', 'bottom', 'left'] as const;
type ExtrusionGroupingMode = 'group' | 'sheet-number';

export function ExtrusionsPage({
  importedRows,
  optimizationGroups,
  activeOptimizationGroupId,
  layout,
  statusMessage,
  busy,
  canExportPdf,
  canExportExcel,
  onLayoutChange,
  onLayoutSync,
  onExportPdf,
  onExportExcel,
}: ExtrusionsPageProps) {
  const orderedOptimizationGroups = useMemo(
    () => [...optimizationGroups].sort((left, right) => left.order - right.order),
    [optimizationGroups],
  );
  const panels = useMemo(
    () => expandPanels(importedRows, orderedOptimizationGroups),
    [importedRows, orderedOptimizationGroups],
  );
  const normalizedLayout = useMemo(
    () => normalizeLayout(layout, panels),
    [layout, panels],
  );
  const [selectedOptimizationGroupId, setSelectedOptimizationGroupId] = useState(
    activeOptimizationGroupId ?? orderedOptimizationGroups[0]?.optimizationGroupId ?? '',
  );
  const visiblePanels = panels.filter(
    (panel) =>
      !selectedOptimizationGroupId || panel.optimizationGroupId === selectedOptimizationGroupId,
  );
  const hasSheetNumbers = visiblePanels.some((panel) => panel.sheetNumber != null);
  const visibleGroups = normalizedLayout.groups.filter(
    (group) =>
      !selectedOptimizationGroupId ||
      (group.optimizationGroupId ?? '') === selectedOptimizationGroupId,
  );
  const [activeGroupKey, setActiveGroupKey] = useState<string>();
  const activeGroup =
    visibleGroups.find((group) => extrusionGroupKey(group) === activeGroupKey) ??
    visibleGroups[0];
  const [selectedInstanceIds, setSelectedInstanceIds] = useState<string[]>([]);
  const activePanels = panels.filter(
    (panel) =>
      activeGroup &&
      panel.optimizationGroupId === (activeGroup.optimizationGroupId ?? '') &&
      getPanelGroupName(panel, normalizedLayout.groupingMode as ExtrusionGroupingMode) ===
        activeGroup.groupName,
  );
  const segments = useMemo(
    () => buildSegments(normalizedLayout, panels),
    [normalizedLayout, panels],
  );
  const activeSummary = summarizeSegments(
    segments.filter(
      (segment) =>
        segment.optimizationGroupId === (activeGroup?.optimizationGroupId ?? '') &&
        segment.groupName === activeGroup?.groupName,
    ),
    normalizedLayout.additionalLineItems,
  );

  useEffect(() => {
    if (!activeGroup && visibleGroups[0]) {
      setActiveGroupKey(extrusionGroupKey(visibleGroups[0]));
      return;
    }

    if (
      activeGroupKey &&
      !visibleGroups.some((group) => extrusionGroupKey(group) === activeGroupKey)
    ) {
      setActiveGroupKey(visibleGroups[0] ? extrusionGroupKey(visibleGroups[0]) : undefined);
    }
  }, [activeGroup, activeGroupKey, visibleGroups]);

  useEffect(() => {
    if (
      activeOptimizationGroupId &&
      orderedOptimizationGroups.some(
        (group) => group.optimizationGroupId === activeOptimizationGroupId,
      )
    ) {
      setSelectedOptimizationGroupId(activeOptimizationGroupId);
    }
  }, [activeOptimizationGroupId, orderedOptimizationGroups]);

  useEffect(() => {
    if (JSON.stringify(layout) !== JSON.stringify(normalizedLayout)) {
      onLayoutSync(normalizedLayout);
    }
  }, [layout, normalizedLayout, onLayoutSync]);

  const updateLayout = (next: ExtrusionLayoutState) => {
    onLayoutChange(normalizeLayout(next, panels));
  };

  const updateActiveGroup = (nextGroup: ExtrusionGroupLayout) => {
    updateLayout({
      ...normalizedLayout,
      groups: normalizedLayout.groups.map((group) =>
        extrusionGroupKey(group) === extrusionGroupKey(nextGroup) ? nextGroup : group,
      ),
    });
  };

  const updateAdditionalLineItem = (
    id: string,
    changes: Partial<ExtrusionAdditionalLineItem>,
  ) => {
    updateLayout({
      ...normalizedLayout,
      additionalLineItems: normalizedLayout.additionalLineItems.map((item) =>
        item.id === id ? { ...item, ...changes } : item,
      ),
    });
  };

  const selectedInstanceId = selectedInstanceIds[0];
  const selectedPanel = panels.find((panel) => panel.instanceId === selectedInstanceId);
  const selectedPanels = panels.filter((panel) => selectedInstanceIds.includes(panel.instanceId));
  const selectedCell = activeGroup?.cells.find((cell) => cell.instanceId === selectedInstanceId);
  const selectedEdges =
    activeGroup?.edgeAssignments.filter(
      (assignment) => selectedInstanceIds.includes(assignment.instanceId),
    ) ?? [];
  const selectedSides =
    selectedPanel && activeGroup
      ? getPanelSides(activeGroup, selectedPanel.instanceId)
      : [];

  useEffect(() => {
    const activeIds = new Set(activePanels.map((panel) => panel.instanceId));
    setSelectedInstanceIds((current) => current.filter((instanceId) => activeIds.has(instanceId)));
  }, [activePanels]);

  const replaceSelection = (instanceId: string) => {
    setSelectedInstanceIds([instanceId]);
  };

  const toggleSelection = (instanceId: string) => {
    setSelectedInstanceIds((current) =>
      current.includes(instanceId)
        ? current.filter((selectedId) => selectedId !== instanceId)
        : [...current, instanceId],
    );
  };

  const setSelection = (instanceIds: string[]) => {
    setSelectedInstanceIds(Array.from(new Set(instanceIds)));
  };

  const movePanel = (instanceId: string, row: number, column: number) => {
    if (!activeGroup) {
      return;
    }

    const sourceCell = activeGroup.cells.find((cell) => cell.instanceId === instanceId);
    if (!sourceCell) {
      return;
    }

    const idsToMove =
      selectedInstanceIds.includes(instanceId) && selectedInstanceIds.length > 1
        ? selectedInstanceIds
        : [instanceId];
    const selectedIdSet = new Set(idsToMove);
    const rowDelta = row - sourceCell.row;
    const columnDelta = column - sourceCell.column;
    const nextPositions = new Map<string, { row: number; column: number }>();
    for (const cell of activeGroup.cells) {
      if (!selectedIdSet.has(cell.instanceId)) {
        continue;
      }

      const nextRow = cell.row + rowDelta;
      const nextColumn = cell.column + columnDelta;
      if (
        nextRow < 0 ||
        nextColumn < 0 ||
        nextRow >= activeGroup.rows ||
        nextColumn >= activeGroup.columns
      ) {
        return;
      }

      nextPositions.set(cell.instanceId, { row: nextRow, column: nextColumn });
    }

    const originalSelectedPositions = new Set(
      activeGroup.cells
        .filter((cell) => selectedIdSet.has(cell.instanceId))
        .map((cell) => `${cell.row}:${cell.column}`),
    );
    const nextSelectedPositions = new Set(
      Array.from(nextPositions.values()).map((position) => `${position.row}:${position.column}`),
    );
    const displacedPositions = new Set<string>();
    const blocked = activeGroup.cells.some((cell) => {
      if (selectedIdSet.has(cell.instanceId)) {
        return false;
      }

      if (!nextSelectedPositions.has(`${cell.row}:${cell.column}`)) {
        return false;
      }

      const displacedPosition = {
        row: cell.row - rowDelta,
        column: cell.column - columnDelta,
      };
      const displacedKey = `${displacedPosition.row}:${displacedPosition.column}`;
      if (
        displacedPosition.row < 0 ||
        displacedPosition.column < 0 ||
        displacedPosition.row >= activeGroup.rows ||
        displacedPosition.column >= activeGroup.columns ||
        !originalSelectedPositions.has(displacedKey) ||
        displacedPositions.has(displacedKey)
      ) {
        return true;
      }

      displacedPositions.add(displacedKey);
      nextPositions.set(cell.instanceId, displacedPosition);
      return false;
    });
    if (blocked) {
      return;
    }

    updateActiveGroup({
      ...activeGroup,
      cells: activeGroup.cells.map((cell) => {
        const nextPosition = nextPositions.get(cell.instanceId);
        return nextPosition ? { ...cell, ...nextPosition } : cell;
      }),
    });
  };

  const applySelectedSideMode = (side: PanelSide, mode: 'edge' | 'joint' | 'ignore') => {
    if (!activeGroup) {
      return;
    }

    const sides = selectedPanels.flatMap((panel) =>
      getPanelSides(activeGroup, panel.instanceId).filter((candidate) => candidate.edge === side.edge),
    );
    updateActiveGroup({
      ...activeGroup,
      edgeAssignments: sides.reduce(
        (assignments, candidate) =>
          mode === 'joint'
            ? assignments.filter(
                (item) =>
                  !(item.instanceId === candidate.firstInstanceId && item.edge === candidate.edge),
              )
            : setEdgeExtrusionName(
                assignments,
                candidate.firstInstanceId,
                candidate.edge,
                normalizedLayout.edgeExtrusionName,
                normalizedLayout.edgeExtrusionName,
                mode === 'ignore',
              ),
        activeGroup.edgeAssignments,
      ),
      jointAssignments: sides.reduce(
        (assignments, candidate) =>
          setJointEnabled(
            assignments,
            candidate,
            mode === 'joint',
            normalizedLayout.panelToPanelExtrusionName,
          ),
        activeGroup.jointAssignments,
      ),
    });
  };

  const applySelectedExtrusionName = (side: PanelSide, extrusionName: string) => {
    if (!activeGroup) {
      return;
    }

    const sides = selectedPanels.flatMap((panel) =>
      getPanelSides(activeGroup, panel.instanceId).filter((candidate) => candidate.edge === side.edge),
    );
    updateActiveGroup({
      ...activeGroup,
      edgeAssignments: sides.reduce(
        (assignments, candidate) => {
          const mode = getSideMode(activeGroup, candidate);
          return mode === 'edge'
            ? setEdgeExtrusionName(
                assignments,
                candidate.firstInstanceId,
                candidate.edge,
                extrusionName,
                normalizedLayout.edgeExtrusionName,
                false,
              )
            : assignments;
        },
        activeGroup.edgeAssignments,
      ),
      jointAssignments: sides.reduce(
        (assignments, candidate) =>
          getSideMode(activeGroup, candidate) === 'joint'
            ? setJointExtrusionName(
                assignments,
                candidate,
                extrusionName,
                normalizedLayout.panelToPanelExtrusionName,
              )
            : assignments,
        activeGroup.jointAssignments,
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
          <div className="extrusions-line-items">
            <div className="results-sidebar__section-head">
              <span>Additional Line Items</span>
              <button
                className="secondary-button extrusions-add-line-item-button"
                onClick={() =>
                  updateLayout({
                    ...normalizedLayout,
                    additionalLineItems: [
                      ...normalizedLayout.additionalLineItems,
                      {
                        id: `line-item-${Date.now()}`,
                        name: '',
                        quantityBasis: 'both',
                        stickLengthFeet: 20,
                      },
                    ],
                  })
                }
                type="button"
              >
                Add row
              </button>
            </div>
            {normalizedLayout.additionalLineItems.length > 0 ? (
              <div className="extrusions-line-item-list">
                {normalizedLayout.additionalLineItems.map((item) => (
                  <div className="extrusions-line-item-row" key={item.id}>
                    <label className="field">
                      <span>Name</span>
                      <input
                        onChange={(event) =>
                          updateAdditionalLineItem(item.id, { name: event.target.value })
                        }
                        placeholder="Line item name"
                        value={item.name}
                      />
                    </label>
                    <label className="field">
                      <span>Based on</span>
                      <select
                        onChange={(event) =>
                          updateAdditionalLineItem(item.id, {
                            quantityBasis: event.target.value as ExtrusionLineItemQuantityBasis,
                          })
                        }
                        value={item.quantityBasis}
                      >
                        <option value="panel-to-panel">Panel-to-panel</option>
                        <option value="edge">Edge</option>
                        <option value="both">Both</option>
                      </select>
                    </label>
                    <label className="field">
                      <span>Stick ft</span>
                      <input
                        min={1}
                        onChange={(event) =>
                          updateAdditionalLineItem(item.id, {
                            stickLengthFeet: Math.max(1, Number(event.target.value) || 20),
                          })
                        }
                        step={0.5}
                        type="number"
                        value={item.stickLengthFeet ?? 20}
                      />
                    </label>
                    <button
                      className="secondary-button extrusions-remove-line-item-button"
                      onClick={() =>
                        updateLayout({
                          ...normalizedLayout,
                          additionalLineItems: normalizedLayout.additionalLineItems.filter(
                            (candidate) => candidate.id !== item.id,
                          ),
                        })
                      }
                      type="button"
                    >
                      Remove
                    </button>
                  </div>
                ))}
              </div>
            ) : null}
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
          {orderedOptimizationGroups.length > 0 ? (
            <label className="field">
              <span>Optimization Group</span>
              <select
                value={selectedOptimizationGroupId}
                onChange={(event) => {
                  setSelectedOptimizationGroupId(event.target.value);
                  setActiveGroupKey(undefined);
                }}
              >
                {orderedOptimizationGroups.map((group) => (
                  <option key={group.optimizationGroupId} value={group.optimizationGroupId}>
                    {group.name}
                  </option>
                ))}
              </select>
            </label>
          ) : null}
          <div className="results-sidebar__section-head">
            <span>Part Groups</span>
            <small>{visiblePanels.length} panels</small>
          </div>
          <div className="segmented-control" aria-label="Extrusion grouping">
            <button
              className={normalizedLayout.groupingMode !== 'sheet-number' ? 'is-active' : undefined}
              onClick={() =>
                updateLayout({
                  ...normalizedLayout,
                  groupingMode: 'group',
                  groups: [],
                })
              }
              type="button"
            >
              Part Group
            </button>
            <button
              className={normalizedLayout.groupingMode === 'sheet-number' ? 'is-active' : undefined}
              disabled={!hasSheetNumbers}
              onClick={() =>
                updateLayout({
                  ...normalizedLayout,
                  groupingMode: 'sheet-number',
                  groups: [],
                })
              }
              type="button"
            >
              Sheet
            </button>
          </div>
          {visibleGroups.map((group) => {
            const count = panels.filter(
              (panel) =>
                panel.optimizationGroupId === (group.optimizationGroupId ?? '') &&
                getPanelGroupName(panel, normalizedLayout.groupingMode as ExtrusionGroupingMode) ===
                group.groupName,
            ).length;
            return (
              <button
                className={
                  extrusionGroupKey(group) === (activeGroup ? extrusionGroupKey(activeGroup) : '')
                    ? 'extrusions-group-button extrusions-group-button--active'
                    : 'extrusions-group-button'
                }
                key={extrusionGroupKey(group)}
                onClick={() => setActiveGroupKey(extrusionGroupKey(group))}
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
                <h3>Part Group Layout</h3>
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
                  selectedInstanceIds={selectedInstanceIds}
                  onMove={movePanel}
                  onReplaceSelection={replaceSelection}
                  onSelectMany={setSelection}
                  onToggleSelection={toggleSelection}
                />
              </div>

              <aside className="module-panel extrusions-inspector">
                <div className="results-sidebar__section-head">
                  <span>Panel Inspector</span>
                  <small>
                    {selectedPanels.length > 1
                      ? `${selectedPanels.length} selected`
                      : selectedPanel?.label ?? 'No selection'}
                  </small>
                </div>
                {selectedPanel ? (
                  <>
                    <div className="extrusions-panel-meta">
                      <strong>
                        {selectedPanels.length > 1
                          ? `${selectedPanels.length} panels selected`
                          : selectedPanel.label}
                      </strong>
                      <span>
                        {formatDimension(selectedPanel.length)} x{' '}
                        {formatDimension(selectedPanel.width)}
                      </span>
                      {selectedCell ? (
                        <span>
                          Row {getBuildingRowNumber(activeGroup, selectedCell)}, column {selectedCell.column + 1}
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
                        const sideStates = selectedPanels.map((panel) => {
                          const matchingSide = getPanelSides(activeGroup, panel.instanceId).find(
                            (candidate) => candidate.edge === side.edge,
                          ) ?? side;
                          return getSideEditorState(
                            activeGroup,
                            matchingSide,
                            normalizedLayout.edgeExtrusionName,
                            normalizedLayout.panelToPanelExtrusionName,
                          );
                        });
                        const modeValue = getMixedValue(sideStates.map((state) => state.mode));
                        const extrusionValue = getMixedValue(
                          sideStates
                            .filter((state) => state.mode !== 'ignore')
                            .map((state) => state.extrusionName),
                        );
                        const assignment = selectedEdges.find(
                          (item) => item.instanceId === selectedPanel.instanceId && item.edge === side.edge,
                        );
                        const jointAssignment = activeGroup.jointAssignments.find(
                          (item) => item.jointId === side.jointId,
                        );
                        const isIgnored = assignment?.isIgnored === true;
                        const isPanelJoint =
                          !isIgnored &&
                          side.category === 'Panel-to-panel' &&
                          (side.secondInstanceId
                            ? jointAssignment?.isEnabled !== false
                            : jointAssignment?.isEnabled === true);
                        const displayedCategory = modeValue === 'varies'
                          ? 'Varies'
                          : modeValue === 'ignore'
                            ? 'Ignore'
                            : modeValue === 'joint'
                              ? 'Panel-to-panel'
                              : 'Edge';
                        return (
                          <div className="extrusions-edge-row" key={side.edge}>
                            <strong>{side.edge}</strong>
                            <span
                              className={
                                isIgnored
                                  ? 'extrusions-edge-kind extrusions-edge-kind--ignored'
                                  : isPanelJoint
                                  ? 'extrusions-edge-kind extrusions-edge-kind--joint'
                                  : 'extrusions-edge-kind'
                              }
                            >
                              {displayedCategory}
                            </span>
                            <div className="extrusions-side-mode">
                              <button
                                className={modeValue === 'edge' ? 'is-active' : undefined}
                                onClick={() => applySelectedSideMode(side, 'edge')}
                                type="button"
                              >
                                Edge
                              </button>
                              <button
                                className={modeValue === 'joint' ? 'is-active' : undefined}
                                onClick={() => applySelectedSideMode(side, 'joint')}
                                type="button"
                              >
                                Joint
                              </button>
                              <button
                                className={modeValue === 'ignore' ? 'is-active' : undefined}
                                onClick={() => applySelectedSideMode(side, 'ignore')}
                                type="button"
                              >
                                Ignore
                              </button>
                            </div>
                            {modeValue !== 'ignore' ? (
                              <input
                              onChange={(event) => applySelectedExtrusionName(side, event.target.value)}
                              placeholder={
                                extrusionValue === 'varies'
                                  ? 'varies'
                                  : modeValue === 'joint'
                                  ? normalizedLayout.panelToPanelExtrusionName
                                  : normalizedLayout.edgeExtrusionName
                              }
                              type="text"
                              value={extrusionValue === 'varies' ? '' : extrusionValue}
                              />
                            ) : null}
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
                  <span>Part Group Summary</span>
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
                            selectedInstanceIds.includes(panel.instanceId)
                              ? 'table-row--active'
                              : undefined
                          }
                          key={panel.instanceId}
                          onClick={(event) =>
                            event.shiftKey ? toggleSelection(panel.instanceId) : replaceSelection(panel.instanceId)
                          }
                        >
                          <td>{panel.label}</td>
                          <td>{panel.materialName}</td>
                          <td>
                            {formatDimension(panel.length)} x {formatDimension(panel.width)}
                          </td>
                          <td>
                            {cell ? `${getBuildingRowNumber(activeGroup, cell)}, ${cell.column + 1}` : 'Unplaced'}
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
  selectedInstanceIds,
  onMove,
  onReplaceSelection,
  onSelectMany,
  onToggleSelection,
}: {
  group: ExtrusionGroupLayout;
  panels: ExtrusionPanelInstance[];
  selectedInstanceIds: string[];
  onMove: (instanceId: string, row: number, column: number) => void;
  onReplaceSelection: (instanceId: string) => void;
  onSelectMany: (instanceIds: string[]) => void;
  onToggleSelection: (instanceId: string) => void;
}) {
  const [zoom, setZoom] = useState(1);
  const [pan, setPan] = useState({ x: 0, y: 0 });
  const panDragRef = useRef<{ pointerId: number; x: number; y: number; panX: number; panY: number } | null>(null);
  const selectionDragRef = useRef<{
    pointerId: number;
    startX: number;
    startY: number;
    additive: boolean;
    baseSelection: string[];
  } | null>(null);
  const [selectionBox, setSelectionBox] = useState<{ left: number; top: number; width: number; height: number }>();
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
          if (event.button === 0) {
            const target = event.target as HTMLElement;
            if (target.closest('.extrusions-panel-tile')) {
              return;
            }

            event.preventDefault();
            const rect = event.currentTarget.getBoundingClientRect();
            selectionDragRef.current = {
              pointerId: event.pointerId,
              startX: event.clientX,
              startY: event.clientY,
              additive: event.shiftKey,
              baseSelection: selectedInstanceIds,
            };
            setSelectionBox({
              left: event.clientX - rect.left,
              top: event.clientY - rect.top,
              width: 0,
              height: 0,
            });
            event.currentTarget.setPointerCapture(event.pointerId);
            return;
          }

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
          const selectionDrag = selectionDragRef.current;
          if (selectionDrag?.pointerId === event.pointerId) {
            event.preventDefault();
            const rect = event.currentTarget.getBoundingClientRect();
            const left = Math.min(selectionDrag.startX, event.clientX) - rect.left;
            const top = Math.min(selectionDrag.startY, event.clientY) - rect.top;
            const width = Math.abs(event.clientX - selectionDrag.startX);
            const height = Math.abs(event.clientY - selectionDrag.startY);
            setSelectionBox({ left, top, width, height });
            return;
          }

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
          const selectionDrag = selectionDragRef.current;
          if (selectionDrag?.pointerId === event.pointerId) {
            const selectedByBox = collectPanelIdsInBox(event.currentTarget, selectionBox);
            const nextSelection = selectionDrag.additive
              ? Array.from(new Set([...selectionDrag.baseSelection, ...selectedByBox]))
              : selectedByBox;
            onSelectMany(nextSelection);
            selectionDragRef.current = null;
            setSelectionBox(undefined);
            return;
          }

          if (panDragRef.current?.pointerId === event.pointerId) {
            panDragRef.current = null;
            event.currentTarget.classList.remove('is-panning');
          }
        }}
        onPointerCancel={(event) => {
          if (selectionDragRef.current?.pointerId === event.pointerId) {
            selectionDragRef.current = null;
            setSelectionBox(undefined);
          }

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
            className={panel && selectedInstanceIds.includes(panel.instanceId) ? 'extrusions-cell is-selected' : 'extrusions-cell'}
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
                data-instance-id={panel.instanceId}
                className="extrusions-panel-tile"
                draggable
                onClick={(event) =>
                  event.shiftKey ? onToggleSelection(panel.instanceId) : onReplaceSelection(panel.instanceId)
                }
                onDragStart={(event) => {
                  event.dataTransfer.effectAllowed = 'move';
                  event.dataTransfer.setData('text/plain', panel.instanceId);
                  if (!selectedInstanceIds.includes(panel.instanceId)) {
                    onReplaceSelection(panel.instanceId);
                  }
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
                    className={getPanelEdgeClassName(group, side)}
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
        {selectionBox ? <div className="extrusions-selection-box" style={selectionBox} /> : null}
      </div>
    </div>
  );
}

function expandPanels(
  parts: PartRow[],
  optimizationGroups: OptimizationGroup[],
): ExtrusionPanelInstance[] {
  const ownerByRowId = new Map(
    optimizationGroups.flatMap((group) =>
      group.parts.map((part) => [
        part.rowId,
        {
          id: group.optimizationGroupId,
          name: group.name,
          order: group.order,
        },
      ] as const),
    ),
  );
  return parts
    .filter((part) => part.validationStatus !== 'error' && part.quantity > 0 && part.length > 0 && part.width > 0)
    .flatMap((part, partIndex) => {
      const groupName = part.group?.trim() || 'Ungrouped';
      const sheetGroupName = part.sheetNumber?.trim() ? `Sheet ${part.sheetNumber.trim()}` : 'Ungrouped';
      const sourceRowId = part.rowId || part.importedId || `panel-${partIndex + 1}`;
      const labelBase = part.importedId || part.rowId || `Panel ${partIndex + 1}`;
      const owner = ownerByRowId.get(part.rowId);
      return Array.from({ length: part.quantity }, (_, index) => ({
        optimizationGroupId: owner?.id ?? '',
        optimizationGroupName: owner?.name ?? '',
        optimizationGroupOrder: owner?.order ?? 0,
        instanceId: `${sourceRowId}#${index + 1}`,
        sourceRowId,
        importedId: part.importedId,
        quantityIndex: index + 1,
        label: `${labelBase}#${index + 1}`,
        materialName: part.materialName,
        groupName,
        sheetGroupName,
        sheetNumber: part.sheetNumber ?? null,
        rowNumber: part.rowNumber ?? null,
        columnNumber: part.columnNumber ?? null,
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
  const groupingMode = normalizeGroupingMode(layout.groupingMode, panels);
  const existing = new Map(layout.groups.map((group) => [extrusionGroupKey(group), group]));
  const groups = Array.from(
    new Map(
      panels.map((panel) => {
        const groupName = getPanelGroupName(panel, groupingMode);
        const key = `${panel.optimizationGroupId}\u001f${groupName}`;
        return [key, { key, groupName, panel }] as const;
      }),
    ).values(),
  ).sort(
    (left, right) =>
      left.panel.optimizationGroupOrder - right.panel.optimizationGroupOrder ||
      left.groupName.localeCompare(right.groupName),
  );
  return {
    groupingMode,
    panelToPanelExtrusionName: layout.panelToPanelExtrusionName || 'Panel Joint',
    edgeExtrusionName: layout.edgeExtrusionName || 'Perimeter Edge',
    panelToPanelStickLengthFeet: normalizeStickLength(layout.panelToPanelStickLengthFeet),
    edgeStickLengthFeet: normalizeStickLength(layout.edgeStickLengthFeet),
    additionalLineItems: normalizeAdditionalLineItems(layout.additionalLineItems),
    groups: groups.map(({ key, groupName, panel }) =>
      normalizeGroup(
        panel.optimizationGroupId,
        panel.optimizationGroupName,
        groupName,
        panels,
        groupingMode,
        existing.get(key) ?? existing.get(`\u001f${groupName}`),
      ),
    ),
  };
}

function normalizeGroup(
  optimizationGroupId: string,
  optimizationGroupName: string,
  groupName: string,
  panels: ExtrusionPanelInstance[],
  groupingMode: ExtrusionGroupingMode,
  existing?: ExtrusionGroupLayout,
): ExtrusionGroupLayout {
  const groupPanels = panels.filter(
    (panel) =>
      panel.optimizationGroupId === optimizationGroupId &&
      getPanelGroupName(panel, groupingMode) === groupName,
  );
  const columns = Math.max(existing?.columns ?? Math.ceil(Math.sqrt(groupPanels.length || 1)), 1);
  let rows = Math.max(existing?.rows ?? Math.ceil((groupPanels.length || 1) / columns), 1);
  const maximumImportedRowNumber = Math.max(
    0,
    ...groupPanels.map((panel) => panel.rowNumber ?? 0),
  );
  rows = Math.max(rows, maximumImportedRowNumber);
  const cells = [...(existing?.cells ?? [])].filter((cell) =>
    groupPanels.some((panel) => panel.instanceId === cell.instanceId),
  );
  sortPanelsForRows(groupPanels).forEach((panel) => {
    if (cells.some((cell) => cell.instanceId === panel.instanceId)) {
      return;
    }
    if (panel.rowNumber != null && panel.columnNumber != null) {
      const row = rows - panel.rowNumber;
      const column = panel.columnNumber - 1;
      const occupied = cells.some((cell) => cell.row === row && cell.column === column);
      if (!occupied) {
        cells.push({
          instanceId: panel.instanceId,
          row,
          column,
        });
        return;
      }
    }

    const index = cells.length;
    cells.push({
      instanceId: panel.instanceId,
      row: Math.floor(index / columns),
      column: index % columns,
    });
  });

  return {
    optimizationGroupId,
    optimizationGroupName,
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

function extrusionGroupKey(group: ExtrusionGroupLayout): string {
  return `${group.optimizationGroupId ?? ''}\u001f${group.groupName}`;
}

function getBuildingRowNumber(group: ExtrusionGroupLayout, cell: ExtrusionGridCell): number {
  return Math.max(1, group.rows - cell.row);
}

function normalizeGroupingMode(
  value: ExtrusionLayoutState['groupingMode'],
  panels: ExtrusionPanelInstance[],
): ExtrusionGroupingMode {
  if (value === 'group' || value === 'sheet-number') {
    return value;
  }

  return panels.some((panel) => panel.sheetNumber != null) ? 'sheet-number' : 'group';
}

function getPanelGroupName(panel: ExtrusionPanelInstance, groupingMode: ExtrusionGroupingMode): string {
  return groupingMode === 'sheet-number' ? panel.sheetGroupName : panel.groupName;
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

function collectPanelIdsInBox(
  container: HTMLElement,
  box?: { left: number; top: number; width: number; height: number },
): string[] {
  if (!box || (box.width < 4 && box.height < 4)) {
    return [];
  }

  const containerRect = container.getBoundingClientRect();
  const selectionRect = {
    left: containerRect.left + box.left,
    top: containerRect.top + box.top,
    right: containerRect.left + box.left + box.width,
    bottom: containerRect.top + box.top + box.height,
  };

  return Array.from(container.querySelectorAll<HTMLElement>('.extrusions-panel-tile'))
    .filter((element) => {
      const rect = element.getBoundingClientRect();
      return (
        rect.left <= selectionRect.right &&
        rect.right >= selectionRect.left &&
        rect.top <= selectionRect.bottom &&
        rect.bottom >= selectionRect.top
      );
    })
    .map((element) => element.dataset.instanceId)
    .filter((instanceId): instanceId is string => Boolean(instanceId));
}

interface PanelSide {
  edge: ExtrusionEdgeAssignment['edge'];
  category: 'Edge' | 'Panel-to-panel';
  firstInstanceId: string;
  secondInstanceId?: string;
  jointId?: string;
}

type SideMode = 'edge' | 'joint' | 'ignore';

interface SideEditorState {
  extrusionName: string;
  mode: SideMode;
}

function getMixedValue<T extends string>(values: T[]): T | 'varies' {
  const uniqueValues = Array.from(new Set(values));
  return uniqueValues.length === 1 ? uniqueValues[0] : 'varies';
}

function getSideMode(group: ExtrusionGroupLayout, side: PanelSide): SideMode {
  const edgeAssignment = group.edgeAssignments.find(
    (assignment) =>
      assignment.instanceId === side.firstInstanceId && assignment.edge === side.edge,
  );
  if (edgeAssignment?.isIgnored === true) {
    return 'ignore';
  }

  if (side.category !== 'Panel-to-panel' || !side.jointId) {
    return 'edge';
  }

  const jointAssignment = group.jointAssignments.find(
    (assignment) => assignment.jointId === side.jointId,
  );
  const isPanelJoint = side.secondInstanceId
    ? jointAssignment?.isEnabled !== false && edgeAssignment === undefined
    : jointAssignment?.isEnabled === true;
  return isPanelJoint ? 'joint' : 'edge';
}

function getSideEditorState(
  group: ExtrusionGroupLayout,
  side: PanelSide,
  edgeFallbackName: string,
  jointFallbackName: string,
): SideEditorState {
  const mode = getSideMode(group, side);
  if (mode === 'ignore') {
    return { extrusionName: '', mode };
  }

  if (mode === 'joint') {
    const jointAssignment = group.jointAssignments.find(
      (assignment) => assignment.jointId === side.jointId,
    );
    return {
      extrusionName: jointAssignment?.extrusionName ?? jointFallbackName,
      mode,
    };
  }

  const edgeAssignment = group.edgeAssignments.find(
    (assignment) =>
      assignment.instanceId === side.firstInstanceId && assignment.edge === side.edge,
  );
  return {
    extrusionName: edgeAssignment?.extrusionName ?? edgeFallbackName,
    mode,
  };
}

function getPanelEdgeClassName(group: ExtrusionGroupLayout, side: PanelSide): string {
  const baseClassName = `extrusions-panel-edge extrusions-panel-edge--${side.edge}`;
  const edgeAssignment = group.edgeAssignments.find(
    (assignment) =>
      assignment.instanceId === side.firstInstanceId && assignment.edge === side.edge,
  );

  if (edgeAssignment?.isIgnored === true) {
    return `${baseClassName} extrusions-panel-edge--ignored`;
  }

  if (side.category !== 'Panel-to-panel' || !side.jointId) {
    return baseClassName;
  }

  const jointAssignment = group.jointAssignments.find(
    (assignment) => assignment.jointId === side.jointId,
  );
  const isPanelJoint = side.secondInstanceId
    ? jointAssignment?.isEnabled !== false && edgeAssignment === undefined
    : jointAssignment?.isEnabled === true;

  return isPanelJoint ? `${baseClassName} extrusions-panel-edge--joint` : baseClassName;
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
        category: 'Panel-to-panel',
        firstInstanceId: instanceId,
        jointId: buildBoundaryJointId(instanceId, edge),
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
  if (!side.jointId) {
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
        secondInstanceId: side.secondInstanceId ?? '',
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
  if (!side.jointId) {
    return assignments;
  }

  const exists = assignments.some((assignment) => assignment.jointId === side.jointId);
  if (!exists) {
    return [
      ...assignments,
      {
        jointId: side.jointId,
        firstInstanceId: side.firstInstanceId,
        secondInstanceId: side.secondInstanceId ?? '',
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
  isIgnored = false,
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
        isIgnored,
      },
    ];
  }

  return assignments.map((assignment) =>
    assignment.instanceId === instanceId && assignment.edge === edge
      ? { ...assignment, extrusionName, isIgnored }
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
        optimizationGroupId: group.optimizationGroupId ?? '',
        optimizationGroupName: group.optimizationGroupName ?? '',
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
      if (!first || (!second && !joint.isBoundary)) {
        return [];
      }
      const jointId = buildJointId(joint.first, joint.second);
      const normalizedJointId = joint.isBoundary ? buildBoundaryJointId(joint.first, joint.edge) : jointId;
      const assignment = group.jointAssignments.find((item) => item.jointId === normalizedJointId);
      if (assignment?.isEnabled === false || isIgnoredJoint(group, joint.first, joint.second, joint.edge)) {
        return [];
      }

      return [{
        optimizationGroupId: group.optimizationGroupId ?? '',
        optimizationGroupName: group.optimizationGroupName ?? '',
        groupName: group.groupName,
        category: 'Panel-to-panel',
        extrusionName: assignment?.extrusionName || layout.panelToPanelExtrusionName,
        location: second ? `${first.label} / ${second.label}` : `${first.label} ${joint.edge}`,
        lengthInches: joint.edge === 'right'
          ? second ? Math.min(first.width, second.width) : first.width
          : second ? Math.min(first.length, second.length) : first.length,
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
      const jointId = neighbor
        ? buildJointId(cell.instanceId, neighbor.instanceId)
        : buildBoundaryJointId(cell.instanceId, edge);
      const disabledJoint = Boolean(
        jointId && group.jointAssignments.find((item) => item.jointId === jointId)?.isEnabled === false,
      );
      const explicitEdge = group.edgeAssignments.find(
        (item) => item.instanceId === cell.instanceId && item.edge === edge,
      );
      const key = `${cell.instanceId}|${edge}`;
      if (explicitEdge?.isIgnored || (neighbor && !disabledJoint && !explicitEdge) || seen.has(key)) {
        return [];
      }

      seen.add(key);
      return [{ instanceId: cell.instanceId, edge, extrusionName }];
    });
  });
}

function detectJoints(group: ExtrusionGroupLayout): Array<{ first: string; second: string; edge: string; isBoundary?: boolean }> {
  const byPosition = new Map(group.cells.map((cell) => [`${cell.row}:${cell.column}`, cell]));
  const adjacencyJoints = group.cells.flatMap((cell) => {
    const right = byPosition.get(`${cell.row}:${cell.column + 1}`);
    const bottom = byPosition.get(`${cell.row + 1}:${cell.column}`);
    return [
      right ? { first: cell.instanceId, second: right.instanceId, edge: 'right' } : null,
      bottom ? { first: cell.instanceId, second: bottom.instanceId, edge: 'bottom' } : null,
    ].filter((item): item is { first: string; second: string; edge: string } => Boolean(item));
  }).filter((joint) => !isIgnoredJoint(group, joint.first, joint.second, joint.edge));
  const boundaryJoints = group.jointAssignments
    .filter((assignment) => assignment.isEnabled !== false && assignment.secondInstanceId.trim().length === 0)
    .map((assignment) => ({
      first: assignment.firstInstanceId,
      second: '',
      edge: assignment.edge,
      isBoundary: true,
    }));
  return [...adjacencyJoints, ...boundaryJoints];
}

function buildJointId(first: string, second: string): string {
  return first.localeCompare(second) <= 0 ? `${first}|${second}` : `${second}|${first}`;
}

function buildBoundaryJointId(instanceId: string, edge: string): string {
  return `${instanceId}|${edge}|boundary`;
}

function isIgnoredEdge(group: ExtrusionGroupLayout, instanceId: string, edge: string): boolean {
  return group.edgeAssignments.some(
    (assignment) =>
      assignment.instanceId === instanceId &&
      assignment.edge === edge &&
      assignment.isIgnored === true,
  );
}

function isIgnoredJoint(
  group: ExtrusionGroupLayout,
  firstInstanceId: string,
  secondInstanceId: string,
  firstEdge: string,
): boolean {
  if (isIgnoredEdge(group, firstInstanceId, firstEdge)) {
    return true;
  }

  if (!secondInstanceId) {
    return false;
  }

  const secondEdge =
    firstEdge === 'right'
      ? 'left'
      : firstEdge === 'bottom'
        ? 'top'
        : firstEdge === 'left'
          ? 'right'
          : 'bottom';
  return isIgnoredEdge(group, secondInstanceId, secondEdge);
}

function summarizeSegments(
  segments: ExtrusionSegmentDetail[],
  additionalLineItems: ExtrusionAdditionalLineItem[] = [],
) {
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
  const rows = Array.from(map.values());
  additionalLineItems.forEach((item) => {
    const matching = segments.filter((segment) => includesLineItemCategory(item.quantityBasis, segment.category));
    rows.push({
      key: `additional|${item.id}`,
      category: 'Additional line item',
      extrusionName: item.name || 'Additional line item',
      totalLengthInches: matching.reduce((total, segment) => total + segment.lengthInches, 0),
      segmentCount: matching.length,
    });
  });
  return rows;
}

function normalizeAdditionalLineItems(
  items: ExtrusionAdditionalLineItem[] | undefined,
): ExtrusionAdditionalLineItem[] {
  return (items ?? []).map((item, index) => ({
    id: item.id || `line-item-${index + 1}`,
    name: item.name ?? '',
    quantityBasis: normalizeQuantityBasis(item.quantityBasis),
    stickLengthFeet: normalizeStickLength(item.stickLengthFeet),
  }));
}

function normalizeQuantityBasis(
  value: ExtrusionLineItemQuantityBasis | undefined,
): ExtrusionLineItemQuantityBasis {
  return value === 'panel-to-panel' || value === 'edge' ? value : 'both';
}

function includesLineItemCategory(
  quantityBasis: ExtrusionLineItemQuantityBasis,
  category: string,
): boolean {
  return (
    quantityBasis === 'both' ||
    (quantityBasis === 'edge' && category === 'Edge') ||
    (quantityBasis === 'panel-to-panel' && category === 'Panel-to-panel')
  );
}

function normalizeStickLength(value: number | undefined): number {
  return value && value > 0 ? value : 20;
}

function formatDimension(value: number): string {
  return `${Number.isInteger(value) ? value : value.toFixed(2).replace(/0+$/, '').replace(/\.$/, '')}"`;
}
