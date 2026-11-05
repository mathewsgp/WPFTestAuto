using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace WpfTestIde.Uia3Worker.Core;

/// <summary>
/// Screenshot capture via PrintWindow/GDI (UIA3 has no screenshot support).
/// Returns base64-encoded PNG of the element's native window.
/// </summary>
public static class ScreenshotCapture
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, int nFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, ref RECT lpRect);

    private const uint GW_OWNER = 4;

    public static string? CaptureWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return CaptureScreen();
        }

        IntPtr root = GetWindow(hwnd, GW_OWNER);
        if (root != IntPtr.Zero && root != hwnd)
            hwnd = root;

        try
        {
            var rect = new RECT();
            if (!GetWindowRect(hwnd, ref rect))
                return null;

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0)
                return null;

            using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using var gfx = Graphics.FromImage(bmp);
            IntPtr hdc = gfx.GetHdc();
            try
            {
                PrintWindow(hwnd, hdc, 0);
            }
            finally
            {
                gfx.ReleaseHdc(hdc);
            }

            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return Convert.ToBase64String(ms.ToArray());
        }
        catch
        {
            return null;
        }
    }

    private static string? CaptureScreen()
    {
        try
        {
            IntPtr desktop = GetDesktopWindow();
            var rect = new RECT();
            if (!GetWindowRect(desktop, ref rect))
                return null;

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0)
                return null;

            using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using var gfx = Graphics.FromImage(bmp);
            IntPtr hdc = gfx.GetHdc();
            try
            {
                PrintWindow(desktop, hdc, 0);
            }
            finally
            {
                gfx.ReleaseHdc(hdc);
            }

            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return Convert.ToBase64String(ms.ToArray());
        }
        catch
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
