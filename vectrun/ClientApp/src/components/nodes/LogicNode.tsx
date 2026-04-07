import { Handle, Position } from '@xyflow/react'
import type { Node, NodeProps } from '@xyflow/react'
import type { LogicNodeData } from '../../types/pipeline'

export type LogicFlowNode = Node<LogicNodeData, 'logic'>

export function LogicNode({ data, id, selected }: NodeProps<LogicFlowNode>) {
  return (
    <div className={`w-55 rounded-xl bg-white transition-all duration-150 ${
      selected
        ? 'border-2 border-emerald-500 shadow-lg shadow-emerald-100/60'
        : 'border border-emerald-200 shadow-sm hover:shadow-md'
    }`}>
      <Handle type="target" position={Position.Top} className="!bg-emerald-400 !w-3 !h-3 !border-2 !border-white" />
      <div className="rounded-t-[11px] bg-gradient-to-br from-emerald-500 to-emerald-600 px-3 py-1.5 flex items-center gap-2">
        <span className="text-[10px] font-semibold text-white/70 uppercase tracking-widest">Logic</span>
        <span className="ml-auto font-mono text-[10px] text-emerald-200/80">{id}</span>
      </div>
      <div className="px-3 py-2">
        {data.name
          ? <p className="text-sm font-semibold text-slate-800 truncate">{data.name}</p>
          : <p className="text-xs text-slate-400 italic">unnamed</p>
        }
        <span className="mt-0.5 inline-block rounded-md bg-emerald-50 px-1.5 py-0.5 text-xs font-semibold text-emerald-700 border border-emerald-100">
          {data.logicType}
        </span>
        {data.processPath && (
          <p className="mt-0.5 text-xs text-slate-400 truncate font-mono">{data.processPath}</p>
        )}
      </div>
      <Handle type="source" position={Position.Bottom} className="!bg-emerald-400 !w-3 !h-3 !border-2 !border-white" />
    </div>
  )
}
