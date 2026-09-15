# Unified Hydro Workflow

A Windows-based workflow for hydrological model preprocessing, input checking, preflight validation, and batch simulation.

> 当前稳定版本：**V5.1.0**

> Workflow Core：**V3.5**

## 简介

**Unified Hydro Workflow** 是一个面向水文模型工作流的 Windows 辅助程序，用于统一完成 GIS 数据预处理、输入数据映射、计算前预检、批量运行以及运行环境诊断等工作。

主要功能包括：

- GIS 数据预处理
- 计算单元扫描与管理
- SHP / 站点 / 降雨 / 上游水源数据映射
- 输入数据智能识别
- 计算前预检
- 预检结果复用
- 上游水源识别与自动生成
- 批量运行多个计算单元
- `project.db` 运行前保护与失败回滚
- 运行环境诊断
- GitHub Release 在线更新检查

目前支持以下 GIS 引擎：

- **ArcGIS Pro**
- **ArcGIS Desktop / ArcMap**
- **QGIS 3.x**

GIS 自动选择优先级：

```text
ArcGIS Pro → ArcMap → QGIS
```

---

## 当前稳定版

### Unified Hydro Workflow V5.1.0

V5.1.0 当前已完成并稳定使用的主要功能：

- ArcGIS Pro / ArcMap / QGIS 三引擎数据预处理
- GIS 环境自动检测
- ArcGIS Pro → ArcMap → QGIS 自动选择
- 智能输入映射
- 预检与预检结果复用
- 土地利用 / 土壤覆盖率检查
- 上游水源识别与自动生成
- 批量计算
- `project.db` 运行前保护与失败回滚
- 支持不同 SHP 文件名映射
- Launcher 与 Workflow Core 兼容性检查
- 运行诊断
- GitHub Release 在线更新检查
- QGIS DBF 数值字段写入兼容修复
- QGIS 面裁剪低维几何过滤
- 独立程序文件夹嵌套部署

---

## 推荐使用流程

1. 启动 `UnifiedHydroLauncher.exe`
2. 选择计算单元根目录
3. 点击“扫描单元”
4. 使用“智能映射（推荐）”
5. 检查输入数据映射
6. 设置工程名称和洪水场次参数
7. 先运行“仅预检”
8. 确认全部计算单元通过预检
9. 切换到“计算”
10. 正式运行模型

详细操作请参见：

```text
Unified_Hydro_Workflow_V5.1.0_简明使用说明.txt
```

---

## GIS 数据预处理

预处理支持三套独立脚本：

```text
UnifiedHydroPreprocess_ArcGISPro.py
UnifiedHydroPreprocess_ArcMap.py
UnifiedHydroPreprocess_QGIS.py
```

三种 GIS 引擎均使用统一的标准模板，以尽量保持输出 SHP / DBF 字段结构与原水文模型兼容。

标准模板位于：

```text
PreprocessTemplates/
├─ wata.*
├─ rivl.*
└─ node.*
```

### 计算单元拆分

计算单元按照流域图层中的：

```text
WSCU_Name
```

字段进行拆分。

`WSCD` 主要用于河道归属匹配及相关模型逻辑。

---

## SHP 数据说明

输入 SHP 文件名可以根据实际项目调整，并通过输入映射功能指定。

模型相关字段结构不建议随意修改。

当前明确依赖的关键字段包括：

| 数据类型 | 关键字段 |
|---|---|
| 流域 | `WSCU_Name`、`WSCD` |
| 河道 | `RVCD`、`FRVCD`、`RVCS` |
| 节点 | `NDCD` |
| 土地利用 | `XDMDM` |
| 土壤类型 | `TRZDBM` |

为了保持与原水文模型兼容，预处理输出会尽量保留标准模板中的完整字段结构，而不仅仅是上述关键字段。

---

## 输入数据映射

程序支持以下输入方式：

- **智能映射（推荐）**
- 统一文件名
- 手动映射

支持识别和映射：

- 流域 SHP
- 河道 SHP
- 节点 SHP
- 土地利用 SHP
- 土壤类型 SHP
- 站点信息
- 降雨数据
- 上游水源数据

上游水源数据在满足条件时可以自动生成。

---

## 运行诊断

程序提供：

```text
帮助
├─ 使用说明
├─ 运行诊断
├─ 检查更新
└─ 关于
```

“运行诊断”可检查：

- Windows 环境
- x86 / x64 进程架构
- .NET Framework / CLR
- 模型根目录
- 部署模式
- ArcGIS Pro
- ArcMap
- QGIS
- Workflow Core
- Core 版本
- Core capabilities
- 必要模型文件
- 输入数据目录

---

## 在线检查更新

程序通过 GitHub Releases 检查正式版本更新。

开发中的 `main` 分支更新不会被当作正式发布版本。

版本关系：

```text
main
│
├─ 持续开发源码
│
├─ tag v5.1.0
│  └─ 固定 V5.1.0 源码快照
│
└─ Release V5.1.0
   └─ 正式发布版本及可部署程序包
```

只有创建新的正式 GitHub Release 后，程序的“检查更新”才会将其识别为新版本。

---

## 源码结构

当前仓库主要结构：

```text
UnifiedHydroWorkflow/
├─ README.md
├─ .gitignore
├─ VERSION.txt
│
├─ App.config
├─ AppInfo.cs
├─ AssemblyInfo.cs
├─ Program.cs
│
├─ MainForm.cs
├─ MainForm.Environment.cs
├─ MainForm.ExternalSources.cs
├─ MainForm.Help.cs
├─ MainForm.InputMapping.cs
├─ MainForm.Preprocessing.cs
│
├─ DateTimeSelectionForm.cs
├─ DeploymentLayout.cs
├─ EnvironmentChecker.cs
├─ InputDiscovery.cs
├─ InputMappingForm.cs
├─ LauncherInputMapping.cs
├─ MaintenanceManager.cs
├─ PreprocessingForm.cs
├─ WorkflowRuntimeStager.cs
│
├─ UnifiedHydroLauncher.csproj
│
├─ UnifiedHydroPreprocess_ArcGISPro.py
├─ UnifiedHydroPreprocess_ArcMap.py
├─ UnifiedHydroPreprocess_QGIS.py
│
├─ PreprocessTemplates/
│  ├─ wata.*
│  ├─ rivl.*
│  └─ node.*
│
├─ WorkflowCore/
│  ├─ UnifiedHydroWorkflow.cs
│  └─ build_workflow_v35.bat
│
├─ tests/
│
├─ build_release.bat
└─ prepare_deployment_v510.bat
```

---

## 推荐部署结构

Unified Hydro Workflow 推荐以独立子目录形式部署到原水文模型根目录。

```text
<原水文模型根目录>/
├─ FloodAnalysisSYS.exe
├─ project.db
├─ SysDAL.dll
├─ DBSupport.dll
├─ System.Data.SQLite.dll
├─ SKBYEXE/
│
└─ UnifiedHydroWorkflow/
   ├─ UnifiedHydroLauncher.exe
   ├─ UnifiedHydroLauncher.exe.config
   ├─ UnifiedHydroWorkflow.exe
   ├─ UnifiedHydroWorkflow.exe.config
   │
   ├─ UnifiedHydroPreprocess_ArcGISPro.py
   ├─ UnifiedHydroPreprocess_ArcMap.py
   ├─ UnifiedHydroPreprocess_QGIS.py
   │
   ├─ PreprocessTemplates/
   ├─ VERSION.txt
   └─ Unified_Hydro_Workflow_V5.1.0_简明使用说明.txt
```

程序不会要求原水文模型根目录使用固定文件夹名称。

---

## 运行环境

建议运行环境：

- Windows
- .NET Framework 4.0
- x86 运行环境
- Microsoft Visual C++ 2010 SP1 Redistributable (x86)
- ArcGIS Pro、ArcMap 或 QGIS 3.x 至少一种
- 已正确安装并可以独立运行的原水文模型环境

GIS 软件不要求安装在固定目录，程序会自动尝试检测常见安装位置和可用 Python / 启动器。

---

## 构建

源码目录中提供：

```text
build_release.bat
WorkflowCore\build_workflow_v35.bat
```

`build_release.bat` 用于构建 Launcher；`build_workflow_v35.bat` 用于构建 Workflow Core V3.5。

V5.1.0 部署准备脚本：

```text
prepare_deployment_v510.bat
```

示例：

```powershell
.\prepare_deployment_v510.bat "E:\hydrological_modeling\ModelRoot"
```

实际模型根目录名称不限于 `ModelRoot`。

---

## Release

正式稳定版本通过 GitHub Releases 发布。

V5.1.0 可部署程序包建议命名为：

```text
UnifiedHydroWorkflow_V5.1.0.zip
```

Release 中：

- `UnifiedHydroWorkflow_V5.1.0.zip`：可部署程序包
- `Source code (zip)`：GitHub 自动生成的源码快照
- `Source code (tar.gz)`：GitHub 自动生成的源码快照

程序包本身不包含原水文模型文件。

---

## 重要说明

本仓库仅包含 **Unified Hydro Workflow 自身源码、预处理脚本及相关模板**。

本仓库不包含：

- 原水文模型主程序
- 原模型专有 DLL
- 原模型数据库
- 原模型完整运行目录
- 实际业务数据
- 真实站点数据
- 真实降雨数据
- 真实上游水源数据
- 真实洪水项目数据

因此，仅下载本仓库不能独立运行原水文模型。

使用 Unified Hydro Workflow 前，应确保原水文模型已经能够正常运行。

---

## GitHub Repository

https://github.com/xyhosino/UnifiedHydroWorkflow

---

## License

当前仓库暂未附加开源许可证。

在明确许可证之前，请不要默认将本项目视为可自由复制、修改或再发布的软件。