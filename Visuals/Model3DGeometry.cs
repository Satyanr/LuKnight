using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace LuKnight.Visuals;

// Shared, frozen meshes/materials: animation changes joint transforms only.
internal static class Model3DGeometry
{
    private static readonly MeshGeometry3D Sphere = CreateSphere();

    internal static Material Material(string color, double gloss = 25)
    {
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(color))));
        material.Children.Add(new SpecularMaterial(new SolidColorBrush(
            Color.FromRgb(70, 70, 75)), gloss));
        material.Freeze();
        return material;
    }

    internal static GeometryModel3D Ellipsoid(Model3DGroup parent, Material material,
        double x, double y, double z, double rx, double ry, double rz)
    {
        var transforms = new Transform3DGroup();
        transforms.Children.Add(new ScaleTransform3D(rx, ry, rz));
        transforms.Children.Add(new TranslateTransform3D(x, y, z));
        transforms.Freeze();
        var model = new GeometryModel3D(Sphere, material) { Transform = transforms };
        model.Freeze();
        parent.Children.Add(model);
        return model;
    }

    private static MeshGeometry3D CreateSphere()
    {
        const int rings = 20, slices = 32;
        var mesh = new MeshGeometry3D();
        for (int row = 0; row <= rings; row++)
        {
            double latitude = Math.PI * row / rings;
            for (int col = 0; col <= slices; col++)
            {
                double longitude = 2 * Math.PI * col / slices;
                var normal = new Vector3D(Math.Sin(latitude) * Math.Cos(longitude),
                    Math.Cos(latitude), Math.Sin(latitude) * Math.Sin(longitude));
                mesh.Positions.Add(new Point3D(normal.X, normal.Y, normal.Z));
                mesh.Normals.Add(normal);
            }
        }
        for (int row = 0; row < rings; row++)
        for (int col = 0; col < slices; col++)
        {
            int a = row * (slices + 1) + col, b = a + slices + 1;
            AddTriangle(mesh, a, a + 1, b);
            AddTriangle(mesh, a + 1, b + 1, b);
        }
        mesh.Freeze();
        return mesh;
    }

    internal static void Ear(Model3DGroup parent, int side, Material fur, Material inner)
    {
        // A curved closed tube and a cyan inset following the very same curve.
        var mesh = new MeshGeometry3D();
        const int rows = 28, cols = 20;
        for (int row = 0; row <= rows; row++)
        {
            double t = row / (double)rows;
            double radius = Math.Pow(Math.Sin(Math.PI * t), .45);
            double cx = side * (.04 * t + .48 * t * t);
            double cy = 1.95 * t - 1.13 * t * t;
            var across = new Vector3D(1.95 - 2.26 * t, -side * (.04 + .96 * t), 0);
            across.Normalize();
            for (int col = 0; col <= cols; col++)
            {
                double angle = 2 * Math.PI * col / cols;
                var normal = across * Math.Cos(angle) + new Vector3D(0, 0, Math.Sin(angle));
                normal.Normalize();
                mesh.Positions.Add(new Point3D(cx + across.X * .215 * radius * Math.Cos(angle),
                    cy + across.Y * .215 * radius * Math.Cos(angle), .115 * radius * Math.Sin(angle)));
                mesh.Normals.Add(normal);
            }
        }
        for (int row = 0; row < rows; row++)
        for (int col = 0; col < cols; col++)
        {
            int a = row * (cols + 1) + col, b = a + cols + 1;
            AddTriangle(mesh, a, b, a + 1);
            AddTriangle(mesh, a + 1, b, b + 1);
        }
        mesh.Freeze();
        var ear = new GeometryModel3D(mesh, fur) { BackMaterial = fur };
        ear.Freeze();
        parent.Children.Add(ear);

        var inset = new MeshGeometry3D();
        for (int row = 0; row <= rows; row++)
        {
            double t = .13 + .74 * row / rows;
            double radius = Math.Pow(Math.Sin(Math.PI * t), .45);
            double width = .115 * Math.Pow(Math.Sin(Math.PI * row / rows), .5);
            var across = new Vector3D(1.95 - 2.26 * t, -side * (.04 + .96 * t), 0);
            across.Normalize();
            for (int col = 0; col <= 8; col++)
            {
                double offset = (col / 4.0 - 1) * width;
                inset.Positions.Add(new Point3D(side * (.04 * t + .48 * t * t) + across.X * offset,
                    1.95 * t - 1.13 * t * t + across.Y * offset,
                    .115 * radius * Math.Sqrt(1 - Math.Pow(offset / (.215 * radius), 2)) + .005));
                inset.Normals.Add(new Vector3D(0, 0, 1));
            }
        }
        for (int row = 0; row < rows; row++)
        for (int col = 0; col < 8; col++)
        {
            int a = row * 9 + col;
            AddTriangle(inset, a, a + 1, a + 9);
            AddTriangle(inset, a + 1, a + 10, a + 9);
        }
        inset.Freeze();
        var lining = new GeometryModel3D(inset, inner) { BackMaterial = inner };
        lining.Freeze();
        parent.Children.Add(lining);
    }

    internal static void Line(Model3DGroup parent, Material material, Point3D from, Point3D to, double width)
    {
        Vector3D direction = to - from;
        var up = new Vector3D(0, 1, 0);
        var axis = Vector3D.CrossProduct(up, direction);
        if (axis.LengthSquared < .000001) axis = new Vector3D(1, 0, 0);
        var rotation = new Quaternion(axis, Vector3D.AngleBetween(up, direction));
        var transforms = new Transform3DGroup();
        transforms.Children.Add(new ScaleTransform3D(width, direction.Length / 2 + width, width));
        transforms.Children.Add(new RotateTransform3D(new QuaternionRotation3D(rotation)));
        transforms.Children.Add(new TranslateTransform3D((from.X + to.X) / 2,
            (from.Y + to.Y) / 2, (from.Z + to.Z) / 2));
        transforms.Freeze();
        var line = new GeometryModel3D(Sphere, material) { Transform = transforms };
        line.Freeze();
        parent.Children.Add(line);
    }

    internal static void Star(Model3DGroup parent, Material material, double x, double y, double z, double size)
    {
        var mesh = new MeshGeometry3D();
        mesh.Positions.Add(new Point3D(x, y, z + .025));
        for (int i = 0; i < 8; i++)
        {
            double angle = Math.PI / 2 + i * Math.PI / 4, radius = (i % 2 == 0 ? 1 : .33) * size;
            mesh.Positions.Add(new Point3D(x + Math.Cos(angle) * radius, y + Math.Sin(angle) * radius, z));
        }
        for (int i = 0; i < 8; i++) AddTriangle(mesh, 0, i + 1, (i + 1) % 8 + 1);
        mesh.Freeze();
        var star = new GeometryModel3D(mesh, material) { BackMaterial = material };
        star.Freeze();
        parent.Children.Add(star);
    }

    private static void AddTriangle(MeshGeometry3D mesh, int a, int b, int c)
    {
        mesh.TriangleIndices.Add(a);
        mesh.TriangleIndices.Add(b);
        mesh.TriangleIndices.Add(c);
    }
}
