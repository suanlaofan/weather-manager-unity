#!/usr/bin/env python3
"""Restore desktop PNG textures from the KTX2 images embedded in Ange GLBs."""

from __future__ import annotations

import argparse
import binascii
import ctypes
import json
from pathlib import Path
import struct
import zlib


PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
RGBA32 = 13
IMAGE_PROPERTIES = (
    ("normal", "normalTexture"),
    ("orm", "metallicRoughnessTexture"),
    ("color", "baseColorTexture"),
)


class KtxDecoder:
    def __init__(self, library_path: Path) -> None:
        self._library = ctypes.CDLL(str(library_path))
        self._library.ktx_load_ktx.argtypes = [
            ctypes.c_void_p,
            ctypes.c_uint32,
            ctypes.POINTER(ctypes.c_int),
        ]
        self._library.ktx_load_ktx.restype = ctypes.c_void_p
        self._library.ktx_get_baseWidth.argtypes = [ctypes.c_void_p]
        self._library.ktx_get_baseWidth.restype = ctypes.c_uint32
        self._library.ktx_get_baseHeight.argtypes = [ctypes.c_void_p]
        self._library.ktx_get_baseHeight.restype = ctypes.c_uint32
        self._library.ktxTexture2_TranscodeBasis.argtypes = [
            ctypes.c_void_p,
            ctypes.c_uint32,
            ctypes.c_uint32,
        ]
        self._library.ktxTexture2_TranscodeBasis.restype = ctypes.c_int
        self._library.ktx_get_data.argtypes = [
            ctypes.c_void_p,
            ctypes.POINTER(ctypes.c_void_p),
            ctypes.POINTER(ctypes.c_uint32),
        ]
        self._library.ktx_get_image_offset.argtypes = [
            ctypes.c_void_p,
            ctypes.c_uint32,
            ctypes.c_uint32,
            ctypes.c_uint32,
            ctypes.POINTER(ctypes.c_uint32),
        ]
        self._library.ktx_get_image_offset.restype = ctypes.c_int
        self._library.ktx_get_image_size.argtypes = [ctypes.c_void_p, ctypes.c_uint32]
        self._library.ktx_get_image_size.restype = ctypes.c_uint32
        self._library.ktx_unload_ktx.argtypes = [ctypes.c_void_p]

    def decode_rgba(self, payload: bytes) -> tuple[int, int, bytes]:
        buffer = ctypes.create_string_buffer(payload)
        status = ctypes.c_int()
        handle = self._library.ktx_load_ktx(buffer, len(payload), ctypes.byref(status))
        if not handle or status.value != 0:
            raise RuntimeError(f"KTX load failed with status {status.value}")

        try:
            width = self._library.ktx_get_baseWidth(handle)
            height = self._library.ktx_get_baseHeight(handle)
            status_value = self._library.ktxTexture2_TranscodeBasis(handle, RGBA32, 0)
            if status_value != 0:
                raise RuntimeError(f"KTX transcode failed with status {status_value}")

            data_pointer = ctypes.c_void_p()
            data_length = ctypes.c_uint32()
            self._library.ktx_get_data(
                handle,
                ctypes.byref(data_pointer),
                ctypes.byref(data_length),
            )
            image_offset = ctypes.c_uint32()
            status_value = self._library.ktx_get_image_offset(
                handle,
                0,
                0,
                0,
                ctypes.byref(image_offset),
            )
            if status_value != 0:
                raise RuntimeError(f"KTX image lookup failed with status {status_value}")

            expected_size = width * height * 4
            image_size = self._library.ktx_get_image_size(handle, 0)
            if image_size < expected_size or image_offset.value + expected_size > data_length.value:
                raise RuntimeError(
                    f"Unexpected KTX payload size {image_size} for {width}x{height} RGBA"
                )
            pixels = ctypes.string_at(data_pointer.value + image_offset.value, expected_size)
            return width, height, pixels
        finally:
            self._library.ktx_unload_ktx(handle)


def png_chunk(chunk_type: bytes, payload: bytes) -> bytes:
    checksum = binascii.crc32(chunk_type + payload) & 0xFFFFFFFF
    return struct.pack(">I", len(payload)) + chunk_type + payload + struct.pack(">I", checksum)


def encode_png(width: int, height: int, rgba: bytes) -> bytes:
    stride = width * 4
    scanlines = bytearray((stride + 1) * height)
    for row in range(height):
        target_offset = row * (stride + 1)
        source_offset = row * stride
        scanlines[target_offset] = 0
        scanlines[target_offset + 1 : target_offset + stride + 1] = rgba[
            source_offset : source_offset + stride
        ]
    header = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    return (
        PNG_SIGNATURE
        + png_chunk(b"IHDR", header)
        + png_chunk(b"IDAT", zlib.compress(scanlines, 6))
        + png_chunk(b"IEND", b"")
    )


def read_glb(glb_path: Path) -> tuple[dict, bytes]:
    data = glb_path.read_bytes()
    if len(data) < 20:
        raise RuntimeError(f"Invalid GLB: {glb_path}")
    magic, version, total_length = struct.unpack_from("<4sII", data, 0)
    if magic != b"glTF" or version != 2 or total_length > len(data):
        raise RuntimeError(f"Unsupported GLB header: {glb_path}")

    json_document = None
    binary_chunk = None
    offset = 12
    while offset < total_length:
        chunk_length, chunk_type = struct.unpack_from("<I4s", data, offset)
        offset += 8
        chunk = data[offset : offset + chunk_length]
        offset += chunk_length
        if chunk_type == b"JSON":
            json_document = json.loads(chunk.decode("utf-8").rstrip("\x00 "))
        elif chunk_type == b"BIN\x00":
            binary_chunk = chunk
    if json_document is None or binary_chunk is None:
        raise RuntimeError(f"GLB is missing JSON or BIN data: {glb_path}")
    return json_document, binary_chunk


def property_for_image(image_name: str, image_index: int) -> str:
    lowered = image_name.lower()
    for marker, property_name in IMAGE_PROPERTIES:
        if marker in lowered:
            return property_name
    fallback = ("baseColorTexture", "metallicRoughnessTexture", "normalTexture")
    if image_index >= len(fallback):
        raise RuntimeError(f"Cannot map GLB image {image_name!r}")
    return fallback[image_index]


def find_ktx_library(project_root: Path) -> Path:
    candidates = sorted(
        project_root.glob(
            "Library/PackageCache/com.unity.cloud.ktx@*/Runtime/Plugins/"
            "x86_64/ktx_unity.bundle/Contents/MacOS/ktx_unity"
        )
    )
    if not candidates:
        raise FileNotFoundError("Unity KTX decoder library was not found in Library/PackageCache")
    return candidates[-1]


def restore(project_root: Path) -> None:
    ange_root = project_root / "Assets/WeatherManager/Art/Ange"
    output_root = ange_root / "DesktopTextures"
    decoder = KtxDecoder(find_ktx_library(project_root))
    glb_paths = sorted(ange_root.rglob("*.glb"))
    restored = 0
    base_color_fingerprints: set[int] = set()

    for glb_path in glb_paths:
        document, binary_chunk = read_glb(glb_path)
        restored_properties: set[str] = set()
        for image_index, image in enumerate(document.get("images", [])):
            buffer_view = document["bufferViews"][image["bufferView"]]
            start = buffer_view.get("byteOffset", 0)
            end = start + buffer_view["byteLength"]
            payload = binary_chunk[start:end]
            property_name = property_for_image(image.get("name", ""), image_index)
            width, height, rgba = decoder.decode_rgba(payload)
            target = output_root / f"{glb_path.stem}-{property_name}.png"
            if not target.with_suffix(target.suffix + ".meta").exists():
                raise FileNotFoundError(f"Refusing to replace texture without preserved meta: {target}")
            target.write_bytes(encode_png(width, height, rgba))
            restored_properties.add(property_name)
            restored += 1
            if property_name == "baseColorTexture":
                base_color_fingerprints.add(binascii.crc32(rgba) & 0xFFFFFFFF)

        if restored_properties != {property_name for _, property_name in IMAGE_PROPERTIES}:
            raise RuntimeError(
                f"Expected three PBR textures in {glb_path}, found {sorted(restored_properties)}"
            )

    expected = len(glb_paths) * len(IMAGE_PROPERTIES)
    if restored != expected or len(glb_paths) != 46:
        raise RuntimeError(f"Restored {restored}/{expected} textures from {len(glb_paths)} GLBs")
    if len(base_color_fingerprints) < len(glb_paths) // 2:
        raise RuntimeError("Restored base-color textures do not have enough visual variation")
    print(
        f"WEATHER_MANAGER_ANGE_TEXTURE_RESTORE_PASS:models={len(glb_paths)}:"
        f"textures={restored}:uniqueBaseColors={len(base_color_fingerprints)}"
    )


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("project_root", nargs="?", default=".")
    args = parser.parse_args()
    restore(Path(args.project_root).resolve())


if __name__ == "__main__":
    main()
