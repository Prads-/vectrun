import CodeMirror from '@uiw/react-codemirror'
import { StreamLanguage } from '@codemirror/language'
import { lua } from '@codemirror/legacy-modes/mode/lua'
import { useState } from 'react'

interface Props {
  value: string
  onChange: (value: string) => void
}

const extensions = [StreamLanguage.define(lua)]

export function LuaScriptEditor({ value, onChange }: Props) {
  const [height, setHeight] = useState(180)

  return (
    <div
      className="resize-y overflow-hidden rounded-lg border border-slate-200 focus-within:border-blue-400 focus-within:ring-2 focus-within:ring-blue-100"
      style={{ height, minHeight: 120 }}
      onMouseUp={e => {
        const h = (e.currentTarget as HTMLElement).getBoundingClientRect().height
        if (h !== height) setHeight(h)
      }}
    >
      <CodeMirror
        value={value}
        onChange={onChange}
        extensions={extensions}
        height="100%"
        basicSetup={{ foldGutter: false, autocompletion: false }}
        className="h-full text-sm"
      />
    </div>
  )
}
