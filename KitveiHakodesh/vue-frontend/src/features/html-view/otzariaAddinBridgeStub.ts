/**
 * Injected bridge stub for Otzaria addins.
 *
 * Recreates the official Otzaria plugin SDK surface inside the addin iframe:
 * the host injects a global `window.Otzaria` object with `call(method, payload)`,
 * `on(event, handler)` and `off(event, handler)`. `call` resolves with the official
 * envelope `{ success, data, error }` — it never rejects — exactly like the real
 * Otzaria app (see docs/plugin-sdk/README.md in the otzaria/otzaria repository).
 *
 * `window.OtzariaAddin` is kept as a legacy alias from the first bridge version;
 * its `call` resolves with the bare data and rejects on error.
 *
 * Must be plain ES5 — we cannot control the addin's execution environment.
 */

export function buildBridgeStubScript(): string {
  return `
(function () {
  if (window.Otzaria) return;
  var callIdCounter = 0, pendingCalls = {}, eventHandlers = {};

  window.addEventListener('message', function (event) {
    var data = event.data;
    if (!data || typeof data !== 'object') return;
    if (data.type === 'otzaria-reply') {
      var resolve = pendingCalls[data.callId]; if (!resolve) return;
      delete pendingCalls[data.callId];
      resolve({
        success: data.success === true,
        data: data.success === true ? data.data : null,
        error: data.success === true ? null : (data.error || { code: 'INTERNAL', message: 'unknown error', schemaVersion: 1 })
      });
    }
    if (data.type === 'otzaria-event') {
      var handlers = eventHandlers[data.event]; if (!handlers) return;
      for (var i = 0; i < handlers.length; i++) { try { handlers[i](data.payload); } catch (_) {} }
    }
  });

  function call(method, payload) {
    return new Promise(function (resolve) {
      var id = String(++callIdCounter);
      pendingCalls[id] = resolve;
      window.parent.postMessage({ type: 'otzaria-call', callId: id, method: method, params: payload != null ? payload : null }, '*');
    });
  }
  function on(eventName, handler) {
    if (!eventHandlers[eventName]) eventHandlers[eventName] = [];
    eventHandlers[eventName].push(handler);
  }
  function off(eventName, handler) {
    if (!eventHandlers[eventName]) return;
    if (!handler) { eventHandlers[eventName] = []; return; }
    eventHandlers[eventName] = eventHandlers[eventName].filter(function (h) { return h !== handler; });
  }

  window.Otzaria = { call: call, on: on, off: off };
  window.OtzariaAddin = {
    call: function (method, payload) {
      return call(method, payload).then(function (reply) {
        if (!reply.success) return Promise.reject(new Error(reply.error && reply.error.message ? reply.error.message : 'call failed'));
        return reply.data;
      });
    },
    on: on,
    off: off
  };
})();
`
}
