using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace PartyOps.DocumentFormatter.Host
{
    /// <summary>
    /// Mac 加载项主动轮询的对象通道。没有排版规则，不激活或退出用户 WPS。
    /// 当前用于先行门槛；正式注册须先证明独立应用隔离。
    /// </summary>
    internal sealed class MacWpsObjectBridge : IDisposable
    {
        internal const string Backend = "mac-wps-addin-object-proxy-v1";
        private readonly HttpListener listener = new HttpListener();
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 1048576 };
        private readonly object sync = new object();
        private readonly Dictionary<string, object> proxies = new Dictionary<string, object>();
        private Assembly wordAssembly;
        private static long documentIdentitySequence;
        private readonly Dictionary<int,long> documentIdentities=new Dictionary<int,long>();
        private readonly Dictionary<int,int> documentCycles=new Dictionary<int,int>();
        private readonly HashSet<string> reservedDocumentPaths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private long exportDocumentIdentity;
        private string exportDocumentState="unbound";
        private string documentState="unbound";
        private int documentCycle;
        private long stableDocumentIdentity;
        internal static bool IsDocumentType(Type type) { return type!=null&&(type.FullName=="Microsoft.Office.Interop.Word.Document"||type.FullName=="Microsoft.Office.Interop.Word._Document"); }
        private void InvalidateDocumentIdentity() { if(IsTaskLeaseMode){documentState="invalidated";exportDocumentState="invalidated";timeline["document_identity_state"]="invalidated";} }
        // 墓碑仅供原清理查缓存；此查询不发送RPC、不绑定新对象、不授予native权限。
        internal long DocumentIdentity(int id,bool cleanup)
        {
            lock(sync) {
                long identity;
                if(!IsTaskLeaseMode||!documentIdentities.TryGetValue(id,out identity))throw new InvalidOperationException("MAC_LEASE_DOCUMENT_IDENTITY_UNCONFIRMED");
                int cycle;string phase=documentCycles.TryGetValue(id,out cycle)&&cycle==2?exportDocumentState:documentState;
                if(phase!="active"&&!cleanup)throw new InvalidOperationException("MAC_LEASE_DOCUMENT_IDENTITY_EXPIRED");
                return identity;
            }
        }
        // 仅既有句柄归属元数据，不返回业务身份、不建槽、不调用native；供原包装释放分派。
        internal bool HasDocumentOwnership(int id)
        { lock(sync) return IsTaskLeaseMode&&documentIdentities.ContainsKey(id); }
        private void ObserveDocumentState(Dictionary<string,object> received)
        {
            if(!IsTaskLeaseMode)return;
            lock(sync) {
                if(documentState=="invalidated"||documentState=="closed"||documentState=="disposed")return;
                object raw;var state=received.TryGetValue("document_identity_state",out raw)?raw as Dictionary<string,object>:null;
                if(state==null||Convert.ToInt32(state["schema"])!=1||Convert.ToString(state["generation"])!=generation) { InvalidateDocumentIdentity();throw new InvalidDataException("MAC_LEASE_DOCUMENT_STATE_INVALID"); }
                string phase=Convert.ToString(state["state"]);int cycle=Convert.ToInt32(state["cycle"]);
                if(cycle<0||(documentCycle!=0&&documentCycle!=cycle)||Array.IndexOf(new[]{"unbound","active","closed","invalidated"},phase)<0||(phase=="active"&&cycle==0)) { InvalidateDocumentIdentity();throw new InvalidDataException("MAC_LEASE_DOCUMENT_STATE_CHANGED"); }
                documentCycle=cycle;documentState=phase;timeline["document_identity_state"]=phase;timeline["document_identity_cycle"]=cycle;
                if(productNonce!=null){
                    var extra=received.TryGetValue("export_document_identity_state",out raw)?raw as Dictionary<string,object>:null;
                    if(extra==null||Convert.ToInt32(extra["schema"])!=1||Convert.ToString(extra["generation"])!=generation) {InvalidateDocumentIdentity();throw new InvalidDataException("MAC_LEASE_EXPORT_STATE_INVALID");}
                    int ec=Convert.ToInt32(extra["cycle"]);string ep=Convert.ToString(extra["state"]);
                    if((ec!=0&&ec!=2)||Array.IndexOf(new[]{"unbound","active","closed","invalidated"},ep)<0||(ep=="active"&&ec!=2)||(exportDocumentState=="closed"&&ep!="closed"&&ep!="invalidated")){InvalidateDocumentIdentity();throw new InvalidDataException("MAC_LEASE_EXPORT_STATE_CHANGED");}
                    if(exportDocumentState!="invalidated")exportDocumentState=ep;
                    timeline["export_document_identity_state"]=exportDocumentState;
                }
            }
        }
        private void BindDocumentIdentity(int id,Type type,Dictionary<string,object> wire)
        {
            lock(sync) {
                object raw;var descriptor=wire.TryGetValue("document_identity",out raw)?raw as Dictionary<string,object>:null;
                bool companion=descriptor!=null&&productNonce!=null&&Convert.ToInt32(descriptor["cycle"])==2&&Convert.ToString(descriptor["scope"])=="export-task"&&Convert.ToString(descriptor["source"])=="export-add";
                if(documentState!="active"||descriptor==null||wordAssembly!=type.Assembly||Convert.ToInt32(descriptor["schema"])!=1||Convert.ToString(descriptor["type"])!=type.FullName
                    ||Convert.ToString(descriptor["generation"])!=generation||(!companion&&Convert.ToInt32(descriptor["cycle"])!=documentCycle)
                    ||(!companion&&Array.IndexOf(new[]{"task","snapshot-read-only"},Convert.ToString(descriptor["scope"]))<0)
                    ||(!companion&&Array.IndexOf(new[]{"open","parent-document","active-document"},Convert.ToString(descriptor["source"]))<0)||id<=1||(companion&&exportDocumentState!="active"))
                    throw new InvalidDataException("MAC_LEASE_DOCUMENT_DESCRIPTOR_INVALID");
                if(companion){if(exportDocumentIdentity==0){long ordinal=Interlocked.Increment(ref documentIdentitySequence);if(ordinal<=0)throw new InvalidOperationException("MAC_LEASE_IDENTITY_SEQUENCE_EXHAUSTED");exportDocumentIdentity=-ordinal;}documentIdentities[id]=exportDocumentIdentity;documentCycles[id]=2;return;}
                if(stableDocumentIdentity==0) { long ordinal=Interlocked.Increment(ref documentIdentitySequence);if(ordinal<=0)throw new InvalidOperationException("MAC_LEASE_IDENTITY_SEQUENCE_EXHAUSTED");stableDocumentIdentity=-ordinal; }
                documentIdentities[id]=stableDocumentIdentity;
                documentCycles[id]=documentCycle;
            }
        }
        private readonly string resources;
        private readonly string cancelPath;
        private readonly string input;
        private readonly string output;
        private readonly string carrier;
        private readonly string mode;
        private readonly string session;
        private readonly string origin;
        private readonly string productNonce;
        private readonly bool fixedProduct;
        internal const string ProductPrefix = "/partyops-formatter/v1/";
        private readonly string featureId;
        private string Endpoint { get { return productNonce==null?origin:origin+"/p/"+productNonce; } }
        private readonly string generation = Guid.NewGuid().ToString("N");
        private Dictionary<string, object> pending;
        private Dictionary<string, object> reply;
        private bool delivered;
        private bool disposed;
        private bool failed;
        private int sequence;
        private bool pluginConnected;
        private bool bootstrapClaimed;
        private readonly Dictionary<string, object> timeline = new Dictionary<string, object>();
        private Dictionary<string, object> firstTransportTimeout;
        private static readonly string[] TransportTimeoutFields = new[]{"last_command_id","last_command_operation","last_command_member","last_command_start_utc","last_command_delivered_utc","last_command_result_utc","last_command_timeout_utc","last_poll_utc"};
        private readonly Dictionary<string, int> diagnosticCounts = new Dictionary<string, int>();

        internal MacWpsObjectBridge(int port, string resources, string input, string output, string cancelPath)
            : this(port, resources, input, output, cancelPath, null, "object-roundtrip") { }

        internal MacWpsObjectBridge(int port, string resources, string input, string output, string cancelPath, string carrier, string mode)
            : this(port,resources,input,output,cancelPath,carrier,mode,null) { }

        internal MacWpsObjectBridge(int port, string resources, string input, string output, string cancelPath, string carrier, string mode,string productNonce)
            : this(port,resources,input,output,cancelPath,carrier,mode,productNonce,"format") { }
        internal MacWpsObjectBridge(int port, string resources, string input, string output, string cancelPath, string carrier, string mode,string productNonce,string featureId)
            : this(port,resources,input,output,cancelPath,carrier,mode,productNonce,featureId,false) { }
        internal MacWpsObjectBridge(int port, string resources, string input, string output, string cancelPath, string carrier, string mode,string productNonce,string featureId,bool fixedProduct)
        {
            if(productNonce!=null&&(mode!="task-lease-format"||!System.Text.RegularExpressions.Regex.IsMatch(productNonce,"^[A-Za-z0-9_-]{32,128}$")))throw new InvalidDataException("MAC_PRODUCT_BINDING_INVALID");
            this.productNonce=productNonce;
            if(fixedProduct&&productNonce==null)throw new InvalidDataException("MAC_PRODUCT_BINDING_INVALID");
            this.fixedProduct=fixedProduct;
            if(productNonce!=null&&Array.IndexOf(new[]{"format","replace","redheader","rename","convert","pdf-to-word"},featureId)<0)throw new InvalidDataException("MAC_PRODUCT_FEATURE_NOT_ACCEPTED");
            this.featureId=featureId;
            if (port < 1024 || port > 65535) throw new ArgumentOutOfRangeException("port");
            this.resources = Path.GetFullPath(resources);
            this.input = Path.GetFullPath(input);
            this.output = Path.GetFullPath(output);
            this.carrier = carrier == null ? null : Path.GetFullPath(carrier);
            reservedDocumentPaths.Add(this.input);reservedDocumentPaths.Add(this.output);if(this.carrier!=null)reservedDocumentPaths.Add(this.carrier);
            this.mode = mode;
            this.cancelPath = cancelPath;
            if (this.input == this.output || !File.Exists(this.input)) throw new InvalidDataException("门槛副本路径无效。");
            byte[] random = new byte[32];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(random);
            session = Convert.ToBase64String(random);
            origin = "http://127.0.0.1:" + port;
            listener.Prefixes.Add(origin + "/");
            listener.Start();
            Thread thread = new Thread(Serve) { IsBackground = true };
            thread.Start();
        }

        internal string Url { get { return fixedProduct?origin+ProductPrefix:Endpoint + "/"; } }
        private bool IsDualMode { get { return mode == "dual-instance-a-read-only" || mode == "dual-instance-b-read-only"; } }
        internal bool IsTaskLeaseMode { get { return mode == "task-lease-format" || mode == "task-lease-undo-domain" || mode == "task-lease-undo-effect" || (mode == "task-lease-range-primitive" || mode == "task-lease-range-primitive-duplicate"); } }
        private bool UsesBindings { get { return IsDualMode || IsTaskLeaseMode; } }
        internal bool IsServing { get { return listener.IsListening; } }
        internal void CheckQaCoordinationIdle()
        { lock(sync) { if(disposed || failed || !listener.IsListening) throw new InvalidOperationException("MAC_QA_CHANNEL_UNAVAILABLE"); if(pending!=null) throw new InvalidOperationException("MAC_QA_COORDINATION_PENDING"); } }
        internal Dictionary<string, object> QaTimeline()
        { lock(sync) return new Dictionary<string, object>(timeline); }
        // 仅首次运输超时的白名单快照；不含请求、路径、正文或会话凭证。
        // last_poll只截至超时冻结时；delivered只表示服务器选入poll响应（Respond之前），不证明JS收到或执行。
        internal Dictionary<string, object> FirstTransportTimeout()
        {
            lock(sync) {
                if(firstTransportTimeout==null)return null;
                var snapshot=new Dictionary<string,object>();
                foreach(string field in TransportTimeoutFields)snapshot[field]=firstTransportTimeout[field];
                return snapshot;
            }
        }
        private void FreezeFirstTransportTimeout()
        {
            // 调用方持有sync；必须在pending清理与身份失效之前冻结，未知时间保留null。
            if(firstTransportTimeout!=null)return;
            firstTransportTimeout=new Dictionary<string,object>();
            foreach(string field in TransportTimeoutFields) {
                object value;timeline.TryGetValue(field,out value);
                if(value!=null&&field=="last_command_member")value=TraceName(value as string);
                if(value!=null&&field=="last_command_operation")value=Array.IndexOf(new[]{"get","set","call","inspect"},value as string)>=0?value:"other";
                firstTransportTimeout[field]=value;
            }
        }
        // QA定位样式缓存身份：只记录受控类型/原id，无session token、正文或路径，无额外RPC。
        internal void QaRecordIdentity(string interfaceName,int objectId,long identity,string callerClass,string callerMethod)
        {
            if(!IsTaskLeaseMode)return;
            lock(sync)
            {
                List<Dictionary<string,object>> entries;
                if(!timeline.ContainsKey("identity_trace")){entries=new List<Dictionary<string,object>>();timeline["identity_trace"]=entries;timeline["identity_trace_total"]=0;timeline["identity_trace_truncated"]=false;}
                else entries=(List<Dictionary<string,object>>)timeline["identity_trace"];
                timeline["identity_trace_total"]=(int)timeline["identity_trace_total"]+1;
                if(entries.Count>=128){timeline["identity_trace_truncated"]=true;return;}
                bool knownDocument=documentIdentities.ContainsKey(objectId);
                entries.Add(new Dictionary<string,object>{{"checked_at_utc",DateTime.UtcNow.ToString("o")},{"interface",interfaceName},{"object_id",objectId},{"returned_identity",identity},{"bridge_session_bound",objectId!=0},{"generation",generation},{"task_open_cycle",knownDocument?(object)documentCycles[objectId]:null},{"task_open_cycle_observed",knownDocument},{"document_cycle_identity_proven",false},{"caller_class",callerClass},{"caller_method",callerMethod}});
            }
        }
        internal object Application(Type type) { wordAssembly=type.Assembly;return Wrap(1, type); }

        internal Dictionary<string, int> Diagnostics()
        { lock (sync) return new Dictionary<string, int>(diagnosticCounts); }

        internal void WaitForPlugin(TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow.Add(timeout);
            lock (sync)
            {
                while (!pluginConnected)
                {
                    if(timeline.ContainsKey("initialization_failure"))throw new InvalidOperationException("MAC_PRODUCT_INITIALIZATION_FAILED:"+Convert.ToString(((Dictionary<string,object>)timeline["initialization_failure"])["error_code"]));
                    if (disposed || (cancelPath != null && File.Exists(cancelPath))) throw new OperationCanceledException("Mac 门槛启动已取消。");
                    if (DateTime.UtcNow >= deadline) throw new TimeoutException("MAC_PLUGIN_HANDSHAKE_TIMEOUT：插件未完成轮询握手；尚未发出对象调用。");
                    Monitor.Wait(sync, 100);
                }
            }
        }

        internal object Wrap(int id, Type type)
        {
            string key = id + ":" + type.FullName;
            lock (sync)
            {
                object result;
                if (proxies.TryGetValue(key, out result)) return result;
                result = type == typeof(object)
                    ? (object)new MacWpsDynamicObject(this, id)
                    : new MacWpsObjectProxy(this, id, type).GetTransparentProxy();
                proxies.Add(key, result);
                return result;
            }
        }

        internal object Encode(object value)
        {
            if (value == Type.Missing || value == Missing.Value) return new Dictionary<string, object> { { "kind", "missing" } };
            MacWpsDynamicObject dynamicValue = value as MacWpsDynamicObject;
            if (dynamicValue != null)
            {
                if (dynamicValue.Bridge != this) throw new InvalidOperationException("跨会话对象不可传递。");
                return new Dictionary<string, object> { { "kind", "object" }, { "id", dynamicValue.Id } };
            }
            if (value != null && RemotingServices.IsTransparentProxy(value))
            {
                MacWpsObjectProxy proxy = RemotingServices.GetRealProxy(value) as MacWpsObjectProxy;
                if (proxy == null || proxy.Bridge != this) throw new InvalidOperationException("跨会话对象不可传递。");
                return new Dictionary<string, object> { { "kind", "object" }, { "id", proxy.Id } };
            }
            if (value != null && value.GetType().IsEnum) value = Convert.ToInt32(value);
            if (value != null && !(value is string || value is bool || value.GetType().IsPrimitive || value is decimal))
                throw new InvalidDataException("Mac 通道不接受未声明的参数类型。");
            return new Dictionary<string, object> { { "kind", "value" }, { "value", value } };
        }

        internal object Decode(object wire, Type type, bool allowStyle = false)
        {
            if (type.IsByRef) type = type.GetElementType();
            Dictionary<string, object> value = wire as Dictionary<string, object>;
            if (value == null) throw new InvalidDataException("加载项返回类型描述无效。");
            string kind = Convert.ToString(value["kind"]);
            if (kind == "missing") return Type.Missing;
            if (kind == "object")
            {
                // 仅任务Range.Style的有限object返回契约，其他对象提示不扩张为Office权限。
                object runtimeType;
                if(value.TryGetValue("runtime_type",out runtimeType))
                {
                    if(!allowStyle||!IsTaskLeaseMode||type!=typeof(object)||Convert.ToString(runtimeType)!="Microsoft.Office.Interop.Word.Style"||wordAssembly==null)
                        throw new InvalidDataException("MAC_LEASE_RESULT_TYPE_UNVERIFIABLE");
                    type=wordAssembly.GetType("Microsoft.Office.Interop.Word.Style",true);
                }
                int id=Convert.ToInt32(value["id"]);
                if(IsTaskLeaseMode&&IsDocumentType(type))try{BindDocumentIdentity(id,type,value);}catch{lock(sync)InvalidateDocumentIdentity();throw;}
                return Wrap(id, type);
            }
            if (kind != "value") throw new InvalidDataException("加载项返回类型未知。");
            object scalar = value["value"];
            if (type == typeof(void) || scalar == null) return null;
            if (type == typeof(object)) return scalar;
            return type.IsEnum ? Enum.ToObject(type, scalar) : Convert.ChangeType(scalar, type);
        }

        internal object Call(int id, string operation, string member, object[] args, Type resultType, ParameterInfo[] parameters)
        {
            int callSequence=0;string localStage="forward";
            try {
            if(productNonce!=null){
                // 原替换当前DOC/WPS的分支会删除源；此候选不授予该原调用链权限。
                var frames=new System.Diagnostics.StackTrace(false).GetFrames();
                if(frames!=null)foreach(var frame in frames){var method=frame.GetMethod();if(method!=null&&method.DeclaringType!=null&&method.DeclaringType.Assembly==wordAssembly&&method.DeclaringType.FullName=="DocumentRepository.Services.Conversion.DocumentExportService"&&method.Name=="ExportDocxReplacingCurrent")throw new InvalidOperationException("MAC_PRODUCT_REPLACE_CURRENT_DENIED");}
            }
            if(IsTaskLeaseMode) lock(sync) {
                if(timeline.ContainsKey("bootstrap_rejected_attempts")){InvalidateDocumentIdentity();throw new InvalidOperationException("MAC_LEASE_CONTEXT_REINITIALIZED");}
                if((documentState=="closed"||documentState=="invalidated")&&!(id==1&&operation=="inspect"&&(member=="LeaseFinish"||member=="LeaseInspectUndoState"||member=="LeaseReleaseObject"||(mode=="task-lease-undo-effect"&&member=="LeaseInspectUndoEffect")))){localStage="localexpired";throw new InvalidOperationException("MAC_LEASE_DOCUMENT_CAPABILITY_EXPIRED");}
            }
            if(IsTaskLeaseMode && operation=="call" && (member=="SaveAs2"||member=="ExportAsFixedFormat"))
            {
                bool pdf=member=="ExportAsFixedFormat";
                if(args.Length!=(pdf?15:17) || !(args[0] is string)||pdf&&productNonce==null)throw new InvalidDataException("MAC_LEASE_SAVEAS_CONTRACT_INVALID");
                string path=Path.GetFullPath((string)args[0]),root=Path.GetFullPath(Path.GetDirectoryName(output))+Path.DirectorySeparatorChar;
                int ownerCycle;
                if(productNonce!=null&&member=="SaveAs2"&&documentCycles.TryGetValue(id,out ownerCycle)&&ownerCycle==2&&reservedDocumentPaths.Contains(path))throw new InvalidDataException("MAC_LEASE_EXPORT_RESERVED_PATH_DENIED");
                if(!path.StartsWith(root,Path.DirectorySeparatorChar=='\\'?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal)
                    || !string.Equals(Path.GetExtension(path),pdf?".pdf":".docx",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("MAC_LEASE_ARTIFACT_PATH_DENIED");
                Call(1,"inspect","LeaseRegisterArtifact",new object[]{path},typeof(bool),null);
            }
            object[] encoded = new object[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                if (parameters != null && parameters[i].IsOut) throw new InvalidDataException("WPS JS 真正 out 参数契约尚未验证，拒绝调用。");
                encoded[i] = Encode(args[i]);
            }
            Dictionary<string, object> received;
            localStage="transport";
            lock (sync)
            {
                if (disposed || failed) throw new ObjectDisposedException("Mac WPS session");
                if (pending != null) throw new InvalidOperationException("Mac 对象调用必须串行。");
                pending = new Dictionary<string, object> {
                    { "schema", 1 }, { "session", session }, { "id", ++sequence },
                    { "object", id }, { "operation", operation }, { "member", member }, { "args", encoded }
                };
                callSequence=sequence;
                if (UsesBindings) { pending["generation"] = generation; pending["endpoint"] = Endpoint; }
                if (IsTaskLeaseMode) pending["result_type"]=resultType.FullName;
                delivered = false;
                reply = null;
                DateTime deadline = DateTime.UtcNow.AddSeconds(30);
                if(UsesBindings) { timeline["last_command_id"]=sequence;timeline["last_command_operation"]=operation;timeline["last_command_member"]=member;timeline["last_command_start_utc"]=DateTime.UtcNow.ToString("o");timeline["last_command_delivered_utc"]=null;timeline["last_command_result_utc"]=null;timeline["last_command_timeout_utc"]=null; }
                try
                {
                    while (reply == null)
                    {
                        // 产品取消由原CancellationToken协作；已投递调用继续等待有限确认，不制造未知写或阻断原清理。
                        if (disposed || (productNonce==null&&cancelPath != null && File.Exists(cancelPath))) throw new OperationCanceledException("Mac 任务已取消。");
                        if (DateTime.UtcNow >= deadline) { if(UsesBindings) { timeline["last_command_timeout_utc"]=DateTime.UtcNow.ToString("o");FreezeFirstTransportTimeout(); } throw new TimeoutException(UsesBindings ? "MAC_QA_OBJECT_CALL_TIMEOUT" : "Mac 加载项对象调用超时；结果未知，禁止自动重放。"); }
                        Monitor.Wait(sync, 100);
                    }
                    received = reply;
                }
                catch { failed = true; InvalidateDocumentIdentity(); throw; }
                finally { pending = null; reply = null; }
            }
            if((mode=="task-lease-range-primitive"||mode=="task-lease-range-primitive-duplicate")&&received.ContainsKey("range_primitive"))lock(sync)timeline["range_primitive_last_receipt"]=received["range_primitive"];
            localStage="observe";
            try{ObserveDocumentState(received);}catch{lock(sync)InvalidateDocumentIdentity();throw;}
            if(IsTaskLeaseMode&&(documentState=="invalidated"||documentState=="closed"))RecordFirstIdentityInvalidation(callSequence,id,operation,member,resultType,localStage,received.ContainsKey("error")?Convert.ToString(received["error"]):documentState=="closed"?"MAC_LEASE_DOCUMENT_CLOSED":"MAC_LEASE_DOCUMENT_STATE_INVALIDATED","none");
            localStage="forward";
            if (received.ContainsKey("error"))
            {
                if(IsTaskLeaseMode) lock(sync) { timeline["last_object_error_member"]=member;timeline["last_object_error_operation"]=operation;timeline["last_object_error_code"]=Convert.ToString(received["error"]); }
                throw new InvalidOperationException("Mac 加载项对象异常：" + Convert.ToString(received["error"]));
            }
            // JS 对输入 ref 参数返回原值；真正 out 参数已在调用前拒绝。
            localStage="decode";
            IList returnedArgs = received["args"] as IList;
            if (returnedArgs == null || returnedArgs.Count != args.Length) throw new InvalidDataException("Mac ref 参数响应不完整。");
            if (parameters != null)
                for (int i = 0; i < args.Length; i++)
                    if (parameters[i].ParameterType.IsByRef) args[i] = Decode(returnedArgs[i], parameters[i].ParameterType);
            if(productNonce!=null&&operation=="call"&&member=="SaveAs2"){int cycle;if(documentCycles.TryGetValue(id,out cycle)&&cycle==1)reservedDocumentPaths.Add(Path.GetFullPath((string)args[0]));}
            try{return Decode(received["result"], resultType,IsTaskLeaseMode&&operation=="get"&&member=="Style"&&resultType==typeof(object));}
            catch{lock(sync)InvalidateDocumentIdentity();throw;}
            } catch(Exception error) {
                if(IsTaskLeaseMode){RecordFailure(callSequence,id,operation,member,resultType,localStage,error);if(documentState=="invalidated"||documentState=="closed")RecordFirstIdentityInvalidation(callSequence,id,operation,member,resultType,localStage,error.Message,error.GetType().FullName);}
                throw;
            }
        }

        // 仅有界诊断元数据；不记录参数、URL、nonce、路径、正文或原异常文本。
        private static string TraceCode(string message)
        {var match=System.Text.RegularExpressions.Regex.Match(message??"","MAC_(?:LEASE|UNDO|PRODUCT|QA|DUAL)_[A-Z_]+");return match.Success&&TraceCodes.Contains(match.Value)?match.Value:"MAC_TRACE_CODE_UNAVAILABLE";}
        private static readonly HashSet<string> TraceCodes=new HashSet<string>("MAC_LEASE_TABLE_CELL_BINDING_INVALID MAC_LEASE_TABLE_CELL_OWNER_UNVERIFIABLE MAC_LEASE_TABLE_CELL_RETURN_UNVERIFIABLE MAC_LEASE_ACTIVATE_BINDING_INVALID MAC_LEASE_ACTIVATE_RESULT_INVALID MAC_LEASE_ADD_LINE_BINDING_INVALID MAC_LEASE_ADD_LINE_OWNER_UNVERIFIABLE MAC_LEASE_ADD_LINE_RETURN_UNVERIFIABLE MAC_LEASE_VISIBLE_STATE_UNVERIFIABLE MAC_DUAL_BOOTSTRAP_BINDING_MISMATCH MAC_LEASE_ACQUIRE_UNCONFIRMED MAC_LEASE_ACTIVE_CONTEXT_CHANGED MAC_LEASE_ALIAS_UNVERIFIABLE MAC_LEASE_ALREADY_RELEASED MAC_LEASE_ARTIFACT_PATH_DENIED MAC_LEASE_BOOTSTRAP_CLOSE_UNCONFIRMED MAC_LEASE_BOOTSTRAP_FOREIGN MAC_LEASE_BOOTSTRAP_INVALID MAC_LEASE_CHILD_TYPE_UNVERIFIABLE MAC_LEASE_CLEANUP_UNCONFIRMED MAC_LEASE_CLEANUP_UNVERIFIABLE MAC_LEASE_CLEAR_FORMATTING_BINDING_INVALID MAC_LEASE_CLOSE_UNCONFIRMED MAC_LEASE_CLOSE_UNKNOWN_NO_REPLAY MAC_LEASE_COLLECTION_CHANGED MAC_LEASE_COLLECTION_ITEM_UNAVAILABLE MAC_LEASE_COMMAND_INVALID MAC_LEASE_COMMAND_STALE MAC_LEASE_CONTEXT_REINITIALIZED MAC_LEASE_COUNT_INVALID MAC_LEASE_DIRECT_FORMAT_RESET_BINDING_INVALID MAC_LEASE_DIRECT_FORMAT_RESET_RESULT_INVALID MAC_LEASE_DOCUMENT_ANCHOR_UNAVAILABLE MAC_LEASE_DOCUMENT_CAPABILITY_EXPIRED MAC_LEASE_DOCUMENT_CLOSED MAC_LEASE_DOCUMENT_DESCRIPTOR_INVALID MAC_LEASE_DOCUMENT_IDENTITY_EXPIRED MAC_LEASE_DOCUMENT_IDENTITY_SOURCE_INVALID MAC_LEASE_DOCUMENT_IDENTITY_UNCONFIRMED MAC_LEASE_DOCUMENT_STATE_CHANGED MAC_LEASE_DOCUMENT_STATE_INVALID MAC_LEASE_DOCUMENT_STATE_INVALIDATED MAC_LEASE_DOCUMENTS_MUTATION_DENIED MAC_LEASE_EMPTY_SESSION_UNCONFIRMED MAC_LEASE_EXPORT_ADD_BINDING_INVALID MAC_LEASE_EXPORT_ADD_RETURN_INVALID MAC_LEASE_EXPORT_APPLICATION_DENIED MAC_LEASE_EXPORT_CLOSE_UNAVAILABLE MAC_LEASE_EXPORT_CLOSE_UNCONFIRMED MAC_LEASE_EXPORT_MEMBER_DENIED MAC_LEASE_EXPORT_OBJECT_DENIED MAC_LEASE_EXPORT_RESERVED_PATH_DENIED MAC_LEASE_EXPORT_STATE_CHANGED MAC_LEASE_EXPORT_STATE_INVALID MAC_LEASE_EXPORT_XML_INVALID MAC_LEASE_EXTERNAL_MUTATION_DENIED MAC_LEASE_FACTORY_UNAVAILABLE MAC_LEASE_FINISH_UNCONFIRMED MAC_LEASE_FINISH_UNKNOWN_NO_REPLAY MAC_LEASE_FOREIGN_CLOSE_DENIED MAC_LEASE_FOREIGN_DOCUMENT MAC_LEASE_FOREIGN_SAVE_DENIED MAC_LEASE_GLOBAL_MUTATION_DENIED MAC_LEASE_IDENTITY_SEQUENCE_EXHAUSTED MAC_LEASE_INDEXED_MEMBER_DENIED MAC_LEASE_INSPECTION_UNKNOWN MAC_LEASE_LIST_FORMAT_BINDING_INVALID MAC_LEASE_METHOD_UNAVAILABLE MAC_LEASE_NATIVE_CALL_FAILED MAC_LEASE_NO_TASK_DOCUMENT MAC_LEASE_OBJECT_EXPIRED MAC_LEASE_OBJECT_FOREIGN MAC_LEASE_OPEN_BINDING_INVALID MAC_LEASE_OPEN_RETURN_INVALID MAC_LEASE_OPERATION_UNAVAILABLE MAC_LEASE_PAGE_NUMBERS_API_UNVERIFIABLE MAC_LEASE_PAGE_NUMBERS_MEMBER_DENIED MAC_LEASE_PARENT_EXPIRED MAC_LEASE_PARENT_UNVERIFIABLE MAC_LEASE_PDF_BINDING_INVALID MAC_LEASE_PRIMARY_DURING_EXPORT_DENIED MAC_LEASE_PROPERTY_API_UNVERIFIABLE MAC_LEASE_PROPERTY_OWNER_UNVERIFIABLE MAC_LEASE_QUIT_REFERENCE_INVALID MAC_LEASE_REACTIVATION_DENIED MAC_LEASE_RESULT_TYPE_UNVERIFIABLE MAC_LEASE_SAVEAS_CONTRACT_INVALID MAC_LEASE_SAVEAS_RETURN_INVALID MAC_LEASE_SHAPE_RANGE_API_UNVERIFIABLE MAC_LEASE_SNAPSHOT_WRITE_DENIED MAC_LEASE_STYLE_OWNER_UNVERIFIABLE MAC_LEASE_TASK_PATH_CHANGED MAC_LEASE_UNAVAILABLE MAC_LEASE_UNIT_CONVERSION_BINDING_INVALID MAC_LEASE_UNIT_CONVERSION_RESULT_INVALID MAC_LEASE_WRITE_MEMBER_DENIED MAC_LEASE_ZERO_DOCUMENTS_REQUIRED MAC_PRODUCT_BINDING_INVALID MAC_PRODUCT_CLOSE_EVENT_UNAVAILABLE MAC_PRODUCT_CLOSE_EVENT_UNCONFIRMED MAC_PRODUCT_FEATURE_NOT_ACCEPTED MAC_PRODUCT_INITIALIZATION_FAILED MAC_PRODUCT_REPLACE_CURRENT_DENIED MAC_QA_CHANNEL_UNAVAILABLE MAC_QA_COORDINATION_PENDING MAC_QA_OBJECT_CALL_TIMEOUT MAC_UNDO_ACTIVE_STATE_CHANGED MAC_UNDO_BOUNDARY_UNCONFIRMED MAC_UNDO_DOCUMENT_BINDING_INVALID MAC_UNDO_DOCUMENT_REOPEN_DENIED MAC_UNDO_DOMAIN_API_UNAVAILABLE MAC_UNDO_DOMAIN_MODE_OPERATION_DENIED MAC_UNDO_DOMAIN_NATIVE_CALL_FAILED MAC_UNDO_DOMAIN_PRECONDITION_FAILED MAC_UNDO_DOMAIN_STATE_MISMATCH MAC_UNDO_EFFECT_CHANGE_UNCONFIRMED MAC_UNDO_EFFECT_CLEANUP_DENIED MAC_UNDO_EFFECT_END_RESULT_UNKNOWN MAC_UNDO_EFFECT_FONT_PRECONDITION_FAILED MAC_UNDO_EFFECT_MODE_OPERATION_DENIED MAC_UNDO_EFFECT_NATIVE_CALL_FAILED MAC_UNDO_EFFECT_PRECONDITION_FAILED MAC_UNDO_EFFECT_RESTORE_UNCONFIRMED MAC_UNDO_EFFECT_START_RESULT_UNKNOWN MAC_UNDO_EFFECT_STATE_MISMATCH MAC_UNDO_EFFECT_UNDO_RESULT_UNCONFIRMED MAC_UNDO_END_NOT_REPLAYABLE MAC_UNDO_EXIT_STATE_UNVERIFIED MAC_UNDO_EXIT_UNCONFIRMED MAC_UNDO_IDLE_STATE_CHANGED MAC_UNDO_NATIVE_API_UNAVAILABLE MAC_UNDO_OPEN_CYCLE_OR_GENERATION_CHANGED MAC_UNDO_OPEN_CYCLE_UNVERIFIED MAC_UNDO_PREEXISTING_RECORD MAC_UNDO_RECORD_NAME_CHANGED MAC_UNDO_RECORD_OWNERSHIP_UNPROVEN MAC_UNDO_SCOPE_INVALID MAC_UNDO_SCOPE_OPERATION_DENIED MAC_UNDO_START_NOT_REPLAYABLE MAC_UNDO_STATE_API_UNAVAILABLE MAC_UNDO_WRAPPER_STATE_DIVERGED".Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries),StringComparer.Ordinal);
        private static string TraceName(string value)
        {return System.Text.RegularExpressions.Regex.IsMatch(value??"","^[A-Za-z_][A-Za-z0-9_.+]*$")?value:"other";}
        private Dictionary<string,object> FailureMetadata(int seq,int id,string op,string member,Type type,string stage,string message,string exceptionType)
        {return new Dictionary<string,object>{{"seq",seq==0?null:(object)seq},{"object",id},{"op",Array.IndexOf(new[]{"get","set","call","inspect"},op)>=0?op:"other"},{"member",TraceName(member)},{"result_type",TraceName(type==null?null:type.FullName)},{"localstage",stage},{"code",TraceCode(message)},{"type",TraceName(exceptionType)}};}
        private void RecordFailure(int seq,int id,string op,string member,Type type,string stage,Exception error)
        {
            lock(sync){
                List<Dictionary<string,object>> entries;
                if(!timeline.ContainsKey("failure_trace")){entries=new List<Dictionary<string,object>>();timeline["failure_trace"]=entries;timeline["failure_trace_total"]=0;timeline["failure_trace_truncated"]=false;}else entries=(List<Dictionary<string,object>>)timeline["failure_trace"];
                timeline["failure_trace_total"]=(int)timeline["failure_trace_total"]+1;
                var metadata=FailureMetadata(seq,id,op,member,type,stage,error.Message,error.GetType().FullName);
                string code=(string)metadata["code"];
                // 总数仍含全部失败；已知全局选项拒绝/子类型噪声不占诊断槽。
                if(code=="MAC_LEASE_CHILD_TYPE_UNVERIFIABLE" ||
                   (code=="MAC_LEASE_GLOBAL_MUTATION_DENIED" && op=="set" &&
                    Array.IndexOf(new[]{"Visible","DisplayAlerts","ScreenUpdating"},member)>=0))
                {timeline["failure_trace_truncated"]=true;return;}
                if(entries.Count>=8){
                    timeline["failure_trace_truncated"]=true;
                    if(code!="MAC_LEASE_METHOD_UNAVAILABLE")return;
                    // 保留最早的METHOD缺口；后来的同类不得挤掉它，未知写不重放。
                    int replace=entries.FindLastIndex(item=>(string)item["code"]!="MAC_LEASE_METHOD_UNAVAILABLE");
                    if(replace<0)return;
                    entries.RemoveAt(replace);
                }
                entries.Add(metadata);
            }
        }
        private void RecordFirstIdentityInvalidation(int seq,int id,string op,string member,Type type,string stage,string message,string exceptionType)
        {lock(sync)if(!timeline.ContainsKey("first_identity_invalidation"))timeline["first_identity_invalidation"]=FailureMetadata(seq,id,op,member,type,stage,message,exceptionType);}

        private void Serve()
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try { context = listener.GetContext(); }
                catch (HttpListenerException) { return; }
                catch (ObjectDisposedException) { return; }
                try { Handle(context); }
                catch (Exception) { try { Respond(context, 400, "{}", "application/json"); } catch { } }
            }
        }

        private void Handle(HttpListenerContext context)
        {
            string path = context.Request.Url.AbsolutePath;
            string requestOrigin = context.Request.Headers["Origin"];
            if (requestOrigin != null && requestOrigin != origin) { Respond(context, 403, "{}", "application/json"); return; }
            // 固定产品资源没有任务参数；只有真实载体识别出的私有路径能领取能力。
            if(fixedProduct&&context.Request.HttpMethod=="GET"&&path.StartsWith(ProductPrefix,StringComparison.Ordinal))
            {
                string resource=path.Substring(ProductPrefix.Length);
                string file=resource==""||resource=="index.html"?"product-index.html":resource=="ribbon.xml"?"product-ribbon.xml":resource;
                if(file!="product-index.html"&&file!="product-ribbon.xml"&&file!="product-main.js"&&file!="task-lease.js"){Respond(context,404,"{}","application/json");return;}
                Respond(context,200,File.ReadAllText(Path.Combine(resources,file)),file.EndsWith(".js")?"application/javascript":file.EndsWith(".html")?"text/html":"application/xml");return;
            }
            if(productNonce!=null){string prefix="/p/"+productNonce;if(!path.StartsWith(prefix+"/",StringComparison.Ordinal)){Respond(context,403,"{}","application/json");return;}path=path.Substring(prefix.Length);}
            if (context.Request.HttpMethod == "GET" && (path == "/" || path == "/index.html" || path == "/main.js" || path == "/ribbon.xml" || (IsTaskLeaseMode && path == "/task-lease.js")))
            {
                if (path == "/" || path == "/index.html")
                {
                    Respond(context, 200, "<!doctype html><html><head><meta charset=\"utf-8\"></head><body>"+(IsTaskLeaseMode?"<script src=\"task-lease.js\"></script>":"")+"<script src=\"main.js\"></script></body></html>", "text/html");
                    return;
                }
                string file = path == "/" ? "ribbon.xml" : path.Substring(1);
                if(productNonce!=null&&file=="ribbon.xml")file="product-ribbon.xml";
                string body = File.ReadAllText(Path.Combine(resources, file));
                if (file.EndsWith(".js")) body = "this.PartyOpsMacEndpoint=" + json.Serialize(Endpoint) + ";\n" + body;
                Respond(context, 200, body, file.EndsWith(".js") ? "application/javascript" : "application/xml");
                return;
            }
            if (context.Request.HttpMethod == "GET" && path == "/diagnostic")
            {
                string diagnostic = UsesBindings
                    ? json.Serialize(new {schema = 1, backend = Backend, request_stages = Diagnostics(), qa_timeline = QaTimeline()})
                    : json.Serialize(new {schema = 1, backend = Backend, request_stages = Diagnostics()});
                Respond(context, 200, diagnostic, "application/json");
                return;
            }
            if (context.Request.HttpMethod == "GET" && path == "/bootstrap")
            {
                if(fixedProduct&&context.Request.QueryString["carrier"]!=carrier){Respond(context,403,"{}","application/json");return;}
                if(UsesBindings) lock(sync)
                {
                    if(disposed||failed){Respond(context,410,"{}","application/json");return;}
                    if(bootstrapClaimed) { timeline["bootstrap_rejected_attempts"]=timeline.ContainsKey("bootstrap_rejected_attempts") ? (int)timeline["bootstrap_rejected_attempts"]+1 : 1;timeline["last_bootstrap_rejected_utc"]=DateTime.UtcNow.ToString("o");if(productNonce!=null){failed=true;InvalidateDocumentIdentity();Monitor.PulseAll(sync);}Respond(context,409,"{}","application/json");return; }
                    bootstrapClaimed=true;timeline["bootstrap_claimed_utc"]=DateTime.UtcNow.ToString("o");
                }
                Respond(context, 200, json.Serialize(new { schema = 1, backend = Backend, session = session, input = input, output = output, carrier = carrier, mode = mode, generation = generation, endpoint = Endpoint, product_task=productNonce!=null,feature_id=featureId }), "application/json");
                return;
            }
            if(context.Request.HttpMethod=="POST"&&path=="/initialization-error"&&productNonce!=null){
                if(context.Request.ContentLength64<0||context.Request.ContentLength64>2048){Respond(context,413,"{}","application/json");return;}
                Dictionary<string,object> report;using(var reader=new StreamReader(context.Request.InputStream,Encoding.UTF8))report=json.Deserialize<Dictionary<string,object>>(reader.ReadToEnd());
                string[] keys={"schema","stage","lease_factory_type","app_obtained","api_event_available","add_listener_type","wps_api_event_available","wps_add_listener_type","event_source","registration_attempted","registration_returned","error_code","error_type"};
                if(report==null||report.Count!=keys.Length||Array.Exists(keys,k=>!report.ContainsKey(k))||Convert.ToInt32(report["schema"])!=1
                    ||Array.IndexOf(new[]{"json","schema","bindings","application","api-event","lease-factory"},Convert.ToString(report["stage"]))<0
                    ||Array.IndexOf(new[]{"MAC_BOOTSTRAP_SCHEMA_INVALID","MAC_DUAL_BOOTSTRAP_BINDING_MISMATCH","MAC_PRODUCT_CLOSE_EVENT_UNAVAILABLE","MAC_LEASE_FACTORY_UNAVAILABLE","MAC_BOOTSTRAP_OR_WPS_INITIALIZATION_FAILED"},Convert.ToString(report["error_code"]))<0
                    ||Array.IndexOf(new[]{"Error","TypeError","SyntaxError","other"},Convert.ToString(report["error_type"]))<0
                    ||Array.IndexOf(new[]{"undefined","object","function","boolean","number","string","symbol","bigint"},Convert.ToString(report["lease_factory_type"]))<0
                    ||Array.IndexOf(new[]{"undefined","object","function","boolean","number","string","symbol","bigint"},Convert.ToString(report["add_listener_type"]))<0
                    ||Array.IndexOf(new[]{"undefined","object","function","boolean","number","string","symbol","bigint"},Convert.ToString(report["wps_add_listener_type"]))<0
                    ||Array.IndexOf(new[]{"application","wps-global","unavailable"},Convert.ToString(report["event_source"]))<0
                    ||!(report["app_obtained"] is bool)||!(report["api_event_available"] is bool)||!(report["wps_api_event_available"] is bool)||!(report["registration_attempted"] is bool)||!(report["registration_returned"] is bool)){Respond(context,400,"{}","application/json");return;}
                lock(sync){
                    if(!bootstrapClaimed||pluginConnected||failed||disposed||timeline.ContainsKey("initialization_failure")){Respond(context,409,"{}","application/json");return;}
                    Respond(context,200,"{}","application/json");timeline["initialization_failure"]=report;failed=true;InvalidateDocumentIdentity();Monitor.PulseAll(sync);
                }return;
            }
            if (context.Request.QueryString["session"] != session) { Respond(context, 403, "{}", "application/json"); return; }
            if (UsesBindings && (context.Request.QueryString["generation"] != generation || context.Request.QueryString["endpoint"] != Endpoint))
            { Respond(context, 403, "{}", "application/json"); return; }
            if (context.Request.HttpMethod == "GET" && path == "/poll")
            {
                string payload;
                lock (sync)
                {
                    if (disposed || failed) { Respond(context, 410, "{}", "application/json"); return; }
                    pluginConnected = true;
                    if(UsesBindings) timeline["last_poll_utc"]=DateTime.UtcNow.ToString("o");
                    Monitor.PulseAll(sync);
                    payload = pending != null && !delivered ? json.Serialize(pending) : "null";
                    if (pending != null && !delivered) { delivered = true; if(UsesBindings) timeline["last_command_delivered_utc"]=DateTime.UtcNow.ToString("o"); }
                }
                Respond(context, 200, payload, "application/json");
                return;
            }
            if (context.Request.HttpMethod == "POST" && path == "/result")
            {
                if (context.Request.ContentLength64 < 0 || context.Request.ContentLength64 > 1048576) { Respond(context, 413, "{}", "application/json"); return; }
                Dictionary<string, object> received;
                using (StreamReader reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                    received = json.Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
                lock (sync)
                {
                    if (failed || disposed || pending == null || !delivered || reply != null
                        || Convert.ToInt32(received["id"]) != sequence || Convert.ToString(received["session"]) != session
                        || (UsesBindings && (!received.ContainsKey("generation") || !received.ContainsKey("endpoint")
                            || Convert.ToString(received["generation"]) != generation || Convert.ToString(received["endpoint"]) != Endpoint)))
                    { Respond(context, 409, "{}", "application/json"); return; }
                    // 先完成HTTP ACK再发布结果。末次调用可能立即结束宿主，
                    // 若先唤醒调用线程，Dispose会竞态截断还未写完的200响应。
                    Respond(context, 200, "{}", "application/json");
                    if(UsesBindings) timeline["last_command_result_utc"]=DateTime.UtcNow.ToString("o");
                    reply = received;
                    Monitor.PulseAll(sync);
                }
                return;
            }
            Respond(context, 404, "{}", "application/json");
        }

        private void Respond(HttpListenerContext context, int status, string body, string type)
        {
            // 仅记录固定阶段/status/origin分类；不记录URL query、session、文档路径或正文。
            string path = context.Request.Url.AbsolutePath;
            if(productNonce!=null&&path.StartsWith("/p/"+productNonce+"/",StringComparison.Ordinal))path=path.Substring(("/p/"+productNonce).Length);
            string stage = path == "/" ? "index-root" : path == "/index.html" ? "index-html"
                : path == "/main.js" ? "script" : path == "/ribbon.xml" ? "ribbon"
                : path == "/bootstrap" ? "bootstrap" : path == "/poll" ? "poll"
                : path == "/result" ? "result" : path == "/diagnostic" ? "diagnostic" : path=="/task-lease.js"?"task-lease-script":path=="/initialization-error"?"initialization-error":"unknown-resource";
            string requestOrigin = context.Request.Headers["Origin"];
            string originClass = requestOrigin == null ? "absent" : requestOrigin == origin ? "same" : requestOrigin == "null" ? "opaque" : "other";
            string key = stage + ":" + status + ":" + originClass;
            bool first;
            lock (sync)
            {
                int count;
                diagnosticCounts.TryGetValue(key, out count);
                diagnosticCounts[key] = count + 1;
                first = count == 0;
            }
            if (first) Console.WriteLine("MAC_OBJECT_GATE_HTTP " + (IsDualMode
                ? json.Serialize(new {stage = stage, status = status, origin_class = originClass, qa_endpoint_port = context.Request.Url.Port})
                : json.Serialize(new {stage = stage, status = status, origin_class = originClass})));
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            context.Response.StatusCode = status;
            context.Response.ContentType = type + "; charset=utf-8";
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.Close();
        }

        public void Dispose()
        {
            lock (sync) { disposed = true; documentState="disposed";exportDocumentState="disposed"; proxies.Clear(); Monitor.PulseAll(sync); }
            listener.Close();
        }
    }

    internal sealed class MacWpsObjectProxy : RealProxy
    {
        internal readonly MacWpsObjectBridge Bridge;
        internal readonly int Id;
        private readonly Type interfaceType;
        internal Type InterfaceType { get { return interfaceType; } }
        internal MacWpsObjectProxy(MacWpsObjectBridge bridge, int id, Type type) : base(type)
        { Bridge = bridge; Id = id; interfaceType = type; }
        public override IMessage Invoke(IMessage message)
        {
            IMethodCallMessage call = (IMethodCallMessage)message;
            object[] callArgs = call.Args;
            try
            {
                MethodInfo method = (MethodInfo)call.MethodBase;
                object result;
                if(Bridge.IsTaskLeaseMode && method.Name=="GetEnumerator") result=new MacTaskCollectionEnumerator(Bridge,Id,interfaceType);
                else if (method.DeclaringType == typeof(object))
                {
                    if (method.Name == "GetHashCode") result = Id;
                    // 原快照会把Style.ToString纳入格式指纹；仅类型语义，不能混入临时传输句柄。
                    else if (method.Name == "ToString") result = Bridge.IsTaskLeaseMode&&interfaceType.FullName=="Microsoft.Office.Interop.Word.Style"
                        ? interfaceType.FullName : interfaceType.FullName + " (Mac WPS object " + Id + ")";
                    else if (method.Name == "Equals") result = ReferenceEquals(GetTransparentProxy(), call.Args[0]);
                    else throw new MissingMethodException(method.Name);
                }
                else
                {
                    string operation = method.Name.StartsWith("get_") ? "get" : method.Name.StartsWith("set_") ? "set" : "call";
                    string member = operation == "call" ? method.Name : method.Name.Substring(4);
                    result = Bridge.Call(Id, operation, member, callArgs, method.ReturnType, method.GetParameters());
                }
                return new ReturnMessage(result, callArgs, call.ArgCount, call.LogicalCallContext, call);
            }
            catch (Exception error) { return new ReturnMessage(error, call); }
        }
    }

    // 原interop IEnumerable接缝，只转发真实Count/Item，不参与排版。
    internal sealed class MacTaskCollectionEnumerator : IEnumerator
    {
        private readonly MacWpsObjectBridge bridge;private readonly int id;private readonly MethodInfo item;private int index;
        internal MacTaskCollectionEnumerator(MacWpsObjectBridge bridge,int id,Type type)
        {this.bridge=bridge;this.id=id;item=type.GetMethod("get_Item");if(item==null)throw new InvalidDataException("MAC_LEASE_COLLECTION_ITEM_UNAVAILABLE");}
        public bool MoveNext(){index++;return index<=Convert.ToInt32(bridge.Call(id,"get","Count",new object[0],typeof(int),null));}
        public object Current {get {return bridge.Call(id,"get","Item",new object[]{index},item.ReturnType,item.GetParameters());}}
        public void Reset(){index=0;}
    }

    internal sealed class MacWpsDynamicObject : DynamicObject
    {
        internal readonly MacWpsObjectBridge Bridge;
        internal readonly int Id;
        internal MacWpsDynamicObject(MacWpsObjectBridge bridge, int id) { Bridge = bridge; Id = id; }
        public override bool TryGetMember(GetMemberBinder binder, out object result)
        { result = Bridge.Call(Id, "get", binder.Name, new object[0], typeof(object), null); return true; }
        public override bool TrySetMember(SetMemberBinder binder, object value)
        { Bridge.Call(Id, "set", binder.Name, new[] { value }, typeof(void), null); return true; }
        public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result)
        { result = Bridge.Call(Id, "call", binder.Name, args, typeof(object), null); return true; }
    }
}
