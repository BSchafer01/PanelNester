import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  BufferGeometry, CanvasTexture, Color, Group, LineBasicMaterial, LineDashedMaterial,
  LineLoop, LineSegments, Mesh, MeshBasicMaterial, MOUSE, OrthographicCamera, PlaneGeometry, Scene,
  Sprite, SpriteMaterial, TOUCH, Vector3, WebGLRenderer, type Material, type Object3D,
} from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
import type { PieceInstance, StockItem } from '../types/contracts';

export interface StockItemViewerProps {
  stockItem: StockItem;
  pieceInstances: PieceInstance[];
  profileNumber: string;
  finish?: string | null;
  selectedPieceInstanceId?: string;
  onSelectPieceInstance?: (pieceInstanceId?: string) => void;
}

interface StockSegment {
  kind: 'piece' | 'saw-loss' | 'remainder';
  start: number;
  length: number;
  piece?: PieceInstance;
  ordinal?: number;
}

const stockBarHeight = 12;
const stockPadding = 8;
const minZoom = 1;
const maxZoom = 8;
const collapsedStorageKey = 'optifab.stockItemViewer.cutSequenceCollapsed';

function readCollapsedState(): boolean {
  try { return sessionStorage.getItem(collapsedStorageKey) === 'true'; } catch { return false; }
}

function buildSegments(stockItem: StockItem, pieces: PieceInstance[]): StockSegment[] {
  const segments: StockSegment[] = [];
  const kerfCount = Math.max(pieces.length - 1, 0);
  const kerfLength = kerfCount ? stockItem.sawLoss / kerfCount : 0;
  let cursor = 0;
  pieces.forEach((piece, index) => {
    segments.push({ kind: 'piece', start: cursor, length: piece.length, piece, ordinal: index + 1 });
    cursor += piece.length;
    if (index < pieces.length - 1 && kerfLength > 0) {
      segments.push({ kind: 'saw-loss', start: cursor, length: kerfLength, ordinal: index + 1 });
      cursor += kerfLength;
    }
  });
  if (stockItem.remainder > 0) segments.push({ kind: 'remainder', start: cursor, length: stockItem.remainder });
  return segments;
}

function disposeObject(object: Object3D): void {
  object.traverse((child) => {
    (child as { geometry?: BufferGeometry }).geometry?.dispose();
    const material = (child as { material?: Material | Material[] }).material;
    for (const candidate of material ? (Array.isArray(material) ? material : [material]) : []) {
      (candidate as Material & { map?: { dispose: () => void } }).map?.dispose();
      candidate.dispose();
    }
  });
}

function rectangleOutline(width: number, height: number, color: string): LineLoop {
  const geometry = new BufferGeometry().setFromPoints([
    new Vector3(-width / 2, -height / 2), new Vector3(width / 2, -height / 2),
    new Vector3(width / 2, height / 2), new Vector3(-width / 2, height / 2),
  ]);
  return new LineLoop(geometry, new LineBasicMaterial({ color }));
}

function numberSprite(value: number, x: number): Sprite | undefined {
  const canvas = document.createElement('canvas');
  canvas.width = 96;
  canvas.height = 96;
  const context = canvas.getContext('2d');
  if (!context) return undefined;
  context.fillStyle = '#f4f4f4';
  context.font = '700 48px "Segoe UI", sans-serif';
  context.textAlign = 'center';
  context.textBaseline = 'middle';
  context.fillText(`${value}`, 48, 48);
  const sprite = new Sprite(new SpriteMaterial({ map: new CanvasTexture(canvas), transparent: true }));
  sprite.position.set(x, 0, 0.3);
  sprite.scale.set(5, 5, 1);
  return sprite;
}

function pieceLabel(piece: PieceInstance): string {
  return piece.partNumber?.trim() || piece.partName?.trim() || piece.pieceInstanceId;
}

export function StockItemViewer({
  stockItem, pieceInstances, profileNumber, finish, selectedPieceInstanceId, onSelectPieceInstance,
}: StockItemViewerProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const cameraRef = useRef<OrthographicCamera>();
  const controlsRef = useRef<OrbitControls>();
  const rendererRef = useRef<WebGLRenderer>();
  const sceneRef = useRef<Scene>();
  const stockItemRenderGroupRef = useRef<Group>();
  const [internalSelection, setInternalSelection] = useState<string>();
  const [collapsed, setCollapsed] = useState(readCollapsedState);
  const [tooltip, setTooltip] = useState<string>();
  const segments = useMemo(() => buildSegments(stockItem, pieceInstances), [pieceInstances, stockItem]);
  const isSelectionControlled = onSelectPieceInstance !== undefined;
  const selection = isSelectionControlled ? selectedPieceInstanceId : internalSelection;

  const selectPiece = useCallback((pieceInstanceId?: string) => {
    if (!isSelectionControlled) setInternalSelection(pieceInstanceId);
    onSelectPieceInstance?.(pieceInstanceId);
  }, [isSelectionControlled, onSelectPieceInstance]);

  const adjustZoom = useCallback((factor: number) => {
    const camera = cameraRef.current;
    if (!camera) return;
    camera.zoom = Math.min(maxZoom, Math.max(minZoom, camera.zoom * factor));
    camera.updateProjectionMatrix();
    controlsRef.current?.update();
  }, []);

  const fitView = useCallback(() => {
    const camera = cameraRef.current;
    const controls = controlsRef.current;
    const canvas = canvasRef.current;
    if (!camera || !controls || !canvas) return;
    const width = Math.max(canvas.clientWidth, 1);
    const height = Math.max(canvas.clientHeight, 1);
    const overlaySafeArea = width <= 900 ? 0 : Math.min(0.36, 340 / width);
    const framedWidth = (stockItem.stockLength + stockPadding * 2) / Math.max(1 - overlaySafeArea, 0.5);
    const framedHeight = Math.max(stockBarHeight + stockPadding * 2, framedWidth / (width / height));
    camera.left = -framedWidth / 2;
    camera.right = framedWidth / 2;
    camera.top = framedHeight / 2;
    camera.bottom = -framedHeight / 2;
    const centerX = stockItem.stockLength / 2 + overlaySafeArea * framedWidth / 2;
    camera.position.set(centerX, 0, 100);
    camera.zoom = 1;
    controls.target.set(centerX, 0, 0);
    camera.updateProjectionMatrix();
    controls.update();
    rendererRef.current?.setSize(width, height, false);
  }, [stockItem.stockLength]);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas || typeof window.WebGLRenderingContext === 'undefined') return undefined;
    let renderer: WebGLRenderer;
    try { renderer = new WebGLRenderer({ antialias: true, canvas, powerPreference: 'high-performance' }); }
    catch { return undefined; }
    renderer.setClearColor(new Color('#171719'), 1);
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    rendererRef.current = renderer;
    const scene = new Scene();
    sceneRef.current = scene;
    const camera = new OrthographicCamera(-1, 1, 1, -1, 0.1, 1000);
    camera.position.set(stockItem.stockLength / 2, 0, 100);
    camera.lookAt(stockItem.stockLength / 2, 0, 0);
    cameraRef.current = camera;
    const controls = new OrbitControls(camera, canvas);
    controls.enableDamping = true;
    controls.enablePan = true;
    controls.enableRotate = false;
    controls.enableZoom = true;
    controls.minZoom = minZoom;
    controls.maxZoom = maxZoom;
    controls.mouseButtons = { LEFT: MOUSE.PAN, MIDDLE: MOUSE.DOLLY, RIGHT: MOUSE.PAN };
    controls.touches = { ONE: TOUCH.PAN, TWO: TOUCH.DOLLY_PAN };
    controlsRef.current = controls;
    const observer = new ResizeObserver(fitView);
    observer.observe(canvas);
    fitView();
    renderer.setAnimationLoop(() => { controls.update(); renderer.render(scene, camera); });
    return () => {
      observer.disconnect();
      renderer.setAnimationLoop(null);
      controls.dispose();
      renderer.dispose();
      rendererRef.current = undefined;
      controlsRef.current = undefined;
      cameraRef.current = undefined;
      sceneRef.current = undefined;
    };
  }, [fitView, stockItem.stockLength]);

  useEffect(() => {
    const scene = sceneRef.current;
    if (!scene) return;
    if (stockItemRenderGroupRef.current) { scene.remove(stockItemRenderGroupRef.current); disposeObject(stockItemRenderGroupRef.current); }
    const group = new Group();
    for (const segment of segments) {
      if (segment.length <= 0) continue;
      if (segment.kind === 'piece') {
        const selected = segment.piece?.pieceInstanceId === selection;
        const mesh = new Mesh(new PlaneGeometry(segment.length, stockBarHeight), new MeshBasicMaterial({ color: selected ? '#4fc1ff' : segment.ordinal! % 2 ? '#176f9b' : '#235c78' }));
        mesh.position.set(segment.start + segment.length / 2, 0, 0.1);
        const outline = rectangleOutline(segment.length, stockBarHeight, selected ? '#ffffff' : '#c5c7ca');
        outline.position.copy(mesh.position).setZ(0.2);
        group.add(mesh, outline);
        const label = numberSprite(segment.ordinal!, segment.start + segment.length / 2);
        if (label) group.add(label);
      } else if (segment.kind === 'saw-loss') {
        const mesh = new Mesh(new PlaneGeometry(Math.max(segment.length, 0.08), stockBarHeight), new MeshBasicMaterial({ color: '#d7ba7d' }));
        const centerX = segment.start + segment.length / 2;
        mesh.position.set(centerX, 0, 0.1);
        const cueHalfWidth = Math.max(segment.length / 2, stockItem.stockLength * 0.003);
        const stripes = new LineSegments(
          new BufferGeometry().setFromPoints([
            new Vector3(centerX - cueHalfWidth, -stockBarHeight / 2, 0.2),
            new Vector3(centerX + cueHalfWidth, stockBarHeight / 2, 0.2),
            new Vector3(centerX - cueHalfWidth, stockBarHeight / 2, 0.2),
            new Vector3(centerX + cueHalfWidth, -stockBarHeight / 2, 0.2),
          ]),
          new LineBasicMaterial({ color: '#2d281f' }),
        );
        group.add(mesh, stripes);
      } else {
        const outline = rectangleOutline(segment.length, stockBarHeight, '#8b8d91');
        outline.material = new LineDashedMaterial({ color: '#8b8d91', dashSize: 1.25, gapSize: 0.8 });
        outline.computeLineDistances();
        outline.position.set(segment.start + segment.length / 2, 0, 0.1);
        group.add(outline);
      }
    }
    scene.add(group);
    stockItemRenderGroupRef.current = group;
    fitView();
    return () => { scene.remove(group); disposeObject(group); if (stockItemRenderGroupRef.current === group) stockItemRenderGroupRef.current = undefined; };
  }, [fitView, segments, selection]);

  const segmentAt = (clientX: number, clientY: number): StockSegment | undefined => {
    const canvas = canvasRef.current;
    if (!canvas) return undefined;
    const bounds = canvas.getBoundingClientRect();
    if (bounds.width <= 0 || bounds.height <= 0) return undefined;
    const camera = cameraRef.current;
    let lengthAtPointer: number;
    if (camera) {
      const normalizedX = (clientX - bounds.left) / bounds.width - 0.5;
      const normalizedY = 0.5 - (clientY - bounds.top) / bounds.height;
      lengthAtPointer = camera.position.x + normalizedX * (camera.right - camera.left) / camera.zoom;
      const worldY = camera.position.y + normalizedY * (camera.top - camera.bottom) / camera.zoom;
      if (Math.abs(worldY) > stockBarHeight / 2) return undefined;
    } else {
      const relativeY = clientY - bounds.top;
      if (relativeY < bounds.height * 0.3 || relativeY > bounds.height * 0.7) return undefined;
      lengthAtPointer = (clientX - bounds.left) / bounds.width * stockItem.stockLength;
    }
    return segments.find((segment) => lengthAtPointer >= segment.start && lengthAtPointer < segment.start + segment.length);
  };

  const toggleCollapsed = () => setCollapsed((current) => {
    const next = !current;
    try { sessionStorage.setItem(collapsedStorageKey, `${next}`); } catch { /* unavailable */ }
    return next;
  });
  const kerfCount = Math.max(pieceInstances.length - 1, 0);

  return (
    <section aria-label={`Stock Item ${stockItem.stockItemNumber} viewer`} className="stock-item-viewer">
      <header className="stock-item-viewer__header">
        <div><p className="eyebrow">Stock Item Viewer</p><h2>Stock Item {stockItem.stockItemNumber}</h2><p>{profileNumber} — {finish || 'No finish specified'} — {stockItem.stockLength} in</p></div>
        <div className="button-row">
          <button className="secondary-button" onClick={() => adjustZoom(1 / 1.2)} type="button">Zoom out</button>
          <button className="secondary-button" onClick={() => adjustZoom(1.2)} type="button">Zoom in</button>
          <button className="secondary-button" onClick={fitView} type="button">Reset View</button>
        </div>
      </header>
      <div aria-label="Diagram legend" className="stock-item-viewer__legend">
        <span><i className="stock-legend__piece" />Solid numbered blocks</span>
        <span onMouseEnter={() => setTooltip(`${stockItem.sawLoss} in consumed by ${kerfCount} kerf${kerfCount === 1 ? '' : 's'}`)} onMouseLeave={() => setTooltip(undefined)}><i className="stock-legend__saw-loss" />Saw Loss <small>Striped gaps</small></span>
        <span onMouseEnter={() => setTooltip(`${stockItem.remainder} in remains after all cuts`)} onMouseLeave={() => setTooltip(undefined)}><i className="stock-legend__remainder" />Remainder <small>Dotted tail</small></span>
      </div>
      {tooltip ? <div className="stock-item-viewer__tooltip" role="tooltip">{tooltip}</div> : null}
      <div className="stock-item-viewer__surface">
        <canvas
          aria-label={`Proportional schematic for Stock Item ${stockItem.stockItemNumber}`}
          className="stock-item-viewer__canvas"
          onClick={(event) => { const segment = segmentAt(event.clientX, event.clientY); if (segment?.kind === 'piece') selectPiece(segment.piece?.pieceInstanceId); else if (!segment) selectPiece(undefined); }}
          onMouseLeave={() => setTooltip(undefined)}
          onMouseMove={(event) => { const segment = segmentAt(event.clientX, event.clientY); if (segment?.kind === 'saw-loss') setTooltip(`${segment.length} in Saw Loss between cuts`); else if (segment?.kind === 'remainder') setTooltip(`${segment.length} in Remainder after the Cut Sequence`); else setTooltip(undefined); }}
          ref={canvasRef}
          role="img"
        />
        <aside aria-label="Cut Sequence" className={`cut-sequence-card${collapsed ? ' cut-sequence-card--collapsed' : ''}`}>
          <header className="cut-sequence-card__header">
            <div><strong>Cut Sequence</strong><span>{pieceInstances.length} cuts · {stockItem.pieceLength} in</span></div>
            <button aria-label={`${collapsed ? 'Expand' : 'Collapse'} Cut Sequence`} onClick={toggleCollapsed} type="button">{collapsed ? 'Expand' : 'Collapse'}</button>
          </header>
          {!collapsed ? <div className="cut-sequence-card__rows">{pieceInstances.map((piece, index) => (
            <button aria-label={`Cut ${index + 1}, ${pieceLabel(piece)}, ${piece.length} in`} aria-pressed={selection === piece.pieceInstanceId} className="cut-sequence-row" key={piece.pieceInstanceId} onClick={() => selectPiece(piece.pieceInstanceId)} type="button">
              <span className="cut-sequence-row__number">{index + 1}</span><span><strong>{pieceLabel(piece)}</strong><small>{piece.partName || piece.pieceInstanceId}</small></span><span>{piece.length} in</span>
            </button>
          ))}</div> : null}
        </aside>
      </div>
    </section>
  );
}
