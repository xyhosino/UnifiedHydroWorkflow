Unified Hydro Workflow V5.1.0
============================================================

版本定位
------------------------------------------------------------
V5.1.0 基于 V5.0.8 稳定版开发。
本正式版将 Launcher 升级为 V5.1.0、Workflow Core 升级为 V3.5，
并建立对应的正式构建与部署脚本。
不修改水文计算算法、输入映射核心逻辑、预检复用、数据库回滚和三套 GIS 预处理核心算法。

本版调整
------------------------------------------------------------
1. 帮助菜单调整为：
   - 使用说明
   - 运行诊断
   - 检查更新
   - 关于
2. “运行诊断”不再只是重复“运行环境详情”，而是生成完整诊断报告，包括：
   - 程序/Workflow Core 版本
   - 部署模式、模型根目录、程序目录
   - Workflow EXE 与配置文件状态
   - GIS 自动检测结果
   - GIS 优先级：ArcGIS Pro → ArcMap → QGIS
   - Workflow Core capabilities
   - 原有运行环境检查详情
   并提供“复制诊断信息”和“重新诊断”。
3. “检查更新”通过 GitHub Releases 查询最新正式 Release：
   https://github.com/xyhosino/UnifiedHydroWorkflow/releases
   仅比较版本并提示用户，不自动下载、不自动覆盖程序。
4. “关于”窗口继续保留 GitHub 仓库链接。
5. 新建水文工程时，“描述”字段保持空白，不再写入
   Unified bottom-level workflow。已存在工程的历史描述不会自动修改。
6. 缺少 Workflow Core 时的提示使用当前 Core V3.5。
7. AppInfo.cs 集中维护 V5.1.0、Workflow Core V3.5、GitHub 和 Release 地址。
8. Windows 批处理继续使用 BOM-free ASCII + CRLF，避免 CMD 解析异常。
9. 正式部署流程使用 build_workflow_v35.bat 与 prepare_deployment_v510.bat。

保持不变
------------------------------------------------------------
- GIS 自动检测优先级仍为：ArcGIS Pro → ArcMap → QGIS。
- Workflow Core 升级为 V3.5；水文计算算法保持不变。
- V5.0.7 的映射 SHP 文件名修复保留。
- 预检缓存与完整状态恢复逻辑不变。
- project.db 备份/失败回滚逻辑不变。
- ArcGIS Pro / ArcMap / QGIS 三套预处理核心算法不变。
- PreprocessTemplates 不变。

检查更新说明
------------------------------------------------------------
“帮助 → 检查更新”读取 GitHub 最新正式 Release 的 tag。
建议 Release 标签统一使用三段版本号：

  v5.1.0
  v5.1.1
  v5.2.0

如果仓库尚未创建 Release，程序会提示“目前还没有已发布的 Release”。
检查更新需要能够访问 GitHub；网络失败不会影响其他功能。

构建
------------------------------------------------------------
PowerShell：

  .\build_release.bat

然后：

  .\prepare_deployment_v510.bat "E:\hydrological_modeling\ModelRoot"

生成目录：

  dist\UnifiedHydroWorkflow

建议整个目录替换部署，不要只复制单个 EXE。

版本号规则
------------------------------------------------------------
正式用户版本号固定使用最多三段：V5.1.0、V5.1.1、V5.2.0。
不使用四段用户版本号。

V5.1.0 正式版本升级

- Launcher 正式版本升级为 V5.1.0，程序集版本为 5.1.0.0。
- Workflow Core 正式版本升级为 V3.5，并通过 capabilities 输出该版本。
- 新增 build_workflow_v35.bat，正式部署脚本只调用 V3.5 Core 构建脚本。
- 新增 prepare_deployment_v510.bat，生成 dist\UnifiedHydroWorkflow。
- 保留 V5.0.8 的诊断、更新检查和 GIS 兼容性修正：

- ArcMap 自动检测新增对 Apps\Development\ArcGIS*_envs 自定义环境的识别。
- “运行诊断”的 Core 能力查询复用 WorkflowRuntimeStager，在模型根目录环境执行，
  同时记录 Core 自报版本、退出码、stderr 与运行时清理结果。
- Windows 版本优先读取系统注册表，不再只显示 .NET 兼容性版本号。
- “检查更新”明确设置 GitHub User-Agent / Accept / API-Version 与 TLS 1.2，
  并区分无 Release（404）、访问频率限制/拒绝（403）和普通网络错误。
