// Converts a DaggerfallConnect DFMesh into engine-neutral primitives ready for
// glTF export. One Primitive per submesh (each submesh has a single texture).
//
// Coordinate conventions (mirrors DFU's MeshReader.LoadVertices):
//   position = (X, -Y, Z) * GlobalScale   (Daggerfall Y is down)
//   normal   = normalize(NX, -NY, NZ)
//   uv       = (U / texWidth, V / texHeight)   (DF U/V are absolute pixels)
// Planes are triangle fans radiating from point 0.
//
// NOTE (orientation): glTF is right-handed, +Y up, UV origin top-left, front
// faces CCW. We export with the DFU transform above + REPEAT wrap and render
// double-sided in M2; if the model shows up mirrored / inside-out / V-flipped,
// the single knobs to flip are FlipV and the winding order below — verified
// visually in the Three.js step (M2), kept isolated here on purpose.

using System;
using System.Collections.Generic;
using DaggerfallConnect;

namespace Sim.AssetExport
{
    public struct Vtx
    {
        public float px, py, pz;
        public float nx, ny, nz;
        public float u, v;
    }

    public sealed class Primitive
    {
        public int TextureArchive;
        public int TextureRecord;
        public readonly List<Vtx> Verts = new List<Vtx>();
        public readonly List<uint> Indices = new List<uint>();
    }

    public static class MeshExtract
    {
        // DFU's default model scale: native Daggerfall units -> metres.
        public const float GlobalScale = 0.025f;

        // Flip V to match glTF's top-left UV origin if needed (see header note).
        public static bool FlipV = false;

        /// <summary>
        /// Decompose a DFMesh into one Primitive per textured submesh.
        /// <paramref name="texSize"/> returns (width,height) in pixels for a
        /// (textureArchive, textureRecord) pair; pass (0,0) when unknown.
        /// </summary>
        public static List<Primitive> FromDFMesh(DFMesh mesh, Func<int, int, (int w, int h)> texSize)
        {
            var prims = new List<Primitive>();
            if (mesh.SubMeshes == null)
                return prims;

            foreach (var sm in mesh.SubMeshes)
            {
                var (tw, th) = texSize(sm.TextureArchive, sm.TextureRecord);
                if (tw <= 0) tw = 1;
                if (th <= 0) th = 1;

                var prim = new Primitive
                {
                    TextureArchive = sm.TextureArchive,
                    TextureRecord = sm.TextureRecord,
                };

                if (sm.Planes != null)
                {
                    foreach (var plane in sm.Planes)
                    {
                        var pts = plane.Points;
                        if (pts == null || pts.Length < 3)
                            continue;

                        uint baseIndex = (uint)prim.Verts.Count;
                        foreach (var p in pts)
                        {
                            float nx = p.NX, ny = -p.NY, nz = p.NZ;
                            float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                            if (len > 1e-8f) { nx /= len; ny /= len; nz /= len; }

                            prim.Verts.Add(new Vtx
                            {
                                px = p.X * GlobalScale,
                                py = -p.Y * GlobalScale,
                                pz = p.Z * GlobalScale,
                                nx = nx, ny = ny, nz = nz,
                                u = p.U / tw,
                                v = FlipV ? 1f - (p.V / th) : (p.V / th),
                            });
                        }

                        // Triangle fan: (0,1,2),(0,2,3),...,(0,k-2,k-1)
                        for (int i = 1; i + 1 < pts.Length; i++)
                        {
                            prim.Indices.Add(baseIndex);
                            prim.Indices.Add(baseIndex + (uint)i);
                            prim.Indices.Add(baseIndex + (uint)i + 1);
                        }
                    }
                }

                if (prim.Indices.Count > 0)
                    prims.Add(prim);
            }

            return prims;
        }
    }
}
