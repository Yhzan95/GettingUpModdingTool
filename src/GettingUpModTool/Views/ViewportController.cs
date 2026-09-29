using System.Numerics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Media.Media3D;
using GettingUpModTool.Core.Models;

namespace GettingUpModTool.Views;

public sealed class ViewportController
{
    private readonly Viewport3D _viewport;
    private readonly PerspectiveCamera _camera;
    private readonly Model3DGroup _root = new();
    private Point _lastMouse;
    private bool _rotating;
    private bool _panning;
    private double _yaw = 45;
    private double _pitch = -20;
    private double _distance = 10;
    private Point3D _target = new(0, 0, 0);
    private Window? _hostWindow;

    public ViewportController(Viewport3D viewport)
    {
        _viewport = viewport;
        _camera = new PerspectiveCamera { FieldOfView = 60, NearPlaneDistance = 0.01, FarPlaneDistance = 1_000_000 };
        _viewport.Camera = _camera;

        var visual = new ModelVisual3D { Content = _root };
        _viewport.Children.Add(visual);
        _root.Children.Add(new AmbientLight(Color.FromRgb(90, 90, 90)));
        _root.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-1, -1, -2)));
        _root.Children.Add(new DirectionalLight(Color.FromRgb(160, 160, 160), new Vector3D(1, 0.3, 1)));

        viewport.MouseDown += OnMouseDown;
        viewport.MouseUp += OnMouseUp;
        viewport.MouseMove += OnMouseMove;
        viewport.MouseWheel += OnMouseWheel;
        viewport.LostMouseCapture += OnLostMouseCapture;
        viewport.IsVisibleChanged += OnViewportVisibilityChanged;
        viewport.Loaded += OnViewportLoaded;
        viewport.Unloaded += OnViewportUnloaded;
        UpdateCamera();
    }

    public void ShowScene(
        MeshData mesh,
        SkeletonData? skeleton,
        bool showMesh = true,
        bool showSkeleton = true,
        ImageSource? texture = null,
        IReadOnlyList<Matrix4x4>? animatedBoneWorlds = null,
        bool fitCamera = true,
        IReadOnlyDictionary<int, ImageSource>? partTextures = null,
        double textureEmissiveBoost = 0.0,
        int? selectedBoneIndex = null)
    {
        ClearSceneModels();

        if (showMesh)
            AddMesh(mesh, texture, partTextures, textureEmissiveBoost);
        if (showSkeleton && skeleton is not null && skeleton.Bones.Count > 0)
            AddSkeleton(mesh, skeleton, animatedBoneWorlds, selectedBoneIndex);

        if (fitCamera)
            Fit(mesh, skeleton, animatedBoneWorlds);
    }

    private void ClearSceneModels()
    {
        while (_root.Children.Count > 3)
            _root.Children.RemoveAt(_root.Children.Count - 1);
    }

    private void AddMesh(MeshData mesh, ImageSource? texture, IReadOnlyDictionary<int, ImageSource>? partTextures, double textureEmissiveBoost)
    {
        if (mesh.Parts.Count == 0)
        {
            AddGeometryModel(mesh, mesh.Indices, texture, textureEmissiveBoost);
            return;
        }

        foreach (MeshPart part in mesh.Parts.Where(p => p.IsVisible && p.IndexCount >= 3))
        {
            int start = Math.Clamp(part.IndexStart, 0, mesh.Indices.Count);
            int count = Math.Clamp(part.IndexCount, 0, mesh.Indices.Count - start);
            if (count < 3)
                continue;

            ImageSource? partTexture = null;
            bool hasPerSectionTextures = partTextures is not null && partTextures.Count > 0;
            if (hasPerSectionTextures)
                partTextures!.TryGetValue(part.SectionIndex, out partTexture);

            
            
            
            if (!hasPerSectionTextures && partTexture is null && string.IsNullOrWhiteSpace(part.TextureReference))
                partTexture = texture;

            AddGeometryModel(mesh, mesh.Indices.GetRange(start, count), partTexture, textureEmissiveBoost);
        }
    }

    private void AddGeometryModel(MeshData mesh, IEnumerable<int> indices, ImageSource? texture, double textureEmissiveBoost)
    {
        var geometry = new MeshGeometry3D();
        foreach (Vector3 v in mesh.Positions)
            geometry.Positions.Add(new Point3D(v.X, v.Y, v.Z));

        foreach (int idx in indices)
        {
            if (idx >= 0 && idx < geometry.Positions.Count)
                geometry.TriangleIndices.Add(idx);
        }

        if (mesh.UVs.Count == mesh.Positions.Count)
            foreach (var uv in mesh.UVs)
                geometry.TextureCoordinates.Add(new Point(uv.X, uv.Y));

        if (mesh.Normals.Count == mesh.Positions.Count)
            foreach (Vector3 n in mesh.Normals)
                geometry.Normals.Add(new Vector3D(n.X, n.Y, n.Z));

        bool hasTexture = texture is not null && geometry.TextureCoordinates.Count == geometry.Positions.Count;
        Brush brush = hasTexture
            ? new ImageBrush(texture) { Stretch = Stretch.Fill }
            : new SolidColorBrush(Color.FromArgb(210, 170, 190, 210));

        Material material;
        if (hasTexture && textureEmissiveBoost > 0.001)
        {
            var group = new MaterialGroup();
            group.Children.Add(new DiffuseMaterial(brush));
            group.Children.Add(new EmissiveMaterial(new ImageBrush(texture!)
            {
                Stretch = Stretch.Fill,
                Opacity = Math.Clamp(textureEmissiveBoost, 0.0, 1.0)
            }));
            material = group;
        }
        else
        {
            material = new DiffuseMaterial(brush);
        }

        var model = new GeometryModel3D(geometry, material) { BackMaterial = material };
        _root.Children.Add(model);
    }

    private void AddSkeleton(MeshData mesh, SkeletonData skeleton, IReadOnlyList<Matrix4x4>? animatedBoneWorlds, int? selectedBoneIndex)
    {
        double scale = EstimateSceneScale(mesh, skeleton, animatedBoneWorlds);
        double boneRadius = Math.Max(0.01, scale * 0.008);
        double jointRadius = boneRadius * 1.65;

        var skeletonGroup = new Model3DGroup();
        var boneMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(255, 180, 65)));
        var jointMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(255, 235, 125)));
        var rootMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(255, 95, 95)));
        var selectedMaterial = new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(255, 122, 24)));

        foreach (var bone in skeleton.Bones)
        {
            Point3D p = ToPoint(BonePosition(bone, animatedBoneWorlds));
            Material joint = bone.Index == selectedBoneIndex
                ? selectedMaterial
                : bone.ParentIndex < 0 ? rootMaterial : jointMaterial;
            double radius = bone.Index == selectedBoneIndex ? jointRadius * 2.1 : jointRadius;
            var cube = new GeometryModel3D(CreateCube(p, radius), joint)
            {
                BackMaterial = joint
            };
            skeletonGroup.Children.Add(cube);

            if (bone.ParentIndex >= 0)
            {
                BoneData? parentBone = skeleton.Bones.FirstOrDefault(b => b.Index == bone.ParentIndex)
                    ?? (bone.ParentIndex < skeleton.Bones.Count ? skeleton.Bones[bone.ParentIndex] : null);
                if (parentBone is not null)
                {
                    Point3D parent = ToPoint(BonePosition(parentBone, animatedBoneWorlds));
                    if ((p - parent).Length > 0.000001)
                    {
                        var cylinder = new GeometryModel3D(CreateCylinder(parent, p, boneRadius, 8), boneMaterial)
                        {
                            BackMaterial = boneMaterial
                        };
                        skeletonGroup.Children.Add(cylinder);
                    }
                }
            }
        }

        _root.Children.Add(skeletonGroup);
    }

    private static Vector3 BonePosition(BoneData bone, IReadOnlyList<Matrix4x4>? animatedBoneWorlds)
    {
        if (animatedBoneWorlds is not null && bone.Index >= 0 && bone.Index < animatedBoneWorlds.Count)
        {
            Matrix4x4 m = animatedBoneWorlds[bone.Index];
            return new Vector3(m.M41, m.M42, m.M43);
        }
        return bone.BindPosition;
    }

    private static Point3D ToPoint(Vector3 p) => new(p.X, p.Y, p.Z);

    private static MeshGeometry3D CreateCube(Point3D c, double r)
    {
        var g = new MeshGeometry3D();
        Point3D[] p =
        [
            new(c.X-r,c.Y-r,c.Z-r), new(c.X+r,c.Y-r,c.Z-r),
            new(c.X+r,c.Y+r,c.Z-r), new(c.X-r,c.Y+r,c.Z-r),
            new(c.X-r,c.Y-r,c.Z+r), new(c.X+r,c.Y-r,c.Z+r),
            new(c.X+r,c.Y+r,c.Z+r), new(c.X-r,c.Y+r,c.Z+r)
        ];
        foreach (var v in p) g.Positions.Add(v);
        int[] idx =
        [
            0,2,1, 0,3,2, 4,5,6, 4,6,7,
            0,1,5, 0,5,4, 3,7,6, 3,6,2,
            1,2,6, 1,6,5, 0,4,7, 0,7,3
        ];
        foreach (int i in idx) g.TriangleIndices.Add(i);
        return g;
    }

    private static MeshGeometry3D CreateCylinder(Point3D a, Point3D b, double radius, int segments)
    {
        var g = new MeshGeometry3D();
        Vector3D axis = b - a;
        if (axis.Length < 0.000001)
            return g;
        axis.Normalize();

        Vector3D helper = Math.Abs(Vector3D.DotProduct(axis, new Vector3D(0, 1, 0))) < 0.9
            ? new Vector3D(0, 1, 0)
            : new Vector3D(1, 0, 0);
        Vector3D u = Vector3D.CrossProduct(axis, helper);
        u.Normalize();
        Vector3D v = Vector3D.CrossProduct(axis, u);
        v.Normalize();

        for (int i = 0; i < segments; i++)
        {
            double angle = i * Math.PI * 2.0 / segments;
            Vector3D radial = (Math.Cos(angle) * u + Math.Sin(angle) * v) * radius;
            g.Positions.Add(a + radial);
            g.Positions.Add(b + radial);
        }

        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            int a0 = i * 2;
            int b0 = a0 + 1;
            int a1 = next * 2;
            int b1 = a1 + 1;
            g.TriangleIndices.Add(a0); g.TriangleIndices.Add(b0); g.TriangleIndices.Add(b1);
            g.TriangleIndices.Add(a0); g.TriangleIndices.Add(b1); g.TriangleIndices.Add(a1);
        }

        return g;
    }

    private static double EstimateSceneScale(MeshData mesh, SkeletonData skeleton, IReadOnlyList<Matrix4x4>? animatedBoneWorlds)
    {
        var points = new List<Vector3>();
        points.AddRange(mesh.Positions);
        points.AddRange(skeleton.Bones.Select(b => BonePosition(b, animatedBoneWorlds)));
        if (points.Count == 0) return 1;

        Vector3 min = points[0], max = points[0];
        foreach (var p in points)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        Vector3 size = max - min;
        return Math.Max(1.0, Math.Max(size.X, Math.Max(size.Y, size.Z)));
    }

    private void Fit(MeshData mesh, SkeletonData? skeleton, IReadOnlyList<Matrix4x4>? animatedBoneWorlds)
    {
        var points = new List<Vector3>();
        points.AddRange(mesh.Positions);
        if (skeleton is not null)
            points.AddRange(skeleton.Bones.Select(b => BonePosition(b, animatedBoneWorlds)));
        if (points.Count == 0) return;

        Vector3 min = points[0], max = points[0];
        foreach (var v in points)
        {
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }
        Vector3 center = (min + max) * 0.5f;
        Vector3 size = max - min;
        float radius = Math.Max(0.001f, Math.Max(size.X, Math.Max(size.Y, size.Z)) * 0.5f);
        _target = new Point3D(center.X, center.Y, center.Z);
        _distance = Math.Max(1, radius * 3.0);
        UpdateCamera();
    }

    public void FocusBone(SkeletonData skeleton, IReadOnlyList<Matrix4x4>? animatedBoneWorlds, int boneIndex)
    {
        BoneData? selectedBone = skeleton.Bones.FirstOrDefault(b => b.Index == boneIndex);
        if (selectedBone is null)
            return;

        Point3D p = ToPoint(BonePosition(selectedBone, animatedBoneWorlds));
        _target = p;

        
        
        double skeletonScale = 1.0;
        if (skeleton.Bones.Count > 1)
        {
            Vector3 first = BonePosition(skeleton.Bones[0], animatedBoneWorlds);
            Vector3 min = first;
            Vector3 max = first;
            foreach (BoneData bone in skeleton.Bones)
            {
                Vector3 bp = BonePosition(bone, animatedBoneWorlds);
                min = Vector3.Min(min, bp);
                max = Vector3.Max(max, bp);
            }
            Vector3 size = max - min;
            skeletonScale = Math.Max(0.1, Math.Max(size.X, Math.Max(size.Y, size.Z)));
        }
        _distance = Math.Clamp(skeletonScale * 0.65, 0.15, 1_000_000);
        UpdateCamera();
    }
    public void SetPresetView(string preset)
    {
        switch (preset)
        {
            case "Front":
                _yaw = 90;
                _pitch = 0;
                break;
            case "Back":
                _yaw = -90;
                _pitch = 0;
                break;
            case "Left":
                _yaw = 0;
                _pitch = 0;
                break;
            case "Right":
                _yaw = 180;
                _pitch = 0;
                break;
            case "Top":
                _yaw = 90;
                _pitch = 89;
                break;
            default:
                _yaw = 45;
                _pitch = -20;
                break;
        }
        UpdateCamera();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            _rotating = true;
            _panning = false;
        }
        else if (e.ChangedButton == MouseButton.Right)
        {
            _panning = true;
            _rotating = false;
        }
        else
        {
            return;
        }

        _lastMouse = e.GetPosition(_viewport);
        _viewport.CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            _rotating = false;
        else if (e.ChangedButton == MouseButton.Right)
            _panning = false;
        else
            return;

        if (!_rotating && !_panning)
            _viewport.ReleaseMouseCapture();
        e.Handled = true;
    }


    private void CancelPointerInteraction()
    {
        _rotating = false;
        _panning = false;
        if (_viewport.IsMouseCaptured)
            _viewport.ReleaseMouseCapture();
    }

    private void OnViewportLoaded(object sender, RoutedEventArgs e)
    {
        Window? host = Window.GetWindow(_viewport);
        if (ReferenceEquals(host, _hostWindow))
            return;
        if (_hostWindow is not null)
            _hostWindow.Deactivated -= OnHostWindowDeactivated;
        _hostWindow = host;
        if (_hostWindow is not null)
            _hostWindow.Deactivated += OnHostWindowDeactivated;
    }

    private void OnViewportUnloaded(object sender, RoutedEventArgs e)
    {
        if (_hostWindow is not null)
            _hostWindow.Deactivated -= OnHostWindowDeactivated;
        _hostWindow = null;
        CancelPointerInteraction();
    }

    private void OnHostWindowDeactivated(object? sender, EventArgs e)
        => CancelPointerInteraction();

    private void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        _rotating = false;
        _panning = false;
    }

    private void OnViewportVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewport.IsVisible)
            return;
        CancelPointerInteraction();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_rotating && !_panning)
            return;

        Point p = e.GetPosition(_viewport);
        double dx = p.X - _lastMouse.X;
        double dy = p.Y - _lastMouse.Y;

        if (_rotating)
        {
            _yaw += dx * 0.35;
            _pitch = Math.Clamp(_pitch - dy * 0.35, -89, 89);
        }
        else if (_panning)
        {
            PanCamera(dx, dy);
        }

        _lastMouse = p;
        UpdateCamera();
    }

    private void PanCamera(double dx, double dy)
    {
        Vector3D look = _target - _camera.Position;
        if (look.Length < 0.000001)
            return;
        look.Normalize();

        Vector3D cameraUp = _camera.UpDirection;
        if (cameraUp.Length < 0.000001)
            cameraUp = new Vector3D(0, 1, 0);
        cameraUp.Normalize();

        Vector3D right = Vector3D.CrossProduct(look, cameraUp);
        if (right.Length < 0.000001)
            right = new Vector3D(1, 0, 0);
        right.Normalize();

        Vector3D up = Vector3D.CrossProduct(right, look);
        if (up.Length < 0.000001)
            up = new Vector3D(0, 1, 0);
        up.Normalize();

        double viewportHeight = Math.Max(1.0, _viewport.ActualHeight);
        double worldPerPixel = 2.0 * _distance * Math.Tan(_camera.FieldOfView * Math.PI / 360.0) / viewportHeight;

        
        
        
        Vector3D delta = (-dx * right + dy * up) * worldPerPixel;
        _target += delta;
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _distance *= e.Delta > 0 ? 0.85 : 1.18;
        _distance = Math.Clamp(_distance, 0.01, 1_000_000);
        UpdateCamera();
    }

    private void UpdateCamera()
    {
        double yaw = _yaw * Math.PI / 180.0;
        double pitch = _pitch * Math.PI / 180.0;
        double cp = Math.Cos(pitch);
        var offset = new Vector3D(
            _distance * cp * Math.Cos(yaw),
            _distance * Math.Sin(pitch),
            _distance * cp * Math.Sin(yaw));
        _camera.Position = _target + offset;
        _camera.LookDirection = _target - _camera.Position;
        _camera.UpDirection = new Vector3D(0, 1, 0);
    }
}
