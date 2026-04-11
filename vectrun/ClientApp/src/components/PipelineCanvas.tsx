import {
  ReactFlow,
  Background,
  Controls,
  MiniMap,
  BackgroundVariant,
  Panel,
  ConnectionMode,
  MarkerType,
} from '@xyflow/react'
import type { NodeTypes, EdgeTypes, OnNodesChange, OnEdgesChange, Edge, Connection } from '@xyflow/react'
import type { AnyFlowNode } from '../lib/layoutGraph'
import { AgentNode } from './nodes/AgentNode'
import { BranchNode } from './nodes/BranchNode'
import { LogicNode } from './nodes/LogicNode'
import { WaitNode } from './nodes/WaitNode'
import { FloatingEdge } from './edges/FloatingEdge'

const nodeTypes = {
  agent: AgentNode,
  branch: BranchNode,
  logic: LogicNode,
  wait: WaitNode,
} satisfies NodeTypes

const edgeTypes = {
  floating: FloatingEdge,
} satisfies EdgeTypes

const defaultEdgeOptions = {
  type: 'floating',
  style: { stroke: '#94a3b8', strokeWidth: 1.5 },
  markerEnd: { type: MarkerType.ArrowClosed, color: '#94a3b8' },
}

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
  onAutoLayout: () => void
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
  onAutoLayout,
}: Props) {
  return (
    // absolute inset-0 ensures ReactFlow always fills its relative parent
    // regardless of how the flex height chain resolves
    <div style={{ position: 'absolute', inset: 0 }}>
      <ReactFlow
        nodes={nodes}
        edges={edges}
        nodeTypes={nodeTypes}
        edgeTypes={edgeTypes}
        defaultEdgeOptions={defaultEdgeOptions}
        connectionMode={ConnectionMode.Loose}
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
        <Panel position="top-right">
          <button
            onClick={onAutoLayout}
            title="Auto-layout graph"
            className="flex items-center gap-1.5 rounded-lg border border-slate-200 bg-white px-2.5 py-1.5 text-xs font-medium text-slate-600 shadow-sm hover:bg-slate-50 hover:text-slate-800 transition"
          >
            <LayoutIcon />
            Layout
          </button>
        </Panel>
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

function LayoutIcon() {
  return (
    <svg className="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <rect x="3" y="3" width="7" height="5" rx="1" />
      <rect x="14" y="3" width="7" height="5" rx="1" />
      <rect x="7" y="13" width="10" height="5" rx="1" />
      <path d="M6.5 8v2.5a2 2 0 0 0 2 2h7a2 2 0 0 0 2-2V8" />
      <line x1="12" y1="10.5" x2="12" y2="13" />
    </svg>
  )
}
