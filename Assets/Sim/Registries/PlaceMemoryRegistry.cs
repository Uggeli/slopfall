namespace DaggerfallWorkshop.Sim
{
    /// A remembered fact-type about a place — one atom in a PLACES record (Atoms
    /// what_is_memory). The first is whether a larder/shelf had provisions, which
    /// lets a hungry agent recall "nothing at home" and not even consider eating
    /// there. (Data types for the PLACES store; the store itself is the Engine
    /// PlaceMemoryRegistry.)
    public enum PlaceFact
    {
        ProvisionsHere = 0,   // did this place have food when I last saw it? (>0 yes, 0 none)
    }

    /// The remembered value for a PlaceFact (was the nested PlaceMemoryRegistry.Fact).
    public struct PlaceFactValue { public double Value; public long AsOfTick; }
}
