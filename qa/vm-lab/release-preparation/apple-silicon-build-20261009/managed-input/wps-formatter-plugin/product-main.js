/* 固定产品加载页：从真实载体取得一次性私有能力，不公开当前任务。 */
(function (global) {
  'use strict';
  var origin='http://127.0.0.1:18974', initialized=false, application, events, current=null, seen=new Set();
  var eventInfo;
  function retire(task) { if(!task||task.stopped)return;task.stopped=true;if(task.dispatch&&typeof task.dispatch.retire==='function')task.dispatch.retire(); }
  function report(task,stage,error,info) {
    var codes=['MAC_BOOTSTRAP_SCHEMA_INVALID','MAC_DUAL_BOOTSTRAP_BINDING_MISMATCH','MAC_PRODUCT_CLOSE_EVENT_UNAVAILABLE','MAC_LEASE_FACTORY_UNAVAILABLE'];
    var code=error&&error.message;
    var body=Object.assign({},info,{schema:1,stage:stage,error_code:codes.indexOf(code)>=0?code:'MAC_BOOTSTRAP_OR_WPS_INITIALIZATION_FAILED',error_type:error&&['Error','TypeError','SyntaxError'].indexOf(error.name)>=0?error.name:'other'});
    try { var request=new global.XMLHttpRequest();request.open('POST',task.endpoint+'/initialization-error',true);request.setRequestHeader('Content-Type','application/json');request.send(JSON.stringify(body)); } catch(ignored){}
    retire(task);
  }
  function discover(doc) {
    // 事件与首次加载都只从原生文档读取；不从网页参数或配置认领文档。
    var path;
    try {path=String(doc.FullName);}catch(error){return;}
    var match=/(?:^|[\/\\])partyops-carrier-([A-Za-z0-9_-]{32,128})\.docx$/.exec(path);
    if(!match||seen.has(match[1]))return;
    seen.add(match[1]);if(current)retire(current);
    var task={endpoint:origin+'/p/'+match[1],carrier:path,stopped:false,dispatch:null};current=task;
    var request=new global.XMLHttpRequest();request.open('GET',task.endpoint+'/bootstrap?carrier='+encodeURIComponent(path),true);
    request.onload=function(){
      if(task.stopped)return;
      if(request.status!==200){retire(task);return;}
      var config,stage='json',info=Object.assign({},eventInfo,{lease_factory_type:typeof global.PartyOpsMacTaskLeaseDispatcher,registration_attempted:false,registration_returned:false});
      try {
        config=JSON.parse(request.responseText);stage='schema';
        if(config.schema!==1||!config.session||!config.product_task||config.mode!=='task-lease-format')throw new Error('MAC_BOOTSTRAP_SCHEMA_INVALID');
        stage='bindings';if(config.endpoint!==task.endpoint||config.carrier!==path||typeof config.generation!=='string'||!config.generation)throw new Error('MAC_DUAL_BOOTSTRAP_BINDING_MISMATCH');
        stage='api-event';if(!events||typeof events.AddApiEventListener!=='function'||!initialized)throw new Error('MAC_PRODUCT_CLOSE_EVENT_UNAVAILABLE');
        stage='lease-factory';if(typeof global.PartyOpsMacTaskLeaseDispatcher!=='function')throw new Error('MAC_LEASE_FACTORY_UNAVAILABLE');
        config.initialization_diagnostic=info;task.dispatch=global.PartyOpsMacTaskLeaseDispatcher(application,config,global.wps);
      }catch(error){report(task,stage,error,info);return;}
      var binding='?session='+encodeURIComponent(config.session)+'&generation='+encodeURIComponent(config.generation)+'&endpoint='+encodeURIComponent(task.endpoint);
      function poll(){
        if(task.stopped)return;
        var next=new global.XMLHttpRequest();next.open('GET',task.endpoint+'/poll'+binding,true);
        next.onload=function(){
          if(task.stopped)return;if(next.status!==200){retire(task);return;}
          var command;try{command=JSON.parse(next.responseText);}catch(error){retire(task);return;}
          if(!command){global.setTimeout(poll,100);return;}
          var receipt=task.dispatch(command),reply=new global.XMLHttpRequest();reply.open('POST',task.endpoint+'/result'+binding,true);reply.setRequestHeader('Content-Type','application/json');
          reply.onload=function(){if(task.stopped)return;if(reply.status!==200){retire(task);return;}if(command.operation==='inspect'&&command.member==='LeaseFinish'&&!receipt.error){retire(task);return;}global.setTimeout(poll,0);};
          reply.onerror=function(){retire(task);};try{reply.send(JSON.stringify(receipt));}catch(error){retire(task);}
        };
        next.onerror=function(){retire(task);};try{next.send();}catch(error){retire(task);}
      }
      poll();
    };
    request.onerror=function(){retire(task);};try{request.send();}catch(error){retire(task);}
  }
  global.OnAddInLoad=function(){
    if(initialized)return true;
    try {
      application=global.wps.WpsApplication();var appEvents=application&&application.ApiEvent,wpsEvents=global.wps&&global.wps.ApiEvent;
      events=appEvents===null||appEvents===undefined?wpsEvents:appEvents;
      eventInfo={app_obtained:!!application,api_event_available:appEvents!==null&&appEvents!==undefined,add_listener_type:appEvents?typeof appEvents.AddApiEventListener:'undefined',wps_api_event_available:wpsEvents!==null&&wpsEvents!==undefined,wps_add_listener_type:wpsEvents?typeof wpsEvents.AddApiEventListener:'undefined',event_source:events&&typeof events.AddApiEventListener==='function'?(appEvents===null||appEvents===undefined?'wps-global':'application'):'unavailable'};
      if(!events||typeof events.AddApiEventListener!=='function'){discover(application.ActiveDocument);return true;}
      // 第二任务需要真实DocumentOpen；注册异常保持未就绪，不能假装每任务重载。
      events.AddApiEventListener('DocumentOpen',function(doc){discover(doc);});initialized=true;
      discover(application.ActiveDocument);
    }catch(error){if(application){try{discover(application.ActiveDocument);}catch(ignored){}}}
    return true;
  };
})(this);
