export interface ApiConnection {
  centralUrl: string;
  actor: string;
  token: string;
}

export interface ApiResponse<T = unknown> {
  status: number;
  body: T;
}

export interface AgentCapabilities {
  enabled: boolean;
  provider: string;
  fastModel: string;
  reasoningModel: string;
  connectorWorkspaceWorkflow: boolean;
  maxIterations: number;
  maxToolCalls: number;
  maxRunSeconds: number;
}

export interface EvidenceRef {
  kind: string;
  id: string;
  label: string;
  url?: string;
}

export interface AgentToolInvocation {
  tool: string;
  version: string;
  status: string;
  startedAt: string;
  completedAt?: string;
  summary?: string;
  error?: string;
  evidence: EvidenceRef[];
}

export interface AgentRunSnapshot {
  runId: string;
  actorId: string;
  question: string;
  mode: string;
  status: string;
  workflowStage: string;
  iteration: number;
  modelProvider: string;
  model: string;
  createdAt: string;
  completedAt?: string;
  toolInvocations: AgentToolInvocation[];
  answer?: {
    summary: string;
    findings: string[];
    limitations: string[];
    evidence: EvidenceRef[];
  };
  usage: {
    inputTokens: number;
    outputTokens: number;
    totalTokens: number;
    modelCalls: number;
    toolCalls: number;
    estimatedCost?: number | null;
    currency: string;
  };
  error?: string;
  cancellationReason?: string;
}

export interface AgentRunListItem {
  runId: string;
  question: string;
  mode: string;
  status: string;
  createdAt: string;
  completedAt?: string;
  summary?: string;
  usage: AgentRunSnapshot["usage"];
}

export interface AgentRunPage {
  items: AgentRunListItem[];
  nextBefore?: string;
}

export interface AgentArtifact {
  artifactId: string;
  actorId: string;
  kind: string;
  title: string;
  format: string;
  content: string;
  version: number;
  createdAt: string;
  runId?: string;
  metadata?: unknown;
}

export interface ConnectorCommandResult {
  operation: string;
  succeeded: boolean;
  exitCode: number;
  durationMilliseconds: number;
  output: string;
  completedAt: string;
}

export interface ConnectorWorkspaceSnapshot {
  workspaceId: string;
  actorId: string;
  runId: string;
  specificationArtifactId: string;
  packageName: string;
  status: string;
  createdAt: string;
  updatedAt: string;
  revision: number;
  lastBuild?: ConnectorCommandResult;
  lastTest?: ConnectorCommandResult;
  packageSha256?: string;
  packageApprovedBy?: string;
  packageApprovedAt?: string;
}

export interface ConnectorPackageDescriptor {
  workspaceId: string;
  packageName: string;
  sha256: string;
  sizeBytes: number;
  relativePath: string;
  createdAt: string;
}

export interface StreamEnvelope {
  runId: string;
  sequence?: number;
  eventType: string;
  data: unknown;
}

export interface ConnectorRequirement {
  name: string;
  sourceCode: string;
  protocol: string;
  endpoint: string;
  authentication: string;
  dataContract: string;
  samplingPolicy: string;
  successCriteria: string;
  allowedNetworkTargets: string;
  notes: string;
}
