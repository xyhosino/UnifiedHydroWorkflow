# -*- coding: utf-8 -*-
"""Unified Hydro Workflow V5.1.0 - QGIS preprocessing backend.

Run this script from the QGIS Python environment (recommended: python-qgis.bat).
It mirrors the validated ArcGIS Pro preprocessing logic while keeping the
legacy-compatible WATA/RIVL/NODE DBF schemas by file-cloning the bundled
PreprocessTemplates shells and writing records into those shells with GDAL/OGR.
"""
from __future__ import print_function

import argparse
import datetime
import math
import os
import re
import shutil
import struct
import sys
import traceback
from decimal import Decimal, InvalidOperation, ROUND_HALF_UP

ogr = None
osr = None
qgis_version = None


def log(message):
    try:
        print(str(message), flush=True)
    except TypeError:  # older Python fallback
        print(str(message))
        sys.stdout.flush()


def fail(message, code):
    log("PREPROCESS_FAILED " + str(message))
    return code


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description="UnifiedHydro QGIS preprocessing backend")
    parser.add_argument("--wata", required=True)
    parser.add_argument("--river", required=True)
    parser.add_argument("--node", required=True)
    parser.add_argument("--landuse", required=True)
    parser.add_argument("--soil", required=True)
    parser.add_argument("--output-dir", required=True)
    return parser.parse_args(argv)


def abs_path(path):
    return os.path.abspath(os.path.expanduser(str(path)))


def natural_unit_key(name):
    match = re.search(r"_r?(\d+)$", str(name), re.IGNORECASE)
    return (int(match.group(1)) if match else 2147483647, str(name).upper())


def validate_unit_folder_name(name):
    invalid = '<>:"/\\|?*'
    if not name or any(char in name for char in invalid):
        raise ValueError("WSCU_Name 不能作为 Windows 文件夹名：{}".format(name))


def internal_template_dir():
    return os.path.join(os.path.dirname(os.path.abspath(__file__)), "PreprocessTemplates")


def shapefile_sidecars(path):
    base = os.path.splitext(path)[0]
    return [base + ext for ext in (".shp", ".shx", ".dbf")]


def require_shapefile(path, caption):
    if not path.lower().endswith(".shp"):
        raise ValueError("{}必须是 .shp：{}".format(caption, path))
    if not os.path.isfile(path):
        raise IOError("{}不存在：{}".format(caption, path))
    for sidecar in shapefile_sidecars(path):
        if not os.path.exists(sidecar):
            raise IOError("{}缺少必要文件：{}".format(caption, sidecar))


def open_layer(path, update=False):
    ds = ogr.Open(path, 1 if update else 0)
    if ds is None:
        raise IOError("GDAL/OGR 无法打开：{}".format(path))
    layer = ds.GetLayer(0)
    if layer is None:
        ds = None
        raise IOError("GDAL/OGR 无法读取图层：{}".format(path))
    return ds, layer


def normalized_geom_type(geom_type):
    try:
        return ogr.GT_Flatten(geom_type)
    except Exception:
        # Fallback for older bindings where GT_Flatten is unavailable.
        return geom_type & 0x7FFFFFFF


def require_geometry(path, caption, allowed_flat_types):
    ds, layer = open_layer(path)
    try:
        actual = normalized_geom_type(layer.GetGeomType())
        if actual not in allowed_flat_types:
            names = [ogr.GeometryTypeToName(t) for t in allowed_flat_types]
            raise ValueError(
                "{}几何类型必须为 {}，当前为 {}：{}".format(
                    caption, "/".join(names), ogr.GeometryTypeToName(actual), path
                )
            )
    finally:
        layer = None
        ds = None


def field_map(layer):
    definition = layer.GetLayerDefn()
    result = {}
    for index in range(definition.GetFieldCount()):
        field = definition.GetFieldDefn(index)
        result[field.GetNameRef().upper()] = field.GetNameRef()
    return result


def actual_field(layer, expected):
    mapping = field_map(layer)
    key = expected.upper()
    if key not in mapping:
        raise ValueError("致命错误：在图层 {} 中找不到字段 '{}'！".format(layer.GetName(), expected))
    return mapping[key]


def clone_feature(feature):
    result = feature.Clone()
    geometry = feature.GetGeometryRef()
    if geometry is not None:
        result.SetGeometry(geometry.Clone())
    return result


def feature_value(feature, field_name):
    index = feature.GetFieldIndex(field_name)
    if index < 0:
        return None
    return feature.GetField(index)


def dbf_schema_signature(shp_path):
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
        except Exception:
            try:
                name = raw_name.decode("utf-8")
            except Exception:
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
        for index in range(max(len(actual), len(expected))):
            expected_item = expected[index] if index < len(expected) else None
            actual_item = actual[index] if index < len(actual) else None
            if expected_item != actual_item:
                first_diff = index
                break
        raise ValueError(
            "{} DBF物理字段结构与内置标准模板不一致。first_diff={} expected={} actual={}".format(
                caption, first_diff, printable_dbf_schema(expected), printable_dbf_schema(actual)
            )
        )


def remove_shapefile(path):
    base = os.path.splitext(path)[0]
    for extension in (".shp", ".shx", ".dbf", ".prj", ".cpg", ".qix", ".sbn", ".sbx", ".xml"):
        candidate = base + extension
        if os.path.exists(candidate):
            try:
                os.remove(candidate)
            except OSError:
                pass


def _blank_shp_or_shx(path):
    with open(path, "r+b") as stream:
        header = bytearray(stream.read(100))
        if len(header) != 100:
            raise IOError("SHP/SHX 头无效：{}".format(path))
        header[24:28] = struct.pack(">i", 50)  # 100 bytes / 2
        for index in range(36, 100):
            header[index] = 0
        stream.seek(0)
        stream.write(header)
        stream.truncate(100)


def _blank_dbf(path):
    with open(path, "r+b") as stream:
        header = bytearray(stream.read(32))
        if len(header) != 32:
            raise IOError("DBF头无效：{}".format(path))
        header_length = struct.unpack("<H", header[8:10])[0]
        if header_length < 33:
            raise IOError("DBF头长度无效：{}".format(path))
        stream.seek(0)
        full_header = bytearray(stream.read(header_length))
        if len(full_header) != header_length:
            raise IOError("DBF字段描述不完整：{}".format(path))
        full_header[4:8] = struct.pack("<I", 0)
        stream.seek(0)
        stream.write(full_header)
        stream.write(b"\x1A")
        stream.truncate(header_length + 1)


def clone_template_shell(template_shp, out_full_path):
    remove_shapefile(out_full_path)
    template_base = os.path.splitext(template_shp)[0]
    output_base = os.path.splitext(out_full_path)[0]
    for extension in (".shp", ".shx", ".dbf", ".prj", ".cpg"):
        src = template_base + extension
        dst = output_base + extension
        if os.path.exists(src):
            shutil.copy2(src, dst)
    _blank_shp_or_shx(output_base + ".shp")
    _blank_shp_or_shx(output_base + ".shx")
    _blank_dbf(output_base + ".dbf")
    verify_schema_matches(out_full_path, template_shp, "EMPTY_SHELL")


def _decode_dbf_field_name(raw_name):
    try:
        return raw_name.decode("ascii")
    except Exception:
        try:
            return raw_name.decode("utf-8")
        except Exception:
            return raw_name.decode("cp936", "replace")


def _read_dbf_layout(shp_path):
    dbf_path = os.path.splitext(shp_path)[0] + ".dbf"
    with open(dbf_path, "rb") as stream:
        first = stream.read(32)
        if len(first) != 32:
            raise IOError("DBF头无效：{}".format(dbf_path))
        header_length = struct.unpack("<H", first[8:10])[0]
        record_length = struct.unpack("<H", first[10:12])[0]
        stream.seek(0)
        header = bytearray(stream.read(header_length))
        if len(header) != header_length:
            raise IOError("DBF字段描述不完整：{}".format(dbf_path))

    fields = []
    offset = 32
    while offset + 32 <= header_length:
        descriptor = bytes(header[offset:offset + 32])
        if not descriptor or descriptor[0] == 0x0D:
            break
        raw_name = descriptor[0:11].split(b"\x00", 1)[0]
        field_type = chr(descriptor[11])
        width = int(descriptor[16])
        decimals = int(descriptor[17])
        fields.append((_decode_dbf_field_name(raw_name), field_type, width, decimals))
        offset += 32

    expected_record_length = 1 + sum(field[2] for field in fields)
    if expected_record_length != record_length:
        raise ValueError(
            "DBF记录长度异常：{} header={} calculated={}".format(
                dbf_path, record_length, expected_record_length
            )
        )
    return header, header_length, record_length, fields


def _truncate_utf8(raw, width):
    if len(raw) <= width:
        return raw
    raw = raw[:width]
    while raw:
        try:
            raw.decode("utf-8")
            return raw
        except UnicodeDecodeError:
            raw = raw[:-1]
    return b""


def _format_dbf_character(value, width):
    if value is None:
        return b" " * width
    if isinstance(value, bytes):
        try:
            value = value.decode("utf-8")
        except Exception:
            value = value.decode("cp936", "replace")
    raw = str(value).encode("utf-8", "replace")
    raw = _truncate_utf8(raw, width)
    return raw + (b" " * (width - len(raw)))


def _format_dbf_numeric(value, width, decimals, field_name):
    if value is None or value == "":
        return b" " * width

    try:
        number = Decimal(str(value))

        if not number.is_finite():
            return b" " * width

        # 从模板规定的小数位开始尝试。
        # 如果固定小数位导致超过 DBF 字段宽度，
        # 则逐步减少小数位，而不改变字段本身的物理定义。
        max_decimals = max(0, int(decimals))

        for current_decimals in range(max_decimals, -1, -1):

            quantum = Decimal(1).scaleb(-current_decimals)

            formatted_number = number.quantize(
                quantum,
                rounding=ROUND_HALF_UP
            )

            if current_decimals > 0:
                text = format(
                    formatted_number,
                    ".{}f".format(current_decimals)
                )

                # DBF 数值字段不要求把无意义的尾随 0 全部写满。
                # 例如：
                # 10947500.00000000000
                # 写成：
                # 10947500
                text = text.rstrip("0").rstrip(".")

            else:
                text = format(formatted_number, ".0f")

            raw = text.encode("ascii")

            if len(raw) <= width:
                return (b" " * (width - len(raw))) + raw

        # 连整数部分都无法容纳时才是真正超出模板宽度。
        raise ValueError(
            "字段 {} 的数值超出模板宽度 {}：{}".format(
                field_name,
                width,
                value
            )
        )

    except (InvalidOperation, ValueError, TypeError) as exception:

        if isinstance(exception, ValueError) and \
                str(exception).startswith("字段 "):
            raise

        raise ValueError(
            "字段 {} 的数值无法写入 DBF：{} ({})".format(
                field_name,
                value,
                exception
            )
        )


def _format_dbf_value(value, field_type, width, decimals, field_name):
    if field_type == "C":
        return _format_dbf_character(value, width)
    if field_type in ("N", "F"):
        return _format_dbf_numeric(value, width, decimals, field_name)
    if field_type == "L":
        if value is None:
            raw = b"?"
        elif bool(value):
            raw = b"T"
        else:
            raw = b"F"
        return raw + (b" " * max(0, width - 1))
    if field_type == "D":
        if value is None or value == "":
            raw = b""
        elif isinstance(value, (datetime.datetime, datetime.date)):
            raw = value.strftime("%Y%m%d").encode("ascii")
        else:
            text = re.sub(r"[^0-9]", "", str(value))[:8]
            raw = text.encode("ascii")
        raw = raw[:width]
        return raw + (b" " * (width - len(raw)))
    # The bundled templates currently use C/N/F only. Preserve safety for
    # any future field type by writing a blank field rather than rebuilding schema.
    return b" " * width


def _source_field_lookup(feature):
    definition = feature.GetDefnRef()
    result = {}
    for index in range(definition.GetFieldCount()):
        name = definition.GetFieldDefn(index).GetNameRef()
        result[name.upper()] = index
    return result


def _source_value(feature, field_name, lookup, override_map):
    key = field_name.upper()
    if key in override_map:
        return override_map[key]
    index = lookup.get(key)
    if index is None:
        return None
    try:
        if not feature.IsFieldSetAndNotNull(index):
            return None
    except Exception:
        pass
    try:
        return feature.GetField(index)
    except Exception:
        return None


def rewrite_dbf_from_template(template_shp, out_shp, features, overrides=None):
    """Write records without allowing GDAL/OGR to rewrite template descriptors.

    OGR is used only for SHP/SHX geometry.  The final DBF header and all field
    descriptors are copied byte-for-byte from the validated template, then only
    the record count/date and record bytes are written.  This avoids the field
    type/width normalization observed with QGIS/GDAL update mode.
    """
    template_header, header_length, record_length, fields = _read_dbf_layout(template_shp)
    override_map = dict((str(k).upper(), v) for k, v in (overrides or {}).items())
    count = len(features)

    today = datetime.date.today()
    template_header[1] = max(0, min(255, today.year - 1900))
    template_header[2] = today.month
    template_header[3] = today.day
    template_header[4:8] = struct.pack("<I", count)

    out_dbf = os.path.splitext(out_shp)[0] + ".dbf"
    with open(out_dbf, "wb") as stream:
        stream.write(template_header[:header_length])
        for feature in features:
            lookup = _source_field_lookup(feature)
            record = bytearray(b" " * record_length)
            record[0] = 0x20
            position = 1
            for field_name, field_type, width, decimals in fields:
                value = _source_value(feature, field_name, lookup, override_map)
                raw = _format_dbf_value(value, field_type, width, decimals, field_name)
                if len(raw) != width:
                    raise RuntimeError("DBF字段编码长度错误：{} expected={} actual={}".format(field_name, width, len(raw)))
                record[position:position + width] = raw
                position += width
            stream.write(record)
        stream.write(b"\x1A")


def _copy_geometry_to_template(source_feature, destination_layer, source_srs=None):
    out_feature = ogr.Feature(destination_layer.GetLayerDefn())
    geometry = source_feature.GetGeometryRef()
    if geometry is not None:
        destination_srs = destination_layer.GetSpatialRef()
        out_feature.SetGeometry(transform_geometry(geometry, source_srs, destination_srs))
    if destination_layer.CreateFeature(out_feature) != 0:
        out_feature = None
        raise RuntimeError("写入标准模板几何失败：{}".format(destination_layer.GetName()))
    out_feature = None


def export_features_with_template(features, out_path, template_path, caption, source_srs=None, overrides=None):
    clone_template_shell(template_path, out_path)

    # Let OGR write geometry only.  Its DBF side effects are discarded below.
    ds, layer = open_layer(out_path, update=True)
    try:
        for feature in features:
            _copy_geometry_to_template(feature, layer, source_srs=source_srs)
        layer.SyncToDisk()
    finally:
        layer = None
        ds = None

    # Restore the exact template DBF descriptors and write attributes ourselves.
    rewrite_dbf_from_template(template_path, out_path, features, overrides=overrides)

    # OGR may touch sidecars during update; restore the validated metadata files.
    template_base = os.path.splitext(template_path)[0]
    output_base = os.path.splitext(out_path)[0]
    for extension in (".prj", ".cpg"):
        src = template_base + extension
        if os.path.exists(src):
            shutil.copy2(src, output_base + extension)

    verify_schema_matches(out_path, template_path, caption)

    # Verify that geometry and DBF contain the same number of records.
    ds, layer = open_layer(out_path)
    try:
        actual_count = int(layer.GetFeatureCount())
    finally:
        layer = None
        ds = None
    if actual_count != len(features):
        raise RuntimeError(
            "{} 记录数不一致：geometry/dbf={} expected={}".format(caption, actual_count, len(features))
        )
    return out_path

def copy_field_def(source_field):
    field = ogr.FieldDefn(source_field.GetNameRef(), source_field.GetType())
    try:
        field.SetSubType(source_field.GetSubType())
    except Exception:
        pass
    field.SetWidth(source_field.GetWidth())
    field.SetPrecision(source_field.GetPrecision())
    return field


def transform_geometry(geometry, source_srs, target_srs):
    if geometry is None:
        return None
    result = geometry.Clone()
    if source_srs is None or target_srs is None:
        return result
    try:
        if source_srs.IsSame(target_srs):
            return result
    except Exception:
        pass
    transform = osr.CoordinateTransformation(source_srs, target_srs)
    result.Transform(transform)
    return result


def union_geometries(features):
    union = None
    for feature in features:
        geometry = feature.GetGeometryRef()
        if geometry is None or geometry.IsEmpty():
            continue
        if union is None:
            union = geometry.Clone()
        else:
            union = union.Union(geometry)
    if union is None or union.IsEmpty():
        raise ValueError("计算单元流域几何为空。")
    return union


def _polygon_only_geometry(geometry):
    """Return only polygonal parts from an OGR intersection result.

    Polygon-on-polygon intersection can legitimately return LINESTRING/POINT when
    two polygons only touch at an edge/corner. ArcGIS Clip silently drops those
    lower-dimensional pieces; the QGIS backend must do the same instead of trying
    to write them into a POLYGON shapefile.
    """
    if geometry is None or geometry.IsEmpty():
        return None

    flat = normalized_geom_type(geometry.GetGeometryType())
    if flat in (ogr.wkbPolygon, ogr.wkbMultiPolygon):
        return geometry.Clone()

    if flat == ogr.wkbGeometryCollection:
        polygons = []
        for index in range(geometry.GetGeometryCount()):
            child = geometry.GetGeometryRef(index)
            kept = _polygon_only_geometry(child)
            if kept is None or kept.IsEmpty():
                continue
            kept_flat = normalized_geom_type(kept.GetGeometryType())
            if kept_flat == ogr.wkbPolygon:
                polygons.append(kept.Clone())
            elif kept_flat == ogr.wkbMultiPolygon:
                for child_index in range(kept.GetGeometryCount()):
                    polygon = kept.GetGeometryRef(child_index)
                    if polygon is not None and not polygon.IsEmpty():
                        polygons.append(polygon.Clone())

        if not polygons:
            return None
        if len(polygons) == 1:
            return polygons[0]
        multi = ogr.Geometry(ogr.wkbMultiPolygon)
        for polygon in polygons:
            multi.AddGeometry(polygon)
        return multi

    # Boundary/corner-only intersections (LINESTRING/POINT) are not area data.
    return None


def _geometry_for_output_layer(geometry, layer_geom_type):
    target = normalized_geom_type(layer_geom_type)
    if target in (ogr.wkbPolygon, ogr.wkbMultiPolygon):
        return _polygon_only_geometry(geometry)
    # create_clip_output is currently used for polygon land-use/soil layers.
    # Keep the fallback conservative for any future same-dimension use.
    if geometry is None or geometry.IsEmpty():
        return None
    actual = normalized_geom_type(geometry.GetGeometryType())
    if actual == target:
        return geometry.Clone()
    return None


def create_clip_output(source_layer, output_path, clip_geometry, clip_srs):
    remove_shapefile(output_path)
    driver = ogr.GetDriverByName("ESRI Shapefile")
    if driver is None:
        raise RuntimeError("QGIS GDAL 环境缺少 ESRI Shapefile 驱动。")
    source_srs = source_layer.GetSpatialRef()
    clip_for_source = transform_geometry(clip_geometry, clip_srs, source_srs)

    ds_out = driver.CreateDataSource(output_path)
    if ds_out is None:
        raise IOError("无法创建输出：{}".format(output_path))
    layer_name = os.path.splitext(os.path.basename(output_path))[0]
    out_layer = ds_out.CreateLayer(
        layer_name,
        srs=source_srs,
        geom_type=source_layer.GetGeomType(),
        options=["ENCODING=UTF-8"],
    )
    if out_layer is None:
        ds_out = None
        raise IOError("无法创建输出图层：{}".format(output_path))

    source_def = source_layer.GetLayerDefn()
    for index in range(source_def.GetFieldCount()):
        if out_layer.CreateField(copy_field_def(source_def.GetFieldDefn(index))) != 0:
            raise RuntimeError("复制字段失败：{}".format(source_def.GetFieldDefn(index).GetNameRef()))

    out_def = out_layer.GetLayerDefn()
    envelope = clip_for_source.GetEnvelope()  # minx, maxx, miny, maxy
    source_layer.SetSpatialFilterRect(envelope[0], envelope[2], envelope[1], envelope[3])
    source_layer.ResetReading()
    count = 0
    skipped_lower_dimension = 0
    target_geom_type = out_layer.GetGeomType()
    try:
        for feature in source_layer:
            geometry = feature.GetGeometryRef()
            if geometry is None or geometry.IsEmpty() or not geometry.Intersects(clip_for_source):
                continue
            clipped = geometry.Intersection(clip_for_source)
            if clipped is None or clipped.IsEmpty():
                continue
            compatible = _geometry_for_output_layer(clipped, target_geom_type)
            if compatible is None or compatible.IsEmpty():
                skipped_lower_dimension += 1
                continue
            out_feature = ogr.Feature(out_def)
            for field_index in range(source_def.GetFieldCount()):
                if feature.IsFieldSetAndNotNull(field_index):
                    out_feature.SetField(field_index, feature.GetField(field_index))
            out_feature.SetGeometry(compatible)
            if out_layer.CreateFeature(out_feature) != 0:
                out_feature = None
                raise RuntimeError("写入裁剪结果失败：{}".format(output_path))
            out_feature = None
            count += 1
    finally:
        source_layer.SetSpatialFilter(None)
    out_layer.SyncToDisk()
    if skipped_lower_dimension > 0:
        log(
            "QGIS_CLIP_SKIPPED_LOWER_DIMENSION output={} count={}".format(
                output_path, skipped_lower_dimension
            )
        )
    out_layer = None
    ds_out = None
    if count <= 0:
        raise ValueError("裁剪结果为空：{}".format(output_path))
    return output_path


def linear_tolerance_units(srs, meters=0.1):
    if srs is None:
        return 0.0
    try:
        if srs.IsGeographic():
            return meters / 111320.0
        units_in_meters = float(srs.GetLinearUnits() or 1.0)
        if units_in_meters > 0:
            return meters / units_in_meters
    except Exception:
        pass
    return 0.0


def numeric_or_inf(value):
    try:
        return float(value)
    except Exception:
        return float("inf")


def assign_node_unit(node_feature, node_srs, river_layer, river_srs, river_rvcd_field,
                     river_rvcs_field, wscd_to_wscu, wata_layer, wata_srs, wata_wscu_field):
    node_geom = node_feature.GetGeometryRef()
    if node_geom is None or node_geom.IsEmpty():
        return None

    # First priority: touching/nearby river; choose the smallest rvcs just like
    # the ArcGIS Pro backend's JOIN_ONE_TO_MANY + minimum-rvcs rule.
    node_in_river = transform_geometry(node_geom, node_srs, river_srs)
    x = node_in_river.GetX() if normalized_geom_type(node_in_river.GetGeometryType()) == ogr.wkbPoint else None
    y = node_in_river.GetY() if x is not None else None
    tolerance = linear_tolerance_units(river_srs, 0.1)
    best = None
    if x is not None and y is not None:
        river_layer.SetSpatialFilterRect(x - tolerance, y - tolerance, x + tolerance, y + tolerance)
    else:
        river_layer.SetSpatialFilter(node_in_river)
    river_layer.ResetReading()
    try:
        for river in river_layer:
            rvcd = feature_value(river, river_rvcd_field)
            if rvcd is None or len(str(rvcd).strip()) <= 3:
                continue
            wscu = wscd_to_wscu.get(str(rvcd).strip()[3:])
            if not wscu:
                continue
            geometry = river.GetGeometryRef()
            if geometry is None:
                continue
            try:
                distance = geometry.Distance(node_in_river)
            except Exception:
                distance = float("inf")
            if geometry.Intersects(node_in_river) or distance <= tolerance + 1e-12:
                rvcs = numeric_or_inf(feature_value(river, river_rvcs_field))
                if best is None or rvcs < best[0]:
                    best = (rvcs, wscu)
    finally:
        river_layer.SetSpatialFilter(None)
    if best is not None:
        return best[1]

    # Fallback: watershed polygon intersection.
    node_in_wata = transform_geometry(node_geom, node_srs, wata_srs)
    wata_layer.SetSpatialFilter(node_in_wata)
    wata_layer.ResetReading()
    try:
        for wata in wata_layer:
            geometry = wata.GetGeometryRef()
            if geometry is not None and geometry.Intersects(node_in_wata):
                value = feature_value(wata, wata_wscu_field)
                if value:
                    return str(value).strip()
    finally:
        wata_layer.SetSpatialFilter(None)
    return None


def verify_shape(path, caption):
    for extension in (".shp", ".shx", ".dbf"):
        sidecar = os.path.splitext(path)[0] + extension
        if not os.path.exists(sidecar):
            raise IOError("{}输出不完整，缺少：{}".format(caption, sidecar))


def preprocess(args):
    wata_shp = abs_path(args.wata)
    river_shp = abs_path(args.river)
    node_shp = abs_path(args.node)
    soil_shp = abs_path(args.soil)
    landuse_shp = abs_path(args.landuse)
    output_root = abs_path(args.output_dir)

    if " " in output_root:
        raise ValueError("输出根目录不能包含普通空格，以保持与 UnifiedHydroWorkflow 兼容：{}".format(output_root))

    for path, caption in (
        (wata_shp, "流域shp"),
        (river_shp, "河道shp"),
        (node_shp, "节点shp"),
        (landuse_shp, "土地利用shp"),
        (soil_shp, "土壤地质shp"),
    ):
        require_shapefile(path, caption)

    require_geometry(wata_shp, "流域shp", (ogr.wkbPolygon, ogr.wkbMultiPolygon))
    require_geometry(river_shp, "河道shp", (ogr.wkbLineString, ogr.wkbMultiLineString))
    require_geometry(node_shp, "节点shp", (ogr.wkbPoint,))
    require_geometry(landuse_shp, "土地利用shp", (ogr.wkbPolygon, ogr.wkbMultiPolygon))
    require_geometry(soil_shp, "土壤地质shp", (ogr.wkbPolygon, ogr.wkbMultiPolygon))

    if not os.path.isdir(output_root):
        os.makedirs(output_root)

    template_root = internal_template_dir()
    tmpl_wata = os.path.join(template_root, "wata.shp")
    tmpl_rivl = os.path.join(template_root, "rivl.shp")
    tmpl_node = os.path.join(template_root, "node.shp")
    for path, caption in ((tmpl_wata, "内置流域模板"), (tmpl_rivl, "内置河道模板"), (tmpl_node, "内置节点模板")):
        require_shapefile(path, caption)
        for extension in (".prj", ".cpg"):
            sidecar = os.path.splitext(path)[0] + extension
            if not os.path.exists(sidecar):
                raise IOError("{}缺少内置模板文件：{}".format(caption, sidecar))

    log("PREPROCESS_BEGIN")
    log("PREPROCESS_ENGINE QGIS version={}".format(qgis_version or "unknown"))
    log("PREPROCESS_CONFIG output={}".format(output_root))
    log("UNIT_SPLIT_FIELD WSCU_Name")
    log("SCHEMA_MODE internal_embedded_template_fileclone_qgis_v5052_raw_dbf_geomfilter")
    log("SCHEMA_TEMPLATE root={}".format(template_root))

    wata_ds, wata_layer = open_layer(wata_shp)
    river_ds, river_layer = open_layer(river_shp)
    node_ds, node_layer = open_layer(node_shp)
    land_ds, land_layer = open_layer(landuse_shp)
    soil_ds, soil_layer = open_layer(soil_shp)
    try:
        wata_wscd = actual_field(wata_layer, "WSCD")
        wata_wscu = actual_field(wata_layer, "WSCU_Name")
        river_rvcd = actual_field(river_layer, "RVCD")
        actual_field(river_layer, "FRVCD")
        river_rvcs = actual_field(river_layer, "rvcs")
        actual_field(node_layer, "NDCD")
        actual_field(land_layer, "XDMDM")
        actual_field(soil_layer, "TRZDBM")

        wscd_to_wscu = {}
        unit_wata = {}
        wata_layer.ResetReading()
        for feature in wata_layer:
            wscd = feature_value(feature, wata_wscd)
            wscu = feature_value(feature, wata_wscu)
            if not wscd or not wscu:
                continue
            wscd = str(wscd).strip()
            wscu = str(wscu).strip()
            validate_unit_folder_name(wscu)
            if len(wscd) > 3:
                wscd_to_wscu[wscd[3:]] = wscu
                unit_wata.setdefault(wscu, []).append(clone_feature(feature))

        units = sorted(unit_wata.keys(), key=natural_unit_key)
        if not units:
            raise ValueError("WATA 中没有可用的 WSCU_Name/WSCD 记录。")
        log("UNITS_DISCOVERED total={}".format(len(units)))

        log("PREPROCESS_STAGE 1/4 river_assignment")
        unit_rivers = dict((unit, []) for unit in units)
        river_layer.ResetReading()
        for feature in river_layer:
            rvcd = feature_value(feature, river_rvcd)
            if rvcd is None or len(str(rvcd).strip()) <= 3:
                continue
            wscu = wscd_to_wscu.get(str(rvcd).strip()[3:])
            if wscu in unit_rivers:
                unit_rivers[wscu].append(clone_feature(feature))

        log("PREPROCESS_STAGE 2/4 node_assignment")
        unit_nodes = dict((unit, []) for unit in units)
        node_srs = node_layer.GetSpatialRef()
        river_srs = river_layer.GetSpatialRef()
        wata_srs = wata_layer.GetSpatialRef()
        node_layer.ResetReading()
        for feature in node_layer:
            wscu = assign_node_unit(
                feature,
                node_srs,
                river_layer,
                river_srs,
                river_rvcd,
                river_rvcs,
                wscd_to_wscu,
                wata_layer,
                wata_srs,
                wata_wscu,
            )
            if wscu in unit_nodes:
                unit_nodes[wscu].append(clone_feature(feature))

        log("PREPROCESS_STAGE 3/4 unit_export")
        completed = 0
        for wscu in units:
            if not unit_rivers.get(wscu):
                raise ValueError("{} 没有匹配到河道要素。".format(wscu))
            if not unit_nodes.get(wscu):
                raise ValueError("{} 没有匹配到节点要素。".format(wscu))

            folder = os.path.join(output_root, wscu)
            if not os.path.isdir(folder):
                os.makedirs(folder)

            out_wata = export_features_with_template(
                unit_wata[wscu],
                os.path.join(folder, "wata.shp"),
                tmpl_wata,
                "{} WATA".format(wscu),
                source_srs=wata_srs,
            )
            out_rivl = export_features_with_template(
                unit_rivers[wscu],
                os.path.join(folder, "rivl.shp"),
                tmpl_rivl,
                "{} RIVL".format(wscu),
                source_srs=river_srs,
                overrides={"WSCU_Name": wscu},
            )
            out_node = export_features_with_template(
                unit_nodes[wscu],
                os.path.join(folder, "node.shp"),
                tmpl_node,
                "{} NODE".format(wscu),
                source_srs=node_srs,
                overrides={"WSCU_Name": wscu},
            )

            clip_geometry = union_geometries(unit_wata[wscu])
            out_land = create_clip_output(
                land_layer, os.path.join(folder, "土地利用.shp"), clip_geometry, wata_srs
            )
            out_soil = create_clip_output(
                soil_layer, os.path.join(folder, "土壤类型.shp"), clip_geometry, wata_srs
            )

            for path, caption in (
                (out_wata, "WATA"),
                (out_rivl, "RIVL"),
                (out_node, "NODE"),
                (out_soil, "土壤"),
                (out_land, "土地利用"),
            ):
                verify_shape(path, "{} {}".format(wscu, caption))

            for filename in os.listdir(folder):
                lower = filename.lower()
                if lower.endswith((".sbn", ".sbx", ".xml", ".qix")):
                    try:
                        os.remove(os.path.join(folder, filename))
                    except Exception:
                        pass

            completed += 1
            log("PREPROCESS_UNIT unit={} status=ok index={}/{}".format(wscu, completed, len(units)))

        log("PREPROCESS_STAGE 4/4 cleanup")
        log("PREPROCESS_SUMMARY total={} success={} failed=0 output={}".format(len(units), completed, output_root))
        return 0
    finally:
        soil_layer = None
        soil_ds = None
        land_layer = None
        land_ds = None
        node_layer = None
        node_ds = None
        river_layer = None
        river_ds = None
        wata_layer = None
        wata_ds = None


def main(argv=None):
    global ogr, osr, qgis_version
    args = parse_args(argv)
    try:
        from qgis.core import Qgis
        qgis_version = getattr(Qgis, "QGIS_VERSION", "unknown")
    except Exception as exception:
        return fail(
            "无法导入 qgis.core。请使用 QGIS 自带的 python-qgis.bat 运行：{}".format(exception), 3
        )
    try:
        from osgeo import ogr as _ogr, osr as _osr, gdal
        ogr = _ogr
        osr = _osr
        gdal.UseExceptions()
    except Exception as exception:
        return fail("QGIS Python 环境无法导入 GDAL/OGR：{}".format(exception), 4)

    try:
        return preprocess(args)
    except Exception as exception:
        log("PREPROCESS_EXCEPTION {}".format(exception))
        details = traceback.format_exc().replace("\r", "").replace("\n", " | ")
        log("QGIS_ERROR " + details)
        return 1


if __name__ == "__main__":
    sys.exit(main())
