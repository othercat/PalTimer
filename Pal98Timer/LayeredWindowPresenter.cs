using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Pal98Timer
{
    internal static class LayeredWindowPresenter
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int ULW_ALPHA = 0x00000002;
        private const byte AC_SRC_OVER = 0x00;
        private const byte AC_SRC_ALPHA = 0x01;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;

        public static void SetEnabled(Form form, bool enabled)
        {
            if (form == null || form.IsDisposed || !form.IsHandleCreated)
            {
                return;
            }

            IntPtr style = GetWindowLongPtr(form.Handle, GWL_EXSTYLE);
            long value = style.ToInt64();
            long updated = enabled ? value | WS_EX_LAYERED : value & ~((long)WS_EX_LAYERED);
            if (updated != value)
            {
                SetLastError(0);
                IntPtr previous = SetWindowLongPtr(form.Handle, GWL_EXSTYLE, new IntPtr(updated));
                int error = Marshal.GetLastWin32Error();
                if (previous == IntPtr.Zero && error != 0)
                {
                    throw new Win32Exception(error);
                }
                SetWindowPos(
                    form.Handle,
                    IntPtr.Zero,
                    0,
                    0,
                    0,
                    0,
                    SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            }

            if (!enabled)
            {
                form.Invalidate(true);
            }
        }

        public static void Present(Form form, Bitmap source)
        {
            if (form == null || form.IsDisposed || source == null || source.Width <= 0 || source.Height <= 0)
            {
                return;
            }
            if (!form.IsHandleCreated)
            {
                return;
            }

            SetEnabled(form, true);
            using (Bitmap premultiplied = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb))
            {
                using (Graphics graphics = Graphics.FromImage(premultiplied))
                {
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.DrawImageUnscaled(source, 0, 0);
                }

                IntPtr screenDc = GetDC(IntPtr.Zero);
                if (screenDc == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                IntPtr memoryDc = IntPtr.Zero;
                IntPtr bitmapHandle = IntPtr.Zero;
                IntPtr previousBitmap = IntPtr.Zero;
                try
                {
                    memoryDc = CreateCompatibleDC(screenDc);
                    if (memoryDc == IntPtr.Zero)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }

                    bitmapHandle = premultiplied.GetHbitmap(Color.FromArgb(0));
                    previousBitmap = SelectObject(memoryDc, bitmapHandle);
                    POINT sourcePoint = new POINT(0, 0);
                    POINT destinationPoint = new POINT(form.Left, form.Top);
                    SIZE size = new SIZE(source.Width, source.Height);
                    BLENDFUNCTION blend = new BLENDFUNCTION
                    {
                        BlendOp = AC_SRC_OVER,
                        BlendFlags = 0,
                        SourceConstantAlpha = 255,
                        AlphaFormat = AC_SRC_ALPHA,
                    };

                    if (!UpdateLayeredWindow(
                        form.Handle,
                        screenDc,
                        ref destinationPoint,
                        ref size,
                        memoryDc,
                        ref sourcePoint,
                        0,
                        ref blend,
                        ULW_ALPHA))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                }
                finally
                {
                    if (previousBitmap != IntPtr.Zero && memoryDc != IntPtr.Zero)
                    {
                        SelectObject(memoryDc, previousBitmap);
                    }
                    if (bitmapHandle != IntPtr.Zero)
                    {
                        DeleteObject(bitmapHandle);
                    }
                    if (memoryDc != IntPtr.Zero)
                    {
                        DeleteDC(memoryDc);
                    }
                    ReleaseDC(IntPtr.Zero, screenDc);
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;

            public POINT(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int Width;
            public int Height;

            public SIZE(int width, int height)
            {
                Width = width;
                Height = height;
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr newLong);

        [DllImport("kernel32.dll")]
        private static extern void SetLastError(uint errorCode);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hDc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hDc, IntPtr value);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr value);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(
            IntPtr hWnd,
            IntPtr destinationDc,
            ref POINT destinationPoint,
            ref SIZE size,
            IntPtr sourceDc,
            ref POINT sourcePoint,
            int colorKey,
            ref BLENDFUNCTION blend,
            int flags);
    }
}
