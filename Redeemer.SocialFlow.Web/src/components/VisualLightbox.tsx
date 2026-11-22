import { useEffect, useRef, useState } from 'react'
import { Modal, ErrorNotice } from './shared'
import { visualsApi, workspaceVisualsApi, type Visual } from '../api/visuals'

export function VisualLightbox({ postId, visuals, initialId, workspace, close }: {
  postId: string; visuals: Visual[]; initialId: string; workspace: boolean; close: () => void
}) {
  const [index, setIndex] = useState(Math.max(0, visuals.findIndex(v => v.id === initialId)))
  const visual = visuals[Math.min(index, visuals.length - 1)]
  const [url, setUrl] = useState('')
  const [error, setError] = useState<unknown>()
  const [zoom, setZoom] = useState(1)
  const viewport = useRef<HTMLDivElement>(null)
  const drag = useRef<{ x: number; y: number; left: number; top: number } | null>(null)
  useEffect(() => {
    const controller = new AbortController(); let objectUrl: string | undefined
    setUrl(''); setError(undefined); setZoom(1); drag.current = null
    if (viewport.current) { viewport.current.scrollLeft = 0; viewport.current.scrollTop = 0 }
    void (workspace ? workspaceVisualsApi : visualsApi).image(postId, visual.id, controller.signal).then(blob => {
      if (!controller.signal.aborted) { objectUrl = URL.createObjectURL(blob); setUrl(objectUrl) }
    }).catch(e => { if (!controller.signal.aborted) setError(e) })
    return () => { controller.abort(); if (objectUrl) URL.revokeObjectURL(objectUrl) }
  }, [postId, visual.id, workspace])
  return <Modal title="Aperçu du visuel" close={close} wide>
    <div className="lightbox-toolbar">
      <button type="button" className="button" disabled={index === 0} onClick={() => setIndex(i => i - 1)}>Précédent</button>
      <span aria-live="polite">{index + 1} / {visuals.length} · {visual.selected ? 'Sélectionné et validé' : 'Candidat non sélectionné'}</span>
      <button type="button" className="button" disabled={index >= visuals.length - 1} onClick={() => setIndex(i => i + 1)}>Suivant</button>
      <button type="button" className="button" disabled={!url || zoom <= 1} onClick={() => setZoom(z => Math.max(1, z - .5))}>Réduire</button>
      <output aria-label="Zoom">{Math.round(zoom * 100)} %</output>
      <button type="button" className="button" disabled={!url || zoom >= 4} onClick={() => setZoom(z => Math.min(4, z + .5))}>Zoomer</button>
      <button type="button" className="button" disabled={!url} onClick={() => {
        setZoom(1); if (viewport.current) { viewport.current.scrollLeft = 0; viewport.current.scrollTop = 0 }
      }}>Image entière</button>
      {url && <a className="button" href={url} download={`visuel-${visual.id}.${visual.contentType === 'image/jpeg' ? 'jpg' : 'png'}`}>Télécharger</a>}
    </div>
    <p>{visual.width} × {visual.height} · {new Date(visual.createdAt).toLocaleString()} · Déplacez l’image zoomée par glissement ou avec les flèches dans la zone d’image.</p>
    {error != null ? <ErrorNotice error={error} /> : !url ? <p role="status">Chargement de l’image…</p> :
      <div className="lightbox-viewport" ref={viewport} tabIndex={0} role="region" aria-label="Image agrandie, zone de déplacement"
        onKeyDown={e => { const shift = 80; const node = e.currentTarget
          if (['ArrowLeft','ArrowRight','ArrowUp','ArrowDown'].includes(e.key)) {
            e.preventDefault(); node.scrollLeft += e.key === 'ArrowRight' ? shift : e.key === 'ArrowLeft' ? -shift : 0
            node.scrollTop += e.key === 'ArrowDown' ? shift : e.key === 'ArrowUp' ? -shift : 0
          }
        }}
        onPointerDown={e => { if (zoom <= 1) return; e.currentTarget.setPointerCapture(e.pointerId); drag.current = { x: e.clientX, y: e.clientY, left: e.currentTarget.scrollLeft, top: e.currentTarget.scrollTop } }}
        onPointerMove={e => { if (drag.current) { e.currentTarget.scrollLeft = drag.current.left - e.clientX + drag.current.x; e.currentTarget.scrollTop = drag.current.top - e.clientY + drag.current.y } }}
        onPointerUp={() => { drag.current = null }} onPointerCancel={() => { drag.current = null }}>
        <div className="lightbox-image-stage" style={{ width: zoom * 100 + '%', height: zoom * 100 + '%' }}>
          <img src={url} alt={visual.selected ? 'Visuel sélectionné en grand format' : 'Candidat en grand format'} draggable={false} />
        </div>
      </div>}
    <p className="field-note">Cet aperçu ne sélectionne et ne publie aucun visuel.</p>
  </Modal>
}
