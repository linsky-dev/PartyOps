using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Xml;
using System.Xml.Linq;

namespace PartyOps.DocumentFormatter.Host
{
    internal sealed class MacTaskRequest
    {
        public int schema { get; set; }
        public string nonce { get; set; }
        public string state_directory { get; set; }
        public string resources { get; set; }
        public string task_id { get; set; }
    }

    // 正式宿主只建立普通任务租约；诊断、故障注入和 QA 模式不能从请求进入。
    internal sealed class MacSessionCoordinator : IDisposable
    {
        internal static bool IsMac { get { return Environment.OSVersion.Platform==PlatformID.MacOSX || (Environment.OSVersion.Platform==PlatformID.Unix && File.Exists("/System/Library/CoreServices/SystemVersion.plist")); } }
        internal const string Warning="Intel Mac 有限候选：可能出现 WPS 窗口；请人工核对输出文字是否重复、字体、格式与分页。静默与原生回滚尚未验证。";
        private readonly string profile, carrier;
        private readonly Mutex mutex;
        private bool lockHeld, disposed;
        internal readonly MacWpsObjectBridge Bridge;
        internal MacTaskLeaseRuntime Runtime;
        internal readonly Dictionary<string,object> State=new Dictionary<string,object>{{"backend",MacWpsObjectBridge.Backend},{"silent",false},{"rollback_capability_verified",false},{"document_cycle_identity_proven",false},{"cleanup_confirmed",false},{"lease_released",false},{"registration_owned",false},{"capability_revoked",false}};

        [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint="_NSGetExecutablePath", CharSet=CharSet.Ansi, ExactSpelling=true)]
        private static extern int NativeExecutablePath(StringBuilder buffer,ref uint size);
        [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint="realpath", CharSet=CharSet.Ansi, ExactSpelling=true)]
        private static extern IntPtr NativeRealPath(string path,IntPtr resolved);
        [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint="free", ExactSpelling=true)]
        private static extern void NativeFree(IntPtr value);

        private static string DarwinExecutablePath()
        {
            // mkbundle的AppDomain目录跟随任务cwd；只以当前真实Mach-O为资源锚点。
            uint size=4096;
            var buffer=new StringBuilder((int)size);
            if(NativeExecutablePath(buffer,ref size)!=0)
            {
                if(size<1||size>1024*1024)throw new InvalidDataException("MAC_PRODUCT_EXECUTABLE_ANCHOR_UNAVAILABLE");
                buffer=new StringBuilder((int)size);
                if(NativeExecutablePath(buffer,ref size)!=0)throw new InvalidDataException("MAC_PRODUCT_EXECUTABLE_ANCHOR_UNAVAILABLE");
            }
            IntPtr resolved=NativeRealPath(buffer.ToString(),IntPtr.Zero);
            if(resolved==IntPtr.Zero)throw new InvalidDataException("MAC_PRODUCT_EXECUTABLE_ANCHOR_UNAVAILABLE");
            try{return Marshal.PtrToStringAnsi(resolved);}
            finally{NativeFree(resolved);}
        }

        internal static string ProductResources(bool mac,Func<string> executableAnchor)
        {
            if(!mac)return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"wps-formatter-plugin");
            string executable=executableAnchor();
            if(string.IsNullOrWhiteSpace(executable)||!Path.IsPathRooted(executable)||!File.Exists(executable))
                throw new InvalidDataException("MAC_PRODUCT_EXECUTABLE_ANCHOR_UNAVAILABLE");
            return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(executable)),"wps-formatter-plugin");
        }
        internal static string VerifiedProductResources(string requested,bool mac,Func<string> executableAnchor)
        {
            string resources=ProductResources(mac,executableAnchor);
            if(Path.GetFullPath(requested??"")!=Path.GetFullPath(resources))throw new InvalidDataException("MAC_PRODUCT_RESOURCES_INVALID");
            MacPluginResources.Verify(resources);
            return resources;
        }

        internal MacSessionCoordinator(MacTaskRequest task,string input,string output,string cancel,string featureId="format")
        {
            if(task==null||task.schema!=1||!System.Text.RegularExpressions.Regex.IsMatch(task.nonce??"", "^[A-Za-z0-9_-]{32,128}$"))throw new InvalidDataException("MAC_PRODUCT_BINDING_INVALID");
            if(!System.Text.RegularExpressions.Regex.IsMatch(task.task_id??"","^[a-f0-9]{32}$"))throw new InvalidDataException("MAC_PRODUCT_TASK_ID_INVALID");
            State["task_id"]=task.task_id;
            string state=Path.GetFullPath(task.state_directory??"");
            string control=Path.GetDirectoryName(Path.GetFullPath(cancel));
            if(state!=Path.Combine(control,"mac-state")||!Directory.Exists(state))throw new InvalidDataException("MAC_PRODUCT_STATE_INVALID");
            string resources=VerifiedProductResources(task.resources,IsMac,DarwinExecutablePath);
            if(File.Exists(output)||Path.GetDirectoryName(Path.GetFullPath(output))==control)throw new InvalidDataException("MAC_PRODUCT_WORKCOPY_CONFLICT");
            Environment.SetEnvironmentVariable("XDG_DATA_HOME",state);
            Environment.SetEnvironmentVariable("PARTYOPS_MAC_QUOTE_FONT","Times New Roman");
            Type paths=typeof(DocumentRepository.Services.Hosting.Standalone.StandaloneBatchProcessor).Assembly.GetType("DocumentRepository.Services.Configuration.ApplicationDataPaths",true);
            string root=Path.GetFullPath((string)paths.GetField("Root",BindingFlags.Public|BindingFlags.Static).GetValue(null));
            if(root!=Path.Combine(state,"DocumentRepository"))throw new InvalidOperationException("MAC_PRODUCT_ROOT_NOT_PRIVATE");
            mutex=new Mutex(false,"PartyOpsMacFormatterTask");
            lockHeld=mutex.WaitOne(0);
            if(!lockHeld){mutex.Dispose();throw new InvalidOperationException("MAC_PRODUCT_TASK_ALREADY_RUNNING");}
            try
            {
                carrier=Path.Combine(state,"partyops-carrier-"+task.nonce+".docx");
                File.Copy(Path.Combine(resources,"bootstrap-carrier.docx"),carrier,false);
                Bridge=new MacWpsObjectBridge(18974,resources,input,output,cancel,carrier,"task-lease-format",task.nonce,featureId,true);
                profile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal),"Library/Containers/com.kingsoft.wpsoffice.mac/Data/.kingsoft/wps/jsaddons/jsplugins.xml");
                if(!Directory.Exists(Path.GetDirectoryName(profile)))throw new DirectoryNotFoundException("MAC_PRODUCT_WPS_PROFILE_UNAVAILABLE");
                EnsureOwnedRegistration(profile,Bridge.Url);
                State["registration_owned"]=true;
                State["registration_installed"]=true;
            }
            catch { Bridge?.Dispose();if(lockHeld){mutex.ReleaseMutex();lockHeld=false;}mutex.Dispose();throw; }
        }

        internal void Start(CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            string app="/Applications/wpsoffice.app";
            if(!Directory.Exists(app))throw new DirectoryNotFoundException("MAC_PRODUCT_WPS_APP_UNAVAILABLE");
            // open 的退出码只证明启动请求已提交；必须继续等当次插件握手和实际集合核验。
            using(var process=Process.Start(new ProcessStartInfo("/usr/bin/open","-j -g -a "+Quote(app)+" "+Quote(carrier)){UseShellExecute=false,CreateNoWindow=true}))
            {if(!process.WaitForExit(15000)||process.ExitCode!=0)throw new InvalidOperationException("MAC_PRODUCT_WPS_LAUNCH_FAILED");}
            Bridge.WaitForPlugin(TimeSpan.FromSeconds(120));State["plugin_handshake"]=true;
            var json=new JavaScriptSerializer();
            var closed=json.Deserialize<Dictionary<string,object>>((string)Bridge.Call(1,"inspect","LeaseCloseBootstrap",new object[0],typeof(string),null));
            if(!closed.ContainsKey("actual_count")||Convert.ToInt32(closed["actual_count"])!=0)throw new InvalidOperationException("MAC_PRODUCT_CARRIER_CLOSE_UNCONFIRMED");
            State["carrier_closed"]=true;
            cancellation.ThrowIfCancellationRequested();
            Runtime=new MacTaskLeaseRuntime(typeof(DocumentRepository.Services.Hosting.Standalone.StandaloneBatchProcessor).Assembly,Bridge);
        }
        internal void Finish()
        {
            if(Runtime==null)return;
            try {Runtime.Finish();} finally {foreach(var pair in Runtime.State)State[pair.Key]=pair.Value;}
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            try
            {
                // 产品登记跨任务保留；只确认自有条目，不撤销他人的配置或改写信任URL。
                State["registration_owned"]=false;
                if(profile!=null&&Bridge!=null)State["registration_owned"]=VerifyOwnedRegistration(profile,Bridge.Url);
            }
            finally {if(Bridge!=null){var timeline=Bridge.QaTimeline();foreach(string field in new[]{"initialization_failure","failure_trace","failure_trace_total","failure_trace_truncated","first_identity_invalidation"})if(timeline.ContainsKey(field))State[field]=timeline[field];var timeout=Bridge.FirstTransportTimeout();if(timeout!=null)State["first_transport_timeout"]=timeout;State["request_stages"]=Bridge.Diagnostics();Bridge.Dispose();State["capability_revoked"]=true;}if(lockHeld){mutex.ReleaseMutex();lockHeld=false;}mutex.Dispose();}
        }
        private static string Quote(string value){return "\""+value.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";}
        internal static string Digest(byte[] value){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(value)).Replace("-","").ToLowerInvariant();}
        internal static byte[] AddRegistration(byte[] original,string url)
        {
            XDocument doc;
            if(original==null)doc=new XDocument(new XElement("jsplugins"));
            else using(var stream=new MemoryStream(original))using(var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null}))doc=XDocument.Load(reader,LoadOptions.PreserveWhitespace);
            if(doc.Root==null||doc.Root.Name!="jsplugins")throw new InvalidDataException("MAC_PRODUCT_PROFILE_SCHEMA_UNKNOWN");
            foreach(var node in doc.Root.Elements())if((string)node.Attribute("name")=="PartyOpsMacFormatterTask")throw new InvalidDataException("MAC_PRODUCT_REGISTRATION_CONFLICT");
            doc.Root.Add(new XElement("jspluginonline",new XAttribute("name","PartyOpsMacFormatterTask"),new XAttribute("version","1.0"),new XAttribute("type","wps"),new XAttribute("enable","true"),new XAttribute("url",url)));
            using(var stream=new MemoryStream()){using(var writer=XmlWriter.Create(stream,new XmlWriterSettings{Encoding=new UTF8Encoding(false),CloseOutput=false}))doc.Save(writer);return stream.ToArray();}
        }
        private static string OwnershipFile(string path){return Path.Combine(Path.GetDirectoryName(path),"partyops-formatter-v1.ownership.json");}
        private static XElement OwnedEntry(byte[] bytes,string url)
        {
            XDocument doc;using(var stream=new MemoryStream(bytes))using(var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null}))doc=XDocument.Load(reader,LoadOptions.PreserveWhitespace);
            if(doc.Root==null||doc.Root.Name!="jsplugins")throw new InvalidDataException("MAC_PRODUCT_PROFILE_SCHEMA_UNKNOWN");
            XElement found=null;foreach(var node in doc.Root.Elements())if((string)node.Attribute("name")=="PartyOpsMacFormatterTask"){if(found!=null)throw new InvalidDataException("MAC_PRODUCT_REGISTRATION_CONFLICT");found=node;}
            if(found==null||found.Name!="jspluginonline"||(string)found.Attribute("url")!=url||(string)found.Attribute("type")!="wps"||(string)found.Attribute("version")!="1.0"||(string)found.Attribute("enable")!="true"||found.Attributes().Count()!=5||found.HasElements)throw new InvalidDataException("MAC_PRODUCT_REGISTRATION_CONFLICT");
            return found;
        }
        internal static bool VerifyOwnedRegistration(string path,string url)
        {
            var claim=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(OwnershipFile(path)));
            if(claim.Count!=3||!claim.ContainsKey("schema")||Convert.ToInt32(claim["schema"])!=1||!claim.ContainsKey("entry_sha256")||!claim.ContainsKey("original_sha256"))throw new InvalidDataException("MAC_PRODUCT_REGISTRATION_CONFLICT");
            var entry=OwnedEntry(File.ReadAllBytes(path),url);
            if(Digest(Encoding.UTF8.GetBytes(entry.ToString(SaveOptions.DisableFormatting)))!=Convert.ToString(claim["entry_sha256"]))throw new InvalidDataException("MAC_PRODUCT_REGISTRATION_CHANGED");
            string original=Path.Combine(Path.GetDirectoryName(path),"partyops-formatter-v1.original.xml");
            if(Convert.ToString(claim["original_sha256"])=="absent"?File.Exists(original):!File.Exists(original)||Digest(File.ReadAllBytes(original))!=Convert.ToString(claim["original_sha256"]))throw new InvalidDataException("MAC_PRODUCT_REGISTRATION_BACKUP_CHANGED");
            return true;
        }
        internal static void EnsureOwnedRegistration(string path,string url)
        {
            if(File.Exists(OwnershipFile(path))){VerifyOwnedRegistration(path,url);return;}
            byte[] before=File.Exists(path)?File.ReadAllBytes(path):null;
            byte[] installed=AddRegistration(before,url); // 同名但无自有凭证绝不认领。
            string original=Path.Combine(Path.GetDirectoryName(path),"partyops-formatter-v1.original.xml");
            if(File.Exists(original))throw new InvalidDataException("MAC_PRODUCT_REGISTRATION_BACKUP_CONFLICT");
            if(before!=null)using(var stream=new FileStream(original,FileMode.CreateNew,FileAccess.Write))stream.Write(before,0,before.Length);
            var entry=OwnedEntry(installed,url);string claim=new JavaScriptSerializer().Serialize(new{schema=1,entry_sha256=Digest(Encoding.UTF8.GetBytes(entry.ToString(SaveOptions.DisableFormatting))),original_sha256=before==null?"absent":Digest(before)});
            using(var stream=new FileStream(OwnershipFile(path),FileMode.CreateNew,FileAccess.Write))using(var writer=new StreamWriter(stream,new UTF8Encoding(false)))writer.Write(claim);
            ReplaceExpected(path,before,installed);VerifyOwnedRegistration(path,url);
        }
        // 卸载/回退仅移除确认的自有节点；备份与凭证留作证据，不覆盖安装后的外部条目。
        internal static void RemoveOwnedRegistration(string path,string url)
        {
            VerifyOwnedRegistration(path,url);byte[] before=File.ReadAllBytes(path);var entry=OwnedEntry(before,url);var doc=entry.Document;entry.Remove();
            byte[] after;using(var stream=new MemoryStream()){using(var writer=XmlWriter.Create(stream,new XmlWriterSettings{Encoding=new UTF8Encoding(false),CloseOutput=false}))doc.Save(writer);after=stream.ToArray();}ReplaceExpected(path,before,after);
        }
        internal static void ReplaceExpected(string path,byte[] expected,byte[] replacement)
        {
            if(expected==null?File.Exists(path):(!File.Exists(path)||Digest(File.ReadAllBytes(path))!=Digest(expected)))throw new InvalidOperationException("MAC_PRODUCT_REGISTRATION_CONCURRENT_CHANGE");
            string temporary=path+".partyops-"+Guid.NewGuid().ToString("N");
            using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write))stream.Write(replacement,0,replacement.Length);
            if(expected==null)File.Move(temporary,path);else File.Replace(temporary,path,null);
        }
    }
    internal static class MacPluginResources
    {
        // 构建时固定实际资源摘要，拒绝将任意外部脚本作为正式加载项。
        internal static readonly Dictionary<string,string> Hashes=new Dictionary<string,string>{{"bootstrap-carrier.docx","a08269f19d71b83d56280a340f4e918400b87a079298b974dbea0d846c01f857"},{"main.js","1ac7976be28afc49501e12856f238ba412fcb563e9a9a2b10216cc8a992ef960"},{"product-ribbon.xml","299e7b684ac3d8eaae1fe07697276f321780b1a18e8ad6c319644e8c03ace1b8"},{"ribbon.xml","a1efcc586611b4303856cc17fb013f113b41288adf904b50670d508b15a30fe4"},{"task-lease.js","b8c1f86d4f724c4ea49d18fa7972c004a55238f41c60dd22a286a02d72287cdf"},{"product-index.html","d4585f54c1867f5b3e00d9ef605c076900af4b135f65614235eaa59bd99bb374"},{"product-main.js","544b88cfe0dbb759284c81e6bf07b2e9efd354b0633a436cc9643985b7713107"}};
        internal static void Verify(string directory)
        {if(Hashes.Count!=7)throw new InvalidDataException("MAC_PRODUCT_RESOURCE_CATALOG_INCOMPLETE");foreach(var pair in Hashes)if(MacSessionCoordinator.Digest(File.ReadAllBytes(Path.Combine(directory,pair.Key)))!=pair.Value)throw new InvalidDataException("MAC_PRODUCT_RESOURCE_HASH_MISMATCH:"+pair.Key);}
    }
}
