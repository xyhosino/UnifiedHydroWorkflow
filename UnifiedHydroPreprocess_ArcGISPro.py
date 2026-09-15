# -*- coding: utf-8 -*-
"""UnifiedHydroLauncher V5.1.0 ArcGIS Pro preprocessing worker.

This is the parameterized version of the validated ArcPy preprocessing script.
The hydrological core is not imported or modified here.
"""
from __future__ import print_function

import argparse
import shutil
import struct
import os
import re
import sys

arcpy = None


def log(message):
    message = str(message)
    print(message)
    try:
        if arcpy is not None:
            arcpy.AddMessage(message)
    except Exception:
        pass


def fail(message, exit_code=2):
    log("PREPROCESS_FAILED " + str(message))
    return exit_code


def parse_args(argv=None):
    parser = argparse.ArgumentParser(
        description="Split raw hydrological GIS layers into model-ready WSCU folders."
    )
    parser.add_argument("--wata", required=True, help="Raw WATA polygon shapefile")
    parser.add_argument("--river", required=True, help="Raw RIVL polyline shapefile")
    parser.add_argument("--node", required=True, help="Raw NODE point shapefile")
    parser.add_argument("--soil", required=True, help="Soil polygon shapefile")
    parser.add_argument("--landuse", required=True, help="Land-use polygon shapefile")
    parser.add_argument("--output-dir", required=True, help="Output root for unit folders")
    return parser.parse_args(argv)


def abs_path(value):
    return os.path.abspath(os.path.expandvars(os.path.expanduser(value)))


def get_actual_field_name(fc, target_name):
    fc_path = str(fc)
    for field in arcpy.ListFields(fc_path):
        if field.name.upper() == target_name.upper():
            return str(field.name)
    raise ValueError("致命错误：在图层 {} 中找不到字段 '{}'！".format(fc_path, target_name))


def has_field(fc, target_name):
    target = target_name.upper()
    return any(field.name.upper() == target for field in arcpy.ListFields(str(fc)))


def delete_field_if_exists(fc, field_name):
    if has_field(fc, field_name):
        arcpy.DeleteField_management(fc, field_name)


def delete_if_exists(path):
    if arcpy.Exists(path):
        arcpy.Delete_management(path)


def require_shapefile(path, caption):
    if not path.lower().endswith(".shp"):
        raise ValueError("{}必须是 .shp：{}".format(caption, path))
    if not arcpy.Exists(path):
        raise IOError("{}不存在：{}".format(caption, path))
    for extension in (".shp", ".shx", ".dbf"):
        sidecar = os.path.splitext(path)[0] + extension
        if not os.path.exists(sidecar):
            raise IOError("{}缺少必要文件：{}".format(caption, sidecar))


def require_geometry(path, caption, allowed_types):
    shape_type = str(arcpy.Describe(path).shapeType).upper()
    allowed = tuple(value.upper() for value in allowed_types)
    if shape_type not in allowed:
        raise ValueError(
            "{}几何类型必须为 {}，当前为 {}：{}".format(
                caption, "/".join(allowed), shape_type, path
            )
        )


def sql_text(value):
    return str(value).replace("'", "''")


def natural_unit_key(name):
    match = re.search(r"_r?(\d+)$", str(name), re.IGNORECASE)
    return (int(match.group(1)) if match else 2147483647, str(name).upper())


def validate_unit_folder_name(name):
    invalid = '<>:"/\\|?*'
    if not name or any(char in name for char in invalid):
        raise ValueError("WSCU_Name 不能作为 Windows 文件夹名：{}".format(name))


def smart_clip(in_data, clip_lyr, out_base_path):
    desc = arcpy.Describe(in_data)
    datatype = str(desc.dataType)
    if datatype not in ("FeatureClass", "ShapeFile"):
        raise ValueError("V5.1.0 预处理仅接受矢量 SHP 土壤/土地利用：{}".format(in_data))
    out_file = out_base_path + ".shp"
    delete_if_exists(out_file)
    arcpy.Clip_analysis(in_data, clip_lyr, out_file)
    return out_file


def internal_template_dir():
    """Return the bundled schema-template directory next to this worker script."""
    return os.path.join(os.path.dirname(os.path.abspath(__file__)), "PreprocessTemplates")


def require_internal_template(path, caption, allowed_types):
    require_shapefile(path, caption)
    for extension in (".prj", ".cpg"):
        sidecar = os.path.splitext(path)[0] + extension
        if not os.path.exists(sidecar):
            raise IOError("{}缺少内置模板文件：{}".format(caption, sidecar))
    require_geometry(path, caption, allowed_types)


def field_schema_signature(path):
    """ArcPy-visible schema, used only for counts/diagnostic logging."""
    signature = []
    for field in arcpy.ListFields(path):
        field_type = str(field.type).upper()
        if field_type in ("OID", "GEOMETRY"):
            continue
        signature.append(
            (
                str(field.name),
                str(field.type),
                int(field.length or 0),
                int(field.precision or 0),
                int(field.scale or 0),
            )
        )
    return signature


def dbf_schema_signature(shp_path):
    """Return the physical DBF descriptors: name/type/width/decimals."""
    dbf_path = os.path.splitext(shp_path)[0] + ".dbf"
    with open(dbf_path, "rb") as stream:
        header = stream.read(32)
        if len(header) != 32:
            raise IOError("DBF头无效：{}".format(dbf_path))
        header_length = struct.unpack("<H", header[8:10])[0]
        schema = []
        while stream.tell() < header_length:
            descriptor = stream.read(32)
            if not descriptor or descriptor[0] == 0x0D:
                break
            raw_name = descriptor[0:11].split(b"\x00", 1)[0]
            field_type = chr(descriptor[11])
            field_width = int(descriptor[16])
            decimal_count = int(descriptor[17])
            schema.append((raw_name, field_type, field_width, decimal_count))
        return schema


def printable_dbf_schema(schema):
    result = []
    for raw_name, field_type, field_width, decimal_count in schema:
        try:
            name = raw_name.decode("ascii")
        except UnicodeDecodeError:
            try:
                name = raw_name.decode("cp936")
            except Exception:
                name = raw_name.hex()
        result.append("{}:{}({},{})".format(name, field_type, field_width, decimal_count))
    return result


def verify_schema_matches(path, template_path, caption):
    actual = dbf_schema_signature(path)
    expected = dbf_schema_signature(template_path)
    if actual != expected:
        first_diff = None
        max_len = max(len(actual), len(expected))
        for index in range(max_len):
            expected_item = expected[index] if index < len(expected) else None
            actual_item = actual[index] if index < len(actual) else None
            if expected_item != actual_item:
                first_diff = index
                break
        raise ValueError(
            "{} DBF物理字段结构与内置标准模板不一致。first_diff={} expected={} actual={}".format(
                caption,
                first_diff,
                printable_dbf_schema(expected),
                printable_dbf_schema(actual),
            )
        )


def clone_template_shell(template_shp, out_full_path):
    """File-clone the known-good SHP, then empty its records without rebuilding schema."""
    delete_if_exists(out_full_path)
    template_base = os.path.splitext(template_shp)[0]
    output_base = os.path.splitext(out_full_path)[0]
    for extension in (".shp", ".shx", ".dbf", ".prj", ".cpg"):
        src = template_base + extension
        dst = output_base + extension
        if os.path.exists(src):
            shutil.copy2(src, dst)
    if not arcpy.Exists(out_full_path):
        raise IOError("复制内置标准模板失败：{}".format(out_full_path))
    arcpy.DeleteRows_management(out_full_path)
    remaining = int(arcpy.GetCount_management(out_full_path).getOutput(0))
    if remaining != 0:
        raise RuntimeError("标准模板空壳清空失败：{} remaining={}".format(out_full_path, remaining))


def export_with_template(in_lyr, out_dir, out_name, template_shp, geom_type, caption):
    """Clone the exact legacy-compatible SHP shell and append selected data."""
    out_full_path = os.path.join(out_dir, out_name)
    clone_template_shell(template_shp, out_full_path)
    verify_schema_matches(out_full_path, template_shp, caption + " EMPTY_SHELL")
    arcpy.Append_management(in_lyr, out_full_path, "NO_TEST")
    verify_schema_matches(out_full_path, template_shp, caption)
    return out_full_path


def verify_shape(path, caption):
    for extension in (".shp", ".shx", ".dbf"):
        sidecar = os.path.splitext(path)[0] + extension
        if not os.path.exists(sidecar):
            raise IOError("{}输出不完整，缺少：{}".format(caption, sidecar))


def preprocess(args):
    wata_shp = abs_path(args.wata)
    river_shp = abs_path(args.river)
    node_shp = abs_path(args.node)
    soil_data = abs_path(args.soil)
    landuse_data = abs_path(args.landuse)
    output_base_dir = abs_path(args.output_dir)

    if " " in output_base_dir:
        raise ValueError("输出根目录不能包含普通空格，以保持与 UnifiedHydroWorkflow 兼容：{}".format(output_base_dir))

    require_shapefile(wata_shp, "流域shp")
    require_shapefile(river_shp, "河道shp")
    require_shapefile(node_shp, "节点shp")
    require_shapefile(landuse_data, "土地利用shp")
    require_shapefile(soil_data, "土壤地质shp")
    if not os.path.exists(output_base_dir):
        os.makedirs(output_base_dir)

    require_geometry(wata_shp, "流域shp", ("POLYGON",))
    require_geometry(river_shp, "河道shp", ("POLYLINE", "LINE"))
    require_geometry(node_shp, "节点shp", ("POINT",))
    require_geometry(landuse_data, "土地利用shp", ("POLYGON",))
    require_geometry(soil_data, "土壤地质shp", ("POLYGON",))

    template_root = internal_template_dir()
    tmpl_wata = os.path.join(template_root, "wata.shp")
    tmpl_rivl = os.path.join(template_root, "rivl.shp")
    tmpl_node = os.path.join(template_root, "node.shp")
    require_internal_template(tmpl_wata, "内置流域模板", ("POLYGON",))
    require_internal_template(tmpl_rivl, "内置河道模板", ("POLYLINE", "LINE"))
    require_internal_template(tmpl_node, "内置节点模板", ("POINT",))

    log("PREPROCESS_BEGIN")
    log("PREPROCESS_CONFIG output={}".format(output_base_dir))
    log("UNIT_SPLIT_FIELD WSCU_Name")
    log("SCHEMA_MODE internal_embedded_template_fileclone_v504")
    log("SCHEMA_TEMPLATE root={}".format(template_root))
    log(
        "SCHEMA_TEMPLATE_FIELDS wata={} rivl={} node={}".format(
            len(field_schema_signature(tmpl_wata)),
            len(field_schema_signature(tmpl_rivl)),
            len(field_schema_signature(tmpl_node)),
        )
    )

    fld_wscd = get_actual_field_name(wata_shp, "WSCD")
    fld_wscu_name = get_actual_field_name(wata_shp, "WSCU_Name")
    get_actual_field_name(river_shp, "RVCD")
    get_actual_field_name(river_shp, "FRVCD")
    get_actual_field_name(river_shp, "rvcs")
    get_actual_field_name(node_shp, "NDCD")
    get_actual_field_name(landuse_data, "XDMDM")
    get_actual_field_name(soil_data, "TRZDBM")

    wscd_to_wscu = {}
    unique_wscu_names = set()
    with arcpy.da.SearchCursor(wata_shp, [fld_wscd, fld_wscu_name]) as cursor:
        for wscd, wscu_name in cursor:
            if wscd and wscu_name:
                wscd_str = str(wscd).strip()
                wscu_str = str(wscu_name).strip()
                validate_unit_folder_name(wscu_str)
                if len(wscd_str) > 3:
                    wscd_to_wscu[wscd_str[3:]] = wscu_str
                    unique_wscu_names.add(wscu_str)

    if not unique_wscu_names:
        raise ValueError("WATA 中没有可用的 WSCU_Name/WSCD 记录。")

    units = sorted(unique_wscu_names, key=natural_unit_key)
    log("UNITS_DISCOVERED total={}".format(len(units)))

    temp_river_shp = os.path.join(output_base_dir, "temp_rivl_assigned.shp")
    temp_node_shp = os.path.join(output_base_dir, "temp_node_assigned.shp")
    temp_sj_wata = os.path.join(output_base_dir, "temp_sj_wata.shp")
    temp_sj_rivl = os.path.join(output_base_dir, "temp_sj_rivl.shp")

    for temp_path in (temp_river_shp, temp_node_shp, temp_sj_wata, temp_sj_rivl):
        delete_if_exists(temp_path)

    try:
        log("PREPROCESS_STAGE 1/4 river_assignment")
        arcpy.CopyFeatures_management(river_shp, temp_river_shp)
        delete_field_if_exists(temp_river_shp, "WSCU_Name")
        arcpy.AddField_management(temp_river_shp, "WSCU_Name", "TEXT", field_length=80)
        fld_rvcd = get_actual_field_name(temp_river_shp, "RVCD")
        with arcpy.da.UpdateCursor(temp_river_shp, [fld_rvcd, "WSCU_Name"]) as cursor:
            for row in cursor:
                rvcd = row[0]
                if rvcd and len(str(rvcd).strip()) > 3:
                    suffix = str(rvcd).strip()[3:]
                    if suffix in wscd_to_wscu:
                        row[1] = wscd_to_wscu[suffix]
                cursor.updateRow(row)

        log("PREPROCESS_STAGE 2/4 node_assignment")
        arcpy.CopyFeatures_management(node_shp, temp_node_shp)
        delete_field_if_exists(temp_node_shp, "WSCU_Name")
        delete_field_if_exists(temp_node_shp, "N_UID")
        arcpy.AddField_management(temp_node_shp, "N_UID", "LONG")
        oid_field = str(arcpy.Describe(temp_node_shp).OIDFieldName)
        with arcpy.da.UpdateCursor(temp_node_shp, [oid_field, "N_UID"]) as cursor:
            for row in cursor:
                row[1] = row[0]
                cursor.updateRow(row)

        arcpy.SpatialJoin_analysis(
            temp_node_shp,
            wata_shp,
            temp_sj_wata,
            "JOIN_ONE_TO_ONE",
            match_option="INTERSECT",
        )
        node_wscu_spatial = {}
        fld_wscu_sj1 = get_actual_field_name(temp_sj_wata, "WSCU_Name")
        with arcpy.da.SearchCursor(temp_sj_wata, ["N_UID", fld_wscu_sj1]) as cursor:
            for uid, wscu_value in cursor:
                if uid is not None and wscu_value:
                    node_wscu_spatial[uid] = str(wscu_value).strip()

        arcpy.SpatialJoin_analysis(
            temp_node_shp,
            temp_river_shp,
            temp_sj_rivl,
            "JOIN_ONE_TO_MANY",
            match_option="INTERSECT",
            search_radius="0.1 Meters",
        )
        node_wscu_topological = {}
        fld_wscu_sj2 = get_actual_field_name(temp_sj_rivl, "WSCU_Name")
        fld_rvcs = get_actual_field_name(temp_sj_rivl, "rvcs")
        with arcpy.da.SearchCursor(temp_sj_rivl, ["N_UID", fld_rvcs, fld_wscu_sj2]) as cursor:
            for uid, rvcs, wscu_value in cursor:
                if uid is None or not wscu_value:
                    continue
                try:
                    rvcs_value = float(rvcs) if rvcs is not None else float("inf")
                except (TypeError, ValueError):
                    rvcs_value = float("inf")
                wscu_value = str(wscu_value).strip()
                if uid not in node_wscu_topological or rvcs_value < node_wscu_topological[uid][0]:
                    node_wscu_topological[uid] = (rvcs_value, wscu_value)

        arcpy.AddField_management(temp_node_shp, "WSCU_Name", "TEXT", field_length=80)
        with arcpy.da.UpdateCursor(temp_node_shp, ["N_UID", "WSCU_Name"]) as cursor:
            for row in cursor:
                uid = row[0]
                if uid in node_wscu_topological:
                    row[1] = node_wscu_topological[uid][1]
                elif uid in node_wscu_spatial:
                    row[1] = node_wscu_spatial[uid]
                cursor.updateRow(row)

        log("PREPROCESS_STAGE 3/4 unit_export")
        wata_lyr, rivl_lyr, node_lyr = "v5_wata_lyr", "v5_rivl_lyr", "v5_node_lyr"
        for layer in (wata_lyr, rivl_lyr, node_lyr):
            if arcpy.Exists(layer):
                arcpy.Delete_management(layer)
        arcpy.MakeFeatureLayer_management(wata_shp, wata_lyr)
        arcpy.MakeFeatureLayer_management(temp_river_shp, rivl_lyr)
        arcpy.MakeFeatureLayer_management(temp_node_shp, node_lyr)

        fld_wata = arcpy.AddFieldDelimiters(wata_shp, fld_wscu_name)
        fld_rivl = arcpy.AddFieldDelimiters(temp_river_shp, "WSCU_Name")
        fld_node = arcpy.AddFieldDelimiters(temp_node_shp, "WSCU_Name")

        completed = 0
        try:
            for wscu in units:
                folder_path = os.path.join(output_base_dir, wscu)
                if not os.path.exists(folder_path):
                    os.makedirs(folder_path)
                quoted = sql_text(wscu)
                where_wata = "{} = '{}'".format(fld_wata, quoted)
                where_rivl = "{} = '{}'".format(fld_rivl, quoted)
                where_node = "{} = '{}'".format(fld_node, quoted)

                arcpy.SelectLayerByAttribute_management(wata_lyr, "NEW_SELECTION", where_wata)
                out_wata = export_with_template(
                    wata_lyr,
                    folder_path,
                    "wata.shp",
                    tmpl_wata,
                    "POLYGON",
                    "{} WATA".format(wscu),
                )

                arcpy.SelectLayerByAttribute_management(rivl_lyr, "NEW_SELECTION", where_rivl)
                if int(arcpy.GetCount_management(rivl_lyr).getOutput(0)) <= 0:
                    raise ValueError("{} 没有匹配到河道要素。".format(wscu))
                out_rivl = export_with_template(
                    rivl_lyr,
                    folder_path,
                    "rivl.shp",
                    tmpl_rivl,
                    "POLYLINE",
                    "{} RIVL".format(wscu),
                )

                arcpy.SelectLayerByAttribute_management(node_lyr, "NEW_SELECTION", where_node)
                if int(arcpy.GetCount_management(node_lyr).getOutput(0)) <= 0:
                    raise ValueError("{} 没有匹配到节点要素。".format(wscu))
                out_node = export_with_template(
                    node_lyr,
                    folder_path,
                    "node.shp",
                    tmpl_node,
                    "POINT",
                    "{} NODE".format(wscu),
                )

                out_soil = smart_clip(soil_data, wata_lyr, os.path.join(folder_path, "土壤类型"))
                out_land = smart_clip(landuse_data, wata_lyr, os.path.join(folder_path, "土地利用"))

                for path, caption in (
                    (out_wata, "WATA"),
                    (out_rivl, "RIVL"),
                    (out_node, "NODE"),
                    (out_soil, "土壤"),
                    (out_land, "土地利用"),
                ):
                    verify_shape(path, "{} {}".format(wscu, caption))

                for filename in os.listdir(folder_path):
                    lower_name = filename.lower()
                    if lower_name.endswith((".sbn", ".sbx", ".xml")):
                        try:
                            os.remove(os.path.join(folder_path, filename))
                        except Exception:
                            pass

                completed += 1
                log("PREPROCESS_UNIT unit={} status=ok index={}/{}".format(wscu, completed, len(units)))
        finally:
            for layer in (wata_lyr, rivl_lyr, node_lyr):
                if arcpy.Exists(layer):
                    arcpy.Delete_management(layer)

        log("PREPROCESS_STAGE 4/4 cleanup")
        log("PREPROCESS_SUMMARY total={} success={} failed=0 output={}".format(len(units), completed, output_base_dir))
        return 0
    finally:
        for temp_path in (temp_river_shp, temp_node_shp, temp_sj_wata, temp_sj_rivl):
            try:
                delete_if_exists(temp_path)
            except Exception:
                pass


def main(argv=None):
    global arcpy
    args = parse_args(argv)
    try:
        import arcpy as _arcpy
        arcpy = _arcpy
    except Exception as exception:
        return fail("无法导入 arcpy。请在 ArcGIS Pro Python 环境中运行：{}".format(exception), 3)

    arcpy.env.overwriteOutput = True
    arcpy.env.outputZFlag = "Disabled"
    arcpy.env.outputMFlag = "Disabled"

    try:
        return preprocess(args)
    except Exception as exception:
        try:
            details = arcpy.GetMessages(2)
        except Exception:
            details = ""
        log("PREPROCESS_EXCEPTION {}".format(exception))
        if details:
            log("ARCPY_ERROR " + details.replace("\r", " ").replace("\n", " | "))
        return 1


if __name__ == "__main__":
    sys.exit(main())
