# Adobe Downloader for Windows — 设计文档

> 目标：将 macOS 版 [X1a0He/Adobe-Downloader](https://github.com/X1a0He/Adobe-Downloader) 移植为 Windows 版本。
> 日期：2026-07-04

## 1. 背景与范围

macOS 原版是一个 SwiftUI 应用（约 130 个 Swift 文件），核心分为两大块：

1. **下载**（可移植）：从 Adobe 官方 CDN 获取产品目录、版本、依赖、下载包，并生成 `driver.xml`。
2. **安装**（macOS 专属）：`HDPIM` 目录约 80 个文件，是对 Adobe HyperDrive 安装器的**重新实现**（LZMA2 解压、bspatch、包解压、特权 Helper、安装到 `/Applications`、launchd、keychain 等），高度依赖 macOS。

### 关键决策：Windows 上不需要重写安装器

在 Windows 上，Adobe 产品由 Adobe 官方的 `Setup.exe`（HyperDrive 安装器，随 Creative Cloud 安装）读取 `driver.xml` 完成安装。这也是社区工具（adobe-packager / CCMaker）的标准做法。

因此 Windows 版聚焦于：**目录客户端 + 下载引擎 + driver.xml 生成 + 图形界面**，安装环节委托给 Adobe 官方 `Setup.exe`（若存在则调用，否则提示用户）。这与 macOS 原版"下载与安装分离"的架构一致。

## 2. Adobe 下载协议（从原版逆向梳理）

### 2.1 产品目录
```
GET https://prod-rel-ffc-ccm.oobesaas.adobe.com/adobe-ffc-external/core/v6/products/all
    ?channel=ccm&channel=sti&channel=nocc
    &platform=win64&platform=winarm64   # Windows 平台（原版是 macarm64,macuniversal,osx10-64,osx10）
    &payload=true&productType=Desktop&_type=xml
Headers:
    x-adobe-app-id: accc-hdcore-desktop
    x-api-key: Creative Cloud_v6_4
    x-adobe-app-version: 6.8.1.856
    User-Agent: Creative Cloud/6.8.1.856/Win-10.0
```
返回 XML：`/response/channels/channel[@name='ccm']/products/product`。
- 每个 product：`id`(SapCode)、`version`、`displayName`、`productIcons`、`platforms/platform`。
- 每个 platform：`id`(win64/winarm64)、`languageSet`（含 `buildGuid`、`baseVersion`、`productVersion`、`dependencies/dependency`）。
- CDN 基址：`/response/channels/channel[1]/cdn/secure`。

### 2.2 应用包信息（application.json）
```
GET https://cdn-ffc.oobesaas.adobe.com/core/v3/applications?name=<SapCode>&version=<version>&platform=<platform>
Headers: x-adobe-app-id, x-api-key, User-Agent, x-adobe-build-guid: <buildGuid>
```
返回 JSON：`Packages/Package[]`，每个包含 `PackageName`/`fullPackageName`、`Path`（相对 CDN 的下载路径）、`Type`（core/noncore）、`DownloadSize`、`Condition`、`ProcessorFamily`。

### 2.3 下载
每个包的下载地址 = `CDN + Path`（若 Path 已是绝对 URL 则直接用）。保存到 `<下载目录>/<SapCode>/<fullPackageName>`。

### 2.4 driver.xml
生成安装描述文件，包含 `ProductInfo`(SapCode/CodexVersion/BaseVersion/BuildVersion/Platform/BuildGuid/Dependencies) 与 `RequestInfo`(InstallDir/InstallLanguage/TargetArchitecture)。Windows 下 InstallDir 用 Windows 路径，Platform 用 win64/winarm64。

## 3. 技术选型

- **语言/框架**：C# / .NET 10 + WPF（本机已装 .NET 10 SDK，`dotnet build` 可直接构建运行；WPF 内置于 Windows SDK，无需额外 workload）。
- **架构**：MVVM。分为两个工程：
  - `AdobeDownloader.Core`（类库）：Adobe API 客户端、目录 XML 解析、application.json 解析、driver.xml 生成、下载引擎、数据模型。纯逻辑，可单元测试。
  - `AdobeDownloader.App`（WPF）：产品列表、版本选择、语言选择、下载进度、设置、调用 Setup.exe。
  - `AdobeDownloader.Core.Tests`（xUnit）：核心解析与生成逻辑测试。

## 4. 模块划分（Core）

| 模块 | 职责 | 依赖 |
|------|------|------|
| `Models/*` | Product/Platform/LanguageSet/Dependency/Package/DownloadTask 等数据模型 | 无 |
| `NetworkConstants` | Adobe 端点、请求头 | 无 |
| `AdobeApiClient` | HTTP 调用目录与 application.json | HttpClient, Models |
| `CatalogParser` | 解析 products/all XML → Product[] + CDN | Models |
| `ApplicationParser` | 解析 application.json → Package[] | Models |
| `DriverXmlGenerator` | 生成 driver.xml | Models |
| `DownloadEngine` | 并发/可续传下载、进度、校验 | AdobeApiClient |
| `SetupLocator` | 定位 Adobe Setup.exe，触发安装 | 无 |

## 5. 数据流

用户选产品 → `AdobeApiClient.FetchCatalog()` 得到产品列表 → 选版本/语言 → 对主产品及每个依赖调用 `AdobeApiClient.FetchApplicationInfo(buildGuid)` 得到包列表 → `DownloadEngine` 并发下载所有包到 `<dir>/<SapCode>/` → `DriverXmlGenerator` 写 `driver.xml` → 用户点"安装"→ `SetupLocator` 调用 `Setup.exe`（找不到则提示手动）。

## 6. 错误处理

- 网络：重试（对目录 3 次、application.json 3 次，指数退避），HTTP 非 2xx 抛带状态码异常。
- "Build is not operational" → 该版本已被 Adobe 撤销，明确提示。
- 下载：分块写入临时文件，完成后校验大小，再重命名；支持断点续传（Range）。
- 磁盘/权限错误向 UI 汇报，任务标记 failed 且可重试。

## 7. 里程碑

- **v0.1** Core：模型 + 常量 + 目录/应用解析 + driver.xml 生成 + 单元测试。
- **v0.2** Core：AdobeApiClient + DownloadEngine（并发、进度、续传、校验）。
- **v0.3** App：WPF 界面（产品列表、版本/语言选择、下载进度）。
- **v0.4** App：设置（下载目录、语言、架构、API 版本）、Setup.exe 安装集成。
- **v1.0** 打磨、图标、错误提示、README、构建发布单文件 exe。

## 8. 明确不做（YAGNI / 平台不符）

- 不重写 HDPIM 安装器（Windows 用官方 Setup.exe）。
- 首版不做清理（Cleanup）、卸载、增量更新、Apple 特有的 Dock 进度等。
- 不做 Adobe 账号登录（原版也可选；目录接口匿名可用）。

## 9. 安装环节的真实验证与结论（2026-07-05 更新）

初版设计假设「Windows 上直接调用 Adobe 官方 Setup.exe 安装」。真机验证后此假设**不成立**，并进一步探明了自实现安装器的可行性。

### 9.1 官方 Setup.exe 授权墙（已证实不可用）
以管理员身份用 `Setup.exe --install=1 --driverXML=<driver.xml>` 调用，日志（`C:\Program Files (x86)\Common Files\Adobe\Adobe Desktop Common\HDBox\Installers\Install.log`）报：
```
[FATAL] Adobe Setup is not Authorized   Exit Code: -1
```
新版 HyperDrive Setup（6.4.0.359）加了调用方授权校验，拒绝第三方命令行直连。这条路对新版环境走不通。

### 9.2 原版 macOS 如何安装（fork 来源）
原版**不调用 Adobe Setup**，`InstallManager.getInstallCommand` 直言「HDPIM Engine（内置安装引擎，无需外部命令）」。它逆向重写了 Adobe HyperDrive 安装器（`upstream/.../HDPIM/`）：解密/解压包内 `.pimx` 清单 → `HDPIMCommandEngine` 逐条执行（文件部署、bspatch delta、权限、许可）→ 经 root 特权 Helper 落地到 /Applications。因此不受授权墙影响。

### 9.3 PIMX 可行性探路（已实测，结论积极）
对本机已下载的 `AdobeBridge16.0-mul-x64.pimx` 实测：
- **格式**：首字节 `0x18` 为 LZMA2 字典大小字节，其后为裸 LZMA2 流。跳过首字节 + `dict_size=64MiB` 即可解压得到 **25 KB 明文 XML 清单**（样本见 `docs/samples/AdobeBridge16.0-mul-x64.pimx.xml`）。**非加密**，无需破解。
- **清单本就是 Windows 语义**：路径用反斜杠与 `[INSTALLDIR]`/`[StagingFolder]`/`[AdobeCommon]` 变量，注册表用 `HKEY_*`。
- **Bridge 安装 = 纯标准 Windows 操作**：
  - `<Assets>`（5）：把 Staging 子目录拷到 `[INSTALLDIR]`/`[AdobeCommon]`
  - `<Registry>`（74）：文件关联/CLSID/MTP/shell 命令，写 HKCR/HKLM
  - `<Permission>`（2）：注册表项 ACL（Everyone: GENERIC_READ）
  - `<Shortcut>`（1）：开始菜单快捷方式（多语言名）
  - `<FolderIcon>`（1）：文件夹图标
  - 未见在线激活/AMT 调用（Bridge 免费）

### 9.4 移植 HDPIM 到 Windows 的评估
可行，且比 macOS 版更直接（无需破解、无 bspatch/ditto、只需管理员权限而非 root helper）。需实现：
1. LZMA2 解压（引 SharpCompress 或 7-Zip SDK）
2. PIMX 解析（XML → 命令列表）
3. 变量替换表（INSTALLDIR/StagingFolder/AdobeCommon/StartMenuSubFolder…）
4. 命令执行引擎：文件部署、注册表写入（Microsoft.Win32.Registry）、权限（ACL/icacls）、快捷方式（.lnk）、文件夹图标
5. driver.xml → 逐包定位/解压 pimx → 建立 Staging → 执行；应用 manifest 声明 requireAdministrator
风险点：重型产品（PS/AE）清单可能含 `runProgram`（AMT 激活、VC 运行库）等更复杂命令，需逐个逆向；Bridge 这类可较快跑通。
