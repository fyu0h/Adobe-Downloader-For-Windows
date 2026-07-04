using System.Text.Json;
using AdobeDownloader.Core.Models;

namespace AdobeDownloader.Core.Parsing;

/// <summary>
/// 解析 application.json，对应原版 ApplicationJSONParser。
/// 注意 Adobe 的 JSON 里"数组"在只有一个元素时会退化成对象，需两者兼容。
/// </summary>
public static class ApplicationParser
{
    public static ApplicationInfo Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var info = new ApplicationInfo
        {
            RawJson = json,
            SapCode = Str(root, "SAPCode") ?? Str(root, "sapCode") ?? "",
            CompressionType = Str(root, "CompressionType") ?? "",
            DisplayName = Str(root, "Name") ?? Str(root, "ProductName") ?? Str(root, "DisplayName") ?? "",
            CodexVersion = Str(root, "CodexVersion") ?? "",
            ProductVersion = Str(root, "ProductVersion") ?? "",
            BaseVersion = Str(root, "BaseVersion") ?? "",
        };
        if (info.CodexVersion.Length == 0) info.CodexVersion = info.ProductVersion;

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("InstallDir", out var idir) &&
            idir.ValueKind == JsonValueKind.Object)
        {
            info.InstallDir = Str(idir, "value") ?? "";
            info.InstallDirFixed = string.Equals(Str(idir, "isFixed"), "true", StringComparison.OrdinalIgnoreCase);
        }

        foreach (var dep in ArrayAt(root, "SoftDependencies", "Dependency"))
        {
            var sap = Str(dep, "SAPCode");
            if (!string.IsNullOrEmpty(sap)) info.SoftDependencies.Add(sap);
        }

        var seenLang = new HashSet<string>();
        foreach (var lang in ArrayAt(root, "SupportedLanguages", "Language"))
        {
            var locale = Str(lang, "locale");
            if (!string.IsNullOrEmpty(locale) && seenLang.Add(locale))
                info.SupportedLanguages.Add(locale);
        }

        foreach (var pkg in ArrayAt(root, "Packages", "Package"))
            info.Packages.Add(ParsePackage(pkg));

        foreach (var mod in ArrayAt(root, "Modules", "Module"))
        {
            var m = new AppModule { Id = Str(mod, "Id") ?? "" };
            foreach (var rp in ArrayAt(mod, "ReferencePackages", "ReferencePackage"))
                if (rp.ValueKind == JsonValueKind.String) m.ReferencePackages.Add(rp.GetString()!);
            info.Modules.Add(m);
        }

        return info;
    }

    private static AppPackage ParsePackage(JsonElement json)
    {
        var pkg = new AppPackage
        {
            PackageName = Str(json, "PackageName") ?? "",
            FullPackageName = Str(json, "fullPackageName") ?? "",
            ProcessorFamily = Str(json, "ProcessorFamily") ?? "",
            DownloadSize = Long(json, "DownloadSize"),
            ExtractSize = Long(json, "ExtractSize"),
            InstallSequenceNumber = (int)Long(json, "InstallSequenceNumber"),
            Path = Str(json, "Path") ?? "",
            PackageVersion = Str(json, "PackageVersion") ?? "",
            Condition = Str(json, "Condition") ?? "",
            PackageHashKey = Str(json, "packageHashKey") ?? "",
            IsShared = Bool(json, "IsShared") || Bool(json, "isShared"),
        };

        var type = Str(json, "Type") ?? "";
        pkg.Type = type.Length == 0 ? "noncore" : type;

        if (pkg.FullPackageName.Length == 0 && pkg.PackageName.Length > 0)
            pkg.FullPackageName = pkg.PackageName.EndsWith(".zip") ? pkg.PackageName : pkg.PackageName + ".zip";

        pkg.ValidationUrlType1 = FirstStr(json, "ValidationURL", "validationURL", "validationUrl", "validation_url");
        pkg.ValidationUrlType2 = FirstStr(json, "ValidationURLType2", "validationURLType2", "validationUrlType2");
        if (json.TryGetProperty("ValidationURLs", out var vurls) && vurls.ValueKind == JsonValueKind.Object)
            pkg.ValidationUrlType2 = FirstStr(vurls, "TYPE2", "Type2", "type2") ?? pkg.ValidationUrlType2;

        foreach (var f in ArrayAt(json, "Features", "Feature"))
        {
            var name = Str(f, "Name");
            if (!string.IsNullOrEmpty(name)) pkg.Features.Add(name);
        }

        return pkg;
    }

    // ---- JSON 辅助方法（兼容数组退化为对象、数字退化为字符串）----

    /// <summary>取 obj[parent][child]，无论它是数组、单个对象还是缺失，都返回可枚举。</summary>
    private static IEnumerable<JsonElement> ArrayAt(JsonElement obj, string parent, string child)
    {
        if (obj.ValueKind != JsonValueKind.Object) yield break;
        if (!obj.TryGetProperty(parent, out var p) || p.ValueKind != JsonValueKind.Object) yield break;
        if (!p.TryGetProperty(child, out var c)) yield break;

        if (c.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in c.EnumerateArray()) yield return item;
        }
        else
        {
            yield return c;
        }
    }

    private static string? Str(JsonElement obj, string key)
    {
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out var v) &&
            v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString()?.Trim();
            return string.IsNullOrEmpty(s) ? null : s;
        }
        return null;
    }

    private static string? FirstStr(JsonElement obj, params string[] keys)
    {
        foreach (var k in keys)
        {
            var s = Str(obj, k);
            if (s is not null) return s;
        }
        return null;
    }

    private static long Long(JsonElement obj, string key)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(key, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetInt64(out var n) ? n : 0,
            JsonValueKind.String => long.TryParse(v.GetString(), out var s) ? s : 0,
            _ => 0,
        };
    }

    private static bool Bool(JsonElement obj, string key)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(key, out var v)) return false;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => v.TryGetInt64(out var n) && n != 0,
            JsonValueKind.String => v.GetString()?.Trim().ToLowerInvariant() is "true" or "1" or "yes",
            _ => false,
        };
    }
}
