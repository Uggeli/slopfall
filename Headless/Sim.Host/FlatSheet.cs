using System;
using System.IO;
using Sim.AssetExport;

namespace DaggerfallWorkshop.Sim.Host
{
    /// Dumps one TEXTURE archive's flat atlas to a PNG file (no web server needed),
    /// for visually authoring the SpeciesCatalog. Cells are record order, 8 per row.
    public static class FlatSheet
    {
        public static int Run(int archive, string outPng)
        {
            if (string.IsNullOrEmpty(outPng)) { Console.Error.WriteLine("usage: --flatsheet <archive> <out.png>"); return 2; }
            var (png, meta) = SpriteFlat.Build(SimBoot.DefaultArena2Path, archive);
            File.WriteAllBytes(outPng, png);
            Console.WriteLine($"archive {archive}: {meta.Count} records, sheet {meta.SheetW}x{meta.SheetH}, {meta.Cols} cols -> {outPng}");
            return 0;
        }
    }
}
