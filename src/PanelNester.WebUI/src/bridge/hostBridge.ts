import {
  type AddPartRowRequest,
  type BatchNestRequest,
  type BridgeHandshakeRequest,
  type BridgeHandshakeResponse,
  type BridgeMessage,
  type BridgeOperationResponse,
  type BridgeUiReadyRequest,
  type CreateMaterialRequest,
  type ChooseMaterialLibraryLocationRequest,
  type ChooseMaterialLibraryLocationResponse,
  type DeletePartRowRequest,
  type DeleteMaterialRequest,
  type DeleteMaterialResponse,
  type BatchNestResponse,
  type ExportPdfReportRequest,
  type ExportPdfReportResponse,
  type GetProjectMetadataRequest,
  type ProjectMetadataResponse,
  type GetMaterialRequest,
  type HostBridgeSnapshot,
  type ImportFileRequest,
  type ImportFileResponse,
  type ImportResponse,
  type ListMaterialsRequest,
  type ListMaterialsResponse,
  type Material,
  type MaterialDraft,
  type MaterialRecordResponse,
  type NewProjectRequest,
  type OpenFileDialogRequest,
  type OpenFileDialogResponse,
  type OpenProjectRequest,
  type ProjectOperationResponse,
  type RestoreDefaultMaterialLibraryLocationRequest,
  type RestoreDefaultMaterialLibraryLocationResponse,
  type SaveProjectAsRequest,
  type SaveProjectRequest,
  type UpdatePartRowRequest,
  type UpdateMaterialRequest,
  type UpdateProjectMetadataRequest,
  type UpdateReportSettingsRequest,
  type UpdateReportSettingsResponse,
  bridgeMessageTypes,
  requestedBridgeCapabilities,
  toBridgeResponseType,
} from '../types/contracts';

const uiVersion = '0.1.0';
const handshakeType = bridgeMessageTypes.handshake;
const handshakeResponseType = toBridgeResponseType(handshakeType);
const requestTimeoutMs = 5000;

type BridgeDirection = 'inbound' | 'outbound';

export interface HostBridgeEvent {
  direction: BridgeDirection;
  message: BridgeMessage;
  snapshot: HostBridgeSnapshot;
}

type BridgeSubscriber = (event: HostBridgeEvent) => void;

interface PendingRequest {
  resolve: (value: unknown) => void;
  timeoutId: number;
}

interface WebViewMessageEvent {
  data: unknown;
}

interface WebViewTransport {
  addEventListener?: (
    type: 'message',
    listener: (event: WebViewMessageEvent) => void,
  ) => void;
  postMessage: (message: unknown) => void;
  removeEventListener?: (
    type: 'message',
    listener: (event: WebViewMessageEvent) => void,
  ) => void;
}

declare global {
  interface Window {
    chrome?: {
      webview?: WebViewTransport;
    };
    hostBridge?: {
      receive: (message: BridgeMessage | string) => void;
    };
  }
}

class HostBridgeClient {
  private readonly subscribers = new Set<BridgeSubscriber>();
  private readonly pendingRequests = new Map<string, PendingRequest>();
  private readonly inboundListener = (event: WebViewMessageEvent) => {
    this.receive(event.data);
  };
  private receiveAttached = false;
  private requestCounter = 0;
  private snapshot: HostBridgeSnapshot = {
    connected: false,
    handshake: this.createStandaloneHandshake(
      'Preview mode active. Waiting for the desktop host to attach.',
    ),
  };

  constructor() {
    this.attachReceiveHook();
  }

  subscribe(listener: BridgeSubscriber): () => void {
    this.subscribers.add(listener);

    return () => {
      this.subscribers.delete(listener);
    };
  }

  getSnapshot(): HostBridgeSnapshot {
    return {
      ...this.snapshot,
      handshake: {
        ...this.snapshot.handshake,
        capabilities: [...this.snapshot.handshake.capabilities],
      },
    };
  }

  async initialize(): Promise<BridgeHandshakeResponse> {
    this.attachReceiveHook();
    this.attachWebViewListener();

    if (!this.hasWebViewTransport()) {
      const handshake = this.createStandaloneHandshake(
        'WebView2 transport not detected. UI is running with placeholders only.',
      );

      this.updateSnapshot({
        connected: false,
        handshake,
      });

      return handshake;
    }

    try {
      const response = await this.invoke<BridgeHandshakeResponse>(
        handshakeType,
        {
          surface: 'PanelNester.WebUI',
          version: uiVersion,
          requestedCapabilities: requestedBridgeCapabilities,
        } satisfies BridgeHandshakeRequest,
      );

      return response;
    } catch (error) {
      const message =
        error instanceof Error
          ? error.message
          : 'Desktop host did not acknowledge the handshake yet.';
      const handshake = {
        ...this.createStandaloneHandshake(message),
        bridgeMode: 'webview2' as const,
      };

      this.updateSnapshot({
        connected: false,
        handshake,
        lastError: message,
      });

      return handshake;
    }
  }

  async invoke<TResponse>(
    type: string,
    payload: unknown,
    timeoutMs = requestTimeoutMs,
  ): Promise<TResponse> {
    const requestId = this.createRequestId();
    const message: BridgeMessage = {
      type,
      requestId,
      payload,
    };

    if (!this.hasWebViewTransport()) {
      return Promise.reject(
        new Error('Desktop host transport is unavailable in browser preview mode.'),
      );
    }

    return new Promise<TResponse>((resolve, reject) => {
      const timeoutId = window.setTimeout(() => {
        this.pendingRequests.delete(requestId);
        reject(new Error(`Timed out waiting for "${type}" response from desktop host.`));
      }, timeoutMs);

      this.pendingRequests.set(requestId, {
        resolve: resolve as (value: unknown) => void,
        timeoutId,
      });

      this.transport()?.postMessage(message);
      this.notify('outbound', message);
    });
  }

  listMaterials(): Promise<ListMaterialsResponse> {
    return this.invoke<ListMaterialsResponse>(
      bridgeMessageTypes.listMaterials,
      {} satisfies ListMaterialsRequest,
    );
  }

  chooseMaterialLibraryLocation(): Promise<ChooseMaterialLibraryLocationResponse> {
    return this.invoke<ChooseMaterialLibraryLocationResponse>(
      bridgeMessageTypes.chooseMaterialLibraryLocation,
      {} satisfies ChooseMaterialLibraryLocationRequest,
    );
  }

  restoreDefaultMaterialLibraryLocation(): Promise<RestoreDefaultMaterialLibraryLocationResponse> {
    return this.invoke<RestoreDefaultMaterialLibraryLocationResponse>(
      bridgeMessageTypes.restoreDefaultMaterialLibraryLocation,
      {} satisfies RestoreDefaultMaterialLibraryLocationRequest,
    );
  }

  openFileDialog(request: OpenFileDialogRequest): Promise<OpenFileDialogResponse> {
    return this.invoke<OpenFileDialogResponse>(
      bridgeMessageTypes.openFileDialog,
      request,
    );
  }

  importFile(request: ImportFileRequest): Promise<ImportFileResponse> {
    return this.invoke<ImportFileResponse>(bridgeMessageTypes.importFile, request);
  }

  addPartRow(request: AddPartRowRequest): Promise<ImportResponse> {
    return this.invoke<ImportResponse>(bridgeMessageTypes.addPartRow, request);
  }

  updatePartRow(request: UpdatePartRowRequest): Promise<ImportResponse> {
    return this.invoke<ImportResponse>(bridgeMessageTypes.updatePartRow, request);
  }

  deletePartRow(request: DeletePartRowRequest): Promise<ImportResponse> {
    return this.invoke<ImportResponse>(bridgeMessageTypes.deletePartRow, request);
  }

  runBatchNesting(request: BatchNestRequest): Promise<BatchNestResponse> {
    return this.invoke<BatchNestResponse>(bridgeMessageTypes.runBatchNesting, request);
  }

  getMaterial(request: GetMaterialRequest): Promise<MaterialRecordResponse> {
    return this.invoke<MaterialRecordResponse>(
      bridgeMessageTypes.getMaterial,
      request,
    );
  }

  createMaterial(material: MaterialDraft): Promise<MaterialRecordResponse> {
    return this.invoke<MaterialRecordResponse>(
      bridgeMessageTypes.createMaterial,
      {
        material,
      } satisfies CreateMaterialRequest,
    );
  }

  updateMaterial(material: Material): Promise<MaterialRecordResponse> {
    return this.invoke<MaterialRecordResponse>(
      bridgeMessageTypes.updateMaterial,
      {
        material,
      } satisfies UpdateMaterialRequest,
    );
  }

  deleteMaterial(request: DeleteMaterialRequest): Promise<DeleteMaterialResponse> {
    return this.invoke<DeleteMaterialResponse>(
      bridgeMessageTypes.deleteMaterial,
      request,
    );
  }

  newProject(request: NewProjectRequest): Promise<ProjectOperationResponse> {
    return this.invoke<ProjectOperationResponse>(
      bridgeMessageTypes.newProject,
      request,
    );
  }

  openProject(request: OpenProjectRequest): Promise<ProjectOperationResponse> {
    return this.invoke<ProjectOperationResponse>(
      bridgeMessageTypes.openProject,
      request,
    );
  }

  notifyUiReady(): Promise<BridgeOperationResponse> {
    return this.invoke<BridgeOperationResponse>(
      bridgeMessageTypes.bridgeUiReady,
      {} satisfies BridgeUiReadyRequest,
    );
  }

  saveProject(request: SaveProjectRequest): Promise<ProjectOperationResponse> {
    return this.invoke<ProjectOperationResponse>(
      bridgeMessageTypes.saveProject,
      request,
    );
  }

  saveProjectAs(request: SaveProjectAsRequest): Promise<ProjectOperationResponse> {
    return this.invoke<ProjectOperationResponse>(
      bridgeMessageTypes.saveProjectAs,
      request,
    );
  }

  getProjectMetadata(
    request: GetProjectMetadataRequest,
  ): Promise<ProjectMetadataResponse> {
    return this.invoke<ProjectMetadataResponse>(
      bridgeMessageTypes.getProjectMetadata,
      request,
    );
  }

  updateProjectMetadata(
    request: UpdateProjectMetadataRequest,
  ): Promise<ProjectMetadataResponse> {
    return this.invoke<ProjectMetadataResponse>(
      bridgeMessageTypes.updateProjectMetadata,
      request,
    );
  }

  updateReportSettings(
    request: UpdateReportSettingsRequest,
  ): Promise<UpdateReportSettingsResponse> {
    return this.invoke<UpdateReportSettingsResponse>(
      bridgeMessageTypes.updateReportSettings,
      request,
    );
  }

  exportPdfReport(
    request: ExportPdfReportRequest,
  ): Promise<ExportPdfReportResponse> {
    return this.invoke<ExportPdfReportResponse>(
      bridgeMessageTypes.exportPdfReport,
      request,
      15000,
    );
  }

  receive(message: unknown): void {
    const normalized = this.normalizeMessage(message);

    if (!normalized) {
      return;
    }

    if (normalized.type === handshakeResponseType) {
      const handshakePayload = normalized.payload as BridgeHandshakeResponse;
      this.updateSnapshot({
        connected: handshakePayload.success,
        handshake: handshakePayload,
        lastError: handshakePayload.success ? undefined : handshakePayload.message,
      });
    } else {
      this.updateSnapshot({
        ...this.getSnapshot(),
        lastMessageAt: new Date().toISOString(),
      });
    }

    if (normalized.requestId) {
      const pendingRequest = this.pendingRequests.get(normalized.requestId);

      if (pendingRequest) {
        window.clearTimeout(pendingRequest.timeoutId);
        this.pendingRequests.delete(normalized.requestId);
        pendingRequest.resolve(normalized.payload);
      }
    }

    this.notify('inbound', normalized);
  }

  private updateSnapshot(snapshot: HostBridgeSnapshot): void {
    this.snapshot = {
      ...snapshot,
      lastMessageAt: snapshot.lastMessageAt ?? new Date().toISOString(),
    };
  }

  private attachReceiveHook(): void {
    if (window.hostBridge) {
      return;
    }

    window.hostBridge = {
      receive: (message) => {
        this.receive(message);
      },
    };
  }

  private attachWebViewListener(): void {
    const webview = this.transport();

    if (!webview?.addEventListener || this.receiveAttached) {
      return;
    }

    webview.addEventListener('message', this.inboundListener);
    this.receiveAttached = true;
  }

  private createRequestId(): string {
    if (typeof crypto !== 'undefined' && 'randomUUID' in crypto) {
      return crypto.randomUUID();
    }

    this.requestCounter += 1;
    return `bridge-${Date.now()}-${this.requestCounter}`;
  }

  private hasWebViewTransport(): boolean {
    return Boolean(this.transport());
  }

  private transport(): WebViewTransport | undefined {
    return window.chrome?.webview;
  }

  private normalizeMessage(message: unknown): BridgeMessage | undefined {
    if (typeof message === 'string') {
      try {
        return JSON.parse(message) as BridgeMessage;
      } catch {
        return undefined;
      }
    }

    if (this.isBridgeMessage(message)) {
      return message;
    }

    return undefined;
  }

  private isBridgeMessage(value: unknown): value is BridgeMessage {
    if (!value || typeof value !== 'object') {
      return false;
    }

    const candidate = value as Partial<BridgeMessage>;
    return typeof candidate.type === 'string' && 'payload' in candidate;
  }

  private notify(direction: BridgeDirection, message: BridgeMessage): void {
    this.updateSnapshot({
      ...this.getSnapshot(),
      lastMessageAt: new Date().toISOString(),
    });

    const event: HostBridgeEvent = {
      direction,
      message,
      snapshot: this.getSnapshot(),
    };

    this.subscribers.forEach((subscriber) => subscriber(event));
  }

  private createStandaloneHandshake(message: string): BridgeHandshakeResponse {
    return {
      success: false,
      hostName: 'PanelNester desktop host pending',
      bridgeMode: 'standalone',
      capabilities: [],
      message,
    };
  }
}

export const hostBridge = new HostBridgeClient();
