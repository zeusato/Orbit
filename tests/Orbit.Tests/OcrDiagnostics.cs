using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Orbit.App.Services;
using Orbit.Core;
using ImageMagick;
using Tesseract;

internal static partial class Program
{
    private static int DiagnoseOcrVariants(string input, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var language in new[] { "vie+eng", "vie" })
        {
            using var engine = new TesseractEngine(Path.Combine(AppContext.BaseDirectory, "tessdata"), language, EngineMode.LstmOnly);
            engine.SetVariable("user_defined_dpi", "300");
            foreach (var preparation in new[] { "gray", "color", "upscale" })
            {
                var clock = Stopwatch.StartNew();
                using var image = new MagickImage(input);
                if (preparation == "gray") image.ColorSpace = ColorSpace.Gray;
                if (preparation == "upscale") image.Resize(new Percentage(150));
                using var pixels = Pix.LoadFromMemory(image.ToByteArray(MagickFormat.Png));
                using var page = engine.Process(pixels, PageSegMode.Auto);
                var text = page.GetText();
                File.WriteAllText(Path.Combine(destination, language + "-" + preparation + ".txt"), text);
                Console.WriteLine($"{language}-{preparation}: {text.Length} characters, confidence {page.GetMeanConfidence():F3}, {clock.Elapsed.TotalSeconds:F1}s");
            }
        }
        return 0;
    }
    // Explicit local fixture only: copy to the diagnostics folder so the source
    // directory and the user's existing OCR results are never modified.
    private static int DiagnoseOcr(string input, string destination)
    {
        Directory.CreateDirectory(destination);
        var copy = Path.Combine(Path.GetFullPath(destination), Path.GetFileName(input));
        File.Copy(input, copy, false);
        var kind = FileTypes.Detect(copy);
        if (kind == FileKind.Pdf)
        {
            using (var pdf = UglyToad.PdfPig.PdfDocument.Open(copy))
                foreach (var page in pdf.GetPages()) foreach (var image in page.GetImages())
                    Console.WriteLine($"Embedded image={image.WidthInSamples}x{image.HeightInSamples}");
            foreach (var size in PDFtoImage.Conversion.GetPageSizes(File.ReadAllBytes(copy)))
            {
                var oldScale = Math.Min(300d / 72, 4000d / Math.Max(size.Width, size.Height));
                Console.WriteLine($"PDF points={size.Width}x{size.Height}; 0.3.0 raster={Math.Ceiling(size.Width * oldScale)}x{Math.Ceiling(size.Height * oldScale)}");
                Console.WriteLine($"Current raster={TextExtraction.PdfOcrSize(size.Width, size.Height)}");
            }
        }
        else { var bitmap = Engine.Load(copy); Console.WriteLine($"Image={bitmap.PixelWidth}x{bitmap.PixelHeight}"); }
        var clock = Stopwatch.StartNew();
        var output = new DocumentEngine().Execute(new(copy, kind == FileKind.Pdf ? ToolKind.PdfText : ToolKind.ImageText), CancellationToken.None,
            p => Console.WriteLine($"Progress={p}"));
        Console.WriteLine($"Output={output}\nCharacters={File.ReadAllText(output).Length}\nSeconds={clock.Elapsed.TotalSeconds:F1}");
        return 0;
    }
}
