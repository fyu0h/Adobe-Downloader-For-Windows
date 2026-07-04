namespace AdobeDownloader.Core.Cleanup;

/// <summary>清理动作类型（Windows）。对应原版 CleanupActionKind 的 Windows 化。</summary>
public enum CleanupActionKind
{
    RemovePath,     // 删除固定文件/目录
    RemoveGlob,     // 删除通配符匹配的文件/目录
    RegistryKey,    // 删除注册表键
    Service,        // 停止并删除 Windows 服务
    HostsClean,     // 从 hosts 移除 Adobe 相关行
    Credential,     // 删除凭据管理器里的 Adobe 凭据
}

/// <summary>一个清理目标（未解析的模板）。template 支持 %ENVVAR% 展开。</summary>
public sealed class CleanupTarget
{
    public CleanupOption Option { get; }
    public CleanupActionKind Kind { get; }
    public string Template { get; }
    public string Description { get; }

    public CleanupTarget(CleanupOption option, CleanupActionKind kind, string template, string description)
    {
        Option = option;
        Kind = kind;
        Template = template;
        Description = description;
    }

    public static CleanupTarget Path(CleanupOption o, string t, string d) => new(o, CleanupActionKind.RemovePath, t, d);
    public static CleanupTarget Glob(CleanupOption o, string t, string d) => new(o, CleanupActionKind.RemoveGlob, t, d);
    public static CleanupTarget Registry(CleanupOption o, string t, string d) => new(o, CleanupActionKind.RegistryKey, t, d);
    public static CleanupTarget Service(CleanupOption o, string name, string d) => new(o, CleanupActionKind.Service, name, d);
    public static CleanupTarget Hosts(CleanupOption o, string d) => new(o, CleanupActionKind.HostsClean, "hosts", d);
    public static CleanupTarget Credential(CleanupOption o, string filter, string d) => new(o, CleanupActionKind.Credential, filter, d);
}

/// <summary>解析后的一条计划项（确认存在、可执行）。</summary>
public sealed class CleanupPlanItem
{
    public CleanupOption Option { get; init; }
    public CleanupActionKind Kind { get; init; }
    public string Title { get; init; } = "";
    public string Target { get; init; } = "";      // 解析后的实际路径/服务名/注册表键
    public long EstimatedBytes { get; init; }
}

/// <summary>完整清理计划。</summary>
public sealed class CleanupPlan
{
    public IReadOnlyList<CleanupPlanItem> Items { get; init; } = new List<CleanupPlanItem>();
    public long EstimatedBytes => Items.Sum(i => i.EstimatedBytes);

    public IEnumerable<IGrouping<CleanupOption, CleanupPlanItem>> ByOption()
        => Items.GroupBy(i => i.Option);
}
