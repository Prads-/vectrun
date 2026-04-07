import type { AgentConfig } from '../types/workspace'

export interface NodeDragData {
  nodeType: 'agent' | 'branch' | 'logic' | 'wait'
  agentId?: string
}

export const DRAG_TYPE = 'text/plain'

function startDrag(event: React.DragEvent, data: NodeDragData) {
  event.dataTransfer.setData(DRAG_TYPE, JSON.stringify(data))
  event.dataTransfer.effectAllowed = 'move'
}

const BUILTINS: { type: 'branch' | 'logic' | 'wait'; label: string; bg: string; dot: string; desc: string }[] = [
  { type: 'branch', label: 'Branch', bg: 'border-amber-200 bg-amber-50 hover:bg-amber-100', dot: 'bg-amber-400', desc: 'Conditional routing' },
  { type: 'logic', label: 'Logic', bg: 'border-emerald-200 bg-emerald-50 hover:bg-emerald-100', dot: 'bg-emerald-400', desc: 'Script or process' },
  { type: 'wait', label: 'Wait', bg: 'border-violet-200 bg-violet-50 hover:bg-violet-100', dot: 'bg-violet-400', desc: 'Time delay' },
]

interface Props {
  agents: AgentConfig[]
}

export function NodePalette({ agents }: Props) {
  return (
    <div className="flex w-56 shrink-0 flex-col gap-5 border-r border-slate-200 bg-white p-3 overflow-y-auto">

      {/* Agents */}
      <section>
        <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-400">Agents</p>
        {agents.length === 0 ? (
          <p className="text-xs text-slate-400 italic leading-relaxed">
            No agents yet. Create one in the <strong>Agents</strong> tab on the right, then drag it here.
          </p>
        ) : (
          <div className="flex flex-col gap-1.5">
            {agents.map(agent => (
              <div
                key={agent.agentName}
                draggable
                onDragStart={e => startDrag(e, { nodeType: 'agent', agentId: agent.agentName })}
                className="flex cursor-grab flex-col rounded-lg border border-blue-200 bg-blue-50 px-3 py-2 hover:bg-blue-100 active:cursor-grabbing transition select-none"
                title="Drag onto canvas"
              >
                <div className="flex items-center gap-2">
                  <span className="h-2 w-2 rounded-full bg-blue-400 shrink-0" />
                  <span className="text-sm font-semibold text-blue-800 truncate">{agent.agentName}</span>
                </div>
                {agent.modelId && (
                  <span className="mt-0.5 pl-4 text-xs text-blue-400 truncate">{agent.modelId}</span>
                )}
              </div>
            ))}
          </div>
        )}
      </section>

      {/* Built-in node types */}
      <section>
        <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-400">Built-in</p>
        <div className="flex flex-col gap-1.5">
          {BUILTINS.map(({ type, label, bg, dot, desc }) => (
            <div
              key={type}
              draggable
              onDragStart={e => startDrag(e, { nodeType: type })}
              className={`flex cursor-grab flex-col rounded-lg border px-3 py-2 active:cursor-grabbing transition select-none ${bg}`}
              title="Drag onto canvas"
            >
              <div className="flex items-center gap-2">
                <span className={`h-2 w-2 rounded-full ${dot} shrink-0`} />
                <span className="text-sm font-semibold text-slate-700">{label}</span>
              </div>
              <span className="mt-0.5 pl-4 text-xs text-slate-400">{desc}</span>
            </div>
          ))}
        </div>
      </section>

    </div>
  )
}
