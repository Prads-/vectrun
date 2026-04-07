import type { Edge } from '@xyflow/react'
import type { AnyFlowNode } from './layoutGraph'
import type { Pipeline, PipelineNode, AgentNodeData, BranchNodeData, LogicNodeData, WaitNodeData } from '../types/pipeline'

export function toPipeline(
  flowNodes: AnyFlowNode[],
  edges: Edge[],
  meta: { pipelineName: string; startNodeId: string }
): Pipeline {
  const nodes: PipelineNode[] = flowNodes.map(node => {
    const outEdges = edges.filter(e => e.source === node.id)

    switch (node.type) {
      case 'branch': {
        const d = node.data as BranchNodeData
        return {
          id: node.id, type: 'branch',
          data: {
            name: d.name || undefined,
            expectedOutput: d.expectedOutput,
            trueNodeIds: outEdges.filter(e => e.label === 'true').map(e => e.target),
            falseNodeIds: outEdges.filter(e => e.label === 'false').map(e => e.target),
          }
        }
      }
      case 'agent': {
        const d = node.data as AgentNodeData
        return {
          id: node.id, type: 'agent',
          data: { name: d.name || undefined, agentId: d.agentId, nextNodeIds: outEdges.map(e => e.target), toolIds: d.toolIds }
        }
      }
      case 'logic': {
        const d = node.data as LogicNodeData
        return {
          id: node.id, type: 'logic',
          data: { name: d.name || undefined, logicType: d.logicType, script: d.script, processPath: d.processPath, nextNodeIds: outEdges.map(e => e.target) }
        }
      }
      case 'wait': {
        const d = node.data as WaitNodeData
        return { id: node.id, type: 'wait', data: { name: d.name || undefined, durationMs: d.durationMs, nextNodeIds: outEdges.map(e => e.target) } }
      }
      default:
        throw new Error(`Unknown node type: ${(node as AnyFlowNode).type}`)
    }
  })

  return { pipelineName: meta.pipelineName, startNodeId: meta.startNodeId, nodes }
}
