# 发布与版本审查规范

普通功能任务不得自动进入发布。本文件只在用户明确要求准备版本、打包、Tag 或 Release 时读取。

## 1. 权限边界

- commit、push、创建/更新 PR、合并、Tag、打包和 GitHub Release 是不同动作，分别需要与请求相符的授权。
- 人工验收是合并门禁；合并不等于批准发布。
- 发布只处理当前明确候选，不能夹带工作树中的其它改动。

## 2. 版本同步点

`AppVersionInfo` 是运行时版本的唯一来源。发布 `vX.Y.Z` 时检查：

1. `WinPieGestures/WinPieGestures.csproj` 的 Version、AssemblyVersion、FileVersion。
2. `WinPieGestures/AppVersionInfo.cs` 的 FallbackVersion。
3. `WinPieGestures/SettingsWindow.xaml` 的设计期版本占位。
4. `CHANGELOG.md` 顶部新增对应日期与版本。
5. `installer/build-installer.ps1` 的兜底版本。
6. `installer/StarPie.iss` 的 `MyAppVersion` 与 `MyAppNumericVersion` 兜底值。

先从候选版本制定同步清单，再修改；不要全仓搜索后盲目替换相似数字。

## 3. 构建与发布命令

示例使用变量，禁止把个人绝对路径写回仓库：

```powershell
$version = 'X.Y.Z'
$releaseRoot = Join-Path $PWD "releases\v$version"

dotnet build .\WinPieGestures\WinPieGestures.csproj -c Release --nologo

dotnet publish .\WinPieGestures\WinPieGestures.csproj `
  -c Release -r win-x64 --no-self-contained `
  -o (Join-Path $releaseRoot 'Lightweight')

dotnet publish .\WinPieGestures\WinPieGestures.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o (Join-Path $releaseRoot 'Standalone')

& .\installer\build-installer.ps1 -Version $version
```

压缩包与安装器名称遵循既有 `StarPie-vX.Y.Z-*` 规则。实际参数以脚本当前帮助为准，不凭旧文档猜测。

## 4. 插件发行边界

- 主程序发布包不得携带官方插件 DLL 或旧 `plugin/` 来源区。
- 包内只保留主程序、SDK 契约程序集和插件宿主所需文件。
- 官方模块由用户操作后从 `StarPie-Official-Plugins` catalog 下载并校验。
- 干净 `plugin-data` 启动不得联网或自动安装；已安装插件在离线时仍可加载。
- 社区扫描目录只读，安装器不得替用户创建或写入候选目录。

## 5. 发布前检查

1. 工作树和候选提交明确，无无关文件。
2. Release 构建、自检、专项测试和 `git diff --check` 通过。
3. UI、安装/升级、干净数据目录和离线场景已人工验证。
4. Lightweight、Standalone、Setup 分别启动验证。
5. 检查包内容、体积、SHA-256 和文件名。
6. 核对 Changelog、版本号和 Release 文案完全一致。
7. 发布前再次获得明确发布授权。

发现版本目标错误时停止，不通过覆盖旧 Tag 或替换已公开资产“修正”。先说明外部状态和恢复方案，再等待授权。

## 6. v2.0.0 未签名发行例外

2026-10-17，用户确认源码候选实机验收通过，并明确要求在正式 CA 证书流程完成前制作并发布 v2.0.0。此次仅发布从已合入 main 的精确提交构建的未签名 Lightweight、Standalone 和 Setup，不分发 SignPath 测试签名产物，不修改证书存储。

发布前检查三个 EXE 均为 NotSigned，核对版本、图标、包内容与 SHA-256。两种便携包用隔离数据目录和安全夹具运行 `--plugin-selftest --skip-invoke`；Setup 只做编译、资源与内容核对，真实覆盖安装保留为实机检查，不将编译成功当作安装通过。

`.github/workflows/release.yml` 仅排除精确标签 `v2.0.0` / `V2.0.0` 的自动签名发布，以免正式证书未就绪的工作流与本地发行互相覆盖；其它标签及手动签名模式继续保留原有门禁。此次使用本地发行流程：推送已验证的 main 标签、创建草稿、上传三种包与 SHA256SUMS.txt，核对后发布为正式最新版，再重新下载公开资产验证哈希。

公开说明必须写明未签名及证书仍在办理，不声称已有正式证书或完成 Windows 信任验证。后续正式签名使用新版本和新标签，不覆盖本次已公开资产。

## 7. AtomGit Release 镜像

源码、分支和 Tag 继续由 AtomGit 已配置的 Pull 镜像同步；GitHub Actions 只复制已经发布的 Release 元数据和原始附件，不构建、签名、推送代码或修改 Tag。

### 配置与入口

- GitHub 仓库 `Settings → Secrets and variables → Actions → Secrets` 中添加 `ATOMGIT_TOKEN`。使用有目标组织仓库发布权限的 AtomGit 用户访问令牌，不写入源码或日志。
- 默认目标与 GitHub 的 `owner/repo` 相同。若组织路径或仓库名不同，在同处的 `Variables` 中设置 `ATOMGIT_REPOSITORY`，例如 `Star-Pie/StarPie`，不要填写 URL。
- `.github/workflows/atomgit-release.yml` 同时提供 `workflow_call` 和 `workflow_dispatch`。手动执行选择 `Actions → Sync Release to AtomGit → Run workflow`，选包含同步代码的可信分支（通常为 `main`），输入已发布的完整 Tag。
- `.github/workflows/release.yml` 在正式发布步骤成功后输出 `published` 与 `tag`，再调用同步工作流。测试签名不调用；后续 Beta 清理失败不抑制已成功发布版本的同步，取消运行则不启动下游同步。同步失败使对应任务报错，但不回滚 GitHub Release。
- 不使用独立的 `release: published` 事件串联，避免 `GITHUB_TOKEN` 创建事件不触发下游工作流的问题。失败后手动重跑同步入口即可，无需重跑构建、签名或发布步骤。
- 工作流使用 Node.js 24 的内置模块，无 npm 依赖；只授予 GitHub `contents: read`，不向被调用工作流透传 SignPath 等其它密钥。

### 校验与重试

1. 按明确 Tag 读取非草稿、已发布且有附件的 GitHub Release，分页读取附件；递归解析附注 Tag，取得最终提交 SHA。
2. 分页查询 AtomGit Tag 并核对同一提交；缺失时每分钟重试，共 30 次。提交不一致立即失败，不创建或覆盖 Tag。Pull 镜像尚未完成时，先等待/在 AtomGit 手动同步仓库，再重跑本工作流。
3. 下载全部 GitHub 原始附件，核对大小，并在 GitHub 提供 digest 时校验 SHA-256。向对象存储跳转时不转发 GitHub Token。
4. 对已有 AtomGit 同名附件全部回读并比较 SHA-256，再进行任何上传。相同则复用；不同则停止，不删除或覆盖。缺失附件通过官方上传地址接口取得临时 URL 和请求头，再 PUT 上传；不向对象存储透传 GitHub/AtomGit API Token。
5. 上传后有限轮询等待附件登记，再下载回读校验 SHA-256。所有附件通过后才更新已有发行版的标题/说明，并回读检查最终状态。新 Release 必须先创建才能上传，上传途中失败可能留下不完整发行版，重试会补齐，不会回滚或删除它。
6. GET 对临时错误有限重试；POST/PATCH/PUT 不盲目重试。写入成功但响应丢失时，下次运行重新读取远端并核对内容。目标中与源无关的附件保持原状。
7. 跨 Tag 及手动重试使用同一仓库级并发组，不取消正在进行的上传。GitHub concurrency 不是持久任务队列，连续多次触发可能替换尚未开始的 pending 运行；被替换版本可用手动入口补同步。

预发布映射为 AtomGit `pre`；GitHub 当前最新正式版显式设置 `latest`。补传旧正式版时省略该字段，避免主动请求提升；AtomGit 文档仅暴露 `pre/latest`，不保证与 GitHub 独立的 latest 指针完全等价，历史版本排序需在线验收，不声称已验证服务端默认行为。

### 本地验证与在线门禁

```powershell
node --check .github/scripts/sync-atomgit-release.mjs
node --test .github/scripts/sync-atomgit-release.test.mjs
# 若已有 actionlint：
actionlint -shellcheck= -pyflakes= .github/workflows/atomgit-release.yml .github/workflows/release.yml
git -c core.whitespace=cr-at-eol diff --check
```

测试使用内存中的模拟 HTTP 响应和临时文件，不调用发布 API、不读取真实 Token。覆盖新建、幂等重试、部分成功、同名冲突、Tag 缺失/分叉、附注 Tag、损坏下载、分页、预发布、权限错误、跳转凭据隔离和上传响应丢失。工作流字符串检查只作为结构护栏，不能替代 actionlint 或托管 Actions 验收。

首次在线验收须由用户批准并手动执行：确认目标组织仓库、Token 权限、API 实际响应、附件上传回调与公开下载的 SHA-256；再运行同一 Tag，确认不重复上传。自动调用正式发布链路和托管平台的 job 失败/输出传播也须在 GitHub 上验收。本地离线通过不代表已经完成线上镜像或独立发布审查。

接口依据：

- 创建 Release：https://atomgit-api-doc.atomgit.com/14.release/217.create_repository_release.html
- 更新 Release：https://atomgit-api-doc.atomgit.com/14.release/218.update_repository_release.html
- 附件上传地址：https://atomgit-api-doc.atomgit.com/14.release/219.get_the_upload_address_of_the_release_attachment.html
- 按 Tag 查询 Release：https://atomgit-api-doc.atomgit.com/14.release/222.get_repository_release_by_tag_name.html
- Tag 与提交列表：https://atomgit-api-doc.atomgit.com/7.tag/142.list_all_tags_of_the_project.html

Release 创建/上传文档明确使用 `access_token` 查询参数；脚本只向固定 AtomGit API 主机添加，不记录请求 URL、服务端错误正文、签名 URL 或底层网络异常，以免凭据出现在日志中。
