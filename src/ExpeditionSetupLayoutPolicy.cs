namespace ErenshorFollow
{
    // Unity-free sizing constants for the Expedition setup destination list, mirroring the compact
    // MMO row metric SimActionMenuLayoutPolicy already established for Sim Actions. Kept in its own
    // Unity-free file (rather than as literals inside ExpeditionSetupWindow) so a deterministic test
    // can assert the target row height without pulling Unity assemblies into the test suite.
    internal static class ExpeditionSetupLayoutPolicy
    {
        internal const float DestinationRowHeight = 30f;
        internal const float SectionRowHeight = 22f;
        internal const float RowSpacing = 3f;
    }
}
