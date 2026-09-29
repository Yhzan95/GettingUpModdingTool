using System.IO;
using System.Globalization;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GettingUpModTool.Core.Models;

namespace GettingUpModTool.Core.IO;

public static class ObjExporter
{
    public static void Export(MeshData mesh, string path, IReadOnlyDictionary<int, byte[]>? sectionTexturePngs = null)
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine($"# Exported by GettingUpModTool v{AppInfo.Version}");

        string objDir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? Environment.CurrentDirectory;
        string stem = Path.GetFileNameWithoutExtension(path);
        bool hasMaterials = mesh.Parts.Count > 0;
        if (hasMaterials)
            sb.AppendLine($"mtllib {stem}.mtl");

        foreach (var v in mesh.Positions)
            sb.AppendLine($"v {v.X.ToString(c)} {v.Y.ToString(c)} {v.Z.ToString(c)}");

        foreach (var uv in mesh.UVs)
            sb.AppendLine($"vt {uv.X.ToString(c)} {(1f - uv.Y).ToString(c)}");

        foreach (var n in mesh.Normals)
            sb.AppendLine($"vn {n.X.ToString(c)} {n.Y.ToString(c)} {n.Z.ToString(c)}");

        bool uvOk = mesh.UVs.Count == mesh.Positions.Count;
        bool normalOk = mesh.Normals.Count == mesh.Positions.Count;

        void WriteFaces(int start, int count)
        {
            int safeStart = Math.Clamp(start, 0, mesh.Indices.Count);
            int safeCount = Math.Clamp(count, 0, mesh.Indices.Count - safeStart);
            safeCount -= safeCount % 3;
            for (int i = safeStart; i + 2 < safeStart + safeCount; i += 3)
            {
                int a = mesh.Indices[i] + 1;
                int b = mesh.Indices[i + 1] + 1;
                int d = mesh.Indices[i + 2] + 1;
                if (a <= 0 || b <= 0 || d <= 0 || a > mesh.Positions.Count || b > mesh.Positions.Count || d > mesh.Positions.Count)
                    continue;
                sb.AppendLine($"f {Face(a, uvOk, normalOk)} {Face(b, uvOk, normalOk)} {Face(d, uvOk, normalOk)}");
            }
        }

        if (mesh.Parts.Count > 0)
        {
            foreach (MeshPart part in mesh.Parts.Where(p => p.IndexCount >= 3))
            {
                string material = MaterialName(part.SectionIndex);
                sb.AppendLine($"g Section_{part.SectionIndex}");
                sb.AppendLine($"usemtl {material}");
                WriteFaces(part.IndexStart, part.IndexCount);
            }
        }
        else
        {
            WriteFaces(0, mesh.Indices.Count);
        }

        Directory.CreateDirectory(objDir);
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));

        if (!hasMaterials)
            return;

        var mtl = new StringBuilder();
        mtl.AppendLine($"# Materials exported by GettingUpModTool v{AppInfo.Version}");
        foreach (int section in mesh.Parts.Select(p => p.SectionIndex).Distinct().OrderBy(x => x))
        {
            string material = MaterialName(section);
            mtl.AppendLine($"newmtl {material}");
            mtl.AppendLine("Ka 0.000 0.000 0.000");
            mtl.AppendLine("Kd 1.000 1.000 1.000");
            mtl.AppendLine("Ks 0.000 0.000 0.000");
            mtl.AppendLine("d 1.0");
            mtl.AppendLine("illum 1");
            if (sectionTexturePngs is not null && sectionTexturePngs.TryGetValue(section, out byte[]? png))
            {
                string textureName = $"{stem}_section_{section}.png";
                File.WriteAllBytes(Path.Combine(objDir, textureName), png);
                mtl.AppendLine($"map_Kd {textureName}");
                if (TryCreateAlphaMap(png, out byte[]? alphaPng) && alphaPng is not null)
                {
                    string alphaName = $"{stem}_section_{section}_alpha.png";
                    File.WriteAllBytes(Path.Combine(objDir, alphaName), alphaPng);
                    mtl.AppendLine($"map_d {alphaName}");
                }
            }
            mtl.AppendLine();
        }
        File.WriteAllText(Path.Combine(objDir, stem + ".mtl"), mtl.ToString(), new UTF8Encoding(false));
    }


    private static bool TryCreateAlphaMap(byte[] png, out byte[]? alphaPng)
    {
        alphaPng = null;
        try
        {
            using var input = new MemoryStream(png, writable: false);
            var decoder = new PngBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
            int width = converted.PixelWidth;
            int height = converted.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);

            int nearTransparent = 0;
            int opaque = 0;
            int total = width * height;
            for (int i = 3; i < pixels.Length; i += 4)
            {
                byte a = pixels[i];
                if (a <= 16) nearTransparent++;
                if (a >= 240) opaque++;
            }
            int threshold = Math.Max(1, total / 200); 
            if (nearTransparent < threshold || opaque < threshold)
                return false;

            byte[] gray = new byte[width * height];
            for (int src = 3, dst = 0; src < pixels.Length; src += 4, dst++)
                gray[dst] = pixels[src];
            BitmapSource mask = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray8, null, gray, width);
            mask.Freeze();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(mask));
            using var output = new MemoryStream();
            encoder.Save(output);
            alphaPng = output.ToArray();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string MaterialName(int sectionIndex) => $"Section_{sectionIndex}";

    private static string Face(int index, bool uv, bool normal)
        => (uv, normal) switch
        {
            (true, true) => $"{index}/{index}/{index}",
            (true, false) => $"{index}/{index}",
            (false, true) => $"{index}//{index}",
            _ => index.ToString(CultureInfo.InvariantCulture)
        };
}
