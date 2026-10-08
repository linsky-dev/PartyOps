using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Remoting;
using System.Web.Script.Serialization;

namespace PartyOps.DocumentFormatter.Host
{
    // 原PortableOfficeHostRuntime五回调适配，管理任务引用，不接管用户Application生命周期。
    internal sealed class MacTaskLeaseRuntime
    {
        private readonly MacWpsObjectBridge bridge;private readonly object application;
        private readonly Assembly rules;
        private readonly MethodInfo registryRelease,registryTryGet,styleRelease,styleInvalidate;
        private readonly JavaScriptSerializer json=new JavaScriptSerializer();
        internal readonly Dictionary<string,object> State=new Dictionary<string,object>{{"cleanup_confirmed",false},{"lease_released",false}};
        internal MacTaskLeaseRuntime(Assembly rules,MacWpsObjectBridge bridge)
        {
            this.bridge=bridge;this.rules=rules;
            var registry=rules.GetType("DocumentRepository.Services.Hosting.DocumentLifecycleRegistry",true);
            var manager=rules.GetType("DocumentRepository.Services.Formatting.DocumentStyleManager",true);
            registryRelease=registry.GetMethod("Release");registryTryGet=registry.GetMethod("TryGet");styleRelease=manager.GetMethod("ReleaseDocument");styleInvalidate=manager.GetMethod("InvalidateCache");
            application=bridge.Application(rules.GetType("Microsoft.Office.Interop.Word.Application",true));
            rules.GetType("DocumentRepository.Services.Hosting.Standalone.PortableOfficeHostRuntime",true).GetMethod("Register").Invoke(null,
                new object[]{new Func<object>(Activate),new Func<object,bool>(Owns),new Func<object,long>(Identity),new Action<object>(Quit),new Action<object>(Release)});
        }
        private object Activate()
        {
            if(State.ContainsKey("lease_acquired"))throw new InvalidOperationException("MAC_LEASE_REACTIVATION_DENIED");
            var receipt=json.Deserialize<Dictionary<string,object>>((string)bridge.Call(1,"inspect","LeaseAcquire",new object[0],typeof(string),null));
            if(!(bool)receipt["lease_acquired"]||Convert.ToInt32(receipt["actual_count"])!=0)throw new InvalidOperationException("MAC_LEASE_ACQUIRE_UNCONFIRMED");
            State["lease_acquired"]=true;return application;
        }
        private int ObjectId(object value)
        {
            var dynamicValue=value as MacWpsDynamicObject;if(dynamicValue!=null&&dynamicValue.Bridge==bridge)return dynamicValue.Id;
            if(value!=null&&RemotingServices.IsTransparentProxy(value)){var proxy=RemotingServices.GetRealProxy(value) as MacWpsObjectProxy;if(proxy!=null&&proxy.Bridge==bridge)return proxy.Id;}
            return 0;
        }
        private bool IsDocumentValue(object value)
        {
            if(value==null)return false;
            if(RemotingServices.IsTransparentProxy(value)){
                var proxy=RemotingServices.GetRealProxy(value) as MacWpsObjectProxy;
                if(proxy!=null&&proxy.InterfaceType.Assembly==rules&&MacWpsObjectBridge.IsDocumentType(proxy.InterfaceType))return true;
            }
            return rules.GetType("Microsoft.Office.Interop.Word.Document",true).IsInstanceOfType(value)
                ||rules.GetType("Microsoft.Office.Interop.Word._Document",true).IsInstanceOfType(value);
        }
        private long ConfirmDocumentIdentity(object value,int id)
        {
            if(id==0)throw new InvalidOperationException("MAC_LEASE_DOCUMENT_IDENTITY_UNCONFIRMED");
            return bridge.DocumentIdentity(id,IsOriginalCleanup());
        }
        private bool Owns(object value){
            int id=ObjectId(value);
            // 两原接口同GUID并不保证IsInstanceOfType互认；明确原接口元数据优先。
            if(IsDocumentValue(value)&&(id==0||!bridge.HasDocumentOwnership(id)))throw new InvalidOperationException("MAC_LEASE_DOCUMENT_IDENTITY_UNCONFIRMED");
            return id!=0;
        }
        // 精确原Assembly方法元数据；TryGet单独出现不是清理，缺帧/inline未知拒绝。
        private bool IsOriginalCleanup()
        {
            var frames=new StackTrace(false).GetFrames();if(frames==null)return false;
            int registry=-1,release=-1,invalidate=-1;bool direct=false;
            for(int i=0;i<frames.Length;i++) {
                var method=frames[i].GetMethod();if(method==null||method.Module.Assembly!=rules)continue;
                if(method.Equals(registryRelease)){registry=i;direct=true;break;}
                if(method.Equals(registryTryGet))registry=i;
                if(method.Equals(styleInvalidate))invalidate=i;
                if(method.Equals(styleRelease)){release=i;break;}
            }
            return direct || registry>=0&&release>registry&&(invalidate<0||invalidate>registry&&invalidate<release);
        }
        private long Identity(object value)
        {
            int id=ObjectId(value);long identity=id;string type="other",callerClass="other",callerMethod="other";
            if(IsDocumentValue(value))identity=ConfirmDocumentIdentity(value,id);
            // 只读代理类型元数据及允许的原类方法名，不调用Office，不倾倒完整栈。
            try
            {
                if(value!=null&&RemotingServices.IsTransparentProxy(value)){
                    var proxy=RemotingServices.GetRealProxy(value) as MacWpsObjectProxy;
                    if(proxy!=null&&proxy.Bridge==bridge){var name=proxy.InterfaceType.FullName;if(name.StartsWith("Microsoft.Office.Interop.Word.",StringComparison.Ordinal))type=name;}
                }else if(value is MacWpsDynamicObject)type="System.Object";
                var frames=new StackTrace(false).GetFrames();
                if(frames!=null)foreach(var frame in frames){var method=frame.GetMethod();var owner=method==null?null:method.DeclaringType;if(owner==null)continue;
                    if(owner.FullName=="DocumentRepository.Services.Formatting.DocumentStyleManager"&&Array.IndexOf(new[]{"EnsureStylesScoped","EnsureStyles","GetRegisteredStyleDef","ReleaseDocument","InvalidateCache","ResetAllCaches"},method.Name)>=0){callerClass="DocumentStyleManager";callerMethod=method.Name;break;}
                    if(owner.FullName=="DocumentRepository.Services.Hosting.DocumentLifecycleRegistry"&&method.Name=="Release"){callerClass="DocumentLifecycleRegistry";callerMethod="Release";break;}
                    if(owner.FullName=="DocumentRepository.Services.Hosting.DocumentSession"){callerClass="DocumentSession";callerMethod="session";}
                }
            }catch{type="metadata-unavailable";callerClass="metadata-unavailable";callerMethod="metadata-unavailable";}
            bridge.QaRecordIdentity(type,id,identity,callerClass,callerMethod);
            return identity; // 仅有真实描述符的Document使用周期身份，其余对象原值不变。
        }
        private void Quit(object value){if(ObjectId(value)!=1)throw new InvalidOperationException("MAC_LEASE_QUIT_REFERENCE_INVALID");Finish();}
        private void Release(object value)
        {
            int id=ObjectId(value);if(id==1){if(!(bool)State["lease_released"])Finish();return;}
            if((bool)State["lease_released"])return;
            bridge.Call(1,"inspect","LeaseReleaseObject",new object[]{id},typeof(bool),null);
            State["released_object_references"]=State.ContainsKey("released_object_references")?(int)State["released_object_references"]+1:1;
        }
        internal void Finish()
        {
            if((bool)State["lease_released"])return;
            if(State.ContainsKey("finish_attempted"))throw new InvalidOperationException("MAC_LEASE_FINISH_UNKNOWN_NO_REPLAY");
            State["finish_attempted"]=true;
            try
            {
                var receipt=json.Deserialize<Dictionary<string,object>>((string)bridge.Call(1,"inspect","LeaseFinish",new object[0],typeof(string),null));
                foreach(var pair in receipt)State[pair.Key]=pair.Value;
                if(!(bool)State["cleanup_confirmed"]||!(bool)State["lease_released"])throw new InvalidOperationException("MAC_LEASE_FINISH_UNCONFIRMED");
            }
            catch(Exception error)
            {
                State["cleanup_failure_type"]=error.GetType().FullName;
                // 仅取本地已观测状态，不重发End/Close或迁移租约；通道失效则保留未知。
                try{State["undo_state"]=json.Deserialize<Dictionary<string,object>>((string)bridge.Call(1,"inspect","LeaseInspectUndoState",new object[0],typeof(string),null));}
                catch(Exception inspection){State["undo_state_inspection_failure_type"]=inspection.GetType().FullName;}
                throw;
            }
        }
    }
}
