import { DRAG_TYPE } from '../../lib/dragNode'
import type { NodeDragData } from '../../lib/dragNode'
export type { NodeDragData }
import type { AnyFlowNode } from '../../lib/layoutGraph'
import type { AnyNodeData } from '../../types/pipeline'
import type { AgentConfig } from '../../types/workspace'
import { NodePropertiesPanel } from './NodePropertiesPanel'

const BUILTIN_TYPES: { type: 'branch' | 'logic' | 'wait'; label: string; desc: string; classes: string; dot: string }[] = [
  {
    type: 'branch',
    label: 'Branch',
    desc: 'Conditional routing',
    classes: 'border-amber-200 bg-amber-50 hover:bg-amber-100 text-amber-800',
    dot: 'bg-amber-400',
  },
  {
    type: 'logic',
    label: 'Logic',
    desc: 'Script or process',
    classes: 'border-emerald-200 bg-emerald-50 hover:bg-emerald-100 text-emerald-800',
    dot: 'bg-emerald-400',
  },
  {
    type: 'wait',
    label: 'Wait',
    desc: 'Time delay',
    classes: 'border-violet-200 bg-violet-50 hover:bg-violet-100 text-violet-800',
    dot: 'bg-violet-400',
  },
]

function nodeStyle(type: string) {
  switch (type) {
    case 'agent':  return { dot: 'bg-blue-400',    badge: 'bg-blue-50 text-blue-700 border-blue-100',    row: 'border-blue-200 bg-blue-50/60' }
    case 'branch': return { dot: 'bg-amber-400',   badge: 'bg-amber-50 text-amber-700 border-amber-100', row: 'border-amber-200 bg-amber-50/60' }
    case 'logic':  return { dot: 'bg-emerald-400', badge: 'bg-emerald-50 text-emerald-700 border-emerald-100', row: 'border-emerald-200 bg-emerald-50/60' }
    case 'wait':   return { dot: 'bg-violet-400',  badge: 'bg-violet-50 text-violet-700 border-violet-100', row: 'border-violet-200 bg-violet-50/60' }
    default:       return { dot: 'bg-slate-400',   badge: 'bg-slate-50 text-slate-700 border-slate-100',  row: 'border-slate-200 bg-slate-50/60' }
  }
}

interface Props {
  nodes: AnyFlowNode[]
  selectedNodeId: string | null
  editingNodeId: string | null
  agents: AgentConfig[]
  pipelineName: string
  startNodeId: string
  onSelectNode: (id: string) => void
  onEditNode: (id: string | null) => void
  onDeleteNode: (id: string) => void
  onNodeDataChange: (id: string, data: AnyNodeData) => void
  onPipelineMetaChange: (name: string, startNodeId: string) => void
  onAddNode: (drag: NodeDragData) => void
}

export function NodesPanel({
  nodes,
  selectedNodeId,
  editingNodeId,
  agents,
  pipelineName,
  startNodeId,
  onSelectNode,
  onEditNode,
  onDeleteNode,
  onNodeDataChange,
  onPipelineMetaChange,
  onAddNode,
}: Props) {
  if (editingNodeId) {
    return (
      <div className="flex flex-col h-full">
        <div className="flex items-center gap-2 px-3 py-2.5 border-b border-slate-100 shrink-0">
          <button
            onClick={() => onEditNode(null)}
            className="flex items-center gap-1.5 rounded-lg px-2 py-1 text-xs font-medium text-slate-500 hover:bg-slate-100 hover:text-slate-800 transition"
          >
            <ChevronLeftIcon />
            All nodes
          </button>
        </div>
        <div className="flex-1 overflow-y-auto">
          <NodePropertiesPanel
            selectedNodeId={editingNodeId}
            nodes={nodes}
            pipelineName={pipelineName}
            startNodeId={startNodeId}
            agents={agents}
            onPipelineMetaChange={onPipelineMetaChange}
            onNodeDataChange={onNodeDataChange}
            onDeleteNode={id => { onDeleteNode(id); onEditNode(null) }}
          />
        </div>
      </div>
    )
  }

  return (
    <div className="flex flex-col h-full overflow-hidden">
      {/* Pipeline metadata */}
      <div className="px-4 pt-4 pb-3 border-b border-slate-100 shrink-0">
        <p className="mb-2.5 text-[10px] font-semibold uppercase tracking-widest text-slate-400">Pipeline</p>
        <div className="flex flex-col gap-2">
          <div className="flex flex-col gap-1">
            <span className="text-xs font-medium text-slate-500">Name</span>
            <input
              value={pipelineName}
              onChange={e => onPipelineMetaChange(e.target.value, startNodeId)}
              className="rounded-lg border border-slate-200 px-3 py-1.5 text-sm text-slate-700 focus:border-blue-400 focus:outline-none focus:ring-2 focus:ring-blue-100 transition"
              style={{ fontFamily: "'DM Sans', system-ui, sans-serif" }}
            />
          </div>
          <div className="flex flex-col gap-1">
            <span className="text-xs font-medium text-slate-500">Start node</span>
            <select
              value={startNodeId}
              onChange={e => onPipelineMetaChange(pipelineName, e.target.value)}
              className="rounded-lg border border-slate-200 px-3 py-1.5 text-sm text-slate-700 focus:border-blue-400 focus:outline-none focus:ring-2 focus:ring-blue-100 transition"
            >
              <option value="">— none —</option>
              {nodes.map(n => {
                const name = (n.data as { name?: string }).name
                return (
                  <option key={n.id} value={n.id}>
                    {name ? `${name} (${n.id})` : n.id}
                  </option>
                )
              })}
            </select>
          </div>
        </div>
      </div>

      {/* Built-in node palette */}
      <div className="px-4 py-3 border-b border-slate-100 shrink-0">
        <p className="mb-2.5 text-[10px] font-semibold uppercase tracking-widest text-slate-400">Add node</p>
        <div className="flex flex-col gap-1.5">
          {BUILTIN_TYPES.map(({ type, label, desc, classes, dot }) => (
            <div
              key={type}
              draggable
              onDragStart={e => {
                const data: NodeDragData = { nodeType: type }
                e.dataTransfer.setData(DRAG_TYPE, JSON.stringify(data))
                e.dataTransfer.effectAllowed = 'move'
              }}
              onClick={() => onAddNode({ nodeType: type })}
              className={`flex cursor-pointer items-center gap-2.5 rounded-lg border px-3 py-2 select-none transition active:opacity-70 ${classes}`}
              title={`Click or drag to add a ${label} node`}
            >
              <span className={`h-2 w-2 rounded-full shrink-0 ${dot}`} />
              <div className="flex-1 min-w-0">
                <p className="text-sm font-semibold">{label}</p>
                <p className="text-xs opacity-60">{desc}</p>
              </div>
              <PlusIcon className="text-current opacity-40 shrink-0" />
            </div>
          ))}
        </div>
      </div>

      {/* Node list */}
      <div className="flex-1 overflow-y-auto px-4 py-3">
        <div className="flex items-center justify-between mb-2.5">
          <p className="text-[10px] font-semibold uppercase tracking-widest text-slate-400">
            Pipeline nodes
          </p>
          {nodes.length > 0 && (
            <span className="text-[10px] font-semibold text-slate-400 bg-slate-100 rounded-full px-1.5 py-0.5">
              {nodes.length}
            </span>
          )}
        </div>
        {nodes.length === 0 ? (
          <p className="text-xs text-slate-400 italic leading-relaxed">
            No nodes yet. Drag a type from above onto the canvas, or drop an agent from the Agents section.
          </p>
        ) : (
          <div className="flex flex-col gap-1">
            {nodes.map(node => {
              const s = nodeStyle(node.type ?? '')
              const isSelected = selectedNodeId === node.id
              return (
                <div
                  key={node.id}
                  onClick={() => onSelectNode(node.id)}
                  className={`group flex items-center gap-2 rounded-lg border px-2.5 py-2 cursor-pointer transition ${
                    isSelected ? `${s.row} border-opacity-100` : 'border-transparent hover:bg-slate-50 hover:border-slate-100'
                  }`}
                >
                  <span className={`h-2 w-2 rounded-full shrink-0 ${s.dot}`} />
                  <div className="flex-1 min-w-0">
                    {(node.data as { name?: string }).name
                      ? <>
                          <p className="text-sm font-medium text-slate-700 truncate leading-none mb-0.5">
                            {(node.data as { name?: string }).name}
                          </p>
                          <p className="text-[10px] text-slate-400 font-mono truncate">{node.id}</p>
                        </>
                      : <p className="text-sm font-medium text-slate-500 font-mono truncate leading-none mb-0.5">{node.id}</p>
                    }
                    <span className={`inline-block rounded px-1 py-px text-[10px] font-semibold border ${s.badge}`}>
                      {node.type}
                    </span>
                  </div>
                  <div className={`flex items-center gap-0.5 shrink-0 transition ${isSelected ? 'opacity-100' : 'opacity-0 group-hover:opacity-100'}`}>
                    <button
                      onClick={e => { e.stopPropagation(); onEditNode(node.id) }}
                      className="rounded-md p-1.5 text-slate-400 hover:bg-white hover:text-slate-700 transition"
                      title="Edit properties"
                    >
                      <EditIcon />
                    </button>
                    <button
                      onClick={e => { e.stopPropagation(); onDeleteNode(node.id) }}
                      className="rounded-md p-1.5 text-slate-400 hover:bg-red-50 hover:text-red-600 transition"
                      title="Delete node"
                    >
                      <TrashIcon />
                    </button>
                  </div>
                </div>
              )
            })}
          </div>
        )}
      </div>
    </div>
  )
}

function ChevronLeftIcon() {
  return (
    <svg className="h-3.5 w-3.5" viewBox="0 0 20 20" fill="currentColor">
      <path fillRule="evenodd" d="M12.707 5.293a1 1 0 010 1.414L9.414 10l3.293 3.293a1 1 0 01-1.414 1.414l-4-4a1 1 0 010-1.414l4-4a1 1 0 011.414 0z" clipRule="evenodd" />
    </svg>
  )
}

function PlusIcon({ className }: { className?: string }) {
  return (
    <svg className={`h-4 w-4 ${className ?? ''}`} viewBox="0 0 20 20" fill="currentColor">
      <path fillRule="evenodd" d="M10 3a1 1 0 011 1v5h5a1 1 0 110 2h-5v5a1 1 0 11-2 0v-5H4a1 1 0 110-2h5V4a1 1 0 011-1z" clipRule="evenodd" />
    </svg>
  )
}

function EditIcon() {
  return (
    <svg className="h-3.5 w-3.5" viewBox="0 0 20 20" fill="currentColor">
      <path d="M13.586 3.586a2 2 0 112.828 2.828l-.793.793-2.828-2.828.793-.793zM11.379 5.793L3 14.172V17h2.828l8.38-8.379-2.83-2.828z" />
    </svg>
  )
}

function TrashIcon() {
  return (
    <svg className="h-3.5 w-3.5" viewBox="0 0 20 20" fill="currentColor">
      <path fillRule="evenodd" d="M9 2a1 1 0 00-.894.553L7.382 4H4a1 1 0 000 2v10a2 2 0 002 2h8a2 2 0 002-2V6a1 1 0 100-2h-3.382l-.724-1.447A1 1 0 0011 2H9zM7 8a1 1 0 012 0v6a1 1 0 11-2 0V8zm5-1a1 1 0 00-1 1v6a1 1 0 102 0V8a1 1 0 00-1-1z" clipRule="evenodd" />
    </svg>
  )
}
