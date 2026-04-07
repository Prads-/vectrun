import { useState, useCallback, useRef } from 'react'
import { ReactFlowProvider, useNodesState, useEdgesState, useReactFlow, addEdge } from '@xyflow/react'
import type { Connection, Edge } from '@xyflow/react'
import type { Workspace, ModelConfig, ToolConfig, AgentConfig } from '../types/workspace'
import type { AnyNodeData, AgentNodeData, BranchNodeData, LogicNodeData, WaitNodeData } from '../types/pipeline'
import type { AnyFlowNode } from '../lib/layoutGraph'
import type { LogEntry } from '../types/console'
import { buildFlowGraph } from '../lib/layoutGraph'
import { toPipeline } from '../lib/pipelineConvert'
import { saveWorkspace } from '../api/workspace'
import { streamRun, ConflictError } from '../api/pipeline'
import { PipelineCanvas } from './PipelineCanvas'
import { LeftSidebar } from './LeftSidebar'
import type { SidebarSection } from './LeftSidebar'
import { BranchEdgeDialog } from './BranchEdgeDialog'
import { ConsolePanel } from './ConsolePanel'
import { DRAG_TYPE } from '../lib/dragNode'
import type { NodeDragData } from '../lib/dragNode'

interface Props {
  workspace: Workspace
  directory: string
  onSaved: (workspace: Workspace) => void
}

export function WorkspaceEditor(props: Props) {
  return (
    <ReactFlowProvider>
      <WorkspaceEditorInner {...props} />
    </ReactFlowProvider>
  )
}

function WorkspaceEditorInner({ workspace, directory, onSaved }: Props) {
  const { screenToFlowPosition } = useReactFlow()
  const { nodes: initNodes, edges: initEdges } = buildFlowGraph(workspace.pipeline)

  const [nodes, setNodes, onNodesChange] = useNodesState<AnyFlowNode>(initNodes)
  const [edges, setEdges, onEdgesChange] = useEdgesState(initEdges)
  const [models, setModels] = useState<ModelConfig[]>(workspace.models)
  const [tools, setTools] = useState<ToolConfig[]>(workspace.tools)
  const [agents, setAgents] = useState<AgentConfig[]>(workspace.agents)
  const [pipelineName, setPipelineName] = useState(workspace.pipeline.pipelineName)
  const [startNodeId, setStartNodeId] = useState(workspace.pipeline.startNodeId)

  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null)
  const [editingNodeId, setEditingNodeId] = useState<string | null>(null)
  const [activeSection, setActiveSection] = useState<SidebarSection>('nodes')

  const [pendingConnection, setPendingConnection] = useState<Connection | null>(null)
  const [saveStatus, setSaveStatus] = useState<'idle' | 'saving' | 'saved' | 'error'>('idle')
  const [saveError, setSaveError] = useState<string | null>(null)

  // Console / run state
  const [logs, setLogs] = useState<LogEntry[]>([])
  const [isRunning, setIsRunning] = useState(false)
  const [consoleOpen, setConsoleOpen] = useState(false)

  // Ref to the canvas wrapper — used to compute center position for click-to-add
  const canvasWrapRef = useRef<HTMLDivElement>(null)

  const onConnect = useCallback(
    (connection: Connection) => {
      const sourceNode = nodes.find(n => n.id === connection.source)
      if (sourceNode?.type === 'branch') {
        setPendingConnection(connection)
      } else {
        setEdges(eds =>
          addEdge(
            { ...connection, style: { stroke: '#94a3b8', strokeWidth: 1.5 }, animated: false },
            eds
          )
        )
      }
    },
    [nodes, setEdges]
  )

  function handleBranchSelect(label: 'true' | 'false') {
    if (!pendingConnection) return
    setEdges(eds =>
      addEdge(
        {
          ...pendingConnection,
          label,
          style: { stroke: '#94a3b8', strokeWidth: 1.5 },
          labelStyle: { fontSize: 10, fill: '#64748b', fontWeight: 600 },
          labelBgStyle: { fill: '#f8fafc', fillOpacity: 0.95 },
          labelBgPadding: [4, 2] as [number, number],
          labelBgBorderRadius: 4,
        },
        eds
      )
    )
    setPendingConnection(null)
  }

  function spawnNode(drag: NodeDragData, position: { x: number; y: number }) {
    const id = `node-${Date.now()}`
    let newNode: AnyFlowNode

    switch (drag.nodeType) {
      case 'agent': {
        const data: AgentNodeData = { agentId: drag.agentId ?? '', nextNodeIds: [] }
        newNode = { id, type: 'agent', position, data } as AnyFlowNode
        break
      }
      case 'branch': {
        const data: BranchNodeData = { expectedOutput: '', trueNodeIds: [], falseNodeIds: [] }
        newNode = { id, type: 'branch', position, data } as AnyFlowNode
        break
      }
      case 'logic': {
        const data: LogicNodeData = { logicType: 'process', processPath: '', nextNodeIds: [] }
        newNode = { id, type: 'logic', position, data } as AnyFlowNode
        break
      }
      case 'wait': {
        const data: WaitNodeData = { durationMs: 1000, nextNodeIds: [] }
        newNode = { id, type: 'wait', position, data } as AnyFlowNode
        break
      }
    }

    setNodes(nds => [...nds, newNode])
    setSelectedNodeId(id)
    setEditingNodeId(id)
    setActiveSection('nodes')
  }

  // Used by palette click-to-add: places node near canvas center with small jitter
  function addNodeAtCenter(drag: NodeDragData) {
    const el = canvasWrapRef.current
    const rect = el
      ? el.getBoundingClientRect()
      : { left: 200, top: 100, width: 800, height: 600 }
    const jitter = () => (Math.random() - 0.5) * 120
    const position = screenToFlowPosition({
      x: rect.left + rect.width / 2 + jitter(),
      y: rect.top + rect.height / 2 + jitter(),
    })
    spawnNode(drag, position)
  }

  // ReactFlow native drop handler — fires when something is dropped ON the canvas
  function handleCanvasDrop(e: React.DragEvent) {
    e.preventDefault()
    const raw = e.dataTransfer.getData(DRAG_TYPE)
    if (!raw) return
    try {
      const drag: NodeDragData = JSON.parse(raw)
      const position = screenToFlowPosition({ x: e.clientX, y: e.clientY })
      spawnNode(drag, position)
    } catch { /* ignore */ }
  }

  function handleCanvasDragOver(e: React.DragEvent) {
    e.preventDefault()
    e.dataTransfer.dropEffect = 'move'
  }

  // Canvas node click — select + open properties
  function handleCanvasNodeClick(nodeId: string) {
    setSelectedNodeId(nodeId)
    setEditingNodeId(nodeId)
    setActiveSection('nodes')
  }

  // List node click — highlight on canvas
  function handleSelectNode(nodeId: string) {
    setSelectedNodeId(nodeId)
    setNodes(nds => nds.map(n => ({ ...n, selected: n.id === nodeId })))
  }

  function handleEditNode(nodeId: string | null) {
    setEditingNodeId(nodeId)
    if (nodeId) {
      setSelectedNodeId(nodeId)
      setNodes(nds => nds.map(n => ({ ...n, selected: n.id === nodeId })))
    }
  }

  function handlePaneClick() {
    setSelectedNodeId(null)
    setEditingNodeId(null)
    setNodes(nds => nds.map(n => ({ ...n, selected: false })))
  }

  function handlePipelineMetaChange(name: string, newStartNodeId: string) {
    setPipelineName(name)
    setStartNodeId(newStartNodeId)
  }

  function handleNodeDataChange(nodeId: string, data: AnyNodeData) {
    setNodes(nds =>
      nds.map(n => (n.id !== nodeId ? n : ({ ...n, data } as AnyFlowNode)))
    )
  }

  function handleDeleteNode(nodeId: string) {
    setNodes(nds => nds.filter(n => n.id !== nodeId))
    setEdges(eds => eds.filter((e: Edge) => e.source !== nodeId && e.target !== nodeId))
    if (selectedNodeId === nodeId) setSelectedNodeId(null)
    if (editingNodeId === nodeId) setEditingNodeId(null)
  }

  async function handleSave() {
    setSaveStatus('saving')
    setSaveError(null)
    try {
      const pipeline = toPipeline(nodes, edges, { pipelineName, startNodeId })
      await saveWorkspace(directory, pipeline, models, tools, agents)
      onSaved({ pipeline, models, tools, agents })
      setSaveStatus('saved')
      setTimeout(() => setSaveStatus('idle'), 2000)
    } catch (err) {
      setSaveError(String(err))
      setSaveStatus('error')
    }
  }

  async function handleRun(input: string) {
    setLogs([])
    setIsRunning(true)
    setConsoleOpen(true)
    try {
      for await (const entry of streamRun(directory, input || undefined)) {
        setLogs(prev => [...prev, entry])
      }
    } catch (err) {
      const msg = err instanceof ConflictError ? 'A run is already in progress.' : String(err)
      setLogs(prev => [...prev, {
        timestamp: new Date().toISOString(),
        nodeId: 'pipeline',
        nodeType: 'system',
        nodeName: null,
        event: 'error',
        message: msg,
      }])
    } finally {
      setIsRunning(false)
    }
  }

  return (
    <div className="flex h-full flex-1 min-w-0">
      <LeftSidebar
        activeSection={activeSection}
        onSectionChange={setActiveSection}
        nodes={nodes}
        selectedNodeId={selectedNodeId}
        editingNodeId={editingNodeId}
        pipelineName={pipelineName}
        startNodeId={startNodeId}
        onSelectNode={handleSelectNode}
        onEditNode={handleEditNode}
        onDeleteNode={handleDeleteNode}
        onNodeDataChange={handleNodeDataChange}
        onPipelineMetaChange={handlePipelineMetaChange}
        onAddNode={addNodeAtCenter}
        models={models}
        onModelsChange={setModels}
        tools={tools}
        onToolsChange={setTools}
        agents={agents}
        onAgentsChange={setAgents}
        directory={directory}
        isRunning={isRunning}
        onRun={handleRun}
        saveStatus={saveStatus}
        saveError={saveError}
        onSave={handleSave}
      />

      <div className="flex flex-col flex-1 min-w-0 overflow-hidden">
        <div ref={canvasWrapRef} className="flex-1 relative min-w-0 overflow-hidden">
          <PipelineCanvas
            nodes={nodes}
            edges={edges}
            onNodesChange={onNodesChange}
            onEdgesChange={onEdgesChange}
            onConnect={onConnect}
            onNodeClick={handleCanvasNodeClick}
            onPaneClick={handlePaneClick}
            onDrop={handleCanvasDrop}
            onDragOver={handleCanvasDragOver}
          />
        </div>

        <ConsolePanel
          logs={logs}
          isRunning={isRunning}
          open={consoleOpen}
          onToggle={() => setConsoleOpen(o => !o)}
          onClear={() => setLogs([])}
        />
      </div>

      {pendingConnection && (
        <BranchEdgeDialog
          onSelect={handleBranchSelect}
          onCancel={() => setPendingConnection(null)}
        />
      )}
    </div>
  )
}
