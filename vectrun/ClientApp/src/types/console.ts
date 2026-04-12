export type LogEvent = 'started' | 'output' | 'tool_call' | 'tool_result' | 'tool_log' | 'error'

export interface LogEntry {
  timestamp: string   // ISO 8601
  nodeId: string
  nodeType: string
  nodeName: string | null
  event: LogEvent
  message: string | null
}
