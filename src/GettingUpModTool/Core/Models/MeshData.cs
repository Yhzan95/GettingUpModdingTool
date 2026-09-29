using System.Numerics;

namespace GettingUpModTool.Core.Models;

public readonly record struct VertexSkin(
    int Joint0, int Joint1, int Joint2, int Joint3,
    float Weight0, float Weight1, float Weight2, float Weight3)
{
    public IEnumerable<(int Joint, float Weight)> Influences()
    {
        if (Weight0 > 0) yield return (Joint0, Weight0);
        if (Weight1 > 0) yield return (Joint1, Weight1);
        if (Weight2 > 0) yield return (Joint2, Weight2);
        if (Weight3 > 0) yield return (Joint3, Weight3);
    }
}

public sealed class MeshPart
{
    public int SectionIndex { get; set; }
    public int IndexStart { get; set; }
    public int IndexCount { get; set; }
    public string RenderGroupName { get; set; } = "Base";
    public string MaterialName { get; set; } = "<inconnu>";
    public string TextureReference { get; set; } = "";
    public bool IsVisible { get; set; } = true;
}

public sealed class MeshData
{
    public List<Vector3> Positions { get; } = [];
    public List<Vector3> Normals { get; } = [];
    public List<Vector2> UVs { get; } = [];
    public List<int> Indices { get; } = [];
    public List<VertexSkin> Skinning { get; } = [];
    public List<MeshPart> Parts { get; } = [];

    public bool HasTriangles => Indices.Count >= 3;
    public bool HasSkinning => Skinning.Count == Positions.Count && Skinning.Count > 0;
}
