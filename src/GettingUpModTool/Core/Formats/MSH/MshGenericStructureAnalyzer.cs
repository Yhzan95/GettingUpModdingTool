using GettingUpModTool.Core.Models;

namespace GettingUpModTool.Core.Formats.MSH;

public static class MshGenericStructureAnalyzer
{
    public static MshStructureAnalysis Analyze(byte[] data, SkeletonData? skeleton = null)
    {
        MshStructureAnalysis skinned = MshStructureAnalyzer.Analyze(data, skeleton);
        if (skinned.Sections.Count > 0)
            return skinned;

        return MshStaticStructureAnalyzer.Analyze(data);
    }
}
