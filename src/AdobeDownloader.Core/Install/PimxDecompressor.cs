using System.Text;
using SharpCompress.Compressors.LZMA;

namespace AdobeDownloader.Core.Install;

/// <summary>
/// 读取并解压 Adobe 的 .pimx 安装清单，对应原版 PIMXParser.loadXMLData + HDPIMNativeLZMA2。
/// pimx 若不是明文 XML，则为 [1 字节 LZMA2 字典大小][裸 LZMA2 流]，解压后即 UTF-8 XML。
/// </summary>
public static class PimxDecompressor
{
    public static string LoadXml(string pimxPath)
        => LoadXml(File.ReadAllBytes(pimxPath));

    public static string LoadXml(byte[] data)
    {
        if (data.Length == 0)
            throw new InvalidDataException("pimx 为空");

        // 已是明文 XML？以首个非空白字节判断（压缩数据里也可能出现 '<'，不能用 contains）
        if (StartsWithXml(data))
            return Encoding.UTF8.GetString(data);

        // [dictByte][裸 LZMA2 流]
        var props = new byte[] { data[0] };
        using var input = new MemoryStream(data, 1, data.Length - 1);
        using var lzma = new LzmaStream(props, input, -1, -1, presetDictionary: null, isLzma2: true);
        using var output = new MemoryStream();
        lzma.CopyTo(output);
        var xml = Encoding.UTF8.GetString(output.ToArray());

        if (!xml.TrimStart().StartsWith('<'))
            throw new InvalidDataException("pimx 解压结果不是有效 XML");
        return xml;
    }

    /// <summary>首个非空白字节是否为 '&lt;'（含可选 UTF-8 BOM）。</summary>
    private static bool StartsWithXml(byte[] data)
    {
        var i = 0;
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            i = 3; // UTF-8 BOM
        while (i < data.Length && (data[i] == 0x20 || data[i] == 0x09 || data[i] == 0x0A || data[i] == 0x0D))
            i++;
        return i < data.Length && data[i] == (byte)'<';
    }
}
