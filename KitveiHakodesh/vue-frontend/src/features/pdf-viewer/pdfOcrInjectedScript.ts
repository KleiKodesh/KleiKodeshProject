// This script is injected into the PDF.js iframe via contentWindow eval.
// It runs entirely inside the iframe's document context so all coordinates
// are in the same space — no cross-frame translation needed.
// It communicates back to the parent via postMessage.
//
// Coordinate space: everything (mouse points, span/canvas positions, the
// selection div) lives in #viewerContainer PADDING-BOX content coordinates.
// getBoundingClientRect() gives the border-box origin, but the viewer is RTL
// so its vertical scrollbar sits on the LEFT — clientLeft includes that
// scrollbar width, and skipping it drew the selection rect shifted right of
// the cursor. Always subtract clientLeft/clientTop after the rect origin.

export const PDF_OCR_INJECTED_SCRIPT = /* js */ `
(function() {
  if (window.__kitveiHakodeshOcrTool) return; // already injected

  const tool = {
    isActive: false,
    isDrawing: false,
    startX: 0,
    startY: 0,
    selectionDiv: null,
    crosshairDiv: null,
    langFile: 'heb',
    forceOcr: false,
  };

  window.__kitveiHakodeshOcrTool = tool;

  // ── Styles ──────────────────────────────────────────────────────────────

  const style = document.createElement('style');
  style.textContent = \`
    #viewerContainer.kitvei-hakodesh-ocr-mode { cursor: crosshair !important; }
    #viewerContainer.kitvei-hakodesh-ocr-mode * { cursor: crosshair !important; }
    #viewerContainer.kitvei-hakodesh-ocr-drawing { cursor: default !important; }
    #viewerContainer.kitvei-hakodesh-ocr-drawing * { cursor: default !important; }
    .kitvei-hakodesh-ocr-rect {
      position: absolute;
      border: 2px dashed #0078d4;
      background: rgba(0,120,212,0.12);
      pointer-events: none;
      z-index: 9000;
      box-shadow: 0 0 0 1px rgba(0,120,212,0.3), inset 0 0 0 1px rgba(0,120,212,0.2);
      border-radius: 2px;
      transition: box-shadow 100ms ease;
      box-sizing: content-box;
    }
    .kitvei-hakodesh-ocr-rect.active {
      box-shadow: 0 0 8px rgba(0,120,212,0.4), inset 0 0 0 1px rgba(0,120,212,0.3);
    }
  \`;
  document.head.appendChild(style);

  // ── Coordinate space ─────────────────────────────────────────────────────

  // Origin of the container's padding box in viewport coordinates, which is
  // what an absolutely-positioned child's left/top are measured from.
  function paddingBoxOrigin(container) {
    const cr = container.getBoundingClientRect();
    return {
      x: cr.left + container.clientLeft - container.scrollLeft,
      y: cr.top  + container.clientTop  - container.scrollTop,
    };
  }

  // Viewport point → container content coordinates.
  function toContentCoords(container, clientX, clientY) {
    const origin = paddingBoxOrigin(container);
    return { x: clientX - origin.x, y: clientY - origin.y };
  }

  // ── Text layer hit test ──────────────────────────────────────────────────

  function extractText(rect) {
    const container = document.getElementById('viewerContainer');
    if (!container) return null;
    const origin = paddingBoxOrigin(container);
    const words = [];

    for (const span of container.querySelectorAll('.textLayer span')) {
      const text = span.textContent;
      if (!text || !text.trim()) continue;
      const sr = span.getBoundingClientRect();
      const left = sr.left - origin.x;
      const top = sr.top - origin.y;
      const cx = left + sr.width / 2;
      const cy = top + sr.height / 2;
      if (cx >= rect.left && cx <= rect.left + rect.width &&
          cy >= rect.top  && cy <= rect.top  + rect.height) {
        words.push(text);
      }
    }
    if (words.length === 0) return null;
    // Join words and clean up whitespace
    const text = words.join(' ').replace(/\s+/g, ' ').trim();
    return text.length > 0 ? text : null;
  }

  // ── Canvas capture ───────────────────────────────────────────────────────

  function captureCanvas(rect) {
    const container = document.getElementById('viewerContainer');
    if (!container) return null;
    const origin = paddingBoxOrigin(container);

    for (const canvas of container.querySelectorAll('.canvasWrapper canvas')) {
      const cr = canvas.getBoundingClientRect();
      const cl = cr.left - origin.x;
      const ct = cr.top  - origin.y;

      const intersects = !(
        rect.left + rect.width  < cl ||
        rect.left               > cl + cr.width ||
        rect.top  + rect.height < ct ||
        rect.top                > ct + cr.height
      );
      if (!intersects) continue;

      const rx = Math.max(0, rect.left - cl);
      const ry = Math.max(0, rect.top  - ct);
      const rw = Math.min(rect.width,  cr.width  - rx);
      const rh = Math.min(rect.height, cr.height - ry);
      const sx = canvas.width  / cr.width;
      const sy = canvas.height / cr.height;

      const out = document.createElement('canvas');
      out.width  = Math.round(rw * sx);
      out.height = Math.round(rh * sy);
      out.getContext('2d').drawImage(
        canvas,
        Math.round(rx * sx), Math.round(ry * sy),
        out.width, out.height,
        0, 0, out.width, out.height
      );
      return out.toDataURL('image/png');
    }
    return null;
  }

  // ── Mouse handlers ───────────────────────────────────────────────────────

  function onMouseDown(e) {
    if (!tool.isActive || e.button !== 0) return;
    e.preventDefault();
    tool.isDrawing = true;
    const container = document.getElementById('viewerContainer');
    const start = toContentCoords(container, e.clientX, e.clientY);
    tool.startX = start.x;
    tool.startY = start.y;

    tool.selectionDiv = document.createElement('div');
    tool.selectionDiv.className = 'kitvei-hakodesh-ocr-rect';
    tool.selectionDiv.style.left   = tool.startX + 'px';
    tool.selectionDiv.style.top    = tool.startY + 'px';
    tool.selectionDiv.style.width  = '0px';
    tool.selectionDiv.style.height = '0px';
    container.appendChild(tool.selectionDiv);

    // Switch to default cursor — the rect is anchored at the start point and
    // the cursor drags the opposite corner, in any direction.
    container.classList.add('kitvei-hakodesh-ocr-drawing');
  }

  function onMouseMove(e) {
    if (!tool.isDrawing || !tool.selectionDiv) return;
    const container = document.getElementById('viewerContainer');
    const current = toContentCoords(container, e.clientX, e.clientY);
    const currentX = current.x;
    const currentY = current.y;

    // Calculate rectangle from start point to current point (traditional drag)
    const width = Math.abs(currentX - tool.startX);
    const height = Math.abs(currentY - tool.startY);
    const left = Math.min(currentX, tool.startX);
    const top = Math.min(currentY, tool.startY);

    tool.selectionDiv.style.left   = left   + 'px';
    tool.selectionDiv.style.top    = top    + 'px';
    tool.selectionDiv.style.width  = width  + 'px';
    tool.selectionDiv.style.height = height + 'px';

    if (width > 5 && height > 5) {
      tool.selectionDiv.classList.add('active');
    } else {
      tool.selectionDiv.classList.remove('active');
    }
  }

  function onMouseUp(e) {
    if (!tool.isDrawing) return;
    tool.isDrawing = false;

    // Restore crosshair cursor
    const container = document.getElementById('viewerContainer');
    if (container) container.classList.remove('kitvei-hakodesh-ocr-drawing');

    const div = tool.selectionDiv;
    if (div) {
      const rect = {
        left:   parseFloat(div.style.left),
        top:    parseFloat(div.style.top),
        width:  parseFloat(div.style.width),
        height: parseFloat(div.style.height),
      };
      div.remove();
      tool.selectionDiv = null;

      if (rect.width > 5 && rect.height > 5) {
        processRect(rect);
      }
    }
    tool.deactivate();
  }

  // ── Process selection ────────────────────────────────────────────────────

  function processRect(rect) {
    // When forceOcr is set, skip the text layer entirely and go straight to canvas OCR.
    if (!tool.forceOcr) {
      const text = extractText(rect);
      if (text) {
        window.parent.postMessage({ type: 'kitvei-hakodesh-ocr-result', text, isOcr: false }, '*');
        return;
      }
    }
    // No text found (or forceOcr is active) — send canvas data to parent for Tesseract.
    const dataUrl = captureCanvas(rect);
    if (dataUrl) {
      window.parent.postMessage({ type: 'kitvei-hakodesh-ocr-canvas', dataUrl, langFile: tool.langFile }, '*');
    } else {
      window.parent.postMessage({ type: 'kitvei-hakodesh-ocr-result', text: '', isOcr: true }, '*');
    }
  }

  // ── Activate / deactivate ────────────────────────────────────────────────

  tool.activate = function(langFile, forceOcr) {
    tool.isActive = true;
    tool.langFile = langFile || 'heb';
    tool.forceOcr = !!forceOcr;
    const container = document.getElementById('viewerContainer');
    if (!container) return;
    container.classList.add('kitvei-hakodesh-ocr-mode');
    container.addEventListener('mousedown', onMouseDown);
    container.addEventListener('mousemove', onMouseMove);
    container.addEventListener('mouseup',   onMouseUp);
    container.style.userSelect = 'none';
  };

  tool.deactivate = function() {
    tool.isActive = false;
    const container = document.getElementById('viewerContainer');
    if (!container) return;
    container.classList.remove('kitvei-hakodesh-ocr-mode');
    container.classList.remove('kitvei-hakodesh-ocr-drawing');
    container.removeEventListener('mousedown', onMouseDown);
    container.removeEventListener('mousemove', onMouseMove);
    container.removeEventListener('mouseup',   onMouseUp);
    container.style.userSelect = '';
    if (tool.selectionDiv) { tool.selectionDiv.remove(); tool.selectionDiv = null; }
    window.parent.postMessage({ type: 'kitvei-hakodesh-ocr-deactivated' }, '*');
  };
})();
`
