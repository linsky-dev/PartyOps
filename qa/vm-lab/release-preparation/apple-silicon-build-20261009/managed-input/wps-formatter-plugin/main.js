/* Mac 对象通道：仅转发 C# 调用，不实现排版规则。 */
(function (global) {
  'use strict';
  function createDispatcher(application, config, factoryTypes) {
    if (config.mode === 'task-lease-format' || config.mode === 'task-lease-undo-domain' || config.mode === 'task-lease-undo-effect' || (config.mode === 'task-lease-range-primitive' || config.mode === 'task-lease-range-primitive-duplicate')) {
      var leaseFactory = typeof module !== 'undefined' && module.exports ? require('./task-lease.js') : global.PartyOpsMacTaskLeaseDispatcher;
      if (typeof leaseFactory !== 'function') throw new Error('MAC_LEASE_DISPATCHER_UNAVAILABLE');
      return leaseFactory(application, config, global.wps);
    }
    var objects = new Map([[1, {value: application, scope: 'application'}]]);
    var identities = new Map([[application, 1]]);
    var nextId = 2;
    var lastId = 0;
    var taskDocument = null;
    var ownedPath = config.input;
    var closed = false;
    var dual = config.mode === 'dual-instance-a-read-only' || config.mode === 'dual-instance-b-read-only';
    function encode(value, scope) {
      if (value === undefined || value === null) return {kind: 'value', value: null};
      if (typeof value === 'object' || typeof value === 'function') {
        var id = identities.get(value);
        if (!id) { id = nextId++; identities.set(value, id); objects.set(id, {value: value, scope: scope}); }
        return {kind: 'object', id: id};
      }
      if (typeof value !== 'string' && typeof value !== 'number' && typeof value !== 'boolean') throw new Error('MAC_VALUE_UNSUPPORTED');
      return {kind: 'value', value: value};
    }
    function decode(wire) {
      if (!wire || typeof wire !== 'object') throw new Error('MAC_ARGUMENT_INVALID');
      if (wire.kind === 'missing') return undefined;
      if (wire.kind === 'value') return wire.value;
      if (wire.kind === 'object' && objects.has(wire.id)) return objects.get(wire.id).value;
      throw new Error('MAC_OBJECT_EXPIRED');
    }
    function invalidateTask() {
      objects.forEach(function (record, id) {
        if (record.scope === 'task' || record.scope === 'selection') { objects.delete(id); identities.delete(record.value); }
      });
      taskDocument = null;
      closed = true;
    }
    return function dispatch(command) {
      var receipt = {schema: 1, session: config.session, id: command && command.id};
      if (dual) { receipt.generation = config.generation; receipt.endpoint = config.endpoint; }
      try {
        if (!command || command.schema !== 1 || command.session !== config.session
            || !Number.isInteger(command.id) || command.id <= lastId) throw new Error('MAC_COMMAND_STALE');
        if (dual && (command.generation !== config.generation || command.endpoint !== config.endpoint))
          throw new Error('MAC_DUAL_BINDING_MISMATCH');
        // 副作用请求也只执行一次。错误与丢失响应均不能重放。
        lastId = command.id;
        if (!objects.has(command.object)) throw new Error('MAC_OBJECT_EXPIRED');
        var record = objects.get(command.object);
        var member = command.member;
        if (typeof member !== 'string' || !/^[A-Za-z][A-Za-z0-9_]*$/.test(member)
            || ['constructor', 'prototype', '__proto__'].indexOf(member) >= 0) throw new Error('MAC_MEMBER_INVALID');
        if (!Array.isArray(command.args) || command.args.length > 64) throw new Error('MAC_ARGUMENT_INVALID');
        var args = command.args.map(decode);
        var op = command.operation;
        var scope = record.scope;
        var lifecycle = config.mode === 'lifecycle-read-only' || dual;
        if (lifecycle && scope === 'application' && op === 'inspect' && args.length === 0) {
          if (member === 'FactoryTypes') {
            receipt.result = {kind: 'value', value: JSON.stringify(factoryTypes || {factory_invoked: false, metadata_available: false})};
          } else if (member === 'CarrierDocument') {
            if (Number(application.Documents.Count) !== 1 || !config.carrier || String(application.ActiveDocument.FullName) !== config.carrier)
              throw new Error('MAC_CARRIER_DOCUMENT_MISMATCH');
            receipt.result = {kind: 'value', value: config.carrier};
          } else if (dual && member === 'DocumentsSnapshot') {
            // 完整真实集合，不筛选任何文档，不包装用户文档为可修改对象。
            var docs = application.Documents;
            var beforeCount = Number(docs.Count), names = [];
            if (!Number.isInteger(beforeCount) || beforeCount < 0) throw new Error('MAC_DUAL_COLLECTION_COUNT_INVALID');
            for (var index = 1; index <= beforeCount; index++) names.push(String(docs.Item(index).FullName));
            if (Number(docs.Count) !== beforeCount) throw new Error('MAC_DUAL_COLLECTION_CHANGED_DURING_READ');
            receipt.result = {kind: 'value', value: JSON.stringify({count: beforeCount, full_names: names})};
          } else throw new Error('MAC_INSPECTION_UNKNOWN');
          receipt.args = command.args;
          return receipt;
        }
        if (lifecycle && op === 'set') throw new Error('MAC_LIFECYCLE_WRITE_DENIED');
        if (lifecycle && op === 'get' && args.length !== 0) throw new Error('MAC_LIFECYCLE_INDEXED_GET_DENIED');
        if (scope === 'application') {
          var permitted = ['Name', 'Version', 'Path', 'Documents'];
          if (lifecycle) permitted = permitted.concat(['ActiveDocument', 'Selection']);
          if (op !== 'get' || args.length !== 0 || permitted.indexOf(member) < 0)
            throw new Error('MAC_USER_APPLICATION_PROTECTED');
          if (member === 'ActiveDocument' || member === 'Selection') {
            if (!taskDocument || String(taskDocument.FullName) !== ownedPath) throw new Error('MAC_TASK_DOCUMENT_BINDING_LOST');
            var actualDocument = member === 'ActiveDocument' ? application.ActiveDocument : application.Selection.Document;
            if (!actualDocument || String(actualDocument.FullName) !== ownedPath)
              throw new Error(member === 'ActiveDocument' ? 'MAC_ACTIVE_DOCUMENT_SCOPE_MISMATCH' : 'MAC_SELECTION_DOCUMENT_SCOPE_MISMATCH');
          }
        } else if (scope === 'documents') {
          if (!(op === 'get' && member === 'Count' && args.length === 0)
              && !(op === 'call' && member === 'Open')) throw new Error('MAC_USER_DOCUMENTS_PROTECTED');
          if (member === 'Open') {
            if (config.mode === 'dual-instance-a-read-only') throw new Error('MAC_DUAL_A_OPEN_DENIED');
            if (taskDocument || closed || Number(record.value.Count) !== 0) throw new Error('MAC_ISOLATION_NOT_PROVEN');
            if (args[0] !== config.input) throw new Error('MAC_INPUT_PATH_MISMATCH');
            if (lifecycle && (args.length !== 16 || args[2] !== true || args[11] !== false)) throw new Error('MAC_LIFECYCLE_OPEN_CONTRACT_INVALID');
          }
        } else {
          if (!taskDocument || String(taskDocument.FullName) !== ownedPath) throw new Error('MAC_TASK_DOCUMENT_BINDING_LOST');
          if (scope === 'selection' && (!record.value.Document || String(record.value.Document.FullName) !== ownedPath))
            throw new Error('MAC_SELECTION_DOCUMENT_SCOPE_MISMATCH');
          if (lifecycle && op === 'call' && member !== 'Close') throw new Error('MAC_LIFECYCLE_WRITE_DENIED');
          if (member === 'Application' || member === 'Parent') throw new Error('MAC_SCOPE_ESCAPE_DENIED');
          if (member === 'SaveAs' || member === 'SaveAs2' || member === 'ExportAsFixedFormat') {
            if (record.value !== taskDocument || op !== 'call' || args[0] !== config.output) throw new Error('MAC_OUTPUT_PATH_MISMATCH');
          }
          if (member === 'Save') throw new Error('MAC_INPUT_SAVE_DENIED');
          if (member === 'Close' && (record.value !== taskDocument || op !== 'call' || args[0] !== 0))
            throw new Error('MAC_USER_DOCUMENT_CLOSE_DENIED');
          if (member === 'Quit' || member === 'Open' || member === 'Add') throw new Error('MAC_SESSION_MUTATION_DENIED');
        }
        var value;
        if (op === 'get') value = args.length === 0 ? record.value[member] : record.value[member].apply(record.value, args);
        else if (op === 'set' && scope === 'task' && args.length === 1) { record.value[member] = args[0]; value = undefined; }
        else if (op === 'call') {
          if (typeof record.value[member] !== 'function') throw new Error('MAC_METHOD_UNAVAILABLE:' + member);
          value = record.value[member].apply(record.value, args);
        } else throw new Error('MAC_OPERATION_INVALID');
        if (scope === 'application' && member === 'Documents') scope = 'documents';
        if (scope === 'application' && member === 'ActiveDocument') scope = 'task';
        if (scope === 'application' && member === 'Selection') scope = 'selection';
        if (record.scope === 'selection' && member === 'Document') {
          if (!value || String(value.FullName) !== ownedPath) throw new Error('MAC_SELECTION_DOCUMENT_SCOPE_MISMATCH');
          scope = 'task';
        }
        if (record.scope === 'documents' && member === 'Open') {
          if (!value || String(value.FullName) !== config.input) throw new Error('MAC_OPEN_RETURN_PATH_MISMATCH');
          taskDocument = value;
          scope = 'task';
        }
        receipt.result = encode(value, scope);
        receipt.args = command.args;
        if (member === 'SaveAs2' || member === 'SaveAs') ownedPath = config.output;
        if (member === 'Close' && record.scope === 'task') invalidateTask();
        return receipt;
      } catch (error) { receipt.error = String(error.message || error); return receipt; }
    };
  }

  if (typeof module !== 'undefined' && module.exports) { module.exports = createDispatcher; return; }
  // 以下仅为 QA 门槛加载诊断，回执不含 token、任务路径或文档正文。
  var state = {schema: 1, js_loaded: true, onload_observed: false, phase: 'script-loaded',
    xhr_available: typeof global.XMLHttpRequest === 'function',
    timer_available: typeof global.setTimeout === 'function', map_available: typeof Map === 'function',
    wps_api_available: !!(global.wps && typeof global.wps.WpsApplication === 'function'),
    commands_seen: 0, last_http_status: null, error_code: null};
  var factoryTypes = {bare_createobject_type: typeof CreateObject,
    global_createobject_type: typeof global.CreateObject,
    wps_createobject_type: global.wps ? typeof global.wps.CreateObject : 'undefined', factory_invoked: false};
  state.factory_types = factoryTypes;
  var active = false;
  var endpoint = global.PartyOpsMacEndpoint;
  state.endpoint_valid = !!(endpoint && /^http:\/\/127\.0\.0\.1:[0-9]+(?:\/p\/[A-Za-z0-9_-]{32,128})?$/.test(endpoint));
  function stop(code) { active = false; state.phase = 'stopped'; state.error_code = code; }
  // 仅首次product bootstrap失败的脱敏回执；不发送正文、路径、nonce或原始异常。
  function initializationFailure(stage,error,diagnostic) {
    var codes=['MAC_BOOTSTRAP_SCHEMA_INVALID','MAC_DUAL_BOOTSTRAP_BINDING_MISMATCH','MAC_PRODUCT_CLOSE_EVENT_UNAVAILABLE','MAC_LEASE_FACTORY_UNAVAILABLE'];
    var code=codes.indexOf(error && error.message)>=0?error.message:'MAC_BOOTSTRAP_OR_WPS_INITIALIZATION_FAILED';
    stop(code);diagnostic.stage=stage;diagnostic.error_code=code;
    diagnostic.error_type=['Error','TypeError','SyntaxError'].indexOf(error && error.name)>=0?error.name:'other';
    if(!/^http:\/\/127\.0\.0\.1:[0-9]+\/p\/[A-Za-z0-9_-]{32,128}$/.test(endpoint))return;
    try{var report=new global.XMLHttpRequest();report.open('POST',endpoint+'/initialization-error',true);report.setRequestHeader('Content-Type','application/json');report.send(JSON.stringify(diagnostic));}catch(ignored){}
  }
  function connect() {
    if (active) return;
    if (state.commands_seen) { state.error_code = 'MAC_SESSION_NOT_REPLAYABLE'; return; }
    if (!state.endpoint_valid) { stop('MAC_ENDPOINT_INVALID'); return; }
    if (!state.xhr_available || !state.timer_available || !state.map_available || !state.wps_api_available)
    { stop('MAC_INITIALIZATION_API_UNAVAILABLE'); return; }
    active = true;
    state.phase = 'bootstrap-pending';
    state.error_code = null;
    try {
      var bootstrap = new global.XMLHttpRequest();
      bootstrap.open('GET', endpoint + '/bootstrap', true);
      bootstrap.onload = function () {
        state.last_http_status = bootstrap.status;
        if (bootstrap.status !== 200) { stop('MAC_BOOTSTRAP_HTTP_REJECTED'); return; }
        var config, dispatch,stage='json',init={schema:1,lease_factory_type:typeof global.PartyOpsMacTaskLeaseDispatcher,app_obtained:false,api_event_available:false,add_listener_type:'undefined',wps_api_event_available:false,wps_add_listener_type:'undefined',event_source:'unavailable',registration_attempted:false,registration_returned:false};
        try {
          config = JSON.parse(bootstrap.responseText);
          stage='schema';
          if (config.schema !== 1 || !config.session) throw new Error('MAC_BOOTSTRAP_SCHEMA_INVALID');
          var dualConfig = config.mode === 'dual-instance-a-read-only' || config.mode === 'dual-instance-b-read-only' || config.mode === 'task-lease-format' || config.mode === 'task-lease-undo-domain' || config.mode === 'task-lease-undo-effect' || (config.mode === 'task-lease-range-primitive' || config.mode === 'task-lease-range-primitive-duplicate');
          stage='bindings';
          if (dualConfig && (config.endpoint !== endpoint || typeof config.generation !== 'string' || !config.generation))
          throw new Error('MAC_DUAL_BOOTSTRAP_BINDING_MISMATCH');
          stage='application';var application=global.wps.WpsApplication();init.app_obtained=!!application;
          stage='api-event';var api=application&&application.ApiEvent,wpsApi=global.wps&&global.wps.ApiEvent;init.api_event_available=api!==null&&api!==undefined;init.add_listener_type=api?typeof api.AddApiEventListener:'undefined';
          init.wps_api_event_available=wpsApi!==null&&wpsApi!==undefined;init.wps_add_listener_type=wpsApi?typeof wpsApi.AddApiEventListener:'undefined';
          init.event_source=api&&typeof api.AddApiEventListener==='function'?'application':(api===null||api===undefined)&&wpsApi&&typeof wpsApi.AddApiEventListener==='function'?'wps-global':'unavailable';
          stage='lease-factory';
          if(config.product_task){config.initialization_diagnostic=init;if(typeof global.PartyOpsMacTaskLeaseDispatcher!=='function')throw new Error('MAC_LEASE_FACTORY_UNAVAILABLE');}
          dispatch = createDispatcher(application, config, factoryTypes);
        } catch (error) { initializationFailure(stage,error,init); return; }
        state.phase = 'poll-pending';
        var bindingQuery = '?session=' + encodeURIComponent(config.session);
        if (dualConfig) bindingQuery += '&generation=' + encodeURIComponent(config.generation) + '&endpoint=' + encodeURIComponent(endpoint);
        function poll() {
          if (!active) return;
          try {
            var request = new global.XMLHttpRequest();
            request.open('GET', endpoint + '/poll' + bindingQuery, true);
            request.onload = function () {
              state.last_http_status = request.status;
              if (request.status !== 200) { stop('MAC_POLL_HTTP_REJECTED'); return; }
              var command;
              try { command = JSON.parse(request.responseText); }
              catch (error) { stop('MAC_POLL_RESPONSE_INVALID'); return; }
              state.phase = 'poll-connected';
              if (!command) { global.setTimeout(poll, 100); return; }
              state.commands_seen++;
              var receipt = dispatch(command);
              state.phase = 'result-pending';
              try {
                var response = new global.XMLHttpRequest();
                response.open('POST', endpoint + '/result' + bindingQuery, true);
                response.setRequestHeader('Content-Type', 'application/json');
                response.onload = function () {
                  state.last_http_status = response.status;
                  if (response.status === 200) { state.phase = 'poll-pending'; global.setTimeout(poll, 10); }
                  else stop('MAC_RESULT_HTTP_REJECTED');
                };
                response.onerror = function () { stop('MAC_RESULT_NETWORK_FAILED_NO_REPLAY'); };
                response.send(JSON.stringify(receipt));
              } catch (error) { stop('MAC_RESULT_SEND_FAILED_NO_REPLAY'); }
            };
            request.onerror = function () { stop('MAC_POLL_NETWORK_FAILED'); };
            request.send();
          } catch (error) { stop('MAC_POLL_INITIALIZATION_FAILED'); }
        }
        poll();
      };
      bootstrap.onerror = function () { stop('MAC_BOOTSTRAP_NETWORK_FAILED'); };
      bootstrap.send();
    } catch (error) { stop('MAC_BOOTSTRAP_REQUEST_FAILED'); }
  }
  global.MacGateInspect = function () { global.alert(JSON.stringify(state)); };
  global.MacGateConnect = function () { connect(); global.alert(JSON.stringify(state)); };
  global.OnAddInLoad = function () { state.onload_observed = true; connect(); };
  global.PartyOpsMacGateDiagnostic = state;
})(this);
