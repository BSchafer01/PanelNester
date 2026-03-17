import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  BufferGeometry,
  CanvasTexture,
  Color,
  Float32BufferAttribute,
  Group,
  LineBasicMaterial,
  LineLoop,
  LineSegments,
  MOUSE,
  Mesh,
  MeshBasicMaterial,
  OrthographicCamera,
  PlaneGeometry,
  Raycaster,
  Scene,
  Sprite,
  SpriteMaterial,
  TOUCH,
  Vector2,
  Vector3,
  WebGLRenderer,
  type Material,
  type Object3D,
} from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
import type { NestPlacement, NestSheet } from '../types/contracts';

interface SheetViewerPlacement extends NestPlacement {
  group?: string | null;
  displayGroup?: string;
}

interface SheetViewerProps {
  activeGroup?: string;
  activeGroupLabel?: string;
  materialName: string;
  sheet: NestSheet | null;
  placements: SheetViewerPlacement[];
  resetViewToken?: string;
  selectedPlacementId?: string;
  onSelectPlacement?: (placementId?: string) => void;
}

interface TooltipState {
  placement: SheetViewerPlacement;
  x: number;
  y: number;
}

interface PlacementVisual {
  baseColor: string;
  fill: Mesh<PlaneGeometry, MeshBasicMaterial>;
  groupKey: string;
  label?: Sprite;
  calloutConnector?: LineSegments<BufferGeometry, LineBasicMaterial>;
  outline: LineLoop<BufferGeometry, LineBasicMaterial>;
}

const gridStep = 12;
const maxZoom = 10;
const minZoom = 1;
const planViewPolarAngle = Math.PI / 2;
const sheetPadding = 6;
const clickTolerance = 4;
const tooltipOffset = 12;
const inlineLabelMinDimension = 6;
const compactLabelMinDimension = 4;

function getPlacementColor(partId: string): string {
  let hash = 0;
  for (let index = 0; index < partId.length; index += 1) {
    hash = (hash * 31 + partId.charCodeAt(index)) >>> 0;
  }

  const hue = hash % 360;
  return `hsl(${hue} 55% 42%)`;
}

function normalizeGroup(value?: string | null): string | null {
  const trimmed = value?.trim() ?? '';
  return trimmed.length > 0 ? trimmed : null;
}

function getGroupKey(value?: string | null): string {
  return normalizeGroup(value) ?? '';
}

function getDisplayGroup(value?: string | null, fallback?: string): string {
  return fallback?.trim().length ? fallback : normalizeGroup(value) ?? 'Ungrouped';
}

function toSceneY(sheet: NestSheet, y: number, height = 0): number {
  return sheet.sheetWidth - y - height;
}

function disposeMaterial(material: Material | Material[]): void {
  const materials = Array.isArray(material) ? material : [material];

  for (const candidate of materials) {
    const texture = (candidate as Material & { map?: { dispose: () => void } }).map;
    if (texture) {
      texture.dispose();
    }

    candidate.dispose();
  }
}

function disposeObject3D(object: Object3D): void {
  object.traverse((child) => {
    const geometry = (child as { geometry?: BufferGeometry }).geometry;
    if (geometry) {
      geometry.dispose();
    }

    const material = (child as { material?: Material | Material[] }).material;
    if (material) {
      disposeMaterial(material);
    }
  });
}

function createRectangleLoop(
  width: number,
  height: number,
  color: string,
  opacity: number,
): LineLoop<BufferGeometry, LineBasicMaterial> {
  const geometry = new BufferGeometry().setFromPoints([
    new Vector3(-width / 2, -height / 2, 0),
    new Vector3(width / 2, -height / 2, 0),
    new Vector3(width / 2, height / 2, 0),
    new Vector3(-width / 2, height / 2, 0),
  ]);

  return new LineLoop(
    geometry,
    new LineBasicMaterial({
      color,
      opacity,
      transparent: true,
    }),
  );
}

function createGrid(sheet: NestSheet): LineSegments<BufferGeometry, LineBasicMaterial> {
  const vertices: number[] = [];

  for (let x = 0; x <= sheet.sheetLength; x += gridStep) {
    vertices.push(x, 0, 0, x, sheet.sheetWidth, 0);
  }

  for (let y = 0; y <= sheet.sheetWidth; y += gridStep) {
    vertices.push(0, y, 0, sheet.sheetLength, y, 0);
  }

  const geometry = new BufferGeometry();
  geometry.setAttribute('position', new Float32BufferAttribute(vertices, 3));

  return new LineSegments(
    geometry,
    new LineBasicMaterial({
      color: new Color('white'),
      opacity: 0.08,
      transparent: true,
    }),
  );
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max);
}

function truncateLabelText(value: string, maxCharacters: number): string {
  const trimmed = value.trim();
  if (trimmed.length <= maxCharacters) {
    return trimmed;
  }

  if (maxCharacters <= 1) {
    return '…';
  }

  return `${trimmed.slice(0, maxCharacters - 1)}…`;
}

function drawRoundedRect(
  context: CanvasRenderingContext2D,
  x: number,
  y: number,
  width: number,
  height: number,
  radius: number,
): void {
  context.beginPath();
  context.moveTo(x + radius, y);
  context.lineTo(x + width - radius, y);
  context.quadraticCurveTo(x + width, y, x + width, y + radius);
  context.lineTo(x + width, y + height - radius);
  context.quadraticCurveTo(x + width, y + height, x + width - radius, y + height);
  context.lineTo(x + radius, y + height);
  context.quadraticCurveTo(x, y + height, x, y + height - radius);
  context.lineTo(x, y + radius);
  context.quadraticCurveTo(x, y, x + radius, y);
  context.closePath();
}

function createTextSprite(
  text: string,
  x: number,
  y: number,
  scaleX: number,
  scaleY: number,
  tone: 'inline' | 'compact' | 'callout',
): Sprite | undefined {
  if (!text.trim()) {
    return undefined;
  }

  const canvas = document.createElement('canvas');
  canvas.width = 256;
  canvas.height = 96;
  const context = canvas.getContext('2d');
  if (!context) {
    return undefined;
  }

  context.clearRect(0, 0, canvas.width, canvas.height);

  const fontSize = tone === 'inline' ? 34 : tone === 'compact' ? 28 : 24;
  const paddingX = tone === 'callout' ? 22 : 18;
  const measureFont = `600 ${fontSize}px "Segoe UI", sans-serif`;
  context.font = measureFont;
  const textWidth = Math.min(context.measureText(text).width, canvas.width - paddingX * 2);
  const backgroundWidth = Math.max(textWidth + paddingX * 2, tone === 'callout' ? 88 : 72);
  const backgroundHeight = tone === 'inline' ? 56 : 48;
  const backgroundX = (canvas.width - backgroundWidth) / 2;
  const backgroundY = (canvas.height - backgroundHeight) / 2;

  context.fillStyle =
    tone === 'callout' ? 'rgba(30, 30, 30, 0.9)' : 'rgba(30, 30, 30, 0.72)';
  drawRoundedRect(context, backgroundX, backgroundY, backgroundWidth, backgroundHeight, 12);
  context.fill();

  context.strokeStyle = 'rgba(255, 255, 255, 0.16)';
  context.lineWidth = tone === 'callout' ? 2 : 1.5;
  context.stroke();

  context.fillStyle = 'rgba(255, 255, 255, 0.96)';
  context.font = measureFont;
  context.textAlign = 'center';
  context.textBaseline = 'middle';
  context.fillText(text, canvas.width / 2, canvas.height / 2);

  const texture = new CanvasTexture(canvas);
  texture.needsUpdate = true;

  const material = new SpriteMaterial({
    depthTest: false,
    map: texture,
    transparent: true,
  });
  const sprite = new Sprite(material);
  sprite.position.set(x, y, 0.25);
  sprite.scale.set(scaleX, scaleY, 1);

  return sprite;
}

function createCalloutConnector(
  startX: number,
  startY: number,
  endX: number,
  endY: number,
): LineSegments<BufferGeometry, LineBasicMaterial> {
  const geometry = new BufferGeometry().setFromPoints([
    new Vector3(startX, startY, 0.16),
    new Vector3(endX, endY, 0.16),
  ]);

  return new LineSegments(
    geometry,
    new LineBasicMaterial({
      color: '#6b6d72',
      opacity: 0.68,
      transparent: true,
    }),
  );
}

function createPlacementLabel(
  placement: NestPlacement,
  y: number,
  sheet: NestSheet,
  index: number,
): {
  connector?: LineSegments<BufferGeometry, LineBasicMaterial>;
  sprite?: Sprite;
} | undefined {
  const centerX = placement.x + placement.width / 2;
  const centerY = y + placement.height / 2;
  const minDimension = Math.min(placement.width, placement.height);

  if (minDimension >= inlineLabelMinDimension) {
    const inlineLabel = createTextSprite(
      truncateLabelText(placement.partId, clamp(Math.floor(placement.width / 2.6), 3, 16)),
      centerX,
      centerY,
      clamp(placement.width * 0.74, 4.8, 18),
      clamp(placement.height * 0.34, 1.8, 3.4),
      'inline',
    );

    return inlineLabel ? { sprite: inlineLabel } : undefined;
  }

  if (minDimension >= compactLabelMinDimension) {
    const compactLabel = createTextSprite(
      truncateLabelText(placement.partId, clamp(Math.floor(placement.width / 3.2), 2, 6)),
      centerX,
      centerY,
      clamp(placement.width * 0.62, 2.6, 9),
      clamp(placement.height * 0.28, 1.1, 2.2),
      'compact',
    );

    return compactLabel ? { sprite: compactLabel } : undefined;
  }

  const calloutX = clamp(centerX + Math.max(placement.width / 2 + 3.6, 4.8), 4.4, sheet.sheetLength - 4.4);
  const calloutY = clamp(
    centerY +
      (index % 2 === 0 ? 1 : -1) * Math.max(placement.height / 2 + 2.8 + (index % 3) * 0.75, 3.4),
    2.4,
    sheet.sheetWidth - 2.4,
  );
  const connectorX = calloutX > centerX ? calloutX - 2.1 : calloutX + 2.1;
  const connector = createCalloutConnector(centerX, centerY, connectorX, calloutY);
  const calloutLabel = createTextSprite(
    truncateLabelText(placement.partId, 4),
    calloutX,
    calloutY,
    4.8,
    1.7,
    'callout',
  );

  return calloutLabel
    ? {
        connector,
        sprite: calloutLabel,
      }
    : undefined;
}

function getTooltipPosition(bounds: DOMRect, clientX: number, clientY: number): {
  x: number;
  y: number;
} {
  return {
    x: Math.min(
      clientX - bounds.left + tooltipOffset,
      Math.max(tooltipOffset, bounds.width - 228),
    ),
    y: Math.min(
      clientY - bounds.top + tooltipOffset,
      Math.max(tooltipOffset, bounds.height - 92),
    ),
  };
}

export function SheetViewer({
  activeGroup,
  activeGroupLabel,
  materialName,
  sheet,
  placements,
  resetViewToken,
  selectedPlacementId,
  onSelectPlacement,
}: SheetViewerProps) {
  const viewportRef = useRef<HTMLDivElement>(null);
  const sceneRef = useRef<Scene | undefined>(undefined);
  const cameraRef = useRef<OrthographicCamera | undefined>(undefined);
  const rendererRef = useRef<WebGLRenderer | undefined>(undefined);
  const controlsRef = useRef<OrbitControls | undefined>(undefined);
  const viewerGroupRef = useRef<Group | undefined>(undefined);
  const raycasterRef = useRef(new Raycaster());
  const pointerRef = useRef(new Vector2());
  const sheetRef = useRef<NestSheet | null>(sheet);
  const activeGroupRef = useRef(activeGroup);
  const onSelectPlacementRef = useRef(onSelectPlacement);
  const selectedPlacementIdRef = useRef(selectedPlacementId);
  const interactiveMeshesRef = useRef<Array<Mesh<PlaneGeometry, MeshBasicMaterial>>>([]);
  const placementVisualsRef = useRef(new Map<string, PlacementVisual>());
  const hoveredPlacementIdRef = useRef<string | undefined>(undefined);
  const pointerDownRef = useRef<{
    clientX: number;
    clientY: number;
  } | null>(null);
  const draggingRef = useRef(false);
  const [tooltip, setTooltip] = useState<TooltipState>();
  const [isDragging, setIsDragging] = useState(false);

  useEffect(() => {
    sheetRef.current = sheet;
  }, [sheet]);

  useEffect(() => {
    onSelectPlacementRef.current = onSelectPlacement;
  }, [onSelectPlacement]);

  const placementsBySheet = useMemo(
    () =>
      placements
        .filter((placement) => placement.sheetId === sheet?.sheetId)
        .sort((left, right) => left.x - right.x || left.y - right.y),
    [placements, sheet?.sheetId],
  );

  const applyPlacementStyles = useCallback((hoveredPlacementId?: string) => {
    hoveredPlacementIdRef.current = hoveredPlacementId;

    for (const [placementId, visual] of placementVisualsRef.current) {
      const hasActiveGroup = activeGroupRef.current !== undefined;
      const isActiveGroupPlacement =
        !hasActiveGroup || visual.groupKey === activeGroupRef.current;
      const isSelected = placementId === selectedPlacementIdRef.current;
      const isHovered = placementId === hoveredPlacementId;

      visual.fill.material.color.set(
        isActiveGroupPlacement ? visual.baseColor : '#7d7f83',
      );
      if (isActiveGroupPlacement) {
        if (isSelected) {
          visual.fill.material.color.offsetHSL(0, 0, 0.12);
        } else if (isHovered) {
          visual.fill.material.color.offsetHSL(0, 0, 0.06);
        }
      }

      visual.fill.material.opacity = isActiveGroupPlacement
        ? isSelected
          ? 0.98
          : isHovered
            ? 0.9
            : 0.8
        : isSelected
          ? 0.48
          : isHovered
            ? 0.38
            : 0.24;
      visual.outline.material.color.set(
        isActiveGroupPlacement
          ? isSelected
            ? '#e8e8e8'
            : isHovered
              ? '#9da0a6'
              : '#111111'
          : isSelected
            ? '#d0d2d6'
            : isHovered
              ? '#b7b9bd'
              : '#5f6368',
      );
      visual.outline.material.opacity = isActiveGroupPlacement
        ? isSelected
          ? 1
          : isHovered
            ? 0.9
            : 0.72
        : isSelected
          ? 0.82
          : isHovered
            ? 0.72
            : 0.44;

      if (visual.label) {
        visual.label.material.opacity = isActiveGroupPlacement
          ? isSelected || isHovered
            ? 1
            : 0.86
          : isSelected || isHovered
            ? 0.78
            : 0.5;
      }

      if (visual.calloutConnector) {
        visual.calloutConnector.material.color.set(
          isActiveGroupPlacement
            ? isSelected
              ? '#e8e8e8'
              : isHovered
                ? '#c5c7ca'
                : '#6b6d72'
            : isSelected
              ? '#d0d2d6'
              : isHovered
                ? '#b7b9bd'
                : '#66696e',
        );
        visual.calloutConnector.material.opacity = isActiveGroupPlacement
          ? isSelected
            ? 1
            : isHovered
              ? 0.92
              : 0.68
          : isSelected
            ? 0.8
            : isHovered
              ? 0.66
              : 0.4;
      }
    }
  }, []);

  useEffect(() => {
    activeGroupRef.current = activeGroup;
    applyPlacementStyles(hoveredPlacementIdRef.current);
  }, [activeGroup, applyPlacementStyles]);

  const clearViewerScene = useCallback(() => {
    const scene = sceneRef.current;
    const viewerGroup = viewerGroupRef.current;

    interactiveMeshesRef.current = [];
    placementVisualsRef.current.clear();
    hoveredPlacementIdRef.current = undefined;
    setTooltip(undefined);

    if (!scene || !viewerGroup) {
      return;
    }

    scene.remove(viewerGroup);
    disposeObject3D(viewerGroup);
    viewerGroupRef.current = undefined;
  }, []);

  const updateCameraLayout = useCallback((resetView: boolean) => {
    const currentSheet = sheetRef.current;
    const viewport = viewportRef.current;
    const camera = cameraRef.current;
    const renderer = rendererRef.current;
    const controls = controlsRef.current;
    if (!currentSheet || !viewport || !camera || !renderer || !controls) {
      return;
    }

    const width = Math.max(viewport.clientWidth, 1);
    const height = Math.max(viewport.clientHeight, 1);
    const aspect = width / height;
    const framedWidth = currentSheet.sheetLength + sheetPadding * 2;
    const framedHeight = currentSheet.sheetWidth + sheetPadding * 2;
    const framedAspect = framedWidth / framedHeight;

    let halfWidth = framedWidth / 2;
    let halfHeight = framedHeight / 2;
    if (aspect > framedAspect) {
      halfWidth = (framedHeight * aspect) / 2;
    } else {
      halfHeight = framedWidth / (aspect * 2);
    }

    camera.left = -halfWidth;
    camera.right = halfWidth;
    camera.top = halfHeight;
    camera.bottom = -halfHeight;

    if (resetView) {
      const centerX = currentSheet.sheetLength / 2;
      const centerY = currentSheet.sheetWidth / 2;
      camera.position.set(centerX, centerY, 100);
      controls.target.set(centerX, centerY, 0);
      camera.zoom = 1;
    }

    camera.updateProjectionMatrix();
    controls.update();
    renderer.setSize(width, height, false);
  }, []);

  const pickPlacement = useCallback(
    (clientX: number, clientY: number): SheetViewerPlacement | undefined => {
      const viewport = viewportRef.current;
      const camera = cameraRef.current;
      if (!viewport || !camera || interactiveMeshesRef.current.length === 0) {
        return undefined;
      }

      const bounds = viewport.getBoundingClientRect();
      pointerRef.current.set(
        ((clientX - bounds.left) / bounds.width) * 2 - 1,
        -((clientY - bounds.top) / bounds.height) * 2 + 1,
      );

      raycasterRef.current.setFromCamera(pointerRef.current, camera);
      const [hit] = raycasterRef.current.intersectObjects(interactiveMeshesRef.current, false);
      return hit?.object.userData.placement as SheetViewerPlacement | undefined;
    },
    [],
  );

  useEffect(() => {
    selectedPlacementIdRef.current = selectedPlacementId;
    applyPlacementStyles(hoveredPlacementIdRef.current);
  }, [applyPlacementStyles, selectedPlacementId]);

  useEffect(() => {
    if (!sheet) {
      return undefined;
    }

    const viewport = viewportRef.current;
    if (!viewport) {
      return undefined;
    }

    const scene = new Scene();
    scene.background = new Color('#252526');
    sceneRef.current = scene;

    const camera = new OrthographicCamera(-1, 1, 1, -1, 0.1, 1000);
    camera.position.set(sheet.sheetLength / 2, sheet.sheetWidth / 2, 100);
    camera.lookAt(sheet.sheetLength / 2, sheet.sheetWidth / 2, 0);
    cameraRef.current = camera;

    const renderer = new WebGLRenderer({
      alpha: true,
      antialias: true,
      powerPreference: 'high-performance',
    });
    renderer.setClearColor('#252526', 1);
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    renderer.domElement.className = 'sheet-viewer__canvas';
    renderer.domElement.tabIndex = 0;
    renderer.domElement.setAttribute('role', 'img');
    renderer.domElement.setAttribute(
      'aria-label',
      `Sheet viewer for ${materialName} sheet ${sheet.sheetNumber}`,
    );
    viewport.replaceChildren(renderer.domElement);
    rendererRef.current = renderer;

    const controls = new OrbitControls(camera, renderer.domElement);
    controls.enableDamping = true;
    controls.dampingFactor = 0.08;
    controls.enablePan = true;
    controls.enableRotate = false;
    controls.enableZoom = true;
    controls.maxAzimuthAngle = 0;
    controls.maxPolarAngle = planViewPolarAngle;
    controls.maxZoom = maxZoom;
    controls.minAzimuthAngle = 0;
    controls.minPolarAngle = planViewPolarAngle;
    controls.minZoom = minZoom;
    controls.mouseButtons = {
      LEFT: MOUSE.PAN,
      MIDDLE: MOUSE.DOLLY,
      RIGHT: MOUSE.PAN,
    };
    controls.panSpeed = 1.1;
    controls.screenSpacePanning = true;
    controls.touches = {
      ONE: TOUCH.PAN,
      TWO: TOUCH.DOLLY_PAN,
    };
    controls.zoomSpeed = 1.05;
    controls.target.set(sheet.sheetLength / 2, sheet.sheetWidth / 2, 0);
    controls.update();
    controlsRef.current = controls;

    const clearHover = () => {
      if (hoveredPlacementIdRef.current) {
        applyPlacementStyles(undefined);
      }
      setTooltip(undefined);
    };

    const updateHoverFromPointer = (event: PointerEvent) => {
      if (draggingRef.current) {
        return;
      }

      const placement = pickPlacement(event.clientX, event.clientY);
      if (!placement) {
        clearHover();
        return;
      }

      const bounds = viewport.getBoundingClientRect();
      const tooltipPosition = getTooltipPosition(bounds, event.clientX, event.clientY);
      if (hoveredPlacementIdRef.current !== placement.placementId) {
        applyPlacementStyles(placement.placementId);
      }

      setTooltip({
        placement,
        x: tooltipPosition.x,
        y: tooltipPosition.y,
      });
    };

    const handlePointerDown = (event: PointerEvent) => {
      pointerDownRef.current = {
        clientX: event.clientX,
        clientY: event.clientY,
      };
      draggingRef.current = false;
      setIsDragging(false);
      renderer.domElement.focus({ preventScroll: true });
    };

    const handlePointerMove = (event: PointerEvent) => {
      const pointerDown = pointerDownRef.current;
      if (pointerDown) {
        const moved =
          Math.hypot(event.clientX - pointerDown.clientX, event.clientY - pointerDown.clientY) >
          clickTolerance;

        if (moved && !draggingRef.current) {
          draggingRef.current = true;
          setIsDragging(true);
          clearHover();
        }
      }

      updateHoverFromPointer(event);
    };

    const handlePointerLeave = () => {
      clearHover();
    };

    const handlePointerUp = (event: PointerEvent) => {
      const pointerDown = pointerDownRef.current;
      const wasDragging = draggingRef.current;
      pointerDownRef.current = null;
      draggingRef.current = false;
      setIsDragging(false);

      if (wasDragging || !pointerDown) {
        return;
      }

      const bounds = viewport.getBoundingClientRect();
      const pointerInside =
        event.clientX >= bounds.left &&
        event.clientX <= bounds.right &&
        event.clientY >= bounds.top &&
        event.clientY <= bounds.bottom;
      if (!pointerInside) {
        return;
      }

      const placement = pickPlacement(event.clientX, event.clientY);
      if (!placement) {
        onSelectPlacementRef.current?.(undefined);
        return;
      }

      onSelectPlacementRef.current?.(
        placement.placementId === selectedPlacementIdRef.current
          ? undefined
          : placement.placementId,
      );
    };

    const preventViewerScroll = (event: WheelEvent) => {
      event.preventDefault();
      event.stopPropagation();
    };

    const preventContextMenu = (event: MouseEvent) => {
      event.preventDefault();
    };

    const resizeObserver = new ResizeObserver(() => {
      updateCameraLayout(false);
    });
    resizeObserver.observe(viewport);

    renderer.domElement.addEventListener('pointerdown', handlePointerDown);
    renderer.domElement.addEventListener('pointermove', handlePointerMove);
    renderer.domElement.addEventListener('pointerleave', handlePointerLeave);
    renderer.domElement.addEventListener('wheel', preventViewerScroll, { passive: false });
    renderer.domElement.addEventListener('contextmenu', preventContextMenu);
    window.addEventListener('pointerup', handlePointerUp);
    window.addEventListener('pointercancel', handlePointerUp);

    updateCameraLayout(true);
    renderer.setAnimationLoop(() => {
      controls.update();
      renderer.render(scene, camera);
    });

    return () => {
      pointerDownRef.current = null;
      draggingRef.current = false;
      setIsDragging(false);
      clearHover();
      resizeObserver.disconnect();
      window.removeEventListener('pointerup', handlePointerUp);
      window.removeEventListener('pointercancel', handlePointerUp);
      renderer.domElement.removeEventListener('pointerdown', handlePointerDown);
      renderer.domElement.removeEventListener('pointermove', handlePointerMove);
      renderer.domElement.removeEventListener('pointerleave', handlePointerLeave);
      renderer.domElement.removeEventListener('wheel', preventViewerScroll);
      renderer.domElement.removeEventListener('contextmenu', preventContextMenu);
      renderer.setAnimationLoop(null);
      clearViewerScene();
      controls.dispose();
      renderer.dispose();
      viewport.replaceChildren();
      controlsRef.current = undefined;
      rendererRef.current = undefined;
      cameraRef.current = undefined;
      sceneRef.current = undefined;
    };
  }, [applyPlacementStyles, clearViewerScene, materialName, pickPlacement, sheet, updateCameraLayout]);

  useEffect(() => {
    if (!sheet) {
      return;
    }

    updateCameraLayout(true);
  }, [resetViewToken, sheet, updateCameraLayout]);

  useEffect(() => {
    const currentSheet = sheetRef.current;
    const scene = sceneRef.current;
    if (!currentSheet || !scene) {
      return;
    }

    clearViewerScene();

    const viewerGroup = new Group();
    const grid = createGrid(currentSheet);
    grid.position.set(0, 0, -0.1);
    viewerGroup.add(grid);

    const sheetFill = new Mesh(
      new PlaneGeometry(currentSheet.sheetLength, currentSheet.sheetWidth),
      new MeshBasicMaterial({
        color: '#ffffff',
        opacity: 0.04,
        transparent: true,
      }),
    );
    sheetFill.position.set(currentSheet.sheetLength / 2, currentSheet.sheetWidth / 2, -0.2);
    viewerGroup.add(sheetFill);

    const sheetOutline = createRectangleLoop(
      currentSheet.sheetLength,
      currentSheet.sheetWidth,
      '#9da0a6',
      0.95,
    );
    sheetOutline.position.set(currentSheet.sheetLength / 2, currentSheet.sheetWidth / 2, 0);
    viewerGroup.add(sheetOutline);

    for (const [index, placement] of placementsBySheet.entries()) {
      const baseColor = getPlacementColor(placement.partId);
      const sceneY = toSceneY(currentSheet, placement.y, placement.height);
      const fill = new Mesh(
        new PlaneGeometry(placement.width, placement.height),
        new MeshBasicMaterial({
          color: baseColor,
          opacity: 0.8,
          transparent: true,
        }),
      );
      fill.position.set(
        placement.x + placement.width / 2,
        sceneY + placement.height / 2,
        0.1,
      );
      fill.userData.placement = placement;

      const outline = createRectangleLoop(placement.width, placement.height, '#111111', 0.72);
      outline.position.set(
        placement.x + placement.width / 2,
        sceneY + placement.height / 2,
        0.15,
      );

      const annotation = createPlacementLabel(placement, sceneY, currentSheet, index);

      viewerGroup.add(fill);
      viewerGroup.add(outline);
      if (annotation?.connector) {
        viewerGroup.add(annotation.connector);
      }
      if (annotation?.sprite) {
        viewerGroup.add(annotation.sprite);
      }

      interactiveMeshesRef.current.push(fill);
      placementVisualsRef.current.set(placement.placementId, {
        baseColor,
        fill,
        groupKey: getGroupKey(placement.group),
        label: annotation?.sprite,
        calloutConnector: annotation?.connector,
        outline,
      });
    }

    scene.add(viewerGroup);
    viewerGroupRef.current = viewerGroup;
    updateCameraLayout(true);
    applyPlacementStyles(hoveredPlacementIdRef.current);
  }, [applyPlacementStyles, clearViewerScene, placementsBySheet, updateCameraLayout]);

  if (!sheet) {
    return (
      <section className="panel sheet-viewer-panel">
        <div className="section-header">
          <div>
            <p className="eyebrow">Viewer</p>
            <h3>Sheet layout viewer</h3>
          </div>
        </div>
        <div className="empty-state">
          <strong>No sheet selected</strong>
          <span>Choose a material result and sheet to inspect the layout.</span>
        </div>
      </section>
    );
  }

  return (
    <section aria-label="Current sheet viewer panel" className="panel sheet-viewer-panel">
      <div className="section-header">
        <div>
          <p className="eyebrow">Viewer</p>
          <h3>Sheet layout viewer</h3>
          <p className="muted">
            {materialName} — sheet #{sheet.sheetNumber} ({sheet.sheetLength}" × {sheet.sheetWidth}
            ")
          </p>
        </div>
        <div className="button-row">
          <button
            className="secondary-button"
            onClick={() => {
              const controls = controlsRef.current;
              if (!controls) {
                return;
              }

              const camera = cameraRef.current;
              if (!camera) {
                return;
              }

              camera.zoom = Math.max(minZoom, camera.zoom / 1.2);
              camera.updateProjectionMatrix();
              controls.update();
            }}
            type="button"
          >
            Zoom out
          </button>
          <button
            className="secondary-button"
            onClick={() => {
              const controls = controlsRef.current;
              if (!controls) {
                return;
              }

              const camera = cameraRef.current;
              if (!camera) {
                return;
              }

              camera.zoom = Math.min(maxZoom, camera.zoom * 1.2);
              camera.updateProjectionMatrix();
              controls.update();
            }}
            type="button"
          >
            Zoom in
          </button>
          <button
            className="secondary-button"
            onClick={() => updateCameraLayout(true)}
            type="button"
          >
            Reset view
          </button>
        </div>
      </div>

      <div className="token-list">
        <span className="token">{placementsBySheet.length} placement(s)</span>
        <span className="token">Plan view locked</span>
        <span className="token">Hover panels for details</span>
        {activeGroupLabel ? <span className="token">Focus group: {activeGroupLabel}</span> : null}
        {activeGroupLabel ? <span className="token">Other groups subdued</span> : null}
        <span className="token">
          {placementsBySheet.length > 0 ? 'Tiny panels use compact labels or callouts' : 'Sheet outline only'}
        </span>
        <span className="token">Wheel to zoom</span>
        <span className="token">Drag to pan</span>
        <span className="token">Viewer captures input while hovered</span>
      </div>

      <div
        aria-label={`Plan view for ${materialName} sheet ${sheet.sheetNumber}`}
        className={`sheet-viewer${isDragging ? ' sheet-viewer--dragging' : ''}`}
        data-current-sheet-id={sheet.sheetId}
        data-view-mode="plan"
      >
        <div className="sheet-viewer__viewport" ref={viewportRef} />
        {placementsBySheet.length === 0 ? (
          <div className="sheet-viewer__overlay">
            <div className="sheet-viewer__overlay-badge">
              <strong>No placements on this sheet</strong>
              <span>The full sheet outline is still shown so camera fit and sizing stay clear.</span>
            </div>
          </div>
        ) : null}
        {tooltip ? (
          <div
            aria-label="Hovered panel details"
            aria-live="polite"
            className="sheet-viewer__tooltip"
            role="status"
            style={{
              left: `${tooltip.x}px`,
              top: `${tooltip.y}px`,
            }}
          >
            <strong>{tooltip.placement.partId}</strong>
            <span>Group: {getDisplayGroup(tooltip.placement.group, tooltip.placement.displayGroup)}</span>
            <span>
              {tooltip.placement.width}" × {tooltip.placement.height}"
            </span>
            <span>
              ({tooltip.placement.x}", {tooltip.placement.y}")
            </span>
            <span>{tooltip.placement.rotated90 ? 'Rotated 90°' : 'Not rotated'}</span>
          </div>
        ) : null}
      </div>
    </section>
  );
}
