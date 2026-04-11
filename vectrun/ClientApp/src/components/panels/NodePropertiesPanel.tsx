import type { AnyFlowNode } from '../../lib/layoutGraph'
import type { AnyNodeData, AgentNodeData, BranchNodeData, LogicNodeData, WaitNodeData, RetryPolicy } from '../../types/pipeline'
import type { AgentConfig } from '../../types/workspace'

interface Props {
  selectedNodeId: string | null
  nodes: AnyFlowNode[]
  pipelineName: string
  startNodeId: string
  agents: AgentConfig[]
  onPipelineMetaChange: (name: string, startNodeId: string) => void
  onNodeDataChange: (nodeId: string, data: AnyNodeData) => void
  onDeleteNode: (nodeId: string) => void
}

function typeBadgeClass(type: string) {
  switch (type) {
    case 'agent': return 'bg-blue-100 text-blue-700'
    case 'branch': return 'bg-amber-100 text-amber-700'
    case 'logic': return 'bg-emerald-100 text-emerald-700'
    case 'wait': return 'bg-violet-100 text-violet-700'
    default: return 'bg-slate-100 text-slate-700'
  }
}

export function NodePropertiesPanel({
  selectedNodeId,
  nodes,
  pipelineName,
  startNodeId,
  agents,
  onPipelineMetaChange,
  onNodeDataChange,
  onDeleteNode,
}: Props) {
  const selectedNode = selectedNodeId ? nodes.find(n => n.id === selectedNodeId) ?? null : null

  if (!selectedNode) {
    return (
      <div className="flex flex-col gap-4 p-4">
        <p className="text-xs font-semibold uppercase tracking-wide text-slate-400">Pipeline</p>
        <div className="flex flex-col gap-3">
          <label className="flex flex-col gap-1">
            <span className="text-xs font-medium text-slate-500">Pipeline name</span>
            <input
              value={pipelineName}
              onChange={e => onPipelineMetaChange(e.target.value, startNodeId)}
              className="rounded-lg border border-slate-200 px-3 py-1.5 text-sm text-slate-700 focus:border-blue-400 focus:outline-none focus:ring-2 focus:ring-blue-100"
            />
          </label>
          <label className="flex flex-col gap-1">
            <span className="text-xs font-medium text-slate-500">Start node</span>
            <select
              value={startNodeId}
              onChange={e => onPipelineMetaChange(pipelineName, e.target.value)}
              className="rounded-lg border border-slate-200 px-3 py-1.5 text-sm text-slate-700 focus:border-blue-400 focus:outline-none focus:ring-2 focus:ring-blue-100"
            >
              <option value="">— none —</option>
              {nodes.map(n => (
                <option key={n.id} value={n.id}>{n.id}</option>
              ))}
            </select>
          </label>
        </div>
        <p className="text-xs text-slate-400 italic">Select a node to edit its properties.</p>
      </div>
    )
  }

  const node = selectedNode

  return (
    <div className="flex flex-col gap-3 p-4">
      {/* Header */}
      <div className="flex items-center gap-2">
        <span className="font-mono text-xs font-semibold text-slate-700">{node.id}</span>
        <span className={`rounded px-1.5 py-0.5 text-xs font-semibold uppercase ${typeBadgeClass(node.type ?? '')}`}>
          {node.type}
        </span>
        <button
          onClick={() => onDeleteNode(node.id)}
          className="ml-auto rounded-lg p-1.5 text-slate-400 hover:bg-red-50 hover:text-red-600 transition"
          title="Delete node"
        >
          <TrashIcon />
        </button>
      </div>

      {/* Name — common to all node types */}
      <label className="flex flex-col gap-1">
        <span className="text-xs font-medium text-slate-500">Name</span>
        <input
          value={(node.data as { name?: string }).name ?? ''}
          onChange={e => onNodeDataChange(node.id, { ...node.data, name: e.target.value })}
          placeholder="e.g. Classify intent"
          className="rounded-lg border border-slate-200 px-3 py-1.5 text-sm text-slate-700 focus:border-blue-400 focus:outline-none focus:ring-2 focus:ring-blue-100"
        />
      </label>

      {node.type === 'agent' && (
        <AgentForm
          data={node.data as AgentNodeData}
          agents={agents}
          onChange={d => onNodeDataChange(node.id, d)}
        />
      )}

      {node.type === 'branch' && (
        <BranchForm
          data={node.data as BranchNodeData}
          onChange={d => onNodeDataChange(node.id, d)}
        />
      )}

      {node.type === 'logic' && (
        <LogicForm
          data={node.data as LogicNodeData}
          onChange={d => onNodeDataChange(node.id, d)}
        />
      )}

      {node.type === 'wait' && (
        <WaitForm
          data={node.data as WaitNodeData}
          onChange={d => onNodeDataChange(node.id, d)}
        />
      )}
    </div>
  )
}

function AgentForm({ data, agents, onChange }: {
  data: AgentNodeData
  agents: AgentConfig[]
  onChange: (d: AgentNodeData) => void
}) {
  const matchedAgent = agents.find(a => a.agentName === data.agentId)
  return (
    <div className="flex flex-col gap-3">
      <label className="flex flex-col gap-1">
        <span className="text-xs font-medium text-slate-500">Agent ID</span>
        {agents.length > 0 ? (
          <select
            value={data.agentId}
            onChange={e => onChange({ ...data, agentId: e.target.value })}
            className="rounded-lg border border-slate-200 px-3 py-1.5 text-sm text-slate-700 focus:border-blue-400 focus:outline-none focus:ring-2 focus:ring-blue-100"
          >
            <option value="">— select agent —</option>
            {agents.map(a => (
              <option key={a.agentName} value={a.agentName}>{a.agentName}</option>
            ))}
          </select>
        ) : (
          <input
            value={data.agentId}
            onChange={e => onChange({ ...data, agentId: e.target.value })}
            className="rounded-lg border border-slate-200 px-3 py-1.5 text-sm text-slate-700 focus:border-blue-400 focus:outline-none focus:ring-2 focus:ring-blue-100"
          />
        )}
      </label>
      {matchedAgent && (
        <div className="rounded-lg bg-blue-50 px-3 py-2 text-xs text-blue-700">
          Model: <span className="font-semibold">{matchedAgent.modelId}</span>
        </div>
      )}
      <RetryPolicyForm
        policy={data.retry}
        onChange={policy => onChange({ ...data, retry: policy })}
      />
    </div>
  )
}

function BranchForm({ data, onChange }: {
  data: BranchNodeData
  onChange: (d: BranchNodeData) => void
}) {
  return (
    <div className="flex flex-col gap-3">
      <label className="flex flex-col gap-1">
        <span className="text-xs font-medium text-slate-500">Expected output</span>
        <input
          value={data.expectedOutput}
          onChange={e => onChange({ ...data, expectedOutput: e.target.value })}
          className="rounded-lg border border-slate-200 px-3 py-1.5 text-sm text-slate-700 focus:border-blue-400 focus:outline-none focus:ring-2 focus:ring-blue-100"
        />
      </label>
      <p className="text-xs text-slate-400 italic">Use the canvas to draw true/false connections.</p>
    </div>
  )
}

function LogicForm({ data, onChange }: {
  data: LogicNodeData
  onChange: (d: LogicNodeData) => void
}) {
  return (
    <div className="flex flex-col gap-3">
      <label className="flex flex-col gap-1">
        <span className="text-xs font-medium text-slate-500">Logic type</span>
        <select
          value={data.logicType}
          onChange={e => onChange({ ...data, logicType: e.target.value })}
          className="rounded-lg border border-slate-200 px-3 py-1.5 text-sm text-slate-700 focus:border-blue-400 focus:outline-none focus:ring-2 focus:ring-blue-100"
        >
          <option value="process">process</option>
          <option value="script">script</option>
        </select>
      </label>
      {data.logicType === 'process' && (
        <>
          <label className="flex flex-col gap-1">
            <span className="text-xs font-medium text-slate-500">Path type</span>
            <div className="flex gap-3">
              {(['relative', 'absolute'] as const).map(pt => (
                <label key={pt} className="flex items-center gap-1.5 cursor-pointer">
                  <input
                    type="radio"
                    name="processPathType"
                    value={pt}
                    checked={(data.processPathType ?? 'relative') === pt}
                    onChange={() => onChange({ ...data, processPathType: pt })}
                  />
                  <span className="text-sm text-slate-700 capitalize">{pt}</span>
                </label>
              ))}
            </div>
          </label>
          <label className="flex flex-col gap-1">
            <span className="text-xs font-medium text-slate-500">
              {(data.processPathType ?? 'relative') === 'relative' ? 'Process path (relative to pipeline folder)' : 'Process path (absolute)'}
            </span>
            <input
              value={data.processPath ?? ''}
              onChange={e => onChange({ ...data, processPath: e.target.value })}
              className="rounded-lg border border-slate-200 px-3 py-1.5 text-sm text-slate-700 focus:border-blue-400 focus:outline-none focus:ring-2 focus:ring-blue-100"
            />
            {(data.processPathType ?? 'relative') === 'relative' && (
              <span className="text-xs text-slate-400">
                Resolved as: <span className="font-mono">&lt;pipeline folder&gt;/&lt;path&gt;</span>
              </span>
            )}
          </label>
          <label className="flex flex-col gap-1">
            <span className="text-xs font-medium text-slate-500">Process input (optional)</span>
            <textarea
              value={data.processInput ?? ''}
              onChange={e => onChange({ ...data, processInput: e.target.value || undefined })}
              rows={3}
              className="resize-y rounded-lg border border-slate-200 px-3 py-1.5 font-mono text-sm text-slate-700 focus:border-blue-400 focus:outline-none focus:ring-2 focus:ring-blue-100"
            />
            <span className="text-xs text-slate-400">If set, this is piped to stdin instead of the previous node's output.</span>
          </label>
        </>
      )}
      {data.logicType === 'script' && (
        <label className="flex flex-col gap-1">
          <span className="text-xs font-medium text-slate-500">Script</span>
          <textarea
            value={data.script ?? ''}
            onChange={e => onChange({ ...data, script: e.target.value })}
            rows={6}
            className="resize-y rounded-lg border border-slate-200 px-3 py-1.5 font-mono text-sm text-slate-700 focus:border-blue-400 focus:outline-none focus:ring-2 focus:ring-blue-100"
          />
        </label>
      )}
      <RetryPolicyForm
        policy={data.retry}
        onChange={policy => onChange({ ...data, retry: policy })}
      />
    </div>
  )
}

function WaitForm({ data, onChange }: {
  data: WaitNodeData
  onChange: (d: WaitNodeData) => void
}) {
  return (
    <div className="flex flex-col gap-3">
      <label className="flex flex-col gap-1">
        <span className="text-xs font-medium text-slate-500">Duration (ms)</span>
        <input
          type="number"
          value={data.durationMs}
          onChange={e => onChange({ ...data, durationMs: Number(e.target.value) })}
          className="rounded-lg border border-slate-200 px-3 py-1.5 text-sm text-slate-700 focus:border-blue-400 focus:outline-none focus:ring-2 focus:ring-blue-100"
        />
      </label>
    </div>
  )
}

function RetryPolicyForm({ policy, onChange }: {
  policy: RetryPolicy | undefined
  onChange: (policy: RetryPolicy | undefined) => void
}) {
  const retryCount = policy?.retryCount ?? 0
  const retryDelayMs = policy?.retryDelayMs ?? 0
  const delayType = policy?.delayType ?? 'linear'

  function update(count: number, delayMs: number, type: string) {
    onChange(count === 0 ? undefined : { retryCount: count, retryDelayMs: delayMs, delayType: type })
  }

  return (
    <div className="flex flex-col gap-3 border-t border-slate-100 pt-3">
      <p className="text-xs font-semibold uppercase tracking-wide text-slate-400">Retry Policy</p>
      <label className="flex flex-col gap-1">
        <span className="text-xs font-medium text-slate-500">Retries</span>
        <input
          type="number"
          min={0}
          value={retryCount}
          onChange={e => update(Number(e.target.value), retryDelayMs, delayType)}
          className="rounded-lg border border-slate-200 px-3 py-1.5 text-sm text-slate-700 focus:border-blue-400 focus:outline-none focus:ring-2 focus:ring-blue-100"
        />
      </label>
      {retryCount > 0 && (
        <>
          <label className="flex flex-col gap-1">
            <span className="text-xs font-medium text-slate-500">Delay (ms)</span>
            <input
              type="number"
              min={0}
              value={retryDelayMs}
              onChange={e => update(retryCount, Number(e.target.value), delayType)}
              className="rounded-lg border border-slate-200 px-3 py-1.5 text-sm text-slate-700 focus:border-blue-400 focus:outline-none focus:ring-2 focus:ring-blue-100"
            />
          </label>
          <label className="flex flex-col gap-1">
            <span className="text-xs font-medium text-slate-500">Delay type</span>
            <select
              value={delayType}
              onChange={e => update(retryCount, retryDelayMs, e.target.value)}
              className="rounded-lg border border-slate-200 px-3 py-1.5 text-sm text-slate-700 focus:border-blue-400 focus:outline-none focus:ring-2 focus:ring-blue-100"
            >
              <option value="linear">linear</option>
              <option value="sliding">sliding</option>
            </select>
          </label>
        </>
      )}
    </div>
  )
}

function TrashIcon() {
  return (
    <svg className="h-4 w-4" viewBox="0 0 20 20" fill="currentColor">
      <path fillRule="evenodd" d="M9 2a1 1 0 00-.894.553L7.382 4H4a1 1 0 000 2v10a2 2 0 002 2h8a2 2 0 002-2V6a1 1 0 100-2h-3.382l-.724-1.447A1 1 0 0011 2H9zM7 8a1 1 0 012 0v6a1 1 0 11-2 0V8zm5-1a1 1 0 00-1 1v6a1 1 0 102 0V8a1 1 0 00-1-1z" clipRule="evenodd" />
    </svg>
  )
}
