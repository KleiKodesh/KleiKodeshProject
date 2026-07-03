import http from 'node:http'

function post(path, body) {
  return new Promise((resolve) => {
    const bodyStr = JSON.stringify(body)
    const req = http.request(
      {
        host: 'localhost', port: 5173, path,
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(bodyStr) }
      },
      (res) => {
        let d = ''
        res.on('data', c => d += c)
        res.on('end', () => resolve({ status: res.statusCode, body: d.slice(0, 300) }))
      }
    )
    req.on('error', e => resolve({ status: 'ERR', body: e.message }))
    req.write(bodyStr)
    req.end()
  })
}

console.log('/query:',            await post('/query',            { sql: 'SELECT 1', params: [] }))
console.log('/document-locator:', await post('/document-locator', { type: 'status' }))
