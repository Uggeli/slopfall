using System;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;

namespace DaggerfallWorkshop.Sim.Host
{
    /// Headless runner for the CQRS sim engine.
    ///   --probe [region [location]]            inspect ARENA2 data
    ///   --enginedemo                            the toy CQRS loop proof
    ///   --enginesmoke [ticks]                   empty-world engine smoke test
    ///   --engineworld region location [ticks]   load a town, run it, verify parallel==serial
    ///   --soak region location [days]           run a town on the parallel engine, report
    ///   --soakregion region [days]              run a whole region on the parallel engine
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length == 0) { Usage(); return 1; }

            switch (args[0])
            {
                case "--probe":
                    return DataProbe.Run(args.Length > 1 ? args[1] : null, args.Length > 2 ? args[2] : null);

                case "--gatecheck":
                    return GateDiag.Run(args.Length > 1 ? args[1] : null, args.Length > 2 ? args[2] : null);

                case "--flatsheet":
                    return DaggerfallWorkshop.Sim.Host.FlatSheet.Run(
                        args.Length > 1 ? int.Parse(args[1]) : 0,
                        args.Length > 2 ? args[2] : null);

                case "--poicheck":
                    return DaggerfallWorkshop.Sim.Host.PoiCheck.Run(args.Length > 1 ? args[1] : null);

                case "--enginedemo":
                    return DaggerfallWorkshop.Sim.Engine.Demo.EngineDemo.Run();

                case "--enginesmoke":
                    return EngineSmoke.Run(args.Length > 1 ? int.Parse(args[1]) : 1000);

                case "--engineworld":
                    return EngineWorld(args.Length > 1 ? args[1] : "Daggerfall",
                        args.Length > 2 ? args[2] : "Gothway Garden",
                        args.Length > 3 ? int.Parse(args[3]) : 20000);

                case "--soak":
                {
                    var w = SimBoot.CreateTown(SimBoot.DefaultArena2Path, args[1], args[2], 600f, 12345);
                    return EngineSoak.Run(w, args.Length > 3 ? int.Parse(args[3]) : 1);
                }

                case "--soakregion":
                {
                    var w = SimBoot.CreateRegion(SimBoot.DefaultArena2Path, args[1], 600f, 12345);
                    return EngineSoak.Run(w, args.Length > 2 ? int.Parse(args[2]) : 1);
                }

                default:
                    Usage();
                    return 1;
            }
        }

        /// Load a town, run it parallel + serial from the same seed, verify identical.
        static int EngineWorld(string region, string location, int ticks)
        {
            Console.WriteLine($"loading {region}/{location}…");
            var boot = SimBoot.CreateTown(SimBoot.DefaultArena2Path, region, location, 600f, 12345);
            // boot is one parallel world; seed a serial twin from the same source for the diff.
            var par = boot;
            var ser = SimBoot.CreateTown(SimBoot.DefaultArena2Path, region, location, 600f, 12345);
            Console.WriteLine("seeded: " + SimWorldBridge.Fingerprint(par));
            for (int i = 0; i < ticks; i++) { par.Step(); ser.StepSerial(); }
            string fp = SimWorldBridge.Fingerprint(par), fs = SimWorldBridge.Fingerprint(ser);
            Console.WriteLine($"after {ticks} ticks:\n  parallel: {fp}\n  serial:   {fs}");
            Console.WriteLine(fp == fs ? "\nDETERMINISTIC — parallel == serial OK" : "\nMISMATCH — parallel != serial");
            return fp == fs ? 0 : 1;
        }

        static void Usage() => Console.WriteLine(
            "usage: --probe | --poicheck region | --enginedemo | --enginesmoke [ticks] | --engineworld R L [ticks] | --soak R L [days] | --soakregion R [days] | --gatecheck region location | --flatsheet archive out.png");
    }
}
