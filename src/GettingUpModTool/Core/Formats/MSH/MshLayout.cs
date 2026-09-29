namespace GettingUpModTool.Core.Formats.MSH;

public enum IndexElementSize
{
    UInt16 = 2,
    UInt32 = 4
}

public enum PrimitiveMode
{
    TriangleList,
    TriangleStrip
}

public enum NormalEncoding
{
    Float3,
    PackedUnorm8
}

public sealed class MshLayout
{
    public int VertexOffset { get; set; }
    public int VertexCount { get; set; }
    public int VertexStride { get; set; } = 32;
    public int PositionOffset { get; set; }
    public int? NormalOffset { get; set; }
    public NormalEncoding NormalEncoding { get; set; } = NormalEncoding.Float3;
    public int? UVOffset { get; set; }

    
    public int? BoneWeightsOffset { get; set; }
    public int? BoneIndicesOffset { get; set; }
    public int BoneIndexDivisor { get; set; } = 1;

    public int? IndexOffset { get; set; }
    public int IndexCount { get; set; }
    public IndexElementSize IndexSize { get; set; } = IndexElementSize.UInt16;
    public PrimitiveMode PrimitiveMode { get; set; } = PrimitiveMode.TriangleList;
}
