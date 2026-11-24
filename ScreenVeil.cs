// ScreenVeil —— 双屏“老板键”
// 按一个热键：把所有窗口最小化，然后按配置把指定应用分别铺到每块屏幕上；
// 再按一次（或按恢复键）回到原来的样子。
//
// 编译：csc /target:winexe /out:ScreenVeil.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Web.Extensions.dll ScreenVeil.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace ScreenVeil
{
    // ───────────────────────────── Win32 结构体 ─────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width { get { return Right - Left; } }
        public int Height { get { return Bottom - Top; } }
        public override string ToString()
        {
            return string.Format("{0}x{1} @({2},{3})", Width, Height, Left, Top);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
    }

    // ───────────────────────────── Win32 API ─────────────────────────────

    internal static class Nat
    {
        public const int SW_HIDE = 0;
        public const int SW_SHOWNORMAL = 1;
        public const int SW_SHOWMINIMIZED = 2;
        public const int SW_MAXIMIZE = 3;
        public const int SW_SHOWNOACTIVATE = 4;
        public const int SW_SHOW = 5;
        public const int SW_MINIMIZE = 6;
        public const int SW_RESTORE = 9;

        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;

        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;
        public const uint GW_OWNER = 4;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int DWMWA_CLOAKED = 14;

        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        public const int WM_HOTKEY = 0x0312;

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll")]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);

        // ── 鼠标侧键：低级鼠标钩子（只有在绑了鼠标键、或正在录制时才装） ──

        public const int WH_MOUSE_LL = 14;
        public const int WM_XBUTTONDOWN = 0x020B;

        public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public int mouseData;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    }

    // ───────────────────────────── 数据模型 ─────────────────────────────

    internal class Monitor
    {
        public IntPtr Handle;
        public RECT Bounds;
        public RECT Work;
        public bool Primary;
        public int Index;

        public string Describe()
        {
            return string.Format("#{0}{1} {2}  工作区 {3}", Index, Primary ? " [主]" : "     ", Bounds, Work);
        }
    }

    internal class WindowInfo
    {
        public IntPtr H;
        public int Pid;
        public string Process;
        public string Title;
        public string ClassName;
        public RECT Rect;
        public bool Iconic;
    }

    internal class Target
    {
        public int Monitor = 1;
        public string Process = "";
        public string Title = "";
        public string Exe = "";
        public string Args = "";
        public bool Span = false;
        public Regex TitleRe;

        public string Describe()
        {
            return string.Format("屏{0} process='{1}'{2}{3}{4}",
                Monitor, Process,
                string.IsNullOrEmpty(Title) ? "" : " title='" + Title + "'",
                string.IsNullOrEmpty(Exe) ? "" : " exe='" + Exe + "'",
                Span ? " span=true" : "");
        }
    }

    internal class Config
    {
        public string HotKey = "Ctrl+Alt+M";
        public string RestoreHotKey = "Ctrl+Alt+R";
        public bool ToggleOnRepeat = false;
        public bool MinimizeOthers = true;
        public bool HideInsteadOfMinimize = false;
        public bool RestoreClamp = true;
        public bool PauseMediaOnSwitch = true;
        public bool ResumeMediaOnRestore = true;
        public bool ShowTips = true;
        public List<string> NeverMinimize = new List<string>();
        public List<Target> Targets = new List<Target>();
        public string Path = "";
        public string LoadError = "";
    }

    internal class PlanItem
    {
        public WindowInfo W;
        public Monitor Mon;
        public bool Span;
    }

    internal class SavedPlacement
    {
        public IntPtr H;
        public RECT Rect;
        public bool Maximized;
        public bool Minimized;
    }

    // ───────────────────────────── 配置读写 ─────────────────────────────

    internal static class ConfigIO
    {
        public static string DefaultJson()
        {
            return @"{
  ""_说明"": ""改完保存后，在托盘图标右键菜单里点「重新加载配置」。process 填进程名（任务管理器里的名字，不带 .exe，如 winword / chrome / Code / msedge）。exe 可选：程序没开时自动启动它。title 可选：正则，用来在同一个程序里挑窗口。span=true 表示让这个窗口横跨所有屏幕（一个窗口铺满两块屏）。"",
  ""hotkey"": ""Ctrl+Alt+M"",
  ""restoreHotkey"": ""Ctrl+Alt+R"",
  ""toggleOnRepeat"": false,
  ""minimizeOthers"": true,
  ""hideInsteadOfMinimize"": false,
  ""restoreClamp"": true,
  ""pauseMediaOnSwitch"": true,
  ""resumeMediaOnRestore"": true,
  ""showTips"": true,
  ""neverMinimize"": [],
  ""targets"": []
}";
        }

        public static string ResolvePath()
        {
            string env = Environment.GetEnvironmentVariable("BK_CONFIG");
            if (!string.IsNullOrEmpty(env)) return env;

            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            string p = System.IO.Path.Combine(dir, "ScreenVeil.json");
            if (File.Exists(p)) return p;

            try
            {
                string probe = System.IO.Path.Combine(dir, ".write_test");
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return p;
            }
            catch { }

            string alt = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenVeil");
            try { Directory.CreateDirectory(alt); } catch { }
            return System.IO.Path.Combine(alt, "ScreenVeil.json");
        }

        public static Config Load(string path)
        {
            var cfg = new Config();
            cfg.Path = path;

            if (!File.Exists(path))
            {
                try
                {
                    File.WriteAllText(path, DefaultJson(), new UTF8Encoding(true));
                }
                catch (Exception ex)
                {
                    cfg.LoadError = "写不了配置文件：" + ex.Message;
                }
                ApplyDefaults(cfg);
                return cfg;
            }

            string text;
            try
            {
                text = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                cfg.LoadError = "读不了配置文件：" + ex.Message;
                ApplyDefaults(cfg);
                return cfg;
            }

            Dictionary<string, object> d = null;
            try
            {
                var ser = new JavaScriptSerializer();
                ser.MaxJsonLength = 8 * 1024 * 1024;
                d = ser.DeserializeObject(text) as Dictionary<string, object>;
            }
            catch (Exception ex)
            {
                cfg.LoadError = "配置 JSON 语法有错：" + ex.Message;
                ApplyDefaults(cfg);
                return cfg;
            }

            if (d == null)
            {
                cfg.LoadError = "配置文件的顶层不是一个 { } 对象";
                ApplyDefaults(cfg);
                return cfg;
            }

            cfg.HotKey = GetStr(d, "hotkey", cfg.HotKey);
            cfg.RestoreHotKey = GetStr(d, "restoreHotkey", cfg.RestoreHotKey);
            cfg.ToggleOnRepeat = GetBool(d, "toggleOnRepeat", cfg.ToggleOnRepeat);
            cfg.MinimizeOthers = GetBool(d, "minimizeOthers", cfg.MinimizeOthers);
            cfg.HideInsteadOfMinimize = GetBool(d, "hideInsteadOfMinimize", cfg.HideInsteadOfMinimize);
            cfg.RestoreClamp = GetBool(d, "restoreClamp", cfg.RestoreClamp);
            cfg.PauseMediaOnSwitch = GetBool(d, "pauseMediaOnSwitch", cfg.PauseMediaOnSwitch);
            cfg.ResumeMediaOnRestore = GetBool(d, "resumeMediaOnRestore", cfg.ResumeMediaOnRestore);
            cfg.ShowTips = GetBool(d, "showTips", cfg.ShowTips);

            var never = GetArr(d, "neverMinimize");
            if (never != null)
            {
                cfg.NeverMinimize.Clear();
                foreach (var o in never)
                {
                    string s = Convert.ToString(o);
                    if (!string.IsNullOrEmpty(s)) cfg.NeverMinimize.Add(s);
                }
            }

            var targets = GetArr(d, "targets");
            if (targets != null)
            {
                foreach (var o in targets)
                {
                    var td = o as Dictionary<string, object>;
                    if (td == null) continue;
                    var t = new Target();
                    t.Monitor = GetInt(td, "monitor", cfg.Targets.Count + 1);
                    t.Process = GetStr(td, "process", "");
                    t.Title = GetStr(td, "title", "");
                    t.Exe = GetStr(td, "exe", "");
                    t.Args = GetStr(td, "args", "");
                    t.Span = GetBool(td, "span", false);
                    if (string.IsNullOrEmpty(t.Process) && string.IsNullOrEmpty(t.Exe) && string.IsNullOrEmpty(t.Title))
                        continue;
                    if (string.IsNullOrEmpty(t.Process) && !string.IsNullOrEmpty(t.Exe))
                        t.Process = System.IO.Path.GetFileNameWithoutExtension(t.Exe);
                    cfg.Targets.Add(t);
                }
            }

            if (cfg.Targets.Count == 0)
            {
                // 还没选过应用，不算错误：打开选择窗口让用户挑
                cfg.LoadError = "";
            }

            return cfg;
        }

        static void ApplyDefaults(Config cfg)
        {
            // 不预置任何应用：让用户在自己的机器上从下拉框里选
            cfg.Targets.Clear();
        }

        public static void Save(Config cfg)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"_说明\": " + J("改完保存后，在托盘图标右键菜单里点「重新加载配置」。process 填进程名（任务管理器里的名字，不带 .exe）。exe 可选：程序没开时自动启动它。title 可选：正则，用来在同一个程序里挑窗口。span=true 表示让这个窗口横跨所有屏幕。hotkey 可以写组合键，也可以写鼠标侧键 Mouse4 / Mouse5。") + ",");
            sb.AppendLine("  \"hotkey\": " + J(cfg.HotKey) + ",");
            sb.AppendLine("  \"restoreHotkey\": " + J(cfg.RestoreHotKey) + ",");
            sb.AppendLine("  \"toggleOnRepeat\": " + Bool(cfg.ToggleOnRepeat) + ",");
            sb.AppendLine("  \"minimizeOthers\": " + Bool(cfg.MinimizeOthers) + ",");
            sb.AppendLine("  \"hideInsteadOfMinimize\": " + Bool(cfg.HideInsteadOfMinimize) + ",");
            sb.AppendLine("  \"restoreClamp\": " + Bool(cfg.RestoreClamp) + ",");
            sb.AppendLine("  \"pauseMediaOnSwitch\": " + Bool(cfg.PauseMediaOnSwitch) + ",");
            sb.AppendLine("  \"resumeMediaOnRestore\": " + Bool(cfg.ResumeMediaOnRestore) + ",");
            sb.AppendLine("  \"showTips\": " + Bool(cfg.ShowTips) + ",");
            sb.AppendLine();

            sb.AppendLine("  \"neverMinimize\": [");
            for (int i = 0; i < cfg.NeverMinimize.Count; i++)
                sb.AppendLine("    " + J(cfg.NeverMinimize[i]) + (i < cfg.NeverMinimize.Count - 1 ? "," : ""));
            sb.AppendLine("  ],");
            sb.AppendLine();

            sb.AppendLine("  \"targets\": [");
            for (int i = 0; i < cfg.Targets.Count; i++)
            {
                Target t = cfg.Targets[i];
                var parts = new List<string>();
                parts.Add("\"monitor\": " + t.Monitor);
                if (!string.IsNullOrEmpty(t.Process)) parts.Add("\"process\": " + J(t.Process));
                if (!string.IsNullOrEmpty(t.Title)) parts.Add("\"title\": " + J(t.Title));
                if (!string.IsNullOrEmpty(t.Exe)) parts.Add("\"exe\": " + J(t.Exe));
                if (!string.IsNullOrEmpty(t.Args)) parts.Add("\"args\": " + J(t.Args));
                if (t.Span) parts.Add("\"span\": true");
                sb.AppendLine("    { " + string.Join(", ", parts.ToArray()) + " }" + (i < cfg.Targets.Count - 1 ? "," : ""));
            }
            sb.AppendLine("  ]");
            sb.AppendLine("}");

            string text = sb.ToString();

            // 写盘前先自检：宁可报错，也不能把用户的配置写成坏的
            try
            {
                var check = new JavaScriptSerializer();
                check.DeserializeObject(text);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("生成的配置不合法（已拒绝写入）：" + ex.Message);
            }

            try
            {
                if (File.Exists(cfg.Path)) File.Copy(cfg.Path, cfg.Path + ".bak", true);
            }
            catch { }

            File.WriteAllText(cfg.Path, text, new UTF8Encoding(false));
        }

        static string Bool(bool b) { return b ? "true" : "false"; }

        static string J(string s)
        {
            if (s == null) s = "";
            var sb = new StringBuilder("\"");
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(ch); break;
                }
            }
            sb.Append("\"");
            return sb.ToString();
        }

        static string GetStr(Dictionary<string, object> d, string key, string def)        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null && !(v is Dictionary<string, object>) && !(v is object[]))
            {
                string s = Convert.ToString(v);
                return s == null ? def : s;
            }
            return def;
        }

        static bool GetBool(Dictionary<string, object> d, string key, bool def)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null)
            {
                if (v is bool) return (bool)v;
                bool b;
                if (bool.TryParse(Convert.ToString(v), out b)) return b;
            }
            return def;
        }

        static int GetInt(Dictionary<string, object> d, string key, int def)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null)
            {
                try { return Convert.ToInt32(v); } catch { }
            }
            return def;
        }

        static object[] GetArr(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v)) return v as object[];
            return null;
        }
    }

    // ───────────────────────────── 热键解析 ─────────────────────────────

    internal class HotkeySpec
    {
        public uint Mods;
        public uint Key;
        public bool IsMouse;
        public int MouseButton;     // 1 = 侧键（后退）, 2 = 侧键（前进）
        public string Error = "";
    }

    internal static class Hotkey
    {
        public static HotkeySpec Parse(string text)
        {
            var spec = new HotkeySpec();
            if (string.IsNullOrEmpty(text))
            {
                spec.Error = "热键为空";
                return spec;
            }

            // 鼠标侧键：Mouse4 / Mouse5（也认 XButton1/2 和 M4/M5）
            string whole = text.Trim().ToLowerInvariant();
            if (whole == "mouse4" || whole == "xbutton1" || whole == "m4")
            {
                spec.IsMouse = true;
                spec.MouseButton = 1;
                return spec;
            }
            if (whole == "mouse5" || whole == "xbutton2" || whole == "m5")
            {
                spec.IsMouse = true;
                spec.MouseButton = 2;
                return spec;
            }

            string[] parts = text.Split('+');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0) continue;

                string lo = p.ToLowerInvariant();
                bool isModifier = true;
                if (lo == "ctrl" || lo == "control" || lo == "ctl") spec.Mods |= Nat.MOD_CONTROL;
                else if (lo == "alt") spec.Mods |= Nat.MOD_ALT;
                else if (lo == "shift") spec.Mods |= Nat.MOD_SHIFT;
                else if (lo == "win" || lo == "windows" || lo == "cmd" || lo == "super") spec.Mods |= Nat.MOD_WIN;
                else isModifier = false;

                if (!isModifier)
                {
                    if (i != parts.Length - 1)
                    {
                        spec.Error = "按键必须写在最后：" + text;
                        return spec;
                    }
                    uint vk;
                    if (!TryParseKey(p, out vk))
                    {
                        spec.Error = "认不出按键 '" + p + "'";
                        return spec;
                    }
                    spec.Key = vk;
                }
            }

            if (spec.Key == 0) spec.Error = "缺少主按键，例如 Ctrl+Alt+M";
            return spec;
        }

        static bool TryParseKey(string p, out uint vk)
        {
            vk = 0;
            string s = p.Trim();
            if (s.Length == 0) return false;

            if (s.Length == 1)
            {
                char c = char.ToUpperInvariant(s[0]);
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                {
                    vk = c;
                    return true;
                }
                switch (s[0])
                {
                    case '`': vk = 0xC0; return true;
                    case '-': vk = 0xBD; return true;
                    case '=': vk = 0xBB; return true;
                    case '[': vk = 0xDB; return true;
                    case ']': vk = 0xDD; return true;
                    case '\\': vk = 0xDC; return true;
                    case ';': vk = 0xBA; return true;
                    case '\'': vk = 0xDE; return true;
                    case ',': vk = 0xBC; return true;
                    case '.': vk = 0xBE; return true;
                    case '/': vk = 0xBF; return true;
                }
            }

            string up = s.ToUpperInvariant();
            if (up.StartsWith("F") && up.Length <= 3)
            {
                int n;
                if (int.TryParse(up.Substring(1), out n) && n >= 1 && n <= 24)
                {
                    vk = (uint)(0x70 + (n - 1));
                    return true;
                }
            }

            switch (up)
            {
                case "SPACE": vk = 0x20; return true;
                case "TAB": vk = 0x09; return true;
                case "ENTER": case "RETURN": vk = 0x0D; return true;
                case "ESC": case "ESCAPE": vk = 0x1B; return true;
                case "BACK": case "BACKSPACE": vk = 0x08; return true;
                case "DELETE": case "DEL": vk = 0x2E; return true;
                case "INSERT": case "INS": vk = 0x2D; return true;
                case "HOME": vk = 0x24; return true;
                case "END": vk = 0x23; return true;
                case "PAGEUP": case "PGUP": vk = 0x21; return true;
                case "PAGEDOWN": case "PGDN": vk = 0x22; return true;
                case "UP": vk = 0x26; return true;
                case "DOWN": vk = 0x28; return true;
                case "LEFT": vk = 0x25; return true;
                case "RIGHT": vk = 0x27; return true;
                case "PAUSE": vk = 0x13; return true;
                case "CAPSLOCK": vk = 0x14; return true;
                case "NUMLOCK": vk = 0x90; return true;
                case "SCROLLLOCK": vk = 0x91; return true;
                case "PRINTSCREEN": vk = 0x2C; return true;
            }
            return false;
        }
    }

    // ───────────────────────────── 窗口工具 ─────────────────────────────

    internal static class Win
    {
        public static List<Monitor> GetMonitors()
        {
            var list = new List<Monitor>();
            Nat.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                delegate(IntPtr hMon, IntPtr hdc, ref RECT r, IntPtr data)
                {
                    var mi = new MONITORINFO();
                    mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                    if (Nat.GetMonitorInfo(hMon, ref mi))
                    {
                        var m = new Monitor();
                        m.Handle = hMon;
                        m.Bounds = mi.rcMonitor;
                        m.Work = mi.rcWork;
                        m.Primary = (mi.dwFlags & 1) != 0;
                        list.Add(m);
                    }
                    return true;
                }, IntPtr.Zero);

            list.Sort(delegate(Monitor a, Monitor b)
            {
                if (a.Bounds.Left != b.Bounds.Left) return a.Bounds.Left.CompareTo(b.Bounds.Left);
                return a.Bounds.Top.CompareTo(b.Bounds.Top);
            });
            for (int i = 0; i < list.Count; i++) list[i].Index = i + 1;
            return list;
        }

        static Dictionary<int, string> ProcessNameMap()
        {
            var map = new Dictionary<int, string>();
            try
            {
                Process[] ps = Process.GetProcesses();
                foreach (Process p in ps)
                {
                    try { map[p.Id] = p.ProcessName; }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch { }
            return map;
        }

        // 备选方案：直接问内核这个 pid 的可执行文件路径（沙箱/权限受限时 GetProcesses 可能拿不到名字）
        static readonly Dictionary<int, string> nameCache = new Dictionary<int, string>();
        static readonly Dictionary<int, DateTime> nameCacheAt = new Dictionary<int, DateTime>();
        static readonly Dictionary<int, string> pathCache = new Dictionary<int, string>();

        public static string ResolveImagePath(int pid)
        {
            lock (pathCache)
            {
                string cached;
                if (pathCache.TryGetValue(pid, out cached)) return cached;
            }

            string path = "";
            IntPtr hp = Nat.OpenProcess(Nat.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hp != IntPtr.Zero)
            {
                try
                {
                    var sb = new StringBuilder(1024);
                    int size = sb.Capacity;
                    if (Nat.QueryFullProcessImageName(hp, 0, sb, ref size))
                        path = sb.ToString(0, size);
                }
                catch { }
                finally { Nat.CloseHandle(hp); }
            }

            if (path.Length == 0)
            {
                try
                {
                    using (Process p = Process.GetProcessById(pid)) path = p.MainModule.FileName;
                }
                catch { }
            }

            lock (pathCache) { pathCache[pid] = path; }
            return path;
        }

        public static string ResolveName(int pid)
        {
            lock (nameCache)
            {
                string cached;
                DateTime at;
                if (nameCache.TryGetValue(pid, out cached) && nameCacheAt.TryGetValue(pid, out at) &&
                    (DateTime.UtcNow - at).TotalSeconds < 30)
                    return cached;
            }

            string name = "";
            string full = ResolveImagePath(pid);
            if (full.Length > 0) name = System.IO.Path.GetFileNameWithoutExtension(full);

            if (name.Length == 0)
            {
                try
                {
                    using (Process p = Process.GetProcessById(pid)) name = p.ProcessName;
                }
                catch { }
            }

            lock (nameCache)
            {
                nameCache[pid] = name;
                nameCacheAt[pid] = DateTime.UtcNow;
            }
            return name;
        }

        // 空操作 SetWindowPos：只用来判断这个窗口我们能不能动（UIPI 会拒绝更高权限进程的窗口）
        public static bool CanControl(IntPtr h)
        {
            return Nat.SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0,
                Nat.SWP_NOMOVE | Nat.SWP_NOSIZE | Nat.SWP_NOZORDER | Nat.SWP_NOACTIVATE);
        }

        public static List<WindowInfo> GetWindows()
        {
            var result = new List<WindowInfo>();
            var names = ProcessNameMap();
            int self = Process.GetCurrentProcess().Id;

            Nat.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                try
                {
                    if (!Nat.IsWindowVisible(h)) return true;
                    if (Nat.GetWindow(h, Nat.GW_OWNER) != IntPtr.Zero) return true;

                    int len = Nat.GetWindowTextLength(h);
                    if (len <= 0) return true;
                    var sb = new StringBuilder(len + 2);
                    Nat.GetWindowText(h, sb, sb.Capacity);
                    string title = sb.ToString();
                    if (title.Length == 0) return true;

                    int ex = Nat.GetWindowLong(h, Nat.GWL_EXSTYLE);
                    if ((ex & Nat.WS_EX_TOOLWINDOW) != 0) return true;

                    var cls = new StringBuilder(256);
                    Nat.GetClassName(h, cls, cls.Capacity);
                    string className = cls.ToString();
                    if (className == "Shell_TrayWnd" || className == "Progman" ||
                        className == "WorkerW" || className == "Shell_SecondaryTrayWnd" ||
                        className == "Windows.UI.Core.CoreWindow" || className == "ApplicationFrameWindow")
                    {
                        // ApplicationFrameWindow 是 UWP 的容器，真正的标题窗口通常是它的子窗口，这里仍保留
                        if (className != "ApplicationFrameWindow") return true;
                    }

                    int cloaked;
                    if (Nat.DwmGetWindowAttribute(h, Nat.DWMWA_CLOAKED, out cloaked, 4) == 0 && cloaked != 0)
                        return true;

                    uint pid;
                    Nat.GetWindowThreadProcessId(h, out pid);
                    string pname;
                    if (!names.TryGetValue((int)pid, out pname) || string.IsNullOrEmpty(pname))
                        pname = ResolveName((int)pid);

                    RECT rect;
                    if (!Nat.GetWindowRect(h, out rect)) return true;

                    var w = new WindowInfo();
                    w.H = h;
                    w.Pid = (int)pid;
                    w.Process = pname;
                    w.Title = title;
                    w.ClassName = className;
                    w.Rect = rect;
                    w.Iconic = Nat.IsIconic(h);
                    if (w.Pid == self) return true;
                    result.Add(w);
                }
                catch { }
                return true;
            }, IntPtr.Zero);

            return result;
        }

        public static bool Overlaps(RECT a, RECT b)
        {
            return a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom;
        }

        public static RECT UnionWork(List<Monitor> mons)
        {
            RECT r = mons[0].Work;
            for (int i = 1; i < mons.Count; i++)
            {
                if (mons[i].Work.Left < r.Left) r.Left = mons[i].Work.Left;
                if (mons[i].Work.Top < r.Top) r.Top = mons[i].Work.Top;
                if (mons[i].Work.Right > r.Right) r.Right = mons[i].Work.Right;
                if (mons[i].Work.Bottom > r.Bottom) r.Bottom = mons[i].Work.Bottom;
            }
            return r;
        }

        public static void Raise(IntPtr h)
        {
            if (h == IntPtr.Zero || !Nat.IsWindow(h)) return;
            Nat.SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0,
                Nat.SWP_NOMOVE | Nat.SWP_NOSIZE | Nat.SWP_NOACTIVATE | Nat.SWP_SHOWWINDOW);
        }

        public static bool PlaceOn(IntPtr h, RECT r, bool maximize, out string err)
        {
            err = "";
            Nat.ShowWindow(h, Nat.SW_RESTORE);
            bool ok = Nat.SetWindowPos(h, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height,
                Nat.SWP_NOZORDER | Nat.SWP_NOACTIVATE | Nat.SWP_SHOWWINDOW);
            int last = Marshal.GetLastWin32Error();
            if (!ok) err = "SetWindowPos 失败 err=" + last + " (" + new System.ComponentModel.Win32Exception(last).Message + ")";
            if (maximize) Nat.ShowWindow(h, Nat.SW_MAXIMIZE);
            else Nat.ShowWindow(h, Nat.SW_SHOW);
            return ok;
        }

        public static void ForceForeground(IntPtr h)
        {
            if (h == IntPtr.Zero || !Nat.IsWindow(h)) return;
            uint pid;
            uint fgThread = Nat.GetWindowThreadProcessId(Nat.GetForegroundWindow(), out pid);
            uint myThread = Nat.GetCurrentThreadId();
            bool attached = false;
            if (fgThread != 0 && fgThread != myThread)
                attached = Nat.AttachThreadInput(myThread, fgThread, true);
            try
            {
                Nat.BringWindowToTop(h);
                Nat.SetForegroundWindow(h);
            }
            finally
            {
                if (attached) Nat.AttachThreadInput(myThread, fgThread, false);
            }
        }

        public static SavedPlacement SaveState(IntPtr h)
        {
            var s = new SavedPlacement();
            s.H = h;
            Nat.GetWindowRect(h, out s.Rect);
            s.Maximized = Nat.IsZoomed(h);
            s.Minimized = Nat.IsIconic(h);
            return s;
        }

        public static void RestoreState(SavedPlacement s)
        {
            RestoreState(s, null, false);
        }

        public static void RestoreState(SavedPlacement s, List<Monitor> mons, bool clamp)
        {
            if (!Nat.IsWindow(s.H)) return;
            Nat.ShowWindow(s.H, Nat.SW_RESTORE);
            if (!s.Minimized && s.Rect.Width > 0 && s.Rect.Height > 0)
            {
                RECT target = s.Rect;
                // 保险：把窗口收进它自己那块屏，别让它一半压在隔壁屏上
                if (clamp && mons != null && mons.Count > 0 && !s.Maximized)
                    target = ClampToMonitor(target, mons);

                Nat.SetWindowPos(s.H, IntPtr.Zero, target.Left, target.Top, target.Width, target.Height,
                    Nat.SWP_NOZORDER | Nat.SWP_NOACTIVATE | Nat.SWP_SHOWWINDOW);
            }
            if (s.Maximized) Nat.ShowWindow(s.H, Nat.SW_MAXIMIZE);
            else if (s.Minimized) Nat.ShowWindow(s.H, Nat.SW_MINIMIZE);
        }

        static RECT ClampToMonitor(RECT r, List<Monitor> mons)
        {
            Monitor best = null;
            int bestArea = -1;
            foreach (Monitor m in mons)
            {
                int l = Math.Max(r.Left, m.Work.Left);
                int t = Math.Max(r.Top, m.Work.Top);
                int rr = Math.Min(r.Right, m.Work.Right);
                int bb = Math.Min(r.Bottom, m.Work.Bottom);
                int area = Math.Max(0, rr - l) * Math.Max(0, bb - t);
                if (area > bestArea) { bestArea = area; best = m; }
            }
            if (best == null || bestArea <= 0) return r;

            RECT w = best.Work;
            if (r.Left >= w.Left && r.Top >= w.Top && r.Right <= w.Right && r.Bottom <= w.Bottom)
                return r;   // 本来就在里面

            int cw = Math.Min(r.Width, w.Width);
            int ch = Math.Min(r.Height, w.Height);
            int cx = Math.Max(w.Left, Math.Min(r.Left, w.Right - cw));
            int cy = Math.Max(w.Top, Math.Min(r.Top, w.Bottom - ch));

            RECT o;
            o.Left = cx;
            o.Top = cy;
            o.Right = cx + cw;
            o.Bottom = cy + ch;
            return o;
        }
    }

    // ───────────────────────────── 核心引擎 ─────────────────────────────

    internal class Engine
    {
        public Config Cfg;
        public List<string> Log = new List<string>();
        readonly object gate = new object();
        bool applied;
        List<SavedPlacement> saved = new List<SavedPlacement>();
        List<IntPtr> touched = new List<IntPtr>();
        List<string> pausedMedia = new List<string>();
        IntPtr prevForeground;

        static string Trim(string s, int n)
        {
            if (s == null) return "";
            return s.Length <= n ? s : s.Substring(0, n) + "…";
        }

        public bool Applied { get { lock (gate) return applied; } }

        public string Apply()
        {
            lock (gate)
            {
                // 已经在切换状态时再按一次「切换」：只重新摆位置，绝不覆盖掉原始位置，
                // 否则还原就回不去了
                bool reapply = applied;
                applied = false;
                Log.Clear();
                if (!reapply)
                {
                    saved.Clear();
                    touched.Clear();
                    touchedRestoreList.Clear();
                }

                if (Cfg == null) return "还没有配置";
                if (Cfg.LoadError.Length > 0) return Cfg.LoadError;

                List<Monitor> mons = Win.GetMonitors();
                if (mons.Count == 0) return "没检测到显示器";
                if (Cfg.Targets.Count == 0)
                    return "还没选应用：双击托盘图标打开选择窗口，选好再保存";

                List<WindowInfo> all = Win.GetWindows();
                var used = new HashSet<IntPtr>();
                var plan = new List<PlanItem>();
                var missed = new List<string>();

                foreach (Target t in Cfg.Targets)
                {
                    // 这台电脑没有这块屏（比如别人只有一块屏，但配置是从双屏机器拿来的）→ 跳过
                    if (t.Monitor < 1 || t.Monitor > mons.Count)
                    {
                        missed.Add("第 " + t.Monitor + " 块屏（这台电脑只有 " + mons.Count + " 块）");
                        continue;
                    }
                    Monitor mon = mons[t.Monitor - 1];
                    WindowInfo w = Pick(all, t, mon, used);

                    if (w == null && !string.IsNullOrEmpty(t.Exe))
                    {
                        Launch(t);
                        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
                        while (w == null && DateTime.UtcNow < deadline)
                        {
                            Thread.Sleep(350);
                            all = Win.GetWindows();
                            w = Pick(all, t, mon, used);
                        }
                    }

                    if (w == null)
                    {
                        missed.Add(t.Describe());
                        continue;
                    }

                    used.Add(w.H);
                    var item = new PlanItem();
                    item.W = w;
                    item.Mon = mon;
                    item.Span = t.Span;
                    plan.Add(item);
                }

                if (plan.Count == 0)
                {
                    Log.Add("没有可用的目标窗口");
                    foreach (string m in missed) Log.Add("  未命中：" + m);
                    return "没找到目标窗口，检查配置里的 process / exe：" + string.Join("；", missed.ToArray());
                }

                Log.Add("计划 " + plan.Count + " 个窗口：");
                foreach (PlanItem p in plan)
                    Log.Add(string.Format("  hwnd=0x{0:X} pid={1} {2} '{3}' → 屏{4}{5}",
                        p.W.H.ToInt64(), p.W.Pid, p.W.Process, Trim(p.W.Title, 30), p.Mon.Index, p.Span ? " (跨屏)" : ""));

                // 同一个程序配了两块屏但只有一个窗口 → 干脆让它横跨两块屏
                if (plan.Count == 1 && Cfg.Targets.Count >= 2)
                {
                    bool sameProc = true;
                    string p0 = Cfg.Targets[0].Process;
                    foreach (Target t in Cfg.Targets)
                        if (!string.Equals(t.Process, p0, StringComparison.OrdinalIgnoreCase)) sameProc = false;
                    if (sameProc) plan[0].Span = true;
                }

                if (saved.Count == 0)
                {
                    prevForeground = Nat.GetForegroundWindow();
                    foreach (PlanItem p in plan)
                    {
                        SavedPlacement st = Win.SaveState(p.W.H);
                        Log.Add(string.Format("  保存 hwnd=0x{0:X} {1}{2}{3}",
                            st.H.ToInt64(),
                            st.Maximized ? "最大化 " : "",
                            st.Minimized ? "最小化 " : "",
                            st.Rect.ToString()));
                        saved.Add(st);
                    }
                }

                bool spanDone = false;
                RECT spanRect = Win.UnionWork(mons);
                bool wantSpan = false;
                foreach (PlanItem p in plan) if (p.Span) wantSpan = true;

                int placed = 0, failed = 0;
                foreach (PlanItem p in plan)
                {
                    bool span = p.Span || (wantSpan && !spanDone);
                    RECT target = span ? spanRect : p.Mon.Work;
                    string perr;
                    bool placeOk = Win.PlaceOn(p.W.H, target, !span, out perr);
                    if (placeOk) placed++; else failed++;
                    if (span) spanDone = true;
                    if (touched.IndexOf(p.W.H) < 0) touched.Add(p.W.H);

                    RECT after;
                    Nat.GetWindowRect(p.W.H, out after);
                    Log.Add(string.Format("  放置 hwnd=0x{0:X} {1} → {2}  实际 {3}  {4}",
                        p.W.H.ToInt64(), placeOk ? "OK  " : "失败", target.ToString(), after.ToString(),
                        placeOk ? "" : perr));
                    Thread.Sleep(60);
                }

                if (placed == 0)
                {
                    // 一个都没成功：把窗口还原回去，别把用户桌面搞空
                    foreach (SavedPlacement sp in saved) Win.RestoreState(sp);
                    saved.Clear();
                    touched.Clear();
                    return "窗口被系统拒绝（权限不足），一个都没切换成功。请直接双击 ScreenVeil.exe 运行；目标程序若是管理员权限，本程序也要以管理员身份运行。";
                }

                if (Cfg.MinimizeOthers)
                    MinimizeOthers(all, used);

                Win.ForceForeground(plan[0].W.H);
                applied = true;

                // 正在播放的网页视频 / 音乐：切过去时先暂停（还原时再放回来）
                if (Cfg.PauseMediaOnSwitch)
                {
                    List<string> ids = MediaCtl.PausePlaying();
                    foreach (string id in ids)
                        if (!pausedMedia.Contains(id)) pausedMedia.Add(id);
                    Log.Add("  媒体：暂停 " + ids.Count + " 个" +
                        (ids.Count > 0 ? "（" + string.Join(", ", ids.ToArray()) + "）" : "") +
                        (MediaCtl.LastError.Length > 0 ? "；" + MediaCtl.LastError : ""));
                }

                string ok = "已切换";
                if (missed.Count > 0) ok += "（没找到：" + string.Join("，", missed.ToArray()) + "）";
                return ok;
            }
        }

        public string Restore()
        {
            lock (gate)
            {
                if (!applied && saved.Count == 0) return "现在没切换过";
                Log.Clear();
                List<Monitor> mons = Win.GetMonitors();
                foreach (SavedPlacement sp in saved)
                {
                    if (!Nat.IsWindow(sp.H)) continue;
                    Win.RestoreState(sp, mons, Cfg != null && Cfg.RestoreClamp);
                    RECT after;
                    Nat.GetWindowRect(sp.H, out after);
                    Log.Add(string.Format("  还原 hwnd=0x{0:X} 目标 {1}  实际 {2}",
                        sp.H.ToInt64(), sp.Rect.ToString(), after.ToString()));
                }
                foreach (IntPtr h in touchedRestoreList)
                {
                    if (!Nat.IsWindow(h)) continue;
                    Nat.ShowWindow(h, Cfg.HideInsteadOfMinimize ? Nat.SW_SHOW : Nat.SW_RESTORE);
                }
                if (prevForeground != IntPtr.Zero && Nat.IsWindow(prevForeground))
                    Win.ForceForeground(prevForeground);

                if (Cfg != null && Cfg.ResumeMediaOnRestore && pausedMedia.Count > 0)
                {
                    MediaCtl.Resume(pausedMedia);
                    Log.Add("  已恢复媒体播放：" + string.Join(", ", pausedMedia.ToArray()));
                    pausedMedia.Clear();
                }

                saved.Clear();
                touched.Clear();
                touchedRestoreList.Clear();
                prevForeground = IntPtr.Zero;
                applied = false;
                return "已恢复";
            }
        }

        readonly List<IntPtr> touchedRestoreList = new List<IntPtr>();

        // 试跑：切换 → 等几秒 → 还原，把三个阶段各屏的窗口情况写出来，方便判断配置对不对
        public string SelfTest(int seconds)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== ScreenVeil 试跑  " + DateTime.Now.ToString("HH:mm:ss") + " ===");
            sb.AppendLine();
            sb.AppendLine("【切换前】");
            Snapshot(sb);

            string m1 = Apply();
            sb.AppendLine("Apply → " + m1);
            foreach (string l in Log) sb.AppendLine("   " + l);
            Thread.Sleep(Math.Max(1, seconds) * 1000);
            sb.AppendLine();
            sb.AppendLine("【切换后】");
            Snapshot(sb);

            string m2 = Restore();
            Thread.Sleep(900);
            sb.AppendLine("Restore → " + m2);
            foreach (string l in Log) sb.AppendLine("   " + l);
            sb.AppendLine();
            sb.AppendLine("【还原后】");
            Snapshot(sb);
            return sb.ToString();
        }

        static void Snapshot(StringBuilder sb)
        {
            List<Monitor> mons = Win.GetMonitors();
            List<WindowInfo> ws = Win.GetWindows();
            foreach (WindowInfo w in ws)
            {
                if (w.Iconic) continue;
                int monNo = 0;
                foreach (Monitor m in mons) if (Win.Overlaps(w.Rect, m.Bounds)) monNo = m.Index;
                string t = w.Title.Length > 36 ? w.Title.Substring(0, 36) + "…" : w.Title;
                string proc = w.Process.Length > 18 ? w.Process.Substring(0, 18) : w.Process;
                sb.AppendLine(string.Format("   屏{0} {1,-18} {2,-26} {3}",
                    monNo == 0 ? "?" : monNo.ToString(), proc, w.Rect.ToString(), t));
            }
        }

        static WindowInfo Pick(List<WindowInfo> all, Target t, Monitor mon, HashSet<IntPtr> used)
        {
            WindowInfo best = null;
            int bestScore = int.MinValue;
            IntPtr fg = Nat.GetForegroundWindow();

            foreach (WindowInfo w in all)
            {
                if (used.Contains(w.H)) continue;
                if (!Matches(w, t)) continue;

                int score = 0;
                if (Win.Overlaps(w.Rect, mon.Bounds)) score += 1000;
                if (Win.Overlaps(w.Rect, mon.Work)) score += 100;
                if (!w.Iconic) score += 200;
                if (w.H == fg) score += 10;
                long area = (long)w.Rect.Width * (long)w.Rect.Height;
                score += (int)Math.Min(area / 100000, 40);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = w;
                }
            }
            return best;
        }

        static bool Matches(WindowInfo w, Target t)
        {
            if (!string.IsNullOrEmpty(t.Process))
            {
                string want = t.Process.Trim();
                if (want.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    want = want.Substring(0, want.Length - 4);
                if (!string.Equals(want, w.Process, StringComparison.OrdinalIgnoreCase)) return false;
            }

            if (!string.IsNullOrEmpty(t.Title))
            {
                if (t.TitleRe == null)
                {
                    try { t.TitleRe = new Regex(t.Title, RegexOptions.IgnoreCase); }
                    catch { t.TitleRe = null; return w.Title.IndexOf(t.Title, StringComparison.OrdinalIgnoreCase) >= 0; }
                }
                if (!t.TitleRe.IsMatch(w.Title)) return false;
            }

            return true;
        }

        static void Launch(Target t)
        {
            try
            {
                var psi = new ProcessStartInfo();
                psi.FileName = t.Exe;
                if (!string.IsNullOrEmpty(t.Args)) psi.Arguments = t.Args;
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch { }
        }

        void MinimizeOthers(List<WindowInfo> all, HashSet<IntPtr> keep)
        {
            int self = Process.GetCurrentProcess().Id;
            foreach (WindowInfo w in all)
            {
                if (keep.Contains(w.H)) continue;
                if (w.Pid == self) continue;
                if (w.Iconic) continue;
                if (!IsOnAnyMonitor(w.Rect)) continue;
                if (IsNeverMinimize(w.Process)) continue;

                if (Cfg.HideInsteadOfMinimize)
                {
                    Nat.ShowWindow(w.H, Nat.SW_HIDE);
                    touchedRestoreList.Add(w.H);
                }
                else
                {
                    Nat.ShowWindow(w.H, Nat.SW_MINIMIZE);
                    touchedRestoreList.Add(w.H);
                }
            }
        }

        bool IsNeverMinimize(string proc)
        {
            if (string.IsNullOrEmpty(proc)) return false;
            foreach (string s in Cfg.NeverMinimize)
                if (string.Equals(s, proc, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static bool IsOnAnyMonitor(RECT r)
        {
            return r.Right > r.Left && r.Bottom > r.Top && r.Right > -10000 && r.Left < 10000;
        }
    }

    // ───────────────────────────── 媒体会话（暂停网页视频） ─────────────────────────────

    internal static class MediaCtl
    {
        public static string LastError = "";

        // 走 Windows 的全局媒体会话（GSMTC）：只暂停「此刻正在播放」的会话，
        // 不碰音量、也不做「窗口失焦就静音」那种偷懒逻辑。
        public static List<string> PausePlaying()
        {
            var paused = new List<string>();
            LastError = "";
#if WINRT
            try
            {
                var op = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                var t = System.WindowsRuntimeSystemExtensions.AsTask(op);
                if (!t.Wait(3000)) { LastError = "请求媒体会话超时"; return paused; }

                var sessions = t.Result.GetSessions();
                foreach (var s in sessions)
                {
                    try
                    {
                        if (s.GetPlaybackInfo().PlaybackStatus !=
                            Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                            continue;

                        var pop = s.TryPauseAsync();
                        var pt = System.WindowsRuntimeSystemExtensions.AsTask(pop);
                        if (pt.Wait(1500) && pt.Result)
                            paused.Add(s.SourceAppUserModelId);
                        else
                            LastError = "暂停 " + s.SourceAppUserModelId + " 被拒绝";
                    }
                    catch (Exception ex) { LastError = ex.GetType().Name + ":" + ex.Message; }
                }
                if (sessions.Count == 0) LastError = "系统里没有媒体会话";
            }
            catch (Exception ex) { LastError = ex.GetType().Name + ":" + ex.Message; }
#else
            LastError = "编译时没有启用 WINRT";
#endif
            return paused;
        }

        public static void Resume(List<string> ids)
        {
#if WINRT
            if (ids == null || ids.Count == 0) return;
            try
            {
                var op = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                var t = System.WindowsRuntimeSystemExtensions.AsTask(op);
                if (!t.Wait(3000)) return;

                foreach (var s in t.Result.GetSessions())
                {
                    try
                    {
                        if (!ids.Contains(s.SourceAppUserModelId)) continue;
                        if (s.GetPlaybackInfo().PlaybackStatus ==
                            Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                            continue;
                        s.TryPlayAsync();
                    }
                    catch { }
                }
            }
            catch { }
#endif
        }
    }

    // ───────────────────────────── 应用选择窗口 ─────────────────────────────

    internal class AppChoice
    {
        public string Process = "";
        public string Exe = "";
        public string Display = "";
        public Icon Icon;

        public override string ToString() { return Display; }
    }

    internal class PickerForm : Form
    {
        ComboBox cbMain, cbSub;
        CheckBox chkMin, chkAuto;
        TextBox boxSwitch, boxRestore;
        Label hintLabel;
        Config cfg;
        string hotkeyError;
        bool multiScreen;
        Action<Config, int> onResult;   // 0=只保存  1=保存并切换  2=保存并试跑

        public PickerForm(Config cfg, string hotkeyError, Action<Config, int> onResult)
        {
            this.cfg = cfg;
            this.hotkeyError = hotkeyError;
            this.onResult = onResult;

            Text = "ScreenVeil — 选择每块屏幕显示什么";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(640, 300);
            Font = new Font("Microsoft YaHei UI", 9.5f);
            BackColor = Color.FromArgb(250, 250, 252);
            try { Icon = AppIcon.Get(); } catch { }

            var title = new Label();
            title.Text = "每块屏幕显示什么";
            title.Font = new Font("Microsoft YaHei UI", 12.5f, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(18, 14);
            Controls.Add(title);

            var sub = new Label();
            List<Monitor> mons = Win.GetMonitors();
            multiScreen = mons.Count > 1;
            string m1 = mons.Count > 0 ? mons[0].Bounds.Width + "×" + mons[0].Bounds.Height : "";
            string m2 = mons.Count > 1 ? mons[1].Bounds.Width + "×" + mons[1].Bounds.Height : "";
            sub.Text = multiScreen
                ? "左边 " + m1 + "　·　右边 " + m2 + "　·　选好后点「保存」"
                : "只有一块屏幕（" + m1 + "）：选一个应用，按热键它就铺满整块屏幕";
            sub.ForeColor = Color.FromArgb(110, 110, 120);
            sub.AutoSize = true;
            sub.Location = new Point(20, 46);
            Controls.Add(sub);

            List<AppChoice> choices = BuildChoices(cfg);

            var l1 = new Label();
            l1.Text = "主屏（左）";
            l1.AutoSize = true;
            l1.Location = new Point(20, 86);
            Controls.Add(l1);
            cbMain = MakeCombo(new Point(110, 82), choices);
            Controls.Add(cbMain);

            var l2 = new Label();
            l2.Text = "副屏（右）";
            l2.AutoSize = true;
            l2.Location = new Point(20, 128);
            Controls.Add(l2);
            cbSub = MakeCombo(new Point(110, 124), choices);
            Controls.Add(cbSub);

            chkMin = new CheckBox();
            chkMin.Text = "切换时把其它窗口都最小化";
            chkMin.AutoSize = true;
            chkMin.Location = new Point(112, 162);
            chkMin.Checked = cfg.MinimizeOthers;
            Controls.Add(chkMin);

            chkAuto = new CheckBox();
            chkAuto.Text = "开机自启";
            chkAuto.AutoSize = true;
            chkAuto.Location = new Point(360, 162);
            chkAuto.Checked = Startup.Enabled;
            Controls.Add(chkAuto);

            var lblSwitch = new Label();
            lblSwitch.Text = "切换键";
            lblSwitch.AutoSize = true;
            lblSwitch.Location = new Point(112, 197);
            Controls.Add(lblSwitch);

            boxSwitch = MakeKeyBox(new Point(168, 193), cfg.HotKey);
            Controls.Add(boxSwitch);

            var lblRestore = new Label();
            lblRestore.Text = "还原键";
            lblRestore.AutoSize = true;
            lblRestore.Location = new Point(332, 197);
            Controls.Add(lblRestore);

            boxRestore = MakeKeyBox(new Point(388, 193), cfg.RestoreHotKey);
            Controls.Add(boxRestore);

            hintLabel = new Label();
            hintLabel.Text = string.IsNullOrEmpty(hotkeyError) ? "点一下输入框，然后按组合键（鼠标侧键也行）" : hotkeyError;
            hintLabel.ForeColor = string.IsNullOrEmpty(hotkeyError) ? Color.FromArgb(130, 130, 140) : Color.FromArgb(200, 50, 50);
            hintLabel.AutoSize = true;
            hintLabel.Location = new Point(112, 224);
            Controls.Add(hintLabel);

            SelectProcess(cbMain, cfg.Targets.Count > 0 ? cfg.Targets[0].Process : "");
            SelectProcess(cbSub, cfg.Targets.Count > 1 ? cfg.Targets[1].Process : "");

            if (!multiScreen)
            {
                l2.Text = "副屏（无）";
                cbSub.Enabled = false;
                cbSub.SelectedIndex = 0;
            }

            var btnTest = new Button();
            btnTest.Text = "试跑 5 秒";
            btnTest.Location = new Point(20, 248);
            btnTest.Size = new Size(104, 34);
            btnTest.Click += delegate { SaveAndClose(2); };
            Controls.Add(btnTest);

            var stay = new Label();
            stay.Text = "关掉窗口不退出，仍在托盘 ^ 里待命";
            stay.ForeColor = Color.FromArgb(150, 150, 160);
            stay.Font = new Font("Microsoft YaHei UI", 8.5f);
            stay.AutoSize = true;
            stay.Location = new Point(136, 258);
            Controls.Add(stay);

            var btnSave = new Button();
            btnSave.Text = "保存";
            btnSave.Location = new Point(414, 248);
            btnSave.Size = new Size(96, 34);
            btnSave.Click += delegate { SaveAndClose(0); };
            Controls.Add(btnSave);

            var btnSaveGo = new Button();
            btnSaveGo.Text = "保存并切换";
            btnSaveGo.Location = new Point(518, 248);
            btnSaveGo.Size = new Size(104, 34);
            btnSaveGo.Click += delegate { SaveAndClose(1); };
            Controls.Add(btnSaveGo);

            AcceptButton = btnSaveGo;
        }

        TextBox MakeKeyBox(Point loc, string text)
        {
            var tb = new TextBox();
            tb.ReadOnly = true;
            tb.Text = text;
            tb.TextAlign = HorizontalAlignment.Center;
            tb.Location = loc;
            tb.Size = new Size(140, 26);
            tb.Cursor = Cursors.Hand;
            tb.ShortcutsEnabled = false;
            tb.BackColor = Color.White;
            tb.Enter += delegate { tb.BackColor = Color.FromArgb(255, 249, 196); };
            tb.Leave += delegate { tb.BackColor = Color.White; };
            tb.KeyDown += delegate(object s, KeyEventArgs e) { CaptureKeys(e); };
            return tb;
        }

        // 输入框获得焦点时，把按下的组合直接记下来
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Control c = ActiveControl;
            if (c != null && (c == boxSwitch || c == boxRestore))
            {
                Keys key = keyData & Keys.KeyCode;
                Keys mods = keyData & Keys.Modifiers;

                if (key == Keys.Escape) return true;
                if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu ||
                    key == Keys.LWin || key == Keys.RWin || key == Keys.None)
                    return true;

                string text = BuildHotkeyText(mods, key);
                if (text != null) ((TextBox)c).Text = text;
                else ShowKeyHint();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // 鼠标侧键：由全局钩子转进来（钩子跑在 UI 线程，所以这里可以碰控件）
        public bool TryRecordMouse(int button)
        {
            Control c = ActiveControl;
            if (c == null || (c != boxSwitch && c != boxRestore)) return false;
            ((TextBox)c).Text = button == 1 ? "Mouse4" : "Mouse5";
            if (hintLabel != null)
            {
                hintLabel.Text = "已记录鼠标" + (button == 1 ? "侧键（后退）" : "侧键（前进）") + "，这个键会被本程序接管";
                hintLabel.ForeColor = Color.FromArgb(60, 130, 60);
            }
            return true;
        }

        void ShowKeyHint()
        {
            if (hintLabel == null) return;
            hintLabel.Text = "单个字母/数字会抢走键盘输入，加个 Ctrl/Alt/Shift；F1~F12、Pause、PrintScreen 和鼠标侧键可以单用";
            hintLabel.ForeColor = Color.FromArgb(200, 120, 30);
        }

        void CaptureKeys(KeyEventArgs e)
        {
            Control c = ActiveControl;
            if (c == null || (c != boxSwitch && c != boxRestore)) return;
            e.SuppressKeyPress = true;
            e.Handled = true;

            string text = BuildHotkeyText(e.Modifiers, e.KeyCode);
            if (text != null) ((TextBox)c).Text = text;
        }

        static string BuildHotkeyText(Keys mods, Keys key)
        {
            string name = KeyName(key);
            if (name == null) return null;

            bool hasMod = (mods & (Keys.Control | Keys.Alt | Keys.Shift)) != 0;
            bool isFn = key >= Keys.F1 && key <= Keys.F24;
            bool isLone = key == Keys.Pause || key == Keys.Scroll || key == Keys.PrintScreen;
            if (!hasMod && !isFn && !isLone) return null;   // 单按字母会把整个键盘都吃掉，不允许

            var sb = new StringBuilder();
            if ((mods & Keys.Control) != 0) sb.Append("Ctrl+");
            if ((mods & Keys.Alt) != 0) sb.Append("Alt+");
            if ((mods & Keys.Shift) != 0) sb.Append("Shift+");
            sb.Append(name);
            return sb.ToString();
        }

        static string KeyName(Keys key)
        {
            if (key >= Keys.A && key <= Keys.Z) return key.ToString();
            if (key >= Keys.D0 && key <= Keys.D9) return ((char)('0' + (key - Keys.D0))).ToString();
            if (key >= Keys.F1 && key <= Keys.F24) return "F" + (key - Keys.F1 + 1);

            switch (key)
            {
                case Keys.Space: return "Space";
                case Keys.Tab: return "Tab";
                case Keys.Enter: return "Enter";
                case Keys.Back: return "Back";
                case Keys.Delete: return "Delete";
                case Keys.Insert: return "Insert";
                case Keys.Home: return "Home";
                case Keys.End: return "End";
                case Keys.PageUp: return "PageUp";
                case Keys.PageDown: return "PageDown";
                case Keys.Up: return "Up";
                case Keys.Down: return "Down";
                case Keys.Left: return "Left";
                case Keys.Right: return "Right";
                case Keys.Pause: return "Pause";
                case Keys.PrintScreen: return "PrintScreen";
                case Keys.Oemtilde: return "`";
                case Keys.OemMinus: return "-";
                case Keys.Oemplus: return "=";
                case Keys.OemOpenBrackets: return "[";
                case Keys.OemCloseBrackets: return "]";
                case Keys.OemPipe: return "\\";
                case Keys.OemSemicolon: return ";";
                case Keys.OemQuotes: return "'";
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemQuestion: return "/";
            }
            return null;
        }

        static ComboBox MakeCombo(Point loc, List<AppChoice> choices)
        {
            var cb = new ComboBox();
            cb.DropDownStyle = ComboBoxStyle.DropDownList;
            cb.DrawMode = DrawMode.OwnerDrawFixed;
            cb.ItemHeight = 24;
            cb.DropDownWidth = 520;
            cb.Location = loc;
            cb.Size = new Size(500, 28);
            cb.FlatStyle = FlatStyle.Standard;
            cb.Items.AddRange(choices.ToArray());
            cb.DrawItem += ComboDrawItem;
            return cb;
        }

        static void ComboDrawItem(object sender, DrawItemEventArgs e)
        {
            var cb = (ComboBox)sender;
            e.DrawBackground();
            if (e.Index < 0) return;

            var item = cb.Items[e.Index] as AppChoice;
            if (item == null) return;

            // 只有下拉列表展开时，选中行才是蓝底；收起状态下按普通文本框画
            bool hot = ((e.State & DrawItemState.Selected) != 0) && cb.DroppedDown;
            if (!hot)
            {
                using (var b = new SolidBrush(cb.BackColor))
                    e.Graphics.FillRectangle(b, e.Bounds);
            }

            Color fg = hot ? SystemColors.HighlightText : cb.ForeColor;

            if (item.Icon != null)
            {
                var ir = new Rectangle(e.Bounds.Left + 4, e.Bounds.Top + (e.Bounds.Height - 16) / 2, 16, 16);
                try { e.Graphics.DrawIcon(item.Icon, ir); } catch { }
            }

            var tr = new Rectangle(e.Bounds.Left + 26, e.Bounds.Top, e.Bounds.Width - 30, e.Bounds.Height);
            try
            {
                TextRenderer.DrawText(e.Graphics, item.Display, cb.Font, tr, fg,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            catch { }

            e.DrawFocusRectangle();
        }

        static bool IsSystemProcess(string proc)
        {
            switch ((proc ?? "").ToLowerInvariant())
            {
                case "explorer":
                case "textinputhost":
                case "shellexperiencehost":
                case "startmenuexperiencehost":
                case "applicationframehost":
                case "searchhost":
                case "lockapp":
                case "dwm":
                    return true;
            }
            return false;
        }

        static Icon LoadIcon(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return Icon.ExtractAssociatedIcon(path);
            }
            catch { }
            return null;
        }

        static List<AppChoice> BuildChoices(Config cfg)
        {
            var list = new List<AppChoice>();

            var none = new AppChoice();
            none.Process = "";
            none.Display = "（不指定，这块屏保持不动）";
            list.Add(none);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var byProc = new Dictionary<string, WindowInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (WindowInfo w in Win.GetWindows())
            {
                if (string.IsNullOrEmpty(w.Process)) continue;
                if (IsSystemProcess(w.Process)) continue;

                WindowInfo cur;
                if (!byProc.TryGetValue(w.Process, out cur))
                {
                    byProc[w.Process] = w;
                    continue;
                }
                // 同一个程序有多个窗口时：优先没最小化的，其次面积大的
                if (cur.Iconic && !w.Iconic) byProc[w.Process] = w;
                else if (cur.Iconic == w.Iconic &&
                         (long)w.Rect.Width * w.Rect.Height > (long)cur.Rect.Width * cur.Rect.Height)
                    byProc[w.Process] = w;
            }

            foreach (WindowInfo w in byProc.Values)
            {
                if (!seen.Add(w.Process)) continue;

                var c = new AppChoice();
                c.Process = w.Process;
                string t = w.Title;
                if (t.Length > 30) t = t.Substring(0, 30) + "…";
                c.Display = string.IsNullOrEmpty(t) ? w.Process : w.Process + "   " + t;
                c.Icon = LoadIcon(Win.ResolveImagePath(w.Pid));
                list.Add(c);
            }

            foreach (Target t in cfg.Targets)
            {
                if (string.IsNullOrEmpty(t.Process)) continue;
                if (!seen.Add(t.Process)) continue;

                var c = new AppChoice();
                c.Process = t.Process;
                c.Exe = t.Exe;
                c.Display = t.Process + "   （当前没开）";
                c.Icon = LoadIcon(t.Exe);
                list.Add(c);
            }

            return list;
        }

        static void SelectProcess(ComboBox cb, string proc)
        {
            for (int i = 0; i < cb.Items.Count; i++)
            {
                var c = cb.Items[i] as AppChoice;
                if (c == null) continue;
                if (string.Equals(c.Process, proc, StringComparison.OrdinalIgnoreCase))
                {
                    cb.SelectedIndex = i;
                    return;
                }
            }
            cb.SelectedIndex = 0;
        }

        Target MakeTarget(int mon, AppChoice c)
        {
            var t = new Target();
            t.Monitor = mon;
            t.Process = c.Process;
            t.Exe = c.Exe;

            foreach (Target o in cfg.Targets)
            {
                if (!string.Equals(o.Process, c.Process, StringComparison.OrdinalIgnoreCase)) continue;
                t.Title = o.Title;
                t.Args = o.Args;
                t.Span = o.Span;
                if (!string.IsNullOrEmpty(o.Exe)) t.Exe = o.Exe;
                break;
            }
            return t;
        }

        void SaveAndClose(int mode)
        {
            var a = cbMain.SelectedItem as AppChoice;
            var b = cbSub.SelectedItem as AppChoice;

            var targets = new List<Target>();
            if (a != null && a.Process.Length > 0) targets.Add(MakeTarget(1, a));
            if (b != null && b.Process.Length > 0) targets.Add(MakeTarget(2, b));

            if (targets.Count == 0)
            {
                MessageBox.Show(this, "两块屏都没有选应用，至少选一个。", "ScreenVeil",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string hk = boxSwitch.Text.Trim();
            string rk = boxRestore.Text.Trim();
            HotkeySpec ha = Hotkey.Parse(hk);
            HotkeySpec hb = Hotkey.Parse(rk);
            if (ha.Error.Length > 0)
            {
                MessageBox.Show(this, "切换键没法用：" + ha.Error, "ScreenVeil",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (hb.Error.Length > 0)
            {
                MessageBox.Show(this, "还原键没法用：" + hb.Error, "ScreenVeil",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (string.Equals(hk, rk, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "切换键和还原键不能是同一个组合。", "ScreenVeil",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            cfg.Targets = targets;
            cfg.MinimizeOthers = chkMin.Checked;
            cfg.HotKey = hk;
            cfg.RestoreHotKey = rk;

            try { Startup.Set(chkAuto.Checked); } catch { }

            try { ConfigIO.Save(cfg); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存失败：" + ex.Message, "ScreenVeil",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (onResult != null) onResult(cfg, mode);
            Close();
        }
    }

    // ───────────────────────────── 托盘应用 ─────────────────────────────

    internal class HotkeyForm : Form
    {
        public Action<int> OnHotkey;

        public HotkeyForm()
        {
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Minimized;
            Opacity = 0;
            ShowIcon = false;
            Size = new Size(1, 1);
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-4000, -4000);
        }

        protected override void SetVisibleCore(bool value)
        {
            // 永远不真正显示，只用来收 WM_HOTKEY
            base.SetVisibleCore(false);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Nat.WM_HOTKEY && OnHotkey != null)
            {
                OnHotkey(m.WParam.ToInt32());
                return;
            }
            base.WndProc(ref m);
        }
    }

    internal class TrayApp : ApplicationContext
    {
        const int HK_TOGGLE = 1;
        const int HK_RESTORE = 2;

        HotkeyForm form;
        NotifyIcon icon;
        ContextMenuStrip menu;
        Engine engine;
        Config cfg;
        PickerForm picker;
        EventWaitHandle showEvent;
        FileSystemWatcher watcher;
        System.Windows.Forms.Timer reloadTimer;
        string hotkeyError = "";
        string activeHotkey = "";
        string activeRestore = "";
        IntPtr mouseHook = IntPtr.Zero;
        Nat.LowLevelMouseProc mouseProc;
        int mouseSwitchButton;
        int mouseRestoreButton;
        bool shownError;
        readonly object workGate = new object();
        bool busy;

        string CurrentMessage = "";

        public TrayApp()
        {
            cfg = ConfigIO.Load(ConfigIO.ResolvePath());
            engine = new Engine();
            engine.Cfg = cfg;

            form = new HotkeyForm();
            form.OnHotkey = delegate(int id) { OnHotkey(id); };
            IntPtr h = form.Handle;   // 强制创建窗口句柄

            icon = new NotifyIcon();
            icon.Icon = MakeIcon();
            icon.Text = "ScreenVeil 双屏老板键" + (string.IsNullOrEmpty(cfg.HotKey) ? "" : "（" + cfg.HotKey + "）");
            icon.Visible = true;
            icon.DoubleClick += delegate { ShowPicker(); };

            BuildMenu();
            icon.ContextMenuStrip = menu;

            string err = RegisterHotkeys();
            hotkeyError = err;
            string tip = string.IsNullOrEmpty(err) ? "已就绪：" + cfg.HotKey + " 切换" : err;
            Tip(tip);

            if (!string.IsNullOrEmpty(cfg.LoadError))
            {
                MessageBox.Show("配置文件有问题，这次先用默认设置：\n\n" + cfg.LoadError +
                    "\n\n文件：" + cfg.Path, "ScreenVeil", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else if (!string.IsNullOrEmpty(err))
            {
                MessageBox.Show("热键没有注册成功：\n\n" + err +
                    "\n\n可以换一个组合，或先关掉占用它的软件。", "ScreenVeil", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            StartWatcher();
            StartActivationListener();

            if (Program.ShowPickerOnStart)
            {
                var t = new System.Windows.Forms.Timer();
                t.Interval = 250;
                t.Tick += delegate { t.Stop(); t.Dispose(); ShowPicker(); };
                t.Start();
            }
        }

        void StartActivationListener()
        {
            try
            {
                showEvent = new EventWaitHandle(false, EventResetMode.AutoReset,
                    "ScreenVeilShow_" + Process.GetCurrentProcess().SessionId);
            }
            catch { return; }

            var th = new Thread(delegate()
            {
                while (true)
                {
                    try
                    {
                        showEvent.WaitOne();
                        try { form.BeginInvoke((MethodInvoker)delegate { ShowPicker(); }); }
                        catch { }
                    }
                    catch { Thread.Sleep(300); }
                }
            });
            th.IsBackground = true;
            th.Start();
        }

        void ShowPicker()
        {
            if (picker != null && !picker.IsDisposed)
            {
                try { picker.Activate(); } catch { }
                return;
            }

            var p = new PickerForm(cfg, hotkeyError, delegate(Config c, int mode)
            {
                cfg = c;
                engine.Cfg = c;
                string hkErr = RegisterHotkeys();
                BuildMenu();
                icon.ContextMenuStrip = menu;
                icon.Text = "ScreenVeil 双屏老板键（" + cfg.HotKey + "）";
                if (!string.IsNullOrEmpty(hkErr))
                {
                    MessageBox.Show(hkErr, "ScreenVeil", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                if (mode == 1) DoApply();
                else if (mode == 2) DoSelfTest(5);
            });

            picker = p;
            p.FormClosed += delegate
            {
                if (picker == p) picker = null;
                EnsureMouseHook();
                Tip("已缩到托盘继续运行（Win11 可能在右下角 ^ 里）：" + cfg.HotKey + " 切换");
            };
            p.Show();
            EnsureMouseHook();
        }

        // 配置文件被外部改动时自动重载
        void StartWatcher()
        {
            try
            {
                string dir = Path.GetDirectoryName(cfg.Path);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

                watcher = new FileSystemWatcher(dir, Path.GetFileName(cfg.Path));
                watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName;
                watcher.Changed += delegate { ScheduleReload(); };
                watcher.Created += delegate { ScheduleReload(); };
                watcher.EnableRaisingEvents = true;
            }
            catch { }
        }

        void ScheduleReload()
        {
            try
            {
                if (form == null || !form.IsHandleCreated) return;
                form.BeginInvoke((MethodInvoker)delegate
                {
                    if (picker != null && !picker.IsDisposed) return;   // 选择窗口开着时别插手
                    if (reloadTimer == null)
                    {
                        reloadTimer = new System.Windows.Forms.Timer();
                        reloadTimer.Interval = 700;
                        reloadTimer.Tick += delegate { reloadTimer.Stop(); Reload(); };
                    }
                    reloadTimer.Stop();
                    reloadTimer.Start();
                });
            }
            catch { }
        }

        string RegisterHotkeys()
        {
            if (form == null || !form.IsHandleCreated) return null;

            HotkeySpec a = Hotkey.Parse(cfg.HotKey);
            HotkeySpec b = Hotkey.Parse(cfg.RestoreHotKey);

            if (a.Error.Length > 0) { hotkeyError = "切换热键无效：" + a.Error; return hotkeyError; }
            if (b.Error.Length > 0) { hotkeyError = "恢复热键无效：" + b.Error; return hotkeyError; }

            Nat.UnregisterHotKey(form.Handle, HK_TOGGLE);
            Nat.UnregisterHotKey(form.Handle, HK_RESTORE);

            bool ok1 = a.IsMouse || Nat.RegisterHotKey(form.Handle, HK_TOGGLE, a.Mods | Nat.MOD_NOREPEAT, a.Key);
            bool ok2 = b.IsMouse || Nat.RegisterHotKey(form.Handle, HK_RESTORE, b.Mods | Nat.MOD_NOREPEAT, b.Key);

            if (ok1 && ok2)
            {
                mouseSwitchButton = a.IsMouse ? a.MouseButton : 0;
                mouseRestoreButton = b.IsMouse ? b.MouseButton : 0;
                activeHotkey = cfg.HotKey;
                activeRestore = cfg.RestoreHotKey;
                hotkeyError = "";
                EnsureMouseHook();
                return null;
            }

            // 有冲突：撤掉刚注册的，退回上一次能用的组合，并把配置改回去
            Nat.UnregisterHotKey(form.Handle, HK_TOGGLE);
            Nat.UnregisterHotKey(form.Handle, HK_RESTORE);
            mouseSwitchButton = 0;
            mouseRestoreButton = 0;

            if (activeHotkey.Length > 0)
            {
                HotkeySpec oa = Hotkey.Parse(activeHotkey);
                if (oa.Error.Length == 0)
                {
                    if (oa.IsMouse) mouseSwitchButton = oa.MouseButton;
                    else Nat.RegisterHotKey(form.Handle, HK_TOGGLE, oa.Mods | Nat.MOD_NOREPEAT, oa.Key);
                }
            }
            if (activeRestore.Length > 0)
            {
                HotkeySpec ob = Hotkey.Parse(activeRestore);
                if (ob.Error.Length == 0)
                {
                    if (ob.IsMouse) mouseRestoreButton = ob.MouseButton;
                    else Nat.RegisterHotKey(form.Handle, HK_RESTORE, ob.Mods | Nat.MOD_NOREPEAT, ob.Key);
                }
            }
            EnsureMouseHook();

            hotkeyError = ok1
                ? "还原键 " + cfg.RestoreHotKey + " 被别的程序占用了"
                : "热键 " + cfg.HotKey + " 被别的程序占用了";

            if (activeHotkey.Length > 0)
            {
                cfg.HotKey = activeHotkey;
                cfg.RestoreHotKey = activeRestore;
                try { ConfigIO.Save(cfg); } catch { }
                hotkeyError += "（已沿用原来的 " + activeHotkey + "）";
            }

            return hotkeyError;
        }

        bool MouseHotkeyConfigured { get { return mouseSwitchButton != 0 || mouseRestoreButton != 0; } }

        // 只有「绑了鼠标侧键」或「选择窗口开着（可能要录制）」时才装钩子，
        // 纯键盘用户完全不装，没有任何鼠标走这里的开销。
        void EnsureMouseHook()
        {
            bool need = MouseHotkeyConfigured || (picker != null && !picker.IsDisposed);
            try
            {
                if (need && mouseHook == IntPtr.Zero)
                {
                    mouseProc = MouseHookProc;      // 保持引用，别被 GC 掉
                    mouseHook = Nat.SetWindowsHookEx(Nat.WH_MOUSE_LL, mouseProc, IntPtr.Zero, 0);
                }
                else if (!need && mouseHook != IntPtr.Zero)
                {
                    Nat.UnhookWindowsHookEx(mouseHook);
                    mouseHook = IntPtr.Zero;
                    mouseProc = null;
                }
            }
            catch { }
        }

        IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && (int)wParam == Nat.WM_XBUTTONDOWN)
                {
                    var info = (Nat.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Nat.MSLLHOOKSTRUCT));
                    int btn = (info.mouseData >> 16) & 0xFFFF;
                    if (btn == 1 || btn == 2)
                    {
                        // 正在录制按键：写进输入框，并吞掉这个键
                        if (picker != null && !picker.IsDisposed && picker.TryRecordMouse(btn))
                            return (IntPtr)1;

                        // 已经绑定：触发，并吞掉（不然浏览器会执行后退/前进）
                        if (btn == mouseSwitchButton) { DoToggle(); return (IntPtr)1; }
                        if (btn == mouseRestoreButton) { DoRestore(); return (IntPtr)1; }
                    }
                }
            }
            catch { }
            return Nat.CallNextHookEx(mouseHook, nCode, wParam, lParam);
        }

        void BuildMenu()
        {
            if (menu != null) menu.Dispose();
            menu = new ContextMenuStrip();
            menu.Font = new Font("Microsoft YaHei UI", 9f);

            var miPick = new ToolStripMenuItem("选择应用（主屏 / 副屏）…");
            miPick.Font = new Font(menu.Font, FontStyle.Bold);
            miPick.Click += delegate { ShowPicker(); };
            menu.Items.Add(miPick);

            menu.Items.Add(new ToolStripSeparator());

            var miToggle = new ToolStripMenuItem("切换  (" + cfg.HotKey + ")");
            miToggle.Click += delegate { DoToggle(); };
            menu.Items.Add(miToggle);

            var miRestore = new ToolStripMenuItem("恢复  (" + cfg.RestoreHotKey + ")");
            miRestore.Click += delegate { DoRestore(); };
            menu.Items.Add(miRestore);

            var miTry = new ToolStripMenuItem("试跑 5 秒（自动还原）");
            miTry.Click += delegate { DoSelfTest(5); };
            menu.Items.Add(miTry);

            var miNotify = new ToolStripMenuItem("显示桌面通知");
            miNotify.Checked = cfg.ShowTips;
            miNotify.Click += delegate(object sender, EventArgs e)
            {
                var mi = (ToolStripMenuItem)sender;
                cfg.ShowTips = !mi.Checked;
                mi.Checked = cfg.ShowTips;
                try { ConfigIO.Save(cfg); } catch { }
            };
            menu.Items.Add(miNotify);

            menu.Items.Add(new ToolStripSeparator());

            var miDiag = new ToolStripMenuItem("诊断 / 列出窗口");
            miDiag.Click += delegate { ShowDiagnostics(); };
            menu.Items.Add(miDiag);

            var miCfg = new ToolStripMenuItem("编辑配置…");
            miCfg.Click += delegate { OpenConfig(); };
            menu.Items.Add(miCfg);

            var miReload = new ToolStripMenuItem("重新加载配置");
            miReload.Click += delegate { Reload(); };
            menu.Items.Add(miReload);

            var miStart = new ToolStripMenuItem("开机自启");
            miStart.CheckOnClick = false;
            miStart.Checked = File.Exists(StartupLnk());
            miStart.Click += delegate(object sender, EventArgs e)
            {
                var mi = (ToolStripMenuItem)sender;
                bool want = !mi.Checked;
                bool ok = want ? CreateStartupShortcut() : RemoveStartupShortcut();
                mi.Checked = File.Exists(StartupLnk());
                Tip(ok ? (want ? "已设为开机自启" : "已取消开机自启") : "改不了启动文件夹里的快捷方式");
            };
            menu.Items.Add(miStart);

            menu.Items.Add(new ToolStripSeparator());

            var miExit = new ToolStripMenuItem("退出");
            miExit.Click += delegate { ExitApp(); };
            menu.Items.Add(miExit);
        }

        void OnHotkey(int id)
        {
            if (id == HK_TOGGLE) DoToggle();
            else if (id == HK_RESTORE) DoRestore();
        }

        void DoToggle()
        {
            if (cfg.ToggleOnRepeat && engine.Applied) DoRestore();
            else DoApply();
        }

        void DoApply()
        {
            // 选择窗口还开着的话，让它让开，否则它会盖在切换结果上面
            try
            {
                if (picker != null && !picker.IsDisposed && picker.Visible)
                {
                    picker.SendToBack();
                }
            }
            catch { }

            RunWork(delegate
            {
                string msg = engine.Apply();
                try
                {
                    File.WriteAllText(Path.Combine(Path.GetTempPath(), "screenveil-apply.log"),
                        DateTime.Now.ToString("HH:mm:ss") + "  " + msg + "\r\n" + string.Join("\r\n", engine.Log.ToArray()),
                        new UTF8Encoding(false));
                }
                catch { }
                Tip(msg);
                if (!msg.StartsWith("已切换") && !shownError)
                {
                    shownError = true;
                    try
                    {
                        form.BeginInvoke((MethodInvoker)delegate
                        {
                            MessageBox.Show(msg, "ScreenVeil", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        });
                    }
                    catch { }
                }
            });
        }

        void DoRestore()
        {
            RunWork(delegate
            {
                string msg = engine.Restore();
                Tip(msg);
            });
        }

        void RunWork(Action a)
        {
            lock (workGate)
            {
                if (busy) { Tip("上一个操作还没做完…"); return; }
                busy = true;
            }
            ThreadPool.QueueUserWorkItem(delegate(object st)
            {
                try { a(); }
                catch (Exception ex) { Tip("出错：" + ex.Message); }
                finally { lock (workGate) { busy = false; } }
            });
        }

        void Tip(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            CurrentMessage = text;
            if (!cfg.ShowTips) return;
            try
            {
                if (form != null && form.IsHandleCreated && icon != null)
                {
                    form.BeginInvoke((MethodInvoker)delegate
                    {
                        try { icon.ShowBalloonTip(2000, "ScreenVeil", text, ToolTipIcon.Info); }
                        catch { }
                    });
                }
            }
            catch { }
        }

        void Reload()
        {
            string path = cfg.Path;
            Config fresh = ConfigIO.Load(path);
            Nat.UnregisterHotKey(form.Handle, HK_TOGGLE);
            Nat.UnregisterHotKey(form.Handle, HK_RESTORE);
            cfg = fresh;
            engine.Cfg = cfg;
            BuildMenu();
            icon.ContextMenuStrip = menu;
            icon.Text = "ScreenVeil 双屏老板键（" + cfg.HotKey + "）";
            string err = RegisterHotkeys();
            Tip(string.IsNullOrEmpty(err) ? "配置已重新加载" : err);
        }

        void OpenConfig()
        {
            try
            {
                if (!File.Exists(cfg.Path)) File.WriteAllText(cfg.Path, ConfigIO.DefaultJson(), new UTF8Encoding(true));
                Process.Start("notepad.exe", "\"" + cfg.Path + "\"");
            }
            catch (Exception ex) { Tip("打不开配置文件：" + ex.Message); }
        }

        void ShowDiagnostics()
        {
            ShowText("ScreenVeil 诊断", Diagnostics.Report(cfg, engine));
        }

        void ShowText(string title, string text)
        {
            Action show = delegate
            {
                var f = new Form();
                f.Text = title;
                f.StartPosition = FormStartPosition.CenterScreen;
                f.Size = new Size(960, 640);
                f.Font = new Font("Microsoft YaHei UI", 9f);

                var tb = new TextBox();
                tb.Multiline = true;
                tb.ReadOnly = true;
                tb.WordWrap = false;
                tb.ScrollBars = ScrollBars.Both;
                tb.Dock = DockStyle.Fill;
                tb.Font = new Font("Consolas", 9.5f);
                tb.Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");

                var panel = new Panel();
                panel.Dock = DockStyle.Bottom;
                panel.Height = 44;

                var btnCopy = new Button();
                btnCopy.Text = "复制全部";
                btnCopy.Dock = DockStyle.Right;
                btnCopy.Width = 110;
                btnCopy.Click += delegate { try { Clipboard.SetText(tb.Text); } catch { } };

                var btnClose = new Button();
                btnClose.Text = "关闭";
                btnClose.Dock = DockStyle.Right;
                btnClose.Width = 90;
                btnClose.Click += delegate { f.Close(); };

                var btnApply = new Button();
                btnApply.Text = "直接切换一次";
                btnApply.Dock = DockStyle.Left;
                btnApply.Width = 130;
                btnApply.Click += delegate { DoApply(); };

                panel.Controls.Add(btnCopy);
                panel.Controls.Add(btnClose);
                panel.Controls.Add(btnApply);

                f.Controls.Add(tb);
                f.Controls.Add(panel);
                f.Show();
            };

            try
            {
                if (form.InvokeRequired) form.BeginInvoke(show);
                else show();
            }
            catch { }
        }

        void DoSelfTest(int seconds)
        {
            RunWork(delegate
            {
                string report = engine.SelfTest(seconds);
                ShowText("ScreenVeil 试跑结果", report);
            });
        }

        void ExitApp()
        {
            try
            {
                Nat.UnregisterHotKey(form.Handle, HK_TOGGLE);
                Nat.UnregisterHotKey(form.Handle, HK_RESTORE);
                if (mouseHook != IntPtr.Zero) Nat.UnhookWindowsHookEx(mouseHook);
                mouseHook = IntPtr.Zero;
            }
            catch { }
            if (engine.Applied) { try { engine.Restore(); } catch { } }
            icon.Visible = false;
            icon.Dispose();
            form.Dispose();
            ExitThread();
        }

        static Icon MakeIcon()
        {
            return AppIcon.Get();
        }

        static string StartupLnk() { return Startup.Lnk(); }

        static bool CreateStartupShortcut() { return Startup.Set(true); }

        static bool RemoveStartupShortcut() { return Startup.Set(false); }
    }

    internal static class AppIcon
    {
        public static Icon Get()
        {
            // 优先用嵌进 exe 的 ico（这样任务栏、资源管理器、托盘是同一个图标）
            try
            {
                Icon ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (ico != null) return ico;
            }
            catch { }

            try
            {
                var bmp = new Bitmap(32, 32);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.Clear(Color.FromArgb(255, 26, 30, 38));
                    using (var b = new SolidBrush(Color.FromArgb(255, 96, 214, 170)))
                    {
                        g.FillRectangle(b, 4, 8, 10, 16);
                        g.FillRectangle(b, 18, 8, 10, 16);
                    }
                }
                IntPtr h = bmp.GetHicon();
                Icon tmp = Icon.FromHandle(h);
                Icon clone = (Icon)tmp.Clone();
                Nat.DestroyIcon(h);
                bmp.Dispose();
                return clone;
            }
            catch { return SystemIcons.Application; }
        }
    }

    internal static class Startup
    {
        public static string Lnk()
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            return Path.Combine(dir, "ScreenVeil.lnk");
        }

        public static bool Enabled { get { return File.Exists(Lnk()); } }

        public static bool Set(bool on)
        {
            try
            {
                if (!on)
                {
                    if (File.Exists(Lnk())) File.Delete(Lnk());
                    return true;
                }

                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return false;

                object sh = Activator.CreateInstance(t);
                object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { Lnk() });
                Type lt = lnk.GetType();
                lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { Application.ExecutablePath });
                lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Path.GetDirectoryName(Application.ExecutablePath) });
                lt.InvokeMember("Arguments", BindingFlags.SetProperty, null, lnk, new object[] { "--tray" });
                lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "ScreenVeil 双屏老板键" });
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
                return File.Exists(Lnk());
            }
            catch { return false; }
        }
    }

    // ───────────────────────────── 诊断 ─────────────────────────────

    internal static class Diagnostics
    {
        public static string Report(Config cfg, Engine engine)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ScreenVeil 诊断  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("程序    : " + Application.ExecutablePath);
            sb.AppendLine("配置    : " + cfg.Path);
            sb.AppendLine("进程号  : " + Process.GetCurrentProcess().Id);
            sb.AppendLine("配置错误: " + (string.IsNullOrEmpty(cfg.LoadError) ? "无" : cfg.LoadError));

            int procCount = -1;
            try { procCount = Process.GetProcesses().Length; } catch { }
            sb.AppendLine("进程枚举: GetProcesses() = " + (procCount < 0 ? "失败" : procCount + " 个"));

            HotkeySpec a = Hotkey.Parse(cfg.HotKey);
            HotkeySpec b = Hotkey.Parse(cfg.RestoreHotKey);
            sb.AppendLine("切换热键: " + cfg.HotKey + (a.Error.Length > 0 ? "  ← " + a.Error : "  ok"));
            sb.AppendLine("恢复热键: " + cfg.RestoreHotKey + (b.Error.Length > 0 ? "  ← " + b.Error : "  ok"));
            sb.AppendLine("当前状态: " + (engine.Applied ? "已切换" : "正常"));
            sb.AppendLine();

            List<Monitor> mons = Win.GetMonitors();
            sb.AppendLine("显示器 (" + mons.Count + "):");
            foreach (Monitor m in mons) sb.AppendLine("  " + m.Describe());
            sb.AppendLine();

            List<WindowInfo> wins = Win.GetWindows();
            sb.AppendLine("可见窗口 (" + wins.Count + "):");
            int i = 0;
            foreach (WindowInfo w in wins)
            {
                i++;
                if (i > 200) { sb.AppendLine("  …"); break; }
                int monNo = 0;
                foreach (Monitor m in mons) if (Win.Overlaps(w.Rect, m.Bounds)) monNo = m.Index;
                string t = w.Title.Length > 48 ? w.Title.Substring(0, 48) + "…" : w.Title;
                sb.AppendLine(string.Format("  [{0,2}] pid={1,-6} {2,-22} 屏{3} {4,-26} {5}{6}",
                    i, w.Pid,
                    w.Process.Length > 22 ? w.Process.Substring(0, 22) : w.Process,
                    monNo == 0 ? "?" : monNo.ToString(), w.Rect.ToString(), t,
                    w.Iconic ? "  (最小化)" : ""));

                bool ctrl = Win.CanControl(w.H);
                string exe = Win.ResolveImagePath(w.Pid);
                sb.AppendLine("        " + exe + (ctrl ? "" : "   ← 权限更高，普通权限动不了它"));
            }
            if (wins.Count == 0) sb.AppendLine("  （一个都没看到 —— 如果明明开着程序，说明本程序被沙箱/权限隔离了）");
            sb.AppendLine();

            sb.AppendLine("配置目标:");
            foreach (Target t in cfg.Targets) sb.AppendLine("  " + t.Describe());
            sb.AppendLine();
            sb.AppendLine("提示：把上面某行的进程名填进 ScreenVeil.json 的 targets 里即可。");
            return sb.ToString();
        }
    }

    // ───────────────────────────── 入口 ─────────────────────────────

    internal static class Program
    {
        public static bool ShowPickerOnStart = true;

        [STAThread]
        static void Main(string[] args)
        {
            try { Nat.SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            ShowPickerOnStart = (args.Length == 0);

            if (cmd == "--set")
            {
                Config c0 = ConfigIO.Load(ConfigIO.ResolvePath());
                var procs = new List<string>();
                for (int i = 1; i < args.Length; i++)
                {
                    string p = args[i].Trim();
                    if (p.Length > 0) procs.Add(p);
                }

                var list = new List<Target>();
                int mon = 1;
                foreach (string p in procs)
                {
                    var t = new Target();
                    t.Monitor = mon++;
                    t.Process = p;
                    foreach (Target o in c0.Targets)
                    {
                        if (!string.Equals(o.Process, p, StringComparison.OrdinalIgnoreCase)) continue;
                        t.Exe = o.Exe;
                        t.Title = o.Title;
                        t.Args = o.Args;
                        t.Span = o.Span;
                        break;
                    }
                    list.Add(t);
                }
                c0.Targets = list;
                ConfigIO.Save(c0);
                try { Console.Out.Write(File.ReadAllText(c0.Path, Encoding.UTF8)); } catch { }
                return;
            }

            if (cmd == "--diagnose")
            {
                string path = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "ScreenVeil-diagnose.txt");
                Config cfg = ConfigIO.Load(ConfigIO.ResolvePath());
                string txt = Diagnostics.Report(cfg, new Engine());
                try { File.WriteAllText(path, txt, new UTF8Encoding(true)); } catch { }
                try { Console.Out.Write(txt); } catch { }
                return;
            }

            if (cmd == "--selftest")
            {
                int secs = 5;
                if (args.Length > 1)
                {
                    int v;
                    if (int.TryParse(args[1], out v) && v > 0 && v < 600) secs = v;
                }
                string outPath = args.Length > 2 ? args[2] : Path.Combine(Path.GetTempPath(), "ScreenVeil-selftest.txt");
                Config cfg0 = ConfigIO.Load(ConfigIO.ResolvePath());
                var eng0 = new Engine();
                eng0.Cfg = cfg0;
                string report = eng0.SelfTest(secs);
                try { File.WriteAllText(outPath, report, new UTF8Encoding(true)); } catch { }
                try { Console.Out.Write(report); } catch { }
                return;
            }

            if (cmd == "--apply" || cmd == "--restore")
            {
                string logPath = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "ScreenVeil-apply.txt");
                Config cfg = ConfigIO.Load(ConfigIO.ResolvePath());
                var eng = new Engine();
                eng.Cfg = cfg;
                string msg = cmd == "--apply" ? eng.Apply() : eng.Restore();

                var sb = new StringBuilder();
                sb.AppendLine(msg);
                foreach (string l in eng.Log) sb.AppendLine(l);
                try { File.WriteAllText(logPath, sb.ToString(), new UTF8Encoding(true)); } catch { }
                try { Console.Out.Write(sb.ToString()); } catch { }
                return;
            }

            bool created;
            using (var mutex = new Mutex(true, "ScreenVeilTray_" + Process.GetCurrentProcess().SessionId, out created))
            {
                if (!created)
                {
                    // 已经有实例在跑了：叫它把选择窗口弹出来
                    try
                    {
                        using (var ev = EventWaitHandle.OpenExisting("ScreenVeilShow_" + Process.GetCurrentProcess().SessionId))
                            ev.Set();
                    }
                    catch { }
                    return;
                }
                try
                {
                    Application.Run(new TrayApp());
                }
                finally
                {
                    GC.KeepAlive(mutex);
                }
            }
        }
    }
}
