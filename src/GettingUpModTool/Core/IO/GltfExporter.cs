using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GettingUpModTool.Core.Models;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GettingUpModTool.Core.IO;

public static class GltfExporter
{
    public static void Export(MeshData mesh, SkeletonData? skeleton, string path, IReadOnlyDictionary<int, byte[]>? sectionTexturePngs = null, bool visibleOnly = false)
    {
        if (mesh.Positions.Count == 0)
            throw new InvalidOperationException("Le mesh ne contient aucun vertex.");
        if (mesh.Indices.Count < 3)
            throw new InvalidOperationException("Le mesh ne contient aucun triangle.");

        using var buffer = new MemoryStream();
        var bufferViews = new List<Dictionary<string, object?>>();
        var accessors = new List<Dictionary<string, object?>>();

        int AddView(Action<BinaryWriter> write, int? target = null)
        {
            Align4(buffer);
            int offset = checked((int)buffer.Position);
            using (var bw = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
            {
                write(bw);
                bw.Flush();
            }
            int length = checked((int)buffer.Position - offset);
            var view = new Dictionary<string, object?>
            {
                ["buffer"] = 0,
                ["byteOffset"] = offset,
                ["byteLength"] = length
            };
            if (target.HasValue)
                view["target"] = target.Value;
            bufferViews.Add(view);
            return bufferViews.Count - 1;
        }

        int AddAccessor(int view, int componentType, int count, string type,
            float[]? min = null, float[]? max = null, bool normalized = false)
        {
            var a = new Dictionary<string, object?>
            {
                ["bufferView"] = view,
                ["byteOffset"] = 0,
                ["componentType"] = componentType,
                ["count"] = count,
                ["type"] = type
            };
            if (min is not null) a["min"] = min;
            if (max is not null) a["max"] = max;
            if (normalized) a["normalized"] = true;
            accessors.Add(a);
            return accessors.Count - 1;
        }

        Vector3 minPos = mesh.Positions[0];
        Vector3 maxPos = mesh.Positions[0];
        foreach (var p in mesh.Positions)
        {
            minPos = Vector3.Min(minPos, p);
            maxPos = Vector3.Max(maxPos, p);
        }

        int posView = AddView(bw =>
        {
            foreach (var p in mesh.Positions)
            {
                bw.Write(p.X); bw.Write(p.Y); bw.Write(p.Z);
            }
        }, 34962);
        int posAccessor = AddAccessor(posView, 5126, mesh.Positions.Count, "VEC3",
            [minPos.X, minPos.Y, minPos.Z], [maxPos.X, maxPos.Y, maxPos.Z]);

        int? normalAccessor = null;
        if (mesh.Normals.Count == mesh.Positions.Count)
        {
            int view = AddView(bw =>
            {
                foreach (var n in mesh.Normals)
                {
                    bw.Write(n.X); bw.Write(n.Y); bw.Write(n.Z);
                }
            }, 34962);
            normalAccessor = AddAccessor(view, 5126, mesh.Normals.Count, "VEC3");
        }

        int? uvAccessor = null;
        if (mesh.UVs.Count == mesh.Positions.Count)
        {
            int view = AddView(bw =>
            {
                foreach (var uv in mesh.UVs)
                {
                    
                    
                    
                    bw.Write(uv.X); bw.Write(uv.Y);
                }
            }, 34962);
            uvAccessor = AddAccessor(view, 5126, mesh.UVs.Count, "VEC2");
        }

        bool skinValid = skeleton is not null && skeleton.Bones.Count > 0 && mesh.HasSkinning &&
            mesh.Skinning.All(s => s.Influences().All(x => x.Joint >= 0 && x.Joint < skeleton.Bones.Count));

        int? jointsAccessor = null;
        int? weightsAccessor = null;
        if (skinValid)
        {
            int jointsView = AddView(bw =>
            {
                foreach (var s in mesh.Skinning)
                {
                    bw.Write((ushort)s.Joint0);
                    bw.Write((ushort)s.Joint1);
                    bw.Write((ushort)s.Joint2);
                    bw.Write((ushort)s.Joint3);
                }
            }, 34962);
            jointsAccessor = AddAccessor(jointsView, 5123, mesh.Skinning.Count, "VEC4");

            int weightsView = AddView(bw =>
            {
                foreach (var s in mesh.Skinning)
                {
                    bw.Write(s.Weight0); bw.Write(s.Weight1); bw.Write(s.Weight2); bw.Write(s.Weight3);
                }
            }, 34962);
            weightsAccessor = AddAccessor(weightsView, 5126, mesh.Skinning.Count, "VEC4");
        }

        int AddIndexAccessor(IReadOnlyList<int> indices)
        {
            int componentType;
            int view;
            if (indices.Count == 0)
                throw new InvalidOperationException("Primitive glTF sans indices.");

            if (indices.Max() <= ushort.MaxValue)
            {
                componentType = 5123;
                view = AddView(bw =>
                {
                    foreach (int i in indices)
                        bw.Write((ushort)i);
                }, 34963);
            }
            else
            {
                componentType = 5125;
                view = AddView(bw =>
                {
                    foreach (int i in indices)
                        bw.Write((uint)i);
                }, 34963);
            }
            return AddAccessor(view, componentType, indices.Count, "SCALAR");
        }

        int? inverseBindAccessor = null;
        if (skinValid && skeleton is not null)
        {
            int view = AddView(bw =>
            {
                foreach (var bone in skeleton.Bones)
                    WriteMatrix(bw, bone.InverseBindMatrix);
            });
            inverseBindAccessor = AddAccessor(view, 5126, skeleton.Bones.Count, "MAT4");
        }

        var attributes = new Dictionary<string, object?> { ["POSITION"] = posAccessor };
        if (normalAccessor.HasValue) attributes["NORMAL"] = normalAccessor.Value;
        if (uvAccessor.HasValue) attributes["TEXCOORD_0"] = uvAccessor.Value;
        if (jointsAccessor.HasValue) attributes["JOINTS_0"] = jointsAccessor.Value;
        if (weightsAccessor.HasValue) attributes["WEIGHTS_0"] = weightsAccessor.Value;

        var images = new List<object>();
        var textures = new List<object>();
        var materials = new List<object>();
        var materialBySection = new Dictionary<int, int>();

        if (sectionTexturePngs is not null && sectionTexturePngs.Count > 0)
        {
            foreach (var pair in sectionTexturePngs.OrderBy(x => x.Key))
            {
                int sectionIndex = pair.Key;
                byte[] png = pair.Value;
                int imageIndex = images.Count;
                images.Add(new Dictionary<string, object?>
                {
                    ["name"] = $"Section_{sectionIndex}_Texture",
                    ["uri"] = "data:image/png;base64," + Convert.ToBase64String(png)
                });

                int textureIndex = textures.Count;
                textures.Add(new Dictionary<string, object?>
                {
                    ["sampler"] = 0,
                    ["source"] = imageIndex
                });

                MeshPart? part = mesh.Parts.FirstOrDefault(p => p.SectionIndex == sectionIndex);
                string materialName = part?.MaterialName ?? $"Section {sectionIndex}";
                bool hasTransparency = PngUsesVisibleTransparency(png);
                var material = new Dictionary<string, object?>
                {
                    ["name"] = materialName,
                    ["doubleSided"] = true,
                    ["alphaMode"] = hasTransparency ? "BLEND" : "OPAQUE",
                    ["pbrMetallicRoughness"] = new Dictionary<string, object?>
                    {
                        ["baseColorTexture"] = new Dictionary<string, object?> { ["index"] = textureIndex },
                        ["metallicFactor"] = 0.0,
                        ["roughnessFactor"] = 1.0
                    }
                };
                
                materialBySection[sectionIndex] = materials.Count;
                materials.Add(material);
            }
        }

        
        
        foreach (int sectionIndex in mesh.Parts.Select(p => p.SectionIndex).Distinct().OrderBy(x => x))
        {
            if (materialBySection.ContainsKey(sectionIndex))
                continue;
            MeshPart? part = mesh.Parts.FirstOrDefault(p => p.SectionIndex == sectionIndex);
            string materialName = string.IsNullOrWhiteSpace(part?.MaterialName) ? $"Section {sectionIndex}" : part!.MaterialName;
            materialBySection[sectionIndex] = materials.Count;
            materials.Add(new Dictionary<string, object?>
            {
                ["name"] = materialName,
                ["doubleSided"] = true,
                ["alphaMode"] = "OPAQUE",
                ["pbrMetallicRoughness"] = new Dictionary<string, object?>
                {
                    ["baseColorFactor"] = new[] { 1.0, 1.0, 1.0, 1.0 },
                    ["metallicFactor"] = 0.0,
                    ["roughnessFactor"] = 1.0
                }
            });
        }

        var primitives = new List<object>();
        if (mesh.Parts.Count > 0)
        {
            foreach (MeshPart part in mesh.Parts.Where(p => (!visibleOnly || p.IsVisible) && p.IndexCount >= 3))
            {
                int start = Math.Clamp(part.IndexStart, 0, mesh.Indices.Count);
                int count = Math.Clamp(part.IndexCount, 0, mesh.Indices.Count - start);
                count -= count % 3;
                if (count < 3)
                    continue;

                List<int> partIndices = mesh.Indices.GetRange(start, count);
                int partIndexAccessor = AddIndexAccessor(partIndices);
                var primitive = new Dictionary<string, object?>
                {
                    ["attributes"] = attributes,
                    ["indices"] = partIndexAccessor,
                    ["mode"] = 4
                };
                if (materialBySection.TryGetValue(part.SectionIndex, out int materialIndex))
                    primitive["material"] = materialIndex;
                primitives.Add(primitive);
            }
        }

        if (primitives.Count == 0)
        {
            if (visibleOnly)
                throw new InvalidOperationException("Aucune section visible à exporter.");
            int indexAccessor = AddIndexAccessor(mesh.Indices);
            primitives.Add(new Dictionary<string, object?>
            {
                ["attributes"] = attributes,
                ["indices"] = indexAccessor,
                ["mode"] = 4
            });
        }

        var meshes = new List<object>
        {
            new Dictionary<string, object?>
            {
                ["name"] = Path.GetFileNameWithoutExtension(path),
                ["primitives"] = primitives
            }
        };

        var nodes = new List<Dictionary<string, object?>>();
        var sceneRoots = new List<int>();
        var skins = new List<object>();

        if (skinValid && skeleton is not null)
        {
            for (int i = 0; i < skeleton.Bones.Count; i++)
            {
                var bone = skeleton.Bones[i];
                Matrix4x4 local = bone.BindWorldMatrix;
                if (bone.ParentIndex >= 0 && bone.ParentIndex < skeleton.Bones.Count)
                {
                    Matrix4x4.Invert(skeleton.Bones[bone.ParentIndex].BindWorldMatrix, out var invParentWorld);
                    local = bone.BindWorldMatrix * invParentWorld;
                }

                nodes.Add(new Dictionary<string, object?>
                {
                    ["name"] = bone.Name,
                    ["matrix"] = MatrixToArray(local)
                });
            }

            for (int i = 0; i < skeleton.Bones.Count; i++)
            {
                var children = skeleton.Bones.Where(b => b.ParentIndex == i).Select(b => b.Index).ToArray();
                if (children.Length > 0)
                    nodes[i]["children"] = children;
                if (skeleton.Bones[i].ParentIndex < 0)
                    sceneRoots.Add(i);
            }

            int rootIndex = skeleton.Bones.FindIndex(b => b.ParentIndex < 0);
            if (rootIndex < 0) rootIndex = 0;
            skins.Add(new Dictionary<string, object?>
            {
                ["name"] = "GettingUpSkeleton",
                ["joints"] = Enumerable.Range(0, skeleton.Bones.Count).ToArray(),
                ["inverseBindMatrices"] = inverseBindAccessor!.Value,
                ["skeleton"] = rootIndex
            });
        }

        int meshNodeIndex = nodes.Count;
        var meshNode = new Dictionary<string, object?>
        {
            ["name"] = Path.GetFileNameWithoutExtension(path),
            ["mesh"] = 0
        };
        if (skinValid)
            meshNode["skin"] = 0;
        nodes.Add(meshNode);
        sceneRoots.Add(meshNodeIndex);

        byte[] bufferBytes = buffer.ToArray();
        var root = new Dictionary<string, object?>
        {
            ["asset"] = new Dictionary<string, object?>
            {
                ["version"] = "2.0",
                ["generator"] = $"GettingUpModTool v{AppInfo.Version}"
            },
            ["scene"] = 0,
            ["scenes"] = new object[]
            {
                new Dictionary<string, object?> { ["nodes"] = sceneRoots.Distinct().ToArray() }
            },
            ["nodes"] = nodes,
            ["meshes"] = meshes,
            ["buffers"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["byteLength"] = bufferBytes.Length,
                    ["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String(bufferBytes)
                }
            },
            ["bufferViews"] = bufferViews,
            ["accessors"] = accessors
        };
        if (skins.Count > 0)
            root["skins"] = skins;
        if (materials.Count > 0)
            root["materials"] = materials;
        if (images.Count > 0)
        {
            root["samplers"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["magFilter"] = 9729,
                    ["minFilter"] = 9987,
                    ["wrapS"] = 10497,
                    ["wrapT"] = 10497
                }
            };
            root["images"] = images;
            root["textures"] = textures;
        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(root, options), new UTF8Encoding(false));
    }

    public static void ExportGlb(MeshData mesh, SkeletonData? skeleton, string path, IReadOnlyDictionary<int, byte[]>? sectionTexturePngs = null, bool visibleOnly = false)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "GettingUpModTool", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string tempGltf = Path.Combine(tempDir, Path.GetFileNameWithoutExtension(path) + ".gltf");
        try
        {
            Export(mesh, skeleton, tempGltf, sectionTexturePngs, visibleOnly);
            JsonObject root = JsonNode.Parse(File.ReadAllText(tempGltf, Encoding.UTF8))?.AsObject()
                ?? throw new InvalidDataException("Document glTF vide.");

            JsonArray buffers = root["buffers"]?.AsArray()
                ?? throw new InvalidDataException("Buffer glTF manquant.");
            JsonObject buffer0 = buffers[0]?.AsObject()
                ?? throw new InvalidDataException("Buffer glTF invalide.");
            string? bufferUri = buffer0["uri"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(bufferUri) || !bufferUri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Buffer glTF non intégré.");

            byte[] geometry = DecodeDataUri(bufferUri);
            using var bin = new MemoryStream();
            bin.Write(geometry, 0, geometry.Length);

            JsonArray? bufferViews = root["bufferViews"] as JsonArray;
            if (bufferViews is null)
            {
                bufferViews = new JsonArray();
                root["bufferViews"] = bufferViews;
            }

            if (root["images"] is JsonArray images)
            {
                foreach (JsonNode? imageNode in images)
                {
                    if (imageNode is not JsonObject image)
                        continue;
                    string? uri = image["uri"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(uri) || !uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                        continue;

                    Align4(bin);
                    int offset = checked((int)bin.Position);
                    byte[] imageBytes = DecodeDataUri(uri);
                    bin.Write(imageBytes, 0, imageBytes.Length);
                    int viewIndex = bufferViews.Count;
                    bufferViews.Add(new JsonObject
                    {
                        ["buffer"] = 0,
                        ["byteOffset"] = offset,
                        ["byteLength"] = imageBytes.Length
                    });
                    image.Remove("uri");
                    image["bufferView"] = viewIndex;
                    image["mimeType"] = "image/png";
                }
            }

            Align4(bin);
            byte[] binBytes = bin.ToArray();
            buffer0.Remove("uri");
            buffer0["byteLength"] = binBytes.Length;

            byte[] json = Encoding.UTF8.GetBytes(root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
            int jsonPaddedLength = (json.Length + 3) & ~3;
            int binPaddedLength = (binBytes.Length + 3) & ~3;
            int totalLength = checked(12 + 8 + jsonPaddedLength + 8 + binPaddedLength);

            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
            writer.Write(0x46546C67u); 
            writer.Write(2u);
            writer.Write((uint)totalLength);
            writer.Write((uint)jsonPaddedLength);
            writer.Write(0x4E4F534Au); 
            writer.Write(json);
            for (int i = json.Length; i < jsonPaddedLength; i++)
                writer.Write((byte)0x20);

            writer.Write((uint)binPaddedLength);
            writer.Write(0x004E4942u); 
            writer.Write(binBytes);
            for (int i = binBytes.Length; i < binPaddedLength; i++)
                writer.Write((byte)0);
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[GettingUpModTool] Impossible de supprimer le dossier temporaire GLB '{tempDir}': {ex}");
            }
        }
    }

    private static byte[] DecodeDataUri(string uri)
    {
        int comma = uri.IndexOf(',');
        if (comma < 0 || comma + 1 >= uri.Length)
            throw new InvalidDataException("Data URI glTF invalide.");
        return Convert.FromBase64String(uri[(comma + 1)..]);
    }

    private static bool PngUsesVisibleTransparency(byte[] png)
    {
        try
        {
            using var stream = new MemoryStream(png, writable: false);
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource source = decoder.Frames[0];
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int stride = converted.PixelWidth * 4;
            byte[] pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            int nearTransparent = 0;
            int opaque = 0;
            int total = converted.PixelWidth * converted.PixelHeight;
            for (int i = 3; i < pixels.Length; i += 4)
            {
                byte alpha = pixels[i];
                if (alpha <= 16) nearTransparent++;
                if (alpha >= 240) opaque++;
            }

            
            
            
            
            int threshold = Math.Max(1, total / 200); 
            return nearTransparent >= threshold && opaque >= threshold;
        }
        catch (Exception ex)
        {
            
            Trace.WriteLine($"[GettingUpModTool] Analyse alpha PNG impossible; texture considérée opaque: {ex}");
        }
        return false;
    }

    private static void Align4(MemoryStream stream)
    {
        while ((stream.Position & 3) != 0)
            stream.WriteByte(0);
    }

    private static void WriteMatrix(BinaryWriter bw, Matrix4x4 m)
    {
        foreach (float v in MatrixToArray(m))
            bw.Write(v);
    }
    private static float[] MatrixToArray(Matrix4x4 m) =>
    [
        m.M11, m.M12, m.M13, m.M14,
        m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34,
        m.M41, m.M42, m.M43, m.M44
    ];
}
