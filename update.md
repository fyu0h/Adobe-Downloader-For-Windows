# 更新日志 (update.md)

本文档记录 Adobe Downloader for Windows 每次修改涉及的版本号、文件与改动。

---

## v1.6.1 — 2026-07-05（修复：打开软件首次下载偶发"获取 application.json 失败"）

**现象**：打开软件后第一次下载某产品（如 Audition）偶尔报"创建下载任务失败：获取 application.json 失败"，再点一次就好。

**原因**：目录做了本地缓存后，打开软件后**第一个真正的联网请求就是抓 application.json**（host `cdn-ffc.oobesaas.adobe.com`）。该首次"冷连接"偶发抖动（连接重置/超时/DNS）；原重试退避为 5s、10s，首次重试要等 5 秒，太慢，偶尔没覆盖住抖动窗口就整体失败；手动再点时抖动已过便成功。请求头是静态的（无会话依赖），故非会话问题。

**修复**：application.json 重试改为**快速退避 1s/2s/4s/8s**、次数 3→5，让首连抖动在同一次点击内自动恢复；并把失败的内层原因（超时/连接重置/HTTP 码）带入报错信息，便于定位。

### 涉及文件
- 改 `Core/AdobeApiClient.cs`（`FetchApplicationInfoAsync` 退避 `1<<attempt`、报错含 inner 详情）、`Core/NetworkConstants.cs`（`MaxServiceCallRetries` 3→5）。

---

## v1.6.0 — 2026-07-05（安装引擎支持 RunProgram：自动安装 VC++ 运行库）

补上全量安装的已知缺口之一：**VC++ 运行库**。重型 Adobe 产品依赖它，缺失会导致装好后 exe 无法启动。

**机制**（真机解包 VCRedist14-64.pimx 确认）：该依赖包用 `<RunProgram><InstallCommand isThirdParty="true">` 运行 `[StagingFolder]/VC_redist.x64.exe /q /norestart`，且其 `<Asset ignoreAsset="true">` 表示不部署、直接从 staging 运行；包级 `<Condition>[OSProcessorFamily]==64-bit</Condition>`。真机确认该 exe 是有效 PE（微软 VC++ v14 14.50）。

**实现**：
- pimx 解析新增 `RunProgram/InstallCommand`（Path + Arguments + isThirdParty）、Asset 的 `ignoreAsset`、包级 `Condition`。
- 安装引擎：部署时跳过 `ignoreAsset` 资源；文件部署后按包条件执行 RunProgram（运行 exe、等待退出、判定退出码：0/3010/1641/1638 视为成功）。条件用已有 `ConditionEvaluator` 求值；`InstallProperties` 新增 `[OSProcessorFamily]=64-bit`、`[OSArchitecture]` 变量。
- 已装 VC 时再跑返回 1638（已安装），按成功处理；全新机器则真正装上 VC 运行库。

### 涉及文件
- 改 `Core/Install/PimxModels.cs`（`PimxRunProgram`、`PimxAsset.IgnoreAsset`、`PimxPackage.RunPrograms/Condition`）、`PimxParser.cs`（解析 RunProgram/ignoreAsset/Condition）、`InstallProperties.cs`（`architecture` 入参、`OSProcessorFamily`/`OSArchitecture` 变量、`EvaluateCondition`）。
- 改 `App/Install/WindowsInstaller.cs`（跳过 ignoreAsset、执行 RunProgram、传 architecture）。
- 加 `tests/AdobeDownloader.Core.Tests/InstallTests.cs` 两个回归测试。

---

## v1.5.0 — 2026-07-05（卸载增强：扫描磁盘产品目录 + 强制删除，解决自装产品扫不到/Adobe 卸载器失效）

**背景**：真机发现两个现象——① Premiere Pro 2026、Illustrator 2026 等用本工具（v1.4.0 之前）装的产品**扫不到**（当时没写 ARP 卸载项）；② Media Encoder 2026 卸载报 “Specified product is not installed”——那是 **Adobe 官方卸载器**弹的，因为本引擎只铺文件、不更新 Adobe 自己的产品数据库，Adobe 卸载器查不到记录。根因：本引擎装东西不进 Adobe 数据库，早期也不写 Windows 卸载项。

**做法**：
- **卸载列表增加磁盘扫描**：除读注册表卸载项外，再扫描 `Program Files\Adobe` 与 `Program Files (x86)\Adobe` 下的产品目录（名字以 Adobe 开头、含 exe，排除 Desktop Common/Creative Cloud/Sync/GCClient 等基础组件）。注册表已有同名项则补全其安装目录；注册表没有的作为“仅可强制删除”项补入。这样 PR/Illustrator 等自装产品也会出现。
- **强制删除**：对有安装目录的产品提供「强制删除」——删安装目录 + 指向它的快捷方式（扫开始菜单/桌面 .lnk 按目标匹配）+ 残留卸载注册表项（三处按 DisplayName/InstallLocation 匹配删除）。带安全校验（须存在、够深、含 Adobe、无未解析变量）。`--forceremove "<目录>"` 模式，非管理员调用时自提权。
- 适用：厂商卸载器失效（如 AME）或列表本无卸载项（如 PR）的情况。exe 枚举用 `IgnoreInaccessible` 避免受限子目录误判。

### 涉及文件
- 新增 `App/Uninstall/ShortcutFinder.cs`（找指向某目录的快捷方式）。
- 改 `App/Uninstall/InstalledAppScanner.cs`（磁盘产品扫描+合并）、`InstalledApp.cs`（`CanForceRemove`）、`InstalledAppViewModel.cs`（暴露）、`ProductUninstaller.cs`（`ForceRemove`）、`Install/InstallRegistry.cs`（`RemoveArpEntriesFor` + 空键名护栏）。
- 改 `App/UninstallWindow.xaml`+`.cs`（「强制删除」按钮与处理）、`UninstallRunWindow.xaml.cs`（强制删除模式）、`App.xaml.cs`（`--forceremove` 自提权）。

---

## v1.4.3 — 2026-07-05（简化删除任务确认框：去掉“否”，改为删除并删文件/取消）

删除任务确认框原为「是/否/取消」三选项（是=删任务+删文件，否=仅移除保留文件，取消=不删）。按需求去掉“否”，简化为两选项：**确定=删除任务并删除已下载文件**，**取消=不删除**。

### 涉及文件
- 改 `App/ViewModels/DownloadTaskViewModel.cs`：`Remove()` 弹窗由 `YesNoCancel` 改为 `OKCancel`，确认即删任务并 `DeleteDownloadedFiles()`。

---

## v1.4.2 — 2026-07-05（统一弹窗风格：自定义 AppDialog 替换系统 MessageBox）

**问题**：删除任务确认框等用的是系统原生 `MessageBox`（灰色系统样式、系统字体），与程序自定义风格（圆角面板、品牌红、雅黑）不一致。

**做法**：新增与程序风格统一的 `AppDialog`（无边框圆角面板 + 投影、按严重程度显示彩色徽标、品牌红主按钮、雅黑字体），提供与 `MessageBox.Show` 同签名、返回 `MessageBoxResult` 的静态方法，逐处替换（共 13 处 / 5 文件），改动最小。

- 支持 OK/OKCancel/YesNo/YesNoCancel 四种按钮组合，肯定按钮为品牌红主按钮并设为默认（回车）；取消/否设为 IsCancel（Esc）。
- 徽标：错误(红✕)/警告(琥珀!)/询问·信息(蓝)；正文过长可滚动（MaxHeight 360）；居中于所属窗口，可拖动。
- 已真机截图验证外观与程序一致。

### 涉及文件
- 新增 `App/AppDialog.xaml` + `.cs`。
- 改 `App/ViewModels/DownloadTaskViewModel.cs`、`App/ViewModels/MainViewModel.cs`、`App/UninstallWindow.xaml.cs`、`App/CleanupWindow.xaml.cs`、`App/MainWindow.xaml.cs`：`MessageBox.Show` → `AppDialog.Show`。

---

## v1.4.1 — 2026-07-05（版本号显示大众熟知的年份，如 30.6 → 2026 (30.6)）

**需求**：版本下拉里显示的是 26.5 / 30.6 / 12.1.0 这种技术版本号，看不出对应 2024/2025/2026。

**做法**：目录数据里多数主力产品名不含年份（仅 Photoshop Elements 等含），故用「SapCode → 偏移」映射把主版本号换算成营销年份：年份 = 主版本号 + 偏移。偏移经真机已装产品年份反向校验（AE 24=2024、ILST 28=2024、PS 25=2024、AME 26=2026、Bridge 16=2026）。

- 换算表：Photoshop +1999、Illustrator +1996、After Effects/Premiere/Media Encoder/Audition/Animate/Character Animator/Dreamweaver +2000、InDesign/InCopy +2005、Bridge +2010、Lightroom Classic +2011。
- SapCode 末尾 `BETA` 会被识别（如 PPROBETA → Premiere Pro，标签追加 “ Beta”）。
- 未知产品或异常版本号只显示原始版本号（年份护栏 2015–2099）。
- 版本下拉显示为 “2026 (30.6)” 形式（`Product.VersionDisplay`）。

### 涉及文件
- 新增 `Core/ProductVersionNaming.cs`（换算器）、`tests/.../ProductVersionNamingTests.cs`。
- 改 `Core/Models/Product.cs`（新增 `VersionDisplay`）、`App/MainWindow.xaml`（版本下拉 `DisplayMemberPath` 改为 `VersionDisplay`）。

---

## v1.4.0 — 2026-07-05（内置引擎安装后写系统卸载(ARP)项，自装产品可在系统与本工具卸载）

补上此前的已知限制：自装产品现在会登记到 Windows「添加/删除程序」，既出现在系统“应用”列表，也出现在本工具卸载列表，并可真正卸载。

- **安装时登记 ARP**：`WindowsInstaller` 安装完成后收集本引擎创建的快捷方式与主产品安装目录，写入 `HKLM\...\Uninstall\AdobeDownloader.<SapCode>.<版本>`（DisplayName/DisplayVersion/Publisher=Adobe Inc./DisplayIcon=主 exe/InstallLocation/EstimatedSize/NoModify/NoRepair），UninstallString 指向本程序 `--uninstall "<记录JSON>"`。同时写安装记录 JSON 到 `%ProgramData%\AdobeDownloader\installed\`。
- **自带卸载器**：`--uninstall <记录路径>` 模式按记录回删快捷方式、安装目录（清只读/系统属性后递归删，带路径安全校验：须存在、够深、含 Adobe、无未解析变量）、ARP 注册表项与记录 JSON。不逐条回删 pimx 写入的大量注册表（交给“🧹 清理”）。
- **自提权**：`--uninstall` 可能被系统“应用”列表以非管理员调用，故检测到未提权时用 runas 重启自身。
- 本工具卸载列表因 Publisher=Adobe Inc. 会自动收录这些项；点卸载即 runas 执行上述卸载命令。

### 涉及文件
- 新增 `Core/Install/InstallRecord.cs`（安装记录模型）、`App/Install/InstallRegistry.cs`（写/读/删 ARP + 记录）、`App/Uninstall/ProductUninstaller.cs`（回删逻辑）、`App/UninstallRunWindow.xaml`+`.cs`（卸载执行窗口）。
- 改 `App/Install/WindowsInstaller.cs`（收集快捷方式/安装目录，安装后 `WriteArpRecord`；`CreateShortcut` 改实例并记录）、`App/App.xaml.cs`（`--uninstall` 模式 + `IsElevated`/`RelaunchElevated` 自提权）。

> 注：本次改动前已安装的产品（如你现有的 Illustrator 2026）没有 ARP 记录，不会出现在卸载列表；重新用本工具安装一次即可补上登记。

---

## v1.3.2 — 2026-07-05（修复：安装后开始菜单看不到应用图标 —— [StartMenu] 变量未定义）

**问题**：用内置引擎安装 Adobe Illustrator 2026 后，开始菜单/桌面看不到应用图标（虽然 Illustrator.exe 已正确部署、是有效 64MB PE 可运行）。

**根因**（真机诊断）：Illustrator 核心 pimx 的开始菜单快捷方式 `<Directory>` 是 `[StartMenu]`，但 `InstallProperties` 变量表里**只定义了 `StartMenuSubFolder`，没有 `StartMenu`**。于是 `[StartMenu]` 没被替换，`CreateShortcut` 里 `Directory.CreateDirectory("[StartMenu]")` 在安装进程工作目录（提权重启自身时为 `publish\`）下建了一个名为 `[StartMenu]` 的垃圾目录，快捷方式（目标/图标其实都正确）被塞进那里，真正的开始菜单自然看不到。已在 `publish\[StartMenu]\Adobe Illustrator 2026.lnk` 实锤。

**修复**：
- `Core/Install/InstallProperties.cs`：新增 `[StartMenu]`/`[Programs]`/`[CommonPrograms]`（=所有用户开始菜单\Programs，`SpecialFolder.CommonPrograms`）与 `[Desktop]`/`[DesktopFolder]`（=公共桌面）变量。
- `App/Install/WindowsInstaller.cs`：`CreateShortcut` 增加防护——解析后的目录/目标若仍含未解析变量 `[`，抛错中止（由 `TryDo` 记为警告），避免再建出 `[Xxx]` 垃圾目录。
- 已为你当前已装的 Illustrator 2026 手动把快捷方式补到开始菜单，并删除 `publish\[StartMenu]` 垃圾目录。
- 新增回归测试 `InstallPropertiesTests.ForWindows_DefinesStartMenuAndShortcutVars`。

> 附带说明：我们的内置引擎目前不写 Windows“添加/删除程序(ARP)”注册表项（Adobe 官方靠 Setup 后置步骤写），故自装的产品不会出现在系统“应用”列表或本工具的卸载列表里——这是与图标无关的已知限制，后续可单独补。

### 涉及文件
- 改 `Core/Install/InstallProperties.cs`、`App/Install/WindowsInstaller.cs`；加 `tests/AdobeDownloader.Core.Tests/InstallTests.cs` 回归测试。

---

## v1.3.1 — 2026-07-05（卸载/清理图标改用 SVG 矢量图标）

把顶栏「卸载」「清理」等处的 emoji（🗑/🧹）替换为 SVG 矢量图标（清晰、随缩放不糊、随主题变色）。WPF 原生不渲染 .svg 文件，故将 SVG 的 path 数据直接作为 WPF `Path.Data`（同一套路径迷你语言），无需额外依赖。

- 卸载图标 = 垃圾桶，清理图标 = 扫帚，均为 24×24 视图的矢量 path。
- 图标颜色随所在按钮的前景色/主题（主按钮内为白色，头部标题为品牌红 Accent）。

### 涉及文件
- `App/App.xaml`：新增两个 `Geometry` 资源 `IconUninstall`（垃圾桶）、`IconCleanup`（扫帚）。
- `App/MainWindow.xaml`：顶栏「卸载」「清理」按钮改为 `Path` 矢量图标 + 文本。
- `App/UninstallWindow.xaml`、`App/CleanupWindow.xaml`：窗口标题及「开始清理」按钮的 emoji 换为矢量图标。

> 注：本次改动编译通过（0 错误）；发布 exe 时若上一版实例（尤其以管理员身份运行的）仍开着会锁定文件，需先关闭再 `dotnet publish`。

---

## v1.3.0 — 2026-07-05（独立卸载功能：后台扫描已安装程序 + 带图标按版本单独卸载）

新增独立的“卸载已安装程序”功能，可只卸载某一个版本（如仅卸载 Premiere 2024）。

- **启动后台扫描并存储结果**：软件启动时后台扫描 Windows 卸载注册表项（HKLM 64/32 位 + HKCU），筛出 Adobe 程序，结果存到内存并写盘 `%AppData%\AdobeDownloader\installed-apps.json`；卸载窗口打开即读存储结果，秒开无需等待。
- **带程序对应图标**：从注册表 `DisplayIcon`（指向 exe/ico）用 `shell32.ExtractIconEx` 提取每个程序自己的图标显示在列表中（未引入 System.Drawing 依赖）。
- **按版本单独卸载**：每个 Adobe 版本在注册表各注册一条（带各自 sapCode + productVersion），列表中每行一个“卸载”按钮，点击后调用该程序自带的卸载命令（`Uninstaller.exe --uninstall=1 --sapCode=... --productVersion=...`），以管理员权限（runas）运行——委托厂商卸载器，安全可靠。
- 顶栏新增“🗑 卸载”按钮；卸载窗口含“⟳ 重新扫描”。

### 涉及文件
- 新增 `App/Uninstall/InstalledApp.cs`（模型）、`InstalledAppScanner.cs`（注册表扫描）、`InstalledAppsStore.cs`（内存+磁盘存储）、`AppIconExtractor.cs`（ExtractIconEx 提图标）、`InstalledAppViewModel.cs`（行视图+异步图标）。
- 新增 `App/UninstallWindow.xaml` + `.cs`（卸载窗口，含卸载命令解析 SplitCommand + runas 提权）。
- 改 `App/MainWindow.xaml`（加“🗑 卸载”按钮）、`MainWindow.xaml.cs`（OnOpenUninstall）。
- 改 `App/ViewModels/MainViewModel.cs`：启动时 `StartBackgroundScanInstalledApps` 后台扫描并存储。

---

## v1.2.2 — 2026-07-05（产品目录本地缓存 / 图标磁盘缓存，启动免联网刷新）

针对用户反馈"每次打开都要刷新目录、图标也重新获取"：

- **产品目录本地缓存**：目录 XML 按架构缓存到 `%AppData%\AdobeDownloader\catalog-{arch}.xml`。启动即从缓存显示产品列表，无需每次联网点"刷新目录"；切换架构时加载对应架构的缓存；点"刷新目录"才联网并更新缓存，状态栏显示缓存时间。
- **图标磁盘缓存**：产品图标首次按 URL 下载后存盘到 `%AppData%\AdobeDownloader\icons\{sha1}.img`，之后各次启动直接读盘，不再联网重复获取。改用异步加载、返回已 Freeze 的位图，可跨线程绑定。
- **原因**：原先构造函数不自动加载目录（需手动刷新），且图标绑定远程 URL（`BitmapCacheOption.OnDemand` 每次启动都联网下载）。

### 涉及文件
- 改 `Core/AdobeApiClient.cs`：拆出 `FetchCatalogXmlAsync`（返回原始 XML 供缓存），`FetchCatalogAsync` 复用之。
- 新增 `App/CatalogCache.cs`（目录 XML 本地缓存 Load/Save）、`App/IconCache.cs`（图标磁盘缓存 LoadAsync）。
- 改 `App/ViewModels/ProductGroupViewModel.cs`：改为 `ObservableObject`，新增异步加载、走磁盘缓存的 `Icon` 属性。
- 改 `App/ViewModels/MainViewModel.cs`：新增 `LoadCachedCatalog`（启动/切换架构时加载缓存）、`PopulateProducts`（抽出列表填充）；`LoadCatalogAsync` 改为取 XML→写缓存→解析。
- 改 `MainWindow.xaml`：图标 `Source` 由远程 URL 转换器改为绑定磁盘缓存的 `Icon`。

---

## v1.2.1 — 2026-07-05（安装日志实时显示 / 单实例 / 删除任务可删文件）

针对用户反馈的三点改进：

- **安装日志实时显示**：`WindowsInstaller` 新增 `Logged` 事件，`InstallWindow` 实时把每条日志追加到下方框（原先只在安装完成后一次性显示，过程中空白）；并在框上方加标题「安装日志（实时显示部署文件、写注册表、创建快捷方式等详细过程）」说明用途。
- **单实例限制**：`App.OnStartup` 主窗口模式用命名 `Mutex`（`Local\AdobeDownloader_SingleInstance`）限制单实例，再次启动会激活已有窗口并退出；`--install`/`--cleanup` 提权子进程不受限（允许并存）。这也避免了多实例并发写 tasks.json 互相覆盖。
- **删除任务可选删文件**：删除任务时弹出「是/否/取消」——「是」删任务并删除已下载文件，「否」仅移除任务记录保留文件（含清只读属性后递归删除，且校验只删本任务子目录）。
- 说明：早前版本停止下载后会留「已取消」状态，v1.2.0 起已改为「已暂停」（可继续）+「删除」，源码已无「已取消」；用户需使用最新发布的 exe。

### 涉及文件
- 改 `App/Install/WindowsInstaller.cs`（Logged 事件）、`InstallWindow.xaml`+`.cs`（实时日志+标题）、`App.xaml.cs`（单实例 Mutex）、`ViewModels/DownloadTaskViewModel.cs`（删除询问删文件）

---

## v1.2.0 — 2026-07-05（下载任务持久化，可重启后继续）

移植原版的"任务记录持久化"。下载任务保存到磁盘，重启软件后自动恢复并可继续（断点续传），任务一直保留直到用户手动删除。

### 实现
- `Core/Models/DownloadPlan.cs`：新增 `Cdn` 字段（随任务持久化，重启后恢复下载用；`TotalDownloadSize`/`TotalPackages` 标 `JsonIgnore`）。
- `App/TaskStore.cs`：`PersistedTask` + `TaskStore`，保存到 `%AppData%\AdobeDownloader\tasks.json`（枚举用字符串）。
- `App/ViewModels/DownloadTaskViewModel.cs`：新增 `Id`、可重建的取消令牌、`Paused` 状态、`Pause/Resume/Remove` 命令、状态变化/继续/删除事件、`ToPersisted()`。
- `App/ViewModels/MainViewModel.cs`：启动时 `LoadPersistedTasks`（进行中的重启后显示为已暂停）；创建/状态变化时 `SaveTasks`；`ResumeTask`（重建令牌后续传）、`RemoveTask`（移除并保存）。
- `MainWindow.xaml`：任务卡片按钮改为 暂停/继续/打开目录/删除/安装。

### 行为
- 下载中重启 → 恢复为"已暂停"，点"继续"从磁盘已下载部分续传（DownloadEngine 断点续传）。
- 已完成任务保留，可随时"安装"或"删除"。
- 只有点"删除"才从列表与 tasks.json 移除。

### 测试与验证
- `DownloadPlanSerializationTests.cs`（含 CDN 往返），共 70 用例全过。
- 真机：构造 tasks.json → 重启 app 自动恢复 2 个任务（暂停 + 完成态）显示正确；点删除后 tasks.json 实时更新为剩余任务。

---

## v1.1.2 — 2026-07-05（修复下载速度虚高）

- 现象：下载速度显示约为真实值的 10 倍（如 80 MB/s 显示成 800 MB/s）。
- 原因：`DownloadEngine` 用「累计字节 ÷ 累计耗时」的**累计平均**作为速度，断点续传时会把盘上已有的字节和启动瞬时突发计入，除以很小的耗时导致虚高。
- 修复：新增 `Core/SlidingWindowSpeed.cs`，改为**最近约 2 秒窗口内的增量**估算“当前速度”；`DownloadEngine` 改用它。
- 测试：`SlidingWindowSpeedTests.cs`（稳态 80MB/s、断点续传不计已有字节等），共 69 用例全过。
- 涉及文件：新增 `Core/SlidingWindowSpeed.cs`、`tests/.../SlidingWindowSpeedTests.cs`；改 `Core/DownloadEngine.cs`。

---

## v1.1.1 — 2026-07-05（应用图标）

- 使用用户提供的 `图标.png`（红黑下载图标）生成多尺寸 `AppIcon.ico`（16/24/32/48/64/128/256）。
- 配置为 exe 图标（`<ApplicationIcon>`，资源管理器/任务栏显示）与各窗口图标（`Icon="AppIcon.ico"`，标题栏显示）。
- 涉及文件：新增 `src/AdobeDownloader.App/AppIcon.ico`；改 `AdobeDownloader.App.csproj`、`MainWindow.xaml`、`InstallWindow.xaml`、`CleanupWindow.xaml`、`CleanupRunWindow.xaml`。
- 验证：标题栏显示新图标；exe 内嵌图标提取成功。

---

## v1.1.0 — 2026-07-05（新增清理功能 🧹）

移植原版 1.5.0 的清理功能到 Windows。10 个清理类别：Adobe 应用程序 / Creative Cloud / 偏好设置 / 缓存 / 许可 / 日志 / 服务 / 钥匙串(凭据) / 正版验证服务(AGS) / Hosts。原版清理 macOS 路径（plist/keychain/launchctl），此版全部映射为 Windows 机制（文件、注册表、Windows 服务、凭据管理器、hosts）。

### 架构（对应原版 CleanupPlanner / CleanupProtectedResource）
- Core（纯逻辑，可测）：
  - `Cleanup/CleanupModels.cs` — 动作类型（RemovePath/RemoveGlob/RegistryKey/Service/HostsClean/Credential）与计划项
  - `Cleanup/CleanupOption.cs` — 10 个类别及各自的 Windows 目标、执行顺序、显示名
  - `Cleanup/CleanupSafety.cs` — 安全护栏：只删"确属 Adobe"的路径，绝不触碰盘根/系统目录/Adobe 根/工具自身
  - `Cleanup/CleanupHosts.cs` — hosts 行识别与移除
- App（Windows 专属）：
  - `Cleanup/CleanupPlanner.cs` — 扫描系统生成实际存在的清理计划（文件/注册表/服务/hosts/凭据）
  - `Cleanup/CleanupExecutor.cs` — 执行：删文件(清只读)、删注册表键、sc 停删服务、清 hosts Adobe 行、删凭据；逐项容错、删前再校验
  - `CleanupWindow` — 勾选类别 + 扫描预览
  - `CleanupRunWindow` — 管理员执行进度/日志
- `App.xaml.cs` 新增 `--cleanup <类别>`（管理员执行模式）与 `--cleanup-ui`（直接打开清理选择窗口）
- 主界面顶部新增「🧹 清理」按钮

### 安全设计
- 三重校验：非盘根/系统目录（IsDangerous）、非工具自身（IsProtected，含 adobedownloader 归一化）、且与 Adobe 相关（IsAdobeRelated）。
- 先扫描预览、用户确认、管理员执行；执行时对每个路径再次校验；hosts 只删 Adobe 行不动其它。

### 测试与验证
- 新增 `CleanupTests.cs`（安全规则、执行顺序覆盖、hosts 行识别等），共 66 用例全过。
- 真机扫描验证：识别出 45 项 Adobe 残留、预计释放约 40 GB，预览仅列 Adobe 路径（未触碰系统目录）。未实际执行删除。

### 涉及文件
- 新增 Core `Cleanup/*.cs`（4 个）、App `Cleanup/*.cs`（3 个）、`CleanupWindow.*`、`CleanupRunWindow.*`
- 改 `App.xaml.cs`、`MainWindow.xaml`+`.cs`（清理入口）、测试 `CleanupTests.cs`

---

## v1.0.5 — 2026-07-05（产品列表显示图标）

- 左侧产品列表每项显示对应的 Adobe 官方产品图标（从 CDN `ffc-static-cdn.oobesaas.adobe.com/icons/...` 异步加载，带圆角底框）。
- 新增 `App/Converters.cs` 的 `UrlToImageConverter`（URL→位图，DecodePixelWidth 48，异步、失败静默）。
- `App/App.xaml` 注册转换器；`App/MainWindow.xaml` 列表模板改为「图标 + 文字」水平布局（Image `IsAsync=True`）。
- `ProductGroupViewModel.IconUrl` 早已暴露最佳图标地址，此版接入 UI。

---

## v1.0.4 — 2026-07-05（修复重型产品安装：文件解压 / 变量 / 快捷方式参数 / 文件系统权限）

安装 After Effects 时暴露的问题，根因是安装的 exe「此应用无法在你的电脑上运行」。逐一定位并修复，已真机重装 Bridge 与 AEFT 验证（exe 均为有效 PE，无警告）。

### 关键修复：包内文件本身是 LZMA2 压缩的
- 发现 `CompressionType=Zip-Lzma2` 的包里**每个文件**（非 .pimx）都是 `[1字节dict][裸LZMA2]`，之前直接复制导致装出的 exe 是压缩数据（`file` 报 data，无法运行）。
- 新增 `Core/Install/Lzma2FileDecompressor.cs` 流式解压单文件；`WindowsInstaller` 部署时按 CompressionType 解压（对应原版 HDPIMMiniZipExtractor.extractHDPIMLZMA2Entry）。
- 验证：Adobe Bridge.exe 14.9MB(压缩)→72.8MB，`PE32+ x86-64`；AfterFX.exe 同为有效 PE。

### 其它修复
- `InstallProperties`：补充 `[SharedDocuments]`/`[UserRoamingAppData]`/`[UserLocalAppData]`/`[CommonAppData]` 等变量（AEFT 用到）。
- `PimxParser`：Shortcut 的 Target/Directory 用 `|` 分隔命令行参数（如 `...|-re` render engine），拆分为 Arguments。
- `WindowsInstaller.ApplyPermission`：Permission 命令可作用于**文件系统路径**（如 `[SharedDocuments]\...`）而不仅注册表，按路径类型分派（DirectorySecurity / RegistryKey ACL）。
- 文件夹图标：写 desktop.ini 前先清除隐藏/系统属性，避免重装时被拒。

### 涉及文件
- 新增 `Core/Install/Lzma2FileDecompressor.cs`
- 改 `Core/Install/InstallProperties.cs`、`Core/Install/PimxParser.cs`
- 改 `App/Install/WindowsInstaller.cs`（解压部署、文件系统权限、folder icon）
- 测试 `+3`（文件解压 / IsZipLzma2 / Shortcut 管道参数），共 41 用例全过

### 已知限制
- VC 运行库等依赖若需 `runProgram`（运行安装器/激活）尚未实现；AEFT 这类重型产品装好后 exe 有效，但可能仍需系统已装 VC++ 运行库或 Adobe 许可才能实际启动。

---

## v1.0.3 — 2026-07-05（实现 Windows 内置安装引擎，真机安装成功）

按原版 HDPIM 思路，在最小修改基础上实现 Windows 安装引擎，**不依赖 Adobe 官方 Setup.exe**，绕开授权墙。已真机将 Adobe Bridge 16.0.4 完整安装到系统。

### 新增（Core，纯逻辑，可单测）
- `Install/PimxDecompressor.cs` — 解压 pimx（LZMA2，用 SharpCompress）；对应原版 PIMXParser.loadXMLData + HDPIMNativeLZMA2
- `Install/PimxModels.cs` / `Install/PimxParser.cs` — 解析 pimx 清单为命令（Assets/Registry/Permission/Shortcut/FolderIcon）
- `Install/InstallProperties.cs` — 变量表与替换（[INSTALLDIR]/[StagingFolder]/[AdobeCommon]/[AdobeProgramFiles] 等）；对应 HDPIMPropertyTable
- `Install/DriverInfo.cs` — 读回 driver.xml
- 依赖：`SharpCompress 0.36.0`（LZMA2 解压；仅用内存流解压，不受其归档提取漏洞影响）

### 修改（Core）
- `Models/ApplicationInfo.cs` / `Parsing/ApplicationParser.cs` — 新增解析 `InstallDir`（供 [INSTALLDIR] 展开）

### 新增/修改（App）
- `Install/WindowsInstaller.cs` — 执行引擎：解压 pimx、部署文件、写注册表、设 ACL、建快捷方式、文件夹图标；对应 HDPIMCommandEngine + InstallManager
- `InstallWindow.xaml` / `.cs` — 安装进度窗口
- `App.xaml` / `App.xaml.cs` — 去掉 StartupUri，改由启动参数决定：`--install "<driverDir>"` 进入安装模式，否则主窗口
- `ViewModels/DownloadTaskViewModel.cs` — 「安装」按钮改为以管理员重启自身进入 --install（对应原版用特权 Helper 重新执行自身）

### 测试
- `tests/.../InstallTests.cs`（+7 用例）+ `TestData/AdobeBridge16.0-mul-x64.pimx`（真实 pimx 回归）；共 35 用例全过
- 修复回归 bug：PimxDecompressor 误把压缩数据当明文（压缩流含 '<'），改为按首个非空白字节判断

### 真机验证（Adobe Bridge 16.0.4 → C:\Program Files\Adobe\Adobe Bridge 2026）
- 部署 2496 文件 / 647 MB，主程序 Adobe Bridge.exe 就位
- 注册表写入正确（[INSTALLDIR] 展开为 bridgeproxy.exe 真实路径）
- 开始菜单快捷方式指向正确 exe
- 全程不调用 Adobe Setup.exe，退出码 0

---

## v1.0.2 — 2026-07-05（安装环节真机验证 + PIMX 可行性探路，文档）

- 真机验证「委托官方 Setup.exe 安装」不可行：新版 HyperDrive Setup（6.4.0.359）报 `Adobe Setup is not Authorized`（Exit -1），拒绝第三方命令行直连。
- 探路证实自实现安装器可行：本机 `AdobeBridge16.0-mul-x64.pimx` 为 **LZMA2 压缩（首字节 0x18 字典字节 + 裸流）非加密**，跳首字节 + dict 64MiB 解压得 25KB 明文 XML 清单；命令本就是 Windows 语义（Assets 部署 / Registry 74 项 / Permission / Shortcut / FolderIcon）。
- 涉及文件：`docs/DESIGN.md`（新增 §9 安装环节真实验证与结论）、`docs/samples/AdobeBridge16.0-mul-x64.pimx.xml`（新增解压清单样本）、`update.md`（本条目）。
- 结论：移植 HDPIM 安装引擎到 Windows 可行且比 macOS 更直接；等用户确认是否投入实现。

---

## v1.0.1 — 2026-07-05（发布单文件 exe）

- 发布自包含单文件可执行程序：`publish/AdobeDownloader.App.exe`（win-x64，约 62 MB，内含 .NET 运行时 + WPF，双击即可运行，无需另装运行时）。
- 发布参数：`--self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true`。
- 更新 `README.md` 的发布命令为经过验证的完整命令。
- 涉及文件：`README.md`（发布命令）、`update.md`（本条目）；新增产物 `publish/`（已在 `.gitignore` 忽略）。
- 验证：单文件 exe 独立启动、界面正常渲染（`docs/screenshot-exe.png`）。

---

## v1.0.0 — 2026-07-04（Windows 移植首个可用版本）

将 macOS 版 [X1a0He/Adobe-Downloader](https://github.com/X1a0He/Adobe-Downloader) 移植为 Windows 版本。技术栈：C# / .NET 10 + WPF。安装环节委托给 Adobe 官方 `Setup.exe`（HyperDrive 安装器），不重写 macOS 专属的 HDPIM 安装引擎。

### 新增文件

**核心库 `src/AdobeDownloader.Core`（纯逻辑，已单元测试 + 真机联调 Adobe API 通过）**
- `Models/Product.cs` — 产品/平台/语言集/依赖/图标/目录结果模型
- `Models/ApplicationInfo.cs` — application.json 解析模型（包 / 模块）
- `Models/DownloadPlan.cs` — 下载计划（主产品 + 依赖 + 包列表）
- `NetworkConstants.cs` — Adobe 端点与请求头（Windows User-Agent）
- `TargetArchitecture.cs` — win64 / winarm64 架构与平台优先级
- `AppStatics.cs` — 支持的安装语言列表
- `Parsing/CatalogParser.cs` — 解析 products/all XML（ccm 可见 + 全频道依赖池 + dependencyFFCChannel 提取）
- `Parsing/ApplicationParser.cs` — 解析 application.json（兼容数组退化为对象）
- `Selection/ConditionEvaluator.cs` — 移植 Adobe 包 Condition 表达式求值器
- `Selection/PackageSelector.cs` — 按语言/架构/Condition 选包（Windows "64-bit" 约定）
- `AdobeApiClient.cs` — 调用目录与 application.json（重试 + 依赖频道二次抓取）
- `DependencyResolver.cs` — 从产品池解析依赖 buildGuid/platform/version
- `PlanBuilder.cs` — 由产品选择构建完整下载计划
- `DriverXmlGenerator.cs` — 生成 Windows 版 driver.xml
- `DownloadEngine.cs` — 并发/断点续传/进度/大小校验下载 + 写 driver.xml
- `SetupLocator.cs` — 定位 Adobe 官方 Setup.exe 并触发安装

**WPF 应用 `src/AdobeDownloader.App`**
- `Mvvm.cs` — ObservableObject / RelayCommand / AsyncRelayCommand
- `AppSettings.cs` — 设置持久化到 %AppData%\AdobeDownloader\settings.json
- `Converters.cs` — Bool→Visibility、Zero→Visibility 转换器
- `ViewModels/MainViewModel.cs` — 主视图模型（目录、选择、下载任务）
- `ViewModels/ProductGroupViewModel.cs` — 按 SapCode 归组的产品
- `ViewModels/DownloadTaskViewModel.cs` — 下载任务状态与安装/打开目录/取消操作
- `App.xaml` / `MainWindow.xaml` — 界面与样式（红色 Adobe 主题）

**测试 `tests/AdobeDownloader.Core.Tests`（27 个用例全部通过）**
- `ConditionEvaluatorTests.cs`、`CatalogParserTests.cs`、`ApplicationParserTests.cs`、
  `DriverXmlGeneratorTests.cs`、`DownloadEngineTests.cs`

**文档**
- `docs/DESIGN.md` — 设计文档（协议逆向、架构、里程碑）
- `README.md` — 使用说明

### 真机验证（对 Adobe 官方接口）
- 目录抓取成功：579 个产品条目、依赖池 944、CDN=https://ccmdls.adobe.com
- application.json 抓取成功（AEFT 26.3，含真实包大小）
- 下载计划构建成功：AEFT + VC14win64 依赖，5 个包，2653 MB
- 真实下载地址可达：HTTP 206 断点续传，返回合法 ZIP（PK 头）
- driver.xml 生成含真实 buildGuid
- WPF 界面联调：刷新目录后展示 38 个可安装产品

### 已知限制
- 部分共享依赖（ACR/CCXP/CORG/COMP）不在 Adobe Windows 目录中，无法单独下载（与官方及社区工具行为一致，跳过处理）。
- 安装需要系统已安装 Adobe Creative Cloud（提供官方 Setup.exe）并以管理员权限运行。
