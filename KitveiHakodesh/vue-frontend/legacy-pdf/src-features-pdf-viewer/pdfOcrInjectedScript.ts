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
    // Join words and clean up whitespace. (Backslashes are doubled: this is a template string.)
    const text = words.join(' ').replace(/\\s+/g, ' ').trim();
    return text.length > 0 ? text : null;
  }

  // A text layer that is there but unreadable — boxes/replacement chars, private-use or
  // control glyphs (fonts with no Unicode map), or Latin-1 mojibake (old Hebrew fonts
  // mapped onto à-ú). Such a selection goes to canvas OCR, as if forceOcr were set.
  function isGibberish(text) {
    const chars = Array.from(text.replace(/\\s+/g, ''));
    if (chars.length === 0) return true;
    let bad = 0;
    for (const ch of chars) {
      if (/[\\uFFFD\\u25A0-\\u25FF\\u0000-\\u001F\\u007F-\\u009F]|\\p{Co}|\\p{Cn}/u.test(ch)) bad++;
      else if (/[\\u00C0-\\u00FF]/.test(ch)) bad++;
    }
    return bad / chars.length >= 0.3;
  }

  // ── Canvas capture ───────────────────────────────────────────────────────

  function captureCanvas(rect) {
    const container = document.getElementById('viewerContainer');
    if (!container) return null;
    const origin = paddingBoxOrigin(container);

    // Collect every page canvas the box overlaps (a box can span pages, or both pages of
    // a spread), cropped to the true overlap in content coordinates.
    const parts = [];
    for (const canvas of container.querySelectorAll('.canvasWrapper canvas')) {
      const cr = canvas.getBoundingClientRect();
      if (!cr.width || !cr.height) continue;
      const cl = cr.left - origin.x;
      const ct = cr.top  - origin.y;

      const x0 = Math.max(rect.left, cl);
      const y0 = Math.max(rect.top, ct);
      const x1 = Math.min(rect.left + rect.width,  cl + cr.width);
      const y1 = Math.min(rect.top  + rect.height, ct + cr.height);
      if (x1 - x0 < 1 || y1 - y0 < 1) continue;

      parts.push({
        canvas, x0, y0, x1, y1, cl, ct,
        sx: canvas.width  / cr.width,
        sy: canvas.height / cr.height,
      });
    }
    if (parts.length === 0) return null;

    // Draw each part where it sits on screen, at the sharpest canvas resolution among
    // them; the gaps between pages stay white.
    let scale = 0;
    let ux = Infinity, uy = Infinity, ux1 = -Infinity, uy1 = -Infinity;
    for (const p of parts) {
      scale = Math.max(scale, p.sx, p.sy);
      ux = Math.min(ux, p.x0);
      uy = Math.min(uy, p.y0);
      ux1 = Math.max(ux1, p.x1);
      uy1 = Math.max(uy1, p.y1);
    }

    const out = document.createElement('canvas');
    out.width  = Math.max(1, Math.round((ux1 - ux) * scale));
    out.height = Math.max(1, Math.round((uy1 - uy) * scale));
    const ctx = out.getContext('2d');
    ctx.fillStyle = '#ffffff';
    ctx.fillRect(0, 0, out.width, out.height);
    ctx.imageSmoothingQuality = 'high';
    for (const p of parts) {
      ctx.drawImage(
        p.canvas,
        (p.x0 - p.cl) * p.sx, (p.y0 - p.ct) * p.sy,
        (p.x1 - p.x0) * p.sx, (p.y1 - p.y0) * p.sy,
        (p.x0 - ux) * scale, (p.y0 - uy) * scale,
        (p.x1 - p.x0) * scale, (p.y1 - p.y0) * scale
      );
    }
    return out.toDataURL('image/png');
  }

  // ── Mouse handlers ───────────────────────────────────────────────────────

  function onMouseDown(e) {
    if (!tool.isActive || e.button !== 0) return;
    const container = document.getElementById('viewerContainer');
    if (!container || !container.contains(e.target)) return;
    e.preventDefault();
    // The hand tool (GrabToPan) takes mousedown in the CAPTURE phase on the
    // container and stops it there — these listeners sit on window, capture
    // phase, so they run first; stop the event so the page doesn't pan.
    e.stopImmediatePropagation();
    tool.isDrawing = true;
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
    e.stopImmediatePropagation();
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
    e.stopImmediatePropagation();
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
      if (text && !isGibberish(text)) {
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
    window.addEventListener('mousedown', onMouseDown, true);
    window.addEventListener('mousemove', onMouseMove, true);
    window.addEventListener('mouseup',   onMouseUp, true);
    container.style.userSelect = 'none';
  };

  tool.deactivate = function() {
    tool.isActive = false;
    const container = document.getElementById('viewerContainer');
    if (!container) return;
    container.classList.remove('kitvei-hakodesh-ocr-mode');
    container.classList.remove('kitvei-hakodesh-ocr-drawing');
    window.removeEventListener('mousedown', onMouseDown, true);
    window.removeEventListener('mousemove', onMouseMove, true);
    window.removeEventListener('mouseup',   onMouseUp, true);
    container.style.userSelect = '';
    if (tool.selectionDiv) { tool.selectionDiv.remove(); tool.selectionDiv = null; }
    window.parent.postMessage({ type: 'kitvei-hakodesh-ocr-deactivated' }, '*');
  };
})();
`
