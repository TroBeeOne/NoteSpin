# Draws NoteSpin's icon (three sticky notes fanned on a hub) and writes ..\favicon.ico
# Sizes 16-64 are stored as bitmaps (what Windows Forms reads), 256 as PNG.
$ErrorActionPreference = 'Stop'
$out = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) 'favicon.ico'

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class NsIcon
{
    static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static void Card(Graphics g, float s, float angle, Color body, Color band, bool lines)
    {
        var st = g.Save();
        float px = s * 0.5f, py = s * 0.86f;
        g.TranslateTransform(px, py);
        g.RotateTransform(angle);
        float w = s * 0.66f, h = s * 0.70f;
        var r = new RectangleF(-w / 2, -h, w, h);
        float rad = Math.Max(1f, s * 0.06f);
        using (var path = Round(r, rad))
        {
            if (s >= 32)
                using (var sh = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
                {
                    g.TranslateTransform(s * 0.015f, s * 0.02f);
                    g.FillPath(sh, path);
                    g.TranslateTransform(-s * 0.015f, -s * 0.02f);
                }
            using (var b = new SolidBrush(body)) g.FillPath(b, path);
            var clip = g.Clip;
            g.SetClip(path);
            using (var b = new SolidBrush(band)) g.FillRectangle(b, r.X, r.Y, w, h * 0.2f);
            g.Clip = clip;
            if (lines)
            {
                using (var b = new SolidBrush(Color.FromArgb(150, 60, 52, 30)))
                {
                    float lh = Math.Max(1f, s * 0.045f);
                    for (int i = 0; i < 3; i++)
                    {
                        float lw = w * (i == 2 ? 0.45f : 0.7f);
                        g.FillRectangle(b, r.X + w * 0.14f, r.Y + h * (0.36f + i * 0.17f), lw, lh);
                    }
                }
            }
            if (s >= 48)
                using (var pen = new Pen(Color.FromArgb(60, 0, 0, 0), Math.Max(1f, s / 128f))) g.DrawPath(pen, path);
        }
        g.Restore(st);
    }

    public static Bitmap Draw(int s)
    {
        var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            Card(g, s, -26f, Color.FromArgb(150, 205, 255), Color.FromArgb(96, 160, 230), false);
            Card(g, s, 22f, Color.FromArgb(255, 170, 205), Color.FromArgb(232, 110, 160), false);
            Card(g, s, -2f, Color.FromArgb(255, 236, 128), Color.FromArgb(240, 196, 60), s >= 24);
            float hr = s * 0.10f, cx = s * 0.5f, cy = s * 0.86f;
            using (var b = new SolidBrush(Color.FromArgb(46, 50, 60))) g.FillEllipse(b, cx - hr, cy - hr, hr * 2, hr * 2);
            using (var b = new SolidBrush(Color.FromArgb(170, 178, 190))) g.FillEllipse(b, cx - hr * 0.45f, cy - hr * 0.45f, hr * 0.9f, hr * 0.9f);
        }
        return bmp;
    }

    static byte[] Dib(Bitmap bmp)
    {
        int s = bmp.Width;
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(40); w.Write(s); w.Write(s * 2); w.Write((short)1); w.Write((short)32);
        w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        for (int y = s - 1; y >= 0; y--)
            for (int x = 0; x < s; x++)
            {
                Color c = bmp.GetPixel(x, y);
                w.Write(c.B); w.Write(c.G); w.Write(c.R); w.Write(c.A);
            }
        int maskRow = ((s + 31) / 32) * 4;
        w.Write(new byte[maskRow * s]);
        return ms.ToArray();
    }

    public static void Write(string path)
    {
        int[] sizes = { 16, 24, 32, 48, 64, 256 };
        var images = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
            using (var bmp = Draw(sizes[i]))
            {
                if (sizes[i] == 256) { var ms = new MemoryStream(); bmp.Save(ms, ImageFormat.Png); images[i] = ms.ToArray(); }
                else images[i] = Dib(bmp);
            }
        using (var f = new BinaryWriter(File.Create(path)))
        {
            f.Write((short)0); f.Write((short)1); f.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                f.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); f.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                f.Write((byte)0); f.Write((byte)0); f.Write((short)1); f.Write((short)32);
                f.Write(images[i].Length); f.Write(offset);
                offset += images[i].Length;
            }
            foreach (var img in images) f.Write(img);
        }
    }

    public static void Preview(string path) { using (var b = Draw(256)) b.Save(path, ImageFormat.Png); }
}
"@

[NsIcon]::Write($out)
if ($args.Count -gt 0) { [NsIcon]::Preview($args[0]) }
"Wrote $out"
