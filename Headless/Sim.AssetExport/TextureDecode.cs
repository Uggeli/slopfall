// Palette-applies a Daggerfall indexed texture record to top-down RGBA8.
// Opaque (alpha 255) — suits solid building models; flats/billboards (index-0
// transparency) come later in the entity-rendering milestone.

using DaggerfallConnect;
using DaggerfallConnect.Arena2;

namespace Sim.AssetExport
{
    public static class TextureDecode
    {
        /// <param name="transparentIndex0">true for sprites — palette index 0 is the
        /// transparent colour; false for ground/building textures (fully opaque).</param>
        public static byte[] Rgba(DFBitmap bmp, TextureFile tex, out int w, out int h, bool transparentIndex0 = false)
        {
            w = bmp.Width;
            h = bmp.Height;
            DFPalette pal = (bmp.Palette != null && bmp.Palette.PaletteBuffer != null) ? bmp.Palette : tex.Palette;
            var rgba = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++)
            {
                int idx = bmp.Data[i];
                int off = pal.HeaderLength + idx * 3;
                rgba[i * 4 + 0] = pal.PaletteBuffer[off];
                rgba[i * 4 + 1] = pal.PaletteBuffer[off + 1];
                rgba[i * 4 + 2] = pal.PaletteBuffer[off + 2];
                rgba[i * 4 + 3] = (transparentIndex0 && idx == 0) ? (byte)0 : (byte)255;
            }
            return rgba;
        }
    }
}
