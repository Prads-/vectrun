import type { AnyFlowNode } from '../lib/layoutGraph'
import type { AnyNodeData } from '../types/pipeline'
import type { ModelConfig, ToolConfig, AgentConfig } from '../types/workspace'
import { NodePropertiesPanel } from './panels/NodePropertiesPanel'
import { ModelsPanel } from './panels/ModelsPanel'
import { ToolsPanel } from './panels/ToolsPanel'
import { AgentsPanel } from './panels/AgentsPanel'
import { RunPanel } from './RunPanel'

type Tab = 'properties' | 'models' | 'tools' | 'agents' | 'run'

interface Props {
  selectedNodeId: string | null
  nodes: AnyFlowNode[]
  pipelineName: string
  startNodeId: string
  models: ModelConfig[]
  tools: ToolConfig[]
  agents: AgentConfig[]
  directory: string
  saving: boolean
  saveError: string | null
  onPipelineMetaChange: (name: string, startNodeId: string) => void
  onNodeDataChange: (nodeId: string, data: AnyNodeData) => void
  onDeleteNode: (nodeId: string) => void
  onModelsChange: (models: ModelConfig[]) => void
  onToolsChange: (tools: ToolConfig[]) => void
  onAgentsChange: (agents: AgentConfig[]) => void
  onSave: () => void
  activeTab: Tab
  onTabChange: (tab: Tab) => void
}

const TABS: { id: Tab; label: string }[] = [
  { id: 'properties', label: 'Props' },
  { id: 'models', label: 'Models' },
  { id: 'tools', label: 'Tools' },
  { id: 'agents', label: 'Agents' },
  { id: 'run', label: 'Run' },
]

export function RightPanel({
  selectedNodeId,
  nodes,
  pipelineName,
  startNodeId,
  models,
  tools,
  agents,
  directory,
  saving,
  saveError,
  onPipelineMetaChange,
  onNodeDataChange,
  onDeleteNode,
  onModelsChange,
  onToolsChange,
  onAgentsChange,
  onSave,
  activeTab,
  onTabChange,
}: Props) {
  return (
    <div className="flex w-72 shrink-0 flex-col border-l border-slate-200 bg-white">
      {/* Save button */}
      <div className="flex items-center gap-2 border-b border-slate-100 px-3 py-2">
        <button
          onClick={onSave}
          disabled={saving}
          className="w-full rounded-lg bg-slate-800 px-4 py-1.5 text-sm font-semibold text-white transition hover:bg-slate-700 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {saving ? 'Saving…' : 'Save'}
        </button>
      </div>

      {saveError && (
        <div className="border-b border-red-100 bg-red-50 px-3 py-2 text-xs text-red-700">
          {saveError}
        </div>
      )}

      {/* Tabs */}
      <div className="flex border-b border-slate-100">
        {TABS.map(tab => (
          <button
            key={tab.id}
            onClick={() => onTabChange(tab.id)}
            className={`flex-1 py-2 text-xs font-semibold transition ${
              activeTab === tab.id
                ? 'border-b-2 border-blue-500 text-blue-600'
                : 'text-slate-500 hover:text-slate-700'
            }`}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {/* Tab content */}
      <div className="flex-1 overflow-y-auto">
        {activeTab === 'properties' && (
          <NodePropertiesPanel
            selectedNodeId={selectedNodeId}
            nodes={nodes}
            pipelineName={pipelineName}
            startNodeId={startNodeId}
            agents={agents}
            onPipelineMetaChange={onPipelineMetaChange}
            onNodeDataChange={onNodeDataChange}
            onDeleteNode={onDeleteNode}
          />
        )}
        {activeTab === 'models' && (
          <ModelsPanel models={models} onChange={onModelsChange} />
        )}
        {activeTab === 'tools' && (
          <ToolsPanel tools={tools} onChange={onToolsChange} />
        )}
        {activeTab === 'agents' && (
          <AgentsPanel agents={agents} models={models} tools={tools} onChange={onAgentsChange} />
        )}
        {activeTab === 'run' && (
          <div className="p-4">
            <RunPanel directory={directory} />
          </div>
        )}
      </div>
    </div>
  )
}

export type { Tab }
