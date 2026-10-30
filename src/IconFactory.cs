using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RP2040AutoFlash
{
    // 通知領域と exe のアイコン（角丸の四角に下向き矢印）を描く
    static class IconFactory
    {
        public static readonly Color AutoColor = Color.FromArgb(46, 125, 50);
        public static readonly Color ManualColor = Color.FromArgb(21, 101, 192);
        public static readonly Color BusyColor = Color.FromArgb(239, 108, 0);

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);

        public static Bitmap Draw(Color color, int size)
        {
            var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.ScaleTransform(size / 16f, size / 16f);
                using (var body = RoundedRect(new RectangleF(0.5f, 0.5f, 15f, 15f), 3.5f))
                using (var brush = new SolidBrush(color))
                    g.FillPath(brush, body);

                // 下向き矢印と受け皿
                g.FillRectangle(Brushes.White, 6.5f, 2.5f, 3f, 4.5f);
                g.FillPolygon(Brushes.White, new[] { new PointF(3.5f, 6.5f), new PointF(12.5f, 6.5f), new PointF(8f, 11f) });
                g.FillRectangle(Brushes.White, 3.5f, 12f, 9f, 1.75f);
            }
            return bitmap;
        }

        public static Icon Create(Color color, int size)
        {
            using (var bitmap = Draw(color, size))
            {
                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (var temp = Icon.FromHandle(handle))
                        return (Icon)temp.Clone();
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            float d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
