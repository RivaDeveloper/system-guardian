# Renders the Lumen "ambient light" background without banding.
# The Lumen website paints three radial gradients on a layer 20% bigger than the viewport on every side,
# so the light bleeds in from the corners and edges instead of forming round blobs. This does the same
# per pixel, with a smooth falloff and triangular dithering so 8-bit output shows no rings.
#
# Usage:  . tools\AmbientLib.ps1 ; $bmp = [Ambient]::Render(2560, 1440, 7)

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class Ambient
{
    // centre x/y and radii x/y as fractions of the visible window (already mapped from the website's
    // inset:-20% layer), colour, and peak opacity.
    static readonly double[,] L = new double[,] {
        // cx     cy      rx     ry     r    g    b    a
        {  0.03,  0.10,   0.70,  0.62,  54,  31, 122,  1.0 },   // violet, top left
        {  0.90,  0.64,   0.62,  0.72, 152,  27,  58,  0.95 },  // crimson, right
        {  0.62, -0.02,   0.50,  0.46, 152,  27,  58,  0.5 },   // crimson, top
    };

    static double Fall(double d)
    {
        double u = d / 0.95; if (u >= 1) return 0;
        double s = u * u * (3 - 2 * u);              // smoothstep: no visible edge
        return Math.Pow(1 - s, 1.2);
    }

    public static Bitmap Render(int w, int h, int seed)
    {
        Bitmap bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        Random rnd = new Random(seed);
        byte[] row = new byte[Math.Abs(data.Stride)];
        for (int y = 0; y < h; y++)
        {
            double fy = (double)y / h;
            for (int x = 0; x < w; x++)
            {
                double fx = (double)x / w;
                double r = 6, g = 7, b = 12;
                for (int i = L.GetLength(0) - 1; i >= 0; i--)   // first layer ends up on top, like CSS
                {
                    double dx = (fx - L[i, 0]) / L[i, 2], dy = (fy - L[i, 1]) / L[i, 3];
                    double a = L[i, 7] * Fall(Math.Sqrt(dx * dx + dy * dy));
                    if (a <= 0) continue;
                    r = r * (1 - a) + L[i, 4] * a;
                    g = g * (1 - a) + L[i, 5] * a;
                    b = b * (1 - a) + L[i, 6] * a;
                }
                double n = rnd.NextDouble() + rnd.NextDouble() - 1.0;   // triangular dither, +-1 level
                int o = x * 3;
                row[o]     = (byte)Math.Max(0, Math.Min(255, Math.Round(b + n)));
                row[o + 1] = (byte)Math.Max(0, Math.Min(255, Math.Round(g + n)));
                row[o + 2] = (byte)Math.Max(0, Math.Min(255, Math.Round(r + n)));
            }
            Marshal.Copy(row, 0, new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), data.Stride);
        }
        bmp.UnlockBits(data);
        return bmp;
    }
}
"@
