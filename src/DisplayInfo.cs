using System;
using System.Collections.Generic;

namespace PortalJump
{
    /// <summary>一个可用分辨率（同宽高只保留最高刷新率）</summary>
    public struct DisplayMode
    {
        public int Width;
        public int Height;
        public int MaxHz;
        public int ColorDepth;

        public string Text
        {
            get
            {
                string s = Width + "x" + Height;
                if (MaxHz > 0) s += "  （最高 " + MaxHz + "Hz）";
                return s;
            }
        }

        public override string ToString()
        {
            return Text;
        }
    }

    /// <summary>
    /// 分辨率层面的兼容层。
    ///
    /// v0.11 起真正的实现在 DisplayConfigBackend（结构体布局修正为 DEVMODEA 156 字节，
    /// 并补上了跨厂商的缩放字段）。这里保留原有 API 名称，
    /// 让界面层（WPF/WinForms）与既有测试工具不用大改。
    /// </summary>
    public static class DisplayInfo
    {
        /// <summary>DEVMODE 结构（现在与 DisplayConfigBackend.DEVMODE 是同一个）</summary>
        public struct DEVMODE
        {
            public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields;
            public int dmPositionX, dmPositionY;
            public int dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType;
            public int dmReserved1, dmReserved2;
            public int dmPanningWidth, dmPanningHeight;

            internal static DEVMODE FromNative(DisplayConfigBackend.DEVMODE n)
            {
                var d = new DEVMODE();
                d.dmDeviceName = n.dmDeviceName;
                d.dmSpecVersion = n.dmSpecVersion;
                d.dmDriverVersion = n.dmDriverVersion;
                d.dmSize = n.dmSize;
                d.dmDriverExtra = n.dmDriverExtra;
                d.dmFields = n.dmFields;
                d.dmPositionX = n.dmPositionX;
                d.dmPositionY = n.dmPositionY;
                d.dmDisplayOrientation = n.dmDisplayOrientation;
                d.dmDisplayFixedOutput = n.dmDisplayFixedOutput;
                d.dmColor = n.dmColor;
                d.dmDuplex = n.dmDuplex;
                d.dmYResolution = n.dmYResolution;
                d.dmTTOption = n.dmTTOption;
                d.dmCollate = n.dmCollate;
                d.dmFormName = n.dmFormName;
                d.dmLogPixels = n.dmLogPixels;
                d.dmBitsPerPel = n.dmBitsPerPel;
                d.dmPelsWidth = n.dmPelsWidth;
                d.dmPelsHeight = n.dmPelsHeight;
                d.dmDisplayFlags = n.dmDisplayFlags;
                d.dmDisplayFrequency = n.dmDisplayFrequency;
                d.dmICMMethod = n.dmICMMethod;
                d.dmICMIntent = n.dmICMIntent;
                d.dmMediaType = n.dmMediaType;
                d.dmDitherType = n.dmDitherType;
                d.dmReserved1 = n.dmReserved1;
                d.dmReserved2 = n.dmReserved2;
                d.dmPanningWidth = n.dmPanningWidth;
                d.dmPanningHeight = n.dmPanningHeight;
                return d;
            }

            internal DisplayConfigBackend.DEVMODE ToNative()
            {
                DisplayConfigBackend.DEVMODE n = DisplayConfigBackend.NewDevMode();
                n.dmDeviceName = dmDeviceName;
                n.dmFields = dmFields;
                n.dmPositionX = dmPositionX;
                n.dmPositionY = dmPositionY;
                n.dmDisplayOrientation = dmDisplayOrientation;
                n.dmDisplayFixedOutput = dmDisplayFixedOutput;
                n.dmBitsPerPel = dmBitsPerPel;
                n.dmPelsWidth = dmPelsWidth;
                n.dmPelsHeight = dmPelsHeight;
                n.dmDisplayFlags = dmDisplayFlags;
                n.dmDisplayFrequency = dmDisplayFrequency;
                n.dmPanningWidth = dmPanningWidth;
                n.dmPanningHeight = dmPanningHeight;
                return n;
            }
        }

        /// <summary>当前主显示器模式</summary>
        public static DEVMODE GetCurrentMode()
        {
            return DEVMODE.FromNative(DisplayConfigBackend.GetCurrentMode());
        }

        public static bool IsCurrent(int w, int h)
        {
            DEVMODE dm = GetCurrentMode();
            return dm.dmPelsWidth == w && dm.dmPelsHeight == h;
        }

        public static string CurrentModeText()
        {
            DEVMODE dm = GetCurrentMode();
            return dm.dmPelsWidth + "x" + dm.dmPelsHeight + " @" + dm.dmDisplayFrequency + "Hz " + dm.dmBitsPerPel + "位";
        }

        /// <summary>主显示器上该分辨率的所有模式，按刷新率从高到低</summary>
        public static List<DEVMODE> FindModes(int w, int h)
        {
            var list = new List<DEVMODE>();
            foreach (DisplayConfigBackend.DEVMODE m in DisplayConfigBackend.FindModes(w, h))
            {
                list.Add(DEVMODE.FromNative(m));
            }
            return list;
        }

        /// <summary>主显示器支持的全部分辨率（去重，按像素数从大到小）</summary>
        public static List<DisplayMode> GetResolutions()
        {
            return DisplayConfigBackend.GetResolutions();
        }

        /// <summary>应用一个模式，返回 ChangeDisplaySettingsEx 的原始结果码</summary>
        public static int ApplyMode(DEVMODE dm)
        {
            return DisplayConfigBackend.ApplyMode(dm.ToNative(), ScalingMode.Unchanged);
        }

        /// <summary>把 DISP_CHANGE_* 结果码翻成中文</summary>
        public static string DescribeResult(int code)
        {
            return DisplayConfigBackend.DescribeResult(code);
        }
    }
}
