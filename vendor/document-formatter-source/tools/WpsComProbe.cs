using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

internal static class WpsComProbe
{
    private static readonly object Sync = new object();
    private static string _result;

    [STAThread]
    private static int Main(string[] args)
    {
        string progId = args != null && args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0] : "partyops.documentformatter";
        var worker = new Thread(() => Probe(progId)) { IsBackground = true };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();

        if (!worker.Join(TimeSpan.FromSeconds(10)))
        {
            Console.WriteLine("status=timeout");
            return 124;
        }

        lock (Sync)
        {
            Console.WriteLine(_result ?? "status=empty");
        }

        return 0;
    }

    private static void Probe(string progId)
    {
        object app = null;
        object comAddIns = null;
        object addin = null;
        try
        {
            Environment.SetEnvironmentVariable("VSTO_LOGALERTS", "1");
            Environment.SetEnvironmentVariable("VSTO_SUPPRESSDISPLAYALERTS", "0");
            var appType = Type.GetTypeFromProgID("KWPS.Application", throwOnError: true);
            app = Activator.CreateInstance(appType);
            var appDispatch = (dynamic)app;
            comAddIns = appDispatch.COMAddIns;
            dynamic comAddInsDispatch = comAddIns;
            // WPS 官方对象模型要求显式 Update，才能把启动后写入注册表的新加载项刷新到集合。
            comAddInsDispatch.Update();
            int count = Convert.ToInt32(comAddInsDispatch.Count);
            var entries = new List<string>();
            for (int index = 1; index <= count; index++)
            {
                object current = null;
                try
                {
                    current = comAddInsDispatch.Item(index);
                    dynamic currentDispatch = current;
                    entries.Add(Convert.ToString(currentDispatch.ProgId) + ":" + Convert.ToBoolean(currentDispatch.Connect));
                }
                catch (Exception ex)
                {
                    entries.Add("#" + index + ":error=0x" + (ex.HResult & 0xffffffff).ToString("X8"));
                }
                finally
                {
                    if (current != null && Marshal.IsComObject(current)) Marshal.FinalReleaseComObject(current);
                }
            }
            Console.WriteLine("collectionCount=" + count + ";entries=" + string.Join(",", entries));
            addin = comAddInsDispatch.Item(progId);
            dynamic addinDispatch = addin;
            var initial = Convert.ToBoolean(addinDispatch.Connect);
            try
            {
                addinDispatch.Connect = true;
                lock (Sync)
                {
                    _result = "status=completed;initial=" + initial + ";after=" + Convert.ToBoolean(addinDispatch.Connect);
                }
            }
            catch (Exception ex)
            {
                lock (Sync)
                {
                    _result = "status=error;initial=" + initial + ";type=" + ex.GetType().FullName + ";hresult=0x" + (ex.HResult & 0xffffffff).ToString("X8") + ";message=" + ex.Message.Replace(';', ',');
                }
            }
        }
        catch (Exception ex)
        {
            lock (Sync)
            {
                _result = "status=error;type=" + ex.GetType().FullName + ";hresult=0x" + (ex.HResult & 0xffffffff).ToString("X8") + ";message=" + ex.Message.Replace(';', ',');
            }
        }
        finally
        {
            if (addin != null && Marshal.IsComObject(addin))
            {
                Marshal.FinalReleaseComObject(addin);
            }
            if (comAddIns != null && Marshal.IsComObject(comAddIns))
            {
                Marshal.FinalReleaseComObject(comAddIns);
            }
            if (app != null && Marshal.IsComObject(app))
            {
                Marshal.FinalReleaseComObject(app);
            }
        }
    }
}
