using System;
using System.Runtime.InteropServices;

internal static class WpsComActivationProbe
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var progId = args != null && args.Length > 0 ? args[0] : "PartyOps.DocumentFormatter.WpsAddIn";
            var type = Type.GetTypeFromProgID(progId, true);
            Console.WriteLine("type=" + type.FullName + ";guid=" + type.GUID);
            dynamic instance = Activator.CreateInstance(type);
            Console.WriteLine("instance=" + instance.GetType().FullName);
            string xml = instance.GetCustomUI("Microsoft.Word.Document");
            Console.WriteLine("xmlLength=" + (xml == null ? -1 : xml.Length));
            if (Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("error=" + ex.GetType().FullName + ";hresult=0x" + (ex.HResult & 0xffffffff).ToString("X8") + ";message=" + ex.Message.Replace(';', ','));
            return 1;
        }
    }
}
