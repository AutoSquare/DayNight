// Copyright (c) 2022 namazso <admin@namazso.eu>
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
// COM 接口顺序参考： https://gist.github.com/namazso/0fde102c2fc56049c7c37f7fdf9ac3cd
using System.Runtime.InteropServices;

namespace DayNight.Services;

/// <summary>通过 Windows 主题管理器应用完整主题，调用线程必须使用 STA。</summary>
internal static class WindowsThemeManager
{
    internal static void Apply(string path)
    {
        var classId = new Guid("9324da94-50ec-4a14-a770-e90ca03e7c8f");
        var interfaceId = typeof(IThemeManager2).GUID;
        Marshal.ThrowExceptionForHR(CoCreateInstance(classId, IntPtr.Zero, 0x17, interfaceId, out var instance));
        try
        {
            var manager = (IThemeManager2)instance;
            manager.Init(0);
            // applyFlags=0：不跳过背景、颜色、声音、光标或图标。packFlags=4：静默。
            // 按完整路径选取，避免同名主题被错误匹配。
            manager.AddAndSelectTheme(IntPtr.Zero, path, 0, 4);
        }
        finally { Marshal.FinalReleaseComObject(instance); }
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(
        [In, MarshalAs(UnmanagedType.LPStruct)] Guid classId, IntPtr outer, uint context,
        [In, MarshalAs(UnmanagedType.LPStruct)] Guid interfaceId,
        [MarshalAs(UnmanagedType.IUnknown)] out object instance);

    // 必须保留原始 vtable 顺序。未调用的方法用于占据 ABI 槽位。
    // 不使用 PreserveSig，失败的 HRESULT 由 COM 互操作转换成异常。
    [ComImport, Guid("c1e8c83e-845d-4d95-81db-e283fdffc000")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IThemeManager2
    {
        void Init(uint flags);
        void InitAsync(IntPtr window, int parameter);
        void Refresh();
        void RefreshAsync(IntPtr window, int parameter);
        void RefreshComplete();
        void GetThemeCount(out int count);
        void GetTheme(int index, out IntPtr theme);
        void IsThemeDisabled(int index, out int disabled);
        void GetCurrentTheme(out int index);
        void SetCurrentTheme(IntPtr window, int index, int applyNow, uint applyFlags, uint packFlags);
        void GetCustomTheme(out int index);
        void GetDefaultTheme(out int index);
        void CreateThemePack(IntPtr window, string path, uint flags);
        void CloneAndSetCurrentTheme(IntPtr window, string path, out string result);
        void InstallThemePack(IntPtr window, string path, int parameter, uint flags, out string result, out IntPtr theme);
        void DeleteTheme(string path);
        void OpenTheme(IntPtr window, string path, uint flags);
        void AddAndSelectTheme(IntPtr window, string path, uint applyFlags, uint packFlags);
    }
}
