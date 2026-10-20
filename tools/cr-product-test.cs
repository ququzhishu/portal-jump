using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
using PortalJump;

static class CrProductTest
{
    [DllImport("ole32.dll")] static extern int CoInitialize(IntPtr p);
    static List<string> log = new List<string>();
    static void L(string s) { Console.WriteLine(s); log.Add(s); }

    [STAThread]
    static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        CoInitialize(IntPtr.Zero);

        bool ready = AdlxBridge.TryInit();
        L("TryInit=" + ready + " err=" + AdlxBridge.Error + " display=" + AdlxBridge.DisplayName);

        bool sup; string detail;
        L("Supported=" + AdlxBridge.CustomResolutionSupported(out sup, out detail)
            + " sup=" + sup + " detail=" + detail);

        Dump("创建前");
        int hz;
        bool ok = AdlxBridge.CreateCustomResolution(1280, 960, out hz, out detail);
        L("Create=" + ok + " hz=" + hz + " detail=" + detail);
        Dump("创建后");
        bool del = AdlxBridge.DeleteCustomResolution(1280, 960, out detail);
        L("Delete=" + del + " detail=" + detail);
        Dump("删除后");

        try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cr-prod-out.txt"),
            string.Join("\n", log.ToArray()), new UTF8Encoding(false)); } catch { }
    }

    static void Dump(string title)
    {
        var list = AdlxBridge.EnumerateCustomResolutions();
        L(title + " 数量=" + list.Count);
        foreach (var c in list) L("  " + c.Width + "x" + c.Height + "@" + c.RefreshRate);
    }
}
