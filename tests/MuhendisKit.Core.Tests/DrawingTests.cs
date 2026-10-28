using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Objects;
using ACadSharp.Tables;
using CSMath;
using MuhendisKit.Core.Cad;

namespace MuhendisKit.Core.Tests;

public class DrawingTests
{
    static CadDocument Sample()
    {
        var doc = new CadDocument();
        var pipe = new Layer("BORU");
        var room = new Layer("MAHAL");
        doc.Layers.Add(pipe);
        doc.Layers.Add(room);
        doc.Entities.Add(new Line(new XYZ(0, 0, 0), new XYZ(3, 4, 0)) { Layer = pipe });
        // Semicircle via bulge 1 between (10,0) and (12,0): length π
        doc.Entities.Add(new LwPolyline([new LwPolyline.Vertex(new XY(10, 0)) { Bulge = 1 }, new LwPolyline.Vertex(new XY(12, 0))]) { Layer = pipe });
        doc.Entities.Add(new LwPolyline(new[] { new XY(0, 0), new XY(10, 0), new XY(10, 10), new XY(0, 10) }.Select(p => new LwPolyline.Vertex(p))) { IsClosed = true, Layer = room });

        var block = new BlockRecord("RADYATOR");
        block.Entities.Add(new Line(new XYZ(0, 0, 0), new XYZ(1, 0, 0)));
        doc.BlockRecords.Add(block);
        doc.Entities.Add(new Insert(block) { InsertPoint = new XYZ(1, 1, 0), Layer = room });
        doc.Entities.Add(new Insert(block) { InsertPoint = new XYZ(5, 1, 0), Layer = room });
        return doc;
    }

    static void AssertStats(DrawingInfo info)
    {
        var pipe = info.Layers.Single(l => l.Layer == "BORU");
        Assert.Equal(5 + Math.PI, pipe.Length, 6);
        var room = info.Layers.Single(l => l.Layer == "MAHAL");
        Assert.Equal(40, room.Length, 6);
        Assert.Equal(100, room.ClosedArea, 6);
        var radiator = Assert.Single(info.Blocks);
        Assert.Equal(("RADYATOR", "MAHAL", 2), (radiator.Block, radiator.Layer, radiator.Count));
        Assert.True(info.SnapPoints.Length >= 2 * (2 + 2 + 4 + 2));
    }

    [Theory]
    [InlineData(".dxf")]
    [InlineData(".dwg")]
    public void RoundTripThroughFileKeepsLengthsAndBlockCounts(string ext)
    {
        string path = Path.Combine(Path.GetTempPath(), $"mk-test-{Guid.NewGuid():n}{ext}");
        try
        {
            if (ext == ".dwg") DwgWriter.Write(path, Sample());
            else DxfWriter.Write(path, Sample(), false);
            var doc = Drawing.Read(path);
            AssertStats(Drawing.Analyze(doc));
            // The DXF handed to the viewer can be read again
            using var ms = new MemoryStream(Drawing.ToDxf(doc));
            AssertStats(Drawing.Analyze(DxfReader.Read(ms)));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DxfWithSharedDictionaryEntryConvertsToDwg()
    {
        // MLightCAD writes ACAD_CIP_PREVIOUS_PRODUCT_INFO both in the root dictionary and again in MLIGHT_XRECORD.
        string dxf;
        using (var ms = new MemoryStream())
        {
            var doc = Sample();
            var info = new XRecord("ACAD_CIP_PREVIOUS_PRODUCT_INFO");
            doc.RootDictionary.Add(info);
            var mlight = new CadDictionary("MLIGHT_XRECORD");
            doc.RootDictionary.Add(mlight);
            var placeholder = new XRecord("ACAD_CIP_PREVIOUS_PRODUCT_INFO");
            mlight.Add(placeholder);
            using (var w = new DxfWriter(ms, doc, false)) w.Write();
            // Point MLIGHT_XRECORD's entry (group 350) at the root's object instead of its own copy.
            dxf = System.Text.Encoding.UTF8.GetString(ms.ToArray()).Replace("\r\n", "\n")
                .Replace($"\n350\n{placeholder.Handle:X}\n", $"\n350\n{info.Handle:X}\n");
        }
        string path = Path.Combine(Path.GetTempPath(), $"mk-test-{Guid.NewGuid():n}.dwg");
        try
        {
            Drawing.DxfToDwg(dxf, path);
            var back = DwgReader.Read(path);
            Assert.Contains("ACAD_CIP_PREVIOUS_PRODUCT_INFO", back.RootDictionary.EntryNames);
            AssertStats(Drawing.Analyze(back));
        }
        finally { File.Delete(path); }
    }
}
