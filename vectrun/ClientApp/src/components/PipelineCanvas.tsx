import {
  ReactFlow,
  Background,
  Controls,
  MiniMap,
  BackgroundVariant,
  Panel,
} from '@xyflow/react'
import type { NodeTypes, OnNodesChange, OnEdgesChange, Edge, Connection } from '@xyflow/react'
import type { AnyFlowNode } from '../lib/layoutGraph'
import { AgentNode } from './nodes/AgentNode'
import { BranchNode } from './nodes/BranchNode'
import { LogicNode } from './nodes/LogicNode'
import { WaitNode } from './nodes/WaitNode'

const nodeTypes = {
  agent: AgentNode,
  branch: BranchNode,
  logic: LogicNode,
  wait: WaitNode,
} satisfies NodeTypes

interface Props {
  nodes: AnyFlowNode[]
  edges: Edge[]
  onNodesChange: OnNodesChange<AnyFlowNode>
  onEdgesChange: OnEdgesChange
  onConnect: (connection: Connection) => void
  onNodeClick: (nodeId: string) => void
  onPaneClick: () => void
  onDrop: (e: React.DragEvent) => void
  onDragOver: (e: React.DragEvent) => void
}

export function PipelineCanvas({
  nodes,
  edges,
  onNodesChange,
  onEdgesChange,
  onConnect,
  onNodeClick,
  onPaneClick,
  onDrop,
  onDragOver,
}: Props) {
  return (
    // absolute inset-0 ensures ReactFlow always fills its relative parent
    // regardless of how the flex height chain resolves
    <div style={{ position: 'absolute', inset: 0 }}>
      <ReactFlow
        nodes={nodes}
        edges={edges}
        nodeTypes={nodeTypes}
        onNodesChange={onNodesChange}
        onEdgesChange={onEdgesChange}
        onConnect={onConnect}
        onNodeClick={(_event, node) => onNodeClick(node.id)}
        onPaneClick={onPaneClick}
        onDrop={onDrop}
        onDragOver={onDragOver}
        deleteKeyCode="Delete"
        fitView
        fitViewOptions={{ padding: 0.25 }}
        proOptions={{ hideAttribution: true }}
      >
        <Background variant={BackgroundVariant.Dots} gap={20} size={1} color="#e2e8f0" />
        <Controls />
        <MiniMap
          nodeColor={(node) => {
            switch (node.type) {
              case 'agent': return '#3b82f6'
              case 'branch': return '#f59e0b'
              case 'logic': return '#10b981'
              case 'wait': return '#8b5cf6'
              default: return '#94a3b8'
            }
          }}
          maskColor="rgba(248,250,252,0.7)"
        />
        {nodes.length === 0 && (
          <Panel position="top-center">
            <div className="mt-16 flex flex-col items-center gap-2 select-none pointer-events-none">
              <div className="rounded-xl border-2 border-dashed border-slate-200 bg-white/80 px-8 py-6 text-center shadow-sm backdrop-blur-sm">
                <p className="text-sm font-semibold text-slate-500">Canvas is empty</p>
                <p className="mt-1 text-xs text-slate-400">
                  Open <strong>Nodes</strong> in the sidebar to add Branch, Logic, or Wait nodes.<br/>
                  Open <strong>Agents</strong> to create an agent and add it here.
                </p>
              </div>
            </div>
          </Panel>
        )}
      </ReactFlow>
    </div>
  )
}
