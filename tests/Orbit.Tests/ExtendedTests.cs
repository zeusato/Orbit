using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using ImageMagick;
using Orbit.Core;
using Orbit.App.Services;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

internal static partial class Program
{
    private static void CheckExtended(string source, bool office)
    {
        var engine = new DocumentEngine();
        Check("Content detection ignores misleading image extensions", () =>
        {
            var renamed = Path.Combine(_folder, "actually-png.jpg"); File.Copy(source, renamed);
            Assert(FileTypes.Detect(renamed) == FileKind.Png, "PNG signature wins");
            Assert(!ToolCatalog.ForFiles([FileTypes.Detect(renamed)]).Contains(ToolKind.Png), "hide actual source format");
            Assert(Size(renamed) == (80, 60), "decoder reads actual content");
        });
        Check("Menus filter source formats, batch tools, Office availability and pages", () =>
        {
            var jpeg = ToolCatalog.ForFiles([FileKind.Jpeg]); Assert(!jpeg.Contains(ToolKind.Jpeg) && jpeg.Contains(ToolKind.Webp), "JPEG menu");
            var mixed = ToolCatalog.ForFiles([FileKind.Jpeg, FileKind.Png]); Assert(!mixed.Contains(ToolKind.Jpeg) && !mixed.Contains(ToolKind.Png) && !mixed.Contains(ToolKind.Crop), "intersection");
            Assert(ToolCatalog.ForFiles([FileKind.Pdf, FileKind.Pdf]).Contains(ToolKind.PdfMerge), "merge selection");
            Assert(!ToolCatalog.ForFiles([FileKind.Pdf]).Contains(ToolKind.Crop), "no image-only action");
            Assert(!ToolCatalog.ForFiles([FileKind.Docx], _ => false).Contains(ToolKind.OfficePdf), "missing Office hidden");
            Assert(ToolCatalog.ForFiles([FileKind.Docx], _ => true).Contains(ToolKind.OfficePdf), "installed Office");
            var pages = Enumerable.Range(0, (jpeg.Count + 4) / 5).SelectMany(p => ToolCatalog.Page(jpeg, p)).Where(t => t != ToolKind.More).ToArray();
            Assert(pages.SequenceEqual(jpeg), "every tool reachable once");
            foreach (int n in new[] { 2,3,4,5,6 }) for (int i = 0; i < n; i++)
            { var p = RadialGeometry.Point(130, -90 + i * 360d / n); Assert(RadialGeometry.HitTest(p.X,p.Y,n) == i, "variable sector hit test"); }
        });
        Check("WebP round trip is lossless and keeps alpha", () =>
        {
            var webp = Engine.Execute(new(source, ToolKind.Webp), CancellationToken.None);
            Assert(FileTypes.Detect(webp) == FileKind.Webp && Size(webp) == (80,60), "WebP signature and decoder");
            Assert(ReadPixels(source).SequenceEqual(ReadPixels(webp)), "lossless pixels");
            var transparent = WriteImage("alpha-webp.png", 12, 9, true);
            var output = Engine.Execute(new(transparent, ToolKind.Webp), CancellationToken.None);
            Assert(ReadPixels(output).Where((_,i) => i % 4 == 3).All(a => a == 0), "transparent alpha");
        });
        Check("BMP TIFF GIF ICO encoders produce readable native formats", () =>
        {
            foreach (var pair in new[] {(ToolKind.Bmp,FileKind.Bmp),(ToolKind.Tiff,FileKind.Tiff),(ToolKind.Gif,FileKind.Gif),(ToolKind.Ico,FileKind.Ico)})
            { var output = Engine.Execute(new(source,pair.Item1),CancellationToken.None); Assert(FileTypes.Detect(output)==pair.Item2, "signature " + pair.Item1); Assert(Size(output)==(80,60), "decode " + pair.Item1); }
        });
        Check("AVIF and TGA inputs decode to PNG", () =>
        {
            foreach (var pair in new[] {(MagickFormat.Avif,"avif"),(MagickFormat.Tga,"tga")})
            {
                var path=Path.Combine(_folder,"extended."+pair.Item2); using(var image=new MagickImage(source)) image.Write(path,pair.Item1);
                var png=Engine.Execute(new(path,ToolKind.Png),CancellationToken.None); Assert(Size(png)==(80,60),"decode " + pair.Item2);
            }
        });
        Check("Animated WebP is rejected without silently losing frames", () =>
        {
            var path=Path.Combine(_folder,"animated.webp"); using(var frames=new MagickImageCollection())
            { frames.Add(new MagickImage(source)); frames.Add(new MagickImage(source)); frames[1].Negate(); frames[0].AnimationDelay=10; frames[1].AnimationDelay=10; frames.Write(path,MagickFormat.WebP); }
            Throws<NotSupportedException>(()=>Engine.Load(path));
        });
        Check("PDF image export, split and merge preserve pages and sources", () =>
        {
            var first=engine.Execute(new(source,ToolKind.ImagePdf),CancellationToken.None);
            var second=Path.Combine(_folder,"two-pages.pdf");
            using(var input=PdfReader.Open(first,PdfDocumentOpenMode.Import)) using(var pdf=new PdfDocument()) { pdf.AddPage(input.Pages[0]); pdf.AddPage(input.Pages[0]); pdf.Save(second); }
            var before=Hash(second);
            var pages=engine.Execute(new(second,ToolKind.PdfPng),CancellationToken.None);
            Assert(Directory.GetFiles(pages,"*.png").Length==2,"all pages PNG"); Assert(Size(Directory.GetFiles(pages)[0])==(125,94),"150 DPI size");
            var jpgs=engine.Execute(new(second,ToolKind.PdfJpeg),CancellationToken.None); Assert(Directory.GetFiles(jpgs,"*.jpg").Length==2,"all pages JPEG");
            var split=engine.Execute(new(second,ToolKind.PdfSplit),CancellationToken.None); Assert(Directory.GetFiles(split,"*.pdf").Length==2,"split count");
            var merged=engine.Execute(new(first,ToolKind.PdfMerge,Sources:[second,first]),CancellationToken.None);
            using var read=PdfReader.Open(merged,PdfDocumentOpenMode.Import); Assert(read.PageCount==3,"merge count"); Assert(Hash(second)==before,"PDF unchanged");
            using var cancel=new CancellationTokenSource(); Throws<OperationCanceledException>(()=>engine.Execute(new(second,ToolKind.PdfPng),cancel.Token,p=>{if(p==90)cancel.Cancel();}));
            Assert(!Directory.GetDirectories(_folder,".orbit-*").Any(),"folder staging removed after cancellation");
        });
        var docx=WriteDocx();
        Check("DOCX detection and UTF-8 text extraction", () =>
        {
            Assert(FileTypes.Detect(docx)==FileKind.Docx,"OOXML contents");
            var output=engine.Execute(new(docx,ToolKind.DocxText),CancellationToken.None);
            Assert(File.ReadAllText(output).Contains("Đại ca · Orbit"),"Unicode paragraphs");
        });
        Check("Generic ZIP and SHA-256 outputs preserve arbitrary source", () =>
        {
            var unknown=Path.Combine(_folder,"sample.bin"); File.WriteAllBytes(unknown,[0,1,2,3,4,255]); var hash=Hash(unknown);
            var zip=engine.Execute(new(unknown,ToolKind.Zip),CancellationToken.None); using var archive=ZipFile.OpenRead(zip);
            using var data=new MemoryStream(); using(var input=archive.Entries.Single().Open()) input.CopyTo(data);
            Assert(data.ToArray().SequenceEqual(File.ReadAllBytes(unknown)),"ZIP contents");
            var checksum=engine.Execute(new(unknown,ToolKind.Sha256),CancellationToken.None); Assert(File.ReadAllText(checksum).StartsWith(hash.ToLowerInvariant()),"checksum"); Assert(Hash(unknown)==hash,"source unchanged");
        });
        if(office) Check("Microsoft Word exports DOCX to readable PDF", () =>
        {
            Assert(OfficeConverter.Available(FileKind.Docx),"Word installed"); var before=Hash(docx);
            var pdf=engine.Execute(new(docx,ToolKind.OfficePdf),CancellationToken.None);
            Assert(FileTypes.Detect(pdf)==FileKind.Pdf,"PDF signature"); using var read=PdfReader.Open(pdf,PdfDocumentOpenMode.Import);
            Assert(read.PageCount==1,"Word page count"); Assert(Hash(docx)==before,"DOCX unchanged");
        });
        if(office) foreach(var kind in new[]{FileKind.Excel,FileKind.PowerPoint}) Check($"Microsoft {kind} exports a document to PDF", () =>
        {
            var input=WriteOfficeFixture(kind); var before=Hash(input);
            Assert(FileTypes.Detect(input)==kind,"OOXML detection");
            var pdf=engine.Execute(new(input,ToolKind.OfficePdf),CancellationToken.None);
            using var read=PdfReader.Open(pdf,PdfDocumentOpenMode.Import);
            Assert(read.PageCount==1,"Office page count"); Assert(Hash(input)==before,"Office source unchanged");
        });
    }
    private static string WriteDocx()
    {
        var path=Path.Combine(_folder,"document.docx"); using var zip=ZipFile.Open(path,ZipArchiveMode.Create);
        void Add(string name,string xml) { using var writer=new StreamWriter(zip.CreateEntry(name).Open(),new UTF8Encoding(false)); writer.Write(xml); }
        Add("[Content_Types].xml","<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
        Add("_rels/.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
        Add("word/document.xml","<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Đại ca · Orbit</w:t></w:r></w:p><w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/></w:sectPr></w:body></w:document>"); return path;
    }

    private static string WriteOfficeFixture(FileKind kind)
    {
        var excel=kind==FileKind.Excel;
        var path=Path.Combine(_folder,excel?"spreadsheet.xlsx":"presentation.pptx");
        if (!excel) { File.Copy(Path.Combine(AppContext.BaseDirectory,"Fixtures","presentation.pptx"),path); return path; }
        using var zip=ZipFile.Open(path,ZipArchiveMode.Create);
        void Add(string name,string xml) { using var writer=new StreamWriter(zip.CreateEntry(name).Open(),new UTF8Encoding(false)); writer.Write(xml); }
        const string rel="http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
        Add("_rels/.rels",$"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"{rel}officeDocument\" Target=\"{(excel?"xl/workbook.xml":"ppt/presentation.xml")}\"/></Relationships>");
        if(excel)
        {
            Add("[Content_Types].xml","<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
            Add("xl/workbook.xml","<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Orbit\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Add("xl/_rels/workbook.xml.rels",$"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"{rel}worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            Add("xl/worksheets/sheet1.xml","<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Orbit test</t></is></c><c r=\"B1\"><v>42</v></c></row></sheetData></worksheet>");
        }
        else
        {
            Add("[Content_Types].xml","<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/ppt/presentation.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml\"/><Override PartName=\"/ppt/slides/slide1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slide+xml\"/></Types>");
            Add("ppt/presentation.xml","<p:presentation xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><p:sldIdLst><p:sldId id=\"256\" r:id=\"rId1\"/></p:sldIdLst><p:sldSz cx=\"9144000\" cy=\"6858000\"/><p:notesSz cx=\"6858000\" cy=\"9144000\"/></p:presentation>");
            Add("ppt/_rels/presentation.xml.rels",$"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"{rel}slide\" Target=\"slides/slide1.xml\"/></Relationships>");
            Add("ppt/slides/slide1.xml","<p:sld xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/><p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"Orbit\"/><p:cNvSpPr txBox=\"1\"/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x=\"914400\" y=\"914400\"/><a:ext cx=\"6000000\" cy=\"1000000\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr><p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>Orbit PDF export</a:t></a:r></a:p></p:txBody></p:sp></p:spTree></p:cSld></p:sld>");
        }
        return path;
    }
}
