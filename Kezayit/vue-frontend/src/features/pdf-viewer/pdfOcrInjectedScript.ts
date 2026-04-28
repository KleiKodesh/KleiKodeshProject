// This script is injected into the PDF.js iframe via contentWindow eval.
// It runs entirely inside the iframe's document context so all coordinates
// are in the same space — no cross-frame translation needed.
// It communicates back to the parent via postMessage.

export const PDF_OCR_INJECTED_SCRIPT = /* js */ `
(function() {
  if (window.__zayitOcrTool) return; // already injected

  const tool = {
    isActive: false,
    isDrawing: false,
    startX: 0,
    startY: 0,
    selectionDiv: null,
    crosshairDiv: null,
    langFile: 'heb',
  };

  window.__zayitOcrTool = tool;

  // ── Styles ──────────────────────────────────────────────────────────────

  const style = document.createElement('style');
  style.textContent = \`
    #viewerContainer.zayit-ocr-mode { cursor: crosshair !important; }
    #viewerContainer.zayit-ocr-mode * { cursor: crosshair !important; }
    .zayit-ocr-rect {
      position: absolute;
      border: 2px dashed #0078d4;
      background: rgba(0,120,212,0.08);
      pointer-events: none;
      z-index: 9000;
    }
  \`;
  document.head.appendChild(style);

  // ── Text layer hit test ──────────────────────────────────────────────────

  function extractText(rect) {
    const container = document.getElementById('viewerContainer');
    if (!container) return null;
    const containerRect = container.getBoundingClientRect();
    const scrollLeft = container.scrollLeft;
    const scrollTop = container.scrollTop;
    const words = [];

    for (const span of container.querySelectorAll('.textLayer span')) {
      const text = span.textContent;
      if (!text || !text.trim()) continue;
      const sr = span.getBoundingClientRect();
      const left = sr.left - containerRect.left + scrollLeft;
      const top = sr.top - containerRect.top + scrollTop;
      const cx = left + sr.width / 2;
      const cy = top + sr.height / 2;
      if (cx >= rect.left && cx <= rect.left + rect.width &&
          cy >= rect.top  && cy <= rect.top  + rect.height) {
        words.push(text);
      }
    }
    return words.length > 0 ? words.join(' ') : null;
  }

  // ── Canvas capture ───────────────────────────────────────────────────────

  function captureCanvas(rect) {
    const container = document.getElementById('viewerContainer');
    if (!container) return null;
    const containerRect = container.getBoundingClientRect();
    const scrollLeft = container.scrollLeft;
    const scrollTop = container.scrollTop;

    for (const canvas of container.querySelectorAll('.canvasWrapper canvas')) {
      const cr = canvas.getBoundingClientRect();
      const cl = cr.left - containerRect.left + scrollLeft;
      const ct = cr.top  - containerRect.top  + scrollTop;

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
    const cr = container.getBoundingClientRect();
    tool.startX = e.clientX - cr.left + container.scrollLeft;
    tool.startY = e.clientY - cr.top  + container.scrollTop;

    tool.selectionDiv = document.createElement('div');
    tool.selectionDiv.className = 'zayit-ocr-rect';
    tool.selectionDiv.style.left   = tool.startX + 'px';
    tool.selectionDiv.style.top    = tool.startY + 'px';
    tool.selectionDiv.style.width  = '0px';
    tool.selectionDiv.style.height = '0px';
    container.appendChild(tool.selectionDiv);
  }

  function onMouseMove(e) {
    if (!tool.isDrawing || !tool.selectionDiv) return;
    const container = document.getElementById('viewerContainer');
    const cr = container.getBoundingClientRect();
    const cx = e.clientX - cr.left + container.scrollLeft;
    const cy = e.clientY - cr.top  + container.scrollTop;
    const left   = Math.min(cx, tool.startX);
    const top    = Math.min(cy, tool.startY);
    const width  = Math.abs(cx - tool.startX);
    const height = Math.abs(cy - tool.startY);
    tool.selectionDiv.style.left   = left   + 'px';
    tool.selectionDiv.style.top    = top    + 'px';
    tool.selectionDiv.style.width  = width  + 'px';
    tool.selectionDiv.style.height = height + 'px';
  }

  function onMouseUp(e) {
    if (!tool.isDrawing) return;
    tool.isDrawing = false;

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
    const text = extractText(rect);
    if (text) {
      window.parent.postMessage({ type: 'zayit-ocr-result', text, isOcr: false }, '*');
      return;
    }
    // Need OCR — send canvas data to parent
    const dataUrl = captureCanvas(rect);
    if (dataUrl) {
      window.parent.postMessage({ type: 'zayit-ocr-canvas', dataUrl, langFile: tool.langFile }, '*');
    } else {
      window.parent.postMessage({ type: 'zayit-ocr-result', text: '', isOcr: true }, '*');
    }
  }

  // ── Activate / deactivate ────────────────────────────────────────────────

  tool.activate = function(langFile) {
    tool.isActive = true;
    tool.langFile = langFile || 'heb';
    const container = document.getElementById('viewerContainer');
    if (!container) return;
    container.classList.add('zayit-ocr-mode');
    container.addEventListener('mousedown', onMouseDown);
    container.addEventListener('mousemove', onMouseMove);
    container.addEventListener('mouseup',   onMouseUp);
    container.style.userSelect = 'none';
  };

  tool.deactivate = function() {
    tool.isActive = false;
    const container = document.getElementById('viewerContainer');
    if (!container) return;
    container.classList.remove('zayit-ocr-mode');
    container.removeEventListener('mousedown', onMouseDown);
    container.removeEventListener('mousemove', onMouseMove);
    container.removeEventListener('mouseup',   onMouseUp);
    container.style.userSelect = '';
    if (tool.selectionDiv) { tool.selectionDiv.remove(); tool.selectionDiv = null; }
    window.parent.postMessage({ type: 'zayit-ocr-deactivated' }, '*');
  };
})();
`
