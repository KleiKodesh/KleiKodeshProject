/**
 * Rectangle Selection Tool for PDF.js
 * Allows users to draw a rectangle to select text in specific columns
 * Falls back to OCR (Tesseract.js) if no text layer is found
 */

class RectangleSelectionTool {
    constructor() {
        this.isActive = false;
        this.isDrawing = false;
        this.centerX = 0;
        this.centerY = 0;
        this.selectionDiv = null;
        this.crosshairDiv = null;
        this.viewerContainer = null;
        this.button = null;
        this.tesseractWorker = null;
        this.rafId = null;
    }

    async init() {
        this.viewerContainer = document.getElementById('viewerContainer');
        if (!this.viewerContainer) {
            console.error('Viewer container not found');
            return;
        }

        this.createButton();
        this.createStyles();
        await this.initTesseract();
    }

    async initTesseract() {
        try {
            // Load Tesseract.js from CDN
            if (!window.Tesseract) {
                const script = document.createElement('script');
                script.src = 'https://cdn.jsdelivr.net/npm/tesseract.js@5/dist/tesseract.min.js';
                await new Promise((resolve, reject) => {
                    script.onload = resolve;
                    script.onerror = reject;
                    document.head.appendChild(script);
                });
            }

            // Create worker configured for Hebrew with local language data
            const baseUrl = window.location.origin;
            const worker = await Tesseract.createWorker('heb', 1, {
                langPath: `${baseUrl}/pdfjs/tesseract/`,
                gzip: false
            });

            this.tesseractWorker = worker;

            console.log('[RectSelect] Tesseract worker initialized for Hebrew');
        } catch (error) {
            console.error('[RectSelect] Failed to initialize Tesseract:', error);
        }
    }

    createButton() {
        // Find the toolbar to add our button
        const toolbar = document.getElementById('toolbarViewerRight');
        if (!toolbar) return;

        // Create button container
        const buttonContainer = document.createElement('div');
        buttonContainer.className = 'toolbarButtonWithContainer';
        buttonContainer.id = 'rectangleSelectionContainer';

        // Create the button
        this.button = document.createElement('button');
        this.button.id = 'rectangleSelectionButton';
        this.button.className = 'toolbarButton';
        this.button.type = 'button';
        this.button.tabIndex = 0;
        this.button.title = 'בחירת טקסט באזור מלבני (Rectangle Text Selection)';
        this.button.setAttribute('aria-label', 'Rectangle Text Selection');

        // Add label span (required by PDF.js button structure)
        const label = document.createElement('span');
        label.setAttribute('data-l10n-id', 'rectangle-selection-button-label');
        label.textContent = 'Rectangle Selection';
        this.button.appendChild(label);

        buttonContainer.appendChild(this.button);

        // Insert before the first button in the right toolbar
        toolbar.insertBefore(buttonContainer, toolbar.firstChild);

        // Add click handler
        this.button.addEventListener('click', () => this.toggle());
    }

    createStyles() {
        const style = document.createElement('style');
        style.textContent = `
      #rectangleSelectionButton {
        position: relative;
      }

      #rectangleSelectionButton::before {
        content: '';
        display: inline-block;
        width: 16px;
        height: 16px;
        background-color: currentColor;
        mask-image: url('data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 16 16"><rect x="2" y="2" width="12" height="12" fill="none" stroke="black" stroke-width="2" stroke-dasharray="2,2"/></svg>');
        mask-size: contain;
        mask-repeat: no-repeat;
        mask-position: center;
        -webkit-mask-image: url('data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 16 16"><rect x="2" y="2" width="12" height="12" fill="none" stroke="black" stroke-width="2" stroke-dasharray="2,2"/></svg>');
        -webkit-mask-size: contain;
        -webkit-mask-repeat: no-repeat;
        -webkit-mask-position: center;
      }

      #rectangleSelectionButton span {
        display: none;
      }

      #rectangleSelectionButton.active {
        background-color: rgba(0, 0, 0, 0.2);
      }

      .rectangle-selection-overlay {
        position: absolute;
        border: 2px dashed #0066cc;
        background-color: rgba(0, 102, 204, 0.1);
        pointer-events: none;
        z-index: 1000;
      }

      #viewerContainer.rectangle-selection-mode {
        cursor: none !important;
      }

      #viewerContainer.rectangle-selection-mode * {
        cursor: none !important;
      }

      .rectangle-selection-crosshair {
        position: absolute;
        width: 20px;
        height: 20px;
        pointer-events: none;
        z-index: 1001;
        left: -10px;
        top: -10px;
        will-change: transform;
      }

      .rectangle-selection-crosshair::before,
      .rectangle-selection-crosshair::after {
        content: '';
        position: absolute;
        background-color: #0066cc;
      }

      .rectangle-selection-crosshair::before {
        left: 50%;
        top: 0;
        width: 2px;
        height: 100%;
        transform: translateX(-50%);
      }

      .rectangle-selection-crosshair::after {
        left: 0;
        top: 50%;
        width: 100%;
        height: 2px;
        transform: translateY(-50%);
      }
    `;
        document.head.appendChild(style);
    }

    toggle() {
        this.isActive = !this.isActive;

        if (this.isActive) {
            this.activate();
        } else {
            this.deactivate();
        }
    }

    activate() {
        this.button.classList.add('active');
        this.viewerContainer.classList.add('rectangle-selection-mode');

        // Create custom crosshair cursor
        this.crosshairDiv = document.createElement('div');
        this.crosshairDiv.className = 'rectangle-selection-crosshair';
        this.viewerContainer.appendChild(this.crosshairDiv);

        // Add event listeners
        this.viewerContainer.addEventListener('mousedown', this.onMouseDown);
        this.viewerContainer.addEventListener('mousemove', this.onMouseMove);
        this.viewerContainer.addEventListener('mouseup', this.onMouseUp);

        // Prevent text selection while drawing
        this.viewerContainer.style.userSelect = 'none';
    }

    deactivate() {
        this.button.classList.remove('active');
        this.viewerContainer.classList.remove('rectangle-selection-mode');

        // Remove event listeners
        this.viewerContainer.removeEventListener('mousedown', this.onMouseDown);
        this.viewerContainer.removeEventListener('mousemove', this.onMouseMove);
        this.viewerContainer.removeEventListener('mouseup', this.onMouseUp);

        // Re-enable text selection
        this.viewerContainer.style.userSelect = '';

        // Remove crosshair
        if (this.crosshairDiv) {
            this.crosshairDiv.remove();
            this.crosshairDiv = null;
        }

        // Remove any existing selection rectangle
        if (this.selectionDiv) {
            this.selectionDiv.remove();
            this.selectionDiv = null;
        }
    }

    onMouseMove = (e) => {
        // Use requestAnimationFrame to throttle updates
        if (this.rafId) {
            return;
        }

        this.rafId = requestAnimationFrame(() => {
            this.rafId = null;

            // Update crosshair position
            if (this.crosshairDiv) {
                const rect = this.viewerContainer.getBoundingClientRect();
                const x = e.clientX - rect.left + this.viewerContainer.scrollLeft;
                const y = e.clientY - rect.top + this.viewerContainer.scrollTop;
                this.crosshairDiv.style.transform = `translate(${x}px, ${y}px)`;
            }

            if (!this.isDrawing || !this.selectionDiv) return;

            const rect = this.viewerContainer.getBoundingClientRect();
            const currentX = e.clientX - rect.left + this.viewerContainer.scrollLeft;
            const currentY = e.clientY - rect.top + this.viewerContainer.scrollTop;

            // Calculate rectangle from start point to current point (traditional drag)
            const width = Math.abs(currentX - this.centerX);
            const height = Math.abs(currentY - this.centerY);
            const left = Math.min(currentX, this.centerX);
            const top = Math.min(currentY, this.centerY);

            this.selectionDiv.style.left = left + 'px';
            this.selectionDiv.style.top = top + 'px';
            this.selectionDiv.style.width = width + 'px';
            this.selectionDiv.style.height = height + 'px';
        });
    }

    onMouseDown = (e) => {
        if (!this.isActive || e.button !== 0) return;

        this.isDrawing = true;
        const rect = this.viewerContainer.getBoundingClientRect();
        this.centerX = e.clientX - rect.left + this.viewerContainer.scrollLeft;
        this.centerY = e.clientY - rect.top + this.viewerContainer.scrollTop;

        // Create selection div at start point
        this.selectionDiv = document.createElement('div');
        this.selectionDiv.className = 'rectangle-selection-overlay';
        this.selectionDiv.style.left = this.centerX + 'px';
        this.selectionDiv.style.top = this.centerY + 'px';
        this.selectionDiv.style.width = '0px';
        this.selectionDiv.style.height = '0px';
        this.viewerContainer.appendChild(this.selectionDiv);

        e.preventDefault();
    }

    onMouseUp = (e) => {
        if (!this.isDrawing) return;

        this.isDrawing = false;

        if (this.selectionDiv) {
            // Get the rectangle bounds
            const rect = {
                left: parseFloat(this.selectionDiv.style.left),
                top: parseFloat(this.selectionDiv.style.top),
                width: parseFloat(this.selectionDiv.style.width),
                height: parseFloat(this.selectionDiv.style.height)
            };

            // Only proceed if rectangle has meaningful size
            if (rect.width > 5 && rect.height > 5) {
                this.selectTextInRectangle(rect);
            }

            // Remove the selection rectangle
            if (this.selectionDiv) {
                this.selectionDiv.remove();
                this.selectionDiv = null;
            }
        }
    }

    onMouseLeave = (e) => {
        // Hide crosshair when mouse leaves container
        if (this.crosshairDiv) {
            this.crosshairDiv.style.display = 'none';
        }
    }

    async selectTextInRectangle(rect) {
        // Get all text layers
        const textLayers = this.viewerContainer.querySelectorAll('.textLayer');
        const containerRect = this.viewerContainer.getBoundingClientRect();

        // Collect all spans within the rectangle
        const selectedWords = [];

        textLayers.forEach(textLayer => {
            const spans = textLayer.querySelectorAll('span');

            spans.forEach(span => {
                const text = span.textContent;
                if (!text) return;

                const spanRect = span.getBoundingClientRect();
                const spanLeft = spanRect.left - containerRect.left + this.viewerContainer.scrollLeft;
                const spanTop = spanRect.top - containerRect.top + this.viewerContainer.scrollTop;

                // Check if span center is within rectangle
                const spanCenterX = spanLeft + spanRect.width / 2;
                const spanCenterY = spanTop + spanRect.height / 2;

                const isInRect = (
                    spanCenterX >= rect.left &&
                    spanCenterX <= rect.left + rect.width &&
                    spanCenterY >= rect.top &&
                    spanCenterY <= rect.top + rect.height
                );

                if (isInRect) {
                    selectedWords.push(text);
                }
            });
        });

        // If text found, show popup
        if (selectedWords.length > 0) {
            const textToCopy = selectedWords.join(' ');
            this.showTextPopup(textToCopy);
        } else {
            // No text layer found - try OCR
            console.log('[RectSelect] No text found in selection, attempting OCR...');
            await this.performOCR(rect);
        }

        // Auto-deactivate the tool after selection
        this.deactivate();
        this.isActive = false;
    }

    async performOCR(rect) {
        let loadingPopup = null;
        try {
            // Show loading popup
            loadingPopup = this.showLoadingPopup('מבצע OCR...');

            // Capture the rectangle area as an image
            const canvas = await this.captureRectangle(rect);

            // Preprocess image for better OCR quality
            const processedCanvas = this.preprocessImageForOCR(canvas);

            // Use local Tesseract for high-quality Hebrew OCR
            let text = null;
            if (this.tesseractWorker) {
                const { data: { text: tesseractText } } = await this.tesseractWorker.recognize(processedCanvas);
                text = tesseractText;
            }

            // Close loading popup
            if (loadingPopup) {
                loadingPopup.remove();
                loadingPopup = null;
            }

            // Show result
            if (text && text.trim()) {
                this.showTextPopup(text.trim(), 'טקסט מזוהה (OCR)');
            } else {
                this.showTextPopup('', 'לא נמצא טקסט באזור הנבחר');
            }
        } catch (error) {
            console.error('[RectSelect] OCR failed:', error);
            // Close loading popup on error
            if (loadingPopup) {
                loadingPopup.remove();
            }
            this.showTextPopup('', 'שגיאה בזיהוי טקסט');
        }
    }

    preprocessImageForOCR(canvas) {
        // Create a new canvas for preprocessing
        const processedCanvas = document.createElement('canvas');

        // Scale up for better quality (2x)
        const scale = 2;
        processedCanvas.width = canvas.width * scale;
        processedCanvas.height = canvas.height * scale;

        const ctx = processedCanvas.getContext('2d');

        // Use high-quality image smoothing
        ctx.imageSmoothingEnabled = true;
        ctx.imageSmoothingQuality = 'high';

        // Draw scaled image
        ctx.drawImage(canvas, 0, 0, processedCanvas.width, processedCanvas.height);

        // Get image data for processing
        const imageData = ctx.getImageData(0, 0, processedCanvas.width, processedCanvas.height);
        const data = imageData.data;

        // Convert to grayscale and increase contrast
        for (let i = 0; i < data.length; i += 4) {
            // Convert to grayscale
            const gray = 0.299 * data[i] + 0.587 * data[i + 1] + 0.114 * data[i + 2];

            // Increase contrast (simple threshold)
            const threshold = 128;
            const value = gray > threshold ? 255 : 0;

            data[i] = value;     // R
            data[i + 1] = value; // G
            data[i + 2] = value; // B
            // Alpha stays the same
        }

        // Put processed image back
        ctx.putImageData(imageData, 0, 0);

        return processedCanvas;
    }

    async tryOnlineOCR(canvas) {
        try {
            // Optimize image size for faster processing (max 2000px on longest side)
            let processCanvas = canvas;
            const maxSize = 2000;
            if (canvas.width > maxSize || canvas.height > maxSize) {
                const scale = Math.min(maxSize / canvas.width, maxSize / canvas.height);
                processCanvas = document.createElement('canvas');
                processCanvas.width = Math.round(canvas.width * scale);
                processCanvas.height = Math.round(canvas.height * scale);
                const ctx = processCanvas.getContext('2d');
                ctx.drawImage(canvas, 0, 0, processCanvas.width, processCanvas.height);
            }

            // Convert canvas to blob
            const blob = await new Promise(resolve => processCanvas.toBlob(resolve, 'image/png'));

            // Try OCR.space API with Engine 3 (supports 200+ languages with auto-detection)
            const formData = new FormData();
            formData.append('file', blob, 'image.png');
            formData.append('language', 'auto'); // Engine 3 requires 'auto' for language detection
            formData.append('isOverlayRequired', 'false');
            formData.append('detectOrientation', 'true');
            formData.append('scale', 'true');
            formData.append('OCREngine', '3'); // Engine 3 supports Hebrew and 200+ languages

            // Add timeout to prevent hanging
            const controller = new AbortController();
            const timeoutId = setTimeout(() => controller.abort(), 15000); // 15 second timeout

            const response = await fetch('https://api.ocr.space/parse/image', {
                method: 'POST',
                headers: {
                    'apikey': 'K87899142388957' // Free public API key
                },
                body: formData,
                signal: controller.signal
            });

            clearTimeout(timeoutId);

            if (!response.ok) {
                console.log('[RectSelect] Online OCR API error:', response.status);
                return null;
            }

            const result = await response.json();

            if (result.IsErroredOnProcessing) {
                console.log('[RectSelect] Online OCR processing error:', result.ErrorMessage);
                return null;
            }

            const text = result.ParsedResults?.[0]?.ParsedText;
            if (text) {
                console.log('[RectSelect] Online OCR successful');
                return text;
            }

            return null;
        } catch (error) {
            if (error.name === 'AbortError') {
                console.log('[RectSelect] Online OCR timeout');
            } else {
                console.log('[RectSelect] Online OCR failed:', error.message);
            }
            return null;
        }
    }

    async captureRectangle(rect) {
        // Find the PDF canvas that intersects with the rectangle
        const canvases = this.viewerContainer.querySelectorAll('.canvasWrapper canvas');
        const containerRect = this.viewerContainer.getBoundingClientRect();

        for (const canvas of canvases) {
            const canvasRect = canvas.getBoundingClientRect();
            const canvasLeft = canvasRect.left - containerRect.left + this.viewerContainer.scrollLeft;
            const canvasTop = canvasRect.top - containerRect.top + this.viewerContainer.scrollTop;

            // Check if rectangle intersects with this canvas
            const intersects = !(
                rect.left + rect.width < canvasLeft ||
                rect.left > canvasLeft + canvasRect.width ||
                rect.top + rect.height < canvasTop ||
                rect.top > canvasTop + canvasRect.height
            );

            if (intersects) {
                // Calculate the rectangle position relative to the canvas
                const relativeX = Math.max(0, rect.left - canvasLeft);
                const relativeY = Math.max(0, rect.top - canvasTop);

                // Clamp width and height to canvas bounds
                const maxWidth = canvasRect.width - relativeX;
                const maxHeight = canvasRect.height - relativeY;
                const clampedWidth = Math.min(rect.width, maxWidth);
                const clampedHeight = Math.min(rect.height, maxHeight);

                // Get the scale factor (canvas internal size vs displayed size)
                const scaleX = canvas.width / canvasRect.width;
                const scaleY = canvas.height / canvasRect.height;

                // Create a new canvas for the cropped area
                const croppedCanvas = document.createElement('canvas');
                croppedCanvas.width = Math.round(clampedWidth * scaleX);
                croppedCanvas.height = Math.round(clampedHeight * scaleY);
                const ctx = croppedCanvas.getContext('2d');

                // Draw the cropped portion
                ctx.drawImage(
                    canvas,
                    Math.round(relativeX * scaleX),
                    Math.round(relativeY * scaleY),
                    Math.round(clampedWidth * scaleX),
                    Math.round(clampedHeight * scaleY),
                    0,
                    0,
                    croppedCanvas.width,
                    croppedCanvas.height
                );

                return croppedCanvas;
            }
        }

        throw new Error('No canvas found for rectangle');
    }

    showLoadingPopup(message) {
        // Create overlay
        const overlay = document.createElement('div');
        overlay.style.cssText = `
            position: fixed;
            top: 0;
            left: 0;
            right: 0;
            bottom: 0;
            background-color: rgba(0, 0, 0, 0.5);
            z-index: 10000;
            display: flex;
            align-items: center;
            justify-content: center;
        `;

        // Create loading container
        const loading = document.createElement('div');
        loading.style.cssText = `
            background: white;
            border-radius: 8px;
            padding: 30px 40px;
            text-align: center;
            box-shadow: 0 4px 20px rgba(0, 0, 0, 0.3);
        `;

        // Create spinner
        const spinner = document.createElement('div');
        spinner.style.cssText = `
            border: 4px solid #f3f3f3;
            border-top: 4px solid #0066cc;
            border-radius: 50%;
            width: 40px;
            height: 40px;
            animation: spin 1s linear infinite;
            margin: 0 auto 15px;
        `;

        // Add spinner animation
        const style = document.createElement('style');
        style.textContent = `
            @keyframes spin {
                0% { transform: rotate(0deg); }
                100% { transform: rotate(360deg); }
            }
        `;
        document.head.appendChild(style);

        // Create message
        const text = document.createElement('div');
        text.textContent = message;
        text.style.cssText = `
            font-size: 16px;
            color: #333;
        `;

        loading.appendChild(spinner);
        loading.appendChild(text);
        overlay.appendChild(loading);
        document.body.appendChild(overlay);

        return overlay;
    }

    showTextPopup(text, title = 'טקסט נבחר') {
        // Create overlay
        const overlay = document.createElement('div');
        overlay.style.cssText = `
            position: fixed;
            top: 0;
            left: 0;
            right: 0;
            bottom: 0;
            background-color: rgba(0, 0, 0, 0.5);
            z-index: 10000;
            display: flex;
            align-items: center;
            justify-content: center;
        `;

        // Create popup container
        const popup = document.createElement('div');
        popup.style.cssText = `
            background: white;
            border-radius: 8px;
            padding: 20px;
            max-width: 600px;
            width: 90%;
            box-shadow: 0 4px 20px rgba(0, 0, 0, 0.3);
        `;

        // Create title
        const titleElement = document.createElement('div');
        titleElement.textContent = title;
        titleElement.style.cssText = `
            font-size: 16px;
            font-weight: bold;
            margin-bottom: 12px;
            text-align: right;
        `;

        // Create textarea
        const textarea = document.createElement('textarea');
        textarea.value = text;
        textarea.style.cssText = `
            width: 100%;
            min-height: 150px;
            padding: 10px;
            border: 1px solid #ccc;
            border-radius: 4px;
            font-size: 14px;
            font-family: inherit;
            resize: vertical;
            direction: rtl;
            text-align: right;
            box-sizing: border-box;
        `;

        // Create button container
        const buttonContainer = document.createElement('div');
        buttonContainer.style.cssText = `
            display: flex;
            gap: 10px;
            margin-top: 12px;
            justify-content: flex-end;
        `;

        // Create copy button
        const copyButton = document.createElement('button');
        copyButton.textContent = 'העתק';
        copyButton.style.cssText = `
            padding: 8px 20px;
            background-color: #0066cc;
            color: white;
            border: none;
            border-radius: 4px;
            cursor: pointer;
            font-size: 14px;
        `;
        copyButton.onmouseover = () => copyButton.style.backgroundColor = '#0052a3';
        copyButton.onmouseout = () => copyButton.style.backgroundColor = '#0066cc';
        copyButton.onclick = () => {
            navigator.clipboard.writeText(textarea.value).then(() => {
                copyButton.textContent = '✓ הועתק';
                setTimeout(() => overlay.remove(), 500);
            }).catch(err => {
                console.error('Failed to copy:', err);
                alert('שגיאה בהעתקה');
            });
        };

        // Create cancel button
        const cancelButton = document.createElement('button');
        cancelButton.textContent = 'ביטול';
        cancelButton.style.cssText = `
            padding: 8px 20px;
            background-color: #f0f0f0;
            color: #333;
            border: 1px solid #ccc;
            border-radius: 4px;
            cursor: pointer;
            font-size: 14px;
        `;
        cancelButton.onmouseover = () => cancelButton.style.backgroundColor = '#e0e0e0';
        cancelButton.onmouseout = () => cancelButton.style.backgroundColor = '#f0f0f0';
        cancelButton.onclick = () => overlay.remove();

        // Assemble popup
        buttonContainer.appendChild(cancelButton);
        buttonContainer.appendChild(copyButton);
        popup.appendChild(titleElement);
        popup.appendChild(textarea);
        popup.appendChild(buttonContainer);
        overlay.appendChild(popup);

        // Close on overlay click
        overlay.onclick = (e) => {
            if (e.target === overlay) {
                overlay.remove();
            }
        };

        // Close on Escape key
        const escapeHandler = (e) => {
            if (e.key === 'Escape') {
                overlay.remove();
                document.removeEventListener('keydown', escapeHandler);
            }
        };
        document.addEventListener('keydown', escapeHandler);

        // Add to document and focus textarea
        document.body.appendChild(overlay);
        textarea.focus();
        textarea.select();
    }
}

// Initialize when DOM is ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', async () => {
        const tool = new RectangleSelectionTool();
        await tool.init();
    });
} else {
    const tool = new RectangleSelectionTool();
    tool.init();
}
