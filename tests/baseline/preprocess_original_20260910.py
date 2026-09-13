# -*- coding: utf-8 -*-
from __future__ import print_function
import os
import sys
import arcpy

# =============================================================================
# 0. 用户参数配置
# =============================================================================
WATA_SHP =  r"E:\SC_hy_process\HY_2_3\wata_2_3.shp"
RIVER_SHP = r"E:\SC_hy_process\HY_2_3\rivl_2_3.shp"
NODE_SHP = r"E:\SC_hy_process\HY_2_3\node_2_3.shp"

SOIL_DATA = r"E:\SC_hy_process\HY_2_3\土壤类型_wata_2_3.shp"
LANDUSE_DATA = r"E:\SC_hy_process\HY_2_3\土地利用_wata_2_3.shp"

# 【新增】：标准数据的文件夹！我们将用它里面正常的 wata/rivl/node 作为骨架模板
TEMPLATE_DIR = r"E:\SC_hy_process\WHA39_5_2_1"

OUTPUT_BASE_DIR = r"E:\SC_hy_process\五个图层shp"

arcpy.env.overwriteOutput = True
arcpy.env.outputZFlag = "Disabled"
arcpy.env.outputMFlag = "Disabled"


# =============================================================================
# 1. 日志与底层工具
# =============================================================================
def log(message):
    # ArcGIS Pro 使用 Python 3，文本类型统一为 str；不要再引用 Python 2 的 unicode。
    message = str(message)
    print(message)
    try:
        arcpy.AddMessage(message)
    except Exception:
        pass


def get_actual_field_name(fc, target_name):
    fc_path = str(fc)
    for f in arcpy.ListFields(fc_path):
        if f.name.upper() == target_name.upper():
            return str(f.name)
    raise ValueError(u"致命错误：在图层 {} 中找不到字段 '{}'！".format(fc_path, target_name))


def smart_clip(in_data, clip_lyr, out_base_path):
    if not in_data or not arcpy.Exists(in_data):
        return
    desc = arcpy.Describe(in_data)
    datatype = desc.dataType
    try:
        if datatype in ["FeatureClass", "ShapeFile"]:
            out_file = out_base_path + u".shp"
            arcpy.Clip_analysis(in_data, clip_lyr, out_file)
        elif datatype in ["RasterDataset", "RasterBand"]:
            out_file = out_base_path + u".tif"
            arcpy.Clip_management(in_data, "#", out_file, clip_lyr, "0", "ClippingGeometry")
    except Exception as e:
        log(u"  [裁剪失败] {}: {}".format(in_data, str(e)))


def export_with_template(in_lyr, out_dir, out_name, template_shp, geom_type):
    """【核心】使用模板创建空壳，并无损灌入数据，保证 DBF 结构 100% 一致"""
    out_full_path = os.path.join(out_dir, out_name)
    # 1. 创建空壳 (借用 template_shp 的坐标系和字段结构)
    arcpy.CreateFeatureclass_management(out_dir, out_name, geom_type, template_shp, "DISABLED", "DISABLED",
                                        template_shp)
    # 2. 将数据灌入空壳 (NO_TEST 忽略多余字段，自动匹配同名字段)
    arcpy.Append_management(in_lyr, out_full_path, "NO_TEST")


# =============================================================================
# 2. 核心处理流程
# =============================================================================
def main():
    log(u"=" * 70)
    log(u"启动：基于标准模板的 DBF 结构克隆提取任务")
    log(u"=" * 70)

    if not os.path.exists(OUTPUT_BASE_DIR):
        os.makedirs(OUTPUT_BASE_DIR)

    # 检查模板文件是否存在
    tmpl_wata = os.path.join(TEMPLATE_DIR, "WHA39_5_2_1.shp")
    tmpl_rivl = os.path.join(TEMPLATE_DIR, "rivl_WHA39_5_2_1.shp")
    tmpl_node = os.path.join(TEMPLATE_DIR, "node_WHA39_5_2_1.shp")
    if not arcpy.Exists(tmpl_wata) or not arcpy.Exists(tmpl_rivl) or not arcpy.Exists(tmpl_node):
        raise IOError(u"致命错误：未在模板文件夹中找到标准 wata/rivl/node，请检查 TEMPLATE_DIR 路径！")

    # -------------------------------------------------------------------------
    # 步骤 1：处理河流 (RIVL)
    # -------------------------------------------------------------------------
    log(u"--> [1/4] 正在建立 RVCD 与 WSCD 映射，并为河流赋值...")
    wscd_to_wscu = {}
    unique_wscu_names = set()

    wata_str_path = str(WATA_SHP)
    fld_wscd = get_actual_field_name(wata_str_path, "WSCD")
    fld_wscu_name = get_actual_field_name(wata_str_path, "WSCU_Name")

    with arcpy.da.SearchCursor(wata_str_path, [fld_wscd, fld_wscu_name]) as cur:
        for wscd, wscu_name in cur:
            if wscd and wscu_name:
                wscd_str = str(wscd).strip()
                wscu_str = str(wscu_name).strip()
                if len(wscd_str) > 3:
                    wscd_to_wscu[wscd_str[3:]] = wscu_str
                    unique_wscu_names.add(wscu_str)

    temp_river_shp = os.path.join(OUTPUT_BASE_DIR, "temp_rivl_assigned.shp")
    arcpy.CopyFeatures_management(str(RIVER_SHP), temp_river_shp)
    arcpy.AddField_management(temp_river_shp, "WSCU_Name", "TEXT", field_length=80)

    fld_rvcd = get_actual_field_name(temp_river_shp, "RVCD")
    with arcpy.da.UpdateCursor(temp_river_shp, [fld_rvcd, "WSCU_Name"]) as cur:
        for row in cur:
            rvcd = row[0]
            if rvcd and len(str(rvcd).strip()) > 3:
                suffix = str(rvcd).strip()[3:]
                if suffix in wscd_to_wscu:
                    row[1] = wscd_to_wscu[suffix]
            cur.updateRow(row)

    # -------------------------------------------------------------------------
    # 步骤 2：处理节点 (NODE)
    # -------------------------------------------------------------------------
    log(u"--> [2/4] 正在处理 NODE 节点，进行边界上游拓扑强制纠正...")

    temp_node_shp = os.path.join(OUTPUT_BASE_DIR, "temp_node_assigned.shp")
    arcpy.CopyFeatures_management(str(NODE_SHP), temp_node_shp)
    arcpy.AddField_management(temp_node_shp, "N_UID", "LONG")

    fld_oid = get_actual_field_name(temp_node_shp, "OID@") if "OID@" in [f.name for f in
                                                                         arcpy.ListFields(temp_node_shp)] else "FID"

    with arcpy.da.UpdateCursor(temp_node_shp, [fld_oid, "N_UID"]) as cur:
        for row in cur:
            row[1] = row[0]
            cur.updateRow(row)

    temp_sj_wata = os.path.join(OUTPUT_BASE_DIR, "temp_sj_wata.shp")
    arcpy.SpatialJoin_analysis(temp_node_shp, str(WATA_SHP), temp_sj_wata, "JOIN_ONE_TO_ONE", match_option="INTERSECT")

    node_wscu_spatial = {}
    fld_wscu_sj1 = get_actual_field_name(temp_sj_wata, "WSCU_Name")
    with arcpy.da.SearchCursor(temp_sj_wata, ["N_UID", fld_wscu_sj1]) as cur:
        for row in cur:
            if row[0] is not None and row[1]:
                node_wscu_spatial[row[0]] = str(row[1]).strip()

    temp_sj_rivl = os.path.join(OUTPUT_BASE_DIR, "temp_sj_rivl.shp")
    arcpy.SpatialJoin_analysis(temp_node_shp, temp_river_shp, temp_sj_rivl, "JOIN_ONE_TO_MANY",
                               match_option="INTERSECT", search_radius="0.1 Meters")

    node_wscu_topological = {}
    fld_wscu_sj2 = get_actual_field_name(temp_sj_rivl, "WSCU_Name")
    fld_rvcs = get_actual_field_name(temp_sj_rivl, "rvcs")

    with arcpy.da.SearchCursor(temp_sj_rivl, ["N_UID", fld_rvcs, fld_wscu_sj2]) as cur:
        for row in cur:
            uid = row[0]
            if uid is None: continue
            try:
                rvcs_val = float(row[1]) if row[1] is not None else float('inf')
            except ValueError:
                rvcs_val = float('inf')
            wscu_val = str(row[2]).strip() if row[2] else u""
            if not wscu_val: continue

            if uid not in node_wscu_topological or rvcs_val < node_wscu_topological[uid][0]:
                node_wscu_topological[uid] = (rvcs_val, wscu_val)

    arcpy.AddField_management(temp_node_shp, "WSCU_Name", "TEXT", field_length=80)
    with arcpy.da.UpdateCursor(temp_node_shp, ["N_UID", "WSCU_Name"]) as cur:
        for row in cur:
            uid = row[0]
            if uid in node_wscu_topological:
                row[1] = node_wscu_topological[uid][1]
            elif uid in node_wscu_spatial:
                row[1] = node_wscu_spatial[uid]
            cur.updateRow(row)

    # -------------------------------------------------------------------------
    # 步骤 3 & 4：使用模板导出，保证 DBF 结构与标准答案分毫不差！
    # -------------------------------------------------------------------------
    log(u"--> [3/4] 启动模板克隆，完美复刻标准属性表结构...")

    wata_lyr, rivl_lyr, node_lyr = "wata_lyr", "rivl_lyr", "node_lyr"
    arcpy.MakeFeatureLayer_management(str(WATA_SHP), wata_lyr)
    arcpy.MakeFeatureLayer_management(temp_river_shp, rivl_lyr)
    arcpy.MakeFeatureLayer_management(temp_node_shp, node_lyr)

    fld_wata = arcpy.AddFieldDelimiters(str(WATA_SHP), fld_wscu_name)
    fld_rivl = arcpy.AddFieldDelimiters(temp_river_shp, "WSCU_Name")
    fld_node = arcpy.AddFieldDelimiters(temp_node_shp, "WSCU_Name")

    total_folders = len(unique_wscu_names)
    success_count = 0

    for wscu in unique_wscu_names:
        folder_path = os.path.join(OUTPUT_BASE_DIR, wscu)
        if not os.path.exists(folder_path):
            os.makedirs(folder_path)

        where_wata = u"{} = '{}'".format(fld_wata, wscu)
        where_rivl = u"{} = '{}'".format(fld_rivl, wscu)
        where_node = u"{} = '{}'".format(fld_node, wscu)

        # 1. 使用模板克隆 Wata
        arcpy.SelectLayerByAttribute_management(wata_lyr, "NEW_SELECTION", where_wata)
        export_with_template(wata_lyr, folder_path, "wata.shp", tmpl_wata, "POLYGON")

        # 2. 使用模板克隆 Rivl
        arcpy.SelectLayerByAttribute_management(rivl_lyr, "NEW_SELECTION", where_rivl)
        if int(arcpy.GetCount_management(rivl_lyr).getOutput(0)) > 0:
            export_with_template(rivl_lyr, folder_path, "rivl.shp", tmpl_rivl, "POLYLINE")

        # 3. 使用模板克隆 Node
        arcpy.SelectLayerByAttribute_management(node_lyr, "NEW_SELECTION", where_node)
        if int(arcpy.GetCount_management(node_lyr).getOutput(0)) > 0:
            export_with_template(node_lyr, folder_path, "node.shp", tmpl_node, "POINT")

        # 4. 裁剪土壤和土地利用
        smart_clip(SOIL_DATA, wata_lyr, os.path.join(folder_path, u"土壤类型"))
        smart_clip(LANDUSE_DATA, wata_lyr, os.path.join(folder_path, u"土地利用"))

        # 5. 再次清理多余的文件
        for filename in os.listdir(folder_path):
            lower_name = filename.lower()
            if lower_name.endswith(".sbn") or lower_name.endswith(".sbx") or lower_name.endswith(".xml"):
                try:
                    os.remove(os.path.join(folder_path, filename))
                except:
                    pass

        success_count += 1
        if success_count % 10 == 0 or success_count == total_folders:
            log(u"   进度: {} / {} 子流域底层结构克隆完成...".format(success_count, total_folders))

    # -------------------------------------------------------------------------
    # 步骤 5：清理环境垃圾
    # -------------------------------------------------------------------------
    for lyr in [wata_lyr, rivl_lyr, node_lyr]:
        if arcpy.Exists(lyr): arcpy.Delete_management(lyr)
    for temp_shp in [temp_river_shp, temp_node_shp, temp_sj_wata, temp_sj_rivl]:
        if arcpy.Exists(temp_shp): arcpy.Delete_management(temp_shp)

    log(u"=" * 70)
    log(u"任务圆满完成！克隆的 DBF 结构与标准模板分毫不差，请放心导入模型！")
    log(u"=" * 70)


if __name__ == "__main__":
    main()
