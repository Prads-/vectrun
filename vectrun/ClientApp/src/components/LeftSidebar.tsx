import type { AnyFlowNode } from '../lib/layoutGraph'
import type { AnyNodeData } from '../types/pipeline'
import type { ModelConfig, ToolConfig, AgentConfig } from '../types/workspace'
import { NodesPanel } from './panels/NodesPanel'
import { ModelsPanel } from './panels/ModelsPanel'
import { AgentsPanel } from './panels/AgentsPanel'
import { ToolsPanel } from './panels/ToolsPanel'
import { RunPanel } from './RunPanel'

export type SidebarSection = 'nodes' | 'models' | 'agents' | 'tools' | 'run' | null

const SECTIONS = [
  { id: 'nodes' as const,  label: 'Nodes',  icon: <NodesIcon />,  accent: 'text-sky-400',    activeBg: 'bg-sky-500/15' },
  { id: 'models' as const, label: 'Models', icon: <ModelsIcon />, accent: 'text-purple-400', activeBg: 'bg-purple-500/15' },
  { id: 'agents' as const, label: 'Agents', icon: <AgentsIcon />, accent: 'text-blue-400',   activeBg: 'bg-blue-500/15' },
  { id: 'tools' as const,  label: 'Tools',  icon: <ToolsIcon />,  accent: 'text-emerald-400',activeBg: 'bg-emerald-500/15' },
]

const SECTION_LABELS: Record<string, string> = {
  nodes:  'Nodes',
  models: 'Models',
  agents: 'Agents',
  tools:  'Tools',
  run:    'Run',
}

interface Props {
  activeSection: SidebarSection
  onSectionChange: (s: SidebarSection) => void
  // nodes
  nodes: AnyFlowNode[]
  selectedNodeId: string | null
  editingNodeId: string | null
  pipelineName: string
  startNodeId: string
  onSelectNode: (id: string) => void
  onEditNode: (id: string | null) => void
  onDeleteNode: (id: string) => void
  onNodeDataChange: (id: string, data: AnyNodeData) => void
  onPipelineMetaChange: (name: string, startNodeId: string) => void
  onAddNode: (drag: import('../lib/dragNode').NodeDragData) => void
  // models
  models: ModelConfig[]
  onModelsChange: (m: ModelConfig[]) => void
  // tools
  tools: ToolConfig[]
  onToolsChange: (t: ToolConfig[]) => void
  // agents
  agents: AgentConfig[]
  onAgentsChange: (a: AgentConfig[]) => void
  // run / save
  directory: string
  isRunning: boolean
  onRun: (input: string) => void
  saveStatus: 'idle' | 'saving' | 'saved' | 'error'
  saveError: string | null
  onSave: () => void
}

export function LeftSidebar({
  activeSection, onSectionChange,
  nodes, selectedNodeId, editingNodeId, pipelineName, startNodeId,
  onSelectNode, onEditNode, onDeleteNode, onNodeDataChange, onPipelineMetaChange, onAddNode,
  models, onModelsChange,
  tools, onToolsChange,
  agents, onAgentsChange,
  directory, isRunning, onRun, saveStatus, saveError, onSave,
}: Props) {
  function toggle(s: SidebarSection) {
    onSectionChange(activeSection === s ? null : s)
  }

  return (
    <div className="flex h-full shrink-0">
      {/* Icon bar */}
      <div className="flex w-[52px] shrink-0 flex-col items-center border-r border-slate-800 bg-slate-950 py-3">
        {/* Section icons */}
        <div className="flex flex-col items-center gap-1 flex-1">
          {SECTIONS.map(s => {
            const isActive = activeSection === s.id
            return (
              <button
                key={s.id}
                title={s.label}
                onClick={() => toggle(s.id)}
                className={`relative flex h-9 w-9 items-center justify-center rounded-lg transition-all duration-150 ${
                  isActive
                    ? `${s.activeBg} ${s.accent}`
                    : 'text-slate-500 hover:bg-slate-800 hover:text-slate-300'
                }`}
              >
                {isActive && (
                  <span className={`absolute -left-[1px] top-1/2 -translate-y-1/2 h-5 w-0.5 rounded-r ${s.accent.replace('text-', 'bg-')}`} />
                )}
                {s.icon}
              </button>
            )
          })}
        </div>

        {/* Bottom actions */}
        <div className="flex flex-col items-center gap-1 border-t border-slate-800 pt-3 mt-1">
          {/* Run */}
          <button
            title="Run pipeline"
            onClick={() => toggle('run')}
            className={`relative flex h-9 w-9 items-center justify-center rounded-lg transition-all duration-150 ${
              activeSection === 'run'
                ? 'bg-rose-500/15 text-rose-400'
                : 'text-slate-500 hover:bg-slate-800 hover:text-slate-300'
            }`}
          >
            {activeSection === 'run' && (
              <span className="absolute -left-[1px] top-1/2 -translate-y-1/2 h-5 w-0.5 rounded-r bg-rose-400" />
            )}
            <PlayIcon />
          </button>

          {/* Save */}
          <button
            title={saveStatus === 'saving' ? 'Saving…' : saveStatus === 'saved' ? 'Saved!' : saveStatus === 'error' ? 'Save failed' : 'Save workspace'}
            onClick={onSave}
            disabled={saveStatus === 'saving'}
            className={`relative flex h-9 w-9 items-center justify-center rounded-lg transition-all duration-150 disabled:cursor-not-allowed ${
              saveStatus === 'saved'
                ? 'bg-emerald-500/15 text-emerald-400'
                : saveStatus === 'error'
                ? 'bg-red-500/15 text-red-400'
                : 'text-slate-500 hover:bg-slate-800 hover:text-slate-300'
            }`}
          >
            {saveStatus === 'saving'
              ? <SpinnerIcon />
              : saveStatus === 'saved'
              ? <CheckIcon />
              : <SaveIcon />
            }
            {saveStatus === 'error' && (
              <span className="absolute right-1 top-1 h-1.5 w-1.5 rounded-full bg-red-500" />
            )}
          </button>
        </div>
      </div>

      {/* Content panel */}
      {activeSection && (
        <div className="flex w-72 shrink-0 flex-col border-r border-slate-200 bg-white overflow-hidden panel-enter">
          {/* Panel header */}
          <div className="flex items-center justify-between px-4 py-3 border-b border-slate-100 shrink-0">
            <span className="text-[10px] font-bold uppercase tracking-widest text-slate-400">
              {SECTION_LABELS[activeSection]}
            </span>
            <button
              onClick={() => onSectionChange(null)}
              className="rounded-md p-0.5 text-slate-400 hover:bg-slate-100 hover:text-slate-600 transition"
              title="Close panel"
            >
              <XIcon />
            </button>
          </div>

          {/* Panel content */}
          <div className="flex-1 overflow-hidden">
            {activeSection === 'nodes' && (
              <NodesPanel
                nodes={nodes}
                selectedNodeId={selectedNodeId}
                editingNodeId={editingNodeId}
                agents={agents}
                pipelineName={pipelineName}
                startNodeId={startNodeId}
                onSelectNode={onSelectNode}
                onEditNode={onEditNode}
                onDeleteNode={onDeleteNode}
                onNodeDataChange={onNodeDataChange}
                onPipelineMetaChange={onPipelineMetaChange}
                onAddNode={onAddNode}
              />
            )}
            {activeSection === 'models' && (
              <ModelsPanel models={models} onChange={onModelsChange} />
            )}
            {activeSection === 'agents' && (
              <AgentsPanel
                agents={agents}
                models={models}
                tools={tools}
                onChange={onAgentsChange}
                onAddToCanvas={agentId => onAddNode({ nodeType: 'agent', agentId })}
              />
            )}
            {activeSection === 'tools' && (
              <ToolsPanel tools={tools} onChange={onToolsChange} />
            )}
            {activeSection === 'run' && (
              <div className="p-4">
                {saveError && (
                  <div className="mb-3 rounded-lg bg-red-50 border border-red-100 px-3 py-2 text-xs text-red-700">
                    Save error: {saveError}
                  </div>
                )}
                <RunPanel isRunning={isRunning} onRun={onRun} />
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  )
}

// ─── Icons ───────────────────────────────────────────────────────────────────

function NodesIcon() {
  return (
    <svg className="h-[18px] w-[18px]" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <rect x="3" y="3" width="7" height="7" rx="1" />
      <rect x="14" y="3" width="7" height="7" rx="1" />
      <rect x="3" y="14" width="7" height="7" rx="1" />
      <rect x="14" y="14" width="7" height="7" rx="1" />
    </svg>
  )
}

function ModelsIcon() {
  return (
    <svg className="h-[18px] w-[18px]" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <ellipse cx="12" cy="5" rx="9" ry="3" />
      <path d="M3 5v4c0 1.657 4.03 3 9 3s9-1.343 9-3V5" />
      <path d="M3 9v4c0 1.657 4.03 3 9 3s9-1.343 9-3V9" />
      <path d="M3 13v4c0 1.657 4.03 3 9 3s9-1.343 9-3v-4" />
    </svg>
  )
}

function AgentsIcon() {
  return (
    <svg className="h-[18px] w-[18px]" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <circle cx="12" cy="8" r="4" />
      <path d="M4 20c0-4 3.582-7 8-7s8 3 8 7" />
      <path d="M18.5 3.5 20 2" />
      <path d="M20 6h2" />
      <path d="M18.5 8.5 20 10" />
    </svg>
  )
}

function ToolsIcon() {
  return (
    <svg className="h-[18px] w-[18px]" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z" />
    </svg>
  )
}

function PlayIcon() {
  return (
    <svg className="h-[18px] w-[18px]" viewBox="0 0 24 24" fill="currentColor">
      <path d="M8 6.82v10.36c0 .79.87 1.27 1.54.84l8.14-5.18a1 1 0 0 0 0-1.69L9.54 5.98A.998.998 0 0 0 8 6.82z" />
    </svg>
  )
}

function SaveIcon() {
  return (
    <svg className="h-[18px] w-[18px]" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z" />
      <polyline points="17 21 17 13 7 13 7 21" />
      <polyline points="7 3 7 8 15 8" />
    </svg>
  )
}

function CheckIcon() {
  return (
    <svg className="h-[18px] w-[18px]" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
      <polyline points="20 6 9 17 4 12" />
    </svg>
  )
}

function SpinnerIcon() {
  return (
    <svg className="h-[18px] w-[18px] animate-spin" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
      <path d="M21 12a9 9 0 1 1-6.219-8.56" />
    </svg>
  )
}

function XIcon() {
  return (
    <svg className="h-4 w-4" viewBox="0 0 20 20" fill="currentColor">
      <path fillRule="evenodd" d="M4.293 4.293a1 1 0 011.414 0L10 8.586l4.293-4.293a1 1 0 111.414 1.414L11.414 10l4.293 4.293a1 1 0 01-1.414 1.414L10 11.414l-4.293 4.293a1 1 0 01-1.414-1.414L8.586 10 4.293 5.707a1 1 0 010-1.414z" clipRule="evenodd" />
    </svg>
  )
}
