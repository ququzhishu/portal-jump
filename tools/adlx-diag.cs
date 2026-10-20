// adlx-diag.cs —— AMD ADLX 环境只读诊断工具（折返跃迁 方案 B 配套）
//
// 用途：在 AMD 机器上独立验证 amdadlx64.dll 能否枚举到显示器、缩放接口是否可用。
//       纯只读，不改任何显示设置，可安全反复运行。
//
// 编译（用系统自带 .NET Framework 4 编译器，无需 SDK）：
//   csc /nologo /unsafe /target:exe /out:adlx-diag.exe adlx-diag.cs
// 运行：建议以管理员身份运行（写缩放需要管理员；纯读诊断普通权限即可）。
//
// 本文件同时是 src/AdlxBridge.cs 的"vtable 偏移说明书"——所有偏移都在
// 本机（RX 9070 XT，ADLX 1.4）实测验证过，改动偏移前请先用本工具确认。

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;

static class AdlxDiag
{
    static List<string> Log = new List<string>();
    static void L(string s) { Console.WriteLine(s); Log.Add(s); }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr LoadLibraryW(string path);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr GetProcAddress(IntPtr h, string name);
    [DllImport("ole32.dll")]
    static extern int CoInitialize(IntPtr pv);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int QFV(out ulong v);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Init(ulong v, out IntPtr s);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Op1(IntPtr s, out IntPtr p);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Sz(IntPtr s);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int At(IntPtr s, uint loc, out IntPtr p);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Oi(IntPtr s, out int v);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Do(IntPtr s, IntPtr d, out IntPtr p);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int NameFn(IntPtr s, out IntPtr p);

    [STAThread]
    static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        CoInitialize(IntPtr.Zero);

        IntPtr h = LoadLibraryW(@"C:\Windows\System32\amdadlx64.dll");
        if (h == IntPtr.Zero) { L("找不到 C:\\Windows\\System32\\amdadlx64.dll —— 请安装完整 AMD Software 驱动。"); Flush(); return; }
        L("已加载 amdadlx64.dll");

        // ---- 版本 ----
        var qfv = (QFV)Marshal.GetDelegateForFunctionPointer(GetProcAddress(h, "ADLXQueryFullVersion"), typeof(QFV));
        ulong full; int rq = qfv(out full);
        uint major = (uint)((full >> 48) & 0xFFFF);
        uint minor = (uint)((full >> 32) & 0xFFFF);
        L("ADLX 版本数值=0x" + full.ToString("X") + "（查询返回 " + rq + "）");

        IntPtr pVer = GetProcAddress(h, "ADLXQueryVersion");
        if (pVer != IntPtr.Zero)
        {
            var qvs = (QVerStr)Marshal.GetDelegateForFunctionPointer(pVer, typeof(QVerStr));
            IntPtr pStr;
            if (qvs(out pStr) == 0 && pStr != IntPtr.Zero)
                L("ADLX 版本字符串=" + Marshal.PtrToStringAnsi(pStr));
        }

        // ---- 初始化：必须用 WithIncompatibleDriver ----
        // 标准 ADLXInitialize 在本机上会让 GetNumberOfDisplays 内部空引用崩溃；
        // 兼容入口是官方为内部 ADL 初始化异常的机器准备的完整路径，功能不缺。
        IntPtr pInit = GetProcAddress(h, "ADLXInitializeWithIncompatibleDriver");
        if (pInit == IntPtr.Zero) { L("无 ADLXInitializeWithIncompatibleDriver 导出。"); Flush(); return; }
        var init = (Init)Marshal.GetDelegateForFunctionPointer(pInit, typeof(Init));
        IntPtr sys;
        int ri = init(full, out sys);
        L("ADLXInitializeWithIncompatibleDriver 返回 " + ri + "（0=成功）");
        if (ri != 0 || sys == IntPtr.Zero) { Flush(); return; }

        // IADLXSystem 是单例，vtable 无 Acquire/Release 前缀，直接从业务方法开始：
        //   [0]HybridGraphicsType [1]GetGPUs [2]QueryInterface [3]GetDisplaysServices
        var getDisp = (Op1)Marshal.GetDelegateForFunctionPointer(Vtbl(sys, 3), typeof(Op1));
        IntPtr svc;
        if (getDisp(sys, out svc) != 0 || svc == IntPtr.Zero) { L("GetDisplaysServices 失败。"); Flush(); return; }

        // IADLXDisplayServices（标准 [0]Acquire [1]Release [2]QI）：
        //   [3]GetNumberOfDisplays [4]GetDisplays
        //   [11]GetGPUScaling [12]GetScalingMode [13]GetIntegerScaling
        var gcn = (Oi)Marshal.GetDelegateForFunctionPointer(Vtbl(svc, 3), typeof(Oi));
        int num;
        int rn = gcn(svc, out num);
        L("显示器数量 = " + num + "（GetNumberOfDisplays 返回 " + rn + "）");
        if (num == 0) { L("枚举不到显示器：请检查显示器是否直连 AMD 显卡、是否处于远程会话。"); Flush(); return; }

        var gd = (Op1)Marshal.GetDelegateForFunctionPointer(Vtbl(svc, 4), typeof(Op1));
        IntPtr list;
        gd(svc, out list);

        // IADLXDisplayList（标准前缀）：
        //   [3]Size() 无参返回  [5]Begin()  [7]At 泛型（会返回 UNKNOWN_INTERFACE）
        //   [11]At_DisplayList 类型安全，必须用这个
        var size = (Sz)Marshal.GetDelegateForFunctionPointer(Vtbl(list, 3), typeof(Sz));
        var begin = (Sz)Marshal.GetDelegateForFunctionPointer(Vtbl(list, 5), typeof(Sz));
        var at = (At)Marshal.GetDelegateForFunctionPointer(Vtbl(list, 11), typeof(At));
        uint loc = (uint)begin(list);
        for (uint i = loc; i < (uint)size(list); i++)
        {
            IntPtr disp;
            if (at(list, i, out disp) != 0 || disp == IntPtr.Zero) { L("At_DisplayList(" + i + ") 失败。"); continue; }

            // IADLXDisplay（标准前缀）：[6]Name(const char**)
            var nameFn = (NameFn)Marshal.GetDelegateForFunctionPointer(Vtbl(disp, 6), typeof(NameFn));
            IntPtr pName;
            string dname = nameFn(disp, out pName) == 0 && pName != IntPtr.Zero
                ? Marshal.PtrToStringAnsi(pName) : "(未知)";
            L("");
            L("显示器[" + i + "]：" + dname);

            ReportScaling(svc, disp, 11, "GPU 缩放开关 (IADLXDisplayGPUScaling)");
            ReportScaleMode(svc, disp, 12);
            ReportScaling(svc, disp, 13, "整数缩放 (IADLXDisplayIntegerScaling)");
        }

        L("");
        L("诊断完成。若显示器数量≥1 且 GPU 缩放开关 IsSupported=True，折返跃迁 AMD 版即可用。");
        Flush();
    }

    // 报告 [3]IsSupported [4]IsEnabled
    static void ReportScaling(IntPtr svc, IntPtr disp, int vindex, string title)
    {
        var get = (Do)Marshal.GetDelegateForFunctionPointer(Vtbl(svc, vindex), typeof(Do));
        IntPtr iface;
        if (get(svc, disp, out iface) != 0 || iface == IntPtr.Zero) { L("  " + title + "：取不到接口"); return; }
        var sup = (Oi)Marshal.GetDelegateForFunctionPointer(Vtbl(iface, 3), typeof(Oi));
        var en = (Oi)Marshal.GetDelegateForFunctionPointer(Vtbl(iface, 4), typeof(Oi));
        int s, e;
        sup(iface, out s); en(iface, out e);
        L("  " + title + "：IsSupported=" + (s != 0) + "，IsEnabled=" + (e != 0));
    }

    // ScalingMode 特有：[3]IsSupported [4]GetMode
    static void ReportScaleMode(IntPtr svc, IntPtr disp, int vindex)
    {
        var get = (Do)Marshal.GetDelegateForFunctionPointer(Vtbl(svc, vindex), typeof(Do));
        IntPtr iface;
        if (get(svc, disp, out iface) != 0 || iface == IntPtr.Zero) { L("  缩放模式 (IADLXDisplayScalingMode)：取不到接口"); return; }
        var sup = (Oi)Marshal.GetDelegateForFunctionPointer(Vtbl(iface, 3), typeof(Oi));
        var gm = (Oi)Marshal.GetDelegateForFunctionPointer(Vtbl(iface, 4), typeof(Oi));
        int s, m;
        sup(iface, out s); gm(iface, out m);
        string[] names = { "保持比例", "全屏铺满", "居中" };
        string mn = m >= 0 && m < names.Length ? names[m] : ("未知 " + m);
        L("  缩放模式 (IADLXDisplayScalingMode)：IsSupported=" + (s != 0) + "，当前=" + m + "（" + mn + "）");
    }

    static IntPtr Vtbl(IntPtr obj, int index)
    {
        return Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), index * IntPtr.Size);
    }

    static void Flush()
    {
        try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adlx-diag-out.txt"),
            string.Join("\n", Log.ToArray()), new UTF8Encoding(false)); }
        catch { }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int QVerStr(out IntPtr pStr);
}
