import dagre from '@dagrejs/dagre'
import type { Edge } from '@xyflow/react'
import type { Pipeline } from '../types/pipeline'
import { getNextNodeIds } from '../types/pipeline'
import type { AgentFlowNode } from '../components/nodes/AgentNode'
import type { BranchFlowNode } from '../components/nodes/BranchNode'
import type { LogicFlowNode } from '../components/nodes/LogicNode'
import type { WaitFlowNode } from '../components/nodes/WaitNode'

export type AnyFlowNode = AgentFlowNode | BranchFlowNode | LogicFlowNode | WaitFlowNode

const NODE_WIDTH = 220
const NODE_HEIGHT = 90

export function buildFlowGraph(pipeline: Pipeline): {
  nodes: AnyFlowNode[]
  edges: Edge[]
} {
  const g = new dagre.graphlib.Graph()
  g.setDefaultEdgeLabel(() => ({}))
  g.setGraph({ rankdir: 'TB', ranksep: 80, nodesep: 60 })

  for (const node of pipeline.nodes) {
    g.setNode(node.id, { width: NODE_WIDTH, height: NODE_HEIGHT })
  }

  const edgeDefs: { source: string; target: string; label?: string }[] = []

  for (const node of pipeline.nodes) {
    for (const { targetId, label } of getNextNodeIds(node)) {
      g.setEdge(node.id, targetId)
      edgeDefs.push({ source: node.id, target: targetId, label })
    }
  }

  dagre.layout(g)

  const nodes: AnyFlowNode[] = pipeline.nodes.map((node) => {
    const pos = g.node(node.id)
    return {
      id: node.id,
      type: node.type,
      position: { x: pos.x - NODE_WIDTH / 2, y: pos.y - NODE_HEIGHT / 2 },
      data: node.data,
    } as AnyFlowNode
  })

  const edges: Edge[] = edgeDefs.map(({ source, target, label }, i) => ({
    id: `e-${source}-${target}-${i}`,
    source,
    target,
    label,
    animated: false,
    style: { stroke: '#94a3b8' },
    labelStyle: { fontSize: 11, fill: '#64748b', fontWeight: 600 },
    labelBgStyle: { fill: '#f8fafc', fillOpacity: 0.9 },
  }))

  return { nodes, edges }
}
