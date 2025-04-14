/*
 * Copyright 2024 Google LLC
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.Runtime.InteropServices;
using System.Diagnostics;

namespace vkqlisteditor.editor;

public static class RuntimeDataImporter
{
    private const int FileHeaderSizeBytes = (22 * 4);
    private const int ShortcutTableSizeBytes = (27 * 4);
    private const uint FileIdentifier = 0x564b5141;
    private const uint FileFormatVersion = 0x010200;
    private const uint MinimumLibraryVersion = 0x010200;

    private static string s_Unknown = "[unknown]";

    private struct RuntimeFileSizes
    {
        public int HeaderSize = 0;
        public int DeviceListSize = 0;
        public int DriverAllowListSize = 0;
        public int DriverDenyListSize = 0;
        public int GpuAllowListSize = 0;
        public int GpuDenyListSize = 0;
        public int ShortcutListSize = 0;
        public int SocAllowListSize = 0;
        public int SocDenyListSize = 0;
        public int StringTableSize = 0;
        public int TotalSize = 0;

        public RuntimeFileSizes()
        {
        }
    }
    public static bool Import(RuntimeData runtimeData, string dataPath)
    {
        runtimeData.DeviceAllowList.Clear();
        runtimeData.GpuPredictAllowList.Clear();
        runtimeData.GpuPredictDenyList.Clear();
        runtimeData.DriverAllowList.Clear();
        runtimeData.DriverDenyList.Clear();

        var fileBuffer = File.ReadAllBytes(dataPath);
        Span<byte> fileSpan = fileBuffer;

        var currentBufferOffset = FileHeaderSizeBytes;
        var headerSpan = fileSpan.Slice(0, currentBufferOffset);
        var fileHeader = MemoryMarshal.Cast<byte, uint>(headerSpan);

        // Populate header information
        // vkquality_file_format.h - VkQualityFileHeader
        Debug.Assert(fileHeader[0] == FileIdentifier); // file_identifier
        Debug.Assert(fileHeader[1] == FileFormatVersion); // file_format_version
        Debug.Assert(fileHeader[2] == MinimumLibraryVersion); // library_minimum_version
        var exportedListFileVersion = fileHeader[3]; // list_version
        var minApiForFutureRecommendation = fileHeader[4]; // min_future_vulkan_recommendation_api
        var deviceTableCount = fileHeader[5]; // device_list_count
        var driverAllowCount = fileHeader[6]; // driver_allow_count
        var driverDenyCount = fileHeader[7]; // driver_deny_count
        var gpuAllowCount = fileHeader[8]; // gpu_allow_predict_count
        var gpuDenyCount = fileHeader[9]; // gpu_deny_predict_count
        var socAllowCount = fileHeader[10]; // soc_allow_count
        var socDenyCount = fileHeader[11]; // soc_deny_count
        var stringTableCount = fileHeader[12]; // string_table_count
        var deviceListOffset = fileHeader[13]; // device_list_offset
        var deviceListShortcutsOffset = fileHeader[14]; // device_list_shortcuts_offset
        var driverAllowListOffset = fileHeader[15]; // driver_allow_offset
        var driverDenyListOffset = fileHeader[16]; // driver_deny_offset
        var gpuAllowListOffset = fileHeader[17]; // gpu_allow_predict_offset
        var gpuDenyListOffset = fileHeader[18]; // gpu_deny_predict_offset
        var socAllowListOffset = fileHeader[19]; // soc_allow_offset
        var socDenyListOffset = fileHeader[20]; // soc_deny_offset
        var stringTableOffset = fileHeader[21]; // string_table_offset

        RuntimeFileSizes fileSizes = default;
        CalculateFileSizes((int)deviceTableCount, (int)driverAllowCount, (int)driverDenyCount, (int)gpuAllowCount,
            (int)gpuDenyCount, (int)socAllowCount, (int)socDenyCount, (int)stringTableCount, fileBuffer.Length, ref fileSizes);

        Debug.Assert(fileSizes.StringTableSize == deviceListOffset - stringTableOffset);

        var stringTableSpan = fileSpan.Slice((int)stringTableOffset, fileSizes.StringTableSize);
        var stringTable = ImportStringTable(stringTableSpan, stringTableCount, stringTableOffset);

        var deviceTableSpan = fileSpan.Slice((int)deviceListOffset, fileSizes.DeviceListSize);
        ImportDeviceTable(runtimeData, deviceTableSpan, (int)deviceTableCount, stringTable);

        if (gpuAllowCount > 0)
        {
            var gpuAllowTableSpan = fileSpan.Slice((int)gpuAllowListOffset, fileSizes.GpuAllowListSize);
            ImportGpuTable(runtimeData, true, gpuAllowTableSpan, (int)gpuAllowCount, stringTable);
        }

        if (gpuDenyCount > 0)
        {
            var gpuDenyTableSpan = fileSpan.Slice((int)gpuDenyListOffset, fileSizes.GpuDenyListSize);
            ImportGpuTable(runtimeData, false, gpuDenyTableSpan, (int)gpuDenyCount, stringTable);
        }

        if (driverAllowCount > 0 && socAllowCount > 0)
        {
            var socAllowTableSpan = fileSpan.Slice((int)socAllowListOffset, fileSizes.SocAllowListSize);
            var driverAllowTableSpan = fileSpan.Slice((int)driverAllowListOffset, fileSizes.DriverAllowListSize);
            ImportSocTable(runtimeData, true, socAllowTableSpan, driverAllowTableSpan, (int)socAllowCount, stringTable);
        }

        if (driverDenyCount > 0 && socDenyCount > 0)
        {
            var socDenyTableSpan = fileSpan.Slice((int)socDenyListOffset, fileSizes.SocDenyListSize);
            var driverDenyTableSpan = fileSpan.Slice((int)driverDenyListOffset, fileSizes.DriverDenyListSize);
            ImportSocTable(runtimeData, false, socDenyTableSpan, driverDenyTableSpan, (int)socDenyCount, stringTable);
        }

        return true;
    }

    private static void CalculateFileSizes(int deviceTableCount, int driverAllowTableCount, int driverDenyTableCount,
        int gpuAllowTableCount, int gpuDenyTableCount, int socAllowTableCount, int socDenyTableCount, int stringTableCount, int totalSize,
        ref RuntimeFileSizes fileSizes)
    {
        fileSizes.HeaderSize = FileHeaderSizeBytes;
        // vkquality_file_format.h VkQualityDeviceAllowListEntry
        // Currently 4 x uint32
        fileSizes.DeviceListSize = deviceTableCount * 16;
        fileSizes.DriverAllowListSize = driverAllowTableCount * 4;
        fileSizes.DriverDenyListSize = driverDenyTableCount * 4;
        // vkquality_file_format.h VkQualityGpuPredictEntry
        // Currently 5 x uint32
        fileSizes.GpuAllowListSize = gpuAllowTableCount * 20;
        fileSizes.GpuDenyListSize = gpuDenyTableCount * 20;
        fileSizes.ShortcutListSize = ShortcutTableSizeBytes;
        // vkquality_file_format.h VkQualityDriverSoCEntry
        // Currently 3 x uint32
        fileSizes.SocAllowListSize = socAllowTableCount * 12;
        fileSizes.SocDenyListSize = socDenyTableCount * 12;
        fileSizes.StringTableSize = totalSize - (fileSizes.HeaderSize + fileSizes.DeviceListSize + fileSizes.DriverAllowListSize
                              + fileSizes.DriverDenyListSize + fileSizes.SocAllowListSize
                              + fileSizes.SocDenyListSize + fileSizes.GpuAllowListSize
                              + fileSizes.GpuDenyListSize + fileSizes.ShortcutListSize);
        fileSizes.TotalSize = totalSize;
    }

    private static List<string> ImportStringTable(Span<byte> tableBuffer, uint stringTableCount, uint stringTableOffset)
    {
        List<string> stringTable = new List<string>();
        var actualEntryCount = (int)stringTableCount;
        var stringBuffer = tableBuffer.Slice(actualEntryCount * 4);
        var offsetTable = MemoryMarshal.Cast<byte, uint>(tableBuffer);
        var offsetIndex = 0;
        var stringIndex = 0;

        // read the null string at index 0
        ++offsetIndex;
        var nullString = stringBuffer.Slice(stringIndex, 1);
        stringTable.Add(string.Empty);
        stringIndex += 1;

        for (int ix = 1; ix < actualEntryCount; ++ix) {
            // Read the offset table entry for the current and the next string
            var stringOffset = (int)offsetTable[offsetIndex];
            var stringOffsetNext = tableBuffer.Length + (int)stringTableOffset;
            if(ix < actualEntryCount - 1) {
                stringOffsetNext = (int)offsetTable[offsetIndex + 1];
            }
            var stringLength = stringOffsetNext - stringOffset;
            ++offsetIndex;

            // Read the string bytes in UTF8
            var stringBytes = stringBuffer.Slice(stringIndex, stringLength - 1);
            var str = System.Text.Encoding.UTF8.GetString(stringBytes.ToArray());
            stringTable.Add(str);
            stringIndex += stringLength;
        }
        return stringTable;
    }
    private static void ImportDeviceTable(RuntimeData runtimeData, Span<byte> tableBuffer, int tableCount, List<string> stringTable)
    {
        var deviceTable = MemoryMarshal.Cast<byte, uint>(tableBuffer);
        int spanOffset = 0;
        for (int ix = 0; ix < tableCount; ++ix) {
            var brandIndex = (int)deviceTable[spanOffset];
            var deviceIndex = (int)deviceTable[spanOffset + 1];
            var minApi = (int)deviceTable[spanOffset + 2];
            var driverVer = deviceTable[spanOffset + 3];
            spanOffset += 4;

            var brand = brandIndex >= 0 && brandIndex < stringTable.Count ? stringTable[brandIndex] : s_Unknown;
            var device = deviceIndex >= 0 && deviceIndex < stringTable.Count ? stringTable[deviceIndex] : s_Unknown;

            runtimeData.DeviceAllowList.Add(new DeviceAllowListRecord(brand, device, minApi, driverVer));
        }
    }
    private static void ImportGpuTable(RuntimeData runtimeData, bool allow, Span<byte> tableBuffer, int tableCount, List<string> stringTable)
    {
        var deviceTable = MemoryMarshal.Cast<byte, uint>(tableBuffer);
        int spanOffset = 0;
        for (int ix = 0; ix < tableCount; ++ix) {
            var nameIndex = (int)deviceTable[spanOffset];
            var minApi = (int)deviceTable[spanOffset + 1];
            var deviceId = deviceTable[spanOffset + 2];
            var vendorId = deviceTable[spanOffset + 3];
            var driverVer = deviceTable[spanOffset + 4];
            spanOffset += 5;

            var device = nameIndex >= 0 && nameIndex < stringTable.Count ? stringTable[nameIndex] : s_Unknown;

            if (allow) {
                runtimeData.GpuPredictAllowList.Add(new GpuPredictRecord(string.Empty, device, deviceId, vendorId, minApi, driverVer));
            }
            else {
                runtimeData.GpuPredictDenyList.Add(new GpuPredictRecord(string.Empty, device, deviceId, vendorId, minApi, driverVer));
            }
        }
    }
    private static void ImportSocTable(RuntimeData runtimeData, bool allow, Span<byte> socBuffer, Span<byte> fingerprintBuffer, int tableCount, List<string> stringTable)
    {
        var socTable = MemoryMarshal.Cast<byte, uint>(socBuffer);
        var fingerprintTable = MemoryMarshal.Cast<byte, uint>(fingerprintBuffer);
        int spanOffset = 0;

        for (int ix = 0; ix < tableCount; ++ix) {
            var fingerprintCount = (int)socTable[spanOffset];
            var fingerprintTableOffset = (int)socTable[spanOffset + 1];
            var socIndex = (int)socTable[spanOffset + 2];
            spanOffset += 3;

            var soc = socIndex >= 0 && socIndex < stringTable.Count ? stringTable[socIndex] : s_Unknown;

            for (int jx = 0; jx < fingerprintCount; ++jx) {
                var fingerprintIndex = (int)fingerprintTable[fingerprintTableOffset + jx];

                var fingerprint = fingerprintIndex >= 0 && fingerprintIndex < stringTable.Count ? stringTable[fingerprintIndex] : s_Unknown;

                if (allow) {
                    runtimeData.DriverAllowList.Add(new DriverFingerprintRecord(soc, fingerprint));
                }
                else {
                    runtimeData.DriverDenyList.Add(new DriverFingerprintRecord(soc, fingerprint));
                }
            }
        }
    }
}
