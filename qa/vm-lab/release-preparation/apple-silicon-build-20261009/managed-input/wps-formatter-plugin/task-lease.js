/* 独立任务租约：同步检查真实对象归属后转发原C#调用，无排版规则。 */
(function (global) {
  'use strict';
  function create(application, config, wpsRuntime) {
    var records = new Map([[1, {value: application, scope: 'application'}]]), ids = new Map([[application, 1]]), readonlyIds = new Map();
    var nextId = 2, lastId = 0, leased = false, finished = false, task = null, closeUnknown = false, nativeUndo = null, nativeUndoBinding = null, taskCycle = 0, cycleClosed = false, taskAnchor = null;
    var work = config.output, taskPath = work, artifacts = new Set([work]);
    // 正式导出只容许一个由本次Add返回的伴随文档；身份不由文件名认领。
    var companion=null,companionUsed=false,companionClosed=false,companionUnknown=false,companionPath=null,companionAnchor=null,exportXml=null,exportInserted=false,exportSaved=false;
    var reservedPaths=new Set([String(config.input).toLowerCase(),String(config.carrier).toLowerCase(),String(work).toLowerCase()]);
    var productLifecycle={listener_registered:false,close_events:0,external_close_detected:false},productClosing=null,callbackRetired=false,bootstrapClosed=false,emptySessionEligible=true;
    var domain = {schema:1,attempted:false,passed:false,getter_calls:0,start_calls:0,end_calls:0,doc_undo_calls:0,close_calls:0,unknown:false,cleanup_confirmed:false,lease_released:false,rollback_capability_verified:false,semantic_identity_accepted:false,observations:[]};
    var undo = {native_getter_calls:0,native_object_verified:false,lease_started:false,active:false,end_confirmed:false,unknown:false,preexisting_record:false,start_calls:0,end_calls:0,undo_calls:0,native_level:null,native_recording:null,rollback_capability_verified:false};
    var effect={schema:1,attempted:false,start_calls:0,font_write_calls:0,end_calls:0,doc_undo_calls:0,close_calls:0,unknown:false,effect_observed:false,cleanup_confirmed:false,lease_released:false,rollback_capability_verified:false,history_top_ownership_verified:false,document_cycle_identity_proven:false,observations:[]},effectUndo=null;
    // 独立QA原语模式：逐次真实调用，首次偏差锁止；绝不保存、撤销或关闭现场。
    var isDuplicatePrimitive=config.mode==='task-lease-range-primitive-duplicate',isPrimitive=config.mode==='task-lease-range-primitive'||isDuplicatePrimitive;
    var primitive={schema:1,phase:0,locked:false,passed:false,setup_calls:0,content_write_calls:0,observations:[],cleanup_confirmed:false,lease_released:false},primitiveRanges={},primitiveIds={},primitiveBaseline=null,primitiveExpected=null,primitiveExpectedRanges={};
    var globalReads = ['Name', 'Version', 'Path', 'Documents', 'ActiveDocument', 'Selection', 'ActiveWindow', 'ScreenUpdating', 'DisplayAlerts', 'EnableEvents', 'UndoRecord'];
    // 仅原单format需要的文档子接口；Template/未知对象不从父对象继承写权限。
    var taskTypes = ['Document','Range','Selection','Window','View','Pane','Panes','Paragraphs','Paragraph','Font','ParagraphFormat','PageSetup','Styles','Style','Tables','Table','Rows','Row','Cells','Cell','Columns','Column','Sections','Section','HeadersFooters','HeaderFooter','PageNumbers','Fields','Field','Bookmarks','Bookmark','TabStops','TabStop','Shading','Borders','Border','Find','Replacement','InlineShapes','InlineShape','Shapes','Shape','TextFrame','LineFormat','FillFormat','ColorFormat','PictureFormat','TextColumns','TextColumn','ListFormat'];
    var snapshotCollections={Hyperlinks:['Count','Item'],Comments:['Count'],ContentControls:['Count'],StoryRanges:['Item'],ShapeRange:['Count']};
    var calls = ['Range', 'SetRange', 'Select', 'Collapse', 'Move', 'MoveStart', 'MoveEnd', 'HomeKey', 'EndKey', 'GoTo', 'Delete', 'InsertBefore', 'InsertAfter', 'InsertParagraphBefore', 'InsertParagraphAfter', 'InsertBreak', 'Find', 'Execute', 'Exists', 'ComputeStatistics', 'Repaginate', 'ConvertToText', 'ConvertToTable', 'AutoFitBehavior', 'DistributeWidth', 'ClearAll', 'Update', 'Unlink', 'ScaleHeight', 'ScaleWidth', 'Add', 'AddPicture', 'Save', 'Close', 'ConvertNumbersToText', 'ClearFormatting', 'Reset'];
    if(config.product_task){
      // Mac真实关闭实验已验证global入口；仅应用入口真缺失才使用实际wps闭包。
      var applicationEvents=application.ApiEvent,events=applicationEvents,eventSource='application';
      if(applicationEvents===null||applicationEvents===undefined){events=wpsRuntime&&wpsRuntime.ApiEvent;eventSource='wps-global';}
      if(config.mode!=='task-lease-format'||!events||typeof events.AddApiEventListener!=='function')throw new Error('MAC_PRODUCT_CLOSE_EVENT_UNAVAILABLE');
      productLifecycle.event_source=eventSource;
      if(config.initialization_diagnostic){productLifecycle.application_api_event_available=config.initialization_diagnostic.api_event_available;productLifecycle.application_add_listener_type=config.initialization_diagnostic.add_listener_type;productLifecycle.wps_api_event_available=config.initialization_diagnostic.wps_api_event_available;productLifecycle.wps_add_listener_type=config.initialization_diagnostic.wps_add_listener_type;config.initialization_diagnostic.registration_attempted=true;}
      events.AddApiEventListener('DocumentBeforeClose',function(doc){
        if(callbackRetired)return;
        if(bootstrapClosed&&!leased&&!finished&&productClosing===null){productLifecycle.external_close_detected=true;undo.invalidated=true;}
        try{var path=String(doc.FullName);if(productClosing===path){productLifecycle.close_events++;return;}if((task&&path===taskPath)||(companion&&path===companionPath)){productLifecycle.external_close_detected=true;undo.invalidated=true;}}
        catch(error){productLifecycle.external_close_detected=true;undo.invalidated=true;}
      });productLifecycle.listener_registered=true;if(config.initialization_diagnostic)config.initialization_diagnostic.registration_returned=true;
    }
    function productClose(doc,operation){
      if(!config.product_task)return operation();
      var before=productLifecycle.close_events;productClosing=String(doc.FullName);
      try{var result=operation();if(productLifecycle.close_events!==before+1)throw new Error('MAC_PRODUCT_CLOSE_EVENT_UNCONFIRMED');return result;}
      finally{productClosing=null;}
    }
    function snapshot() {
      var docs = application.Documents, count = Number(docs.Count), paths = [];
      if (!Number.isInteger(count) || count < 0) throw new Error('MAC_LEASE_COUNT_INVALID');
      for (var i = 1; i <= count; i++) paths.push(String(docs.Item(i).FullName));
      if (Number(docs.Count) !== count) throw new Error('MAC_LEASE_COLLECTION_CHANGED');
      return paths;
    }
    function loseBoundary(code) { undo.invalidated=true;throw new Error(code); }
    function verifyDocumentAnchor() {
      if(config.mode!=='task-lease-format'&&config.mode!=='task-lease-undo-effect'&&!isPrimitive)return;
      try {
        var doc=taskAnchor&&taskAnchor.Document,start=taskAnchor&&taskAnchor.Start,end=taskAnchor&&taskAnchor.End;
        var limit=task.Content.End,activeLimit=application.ActiveDocument.Content.End,selectionLimit=application.Selection.Document.Content.End;
        if(!doc||String(doc.FullName)!==taskPath||!Number.isInteger(start)||!Number.isInteger(end)||!Number.isInteger(limit)||start<0||end<start||end>limit||doc.Content.End!==limit||activeLimit!==limit||selectionLimit!==limit)throw new Error('invalid anchor');
        undo.fixed_document_anchor_validated=true;undo.document_cycle_identity_proven=false;
      }catch(error){loseBoundary('MAC_LEASE_DOCUMENT_ANCHOR_UNAVAILABLE');}
    }
    function guard(context, confirmingUndo) {
      try {
      if (!leased || finished || closeUnknown || companionUnknown) throw new Error('MAC_LEASE_UNAVAILABLE');
      if((undo.unknown&&!confirmingUndo) || undo.preexisting_record || undo.rollback_unproven || undo.invalidated) throw new Error('MAC_UNDO_BOUNDARY_UNCONFIRMED');
      var paths = snapshot();
      if (!task) { if (paths.length) loseBoundary('MAC_LEASE_FOREIGN_DOCUMENT'); return; }
      if (String(task.FullName) !== taskPath) loseBoundary('MAC_LEASE_TASK_PATH_CHANGED');
      if(companion){
        if(String(companion.FullName)!==companionPath||paths.length!==2||paths.filter(function(p){return p===taskPath;}).length!==1||paths.filter(function(p){return p===companionPath;}).length!==1||taskPath===companionPath)loseBoundary('MAC_LEASE_FOREIGN_DOCUMENT');
        var ca=companionAnchor,ta=taskAnchor;
        if(!ca||!ta||String(ca.Document.FullName)!==companionPath||String(ta.Document.FullName)!==taskPath||!Number.isInteger(ca.Start)||!Number.isInteger(ca.End)||ca.Start<0||ca.End<ca.Start||ca.End>companion.Content.End||!Number.isInteger(ta.End)||ta.End>task.Content.End)loseBoundary('MAC_LEASE_DOCUMENT_ANCHOR_UNAVAILABLE');
        if(context&&(!application.ActiveDocument||String(application.ActiveDocument.FullName)!==companionPath||!application.Selection||!application.Selection.Document||String(application.Selection.Document.FullName)!==companionPath||application.ActiveDocument.Content.End!==companion.Content.End||application.Selection.Document.Content.End!==companion.Content.End))loseBoundary('MAC_LEASE_ACTIVE_CONTEXT_CHANGED');
        var cp=snapshot();if(cp.length!==2||cp.indexOf(taskPath)<0||cp.indexOf(companionPath)<0)loseBoundary('MAC_LEASE_FOREIGN_DOCUMENT');return;
      }
      if (paths.length !== 1 || paths[0] !== taskPath) loseBoundary('MAC_LEASE_FOREIGN_DOCUMENT');
      if (context && (!application.ActiveDocument || String(application.ActiveDocument.FullName) !== taskPath
          || !application.Selection || !application.Selection.Document || String(application.Selection.Document.FullName) !== taskPath))
        loseBoundary('MAC_LEASE_ACTIVE_CONTEXT_CHANGED');
      if(context)verifyDocumentAnchor();
      var after = snapshot();
      if (after.length !== 1 || after[0] !== taskPath) loseBoundary('MAC_LEASE_FOREIGN_DOCUMENT');
      }catch(error){if(leased)undo.invalidated=true;throw error;}
    }
    function invalidate(id) { var record = records.get(id); if (record) { (record.scope==='snapshot-read-only'?readonlyIds:ids).delete(record.value); records.delete(id); } }
    function expireTask() { records.forEach(function (r, id) { if (r.scope === 'task'||r.scope==='undo'||r.scope==='snapshot-read-only') invalidate(id); }); task = null;taskAnchor=null;cycleClosed=true; }
    function closeCompanion(){
      guard(true);if(!companion||companionClosed||companionUnknown)throw new Error('MAC_LEASE_EXPORT_CLOSE_UNAVAILABLE');
      // 已确认原记录空闲；不对伴随文档建立或接管Undo记录。
      if(undo.active||undo.unknown||undo.invalidated)throw new Error('MAC_UNDO_EXIT_UNCONFIRMED');
      var idle=undoState(application.UndoRecord);if(idle.level!==0||idle.recording||idle.name!=='')loseBoundary('MAC_UNDO_EXIT_UNCONFIRMED');
      companionUnknown=true;var closing=companion;productClose(closing,function(){closing.Close(0);});
      var left=snapshot();if(left.length!==1||left[0]!==taskPath)throw new Error('MAC_LEASE_EXPORT_CLOSE_UNCONFIRMED');
      companion=null;companionAnchor=null;companionClosed=true;companionUnknown=false;
      records.forEach(function(r,id){if(r.scope==='export-task')invalidate(id);});guard(true);
    }
    function applicationAlias(value, member, type) {
      if (value === application || member === 'Application' || /\.(?:_?Application)$/.test(type || '')) return true;
      // Parent可能返回新的全局Application wrapper，不能从原父对象继承task权限。
      try { return !!value && typeof value.Name === 'string' && !!value.Documents && typeof value.Documents.Open === 'function' && typeof value.Quit === 'function'; }
      catch (error) { throw new Error('MAC_LEASE_ALIAS_UNVERIFIABLE'); }
    }
    function ownership(record) {
      if(record.scope==='export-task'){
        try{if(!companion||companionClosed||companionUnknown||record.owner!==companionPath)throw new Error('MAC_LEASE_OBJECT_EXPIRED');
          if(record.type==='Document'){if(record.value!==companion||String(record.value.FullName)!==companionPath)throw new Error('MAC_LEASE_OBJECT_FOREIGN');}
          else if(record.type==='Range'){if(!record.value.Document||String(record.value.Document.FullName)!==companionPath)throw new Error('MAC_LEASE_OBJECT_FOREIGN');}
          else throw new Error('MAC_LEASE_EXPORT_OBJECT_DENIED');
          if(record.parent){var ep=records.get(record.parent);if(!ep||ep.scope!=='export-task')throw new Error('MAC_LEASE_PARENT_EXPIRED');ownership(ep);}return;
        }catch(error){undo.invalidated=true;throw error;}
      }
      if (record.scope !== 'task' && record.scope !== 'undo' && record.scope !== 'snapshot-read-only') return;
      try {
      if (!task || record.owner !== taskPath) throw new Error('MAC_LEASE_OBJECT_EXPIRED');
      var value = record.value, doc;
        if(record.type==='DocumentProperties'||record.type==='DocumentProperty'){
          var propertyParent=value.Parent;
          if(propertyParent){
            var propertyDocument=propertyParent.FullName!==undefined?propertyParent:propertyParent.Parent;
            if(!propertyDocument||String(propertyDocument.FullName)!==taskPath)throw new Error('MAC_LEASE_PROPERTY_OWNER_UNVERIFIABLE');
          }
        }
        if(record.type==='Style'&&record.scope==='snapshot-read-only'){
          var styleParent=value.Parent;
          if(typeof value.NameLocal!=='string'||!styleParent||String(styleParent.FullName)!==taskPath)throw new Error('MAC_LEASE_STYLE_OWNER_UNVERIFIABLE');
        }
      if (record.type === 'Selection' || record.type === 'Range' || record.type === 'Window') {
        doc = value.Document;
        if (!doc || String(doc.FullName) !== taskPath) throw new Error('MAC_LEASE_OBJECT_FOREIGN');
      } else if (record.type === 'Document') {
        if (String(value.FullName) !== taskPath) throw new Error('MAC_LEASE_OBJECT_FOREIGN');
      }
      else if (value.Range && value.Range.Document && String(value.Range.Document.FullName) !== taskPath) throw new Error('MAC_LEASE_OBJECT_FOREIGN');
      if(record.type==='Hyperlink'&&(!value.Range||!value.Range.Document||String(value.Range.Document.FullName)!==taskPath))throw new Error('MAC_LEASE_OBJECT_FOREIGN');
      // 原集合从已确认父Range/Document的真实getter取得；可见Parent若指向其他文档即拒绝。
      if(snapshotCollections[record.type]&&value.Parent){var actualParent=value.Parent;if(actualParent.FullName!==undefined&&String(actualParent.FullName)!==taskPath)throw new Error('MAC_LEASE_OBJECT_FOREIGN');if(actualParent.Document&&String(actualParent.Document.FullName)!==taskPath)throw new Error('MAC_LEASE_OBJECT_FOREIGN');}
      // Font/ParagraphFormat等无Document接口的子对象仅来自已经核定父对象的访问链。
      if (record.parent) { var parent = records.get(record.parent); if (!parent) throw new Error('MAC_LEASE_PARENT_EXPIRED'); ownership(parent); }
      }catch(error){undo.invalidated=true;throw error;}
    }
    function undoState(value) {
      var level=value.CustomRecordLevel, recording=value.IsRecordingCustomRecord, name=value.CustomRecordName;
      if(typeof level!=='number'||!Number.isInteger(level)||level<0||typeof recording!=='boolean'||typeof name!=='string'||name.length>64) throw new Error('MAC_UNDO_STATE_API_UNAVAILABLE');
      return {level:level,recording:recording,name:name};
    }
    function verifyUndo(value, confirmingUndo) {
      try {
        guard(true,confirmingUndo);
        if(!value||typeof value.StartCustomRecord!=='function'||typeof value.EndCustomRecord!=='function')throw new Error('MAC_UNDO_NATIVE_API_UNAVAILABLE');
        if(nativeUndoBinding&&(nativeUndoBinding.cycle!==taskCycle||nativeUndoBinding.generation!==config.generation||nativeUndoBinding.session!==config.session||nativeUndoBinding.endpoint!==config.endpoint))throw new Error('MAC_UNDO_OPEN_CYCLE_OR_GENERATION_CHANGED');
        if(!task&&(!cycleClosed||!nativeUndoBinding))throw new Error('MAC_UNDO_OPEN_CYCLE_UNVERIFIED');
        undo.native_getter_calls++;var fresh=application.UndoRecord;
        if(!fresh||typeof fresh.StartCustomRecord!=='function'||typeof fresh.EndCustomRecord!=='function')throw new Error('MAC_UNDO_NATIVE_API_UNAVAILABLE');
        var state=undoState(value), held=nativeUndo?undoState(nativeUndo):state, current=undoState(fresh);
        guard(true,confirmingUndo);
        if(state.level!==held.level||state.recording!==held.recording||state.name!==held.name||state.level!==current.level||state.recording!==current.recording||state.name!==current.name)throw new Error('MAC_UNDO_WRAPPER_STATE_DIVERGED');
        if(!undo.lease_started){undo.initial_level=state.level;undo.initial_recording=state.recording;if(state.level!==0||state.recording){undo.preexisting_record=true;throw new Error('MAC_UNDO_PREEXISTING_RECORD');}}
        else if(undo.active && (state.level!==1||!state.recording))throw new Error('MAC_UNDO_ACTIVE_STATE_CHANGED');
        else if(!undo.active && (state.level!==0||state.recording))throw new Error('MAC_UNDO_IDLE_STATE_CHANGED');
        if(state.name!==(undo.active?undo.record_name:''))throw new Error('MAC_UNDO_RECORD_NAME_CHANGED');
        if(!nativeUndo){nativeUndo=value;nativeUndoBinding={cycle:taskCycle,generation:config.generation,session:config.session,endpoint:config.endpoint};}
        undo.native_object_verified=true;undo.native_level=state.level;undo.native_recording=state.recording;undo.native_name_matches_expected=true;undo.native_state_observed_at_utc=new Date().toISOString();undo.task_open_cycle=taskCycle;undo.app_generation_bound=true;undo.wrapper_cross_checks=(undo.wrapper_cross_checks||0)+1;undo.last_wrapper_reference_equal=value===nativeUndo;undo.last_fresh_reference_equal=fresh===nativeUndo;
      }catch(error){undo.invalidated=true;throw error;}
    }
    function verifyIdleUndo() {
      if(config.product_task&&task&&!nativeUndo)verifyUndo(application.UndoRecord);
      if(!undo.native_object_verified||!nativeUndo)throw new Error('MAC_UNDO_EXIT_STATE_UNVERIFIED');
      undo.native_getter_calls++;var current=application.UndoRecord;
      verifyUndo(current);var state=undoState(current);
      if(state.level!==0||state.recording||state.name!=='')loseBoundary('MAC_UNDO_EXIT_UNCONFIRMED');guard(true);
    }
    function encode(value, scope, member, type, parent) {
      if(scope==='undo'&&member==='UndoRecord')verifyUndo(value);
      // 原标点保护检查需要真实Range.ShapeRange.Count；未知对象不得合成空集合。
      if(type==='Microsoft.Office.Interop.Word.ShapeRange'&&member==='ShapeRange'&&(scope==='task'||scope==='snapshot-read-only')&&(!value||(typeof value!=='object'&&typeof value!=='function')))throw new Error('MAC_LEASE_SHAPE_RANGE_API_UNVERIFIABLE');
      if(type==='Microsoft.Office.Interop.Word.PageNumbers'&&member==='PageNumbers'&&(scope==='task'||scope==='snapshot-read-only')&&!value)throw new Error('MAC_LEASE_PAGE_NUMBERS_API_UNVERIFIABLE');
      if (value === undefined || value === null) return {kind: 'value', value: null};
      if (typeof value !== 'object' && typeof value !== 'function') return {kind: 'value', value: value};
      var shortType = (type || '').split('.').pop().replace(/^_/, '');
      if (applicationAlias(value, member, type)) scope = 'application';
      else if (shortType === 'Documents') scope = 'documents';
      else if(scope==='export-task'){
        var exportOrigin=records.get(parent);
        if(shortType==='Document'){if(value!==companion||member!=='Add'||!exportOrigin||exportOrigin.scope!=='documents')throw new Error('MAC_LEASE_EXPORT_OBJECT_DENIED');}
        else if(shortType==='Range'){if(member!=='Content'||!exportOrigin||exportOrigin.type!=='Document'||exportOrigin.scope!=='export-task'||!value.Document||String(value.Document.FullName)!==companionPath)throw new Error('MAC_LEASE_EXPORT_OBJECT_DENIED');}
        else throw new Error('MAC_LEASE_EXPORT_OBJECT_DENIED');
      }
      else if(shortType==='UndoRecord'){if(scope!=='undo')throw new Error('MAC_UNDO_SCOPE_INVALID');if(member!=='UndoRecord')verifyUndo(value);}
      else if (scope === 'task'||scope==='snapshot-read-only') {
        var origin=records.get(parent);
        if(shortType==='ShapeRange'){
          if(!origin||origin.type!=='Range'||member!=='ShapeRange')throw new Error('MAC_LEASE_CHILD_TYPE_UNVERIFIABLE');
          ownership(origin);
          scope='snapshot-read-only';
        }
        // 原恢复指纹动态object只读集合/Item路径，不把任意Object放入任务写范围。
        if(shortType==='Object'&&origin&&origin.type==='Document'&&member==='CustomDocumentProperties'){
          if(!Number.isInteger(value.Count)||value.Count<0||typeof value.Item!=='function')throw new Error('MAC_LEASE_PROPERTY_API_UNVERIFIABLE');
          shortType='DocumentProperties';scope='snapshot-read-only';
        }else if(shortType==='Object'&&origin&&origin.type==='DocumentProperties'&&member==='Item'){
          if(typeof value.Name!=='string'||!Number.isInteger(value.Type))throw new Error('MAC_LEASE_PROPERTY_API_UNVERIFIABLE');
          shortType='DocumentProperty';scope='snapshot-read-only';
        }
        if(shortType==='DocumentProperties'&&(!origin||origin.type!=='Document'||member!=='CustomDocumentProperties'))throw new Error('MAC_LEASE_CHILD_TYPE_UNVERIFIABLE');
        if(shortType==='DocumentProperty'&&(!origin||origin.type!=='DocumentProperties'||member!=='Item'))throw new Error('MAC_LEASE_CHILD_TYPE_UNVERIFIABLE');
        if(shortType==='DocumentProperties'||shortType==='DocumentProperty')scope='snapshot-read-only';
        // Range/Paragraph.Style的原interop返回object；仅真实任务父链和文档Parent，且只读。
        if(shortType==='Object'&&member==='Style'&&origin&&['Range','Paragraph'].indexOf(origin.type)>=0){
          if(typeof value.NameLocal!=='string'||!value.Parent||String(value.Parent.FullName)!==taskPath)throw new Error('MAC_LEASE_STYLE_OWNER_UNVERIFIABLE');
          shortType='Style';scope='snapshot-read-only';
        }
        if(shortType==='ListFormat'&&(!origin||origin.type!=='Range'||member!=='ListFormat'))throw new Error('MAC_LEASE_CHILD_TYPE_UNVERIFIABLE');
        if(shortType==='PageNumbers'){
          if(!origin||origin.type!=='HeaderFooter'||member!=='PageNumbers')throw new Error('MAC_LEASE_CHILD_TYPE_UNVERIFIABLE');
          ownership(origin);
          if(typeof value.RestartNumberingAtSection!=='boolean'||!Number.isInteger(value.StartingNumber)||value.StartingNumber<-2147483648||value.StartingNumber>2147483647)throw new Error('MAC_LEASE_PAGE_NUMBERS_API_UNVERIFIABLE');
        }
        if(shortType==='Find'&&(!origin||origin.type!=='Range'||member!=='Find'))throw new Error('MAC_LEASE_CHILD_TYPE_UNVERIFIABLE');
        if(shortType==='Replacement'&&(!origin||origin.type!=='Find'||member!=='Replacement'))throw new Error('MAC_LEASE_CHILD_TYPE_UNVERIFIABLE');
        if(snapshotCollections[shortType]){
          if(!origin||['Document','Range'].indexOf(origin.type)<0||member!==shortType)throw new Error('MAC_LEASE_CHILD_TYPE_UNVERIFIABLE');scope='snapshot-read-only';
        }else if(shortType==='Hyperlink'){
          if(!origin||origin.type!=='Hyperlinks'||member!=='Item'||!value.Range||!value.Range.Document||String(value.Range.Document.FullName)!==taskPath)throw new Error('MAC_LEASE_OBJECT_FOREIGN');scope='snapshot-read-only';
        }
        if (member === 'Parent' && shortType === 'Object') {
          if (value.FullName !== undefined) shortType = 'Document';
          else if (value.Document) shortType = 'Range';
          else throw new Error('MAC_LEASE_PARENT_UNVERIFIABLE');
        }
        if(taskTypes.indexOf(shortType)<0&&!snapshotCollections[shortType]&&['Hyperlink','DocumentProperties','DocumentProperty'].indexOf(shortType)<0)throw new Error('MAC_LEASE_CHILD_TYPE_UNVERIFIABLE');
        if (shortType === 'Document' && String(value.FullName) !== taskPath) throw new Error('MAC_LEASE_OBJECT_FOREIGN');
        if (['Range', 'Selection', 'Window'].indexOf(shortType) >= 0 && (!value.Document || String(value.Document.FullName) !== taskPath)) throw new Error('MAC_LEASE_OBJECT_FOREIGN');
      }
      // 同一原生对象的只读路径保留独立句柄，不能复用已有task写权限或全局降级它。
      var identityMap=scope==='snapshot-read-only'?readonlyIds:ids,id = identityMap.get(value);
      // 已登记全局别名权限只可收紧，不能由子对象路径升级成task。
      if (id && scope === 'application') records.get(id).scope = 'application';
      if (!id) { id = nextId++; identityMap.set(value, id); records.set(id, {value: value, scope: scope, type: shortType, member:member, owner: scope==='export-task'?companionPath:scope === 'task'||scope==='undo'||scope==='snapshot-read-only' ? taskPath : null, parent: scope==='snapshot-read-only'||((scope === 'task'||scope==='export-task') && shortType !== 'Document') ? parent : null});ownership(records.get(id)); }
      var wire={kind: 'object', id: id};
      // 身份仅是经同步归属检查的QA周期标记，不代替句柄权限或原生永久ID。
      if(shortType==='Document' && (type==='Microsoft.Office.Interop.Word.Document'||type==='Microsoft.Office.Interop.Word._Document')) {
        var source=records.get(parent), provenance=null;
        if(member==='Open'&&source&&source.scope==='documents')provenance='open';
        else if(member==='Add'&&source&&source.scope==='documents'&&scope==='export-task')provenance='export-add';
        else if(member==='Document'&&source&&['Range','Selection','Window'].indexOf(source.type)>=0){ownership(source);provenance='parent-document';}
        else if(member==='ActiveDocument'&&source&&source.scope==='application')provenance='active-document';
        if(!provenance)throw new Error('MAC_LEASE_DOCUMENT_IDENTITY_SOURCE_INVALID');
        guard(true);ownership(records.get(id));
        wire.document_identity={schema:1,type:type,scope:scope,cycle:scope==='export-task'?2:taskCycle,generation:config.generation,source:provenance};
      }
      if(shortType==='Style'&&(type||'')==='System.Object')wire.runtime_type='Microsoft.Office.Interop.Word.Style';
      return wire;
    }
    function decode(wire) {
      if (wire.kind === 'missing') return undefined;
      if (wire.kind === 'value') return wire.value;
      if (wire.kind === 'object' && records.has(wire.id)) { var r = records.get(wire.id); ownership(r); return r.value; }
      throw new Error('MAC_LEASE_OBJECT_EXPIRED');
    }
    function primitiveObservation(label) {
      guard(true);var text=task.Content.Text;
      if(typeof text!=='string'||text.length>10000)throw new Error('MAC_RANGE_PRIMITIVE_TEXT_INVALID');
      var o={phase:label,doc_text:text,doc_text_length:text.length,ranges:{}};
      Object.keys(primitiveRanges).forEach(function(key){var r=primitiveRanges[key];if(!r.Document||String(r.Document.FullName)!==taskPath)throw new Error('MAC_RANGE_PRIMITIVE_FOREIGN');var start=r.Start,end=r.End,rt=r.Text;if(!Number.isInteger(start)||!Number.isInteger(end)||start<0||end<start||typeof rt!=='string')throw new Error('MAC_RANGE_PRIMITIVE_RANGE_INVALID');o.ranges[key]={handle:primitiveIds[key],start:start,end:end,text:rt};});
      primitive.observations.push(o);return o;
    }
    function primitiveRequire(o) {
      if(o.doc_text!==(primitiveExpected===null?primitiveBaseline:primitiveExpected))throw new Error('MAC_RANGE_PRIMITIVE_TEXT_DRIFT');
      Object.keys(primitiveExpectedRanges).forEach(function(key){var a=primitiveExpectedRanges[key],b=o.ranges[key];if(!b||a.start!==b.start||a.end!==b.end||a.text!==b.text)throw new Error('MAC_RANGE_PRIMITIVE_RANGE_DRIFT');});
      if(isDuplicatePrimitive&&primitive.phase===2&&o.phase!=='FormattedText-set'){
        ['reference','formatted','target'].forEach(function(key){var actual=primitiveFont(primitiveRanges[key]),expected=primitive[key==='formatted'?'reference_font':key+'_font'];if(!expected||actual.Name!==expected.Name||actual.NameFarEast!==expected.NameFarEast||actual.NameAscii!==expected.NameAscii)throw new Error('MAC_RANGE_PRIMITIVE_FONT_DRIFT');});
      }
      return o;
    }
    function primitiveFont(range){var f=range.Font,n=f.Name,e=f.NameFarEast,a=f.NameAscii;if(typeof n!=='string'||!n.trim()||n==='-1'||n==='9999999'||typeof e!=='string'||!e.trim()||typeof a!=='string')throw new Error('MAC_RANGE_PRIMITIVE_FONT_UNKNOWN');return {Name:n,NameFarEast:e,NameAscii:a};}
    function primitiveExpectedText(text) {
      primitiveExpected=text;
      Object.keys(primitiveExpectedRanges).forEach(function(key){var e=primitiveExpectedRanges[key];e.text=text.substring(e.start,e.end);});
    }
    function primitiveControl(command,record,args) {
      var op=command.operation,member=command.member;
      if(command.object===1&&op==='inspect'){
        if(member==='LeaseInspectRangePrimitive'&&args.length===0)return;
        if(primitive.locked)throw new Error('MAC_RANGE_PRIMITIVE_LOCKED');
        if(['LeaseAcquire','LeaseCloseBootstrap'].indexOf(member)>=0)return;
        if(member==='LeaseBeginRangePrimitive'&&args.length===2&&primitive.phase===0&&primitive.setup_calls===2){
          guard(true);var q=args[0],r=args[1],o=primitiveRequire(primitiveObservation('independence-verified'));
          if(!Number.isInteger(q)||!Number.isInteger(r)||q===r||!primitiveRanges.container||!primitiveRanges.target||!primitiveRanges.reference||primitiveRanges.container===primitiveRanges.target||primitiveRanges.container===primitiveRanges.reference||primitiveRanges.target===primitiveRanges.reference)throw new Error('MAC_RANGE_PRIMITIVE_ALIAS');
          if(o.doc_text!==primitiveBaseline||o.ranges.container.start!==0||o.ranges.container.text!==primitiveBaseline||o.ranges.target.start!==q||o.ranges.target.end!==q+1||o.ranges.target.text!==primitiveBaseline[q]||o.ranges.reference.start!==r||o.ranges.reference.end!==r+1||o.ranges.reference.text!==primitiveBaseline[r]||primitiveBaseline[q]!=='“')throw new Error('MAC_RANGE_PRIMITIVE_SETUP_MISMATCH');
          primitive.quote_index=q;primitive.reference_index=r;
          if(isDuplicatePrimitive&&(q!==208||r!==209||primitiveBaseline.length!==1503||primitiveBaseline[r]!=='组'))throw new Error('MAC_RANGE_DUPLICATE_FIXED_FIXTURE_MISMATCH');
          if(isDuplicatePrimitive)primitive.rhs_variant='reference.Duplicate';
          ['target','reference'].forEach(function(key){var f=primitiveRanges[key].Font,n=f.Name,e=f.NameFarEast,a=f.NameAscii;if(typeof n!=='string'||!n.trim()||n==='-1'||n==='9999999'||typeof e!=='string'||!e.trim())throw new Error('MAC_RANGE_PRIMITIVE_FONT_UNKNOWN');primitive[key+'_font']={Name:n,NameFarEast:e,NameAscii:typeof a==='string'?a:null};});
          primitive.phase=1;return;
        }
        throw new Error('MAC_RANGE_PRIMITIVE_MODE_DENIED');
      }
      if(primitive.locked)throw new Error('MAC_RANGE_PRIMITIVE_LOCKED');
      if(op==='get'&&args.length===0){
        if(isDuplicatePrimitive&&member==='FormattedText')throw new Error('MAC_RANGE_PRIMITIVE_MODE_DENIED');
        if(isDuplicatePrimitive&&member==='Duplicate'&&command.object!==primitiveIds.container){if(command.object!==primitiveIds.reference||primitive.phase!==1)throw new Error('MAC_RANGE_PRIMITIVE_SEQUENCE');primitiveRequire(primitiveObservation('before-reference-Duplicate'));}
        if(member==='FormattedText'&&(command.object!==primitiveIds.reference||primitive.phase!==1))throw new Error('MAC_RANGE_PRIMITIVE_SEQUENCE');
        if(member==='FormattedText')primitiveRequire(primitiveObservation('before-FormattedText-get'));
        if(member==='Duplicate'&&command.object===primitiveIds.container)primitiveRequire(primitiveObservation('before-Duplicate'));
        return;
      }
      if(op==='call'&&record.scope==='documents'&&member==='Open'&&!task)return;
      if(op==='call'&&member==='SetRange'&&record.type==='Range'&&args.length===2&&Number.isInteger(args[0])&&args[1]===args[0]+1){
        if(primitive.phase===0&&primitive.setup_calls<2&&command.object===(primitive.setup_calls===0?primitiveIds.target:primitiveIds.reference)){primitive.pending_before=primitiveRequire(primitiveObservation('before-initial-SetRange-'+(primitive.setup_calls+1)));primitive.setup_calls++;return;}
        if(command.object===primitiveIds.target&&args[0]===primitive.quote_index&&(primitive.phase===3||primitive.phase===5)){primitive.pending_before=primitiveRequire(primitiveObservation('before-reset-'+primitive.phase));return;}
      }
      if(op==='set'&&record.type==='Range'&&command.object===primitiveIds.target&&args.length===1){
        if(member==='FormattedText'&&primitive.phase===2&&args[0]===primitiveRanges.formatted){primitiveRequire(primitiveObservation('before-FormattedText-set'));return;}
        if(member==='Text'&&primitive.phase===4&&args[0]===primitiveBaseline[primitive.quote_index]){primitiveRequire(primitiveObservation('before-Text-set'));return;}
      }
      throw new Error('MAC_RANGE_PRIMITIVE_MODE_DENIED');
    }
    function primitiveAfter(command,value,wire) {
      var member=command.member,op=command.operation;
      if(op==='get'&&member==='Content'&&records.get(command.object).type==='Document'&&!primitiveRanges.container){primitiveRanges.container=value;primitiveIds.container=wire.id;primitiveBaseline=task.Content.Text;var o=primitiveObservation('container');primitiveExpectedRanges.container={start:o.ranges.container.start,end:o.ranges.container.end,text:primitiveBaseline};primitiveRequire(o);}
      if(op==='get'&&member==='Duplicate'&&command.object===primitiveIds.container){
        var key=!primitiveRanges.target?'target':!primitiveRanges.reference?'reference':null;
        if(!key||value===primitiveRanges.container||value===primitiveRanges.target)throw new Error('MAC_RANGE_PRIMITIVE_ALIAS');primitiveRanges[key]=value;primitiveIds[key]=wire.id;var e=primitiveExpectedRanges.container;primitiveExpectedRanges[key]={start:e.start,end:e.end,text:e.text};primitiveRequire(primitiveObservation('duplicate-'+key));
      }
      if(op==='call'&&member==='SetRange'){
        var o=primitiveObservation('after-SetRange');
        var moved=command.object===primitiveIds.target?'target':'reference';
        Object.keys(primitive.pending_before.ranges).forEach(function(key){if(key!==moved){var a=primitive.pending_before.ranges[key],b=o.ranges[key];if(a.start!==b.start||a.end!==b.end||a.text!==b.text)throw new Error('MAC_RANGE_PRIMITIVE_RANGE_LINKED');}});
        var requested=command.args.map(decode);primitiveExpectedRanges[moved]={start:requested[0],end:requested[1],text:(primitiveExpected===null?primitiveBaseline:primitiveExpected).substring(requested[0],requested[1])};primitiveRequire(o);
        if(primitive.phase===0){if(o.doc_text!==primitiveBaseline||o.ranges.container.text!==primitiveBaseline)throw new Error('MAC_RANGE_PRIMITIVE_SETUP_CHANGED_TEXT');}
        else if(primitive.phase===3||primitive.phase===5){if(o.doc_text!==(primitive.phase===3?primitiveExpected:primitiveBaseline))throw new Error('MAC_RANGE_PRIMITIVE_RESET_CHANGED_TEXT');primitive.phase++;if(o.ranges.target.start!==primitive.quote_index||o.ranges.target.end!==primitive.quote_index+1)throw new Error('MAC_RANGE_PRIMITIVE_RESET_MISMATCH');if(primitive.phase===6){if(o.doc_text!==primitiveBaseline)throw new Error('MAC_RANGE_PRIMITIVE_RESTORE_MISMATCH');if(isDuplicatePrimitive){primitive.final_target_font=primitiveFont(primitiveRanges.target);primitive.final_reference_font=primitiveFont(primitiveRanges.reference);var f=primitive.final_target_font,e=primitive.reference_font,r=primitive.final_reference_font;if(r.Name!==e.Name||r.NameFarEast!==e.NameFarEast||r.NameAscii!==e.NameAscii)throw new Error('MAC_RANGE_PRIMITIVE_REFERENCE_FONT_DRIFT');if(f.Name.trim().toLowerCase()!==e.NameFarEast.trim().toLowerCase()||f.NameFarEast!==e.NameFarEast||f.NameAscii!==e.NameAscii)throw new Error('MAC_RANGE_PRIMITIVE_FINAL_FONT_MISMATCH');primitive.target_format_matches_reference=true;}primitive.passed=true;}}
      }
      if(op==='get'&&(member==='FormattedText'||isDuplicatePrimitive&&member==='Duplicate'&&command.object===primitiveIds.reference)){
        if(isDuplicatePrimitive&&(value===primitiveRanges.reference||value===primitiveRanges.target||value===primitiveRanges.container))throw new Error('MAC_RANGE_PRIMITIVE_ALIAS');
        primitiveRanges.formatted=value;primitiveIds.formatted=wire.id;var f=primitiveObservation('FormattedText-get');
        var e=primitiveExpectedRanges.reference;primitiveExpectedRanges.formatted={start:e.start,end:e.end,text:e.text};primitiveRequire(f);
        if(f.doc_text!==primitiveBaseline||f.ranges.formatted.text!==primitiveBaseline[primitive.reference_index]||f.ranges.formatted.end-f.ranges.formatted.start!==1)throw new Error('MAC_RANGE_PRIMITIVE_FORMATTED_RANGE_MISMATCH');primitive.phase=2;
      }
      if(op==='set'&&member==='FormattedText'){
        var q=primitive.quote_index;primitiveExpected=primitiveBaseline.substring(0,q)+primitiveBaseline[primitive.reference_index]+primitiveBaseline.substring(q+1);var o=primitiveObservation('FormattedText-set');
        if(o.doc_text.length!==primitiveBaseline.length||o.doc_text!==primitiveExpected)throw new Error('MAC_RANGE_PRIMITIVE_REPLACEMENT_MISMATCH');primitiveExpectedText(primitiveExpected);primitiveRequire(o);primitive.phase=3;
      }
      if(op==='set'&&member==='Text'){var o=primitiveObservation('Text-set');if(o.doc_text!==primitiveBaseline)throw new Error('MAC_RANGE_PRIMITIVE_RESTORE_MISMATCH');primitiveExpectedText(primitiveBaseline);primitiveRequire(o);primitive.phase=5;}
    }
    // QA独立同步记录域试验：不替换正式nativeUndo身份绑定，不调用原格式化/Undo。
    function diagnoseUndoDomain(name) {
      if(domain.attempted||task||typeof name!=='string'||name.length>64||!/^PartyOpsUndoQA-[A-Za-z0-9]+$/.test(name))throw new Error('MAC_UNDO_DOMAIN_PRECONDITION_FAILED');
      domain.attempted=true;
      function context(){guard(true);domain.context_confirmed=true;}
      function fetch(){context();domain.getter_calls++;return application.UndoRecord;}
      function observe(label, values, level, recording, expectedName){
        var observation={phase:label,checked_at_utc:new Date().toISOString(),objects:[],equal_matrix:values.map(function(a){return values.map(function(b){return a===b;});})};
        domain.observations.push(observation);
        values.forEach(function(value,index){
          context();if(!value)throw new Error('MAC_UNDO_DOMAIN_API_UNAVAILABLE');
          var state={index:index,level:value.CustomRecordLevel,recording:value.IsRecordingCustomRecord,name:value.CustomRecordName,start_type:typeof value.StartCustomRecord,end_type:typeof value.EndCustomRecord};
          // 只保留空名/本轮QA名，外来记录名不写入回执。
          var safe={index:index,level:typeof state.level==='number'?state.level:null,level_type:typeof state.level,recording:typeof state.recording==='boolean'?state.recording:null,recording_type:typeof state.recording,name:state.name===expectedName?state.name:null,name_type:typeof state.name,name_matches:state.name===expectedName,start_type:state.start_type,end_type:state.end_type};observation.objects.push(safe);
          context();if(state.level!==level||state.recording!==recording||state.name!==expectedName||state.start_type!=='function'||state.end_type!=='function')throw new Error('MAC_UNDO_DOMAIN_STATE_MISMATCH');
        });context();
      }
      try {
        context();var openArgs=new Array(16);openArgs[0]=work;openArgs[2]=false;openArgs[11]=false;domain.open_argument_count=16;domain.open_read_only=false;domain.open_visible=false;
        var opened=application.Documents.Open.apply(application.Documents,openArgs);
        if(!opened||String(opened.FullName)!==work)throw new Error('MAC_LEASE_OPEN_RETURN_INVALID');task=opened;context();domain.work_document_opened=true;
        var u0=fetch(),u1=fetch(),u2=fetch();observe('before-start',[u0,u1,u2],0,false,'');
        context();domain.start_calls++;domain.unknown=true;var startResult=u0.StartCustomRecord(name);domain.native_start_result=typeof startResult==='boolean'||typeof startResult==='number'?startResult:null;domain.native_start_result_type=typeof startResult;
        u2=fetch();observe('after-start',[u0,u1,u2],1,true,name);domain.start_observed=true;
        context();domain.end_calls++;var endResult=u0.EndCustomRecord();domain.native_end_result=typeof endResult==='boolean'||typeof endResult==='number'?endResult:null;domain.native_end_result_type=typeof endResult;observe('after-end',[u0,u1,u2],0,false,'');domain.end_observed=true;domain.unknown=false;
        context();observe('before-close',[u0,u1,u2],0,false,'');context();
        domain.close_calls++;domain.close_unknown=true;task.Close(0);
        if(snapshot().length!==0)throw new Error('MAC_LEASE_CLOSE_UNCONFIRMED');domain.close_unknown=false;
        expireTask();leased=false;finished=true;domain.cleanup_confirmed=true;domain.lease_released=true;domain.actual_count=0;domain.passed=true;
      } catch(error) {
        domain.failure_code=/^MAC_(?:LEASE|UNDO)_[A-Z_]+$/.test(error.message||'')?error.message:'MAC_UNDO_DOMAIN_NATIVE_CALL_FAILED';
        domain.native_error_type=typeof error.name==='string'&&/^[A-Za-z]+$/.test(error.name)?error.name:'Unknown';
        domain.native_error_message_redacted=true;domain.cleanup_confirmed=false;domain.lease_released=false;
        try{var count=Number(application.Documents.Count);domain.actual_count=Number.isInteger(count)&&count>=0?count:null;}catch(ignored){domain.actual_count=null;}
        throw new Error(domain.failure_code);
      }
      return JSON.stringify(domain);
    }
    // 只QA固定单次效果实验；正常format的Doc.Undo拒绝保持原样。
    function observeEffect(phase, level, recording, name) {
      guard(true);var current=application.UndoRecord, held=effectUndo&&undoState(effectUndo),fresh=current&&undoState(current);
      if(!held||!fresh||typeof effectUndo.StartCustomRecord!=='function'||typeof effectUndo.EndCustomRecord!=='function'||held.level!==level||held.recording!==recording||held.name!==name||fresh.level!==held.level||fresh.recording!==held.recording||fresh.name!==held.name)throw new Error('MAC_UNDO_EFFECT_STATE_MISMATCH');
      effect.observations.push({phase:phase,level:held.level,recording:held.recording,name_matches:true,checked_at_utc:new Date().toISOString()});guard(true);
    }
    function diagnoseUndoEffect(name) {
      if(effect.attempted||!task||typeof name!=='string'||name.length>64||!/^PartyOpsUndoEffectQA-[A-Za-z0-9]+$/.test(name))throw new Error('MAC_UNDO_EFFECT_PRECONDITION_FAILED');
      effect.attempted=true;
      try {
        guard(true);effectUndo=application.UndoRecord;observeEffect('before-start',0,false,'');
        var paragraph=task.Paragraphs.Item(1),range=paragraph&&paragraph.Range,font=range&&range.Font;
        if(!range||!range.Document||String(range.Document.FullName)!==taskPath||!font||font.Size!==12||typeof task.Saved!=='boolean'||typeof task.Undo!=='function')throw new Error('MAC_UNDO_EFFECT_FONT_PRECONDITION_FAILED');
        effect.font_size_before=font.Size;effect.saved_before=task.Saved;guard(true);
        effect.unknown=true;effect.start_calls++;var result=effectUndo.StartCustomRecord(name);
        if(result!==undefined&&result!==null)throw new Error('MAC_UNDO_EFFECT_START_RESULT_UNKNOWN');observeEffect('after-start',1,true,name);
        guard(true);effect.font_write_calls++;font.Size=18;effect.font_size_changed=font.Size;
        if(effect.font_size_changed!==18)throw new Error('MAC_UNDO_EFFECT_CHANGE_UNCONFIRMED');observeEffect('after-change',1,true,name);
        effect.end_calls++;result=effectUndo.EndCustomRecord();
        if(result!==undefined&&result!==null)throw new Error('MAC_UNDO_EFFECT_END_RESULT_UNKNOWN');observeEffect('after-end',0,false,'');
        guard(true);effect.doc_undo_calls++;result=task.Undo(1);effect.undo_return_type=typeof result;effect.undo_return=typeof result==='boolean'?result:null;
        if(result!==true)throw new Error('MAC_UNDO_EFFECT_UNDO_RESULT_UNCONFIRMED');observeEffect('after-undo',0,false,'');
        effect.font_size_restored=font.Size;effect.saved_after=task.Saved;
        if(effect.font_size_restored!==12||typeof effect.saved_after!=='boolean')throw new Error('MAC_UNDO_EFFECT_RESTORE_UNCONFIRMED');
        guard(true);effect.unknown=false;effect.effect_observed=true;return JSON.stringify(effect);
      }catch(error){effect.failure_code=/^MAC_(?:LEASE|UNDO)_[A-Z_]+$/.test(error.message||'')?error.message:'MAC_UNDO_EFFECT_NATIVE_CALL_FAILED';effect.native_error_type=/^[A-Za-z]+$/.test(error.name||'')?error.name:'Unknown';throw new Error(effect.failure_code);}
    }
    function finishUndoEffect() {
      if(!effect.effect_observed||effect.unknown||effect.close_calls)throw new Error('MAC_UNDO_EFFECT_CLEANUP_DENIED');
      observeEffect('before-close',0,false,'');guard(true);effect.close_calls++;effect.close_unknown=true;
      task.Close(0);
      if(snapshot().length!==0)throw new Error('MAC_LEASE_CLOSE_UNCONFIRMED');effect.close_unknown=false;
      expireTask();leased=false;finished=true;effect.cleanup_confirmed=true;effect.lease_released=true;return JSON.stringify(effect);
    }
    var dispatcher = function dispatch(command) {
      var receipt = {schema: 1, session: config.session, generation: config.generation, endpoint: config.endpoint, id: command && command.id};
      try {
        // 未进入原宿主的managed PDF分支只允许已有载体关闭与最后清理；其它尝试封闭空会话收尾。
        if(!command||command.object!==1||command.operation!=='inspect'||['LeaseCloseBootstrap','LeaseFinish','LeaseInspectUndoState'].indexOf(command.member)<0)emptySessionEligible=false;
        if (!command || command.session !== config.session || command.generation !== config.generation || command.endpoint !== config.endpoint
            || command.schema !== 1 || !Number.isInteger(command.id) || command.id <= lastId) throw new Error('MAC_LEASE_COMMAND_STALE');
        lastId = command.id;
        if (!Array.isArray(command.args) || command.args.length > 64 || !/^[A-Za-z][A-Za-z0-9_]*$/.test(command.member)
            || ['constructor', 'prototype', '__proto__'].indexOf(command.member) >= 0) throw new Error('MAC_LEASE_COMMAND_INVALID');
        var record = records.get(command.object), member = command.member, op = command.operation;
        if (!record) throw new Error('MAC_LEASE_OBJECT_EXPIRED');
        var args = command.args.map(decode), value, scope = record.scope;
        if(isPrimitive)primitiveControl(command,record,args);
        if(config.mode==='task-lease-undo-domain' && (command.object!==1||op!=='inspect'||['LeaseAcquire','LeaseCloseBootstrap','LeaseUndoDomainDiagnostic','LeaseInspectUndoDomain'].indexOf(member)<0))throw new Error('MAC_UNDO_DOMAIN_MODE_OPERATION_DENIED');
        if(config.mode==='task-lease-undo-effect'){
          var effectInspect=command.object===1&&op==='inspect'&&['LeaseAcquire','LeaseCloseBootstrap','LeaseReleaseObject','LeaseUndoEffect','LeaseInspectUndoEffect','LeaseFinishUndoEffect'].indexOf(member)>=0;
          var effectRead=op==='get';var effectReadCall=op==='call'&&((record.scope==='documents'&&member==='Open')||(record.type==='Document'&&['Range','ComputeStatistics'].indexOf(member)>=0));
          if(!effectInspect&&!effectRead&&!effectReadCall)throw new Error('MAC_UNDO_EFFECT_MODE_OPERATION_DENIED');
        }
        if (command.object === 1 && op === 'inspect') {
          if(member==='LeaseInspectRangePrimitive'&&isPrimitive&&args.length===0){value=JSON.stringify(primitive);}
          else if(member==='LeaseBeginRangePrimitive'&&isPrimitive){value=true;}
          else if(member==='LeaseUndoEffect'&&config.mode==='task-lease-undo-effect'&&args.length===1){value=diagnoseUndoEffect(args[0]);}
          else if(member==='LeaseInspectUndoEffect'&&config.mode==='task-lease-undo-effect'&&args.length===0){value=JSON.stringify(effect);}
          else if(member==='LeaseFinishUndoEffect'&&config.mode==='task-lease-undo-effect'&&args.length===0){value=finishUndoEffect();}
          else if(member==='LeaseUndoDomainDiagnostic'&&config.mode==='task-lease-undo-domain'&&args.length===1){value=diagnoseUndoDomain(args[0]);}
          else if(member==='LeaseInspectUndoDomain'&&config.mode==='task-lease-undo-domain'&&args.length===0){value=JSON.stringify(domain);}
          else if (member === 'LeaseAcquire') {
            if (leased || finished || snapshot().length) throw new Error('MAC_LEASE_ZERO_DOCUMENTS_REQUIRED');
            leased = true; value = JSON.stringify({lease_acquired: true, actual_count: 0});
          } else if (member === 'LeaseCloseBootstrap') {
            if (leased || finished || !config.carrier || config.carrier === config.input || config.carrier === work) throw new Error('MAC_LEASE_BOOTSTRAP_INVALID');
            var carriers = snapshot();
            if (carriers.length !== 1 || carriers[0] !== config.carrier || String(application.ActiveDocument.FullName) !== config.carrier) throw new Error('MAC_LEASE_BOOTSTRAP_FOREIGN');
            // 此次关闭响应未知不能再调用此操作。
            finished = true;productClose(application.ActiveDocument,function(){application.ActiveDocument.Close(0);}); if (snapshot().length) throw new Error('MAC_LEASE_BOOTSTRAP_CLOSE_UNCONFIRMED'); finished = false;bootstrapClosed=true;
            value = JSON.stringify({bootstrap_closed: true, actual_count: 0});
          } else if (member === 'LeaseReleaseObject') { if (args[0] !== 1) invalidate(args[0]); value = true; }
          else if (member === 'LeaseRegisterArtifact') {
            guard(true);var path=args[0], root=work.substring(0,Math.max(work.lastIndexOf('/'),work.lastIndexOf('\\'))+1);
            if(typeof path!=='string' || !root || path.indexOf(root)!==0 || /[\\/]\.\.[\\/]/.test(path) || path===config.input || path===config.carrier || !(config.product_task?/\.(docx|pdf)$/i:/\.docx$/i).test(path)) throw new Error('MAC_LEASE_ARTIFACT_PATH_DENIED');
            artifacts.add(path);value=true;
          }
          else if (member === 'LeaseInspectUndoState' && args.length===0) { value=JSON.stringify(undo); }
          else if (member === 'LeaseFinish') {
            if (finished) throw new Error('MAC_LEASE_ALREADY_RELEASED');
            if(!leased){
              var eligible=emptySessionEligible;emptySessionEligible=false;
              if(args.length!==0||!config.product_task||['pdf-to-word','convert'].indexOf(config.feature_id)<0||!eligible||!bootstrapClosed||task||taskCycle!==0||companionUsed||closeUnknown||companionUnknown||undo.invalidated||undo.unknown||productLifecycle.external_close_detected||!productLifecycle.listener_registered||productLifecycle.close_events!==1)throw new Error('MAC_LEASE_EMPTY_SESSION_UNCONFIRMED');
              if(snapshot().length)loseBoundary('MAC_LEASE_FOREIGN_DOCUMENT');
              // 没有Acquire、Open、Undo或任务Close；真实Count0才终结此次空会话。
              finished=true;value=JSON.stringify({cleanup_confirmed:true,lease_released:true,lease_acquired:false,task_document_remaining:false,remaining_count:0,undo_state:undo,product_lifecycle:productLifecycle});
              receipt.result={kind:'value',value:value};receipt.args=command.args;return identityReceipt(receipt);
            }
            if (closeUnknown||companionUnknown) throw new Error('MAC_LEASE_CLOSE_UNKNOWN_NO_REPLAY');
            if(undo.active||undo.unknown||undo.preexisting_record||undo.rollback_unproven||undo.invalidated)throw new Error('MAC_UNDO_EXIT_UNCONFIRMED');
            guard(true);
            if(companion)closeCompanion();
            verifyIdleUndo();
            if (task) {
              if (String(task.FullName) !== taskPath || snapshot().indexOf(taskPath) < 0) throw new Error('MAC_LEASE_CLEANUP_UNVERIFIABLE');
              closeUnknown = true; productClose(task,function(){task.Close(0);});
              if (snapshot().indexOf(taskPath) >= 0) throw new Error('MAC_LEASE_CLEANUP_UNCONFIRMED'); closeUnknown = false; expireTask();
            }
            var finalPaths = snapshot(); leased = false; finished = true; records.forEach(function (r, id) { if (id !== 1) invalidate(id); });
            value = JSON.stringify({cleanup_confirmed: true, lease_released: true, task_document_remaining: false, remaining_count: finalPaths.length,undo_state:undo,product_lifecycle:productLifecycle});
          } else throw new Error('MAC_LEASE_INSPECTION_UNKNOWN');
          receipt.result = {kind: 'value', value: value}; receipt.args = command.args; return identityReceipt(receipt);
        }
        // 下面所有检查与原对象调用在同一个同步JS事件内，不跨await或timer。
        guard(true); ownership(record);
        if(companion&&scope!=='export-task'&&scope!=='application'&&scope!=='documents')throw new Error('MAC_LEASE_PRIMARY_DURING_EXPORT_DENIED');
        // 原页码归一化仅两原生属性；readonly路径仍拒写，不开放其他成员。
        if(record.type==='PageNumbers'){
          if(scope==='snapshot-read-only'&&op!=='get')throw new Error('MAC_LEASE_SNAPSHOT_WRITE_DENIED');
          var numberRead=op==='get'&&args.length===0&&['RestartNumberingAtSection','StartingNumber'].indexOf(member)>=0;
          var numberWrite=op==='set'&&args.length===1&&((member==='RestartNumberingAtSection'&&typeof args[0]==='boolean')||(member==='StartingNumber'&&args[0]===1));
          if(!numberRead&&!numberWrite)throw new Error('MAC_LEASE_PAGE_NUMBERS_MEMBER_DENIED');
        }
        if (scope === 'application') {
          if(companion&&(op!=='get'||member!=='Documents'||args.length))throw new Error('MAC_LEASE_EXPORT_APPLICATION_DENIED');
          // 原命名只要求可见；真实getter已经true才确认等值请求，不调用任何全局setter。
          if(op==='set'&&member==='Visible'){
            if(args.length!==1||args[0]!==true)throw new Error('MAC_LEASE_GLOBAL_MUTATION_DENIED');
            var nativeVisible=record.value.Visible;
            if(nativeVisible!==true)throw new Error('MAC_LEASE_VISIBLE_STATE_UNVERIFIABLE');
            guard(true);receipt.result={kind:'value',value:null};receipt.args=command.args;return identityReceipt(receipt);
          }
          // 原页面设置只需此无副作用原生单位函数；不开放其他Application调用或全局写。
          if(op==='call'&&member==='CentimetersToPoints'){
            if(!task||args.length!==1||typeof args[0]!=='number'||!Number.isFinite(args[0])||Math.abs(args[0])>3.4028234663852886e38)throw new Error('MAC_LEASE_UNIT_CONVERSION_BINDING_INVALID');
          }else if (op !== 'get' || args.length || globalReads.indexOf(member) < 0) throw new Error('MAC_LEASE_GLOBAL_MUTATION_DENIED');
          if (['ActiveDocument', 'Selection', 'ActiveWindow', 'UndoRecord'].indexOf(member) >= 0 && !task) throw new Error('MAC_LEASE_NO_TASK_DOCUMENT');
          if (member === 'Documents') scope = 'documents';
          if (['ActiveDocument', 'Selection', 'ActiveWindow'].indexOf(member) >= 0) scope = 'task';
          if(member==='UndoRecord')scope='undo';
        } else if (scope === 'documents') {
          if (op === 'get' && member === 'Count' && args.length === 0) { /* 真实值 */ }
          else if (op === 'call' && member === 'Open') {
            if(taskCycle)loseBoundary('MAC_UNDO_DOCUMENT_REOPEN_DENIED');
            if (task || args.length !== 16 || args[0] !== work || args[2] !== false || args[11] !== false || snapshot().length) throw new Error('MAC_LEASE_OPEN_BINDING_INVALID');
          } else if(op==='call'&&member==='Add'&&config.product_task&&config.feature_id==='convert'){
            if(!task||companion||companionUsed||exportXml===null||args.length!==4||command.args.slice(0,3).some(function(a){return a.kind!=='missing';})||args[3]!==false)throw new Error('MAC_LEASE_EXPORT_ADD_BINDING_INVALID');
          } else throw new Error('MAC_LEASE_DOCUMENTS_MUTATION_DENIED');
        } else if(scope==='export-task'){
          var exportGet=op==='get'&&args.length===0&&record.type==='Document'&&member==='Content';
          var insert=op==='call'&&record.type==='Range'&&member==='InsertXML'&&args.length===2&&args[0]===exportXml&&command.args[1].kind==='missing'&&!exportInserted;
          var exportSave=op==='call'&&record.type==='Document'&&member==='SaveAs2'&&args.length===17&&typeof args[0]==='string'&&artifacts.has(args[0])&&!reservedPaths.has(args[0].toLowerCase())&&snapshot().every(function(p){return p.toLowerCase()!==args[0].toLowerCase();})&&/\.docx$/i.test(args[0])&&args[1]===12&&command.args.slice(2).every(function(a){return a.kind==='missing';})&&exportInserted&&!exportSaved;
          var exportClose=op==='call'&&record.type==='Document'&&member==='Close'&&args.length===3&&args[0]===0&&command.args.slice(1).every(function(a){return a.kind==='missing';});
          if(!exportGet&&!insert&&!exportSave&&!exportClose)throw new Error('MAC_LEASE_EXPORT_MEMBER_DENIED');
          if(exportClose){closeCompanion();receipt.result={kind:'value',value:null};receipt.args=command.args;return identityReceipt(receipt);}
        } else if(scope==='snapshot-read-only') {
          var propertyItem=record.type==='DocumentProperties'&&op==='call'&&member==='Item'&&args.length===1&&Number.isInteger(args[0])&&args[0]>=1;
          if((op!=='get'&&!propertyItem)||(args.length&&member!=='Item')||(snapshotCollections[record.type]&&snapshotCollections[record.type].indexOf(member)<0))throw new Error('MAC_LEASE_SNAPSHOT_WRITE_DENIED');
          if(record.type==='DocumentProperties'&&!((op==='get'&&member==='Count'&&args.length===0)||propertyItem))throw new Error('MAC_LEASE_SNAPSHOT_WRITE_DENIED');
          if(record.type==='DocumentProperty'&&(op!=='get'||args.length||['Name','Value','Type','LinkToContent','LinkSource'].indexOf(member)<0))throw new Error('MAC_LEASE_SNAPSHOT_WRITE_DENIED');
          if(record.type==='Hyperlink'&&(member!=='Range'||args.length))throw new Error('MAC_LEASE_SNAPSHOT_WRITE_DENIED');
          if(record.type==='Style'&&(member!=='NameLocal'||args.length))throw new Error('MAC_LEASE_SNAPSHOT_WRITE_DENIED');
        } else if(scope==='undo') {
          if(op==='get'&&args.length===0&&['CustomRecordLevel','IsRecordingCustomRecord','CustomRecordName'].indexOf(member)>=0){verifyUndo(record.value);}
          else if(op==='call'&&member==='StartCustomRecord'&&args.length===1&&typeof args[0]==='string'&&args[0].length<=256) {
            verifyUndo(record.value);if(undo.lease_started||undo.active||undo.unknown||!args[0])throw new Error('MAC_UNDO_START_NOT_REPLAYABLE');
          } else if(op==='call'&&member==='EndCustomRecord'&&args.length===0) {
            if(!undo.active||undo.unknown||undo.end_calls)throw new Error('MAC_UNDO_END_NOT_REPLAYABLE');verifyUndo(record.value);
          } else throw new Error('MAC_UNDO_SCOPE_OPERATION_DENIED');
        } else {
          if(member==='ExportAsFixedFormat'){
            if(!config.product_task||op!=='call'||record.type!=='Document'||record.value!==task||args.length!==15||!artifacts.has(args[0])||!/\.pdf$/i.test(args[0])||args[1]!==17||args[2]!==false||args[3]!==0||args[4]!==0||args[5]!==1||args[6]!==1||args[7]!==0||args[8]!==true||args[9]!==true||args[10]!==0||args[11]!==true||args[12]!==true||args[13]!==false||command.args[14].kind!=='missing')throw new Error('MAC_LEASE_PDF_BINDING_INVALID');
          }
          if(member==='Activate'&&(op!=='call'||scope!=='task'||record.type!=='Document'||record.value!==task||args.length!==0))throw new Error('MAC_LEASE_ACTIVATE_BINDING_INVALID');
          if(member==='AddLine'){
            var lineParent=records.get(record.parent),lineAnchor=command.args[4]&&command.args[4].kind==='object'?records.get(command.args[4].id):null;
            if(op!=='call'||scope!=='task'||record.type!=='Shapes'||record.member!=='Shapes'||!lineParent||lineParent.type!=='Document'||lineParent.scope!=='task'||lineParent.value!==task||args.length!==5||command.result_type!=='Microsoft.Office.Interop.Word.Shape'||!lineAnchor||lineAnchor.scope!=='task'||lineAnchor.type!=='Range')throw new Error('MAC_LEASE_ADD_LINE_BINDING_INVALID');
            for(var coordinate=0;coordinate<4;coordinate++)if(typeof args[coordinate]!=='number'||!Number.isFinite(args[coordinate])||Math.abs(args[coordinate])>3.4028234663852886e38)throw new Error('MAC_LEASE_ADD_LINE_BINDING_INVALID');
            if(!record.value.Parent||String(record.value.Parent.FullName)!==taskPath)loseBoundary('MAC_LEASE_ADD_LINE_OWNER_UNVERIFIABLE');
            ownership(lineParent);ownership(lineAnchor);
          }
          // 原印章表仅调用真实任务Table.Cell(row,column)，不开放其他同名对象。
          if(member==='Cell'){
            if(op!=='call'||scope!=='task'||record.type!=='Table'||args.length!==2||command.result_type!=='Microsoft.Office.Interop.Word.Cell'||args.some(function(v){return !Number.isInteger(v)||v<1||v>2147483647;}))throw new Error('MAC_LEASE_TABLE_CELL_BINDING_INVALID');
            if(!record.value.Range||!record.value.Range.Document||String(record.value.Range.Document.FullName)!==taskPath)loseBoundary('MAC_LEASE_TABLE_CELL_OWNER_UNVERIFIABLE');
            ownership(record);
          }
          if (['Quit', 'Open', 'SaveAs', 'SaveCopyAs', 'InsertFile', 'Run', 'PrintOut'].indexOf(member) >= 0) throw new Error('MAC_LEASE_EXTERNAL_MUTATION_DENIED');
          if (op === 'get' && args.length && ['Item', 'Information'].indexOf(member) < 0) throw new Error('MAC_LEASE_INDEXED_MEMBER_DENIED');
          if (op === 'set' && (args.length !== 1 || ['Application', 'Parent', 'Document'].indexOf(member) >= 0
              || (record.type === 'Document' && ['FullName', 'Path', 'Name'].indexOf(member) >= 0))) throw new Error('MAC_LEASE_WRITE_MEMBER_DENIED');
          if (op === 'call' && calls.indexOf(member) < 0 && member!=='SaveAs2' && member!=='Undo' && member!=='ExportAsFixedFormat' && member!=='AddLine' && member!=='Activate' && member!=='Cell') throw new Error('MAC_LEASE_METHOD_UNAVAILABLE:' + member);
          // 原Normalize两入口均为[In]ref Type.Missing，仅转发任务Range.ListFormat真实方法。
          if(member==='ConvertNumbersToText'&&(op!=='call'||record.type!=='ListFormat'||args.length!==1||command.args[0].kind!=='missing'))throw new Error('MAC_LEASE_LIST_FORMAT_BINDING_INVALID');
          // 原TextCleanup仅清理任务Range的Find/Replacement格式，禁止其他同名对象和带参调用。
          if(member==='ClearFormatting'&&(op!=='call'||['Find','Replacement'].indexOf(record.type)<0||args.length!==0))throw new Error('MAC_LEASE_CLEAR_FORMATTING_BINDING_INVALID');
          // 原直接格式清理仅任务Range的ParagraphFormat/Font真实getter、零参Reset。
          if(member==='Reset'){
            var resetParent=records.get(record.parent);
            if(op!=='call'||args.length||['ParagraphFormat','Font'].indexOf(record.type)<0||record.member!==record.type||!resetParent||resetParent.type!=='Range'||resetParent.scope!=='task')throw new Error('MAC_LEASE_DIRECT_FORMAT_RESET_BINDING_INVALID');
          }
          if(member==='Undo') {
            if(op!=='call'||record.type!=='Document'||record.value!==task||args.length!==1||args[0]!==1||!undo.lease_started||!undo.end_confirmed||undo.active||undo.unknown)throw new Error('MAC_UNDO_DOCUMENT_BINDING_INVALID');
            // Level/Name和方法成功不证明End后的历史顶项属于非空本组；禁止撤销旧历史。
            try{verifyIdleUndo();}finally{undo.rollback_unproven=true;}throw new Error('MAC_UNDO_RECORD_OWNERSHIP_UNPROVEN');
          }
          if (member === 'SaveAs2' && (op!=='call'||record.type!=='Document'||record.value!==task||args.length!==17||!artifacts.has(args[0]))) throw new Error('MAC_LEASE_ARTIFACT_PATH_DENIED');
          if (member === 'AddPicture') throw new Error('MAC_LEASE_EXTERNAL_MUTATION_DENIED');
          if (member === 'Close' && (record.type !== 'Document' || record.value !== task || [0, -1].indexOf(args[0]) < 0)) throw new Error('MAC_LEASE_FOREIGN_CLOSE_DENIED');
          if(member==='Close'&&undo.active)throw new Error('MAC_UNDO_EXIT_UNCONFIRMED');
          if(member==='Close')verifyIdleUndo();
          if (member === 'Save' && (record.type !== 'Document' || record.value !== task)) throw new Error('MAC_LEASE_FOREIGN_SAVE_DENIED');
        }
        guard(true);
        if(isPrimitive&&(op==='set'||op==='call'&&member==='SetRange')){
          primitiveRequire(primitiveObservation('immediate-before-'+member));
          if(op==='set')primitive.content_write_calls++;
        }
        if(member==='UndoRecord' && scope==='undo')undo.native_getter_calls++;
        if(scope==='undo'&&op==='call'){undo.unknown=true;if(member==='StartCustomRecord')undo.start_calls++;else undo.end_calls++;}
        if (member === 'Close') closeUnknown = true;
        if(record.scope==='documents'&&member==='Add')companionUsed=true;
        if(scope==='export-task'&&member==='InsertXML')exportInserted=true;
        if(scope==='export-task'&&member==='SaveAs2')exportSaved=true;
        if (op === 'get') value = args.length ? record.value[member].apply(record.value, args) : record.value[member];
        else if (op === 'set') { record.value[member] = args[0]; value = undefined; }
        else if (op === 'call' && typeof record.value[member] === 'function') value = member==='Close'?productClose(record.value,function(){return record.value[member].apply(record.value,args);}):record.value[member].apply(record.value, args);
        else throw new Error('MAC_LEASE_OPERATION_UNAVAILABLE');
        if(member==='AddLine'){
          // 只能认领实际创建的任务线条；空/未知/外来anchor不继承Shape权限。
          if(!value||!value.Anchor||!value.Anchor.Document||String(value.Anchor.Document.FullName)!==taskPath)loseBoundary('MAC_LEASE_ADD_LINE_RETURN_UNVERIFIABLE');
          guard(true);
        }
        if(member==='Cell'){
          // 必须由实际返回Cell的Range确认任务归属；未知结果不合成、不重放。
          if(!value||!value.Range||!value.Range.Document||String(value.Range.Document.FullName)!==taskPath)loseBoundary('MAC_LEASE_TABLE_CELL_RETURN_UNVERIFIABLE');
          guard(true);
        }
        if(member==='Activate'){
          if(value!==undefined&&value!==null)loseBoundary('MAC_LEASE_ACTIVATE_RESULT_INVALID');
          guard(true);
        }
        if(record.type==='ShapeRange'){
          if(!Number.isInteger(value)||value<0||value>2147483647)throw new Error('MAC_LEASE_SHAPE_RANGE_API_UNVERIFIABLE');
          // 仅缓存原format已经完成的真实Count读取，不额外读取原生属性。
          undo.shape_range_count_calls=(undo.shape_range_count_calls||0)+1;
          if(!undo.shape_range_count_observations)undo.shape_range_count_observations=[];
          if(undo.shape_range_count_observations.length<12)undo.shape_range_count_observations.push(value);
          else undo.shape_range_count_observations_truncated=true;
        }
        if(record.type==='PageNumbers'&&op==='get'&&((member==='RestartNumberingAtSection'&&typeof value!=='boolean')||(member==='StartingNumber'&&(!Number.isInteger(value)||value<-2147483648||value>2147483647))))throw new Error('MAC_LEASE_PAGE_NUMBERS_API_UNVERIFIABLE');
        if(op==='call'&&member==='Reset'&&value!==undefined&&value!==null)throw new Error('MAC_LEASE_DIRECT_FORMAT_RESET_RESULT_INVALID');
        if(record.scope==='application'&&op==='call'&&member==='CentimetersToPoints'){
          if(typeof value!=='number'||!Number.isFinite(value)||Math.abs(value)>3.4028234663852886e38)throw new Error('MAC_LEASE_UNIT_CONVERSION_RESULT_INVALID');
          guard(true);
        }
        if(scope==='undo'&&op==='call'){
          if(member==='StartCustomRecord'){undo.native_start_result=value===undefined?null:value;undo.lease_started=true;undo.active=true;undo.record_name=args[0].substring(0,64);verifyUndo(record.value,true);}
          else{undo.native_end_result=value===undefined?null:value;undo.active=false;verifyUndo(record.value,true);undo.end_confirmed=true;}
          undo.unknown=false;
        }
        if(record.type==='Document'&&record.value===task&&op==='get'&&member==='WordOpenXML'&&config.product_task){if(typeof value!=='string'||!value)throw new Error('MAC_LEASE_EXPORT_XML_INVALID');exportXml=value;}
        if(record.scope==='documents'&&member==='Add'){
          if(!value||value===task||typeof value.FullName!=='string'||!value.FullName||value.FullName===taskPath)loseBoundary('MAC_LEASE_EXPORT_ADD_RETURN_INVALID');
          companion=value;companionPath=String(value.FullName);companionAnchor=value.Range(0,0);scope='export-task';guard(true);
        }
        if(member==='SaveAs2'&&scope==='export-task'){
          if(String(companion.FullName)!==args[0])loseBoundary('MAC_LEASE_SAVEAS_RETURN_INVALID');
          companionPath=args[0];records.forEach(function(r){if(r.scope==='export-task')r.owner=companionPath;});guard(true);
        }else if(member==='SaveAs2') {
          if(String(task.FullName)!==args[0]) throw new Error('MAC_LEASE_SAVEAS_RETURN_INVALID');
          var oldPath=taskPath;taskPath=args[0];reservedPaths.add(String(taskPath).toLowerCase());records.forEach(function(r){if((r.scope==='task'||r.scope==='undo'||r.scope==='snapshot-read-only')&&r.owner===oldPath)r.owner=taskPath;});guard(true);
        }
        if (record.scope === 'documents' && member === 'Open') {
          if (!value || String(value.FullName) !== work) throw new Error('MAC_LEASE_OPEN_RETURN_INVALID'); task = value;taskCycle++;cycleClosed=false;scope = 'task';
          try{taskAnchor=task.Range(0,0);}catch(error){loseBoundary('MAC_LEASE_DOCUMENT_ANCHOR_UNAVAILABLE');}guard(true);
        }
        receipt.result = encode(value, scope, member, command.result_type, command.object); receipt.args = command.args;
        if(isPrimitive)primitiveAfter(command,value,receipt.result);
        if (member === 'Close') { if (snapshot().indexOf(taskPath) >= 0) throw new Error('MAC_LEASE_CLOSE_UNCONFIRMED'); closeUnknown = false; expireTask(); }
        return identityReceipt(receipt);
      } catch (error) {
        receipt.error = /^MAC_(?:LEASE|UNDO|RANGE_PRIMITIVE|PRODUCT)_[A-Z_]+(?::[A-Za-z]+)?$/.test(error.message || '') ? error.message : 'MAC_LEASE_NATIVE_CALL_FAILED';
        if(isPrimitive){
          if(!primitive.failure_code){primitive.failure_code=receipt.error;try{if(task)primitiveObservation('failure-readonly');}catch(ignored){primitive.failure_observation_unavailable=true;}}
          primitive.locked=true;primitive.passed=false;
        }
        if(config.mode==='task-lease-undo-effect'&&(effect.unknown||effect.close_unknown))undo.invalidated=true;
        if(receipt.error==='MAC_LEASE_NATIVE_CALL_FAILED'||receipt.error==='MAC_LEASE_DOCUMENT_IDENTITY_SOURCE_INVALID')undo.invalidated=true;
        if(command && (command.member==='UndoRecord'||command.member==='StartCustomRecord'||command.member==='EndCustomRecord'||command.member==='Undo')){if(!undo.failure_code)undo.failure_code=receipt.error;undo.last_failure_code=receipt.error;}
        return identityReceipt(receipt);
      }
    };
    // 只撤销本页的旧任务能力，不调用原生退出/关闭，也不删除新任务事件监听器。
    dispatcher.retire=function(){callbackRetired=true;undo.invalidated=true;leased=false;finished=true;records.clear();ids.clear();readonlyIds.clear();};
    return dispatcher;
    function identityReceipt(receipt) {
      if(isPrimitive)receipt.range_primitive=primitive;
      receipt.document_identity_state={schema:1,generation:config.generation,cycle:taskCycle,state:undo.invalidated||undo.unknown||undo.rollback_unproven||undo.preexisting_record||closeUnknown||companionUnknown?'invalidated':cycleClosed||finished?'closed':task?'active':'unbound'};
      if(config.product_task)receipt.export_document_identity_state={schema:1,generation:config.generation,cycle:companionUsed?2:0,state:undo.invalidated||companionUnknown?'invalidated':companionClosed?'closed':companion?'active':'unbound'};
      if(config.product_task)receipt.product_lifecycle=productLifecycle;
      return receipt;
    }
  }
  if (typeof module !== 'undefined' && module.exports) module.exports = create;
  else global.PartyOpsMacTaskLeaseDispatcher = create;
})(this);
