using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

public static class ImgTool
{
    // Load into premultiplied-free ARGB byte array (B,G,R,A)
    static byte[] GetBytes(Bitmap bmp, out int stride)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        stride = data.Stride;
        var bytes = new byte[stride * bmp.Height];
        Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
        bmp.UnlockBits(data);
        return bytes;
    }

    static Bitmap FromBytes(byte[] bytes, int w, int h, int stride)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, w, h);
        var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        bmp.UnlockBits(data);
        return bmp;
    }

    static int Dist(byte[] p, int i, int r, int g, int b)
    {
        int db = p[i] - b, dg = p[i + 1] - g, dr = p[i + 2] - r;
        return Math.Max(Math.Abs(db), Math.Max(Math.Abs(dg), Math.Abs(dr)));
    }

    // Collect background palette from border ring (distinct colors within tol)
    static List<int[]> BorderPalette(byte[] p, int w, int h, int stride, int ring, int tol)
    {
        var pal = new List<int[]>();
        var counts = new List<int>();
        for (int y = 0; y < h; y++)
        {
            bool edgeRow = y < ring || y >= h - ring;
            for (int x = 0; x < w; x++)
            {
                if (!edgeRow && x >= ring && x < w - ring) continue;
                int i = y * stride + x * 4;
                int b = p[i], g = p[i + 1], r = p[i + 2];
                int found = -1;
                for (int k = 0; k < pal.Count; k++)
                {
                    var c = pal[k];
                    if (Math.Abs(c[0] - r) <= tol && Math.Abs(c[1] - g) <= tol && Math.Abs(c[2] - b) <= tol) { found = k; break; }
                }
                if (found >= 0) counts[found]++;
                else { pal.Add(new[] { r, g, b }); counts.Add(1); }
            }
        }
        // keep colors covering >= 3% of border pixels
        int total = 0; foreach (var c in counts) total += c;
        var keep = new List<int[]>();
        for (int k = 0; k < pal.Count; k++) if (counts[k] >= total * 0.03) keep.Add(pal[k]);
        return keep;
    }

    public static string Describe(string path)
    {
        using (var bmp = new Bitmap(path))
        {
            int stride; var p = GetBytes(bmp, out stride);
            int w = bmp.Width, h = bmp.Height;
            int minX = w, maxX = -1, minY = h, maxY = -1;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                if (p[y * stride + x * 4 + 3] > 20) { if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; }
            }
            var pal = BorderPalette(p, w, h, stride, Math.Max(2, w / 100), 14);
            string ps = "";
            foreach (var c in pal) ps += string.Format("({0},{1},{2}) ", c[0], c[1], c[2]);
            return string.Format("{0}x{1} opaque bbox x{2}-{3} y{4}-{5} borderPal: {6}", w, h, minX, maxX, minY, maxY, ps);
        }
    }

    // Remove background by flood fill from border through palette-like pixels.
    // minCompFrac: drop connected opaque components smaller than this fraction of the largest component.
    // Returns info string.
    public static string RemoveBg(string src, string dst, int tol, double minCompFrac, bool crop, int padPct, int outW, int outH, int maxSide)
    { return RemoveBg(src, dst, tol, minCompFrac, crop, padPct, outW, outH, maxSide, 12); }

    public static string RemoveBg(string src, string dst, int tol, double minCompFrac, bool crop, int padPct, int outW, int outH, int maxSide, int neutralChroma)
    {
        using (var bmp = new Bitmap(src))
        {
            int stride; var p = GetBytes(bmp, out stride);
            int w = bmp.Width, h = bmp.Height;
            var pal = BorderPalette(p, w, h, stride, Math.Max(2, w / 100), 14);

            var isBg = new bool[w * h];
            var q = new Queue<int>();
            double palLum = 0; foreach (var c in pal) palLum += (c[0] + c[1] + c[2]) / 3.0; palLum /= Math.Max(1, pal.Count);
            bool lightMode = palLum >= 128;
            Func<int, int, bool> match = (x, y) =>
            {
                int i = y * stride + x * 4;
                if (p[i + 3] < 20) return true;
                foreach (var c in pal) if (Dist(p, i, c[0], c[1], c[2]) <= tol) return true;
                int b = p[i], g = p[i + 1], r = p[i + 2];
                int chroma = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
                int lum = (r + g + b) / 3;
                if (chroma <= neutralChroma && ((lightMode && lum >= 140) || (!lightMode && lum <= 150))) return true;
                return false;
            };
            for (int x = 0; x < w; x++) { foreach (int y in new[] { 0, h - 1 }) if (!isBg[y * w + x] && match(x, y)) { isBg[y * w + x] = true; q.Enqueue(y * w + x); } }
            for (int y = 0; y < h; y++) { foreach (int x in new[] { 0, w - 1 }) if (!isBg[y * w + x] && match(x, y)) { isBg[y * w + x] = true; q.Enqueue(y * w + x); } }
            int[] dx = { 1, -1, 0, 0 }, dy = { 0, 0, 1, -1 };
            while (q.Count > 0)
            {
                int idx = q.Dequeue(); int cx = idx % w, cy = idx / w;
                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + dx[d], ny = cy + dy[d];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int ni = ny * w + nx;
                    if (isBg[ni]) continue;
                    if (match(nx, ny)) { isBg[ni] = true; q.Enqueue(ni); }
                }
            }

            // connected components of foreground
            var comp = new int[w * h]; // 0 = none
            var areas = new List<int>(); areas.Add(0);
            int nc = 0;
            for (int s = 0; s < w * h; s++)
            {
                if (isBg[s] || comp[s] != 0) continue;
                nc++; areas.Add(0);
                var st = new Stack<int>(); st.Push(s); comp[s] = nc;
                while (st.Count > 0)
                {
                    int idx = st.Pop(); areas[nc]++;
                    int cx = idx % w, cy = idx / w;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = cx + dx[d], ny = cy + dy[d];
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int ni = ny * w + nx;
                        if (isBg[ni] || comp[ni] != 0) continue;
                        comp[ni] = nc; st.Push(ni);
                    }
                }
            }
            int largest = 0; foreach (var a in areas) if (a > largest) largest = a;
            int dropped = 0;
            for (int s = 0; s < w * h; s++)
            {
                if (isBg[s]) continue;
                if (areas[comp[s]] < largest * minCompFrac) { isBg[s] = true; dropped++; }
            }

            // apply alpha + soften edge: pixels adjacent to bg get partial alpha based on palette distance
            int minX = w, maxX = -1, minY = h, maxY = -1;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                int s = y * w + x, i = y * stride + x * 4;
                if (isBg[s]) { p[i + 3] = 0; continue; }
                if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y;
            }
            if (maxX < 0) return "EMPTY after removal";

            // edge feather: 1px ring next to bg -> alpha 60%
            var alphaOut = new byte[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                int s = y * w + x; if (isBg[s]) continue;
                bool edge = false;
                for (int d = 0; d < 4 && !edge; d++) { int nx = x + dx[d], ny = y + dy[d]; if (nx < 0 || ny < 0 || nx >= w || ny >= h || isBg[ny * w + nx]) edge = true; }
                if (edge) alphaOut[s] = 1;
            }
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) if (alphaOut[y * w + x] == 1) p[y * stride + x * 4 + 3] = 150;

            using (var full = FromBytes(p, w, h, stride))
            {
                Rectangle srcRect;
                if (crop)
                {
                    int padX = (maxX - minX + 1) * padPct / 100, padY = (maxY - minY + 1) * padPct / 100;
                    int x0 = Math.Max(0, minX - padX), y0 = Math.Max(0, minY - padY);
                    int x1 = Math.Min(w - 1, maxX + padX), y1 = Math.Min(h - 1, maxY + padY);
                    srcRect = new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
                }
                else srcRect = new Rectangle(0, 0, w, h);

                int tw, th;
                if (outW > 0 && outH > 0) { tw = outW; th = outH; }
                else
                {
                    double sc = Math.Min(1.0, (double)maxSide / Math.Max(srcRect.Width, srcRect.Height));
                    tw = Math.Max(1, (int)Math.Round(srcRect.Width * sc)); th = Math.Max(1, (int)Math.Round(srcRect.Height * sc));
                }
                using (var outBmp = new Bitmap(tw, th, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(outBmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.Clear(Color.Transparent);
                    using (var ia = new ImageAttributes()) { ia.SetWrapMode(WrapMode.TileFlipXY);
                        g.DrawImage(full, new Rectangle(0, 0, tw, th), srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height, GraphicsUnit.Pixel, ia); }
                    outBmp.Save(dst, ImageFormat.Png);
                }
                string ps = ""; foreach (var c in pal) ps += string.Format("({0},{1},{2}) ", c[0], c[1], c[2]);
                return string.Format("pal {0}| comps {1} largest {2} dropped {3}px | bbox x{4}-{5} y{6}-{7} | out {8}x{9}", ps, nc, largest, dropped, minX, maxX, minY, maxY, tw, th);
            }
        }
    }

    // Draw a garland string through the top of each opaque component (stars), left to right.
    public static string DrawString(string src, string dst, int lineWidth, int r, int g, int b, int extraTopPad)
    {
        using (var bmp0 = new Bitmap(src))
        {
            int w = bmp0.Width, h = bmp0.Height;
            int stride; var p = GetBytes(bmp0, out stride);
            var comp = new int[w * h]; int nc = 0;
            var tops = new List<double[]>(); // x, y, area
            int[] dx = { 1, -1, 0, 0 }, dy = { 0, 0, 1, -1 };
            for (int s = 0; s < w * h; s++)
            {
                if (p[(s / w) * stride + (s % w) * 4 + 3] < 40 || comp[s] != 0) continue;
                nc++; var st = new Stack<int>(); st.Push(s); comp[s] = nc;
                int area = 0, minY = h, sumXTop = 0, cntTop = 0;
                while (st.Count > 0)
                {
                    int idx = st.Pop(); area++; int cx = idx % w, cy = idx / w;
                    if (cy < minY) { minY = cy; sumXTop = cx; cntTop = 1; } else if (cy == minY) { sumXTop += cx; cntTop++; }
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = cx + dx[d], ny = cy + dy[d];
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int ni = ny * w + nx;
                        if (comp[ni] != 0 || p[ny * stride + nx * 4 + 3] < 40) continue;
                        comp[ni] = nc; st.Push(ni);
                    }
                }
                tops.Add(new[] { (double)sumXTop / cntTop, minY, area });
            }
            // keep big components only (stars)
            double maxA = 0; foreach (var t in tops) if (t[2] > maxA) maxA = t[2];
            var pts = new List<PointF>();
            foreach (var t in tops) if (t[2] >= maxA * 0.3) pts.Add(new PointF((float)t[0], (float)t[1]));
            pts.Sort((a, c) => a.X.CompareTo(c.X));
            if (pts.Count < 2) return "not enough stars: " + pts.Count;

            using (var outBmp = new Bitmap(w, h + extraTopPad, PixelFormat.Format32bppArgb))
            using (var gr = Graphics.FromImage(outBmp))
            {
                gr.Clear(Color.Transparent);
                gr.SmoothingMode = SmoothingMode.AntiAlias;
                // extend curve slightly beyond first/last star
                var curve = new List<PointF>();
                var f0 = pts[0]; var f1 = pts[1]; var l0 = pts[pts.Count - 1]; var l1 = pts[pts.Count - 2];
                curve.Add(new PointF(f0.X - (f1.X - f0.X) * 0.35f, f0.Y - (f1.Y - f0.Y) * 0.35f + extraTopPad));
                foreach (var pt in pts) curve.Add(new PointF(pt.X, pt.Y + extraTopPad + 2));
                curve.Add(new PointF(l0.X + (l0.X - l1.X) * 0.35f, l0.Y + (l0.Y - l1.Y) * 0.35f + extraTopPad));
                using (var pen = new Pen(Color.FromArgb(255, r, g, b), lineWidth)) { pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; gr.DrawCurve(pen, curve.ToArray(), 0.5f); }
                gr.DrawImage(bmp0, 0, extraTopPad, w, h);
                outBmp.Save(dst, ImageFormat.Png);
            }
            return "stars " + pts.Count + " string drawn, out " + w + "x" + (h + extraTopPad);
        }
    }

    // Contact sheet on magenta so transparency is visible
    public static void Sheet(string[] files, string dst, int cols, int tw, int th)
    {
        int rows = (files.Length + cols - 1) / cols;
        using (var sheet = new Bitmap(cols * tw, rows * th))
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.Magenta); g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var font = new Font("Arial", 9);
            for (int i = 0; i < files.Length; i++)
            {
                using (var b = new Bitmap(files[i]))
                {
                    int x = (i % cols) * tw, y = (i / cols) * th;
                    double s = Math.Min((tw - 10.0) / b.Width, (th - 30.0) / b.Height);
                    g.DrawImage(b, x + 5, y + 5, (int)(b.Width * s), (int)(b.Height * s));
                    g.DrawString(i + " " + System.IO.Path.GetFileName(files[i]) + " " + b.Width + "x" + b.Height, font, Brushes.Black, x + 2, y + th - 22);
                }
            }
            sheet.Save(dst, ImageFormat.Png);
        }
    }
}
