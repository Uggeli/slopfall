// The DaggerfallConnect rendering readers (compiled into Sim.AssetExport) call
// internal helpers on Sim.Data's BsaFile/FileProxy (e.g. GetRecordProxy).
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Sim.AssetExport")]
