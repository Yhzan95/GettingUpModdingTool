using GettingUpModTool.Core.Models;
namespace GettingUpModTool.Core.Animation;

public static class TraneBoneGroups
{
    private static readonly HashSet<string> RightHand = new(StringComparer.OrdinalIgnoreCase)
    {
        "R Finger0", "R Finger01", "R Finger02", "R Finger1", "R Finger11", "R Finger12",
        "R Finger2", "R Finger21", "R Finger22", "R Finger3", "R Finger31", "R Finger32",
        "R Finger4", "R Finger41", "R Finger42"
    };
    private static readonly HashSet<string> LeftHand = new(StringComparer.OrdinalIgnoreCase)
    {
        "L Finger0", "L Finger01", "L Finger02", "L Finger1", "L Finger11", "L Finger12",
        "L Finger2", "L Finger21", "L Finger22", "L Finger3", "L Finger31", "L Finger32",
        "L Finger4", "L Finger41", "L Finger42"
    };
    private static readonly HashSet<string> RightArm = new(RightHand, StringComparer.OrdinalIgnoreCase)
    {
        "R Clavicle", "R UpperArm", "R Forearm", "R Hand"
    };

    private static readonly HashSet<string> LeftArm = new(LeftHand, StringComparer.OrdinalIgnoreCase)
    {
        "L Clavicle", "L UpperArm", "L Forearm", "L Hand"
    };

    private static readonly HashSet<string> Torso = new(StringComparer.OrdinalIgnoreCase)
    {
        "Spine", "Spine1", "Spine2"
    };

    private static readonly HashSet<string> RightLeg = new(StringComparer.OrdinalIgnoreCase)
    {
        "R Thigh", "R Calf", "R Foot", "R Toe0"
    };

    private static readonly HashSet<string> LeftLeg = new(StringComparer.OrdinalIgnoreCase)
    {
        "L Thigh", "L Calf", "L Foot", "L Toe0"
    };
    private static readonly HashSet<string> Facial = new(StringComparer.OrdinalIgnoreCase)
    {
        "Jaw", "LipLowerRt", "LipLowerLft", "Tongue",
        "LipCornerLft", "LipCornerRt", "LipUpperLft", "LipUpperRt",
        "Cheeks", "Eyes", "EyeLids", "Brow"
    };

    public static bool IsRightArm(string boneName) => RightArm.Contains(boneName);
    public static bool IsLeftArm(string boneName) => LeftArm.Contains(boneName);
    public static bool IsFacial(string boneName) => Facial.Contains(boneName);

    public static int ApplyFacialGuard(int[] map, SkeletonData skeleton)
    {
        int removed = 0;
        for (int i = 0; i < map.Length && i < skeleton.Bones.Count; i++)
        {
            if (map[i] < 0 || !Facial.Contains(skeleton.Bones[i].Name))
                continue;

            map[i] = -1;
            removed++;
        }
        return removed;
    }
    public static string DetectBodyMask(int[] map, SkeletonData skeleton)
    {
        
        int rightCore = CountMapped(map, skeleton, "R Clavicle", "R UpperArm", "R Forearm", "R Hand");
        int leftCore = CountMapped(map, skeleton, "L Clavicle", "L UpperArm", "L Forearm", "L Hand");

        if (rightCore < 4 && leftCore == 4)
            return "BodyNoRightArm";
        if (leftCore < 4 && rightCore == 4)
            return "BodyNoLeftArm";
        return "EntireBody";
    }

    public static double ComputeCoreCoverage(int[] map, SkeletonData skeleton, string maskName)
    {
        string[] core =
        {
            "root", "Pelvis", "Spine", "Spine1", "Spine2", "Neck", "Head",
            "L Clavicle", "L UpperArm", "L Forearm", "L Hand",
            "R Clavicle", "R UpperArm", "R Forearm", "R Hand",
            "L Thigh", "L Calf", "L Foot", "L Toe0",
            "R Thigh", "R Calf", "R Foot", "R Toe0"
        };

        int expected = 0;
        int present = 0;
        foreach (string name in core)
        {
            if (!AllowsBone(maskName, name))
                continue;

            int index = FindBone(skeleton, name);
            if (index < 0)
                continue;

            expected++;
            if (index < map.Length && map[index] >= 0)
                present++;
        }

        return expected > 0 ? present / (double)expected : 0.0;
    }
    public static bool AllowsBone(string maskName, string boneName)
    {
        if (string.IsNullOrWhiteSpace(maskName) || maskName.Equals("EntireBody", StringComparison.OrdinalIgnoreCase))
            return true;

        if (maskName.Equals("BodyNoRightArm", StringComparison.OrdinalIgnoreCase))
            return !RightArm.Contains(boneName);
        if (maskName.Equals("BodyNoLeftArm", StringComparison.OrdinalIgnoreCase))
            return !LeftArm.Contains(boneName);
        if (maskName.Equals("RightArm", StringComparison.OrdinalIgnoreCase))
            return RightArm.Contains(boneName);
        if (maskName.Equals("LeftArm", StringComparison.OrdinalIgnoreCase))
            return LeftArm.Contains(boneName);
        if (maskName.Equals("BothArms", StringComparison.OrdinalIgnoreCase))
            return RightArm.Contains(boneName) || LeftArm.Contains(boneName);
        if (maskName.Equals("Torso", StringComparison.OrdinalIgnoreCase))
            return Torso.Contains(boneName);
        if (maskName.Equals("UpperBody", StringComparison.OrdinalIgnoreCase))
            return boneName.Equals("Head", StringComparison.OrdinalIgnoreCase) ||
                   boneName.Equals("Neck", StringComparison.OrdinalIgnoreCase) ||
                   Torso.Contains(boneName) || RightArm.Contains(boneName) || LeftArm.Contains(boneName);
        if (maskName.Equals("LowerBody", StringComparison.OrdinalIgnoreCase))
            return RightLeg.Contains(boneName) || LeftLeg.Contains(boneName);

        return true;
    }
    public static int ApplyMask(int[] map, SkeletonData skeleton, string maskName)
    {
        int removed = 0;
        for (int i = 0; i < map.Length && i < skeleton.Bones.Count; i++)
        {
            if (map[i] < 0 || AllowsBone(maskName, skeleton.Bones[i].Name))
                continue;
            map[i] = -1;
            removed++;
        }
        return removed;
    }

    public static string DetectLayerMask(string animationName)
    {
        string n = animationName ?? string.Empty;
        if (n.Contains("Rhand_Layer", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("ArmLayer_R", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("RightArm", StringComparison.OrdinalIgnoreCase))
            return "RightArm";
        if (n.Contains("Lhand_Layer", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("ArmLayer_L", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("LeftArm", StringComparison.OrdinalIgnoreCase))
            return "LeftArm";
        return string.Empty;
    }

    private static int CountMapped(int[] map, SkeletonData skeleton, params string[] names)
    {
        int count = 0;
        foreach (string name in names)
        {
            int index = FindBone(skeleton, name);
            if (index >= 0 && index < map.Length && map[index] >= 0)
                count++;
        }
        return count;
    }

    private static int FindBone(SkeletonData skeleton, string name)
    {
        for (int i = 0; i < skeleton.Bones.Count; i++)
        {
            if (skeleton.Bones[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }
}
