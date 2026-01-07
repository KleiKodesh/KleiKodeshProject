/**
 * PDF Service - Handles PDF downloads with CORS workarounds
 */

export class PdfService {
  /**
   * Download PDF with fallback mechanisms for CORS issues
   */
  static async downloadPdf(bookId: string): Promise<{ blob: Blob; url: string }> {
    // Try proxy first (development)
    try {
      const proxyUrl = `/api/download?req=${bookId}`
      const response = await fetch(proxyUrl, {
        method: 'GET',
        headers: {
          'Accept': 'application/pdf,*/*'
        }
      })

      if (response.ok) {
        const blob = await response.blob()
        const url = URL.createObjectURL(blob)
        return { blob, url }
      }
    } catch (error) {
      console.log('Proxy method failed, trying direct download:', error)
    }

    // Fallback: Try direct download (might work in production or with browser extensions)
    try {
      const directUrl = `https://download.hebrewbooks.org/downloadhandler.ashx?req=${bookId}`
      const response = await fetch(directUrl, {
        method: 'GET',
        headers: {
          'Accept': 'application/pdf,*/*'
        }
      })

      if (response.ok) {
        const blob = await response.blob()
        const url = URL.createObjectURL(blob)
        return { blob, url }
      }
    } catch (error) {
      console.log('Direct download failed:', error)
    }

    // If both methods fail, throw error - don't open in new tab
    throw new Error('לא ניתן לטעון את הספר. בדוק את החיבור לאינטרנט או נסה שוב מאוחר יותר.')
  }

  /**
   * Download PDF directly using browser's native download (bypasses CORS)
   */
  static downloadPdfDirect(bookId: string, filename: string): void {
    console.log('Using direct download for:', filename, 'Book ID:', bookId)
    
    const directUrl = `https://download.hebrewbooks.org/downloadhandler.ashx?req=${bookId}`
    console.log('Direct download URL:', directUrl)
    
    // Create <a href="https://download.hebrewbooks.org/..." download> approach
    const a = document.createElement('a')
    a.href = directUrl
    a.download = filename  // This triggers the download dialog
    a.style.display = 'none'
    
    // Add to DOM, click to trigger download, then remove
    document.body.appendChild(a)
    console.log('Triggering download with <a download> approach')
    a.click()
    document.body.removeChild(a)
    console.log('Download triggered successfully')
  }

  /**
   * Create a downloadable link for a PDF blob with save dialog
   */
  static async createDownloadLink(blob: Blob, filename: string): Promise<void> {
    console.log('Creating download link for:', filename, 'Blob size:', blob.size, 'Blob type:', blob.type)
    
    // Ensure we have a valid blob
    if (!blob || blob.size === 0) {
      throw new Error('Invalid or empty blob')
    }
    
    // Try different approaches based on browser capabilities
    
    // Method 1: Try the File System Access API (Chrome/Edge with user gesture)
    if ('showSaveFilePicker' in window) {
      try {
        const fileHandle = await (window as any).showSaveFilePicker({
          suggestedName: filename,
          types: [{
            description: 'PDF files',
            accept: { 'application/pdf': ['.pdf'] }
          }]
        })
        
        const writable = await fileHandle.createWritable()
        await writable.write(blob)
        await writable.close()
        console.log('File saved using File System Access API')
        return
      } catch (error) {
        console.log('File System Access API failed or cancelled:', error)
        // Continue to fallback methods
      }
    }
    
    // Method 2: Traditional download attribute method
    try {
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = filename
      a.style.display = 'none'
      
      // Ensure the element is added to DOM before clicking
      document.body.appendChild(a)
      
      // Use setTimeout to ensure the click happens after DOM insertion
      setTimeout(() => {
        a.click()
        document.body.removeChild(a)
        URL.revokeObjectURL(url)
        console.log('Download triggered using anchor method')
      }, 10)
      
      return
    } catch (error) {
      console.log('Anchor download method failed:', error)
    }
    
    // Method 3: Open in new window as fallback
    try {
      const url = URL.createObjectURL(blob)
      const newWindow = window.open(url, '_blank')
      console.log('Opened PDF in new window as fallback')
      
      // Clean up after delay
      setTimeout(() => {
        URL.revokeObjectURL(url)
      }, 5000)
    } catch (error) {
      console.log('All download methods failed:', error)
      throw new Error('Unable to download file')
    }
  }

  /**
   * Check if PDF viewing is supported in current environment
   */
  static isPdfViewingSupported(): boolean {
    // Check if we're in a modern browser that supports PDF viewing
    return 'fetch' in window && 'URL' in window && 'createObjectURL' in URL
  }
}