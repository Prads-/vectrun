import { Handle, Position } from '@xyflow/react'
import type { Node, NodeProps } from '@xyflow/react'
import type { AgentNodeData } from '../../types/pipeline'

export type AgentFlowNode = Node<AgentNodeData, 'agent'>

export function AgentNode({ data, id, selected }: NodeProps<AgentFlowNode>) {
  return (
    <div className={`w-55 rounded-xl bg-white transition-all duration-150 ${
      selected
        ? 'border-2 border-blue-500 shadow-lg shadow-blue-100/60'
        : 'border border-blue-200 shadow-sm hover:shadow-md'
    }`}>
      <Handle type="target" position={Position.Top} className="!bg-blue-400 !w-3 !h-3 !border-2 !border-white" />
      <div className="rounded-t-[11px] bg-gradient-to-br from-blue-500 to-blue-600 px-3 py-1.5 flex items-center gap-2">
        <span className="text-[10px] font-semibold text-white/70 uppercase tracking-widest">Agent</span>
        <span className="ml-auto font-mono text-[10px] text-blue-200/80">{id}</span>
      </div>
      <div className="px-3 py-2">
        {data.name
          ? <p className="text-sm font-semibold text-slate-800 truncate">{data.name}</p>
          : <p className="text-xs text-slate-400 italic">unnamed</p>
        }
        {data.agentId && (
          <p className="mt-0.5 text-xs text-blue-500 truncate">{data.agentId}</p>
        )}
        {data.toolIds && data.toolIds.length > 0 && (
          <p className="mt-1 text-xs text-slate-400">
            {data.toolIds.length} tool{data.toolIds.length !== 1 ? 's' : ''}
          </p>
        )}
      </div>
      <Handle type="source" position={Position.Bottom} className="!bg-blue-400 !w-3 !h-3 !border-2 !border-white" />
    </div>
  )
}
