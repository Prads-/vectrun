export const DRAG_TYPE = 'text/plain'

export interface NodeDragData {
  nodeType: 'agent' | 'branch' | 'logic' | 'wait'
  agentId?: string
}
