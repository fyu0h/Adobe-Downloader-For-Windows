using SharpCompress.Compressors.LZMA;

namespace AdobeDownloader.Core.Install;

/// <summary>
/// 解压 Adobe HyperDrive 包内的单个文件。当 application.json 的 CompressionType 为 Zip-Lzma2 时，
/// zip 里每个（非 .pimx）文件本身也是 [1 字节字典][裸 LZMA2 流]，必须解压后才是真实文件。
/// 对应原版 HDPIMMiniZipExtractor 的 extractHDPIMLZMA2Entry。
/// </summary>
public static class Lzma2FileDecompressor
{
    public const string ZipLzma2 = "zip-lzma2";

    public static bool IsZipLzma2(string? compressionType)
        => string.Equals(compressionType?.Trim(), ZipLzma2, StringComparison.OrdinalIgnoreCase);

    /// <summary>流式解压 srcPath（[dictByte][裸 LZMA2]）到 dstPath。</summary>
    public static void DecompressToFile(string srcPath, string dstPath)
    {
        var dir = Path.GetDirectoryName(dstPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var input = File.OpenRead(srcPath);
        if (input.Length == 0)
        {
            // 空文件按空处理
            File.Create(dstPath).Dispose();
            return;
        }

        var dictByte = input.ReadByte();
        if (dictByte < 0)
            throw new InvalidDataException($"文件为空，无法解压: {srcPath}");

        using var lzma = new LzmaStream(new[] { (byte)dictByte }, input, -1, -1, presetDictionary: null, isLzma2: true);
        using var output = File.Create(dstPath);
        lzma.CopyTo(output);
    }
}
