# Unified Hydro Workflow

A Windows-based workflow for hydrological model preprocessing, input checking, preflight validation, and batch simulation.

> 当前稳定版本：**V5.0.7**

## 简介

Unified Hydro Workflow 是一个面向水文模型工作流的 Windows 辅助程序，用于统一完成：

- GIS 数据预处理
- 计算单元扫描与管理
- SHP / 站点 / 降雨 / 上游水源数据映射
- 计算前预检
- 批量运行多个计算单元
- 运行失败时的数据库回滚保护

程序目前支持：

- **ArcGIS Pro**
- **ArcGIS Desktop / ArcMap**
- **QGIS 3.x**

## 当前稳定版

**Unified Hydro Workflow V5.0.7**

当前版本已完成并稳定使用的主要功能：

- 多 GIS 引擎数据预处理
- 智能输入映射
- 预检与预检结果复用
- 土地利用 / 土壤覆盖率检查
- 上游水源识别与自动生成
- 批量计算
- `project.db` 运行前保护与失败回滚
- 支持不同 SHP 文件名映射
- Launcher 与 Workflow 组件兼容性自动检查

## 推荐使用流程

1. 启动 `UnifiedHydroLauncher.exe`
2. 选择计算单元根目录
3. 点击“扫描单元”
4. 使用“智能映射（推荐）”
5. 检查输入数据映射
6. 设置工程名称和洪水场次参数
7. 先运行“预检”
8. 确认全部计算单元通过预检
9. 切换到“计算”
10. 正式运行模型

详细操作请参见：

`docs/Unified_Hydro_Workflow_V5.0.7_简明使用说明.txt`

## 推荐目录结构

```text
UnifiedHydroWorkflow/
├─ README.md
├─ .gitignore
├─ Launcher/
├─ WorkflowCore/
├─ Preprocess/
├─ docs/
└─ tests/
```

## 重要说明

本仓库**仅包含 Unified Hydro Workflow 程序及相关源码**。

**不包含：**

- 原水文模型主程序
- 原模型 DLL
- 原模型数据库
- 原模型运行目录
- 实际业务数据
- 真实站点、降雨、上游水源或洪水项目数据

因此，仅下载本仓库并不能独立运行原水文模型。

## SHP 数据说明

SHP 文件名可以根据实际项目调整，并通过输入映射指定。

但模型相关字段结构不建议随意修改。当前明确依赖的关键字段包括：

- 流域：`WSCU_Name`、`WSCD`
- 河道：`RVCD`、`FRVCD`、`RVCS`
- 节点：`NDCD`
- 土地利用：`XDMDM`
- 土壤类型：`TRZDBM`

## 运行环境

建议运行环境：

- Windows
- .NET Framework 4.0
- Microsoft Visual C++ 2010 SP1 Redistributable (x86)
- ArcGIS Pro、ArcMap 或 QGIS 3.x 至少一种
- 已正确安装并可运行的原水文模型环境

## GitHub Repository

https://github.com/xyhosino/UnifiedHydroWorkflow

## License

当前仓库暂未附加开源许可证。

在明确许可证之前，请不要默认将本项目视为可自由再发布的软件。
