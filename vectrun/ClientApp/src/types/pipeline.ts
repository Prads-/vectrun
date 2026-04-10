export type NodeType = 'agent' | 'branch' | 'logic' | 'wait'

export interface RetryPolicy {
  retryCount: number
  retryDelayMs: number
  delayType: string  // "linear" | "sliding"
}

export interface AgentNodeData extends Record<string, unknown> {
  name?: string
  agentId: string
  nextNodeIds?: string[]
  toolIds?: string[]
  retry?: RetryPolicy
}

export interface BranchNodeData extends Record<string, unknown> {
  name?: string
  expectedOutput: string
  trueNodeIds: string[]
  falseNodeIds: string[]
}

export interface LogicNodeData extends Record<string, unknown> {
  name?: string
  logicType: string
  script?: string
  processPath?: string
  nextNodeIds?: string[]
  retry?: RetryPolicy
}

export interface WaitNodeData extends Record<string, unknown> {
  name?: string
  durationMs: number
  nextNodeIds?: string[]
}

export type AnyNodeData = AgentNodeData | BranchNodeData | LogicNodeData | WaitNodeData

export interface PipelineNode {
  id: string
  type: NodeType
  data: AnyNodeData
}

export interface Pipeline {
  pipelineName: string
  startNodeId: string
  nodes: PipelineNode[]
}

export function getNextNodeIds(node: PipelineNode): { targetId: string; label?: string }[] {
  switch (node.type) {
    case 'branch': {
      const d = node.data as BranchNodeData
      return [
        ...d.trueNodeIds.map((id) => ({ targetId: id, label: 'true' })),
        ...d.falseNodeIds.map((id) => ({ targetId: id, label: 'false' })),
      ]
    }
    default: {
      const d = node.data as { nextNodeIds?: string[] }
      return (d.nextNodeIds ?? []).map((id) => ({ targetId: id }))
    }
  }
}
