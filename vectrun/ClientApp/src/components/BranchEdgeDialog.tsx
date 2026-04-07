interface Props {
  onSelect: (label: 'true' | 'false') => void
  onCancel: () => void
}

export function BranchEdgeDialog({ onSelect, onCancel }: Props) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
      <div className="w-full max-w-sm rounded-2xl bg-white p-6 shadow-2xl">
        <h2 className="mb-1 text-base font-bold text-slate-800">Branch connection</h2>
        <p className="mb-6 text-sm text-slate-500">Is this a true or false branch?</p>
        <div className="flex justify-end gap-3">
          <button
            onClick={onCancel}
            className="rounded-lg border border-slate-200 px-4 py-2 text-sm font-medium text-slate-600 hover:bg-slate-50"
          >
            Cancel
          </button>
          <button
            onClick={() => onSelect('false')}
            className="rounded-lg bg-red-500 px-4 py-2 text-sm font-semibold text-white hover:bg-red-600"
          >
            False
          </button>
          <button
            onClick={() => onSelect('true')}
            className="rounded-lg bg-emerald-500 px-4 py-2 text-sm font-semibold text-white hover:bg-emerald-600"
          >
            True
          </button>
        </div>
      </div>
    </div>
  )
}
