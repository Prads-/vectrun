import { Handle, Position } from '@xyflow/react'
import type { Node, NodeProps } from '@xyflow/react'
import type { BranchNodeData } from '../../types/pipeline'

export type BranchFlowNode = Node<BranchNodeData, 'branch'>

export function BranchNode({ data, id, selected }: NodeProps<BranchFlowNode>) {
  return (
    <div className={`w-55 rounded-xl bg-white transition-all duration-150 ${
      selected
        ? 'border-2 border-amber-500 shadow-lg shadow-amber-100/60'
        : 'border border-amber-200 shadow-sm hover:shadow-md'
    }`}>
      <Handle type="source" position={Position.Top}    className="node-handle !bg-amber-400" />
      <Handle type="source" position={Position.Left}   className="node-handle !bg-amber-400" />
      <Handle type="source" position={Position.Right}  className="node-handle !bg-amber-400" />
      <div className="rounded-t-[11px] bg-gradient-to-br from-amber-500 to-amber-600 px-3 py-1.5 flex items-center gap-2">
        <span className="text-[10px] font-semibold text-white/70 uppercase tracking-widest">Branch</span>
        <span className="ml-auto font-mono text-[10px] text-amber-200/80">{id}</span>
      </div>
      <div className="px-3 py-2">
        {data.name
          ? <p className="text-sm font-semibold text-slate-800 truncate">{data.name}</p>
          : <p className="text-xs text-slate-400 italic">unnamed</p>
        }
        <p className="mt-0.5 text-xs text-slate-400 truncate">&quot;{data.expectedOutput}&quot;</p>
      </div>
      <Handle type="source" position={Position.Bottom} className="node-handle !bg-amber-400" />
    </div>
  )
}
