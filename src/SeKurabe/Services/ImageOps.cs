using Avalonia;
using Avalonia.Platform;
using SkiaSharp;
using AvBitmap = Avalonia.Media.Imaging.Bitmap;

namespace SeKurabe.Services;

/// <summary>画像の読み込み・切り抜き・縮小・保存。すべて Bgra8888 / Premul で扱う。</summary>
public static class ImageOps
{
    public static readonly string[] SupportedExtensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp"];

    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>画素をデコードせずに縦横サイズだけ読む。</summary>
    public static (int Width, int Height) ReadSize(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
        using var codec = SKCodec.Create(fs) ?? throw new InvalidDataException("画像として読み込めません");
        return (codec.Info.Width, codec.Info.Height);
    }

    /// <summary>全画素をデコードする。ネットワーク越しでも速いよう、先にファイル全体をメモリに読む。</summary>
    public static SKBitmap Decode(string path)
    {
        var bytes = File.ReadAllBytes(path);
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("画像として読み込めません");
        var info = codec.Info.WithColorType(SKColorType.Bgra8888).WithAlphaType(SKAlphaType.Premul);
        var bmp = new SKBitmap(info);
        var result = codec.GetPixels(info, bmp.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            bmp.Dispose();
            throw new InvalidDataException($"デコードに失敗しました ({result})");
        }
        return bmp;
    }

    /// <summary>長辺が maxSide 以下になるよう縮小したコピーを作る（一覧表示用）。</summary>
    public static SKBitmap Downscale(SKBitmap src, int maxSide)
    {
        var scale = Math.Min(1.0, (double)maxSide / Math.Max(src.Width, src.Height));
        var w = Math.Max(1, (int)Math.Round(src.Width * scale));
        var h = Math.Max(1, (int)Math.Round(src.Height * scale));
        return Resample(src, new SKRect(0, 0, src.Width, src.Height), w, h, null);
    }

    /// <summary>
    /// src の rect（はみ出し可）を outW×outH に描く。大きく縮小するときは 1/2 ずつ縮めてから
    /// 最後にバイキュービックで仕上げ、エイリアスを抑える。はみ出し部分は透明（bg 指定時はその色）。
    /// </summary>
    public static SKBitmap Resample(SKBitmap src, SKRect rect, int outW, int outH, SKColor? bg)
    {
        // 1) まず切り抜き範囲を等倍（以上）の中間画像へ写す
        var curW = Math.Max(outW, (int)Math.Ceiling(rect.Width));
        var curH = Math.Max(outH, (int)Math.Ceiling(rect.Height));
        // 等倍で整数座標なら無補間コピー、そうでなければ線形
        bool exact = curW == (int)rect.Width && curH == (int)rect.Height
                     && rect.Left == MathF.Floor(rect.Left) && rect.Top == MathF.Floor(rect.Top);
        var cur = DrawScaled(src, rect, curW, curH,
            exact ? new SKSamplingOptions(SKFilterMode.Nearest) : new SKSamplingOptions(SKFilterMode.Linear), null);

        // 2) 目標の 2 倍以上ある間は半分ずつ縮める（2:1 の線形補間 = 2×2 平均）
        while (cur.Width >= outW * 2 && cur.Height >= outH * 2)
        {
            var next = DrawScaled(cur, new SKRect(0, 0, cur.Width, cur.Height), cur.Width / 2, cur.Height / 2,
                new SKSamplingOptions(SKFilterMode.Linear), null);
            cur.Dispose();
            cur = next;
        }

        // 3) 仕上げ（背景色はここで合成）
        if (cur.Width == outW && cur.Height == outH && bg is null) return cur;
        var result = DrawScaled(cur, new SKRect(0, 0, cur.Width, cur.Height), outW, outH,
            new SKSamplingOptions(SKCubicResampler.Mitchell), bg);
        cur.Dispose();
        return result;
    }

    static SKBitmap DrawScaled(SKBitmap src, SKRect srcRect, int w, int h, SKSamplingOptions sampling, SKColor? bg)
    {
        var dst = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(dst);
        canvas.Clear(bg ?? SKColors.Transparent);
        using var image = SKImage.FromBitmap(src);
        using var paint = new SKPaint { BlendMode = SKBlendMode.SrcOver };
        canvas.DrawImage(image, srcRect, new SKRect(0, 0, w, h), sampling, paint);
        canvas.Flush();
        return dst;
    }

    public static void SavePng(SKBitmap bmp, string path)
    {
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var tmp = path + ".tmp";
        using (var fs = File.Create(tmp)) data.SaveTo(fs);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>SKBitmap を Avalonia の Bitmap に変換（画素はコピーされる）。</summary>
    /// <summary>不透明（アルファが threshold より大きい）な部分を囲む最小の矩形。すべて透明なら空。</summary>
    public static unsafe SKRectI AlphaBounds(SKBitmap bmp, byte threshold)
    {
        if (bmp.ColorType != SKColorType.Bgra8888 && bmp.ColorType != SKColorType.Rgba8888)
        {
            using var copy = bmp.Copy(SKColorType.Bgra8888);
            return AlphaBounds(copy, threshold);
        }
        int w = bmp.Width, h = bmp.Height, stride = bmp.RowBytes;
        byte* basePtr = (byte*)bmp.GetPixels();
        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
        {
            byte* row = basePtr + (long)y * stride;
            int first = -1, last = -1;
            for (int x = 0; x < w; x++)
                if (row[x * 4 + 3] > threshold) { first = x; break; }
            if (first < 0) continue;
            for (int x = w - 1; x >= first; x--)
                if (row[x * 4 + 3] > threshold) { last = x; break; }
            if (y < minY) minY = y;
            maxY = y;
            if (first < minX) minX = first;
            if (last > maxX) maxX = last;
        }
        return maxX < 0 ? SKRectI.Empty : new SKRectI(minX, minY, maxX + 1, maxY + 1);
    }

    /// <summary>JPEG で保存する（透明部分は白で塗る）。</summary>
    public static void SaveJpeg(SKBitmap bmp, string path, int quality)
    {
        using var flat = new SKBitmap(new SKImageInfo(bmp.Width, bmp.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(flat))
        {
            c.Clear(SKColors.White);
            c.DrawBitmap(bmp, 0, 0);
        }
        var tmp = path + ".tmp";
        using (var data = flat.Encode(SKEncodedImageFormat.Jpeg, quality))
        using (var fs = File.Create(tmp))
            data.SaveTo(fs);
        File.Move(tmp, path, overwrite: true);
    }

    public static AvBitmap ToAvalonia(SKBitmap bmp)
    {
        if (bmp.ColorType != SKColorType.Bgra8888 || bmp.AlphaType != SKAlphaType.Premul)
            throw new ArgumentException("Bgra8888/Premul 以外は未対応です");
        return new AvBitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, bmp.GetPixels(),
            new PixelSize(bmp.Width, bmp.Height), new Vector(96, 96), bmp.RowBytes);
    }

    public static SKColor? ParseColor(string? text) =>
        SKColor.TryParse(text?.Trim() ?? "", out var c) ? c : null;
}
