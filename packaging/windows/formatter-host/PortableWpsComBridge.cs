using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Threading;
using System.Web.Script.Serialization;
using DocumentRepository.Services.Hosting.Standalone;

namespace PartyOps.DocumentFormatter.Host;

/// <summary>
/// Linux WPS RPC 到原 Microsoft.Office.Interop.Word 接口的 ABI 适配层。
/// 该层只负责激活和 COM 参数封送，不包含、复制或改写任何排版规则。
/// </summary>
internal static class PortableWpsComBridge
{
    private const string MapFileName = "word-vtable-map.json";

    internal static bool IsPortablePlatform => Environment.OSVersion.Platform == PlatformID.Unix
        || Environment.OSVersion.Platform == PlatformID.MacOSX;

    internal static void RegisterIfRequired()
    {
        if (!IsPortablePlatform || PortableOfficeHostRuntime.IsRegistered)
        {
            return;
        }
        string mapPath = Environment.GetEnvironmentVariable("PARTYOPS_WPS_VTABLE_MAP");
        if (string.IsNullOrWhiteSpace(mapPath))
        {
            mapPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, MapFileName);
        }
        WordVtableMap map = WordVtableMap.Load(mapPath);
        PortableOfficeHostRuntime.Register(
            () => WpsRpcSession.Start(map).Application,
            WpsVtableProxy.Owns,
            value => WpsVtableProxy.From(value).Identity,
            value => WpsVtableProxy.From(value).QuitApplication(),
            value => WpsVtableProxy.From(value).ReleaseOnce());
    }
}

internal sealed class WordVtableMap
{
    private readonly IReadOnlyDictionary<string, int> slots;

    private WordVtableMap(Dictionary<string, int> slots)
    {
        this.slots = slots;
    }

    internal static WordVtableMap Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException("缺少锁定的 WPS Word 虚表槽位表。", path);
        }
        VtableMapPayload payload = new JavaScriptSerializer().Deserialize<VtableMapPayload>(
            File.ReadAllText(path));
        if (payload == null || payload.schema != 1 || payload.method_count < 400
            || payload.slots == null || payload.slots.Count != payload.method_count)
        {
            throw new InvalidDataException("WPS Word 虚表槽位表无效或不完整。");
        }
        return new WordVtableMap(payload.slots);
    }

    internal int Resolve(MethodInfo method)
    {
        string owner = method.DeclaringType?.FullName;
        string key = owner + "|" + method.Name;
        if (string.IsNullOrWhiteSpace(owner) || !slots.TryGetValue(key, out int slot))
        {
            throw new MissingMethodException("WPS Word 虚表槽位未锁定：" + key);
        }
        return slot;
    }

    private sealed class VtableMapPayload
    {
        public int schema { get; set; }
        public int method_count { get; set; }
        public Dictionary<string, int> slots { get; set; }
    }
}

internal sealed class WpsRpcSession
{
    private const int ApplicationQuitSlot = 120;
    private readonly WpsNativeAbi abi;
    private readonly WordVtableMap map;
    private readonly WpsVtableProxy applicationProxy;

    private WpsRpcSession(WpsNativeAbi abi, WordVtableMap map, IntPtr application)
    {
        this.abi = abi;
        this.map = map;
        Type applicationType = typeof(PortableOfficeHostRuntime).Assembly.GetType(
            "Microsoft.Office.Interop.Word.Application", true);
        applicationProxy = new WpsVtableProxy(this, applicationType, application, true);
    }

    internal object Application => applicationProxy.GetTransparentProxy();

    internal static WpsRpcSession Start(WordVtableMap map)
    {
        (string library, string executable) = WpsInstallation.Resolve();
        WpsNativeAbi abi = new WpsNativeAbi(library);
        IntPtr application = abi.CreateApplication(executable);
        WpsRpcSession session = new WpsRpcSession(abi, map, application);
        session.CloseFreshStartupDocument();
        return session;
    }

    private void CloseFreshStartupDocument()
    {
        // 部分 Linux WPS 在 RPC 新进程中自动创建空白文稿。仅处理本次新建
        // 的进程及未编辑、未落盘的空白文稿；既有会话仍交原隔离检查拒绝。
        if (!abi.OwnsFreshLinuxProcess)
        {
            return;
        }
        object documents = null;
        object document = null;
        object content = null;
        try
        {
            documents = ReadWordProperty(Application, "_Application", "Documents");
            if ((int)ReadWordProperty(documents, "Documents", "Count") != 1)
            {
                return;
            }
            document = WordType("Documents").GetMethod("get_Item").Invoke(documents, new object[] { 1 });
            if (!(bool)ReadWordProperty(document, "_Document", "Saved")
                || !string.IsNullOrEmpty((string)ReadWordProperty(document, "_Document", "Path")))
            {
                return;
            }
            content = ReadWordProperty(document, "_Document", "Content");
            if ((string)ReadWordProperty(content, "Range", "Text") != "\r")
            {
                return;
            }
            WordType("_Document").GetMethod("Close").Invoke(document,
                new object[] { 0 /* wdDoNotSaveChanges */, Type.Missing, Type.Missing });
        }
        finally
        {
            foreach (object value in new object[] { content, document, documents })
            {
                if (value != null && WpsVtableProxy.Owns(value))
                {
                    WpsVtableProxy.From(value).ReleaseOnce();
                }
            }
        }
    }

    private static Type WordType(string name) => typeof(PortableOfficeHostRuntime).Assembly
        .GetType("Microsoft.Office.Interop.Word." + name, true);

    private static object ReadWordProperty(object value, string owner, string name) =>
        WordType(owner).GetProperty(name).GetValue(value, null);

    internal object Invoke(IntPtr instance, MethodInfo method, object[] arguments)
    {
        return abi.InvokeVtable(this, instance, map.Resolve(method), method, arguments);
    }

    internal object WrapInterface(Type interfaceType, IntPtr pointer)
    {
        if (pointer == IntPtr.Zero)
        {
            return null;
        }
        return new WpsVtableProxy(this, interfaceType, pointer, false).GetTransparentProxy();
    }

    internal object WrapUnknown(IntPtr pointer, WpsDynamicKind kind = WpsDynamicKind.Generic)
    {
        return pointer == IntPtr.Zero ? null : new WpsDynamicDispatch(this, pointer, kind);
    }

    internal void AddRef(IntPtr pointer) => abi.AddRef(pointer);

    internal void Release(IntPtr pointer) => abi.Release(pointer);

    internal void ClearVariant(IntPtr pointer) => abi.ClearVariantValue(pointer);

    internal long Identity(IntPtr pointer) => abi.Identity(pointer);

    internal object DecodeVariant(IntPtr variant, Type interfaceHint = null)
    {
        return abi.DecodeVariant(this, variant, interfaceHint);
    }

    internal object InvokeDispatch(IntPtr instance, string name, ushort flags, object[] arguments)
    {
        return abi.InvokeDispatch(this, instance, name, flags, arguments);
    }

    internal object InvokeOfficeDynamic(
        IntPtr instance,
        WpsDynamicKind kind,
        string name,
        ushort flags,
        object[] arguments)
    {
        return abi.InvokeOfficeDynamic(this, instance, kind, name, flags, arguments);
    }

    internal void QuitApplication(WpsVtableProxy proxy)
    {
        if (!ReferenceEquals(proxy, applicationProxy))
        {
            return;
        }
        abi.QuitApplication(proxy.Pointer, ApplicationQuitSlot);
    }
}

internal static class WpsInstallation
{
    internal static (string library, string executable) Resolve()
    {
        string explicitLibrary = Environment.GetEnvironmentVariable("PARTYOPS_WPS_RPC_LIBRARY");
        string explicitExecutable = Environment.GetEnvironmentVariable("PARTYOPS_WPS_EXECUTABLE");
        if (!string.IsNullOrWhiteSpace(explicitLibrary) || !string.IsNullOrWhiteSpace(explicitExecutable))
        {
            if (string.IsNullOrWhiteSpace(explicitLibrary) || !File.Exists(explicitLibrary))
            {
                throw new FileNotFoundException("PARTYOPS_WPS_RPC_LIBRARY 指向的 WPS RPC 库不存在。", explicitLibrary);
            }
            if (string.IsNullOrWhiteSpace(explicitExecutable) || !File.Exists(explicitExecutable))
            {
                throw new FileNotFoundException("PARTYOPS_WPS_EXECUTABLE 指向的 WPS 主程序不存在。", explicitExecutable);
            }
            return (Path.GetFullPath(explicitLibrary), Path.GetFullPath(explicitExecutable));
        }

        string[] roots =
        {
            "/opt/kingsoft/wps-office/office6",
            "/opt/kingsoft/wps-office/office6/",
            "/usr/lib/office6",
            "/usr/local/lib/office6"
        };
        string[] libraries = { "librpcwpsapi_wpsqt.so", "librpcwpsapi_sysqt5.so" };
        foreach (string root in roots.Distinct(StringComparer.Ordinal))
        {
            string executable = Path.Combine(root, "wps");
            if (!File.Exists(executable))
            {
                continue;
            }
            foreach (string name in libraries)
            {
                string library = Path.Combine(root, name);
                if (File.Exists(library))
                {
                    return (library, executable);
                }
            }
        }
        throw new FileNotFoundException(
            "未发现支持原生 RPC 的 WPS。请安装 WPS Linux，或由安装包配置 PARTYOPS_WPS_RPC_LIBRARY 与 PARTYOPS_WPS_EXECUTABLE。");
    }
}

internal sealed class WpsVtableProxy : RealProxy
{
    private readonly WpsRpcSession session;
    private readonly Type interfaceType;
    private readonly bool rootApplication;
    private bool nextFontUsesEastAsianName;
    private bool preferEastAsianName;
    private IntPtr pointer;

    internal WpsVtableProxy(
        WpsRpcSession session,
        Type interfaceType,
        IntPtr pointer,
        bool rootApplication)
        : base(interfaceType)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.interfaceType = interfaceType ?? throw new ArgumentNullException(nameof(interfaceType));
        this.pointer = pointer != IntPtr.Zero
            ? pointer
            : throw new ArgumentException("WPS 接口指针不能为空。", nameof(pointer));
        this.rootApplication = rootApplication;
    }

    internal IntPtr Pointer => pointer;

    internal long Identity => session.Identity(pointer);

    internal static bool Owns(object value)
    {
        if (value == null || !RemotingServices.IsTransparentProxy(value))
        {
            return false;
        }
        try
        {
            return RemotingServices.GetRealProxy(value) is WpsVtableProxy;
        }
        catch
        {
            return false;
        }
    }

    internal static WpsVtableProxy From(object value)
    {
        if (!Owns(value))
        {
            throw new InvalidOperationException("对象不属于 PartyOps WPS RPC 宿主。");
        }
        return (WpsVtableProxy)RemotingServices.GetRealProxy(value);
    }

    public override IMessage Invoke(IMessage message)
    {
        IMethodCallMessage call = (IMethodCallMessage)message;
        try
        {
            MethodInfo method = (MethodInfo)call.MethodBase;
            if (method.DeclaringType == typeof(object))
            {
                object objectResult = InvokeObjectMethod(method, call.Args);
                return new ReturnMessage(objectResult, call.Args, call.ArgCount, call.LogicalCallContext, call);
            }
            IntPtr current = pointer;
            if (current == IntPtr.Zero)
            {
                throw new ObjectDisposedException(interfaceType.FullName);
            }
            WpsBridgeTrace.Write("CALL " + method.DeclaringType?.Name + "." + method.Name);
            object result = session.Invoke(current, method, call.Args);
            TrackFormattedTextFont(method, result);
            result = NormalizePortableFontResult(current, method, result);
            WpsBridgeTrace.Write("OK   " + method.DeclaringType?.Name + "." + method.Name
                + " => " + WpsBridgeTrace.DescribeResult(method, result));
            return new ReturnMessage(result, call.Args, call.ArgCount, call.LogicalCallContext, call);
        }
        catch (Exception error)
        {
            WpsBridgeTrace.Write("FAIL " + call.MethodBase?.DeclaringType?.Name + "."
                + call.MethodName + " => " + error);
            return new ReturnMessage(error, call);
        }
    }

    private object NormalizePortableFontResult(IntPtr current, MethodInfo method, object result)
    {
        if (!string.Equals(method.DeclaringType?.Name, "_Font", StringComparison.Ordinal)
            || !method.Name.StartsWith("get_Name", StringComparison.Ordinal)
            || !(result is string fontName))
        {
            return result;
        }
        if (method.Name == "get_Name" && preferEastAsianName)
        {
            // WPS Linux 对刚继承格式的中文标点仍从 Font.Name 回读西文字体
            // 槽，而 NameFarEast 已正确继承相邻汉字字体。Windows WPS/Word
            // 会从通用 Name 返回该字体；在 ABI 边界补齐这一平台差异，保持
            // 原源码的格式复核语义，不写文档、也不改变任何排版规则。
            MethodInfo farEastGetter = method.DeclaringType?.GetProperty("NameFarEast")?.GetGetMethod();
            if (farEastGetter != null)
            {
                object farEast = session.Invoke(current, farEastGetter, Array.Empty<object>());
                if (farEast is string farEastName && !string.IsNullOrWhiteSpace(farEastName))
                {
                    fontName = farEastName;
                }
            }
            preferEastAsianName = false;
        }
        return PortableFontAliases.ToSourceName(fontName);
    }

    private void TrackFormattedTextFont(MethodInfo method, object result)
    {
        if (string.Equals(method.DeclaringType?.Name, "Range", StringComparison.Ordinal)
            && string.Equals(method.Name, "set_FormattedText", StringComparison.Ordinal))
        {
            // 原源码只有中文标点修正会写 Range.FormattedText；WPS Linux 在
            // 随后的 Font.Name 复核中仍回读西文字体槽。记录这一轮调用，
            // 仅让紧随其后的 Font.Name 使用 NameFarEast 语义。
            nextFontUsesEastAsianName = true;
            return;
        }
        if (nextFontUsesEastAsianName
            && string.Equals(method.DeclaringType?.Name, "Range", StringComparison.Ordinal)
            && string.Equals(method.Name, "get_Font", StringComparison.Ordinal)
            && WpsVtableProxy.Owns(result))
        {
            WpsVtableProxy.From(result).preferEastAsianName = true;
            nextFontUsesEastAsianName = false;
        }
    }

    private object InvokeObjectMethod(MethodInfo method, object[] arguments)
    {
        switch (method.Name)
        {
            case "ToString":
                return interfaceType.FullName + " (WPS RPC)";
            case "GetHashCode":
                return pointer.GetHashCode();
            case "Equals":
                return arguments != null && arguments.Length == 1
                    && ReferenceEquals(GetTransparentProxy(), arguments[0]);
            default:
                throw new MissingMethodException(method.Name);
        }
    }

    internal void QuitApplication()
    {
        if (rootApplication && pointer != IntPtr.Zero)
        {
            session.QuitApplication(this);
        }
    }

    internal void ReleaseOnce()
    {
        IntPtr owned = Interlocked.Exchange(ref pointer, IntPtr.Zero);
        if (owned != IntPtr.Zero)
        {
            session.Release(owned);
        }
    }

    // WPS Linux RPC 的短生命周期宿主按任务退出。部分远端对象会复用同一
    // 本地代理地址；若由 Mono 终结器线程异步 Release，可能与主线程的
    // 后续调用竞态并导致原生崩溃。源码显式释放的 Application、Documents
    // 和 Document 仍走 ReleaseOnce，其余临时代理交由 Quit + 进程退出回收。
}

[StructLayout(LayoutKind.Explicit, Size = 16)]
internal struct WpsVariant
{
    [FieldOffset(0)] internal ushort Type;
    [FieldOffset(8)] internal short Int16;
    [FieldOffset(8)] internal int Int32;
    [FieldOffset(8)] internal long Int64;
    [FieldOffset(8)] internal float Single;
    [FieldOffset(8)] internal double Double;
    [FieldOffset(8)] internal IntPtr Pointer;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WpsDispatchParameters
{
    internal IntPtr Arguments;
    internal IntPtr NamedArguments;
    internal uint ArgumentCount;
    internal uint NamedArgumentCount;
}

internal sealed class WpsNativeAbi
{
    private const ushort VtEmpty = 0;
    private const ushort VtNull = 1;
    private const ushort VtI2 = 2;
    private const ushort VtI4 = 3;
    private const ushort VtR4 = 4;
    private const ushort VtR8 = 5;
    private const ushort VtDate = 7;
    private const ushort VtBstr = 8;
    private const ushort VtDispatch = 9;
    private const ushort VtError = 10;
    private const ushort VtBool = 11;
    private const ushort VtUnknown = 13;
    private const ushort VtI1 = 16;
    private const ushort VtUi1 = 17;
    private const ushort VtUi2 = 18;
    private const ushort VtUi4 = 19;
    private const ushort VtI8 = 20;
    private const ushort VtUi8 = 21;
    private const ushort VtInt = 22;
    private const ushort VtUInt = 23;
    private const ushort VtArray = 0x2000;
    private const int DispEParamNotFound = unchecked((int)0x80020004);
    private const int RpcEInvalidCallData = unchecked((int)0x80010010);
    private const ushort DispatchMethod = 0x1;
    private const ushort DispatchPropertyGet = 0x2;
    private const ushort DispatchPropertyPut = 0x4;
    private const int DispIdPropertyPut = -3;
    private const int VariantSize = 16;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CreateWpsRpcInstance(out IntPtr client);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetWpsApplication(IntPtr self, out IntPtr application);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SetProcessPath(IntPtr self, IntPtr path);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SetProcessArguments(IntPtr self, int count, IntPtr arguments);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SetStartTimeout(IntPtr self, int microseconds);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetProcessPid(IntPtr self, out long pid);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint ReferenceObject(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int QueryObject(IntPtr self, ref Guid iid, out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint StringLength(IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr AllocateString(IntPtr value, uint length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FreeString(IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ClearVariant(IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SafeArrayAccessData(IntPtr array, out IntPtr data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SafeArrayUnaccessData(IntPtr array);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SafeArrayGetBound(IntPtr array, uint dimension, out int bound);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetIdsOfNames(
        IntPtr self, ref Guid iid, IntPtr names, uint count, uint locale, IntPtr identifiers);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeInvokeDispatch(
        IntPtr self, int member, ref Guid iid, uint locale, ushort flags,
        ref WpsDispatchParameters parameters, IntPtr result, IntPtr exception, IntPtr argumentError);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeQuitApplication(
        IntPtr self, IntPtr saveChanges, IntPtr originalFormat, IntPtr routeDocument);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetIntegerProperty(IntPtr self, out int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetBooleanProperty(IntPtr self, out short value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetBstrProperty(IntPtr self, out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetBstrPropertyWithLocale(IntPtr self, int locale, out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetIntegerPropertyWithLocale(IntPtr self, int locale, out int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetVariantPropertyWithLocale(IntPtr self, int locale, IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetDocumentProperty(
        IntPtr self, WpsVariant index, int locale, out IntPtr property);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int AddDocumentProperty(
        IntPtr self,
        IntPtr name,
        short linkToContent,
        WpsVariant type,
        WpsVariant value,
        WpsVariant linkSource,
        int locale,
        out IntPtr property);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DeleteDocumentProperty(IntPtr self);

    private readonly IntPtr library;
    private readonly AllocateString allocateString;
    private readonly StringLength stringLength;
    private readonly FreeString freeString;
    private readonly ClearVariant clearVariant;
    private readonly SafeArrayAccessData safeArrayAccessData;
    private readonly SafeArrayUnaccessData safeArrayUnaccessData;
    private readonly SafeArrayGetBound safeArrayGetLowerBound;
    private readonly SafeArrayGetBound safeArrayGetUpperBound;
    private readonly NativeDelegateFactory delegateFactory = new NativeDelegateFactory();

    internal bool OwnsFreshLinuxProcess { get; private set; }

    internal WpsNativeAbi(string libraryPath)
    {
        library = NativeLibraryLoader.Open(libraryPath);
        allocateString = Symbol<AllocateString>("_XSysAllocStringLen");
        stringLength = Symbol<StringLength>("_XSysStringLen");
        freeString = Symbol<FreeString>("_XSysFreeString");
        clearVariant = Symbol<ClearVariant>("_MVariantClear");
        safeArrayAccessData = Symbol<SafeArrayAccessData>("_MSafeArrayAccessData");
        safeArrayUnaccessData = Symbol<SafeArrayUnaccessData>("_MSafeArrayUnaccessData");
        safeArrayGetLowerBound = Symbol<SafeArrayGetBound>("_MSafeArrayGetLBound");
        safeArrayGetUpperBound = Symbol<SafeArrayGetBound>("_MSafeArrayGetUBound");
    }

    internal IntPtr CreateApplication(string executable)
    {
        IntPtr client;
        ThrowIfFailed(Symbol<CreateWpsRpcInstance>("createWpsRpcInstance")(out client),
            "createWpsRpcInstance");
        if (client == IntPtr.Zero)
        {
            throw new InvalidOperationException("WPS RPC 客户端为空。");
        }
        IntPtr nativePath = ToBstr(executable);
        try
        {
            ThrowIfFailed(Vtable<SetProcessPath>(client, 5)(client, nativePath), "setProcessPath");
        }
        finally
        {
            freeString(nativePath);
        }
        string[] arguments = { "-shield", "-multiply", "-hidentp" };
        IntPtr array = Marshal.AllocHGlobal(arguments.Length * IntPtr.Size);
        IntPtr[] strings = new IntPtr[arguments.Length];
        try
        {
            for (int index = 0; index < arguments.Length; index++)
            {
                strings[index] = ToBstr(arguments[index]);
                Marshal.WriteIntPtr(array, index * IntPtr.Size, strings[index]);
            }
            ThrowIfFailed(
                Vtable<SetProcessArguments>(client, 6)(client, arguments.Length, array),
                "setProcessArgs");
        }
        finally
        {
            foreach (IntPtr value in strings)
            {
                if (value != IntPtr.Zero)
                {
                    freeString(value);
                }
            }
            Marshal.FreeHGlobal(array);
        }
        ThrowIfFailed(Vtable<SetStartTimeout>(client, 8)(client, 30000000), "setStartTimeout");
        // WPS 会自行 daemonize（PPid=1），不能使用父 PID 判断归属。
        // SDK 返回的实际进程须不在启动前快照中，并具有独立 RPC 启动标记。
        HashSet<string> priorProcesses = Directory.Exists("/proc/self")
            ? new HashSet<string>(Directory.GetDirectories("/proc").Select(Path.GetFileName))
            : null;
        IntPtr application;
        ThrowIfFailed(Vtable<GetWpsApplication>(client, 2)(client, out application),
            "getWpsApplication");
        if (application == IntPtr.Zero)
        {
            throw new InvalidOperationException("WPS RPC 未返回 Application。");
        }
        long processId;
        if (priorProcesses != null
            && Vtable<GetProcessPid>(client, 7)(client, out processId) == 0
            && processId > 0 && !priorProcesses.Contains(processId.ToString()))
        {
            try
            {
                string[] command = File.ReadAllText("/proc/" + processId + "/cmdline").Split('\0');
                OwnsFreshLinuxProcess = command.Length > 1 && command[0] == executable
                    && command.Contains("-automation") && command.Contains("-multiply")
                    && command.Any(value => value.StartsWith("-rpcserverport=/wpsrpc-", StringComparison.Ordinal));
            }
            catch (IOException)
            {
                OwnsFreshLinuxProcess = false;
            }
            catch (UnauthorizedAccessException)
            {
                OwnsFreshLinuxProcess = false;
            }
        }
        return application;
    }

    internal object InvokeVtable(
        WpsRpcSession session,
        IntPtr instance,
        int slot,
        MethodInfo method,
        object[] arguments)
    {
        ParameterInfo[] parameters = method.GetParameters();
        object[] managedArguments = arguments ?? Array.Empty<object>();
        if (managedArguments.Length != parameters.Length)
        {
            throw new TargetParameterCountException(method.Name);
        }
        List<Action> cleanup = new List<Action>();
        List<Action> copyBack = new List<Action>();
        List<Type> nativeTypes = new List<Type> { typeof(IntPtr) };
        List<object> nativeArguments = new List<object> { instance };
        try
        {
            for (int index = 0; index < parameters.Length; index++)
            {
                MarshalParameter(session, parameters[index], managedArguments, index,
                    nativeTypes, nativeArguments, cleanup, copyBack);
            }
            ReturnBuffer returnBuffer = PrepareReturn(method);
            if (returnBuffer.Pointer != IntPtr.Zero)
            {
                nativeTypes.Add(typeof(IntPtr));
                nativeArguments.Add(returnBuffer.Pointer);
                cleanup.Add(returnBuffer.Dispose);
            }
            IntPtr function = VtablePointer(instance, slot);
            Delegate invoker = delegateFactory.Create(function, nativeTypes.ToArray());
            int result;
            try
            {
                result = (int)invoker.DynamicInvoke(nativeArguments.ToArray());
            }
            catch (TargetInvocationException error) when (error.InnerException != null)
            {
                throw error.InnerException;
            }
            if (result == RpcEInvalidCallData && IsNullInterfaceProperty(method, returnBuffer))
            {
                // WPS Linux RPC 把“接口属性没有下一个对象”编码成
                // RPC_E_INVALID_CALLDATA，而 Word/WPS COM 语义应返回 null。
                // 仅接受无参数接口属性且输出指针仍为零，避免掩盖真实调用错误。
                WpsBridgeTrace.Write("NULL " + method.DeclaringType?.Name + "." + method.Name);
                result = 0;
            }
            ThrowIfFailed(result, method.DeclaringType?.Name + "." + method.Name);
            foreach (Action action in copyBack)
            {
                action();
            }
            return DecodeReturn(session, method, returnBuffer);
        }
        finally
        {
            for (int index = cleanup.Count - 1; index >= 0; index--)
            {
                cleanup[index]();
            }
        }
    }

    private static bool IsNullInterfaceProperty(MethodInfo method, ReturnBuffer buffer)
    {
        return method.ReturnType.IsInterface
            && method.GetParameters().Length == 0
            && method.Name.StartsWith("get_", StringComparison.Ordinal)
            && buffer.Pointer != IntPtr.Zero
            && Marshal.ReadIntPtr(buffer.Pointer) == IntPtr.Zero;
    }

    private void MarshalParameter(
        WpsRpcSession session,
        ParameterInfo parameter,
        object[] managedArguments,
        int index,
        ICollection<Type> nativeTypes,
        ICollection<object> nativeArguments,
        ICollection<Action> cleanup,
        ICollection<Action> copyBack)
    {
        Type parameterType = parameter.ParameterType;
        object value = managedArguments[index];
        if (parameterType.IsByRef)
        {
            Type elementType = parameterType.GetElementType();
            IntPtr storage;
            if (elementType == typeof(object))
            {
                storage = AllocateVariant(session, value);
                cleanup.Add(() =>
                {
                    clearVariant(storage);
                    Marshal.FreeHGlobal(storage);
                });
                if (parameter.IsOut)
                {
                    copyBack.Add(() => managedArguments[index] = DecodeVariant(session, storage));
                }
            }
            else if (elementType == typeof(bool))
            {
                storage = Marshal.AllocHGlobal(sizeof(short));
                Marshal.WriteInt16(storage, Convert.ToBoolean(value) ? (short)-1 : (short)0);
                cleanup.Add(() => Marshal.FreeHGlobal(storage));
                if (parameter.IsOut)
                {
                    copyBack.Add(() => managedArguments[index] = Marshal.ReadInt16(storage) != 0);
                }
            }
            else
            {
                throw new NotSupportedException("WPS RPC 不支持的引用参数：" + parameterType.FullName);
            }
            nativeTypes.Add(typeof(IntPtr));
            nativeArguments.Add(storage);
            return;
        }

        if (parameterType == typeof(string))
        {
            IntPtr text = ToBstr((string)value ?? string.Empty);
            cleanup.Add(() => freeString(text));
            nativeTypes.Add(typeof(IntPtr));
            nativeArguments.Add(text);
        }
        else if (parameterType == typeof(bool))
        {
            nativeTypes.Add(typeof(short));
            nativeArguments.Add(Convert.ToBoolean(value) ? (short)-1 : (short)0);
        }
        else if (parameterType.IsEnum || parameterType == typeof(int))
        {
            nativeTypes.Add(typeof(int));
            nativeArguments.Add(Convert.ToInt32(value));
        }
        else if (parameterType == typeof(float))
        {
            nativeTypes.Add(typeof(float));
            nativeArguments.Add(Convert.ToSingle(value));
        }
        else if (parameterType.IsInterface || parameterType == typeof(object))
        {
            nativeTypes.Add(typeof(IntPtr));
            nativeArguments.Add(PointerFromObject(value));
        }
        else
        {
            throw new NotSupportedException("WPS RPC 不支持的参数：" + parameterType.FullName);
        }
    }

    private ReturnBuffer PrepareReturn(MethodInfo method)
    {
        if (method.ReturnType == typeof(void))
        {
            return ReturnBuffer.Empty;
        }
        MarshalAsAttribute marshal = method.ReturnParameter.GetCustomAttribute<MarshalAsAttribute>();
        bool variant = method.ReturnType == typeof(object)
            && (marshal == null || marshal.Value == UnmanagedType.Struct);
        int size = variant ? VariantSize : ReturnStorageSize(method.ReturnType);
        IntPtr pointer = Marshal.AllocHGlobal(size);
        for (int offset = 0; offset < size; offset++)
        {
            Marshal.WriteByte(pointer, offset, 0);
        }
        return new ReturnBuffer(pointer, variant, () =>
        {
            if (variant)
            {
                clearVariant(pointer);
            }
            Marshal.FreeHGlobal(pointer);
        });
    }

    private static int ReturnStorageSize(Type type)
    {
        if (type == typeof(bool)) return sizeof(short);
        if (type == typeof(float)) return sizeof(float);
        if (type == typeof(int) || type.IsEnum) return sizeof(int);
        return IntPtr.Size;
    }

    private object DecodeReturn(WpsRpcSession session, MethodInfo method, ReturnBuffer buffer)
    {
        Type type = method.ReturnType;
        if (type == typeof(void)) return null;
        if (buffer.Variant)
        {
            return DecodeVariant(session, buffer.Pointer, InferVariantInterface(method));
        }
        if (type == typeof(bool)) return Marshal.ReadInt16(buffer.Pointer) != 0;
        if (type == typeof(float)) return Marshal.PtrToStructure<float>(buffer.Pointer);
        if (type == typeof(int)) return Marshal.ReadInt32(buffer.Pointer);
        if (type.IsEnum) return Enum.ToObject(type, Marshal.ReadInt32(buffer.Pointer));
        IntPtr value = Marshal.ReadIntPtr(buffer.Pointer);
        if (type == typeof(string))
        {
            if (value == IntPtr.Zero) return string.Empty;
            try
            {
                return Marshal.PtrToStringUni(value, checked((int)stringLength(value)));
            }
            finally
            {
                freeString(value);
                Marshal.WriteIntPtr(buffer.Pointer, IntPtr.Zero);
            }
        }
        if (typeof(IEnumerator).IsAssignableFrom(type))
        {
            return value == IntPtr.Zero
                ? null
                : new WpsEnumVariant(session, value, InferEnumeratorElement(method.DeclaringType));
        }
        if (type.IsInterface)
        {
            return session.WrapInterface(type, value);
        }
        if (type == typeof(object))
        {
            return session.WrapUnknown(value, InferUnknownKind(method));
        }
        throw new NotSupportedException("WPS RPC 不支持的返回类型：" + type.FullName);
    }

    private static Type InferVariantInterface(MethodInfo method)
    {
        if (method.Name == "get_Style")
        {
            return method.DeclaringType?.Assembly.GetType("Microsoft.Office.Interop.Word.Style", false);
        }
        return null;
    }

    private static WpsDynamicKind InferUnknownKind(MethodInfo method)
    {
        return method.Name == "get_CustomDocumentProperties"
            ? WpsDynamicKind.DocumentProperties
            : WpsDynamicKind.Generic;
    }

    private static Type InferEnumeratorElement(Type collectionType)
    {
        MethodInfo item = collectionType?.GetMethod("get_Item", BindingFlags.Public | BindingFlags.Instance);
        return item?.ReturnType.IsInterface == true ? item.ReturnType : null;
    }

    internal object DecodeVariant(WpsRpcSession session, IntPtr storage, Type interfaceHint = null)
    {
        WpsVariant value = Marshal.PtrToStructure<WpsVariant>(storage);
        ushort type = (ushort)(value.Type & ~0x4000);
        if (type == VtEmpty || type == VtNull) return null;
        if (type == VtI1) return unchecked((sbyte)value.Int16);
        if (type == VtUi1) return unchecked((byte)value.Int16);
        if (type == VtI2) return value.Int16;
        if (type == VtUi2) return unchecked((ushort)value.Int16);
        if (type == VtI4 || type == VtInt || type == VtError) return value.Int32;
        if (type == VtUi4 || type == VtUInt) return unchecked((uint)value.Int32);
        if (type == VtI8) return value.Int64;
        if (type == VtUi8) return unchecked((ulong)value.Int64);
        if (type == VtR4) return value.Single;
        if (type == VtR8) return value.Double;
        if (type == VtDate) return DateTime.FromOADate(value.Double);
        if (type == VtBool) return value.Int16 != 0;
        if (type == VtBstr)
        {
            return value.Pointer == IntPtr.Zero
                ? string.Empty
                : Marshal.PtrToStringUni(value.Pointer, checked((int)stringLength(value.Pointer)));
        }
        if (type == (VtArray | VtUi1))
        {
            return ReadByteArray(value.Pointer);
        }
        if (type == VtDispatch || type == VtUnknown)
        {
            IntPtr pointer = value.Pointer;
            Marshal.StructureToPtr(new WpsVariant { Type = VtEmpty }, storage, false);
            return interfaceHint != null
                ? session.WrapInterface(interfaceHint, pointer)
                : session.WrapUnknown(pointer);
        }
        throw new InvalidCastException("WPS RPC 返回了未支持的 VARIANT 类型：" + value.Type);
    }

    private byte[] ReadByteArray(IntPtr array)
    {
        if (array == IntPtr.Zero) return Array.Empty<byte>();
        ThrowIfFailed(safeArrayGetLowerBound(array, 1, out int lower), "SafeArrayGetLBound");
        ThrowIfFailed(safeArrayGetUpperBound(array, 1, out int upper), "SafeArrayGetUBound");
        int length = checked(upper - lower + 1);
        if (length <= 0) return Array.Empty<byte>();
        ThrowIfFailed(safeArrayAccessData(array, out IntPtr data), "SafeArrayAccessData");
        try
        {
            byte[] result = new byte[length];
            Marshal.Copy(data, result, 0, length);
            return result;
        }
        finally
        {
            ThrowIfFailed(safeArrayUnaccessData(array), "SafeArrayUnaccessData");
        }
    }

    private IntPtr AllocateVariant(WpsRpcSession session, object value)
    {
        IntPtr storage = Marshal.AllocHGlobal(VariantSize);
        Marshal.StructureToPtr(ToVariant(session, value), storage, false);
        return storage;
    }

    private WpsVariant ToVariant(WpsRpcSession session, object value)
    {
        if (value == null) return new WpsVariant { Type = VtEmpty };
        if (ReferenceEquals(value, Type.Missing) || value is Missing)
        {
            return new WpsVariant { Type = VtError, Int32 = DispEParamNotFound };
        }
        Type type = value.GetType();
        if (type.IsEnum) return new WpsVariant { Type = VtI4, Int32 = Convert.ToInt32(value) };
        if (value is bool boolean) return new WpsVariant { Type = VtBool, Int16 = boolean ? (short)-1 : (short)0 };
        if (value is byte unsignedByte) return new WpsVariant { Type = VtUi1, Int16 = unsignedByte };
        if (value is short shortValue) return new WpsVariant { Type = VtI2, Int16 = shortValue };
        if (value is int integer) return new WpsVariant { Type = VtI4, Int32 = integer };
        if (value is long longValue) return new WpsVariant { Type = VtI8, Int64 = longValue };
        if (value is float single) return new WpsVariant { Type = VtR4, Single = single };
        if (value is double number) return new WpsVariant { Type = VtR8, Double = number };
        if (value is DateTime date) return new WpsVariant { Type = VtDate, Double = date.ToOADate() };
        if (value is string text) return new WpsVariant { Type = VtBstr, Pointer = ToBstr(text) };
        IntPtr pointer = PointerFromObject(value);
        if (pointer != IntPtr.Zero)
        {
            AddRef(pointer);
            return new WpsVariant { Type = VtDispatch, Pointer = pointer };
        }
        throw new NotSupportedException("WPS RPC 不支持的 VARIANT 参数：" + type.FullName);
    }

    internal object InvokeDispatch(
        WpsRpcSession session,
        IntPtr instance,
        string name,
        ushort flags,
        object[] arguments)
    {
        int member = ResolveDispatchId(instance, name);
        object[] values = arguments ?? Array.Empty<object>();
        IntPtr nativeArguments = values.Length == 0
            ? IntPtr.Zero
            : Marshal.AllocHGlobal(values.Length * VariantSize);
        IntPtr named = (flags & DispatchPropertyPut) == 0
            ? IntPtr.Zero
            : Marshal.AllocHGlobal(sizeof(int));
        IntPtr result = Marshal.AllocHGlobal(VariantSize);
        List<IntPtr> initialized = new List<IntPtr>();
        try
        {
            Marshal.StructureToPtr(new WpsVariant { Type = VtEmpty }, result, false);
            for (int index = 0; index < values.Length; index++)
            {
                IntPtr target = IntPtr.Add(nativeArguments, index * VariantSize);
                Marshal.StructureToPtr(ToVariant(session, values[values.Length - 1 - index]), target, false);
                initialized.Add(target);
            }
            if (named != IntPtr.Zero) Marshal.WriteInt32(named, DispIdPropertyPut);
            WpsDispatchParameters parameters = new WpsDispatchParameters
            {
                Arguments = nativeArguments,
                NamedArguments = named,
                ArgumentCount = (uint)values.Length,
                NamedArgumentCount = named == IntPtr.Zero ? 0u : 1u
            };
            Guid iid = Guid.Empty;
            ThrowIfFailed(
                Vtable<NativeInvokeDispatch>(instance, 6)(instance, member, ref iid, 0, flags,
                    ref parameters, result, IntPtr.Zero, IntPtr.Zero),
                "IDispatch.Invoke(" + name + ")");
            return DecodeVariant(session, result);
        }
        finally
        {
            clearVariant(result);
            Marshal.FreeHGlobal(result);
            foreach (IntPtr value in initialized)
            {
                clearVariant(value);
            }
            if (nativeArguments != IntPtr.Zero) Marshal.FreeHGlobal(nativeArguments);
            if (named != IntPtr.Zero) Marshal.FreeHGlobal(named);
        }
    }

    internal object InvokeOfficeDynamic(
        WpsRpcSession session,
        IntPtr instance,
        WpsDynamicKind kind,
        string name,
        ushort flags,
        object[] arguments)
    {
        if (kind == WpsDynamicKind.DocumentProperties)
        {
            if ((flags & DispatchPropertyGet) != 0 && name == "Count")
            {
                ThrowIfFailed(Vtable<GetIntegerProperty>(instance, 9)(instance, out int count),
                    "DocumentProperties.Count");
                return count;
            }
            if ((flags & DispatchMethod) != 0 && name == "Item")
            {
                if (arguments == null || arguments.Length != 1)
                {
                    throw new TargetParameterCountException("DocumentProperties.Item");
                }
                WpsVariant index = ToVariant(session, arguments[0]);
                try
                {
                    ThrowIfFailed(
                        Vtable<GetDocumentProperty>(instance, 8)(instance, index, 0, out IntPtr property),
                        "DocumentProperties.Item");
                    return property == IntPtr.Zero
                        ? null
                        : new WpsDynamicDispatch(session, property, WpsDynamicKind.DocumentProperty);
                }
                finally
                {
                    ClearStandaloneVariant(ref index);
                }
            }
            if ((flags & DispatchMethod) != 0 && name == "Add")
            {
                if (arguments == null || arguments.Length != 5)
                {
                    throw new TargetParameterCountException("DocumentProperties.Add");
                }
                IntPtr nativeName = ToBstr(Convert.ToString(arguments[0]) ?? string.Empty);
                WpsVariant type = ToVariant(session, arguments[2]);
                WpsVariant value = ToVariant(session, arguments[3]);
                WpsVariant linkSource = ToVariant(session, arguments[4]);
                try
                {
                    ThrowIfFailed(
                        Vtable<AddDocumentProperty>(instance, 10)(
                            instance,
                            nativeName,
                            Convert.ToBoolean(arguments[1]) ? (short)-1 : (short)0,
                            type,
                            value,
                            linkSource,
                            0,
                            out IntPtr property),
                        "DocumentProperties.Add");
                    return property == IntPtr.Zero
                        ? null
                        : new WpsDynamicDispatch(session, property, WpsDynamicKind.DocumentProperty);
                }
                finally
                {
                    freeString(nativeName);
                    ClearStandaloneVariant(ref type);
                    ClearStandaloneVariant(ref value);
                    ClearStandaloneVariant(ref linkSource);
                }
            }
        }
        else if (kind == WpsDynamicKind.DocumentProperty)
        {
            if ((flags & DispatchMethod) != 0 && name == "Delete")
            {
                ThrowIfFailed(Vtable<DeleteDocumentProperty>(instance, 8)(instance),
                    "DocumentProperty.Delete");
                return null;
            }
            if ((flags & DispatchPropertyGet) != 0 && name == "Name")
            {
                ThrowIfFailed(
                    Vtable<GetBstrPropertyWithLocale>(instance, 9)(instance, 0, out IntPtr text),
                    "DocumentProperty.Name");
                return ReadAndFreeBstr(text);
            }
            if ((flags & DispatchPropertyGet) != 0 && name == "Value")
            {
                IntPtr value = Marshal.AllocHGlobal(VariantSize);
                try
                {
                    Marshal.StructureToPtr(new WpsVariant { Type = VtEmpty }, value, false);
                    ThrowIfFailed(
                        Vtable<GetVariantPropertyWithLocale>(instance, 11)(instance, 0, value),
                        "DocumentProperty.Value");
                    return DecodeVariant(session, value);
                }
                finally
                {
                    clearVariant(value);
                    Marshal.FreeHGlobal(value);
                }
            }
            if ((flags & DispatchPropertyGet) != 0 && name == "Type")
            {
                ThrowIfFailed(
                    Vtable<GetIntegerPropertyWithLocale>(instance, 13)(instance, 0, out int type),
                    "DocumentProperty.Type");
                return type;
            }
            if ((flags & DispatchPropertyGet) != 0 && name == "LinkToContent")
            {
                ThrowIfFailed(
                    Vtable<GetBooleanProperty>(instance, 15)(instance, out short linked),
                    "DocumentProperty.LinkToContent");
                return linked != 0;
            }
            if ((flags & DispatchPropertyGet) != 0 && name == "LinkSource")
            {
                ThrowIfFailed(
                    Vtable<GetBstrProperty>(instance, 17)(instance, out IntPtr text),
                    "DocumentProperty.LinkSource");
                return ReadAndFreeBstr(text);
            }
        }
        return InvokeDispatch(session, instance, name, flags, arguments);
    }

    private string ReadAndFreeBstr(IntPtr value)
    {
        if (value == IntPtr.Zero) return string.Empty;
        try
        {
            return Marshal.PtrToStringUni(value, checked((int)stringLength(value)));
        }
        finally
        {
            freeString(value);
        }
    }

    private void ClearStandaloneVariant(ref WpsVariant value)
    {
        IntPtr storage = Marshal.AllocHGlobal(VariantSize);
        try
        {
            Marshal.StructureToPtr(value, storage, false);
            clearVariant(storage);
            value = new WpsVariant { Type = VtEmpty };
        }
        finally
        {
            Marshal.FreeHGlobal(storage);
        }
    }

    private int ResolveDispatchId(IntPtr instance, string name)
    {
        IntPtr text = Marshal.StringToHGlobalUni(name);
        IntPtr names = Marshal.AllocHGlobal(IntPtr.Size);
        IntPtr identifier = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteIntPtr(names, text);
            Guid iid = Guid.Empty;
            ThrowIfFailed(
                Vtable<GetIdsOfNames>(instance, 5)(instance, ref iid, names, 1, 0, identifier),
                "IDispatch.GetIDsOfNames(" + name + ")");
            return Marshal.ReadInt32(identifier);
        }
        finally
        {
            Marshal.FreeHGlobal(identifier);
            Marshal.FreeHGlobal(names);
            Marshal.FreeHGlobal(text);
        }
    }

    internal void QuitApplication(IntPtr application, int slot)
    {
        if (application == IntPtr.Zero) return;
        IntPtr save = AllocateVariant(null, Type.Missing);
        IntPtr format = AllocateVariant(null, Type.Missing);
        IntPtr route = AllocateVariant(null, Type.Missing);
        try
        {
            ThrowIfFailed(Vtable<NativeQuitApplication>(application, slot)(application, save, format, route),
                "Application.Quit");
        }
        finally
        {
            foreach (IntPtr value in new[] { save, format, route })
            {
                clearVariant(value);
                Marshal.FreeHGlobal(value);
            }
        }
    }

    internal void AddRef(IntPtr value)
    {
        if (value != IntPtr.Zero) Vtable<ReferenceObject>(value, 1)(value);
    }

    internal void Release(IntPtr value)
    {
        if (value != IntPtr.Zero) Vtable<ReferenceObject>(value, 2)(value);
    }

    internal long Identity(IntPtr value)
    {
        if (value == IntPtr.Zero) return 0L;
        Guid iid = new Guid("00000000-0000-0000-C000-000000000046");
        ThrowIfFailed(Vtable<QueryObject>(value, 0)(value, ref iid, out IntPtr identity),
            "IUnknown.QueryInterface");
        if (identity == IntPtr.Zero)
        {
            throw new InvalidOperationException("WPS RPC 未返回 IUnknown 身份指针。");
        }
        try
        {
            return identity.ToInt64();
        }
        finally
        {
            Release(identity);
        }
    }

    internal void ClearVariantValue(IntPtr value)
    {
        if (value != IntPtr.Zero) ThrowIfFailed(clearVariant(value), "VariantClear");
    }

    private IntPtr PointerFromObject(object value)
    {
        if (value == null) return IntPtr.Zero;
        if (WpsVtableProxy.Owns(value)) return WpsVtableProxy.From(value).Pointer;
        if (value is WpsDynamicDispatch dynamicDispatch) return dynamicDispatch.Pointer;
        throw new InvalidCastException("对象不是 PartyOps WPS RPC 代理：" + value.GetType().FullName);
    }

    private IntPtr ToBstr(string value)
    {
        IntPtr characters = Marshal.StringToHGlobalUni(value ?? string.Empty);
        try
        {
            IntPtr result = allocateString(characters, checked((uint)(value ?? string.Empty).Length));
            return result != IntPtr.Zero ? result : throw new OutOfMemoryException("WPS BSTR 分配失败。");
        }
        finally
        {
            Marshal.FreeHGlobal(characters);
        }
    }

    private T Symbol<T>(string name) where T : class
    {
        return (T)(object)Marshal.GetDelegateForFunctionPointer(
            NativeLibraryLoader.Symbol(library, name), typeof(T));
    }

    private static T Vtable<T>(IntPtr instance, int slot) where T : class
    {
        return (T)(object)Marshal.GetDelegateForFunctionPointer(VtablePointer(instance, slot), typeof(T));
    }

    private static IntPtr VtablePointer(IntPtr instance, int slot)
    {
        if (instance == IntPtr.Zero) throw new ObjectDisposedException("WPS COM interface");
        return Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), checked(slot * IntPtr.Size));
    }

    private static void ThrowIfFailed(int result, string operation)
    {
        if (result < 0)
        {
            throw new COMException(operation + " 失败（HRESULT=0x" + result.ToString("X8") + "）。", result);
        }
    }

    private readonly struct ReturnBuffer
    {
        internal static readonly ReturnBuffer Empty = new ReturnBuffer(IntPtr.Zero, false, () => { });

        internal ReturnBuffer(IntPtr pointer, bool variant, Action dispose)
        {
            Pointer = pointer;
            Variant = variant;
            Dispose = dispose;
        }

        internal IntPtr Pointer { get; }
        internal bool Variant { get; }
        internal Action Dispose { get; }
    }
}

internal sealed class NativeDelegateFactory
{
    private readonly ConcurrentDictionary<string, Type> types = new ConcurrentDictionary<string, Type>();
    private readonly ModuleBuilder module;
    private int serial;

    internal NativeDelegateFactory()
    {
        AssemblyName name = new AssemblyName("PartyOps.Wps.NativeDelegates");
        AssemblyBuilder assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
        module = assembly.DefineDynamicModule(name.Name);
    }

    internal Delegate Create(IntPtr function, Type[] parameters)
    {
        string key = string.Join("|", parameters.Select(item => item.FullName));
        Type type = types.GetOrAdd(key, _ => Build(parameters));
        return Marshal.GetDelegateForFunctionPointer(function, type);
    }

    private Type Build(Type[] parameters)
    {
        string name = "WpsCall" + Interlocked.Increment(ref serial);
        TypeBuilder builder = module.DefineType(
            name,
            TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.Sealed,
            typeof(MulticastDelegate));
        ConstructorInfo callingConvention = typeof(UnmanagedFunctionPointerAttribute)
            .GetConstructor(new[] { typeof(CallingConvention) });
        builder.SetCustomAttribute(new CustomAttributeBuilder(
            callingConvention, new object[] { CallingConvention.Cdecl }));
        ConstructorBuilder constructor = builder.DefineConstructor(
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            new[] { typeof(object), typeof(IntPtr) });
        constructor.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);
        MethodBuilder invoke = builder.DefineMethod(
            "Invoke",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            typeof(int),
            parameters);
        invoke.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);
        return builder.CreateType();
    }
}

internal sealed class WpsEnumVariant : IEnumerator, IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NextValue(IntPtr self, uint count, IntPtr values, out uint fetched);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ResetValues(IntPtr self);

    private readonly WpsRpcSession session;
    private readonly Type elementType;
    private IntPtr pointer;
    private object current;

    internal WpsEnumVariant(WpsRpcSession session, IntPtr pointer, Type elementType)
    {
        this.session = session;
        this.pointer = pointer;
        this.elementType = elementType;
    }

    public object Current => current;

    public bool MoveNext()
    {
        IntPtr value = Marshal.AllocHGlobal(16);
        try
        {
            Marshal.StructureToPtr(new WpsVariant(), value, false);
            int result = Vtable<NextValue>(pointer, 3)(pointer, 1, value, out uint fetched);
            if (result < 0) throw new COMException("IEnumVARIANT.Next 失败。", result);
            if (fetched == 0)
            {
                current = null;
                return false;
            }
            current = session.DecodeVariant(value, elementType);
            return true;
        }
        finally
        {
            try
            {
                session.ClearVariant(value);
            }
            finally
            {
                Marshal.FreeHGlobal(value);
            }
        }
    }

    public void Reset()
    {
        int result = Vtable<ResetValues>(pointer, 5)(pointer);
        if (result < 0) throw new COMException("IEnumVARIANT.Reset 失败。", result);
        current = null;
    }

    internal static T Vtable<T>(IntPtr instance, int slot) where T : class
    {
        IntPtr address = Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size);
        return (T)(object)Marshal.GetDelegateForFunctionPointer(address, typeof(T));
    }

    public void Dispose()
    {
        IntPtr owned = Interlocked.Exchange(ref pointer, IntPtr.Zero);
        if (owned != IntPtr.Zero) session.Release(owned);
    }

    ~WpsEnumVariant()
    {
        Dispose();
    }
}

internal static class PortableFontAliases
{
    internal static string ToSourceName(string value)
    {
        // WPS Linux 的英文 API 名与 Windows 中文版 WPS 的本地化名称指向
        // 同一字体。统一回读名称可避免源码把 SimSun→宋体误判为格式失败。
        if (string.Equals(value, "SimSun", StringComparison.OrdinalIgnoreCase))
        {
            return "宋体";
        }
        return value;
    }
}

internal enum WpsDynamicKind
{
    Generic,
    DocumentProperties,
    DocumentProperty
}

internal sealed class WpsDynamicDispatch : DynamicObject
{
    private const ushort DispatchMethod = 0x1;
    private const ushort DispatchPropertyGet = 0x2;
    private const ushort DispatchPropertyPut = 0x4;
    private readonly WpsRpcSession session;
    private readonly WpsDynamicKind kind;
    private IntPtr pointer;

    internal WpsDynamicDispatch(WpsRpcSession session, IntPtr pointer, WpsDynamicKind kind)
    {
        this.session = session;
        this.pointer = pointer;
        this.kind = kind;
    }

    internal IntPtr Pointer => pointer;

    public override bool TryGetMember(GetMemberBinder binder, out object result)
    {
        try
        {
            WpsBridgeTrace.Write("DCALL get " + binder.Name);
            result = session.InvokeOfficeDynamic(
                pointer, kind, binder.Name, DispatchPropertyGet, Array.Empty<object>());
            WpsBridgeTrace.Write("DOK   get " + binder.Name + " => " + (result?.GetType().FullName ?? "<null>"));
            return true;
        }
        catch (Exception error)
        {
            WpsBridgeTrace.Write("DFAIL get " + binder.Name + " => " + error);
            throw;
        }
    }

    public override bool TrySetMember(SetMemberBinder binder, object value)
    {
        try
        {
            WpsBridgeTrace.Write("DCALL set " + binder.Name);
            session.InvokeOfficeDynamic(pointer, kind, binder.Name, DispatchPropertyPut, new[] { value });
            WpsBridgeTrace.Write("DOK   set " + binder.Name);
            return true;
        }
        catch (Exception error)
        {
            WpsBridgeTrace.Write("DFAIL set " + binder.Name + " => " + error);
            throw;
        }
    }

    public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result)
    {
        try
        {
            WpsBridgeTrace.Write("DCALL method " + binder.Name);
            result = session.InvokeOfficeDynamic(pointer, kind, binder.Name, DispatchMethod, args);
            WpsBridgeTrace.Write("DOK   method " + binder.Name + " => " + (result?.GetType().FullName ?? "<null>"));
            return true;
        }
        catch (Exception error)
        {
            WpsBridgeTrace.Write("DFAIL method " + binder.Name + " => " + error);
            throw;
        }
    }

    // 与 WpsVtableProxy 相同，临时属性代理不得由 Mono 终结器线程异步释放。
}

internal static class NativeLibraryLoader
{
    private const int RtldNow = 2;

    [DllImport("libdl.so.2", EntryPoint = "dlopen", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr LinuxOpen(string path, int flags);
    [DllImport("libdl.so.2", EntryPoint = "dlsym", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr LinuxSymbol(IntPtr handle, string name);
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dlopen", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr MacOpen(string path, int flags);
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dlsym", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr MacSymbol(IntPtr handle, string name);

    internal static IntPtr Open(string path)
    {
        IntPtr handle = Environment.OSVersion.Platform == PlatformID.MacOSX
            ? MacOpen(path, RtldNow)
            : LinuxOpen(path, RtldNow);
        return handle != IntPtr.Zero
            ? handle
            : throw new DllNotFoundException("无法加载 WPS RPC 库：" + path);
    }

    internal static IntPtr Symbol(IntPtr handle, string name)
    {
        IntPtr address = Environment.OSVersion.Platform == PlatformID.MacOSX
            ? MacSymbol(handle, name)
            : LinuxSymbol(handle, name);
        return address != IntPtr.Zero
            ? address
            : throw new EntryPointNotFoundException("WPS RPC 缺少导出：" + name);
    }
}

internal static class WpsBridgeTrace
{
    private static readonly bool Enabled = string.Equals(
        Environment.GetEnvironmentVariable("PARTYOPS_WPS_TRACE"),
        "1",
        StringComparison.Ordinal);

    internal static void Write(string message)
    {
        if (Enabled)
        {
            Console.Error.WriteLine("[PARTYOPS_WPS_RPC] " + message);
        }
    }

    internal static string DescribeResult(MethodInfo method, object result)
    {
        if (result == null)
        {
            return "<null>";
        }
        // 诊断字体别名时只输出字体接口的短字符串属性；不得把 Range.Text
        // 等用户公文正文写进追踪日志。
        if (Enabled
            && result is string text
            && string.Equals(method.DeclaringType?.Name, "_Font", StringComparison.Ordinal)
            && text.Length <= 128)
        {
            return "System.String[" + text + "]";
        }
        return result.GetType().FullName;
    }
}
