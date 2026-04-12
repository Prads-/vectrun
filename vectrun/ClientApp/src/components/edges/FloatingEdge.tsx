import { BaseEdge, EdgeLabelRenderer, getBezierPath, useInternalNode, Position } from '@xyflow/react'
import type { EdgeProps } from '@xyflow/react'

// Find where the line from nodeCenter to otherPoint intersects the node's rectangular border,
// and return which edge (Position) was hit so getBezierPath can curve correctly.
function getBorderIntersection(
  nodeX: number, nodeY: number, nodeW: number, nodeH: number,
  otherX: number, otherY: number,
): { x: number; y: number; position: Position } {
  const cx = nodeX + nodeW / 2
  const cy = nodeY + nodeH / 2
  const hw = nodeW / 2
  const hh = nodeH / 2
  const dx = otherX - cx
  const dy = otherY - cy

  if (dx === 0 && dy === 0) return { x: cx, y: cy, position: Position.Bottom }

  // Which border does the ray hit first?
  const hitLR = Math.abs(dx) * hh > Math.abs(dy) * hw
  const scale = hitLR ? hw / Math.abs(dx) : hh / Math.abs(dy)
  const position = hitLR
    ? (dx > 0 ? Position.Right : Position.Left)
    : (dy > 0 ? Position.Bottom : Position.Top)

  return { x: cx + dx * scale, y: cy + dy * scale, position }
}

export function FloatingEdge({
  id,
  source,
  target,
  markerEnd,
  style,
  label,
  labelStyle,
  labelBgStyle,
  labelBgPadding,
  labelBgBorderRadius,
}: EdgeProps) {
  const sourceNode = useInternalNode(source)
  const targetNode = useInternalNode(target)

  if (!sourceNode || !targetNode) return null

  const sw = sourceNode.measured?.width  ?? 220
  const sh = sourceNode.measured?.height ?? 90
  const tw = targetNode.measured?.width  ?? 220
  const th = targetNode.measured?.height ?? 90

  const sx = sourceNode.internals.positionAbsolute.x
  const sy = sourceNode.internals.positionAbsolute.y
  const tx = targetNode.internals.positionAbsolute.x
  const ty = targetNode.internals.positionAbsolute.y

  const targetCx = tx + tw / 2
  const targetCy = ty + th / 2
  const sourceCx = sx + sw / 2
  const sourceCy = sy + sh / 2

  const sp = getBorderIntersection(sx, sy, sw, sh, targetCx, targetCy)
  const tp = getBorderIntersection(tx, ty, tw, th, sourceCx, sourceCy)

  const [edgePath, labelX, labelY] = getBezierPath({
    sourceX: sp.x,
    sourceY: sp.y,
    sourcePosition: sp.position,
    targetX: tp.x,
    targetY: tp.y,
    targetPosition: tp.position,
  })

  return (
    <>
      <BaseEdge id={id} path={edgePath} markerEnd={markerEnd} style={style} />
      {label && (
        <EdgeLabelRenderer>
          <div
            className="nodrag nopan"
            style={{
              position: 'absolute',
              transform: `translate(-50%,-50%) translate(${labelX}px,${labelY}px)`,
              pointerEvents: 'all',
              padding: labelBgPadding ? `${labelBgPadding[1]}px ${labelBgPadding[0]}px` : '2px 4px',
              borderRadius: labelBgBorderRadius ?? 4,
              ...(labelBgStyle as React.CSSProperties),
            }}
          >
            <span style={labelStyle as React.CSSProperties}>{label as string}</span>
          </div>
        </EdgeLabelRenderer>
      )}
    </>
  )
}
