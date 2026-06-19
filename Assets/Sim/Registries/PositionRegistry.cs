using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    public sealed class PositionData
    {
        public float X, Y, Z;
        public float Yaw;
        public override string ToString() => "(" + X.ToString("F1") + "," + Y.ToString("F1") + "," + Z.ToString("F1") + " yaw=" + Yaw.ToString("F0") + ")";
    }
}
