using System.Globalization;
using System.IO;
using System.Numerics;
using System.Xml.Linq;

namespace GettingUpModTool.Core.Formats.GAT;

public static class GatReader
{
    public static GatDocument Read(string path)
    {
        XDocument xml = XDocument.Load(path, LoadOptions.None);
        var result = new GatDocument { FileName = Path.GetFileName(path) };

        foreach (XElement attachment in xml.Descendants("Attachment"))
        {
            XElement? translation = attachment.Elements("vector3")
                .FirstOrDefault(x => string.Equals((string?)x.Attribute("id"), "Translation", StringComparison.OrdinalIgnoreCase));
            XElement? rotation = attachment.Elements("quat")
                .FirstOrDefault(x => string.Equals((string?)x.Attribute("id"), "Rotation", StringComparison.OrdinalIgnoreCase));

            result.Attachments.Add(new GatAttachment
            {
                Name = (string?)attachment.Attribute("name") ?? string.Empty,
                BoneName = (string?)attachment.Attribute("boneName") ?? string.Empty,
                Translation = translation is null ? Vector3.Zero : new Vector3(
                    F(translation, "x"), F(translation, "y"), F(translation, "z")),
                Rotation = rotation is null ? Quaternion.Identity : new Quaternion(
                    F(rotation, "x"), F(rotation, "y"), F(rotation, "z"), F(rotation, "w"))
            });
        }

        return result;
    }

    private static float F(XElement element, string attribute)
    {
        string? text = (string?)element.Attribute(attribute);
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : 0f;
    }
}
