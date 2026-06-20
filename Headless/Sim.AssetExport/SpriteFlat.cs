using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;

namespace Sim.AssetExport
{
    public struct FlatCell { public int U, V, W, H; public float WorldW, WorldH; }

    public sealed class FlatMeta
    {
        public int Archive;
        public int SheetW, SheetH;
        public int Count;
        public int Cols;
        public FlatCell[] Cells;
    }

    /// Packs every record of a TEXTURE archive into a single static atlas (one cell
    /// per record, record order, 8 columns). Used for nature/decorative flat billboards
    /// and as the controller's visual catalog-authoring sheet. Unlike SpritePerson there
    /// is no animation — one frame (0) per record.
    public static class SpriteFlat
    {
        const int Cols = 8;
        const float GlobalScale = 0.025f;

        public static (byte[] png, FlatMeta meta) Build(string arena2, int archive)
        {
            var tex = new TextureFile(System.IO.Path.Combine(arena2, TextureFile.IndexToFileName(archive)), FileUsage.UseMemory, true);
            tex.LoadPalette(System.IO.Path.Combine(arena2, tex.PaletteName));
            int count = tex.RecordCount;

            // Decode each record frame 0; track max cell size.
            var rgbas = new byte[count][];
            var ws = new int[count]; var hs = new int[count];
            int cellW = 1, cellH = 1;
            for (int i = 0; i < count; i++)
            {
                try
                {
                    DFBitmap bmp = tex.GetDFBitmap(i, 0);
                    rgbas[i] = TextureDecode.Rgba(bmp, tex, out int w, out int h);
                    ws[i] = w; hs[i] = h;
                    if (w > cellW) cellW = w;
                    if (h > cellH) cellH = h;
                }
                catch { rgbas[i] = null; ws[i] = 0; hs[i] = 0; }
            }

            int rows = (count + Cols - 1) / Cols;
            int sheetW = Cols * cellW, sheetH = rows * cellH;
            var sheet = new byte[sheetW * sheetH * 4];   // transparent (zeroed)
            var cells = new FlatCell[count];

            for (int i = 0; i < count; i++)
            {
                int cx = (i % Cols) * cellW, cy = (i / Cols) * cellH;
                int w = ws[i], h = hs[i];
                // bottom-align within the cell (feet/base on cell floor), left-align
                int ox = cx, oy = cy + (cellH - h);
                if (rgbas[i] != null)
                    for (int y = 0; y < h; y++)
                        Array.Copy(rgbas[i], y * w * 4, sheet, ((oy + y) * sheetW + ox) * 4, w * 4);
                cells[i] = new FlatCell { U = ox, V = oy, W = w, H = h, WorldW = w * GlobalScale, WorldH = h * GlobalScale };
            }

            var meta = new FlatMeta { Archive = archive, SheetW = sheetW, SheetH = sheetH, Count = count, Cols = Cols, Cells = cells };
            return (Png.Encode(sheetW, sheetH, sheet), meta);
        }
    }
}
