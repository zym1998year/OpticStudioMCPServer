using ZOSAPI.Analysis.Data;

namespace ZemaxMCP.Server.Tools.Analysis;

/// <summary>
/// Generates BMP files from ZOSAPI analysis data grids.
/// ZOSAPI standalone mode has no built-in image export; this helper
/// renders DataGrid results as grayscale BMP images.
/// </summary>
internal static class AnalysisBmpHelper
{
    /// <summary>
    /// Try to export analysis results as a BMP image.
    /// Returns true if a BMP was written, false if no renderable data grid exists.
    /// </summary>
    internal static bool TryExportBmp(IAR_ results, string imagePath)
    {
        try
        {
            if (results.NumberOfDataGrids > 0)
            {
                var grid = results.GetDataGrid(0);
                return WriteDataGridAsBmp(grid, imagePath);
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool WriteDataGridAsBmp(IAR_DataGrid grid, string path)
    {
        int width = (int)grid.Nx;
        int height = (int)grid.Ny;
        if (width <= 0 || height <= 0)
            return false;

        // Find value range for normalization
        double min = double.MaxValue, max = double.MinValue;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                double v = grid.Z(x, y);
                if (v < min) min = v;
                if (v > max) max = v;
            }

        double range = max - min;
        if (range <= 0) range = 1;

        // BMP geometry: 24-bit RGB, rows padded to 4-byte boundary
        int rowBytes = (width * 3 + 3) & ~3;
        int pixelDataSize = rowBytes * height;
        int fileSize = 54 + pixelDataSize;

        using var fs = new FileStream(path, FileMode.Create);
        using var bw = new BinaryWriter(fs);

        // ---- BMP file header (14 bytes) ----
        bw.Write((byte)'B');
        bw.Write((byte)'M');
        bw.Write(fileSize);
        bw.Write(0);           // reserved
        bw.Write(54);          // pixel data offset

        // ---- DIB header / BITMAPINFOHEADER (40 bytes) ----
        bw.Write(40);          // header size
        bw.Write(width);
        bw.Write(height);      // positive = bottom-up row order
        bw.Write((short)1);    // color planes
        bw.Write((short)24);   // bits per pixel
        bw.Write(0);           // no compression
        bw.Write(pixelDataSize);
        bw.Write(3780);        // horizontal resolution (pixels/m)
        bw.Write(3780);        // vertical resolution
        bw.Write(0);           // palette colors
        bw.Write(0);           // important colors

        // ---- Pixel data (bottom-to-top) ----
        byte[] row = new byte[rowBytes];
        for (int y = height - 1; y >= 0; y--)
        {
            Array.Clear(row, 0, row.Length);
            for (int x = 0; x < width; x++)
            {
                double normalized = (grid.Z(x, y) - min) / range;
                HotColormap(normalized, out byte r, out byte g, out byte b);
                int off = x * 3;
                row[off] = b;
                row[off + 1] = g;
                row[off + 2] = r;
            }
            bw.Write(row);
        }

        return true;
    }

    /// <summary>
    /// "Hot" colormap: black → red → yellow → white.
    /// Produces visually informative irradiance/intensity images.
    /// </summary>
    private static void HotColormap(double t, out byte r, out byte g, out byte b)
    {
        if (t < 0.0) t = 0.0;
        else if (t > 1.0) t = 1.0;

        if (t < 1.0 / 3.0)
        {
            // black → red
            double s = t * 3.0;
            r = (byte)(s * 255);
            g = 0;
            b = 0;
        }
        else if (t < 2.0 / 3.0)
        {
            // red → yellow
            double s = (t - 1.0 / 3.0) * 3.0;
            r = 255;
            g = (byte)(s * 255);
            b = 0;
        }
        else
        {
            // yellow → white
            double s = (t - 2.0 / 3.0) * 3.0;
            r = 255;
            g = 255;
            b = (byte)(s * 255);
        }
    }
}
