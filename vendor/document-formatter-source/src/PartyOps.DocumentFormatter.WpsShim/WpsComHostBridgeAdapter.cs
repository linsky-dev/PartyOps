using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace PartyOps.DocumentFormatter.Wps;

/// <summary>
/// 以延迟绑定方式调用主业务程序集，避免 WPS 的 CLR COM 类工厂在创建加载桥时
/// 先解析整套 VSTO 依赖。真正执行功能时仍复用同一个恢复后的业务入口。
/// </summary>
internal static class WpsComHostBridgeAdapter
{
    private static readonly object Sync = new object();
    private static Assembly businessAssembly;
    private static Type bridgeType;

    public static void Initialize(object application)
    {
        Invoke("Initialize", application);
    }

    public static void Shutdown()
    {
        Invoke("Shutdown");
    }

    public static void ExecuteFeature(object application, string featureId, bool autoCloseSuccess)
    {
        Invoke("ExecuteFeature", application, featureId, autoCloseSuccess);
    }

    public static void ExecuteConvert(object application, string formatName)
    {
        Type formatType = LoadBridgeType().Assembly.GetType("DocumentRepository.Models.Conversion.ConvertFormat", true);
        object format = Enum.Parse(formatType, formatName, true);
        Invoke("ExecuteConvert", application, format);
    }

    public static void OpenFormatSettings() => Invoke("OpenFormatSettings");
    public static void OpenReplaceSettings() => Invoke("OpenReplaceSettings");
    public static void OpenRedHeaderSettings() => Invoke("OpenRedHeaderSettings");
    public static void OpenRenameSettings(object application) => Invoke("OpenRenameSettings", application);
    public static void OpenConvertSettings() => Invoke("OpenConvertSettings");
    private static object Invoke(string methodName, params object[] arguments)
    {
        MethodInfo method = LoadBridgeType().GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(candidate => candidate.Name == methodName && candidate.GetParameters().Length == arguments.Length);
        if (method == null)
        {
            throw new MissingMethodException(LoadBridgeType().FullName, methodName);
        }
        try
        {
            return method.Invoke(null, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            throw exception.InnerException;
        }
    }

    private static Type LoadBridgeType()
    {
        lock (Sync)
        {
            if (bridgeType != null)
            {
                return bridgeType;
            }
            string directory = Path.GetDirectoryName(typeof(WpsComHostBridgeAdapter).Assembly.Location);
            string assemblyPath = Path.Combine(directory ?? string.Empty, "PartyOps.DocumentFormatter.AddIn.dll");
            if (!File.Exists(assemblyPath))
            {
                throw new FileNotFoundException("找不到主业务程序集，无法启动 WPS 功能。", assemblyPath);
            }
            businessAssembly = Assembly.LoadFrom(assemblyPath);
            bridgeType = businessAssembly.GetType("DocumentRepository.Services.Hosting.WpsComHostBridge", true);
            return bridgeType;
        }
    }
}
