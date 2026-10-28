using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Objects;
using CSMath;

namespace MuhendisKit.Core.Cad;

public sealed record LayerStat(string Layer, int Entities, double Length, int Closed, double ClosedArea);

public sealed record BlockStat(string Block, string Layer, int Count);

/// <param name="Units">drawing unit from $INSUNITS ("Millimeters", "Centimeters", "Meters", "Unitless" …)</param>
/// <param name="SnapPoints">x0, y0, x1, y1 … in drawing coordinates (line ends, vertices, centres, insertion points)</param>
/// <param name="TextEncoding">encoding of the DXF text: UTF-8 from AutoCAD 2007 (AC1021) on, the $DWGCODEPAGE before</param>
public sealed record DrawingInfo(string Units, string Version, string TextEncoding, LayerStat[] Layers, BlockStat[] Blocks,
    double[] SnapPoints, int EntityCount, string[] Notes);

public static class Drawing
{
    const int MaxSnapPoints = 300_000;

    public static CadDocument Read(string path, List<string>? notes = null)
    {
        NotificationEventHandler log = (_, e) =>
        {
            if (notes is not null && e.NotificationType is NotificationType.Error or NotificationType.NotImplemented && notes.Count < 20)
                notes.Add(e.Message);
        };
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".dwg" => DwgReader.Read(path, log),
            ".dxf" => DxfReader.Read(path, log),
            _ => throw new NotSupportedException("Yalnızca .dwg ve .dxf dosyaları açılabilir."),
        };
    }

    /// <summary>ASCII DXF of the document, for the WebGL viewer (which reads DXF only).</summary>
    public static byte[] ToDxf(CadDocument doc)
    {
        using var ms = new MemoryStream();
        using (var w = new DxfWriter(ms, doc, false)) w.Write();
        return ms.ToArray();
    }

    /// <summary>Converts a DXF (as written by the 2D editor) to a DWG file.</summary>
    public static void DxfToDwg(string dxf, string path)
    {
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(dxf));
        var doc = DxfReader.Read(ms);
        DedupeDictionaries(doc);
        DwgWriter.Write(path, doc);
    }

    /// <summary>
    /// MLightCAD lists objects that other dictionaries own (ACAD_CIP_PREVIOUS_PRODUCT_INFO …) again under its own
    /// MLIGHT_XRECORD dictionary. DXF tolerates that; the DWG writer registers the object twice and fails
    /// ("same key has already been added"). Keep the first entry of each object and give it back its owner.
    /// Returns the number of entries removed.
    /// </summary>
    public static int DedupeDictionaries(CadDocument doc)
    {
        var keep = new Dictionary<CadObject, (CadDictionary Dict, string Name)>(ReferenceEqualityComparer.Instance);
        var drop = new List<(CadDictionary Dict, string Name)>();
        void Walk(CadDictionary dict)
        {
            foreach (var name in dict.EntryNames.ToArray())
            {
                var obj = dict[name];
                if (!keep.TryAdd(obj, (dict, name))) drop.Add((dict, name));
                else if (obj is CadDictionary sub) Walk(sub);
            }
        }
        Walk(doc.RootDictionary);
        foreach (var (dict, name) in drop) dict.Remove(name);
        // Removing a shared entry clears the object's owner; re-adding it sets the owner the writer needs.
        foreach (var (obj, (dict, name)) in keep)
            if (obj.Owner != dict)
            {
                dict.Remove(name);
                dict.Add(name, (NonGraphicalObject)obj);
            }
        return drop.Count;
    }

    public static DrawingInfo Analyze(CadDocument doc, IEnumerable<string>? notes = null)
    {
        var layers = new Dictionary<string, (int n, double len, int closed, double area)>();
        var blocks = new Dictionary<(string, string), int>();
        var snaps = new List<double>();
        int count = 0;

        void Snap(XYZ p)
        {
            if (snaps.Count < MaxSnapPoints * 2) { snaps.Add(p.X); snaps.Add(p.Y); }
        }
        void Add(string layer, double len, double? area)
        {
            layers.TryGetValue(layer, out var s);
            layers[layer] = (s.n + 1, s.len + len, s.closed + (area is null ? 0 : 1), s.area + (area ?? 0));
        }

        foreach (var e in doc.Entities)
        {
            count++;
            string layer = e.Layer?.Name ?? "0";
            switch (e)
            {
                case Line l:
                    Add(layer, Dist(l.StartPoint, l.EndPoint), null);
                    Snap(l.StartPoint); Snap(l.EndPoint);
                    break;
                case LwPolyline p:
                {
                    var v = p.Vertices.Select(x => new XYZ(x.Location.X, x.Location.Y, 0)).ToList();
                    var bulges = p.Vertices.Select(x => x.Bulge).ToList();
                    Add(layer, PolyLength(v, bulges, p.IsClosed), p.IsClosed ? Math.Abs(ShoelaceArea(v)) : null);
                    v.ForEach(Snap);
                    break;
                }
                case Polyline2D p:
                {
                    var v = p.Vertices.Select(x => x.Location).ToList();
                    var bulges = p.Vertices.Select(x => x.Bulge).ToList();
                    Add(layer, PolyLength(v, bulges, p.IsClosed), p.IsClosed ? Math.Abs(ShoelaceArea(v)) : null);
                    v.ForEach(Snap);
                    break;
                }
                case Polyline3D p:
                {
                    var v = p.Vertices.Select(x => x.Location).ToList();
                    Add(layer, PolyLength(v, null, p.IsClosed), null);
                    v.ForEach(Snap);
                    break;
                }
                case Arc a:
                {
                    double sweep = a.EndAngle - a.StartAngle;
                    if (sweep < 0) sweep += 2 * Math.PI;
                    Add(layer, a.Radius * sweep, null);
                    Snap(a.Center);
                    a.GetEndVertices(out var s0, out var s1);
                    Snap(s0); Snap(s1);
                    break;
                }
                case Circle c:
                    Add(layer, 2 * Math.PI * c.Radius, Math.PI * c.Radius * c.Radius);
                    Snap(c.Center);
                    break;
                case Insert ins:
                {
                    var key = (ins.Block?.Name ?? "?", layer);
                    blocks[key] = blocks.GetValueOrDefault(key) + 1;
                    Snap(ins.InsertPoint);
                    layers.TryGetValue(layer, out var s);
                    layers[layer] = (s.n + 1, s.len, s.closed, s.area);
                    break;
                }
                default:
                {
                    layers.TryGetValue(layer, out var s);
                    layers[layer] = (s.n + 1, s.len, s.closed, s.area);
                    break;
                }
            }
        }

        return new DrawingInfo(
            doc.Header.InsUnits.ToString(),
            doc.Header.VersionString,
            TextEncoding(doc.Header.VersionString, doc.Header.CodePage),
            layers.OrderBy(k => k.Key, StringComparer.CurrentCultureIgnoreCase)
                .Select(k => new LayerStat(k.Key, k.Value.n, k.Value.len, k.Value.closed, k.Value.area)).ToArray(),
            blocks.Where(b => !b.Key.Item1.StartsWith('*'))   // anonymous blocks (dimensions, hatches)
                .OrderByDescending(b => b.Value)
                .Select(b => new BlockStat(b.Key.Item1, b.Key.Item2, b.Value)).ToArray(),
            snaps.ToArray(), count, notes?.ToArray() ?? []);
    }

    public static string TextEncoding(string version, string? codePage)
    {
        if (string.CompareOrdinal(version, "AC1021") >= 0) return "utf-8";
        // "ANSI_1254" → "windows-1254"; Turkish drawings are usually 1254, the most common default is 1252.
        return codePage is not null && codePage.StartsWith("ANSI_", StringComparison.OrdinalIgnoreCase)
            ? "windows-" + codePage[5..]
            : "windows-1252";
    }

    /// <summary>Length of a polyline; a bulge b on segment i turns it into an arc with included angle 4·atan(b).</summary>
    public static double PolyLength(IReadOnlyList<XYZ> v, IReadOnlyList<double>? bulges, bool closed)
    {
        double total = 0;
        int n = v.Count, segs = closed ? n : n - 1;
        for (int i = 0; i < segs; i++)
        {
            var a = v[i]; var b = v[(i + 1) % n];
            double chord = Dist(a, b);
            double bulge = bulges is null ? 0 : bulges[i];
            if (Math.Abs(bulge) < 1e-12) { total += chord; continue; }
            double theta = 4 * Math.Atan(Math.Abs(bulge));
            double r = chord / (2 * Math.Sin(theta / 2));
            total += r * theta;
        }
        return total;
    }

    static double Dist(XYZ a, XYZ b) => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y) + (b.Z - a.Z) * (b.Z - a.Z));

    public static double ShoelaceArea(IReadOnlyList<XYZ> v)
    {
        double s = 0;
        for (int i = 0; i < v.Count; i++)
        {
            var a = v[i]; var b = v[(i + 1) % v.Count];
            s += a.X * b.Y - b.X * a.Y;
        }
        return s / 2;
    }
}
