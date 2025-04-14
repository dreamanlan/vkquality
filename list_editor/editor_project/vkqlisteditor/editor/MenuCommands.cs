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

using System.IO.Compression;

namespace vkqlisteditor.editor;

using System.Diagnostics;
using System.Text.Json;
using Terminal.Gui;

public static class MenuCommands
{
    public static bool ExportVkq(RuntimeData runtimeData)
    {
        bool success = false;
        if (runtimeData is {MainProject: not null, ProjectPath: not null})
        {
            var exportPath = Path.ChangeExtension(runtimeData.ProjectPath, "vkq");
            success = RuntimeDataExporter.Export(runtimeData, exportPath);
        }

        return success;
    }

    public static void ExportAar(RuntimeData runtimeData)
    {
        if (runtimeData is {MainProject: not null, ProjectPath: not null})
        {
            // Make sure we save the current project state
            SaveProject(runtimeData);            

            // Generate the .vkq file to copy into the .aar
            if (!ExportVkq(runtimeData)) return;

            var vkqPath = Path.ChangeExtension(runtimeData.ProjectPath, "vkq");
            var templatePath = Path.GetDirectoryName(runtimeData.ProjectPath);
            templatePath = $"{templatePath}{Path.DirectorySeparatorChar}templateproject";

            if (Directory.Exists(templatePath))
            {
                Directory.Delete(templatePath, true);
            }
            Directory.CreateDirectory(templatePath);
            AarTemplate.CreateProjectTemplate(vkqPath, templatePath);
        }
    }

    public static void ExportCsv(RuntimeData runtimeData)
    {
        var aTypes = new List<string>() { ".csv" };
        var sd = new SaveDialog("Export CSV files", "Choose a directory and base filename for the CSV files", aTypes);
        sd.DirectoryPath = runtimeData.UserPreferences.GetLastUsedPath();
        sd.FilePath = Path.Combine(runtimeData.UserPreferences.GetLastUsedPath(), "vkq.csv");
        Application.Run(sd);

        if (sd.Canceled || sd.FilePath == null)
            return;
        string? pathString = sd.FilePath.ToString();
        if (pathString == null)
            return;
        var pathDirectory = Path.GetDirectoryName(pathString);
        if (pathDirectory != null)
            runtimeData.UserPreferences.SetLastUsedPath(pathDirectory);

        Debug.Assert(null != pathDirectory);
        var fileName = Path.GetFileNameWithoutExtension(pathString);

        var deviceListCsvPath = Path.Combine(pathDirectory, $"{fileName}_device_list.csv");
        using (var fs = new StreamWriter(deviceListCsvPath)) {
            fs.WriteLine("brand,device,min_api,device_version");
            foreach (var item in runtimeData.DeviceAllowList) {
                fs.WriteLine("{0},{1},{2},{3}", item.Brand, item.Device, item.MinApi, item.DriverVersion);
            }
            fs.Close();
        }

        var gpuAllowCsvPath = Path.Combine(pathDirectory, $"{fileName}_gpu_allow.csv");
        using (var fs = new StreamWriter(gpuAllowCsvPath)) {
            fs.WriteLine("device,device_id,vendor_id,min_api,device_version");
            foreach (var item in runtimeData.GpuPredictAllowList) {
                fs.WriteLine("{0},{1},{2},{3},{4}", item.DeviceName, item.DeviceId, item.VendorId, item.MinApi, item.DriverVersion);
            }
            fs.Close();
        }

        var gpuDenyCsvPath = Path.Combine(pathDirectory, $"{fileName}_gpu_deny.csv");
        using (var fs = new StreamWriter(gpuDenyCsvPath)) {
            fs.WriteLine("device,device_id,vendor_id,min_api,device_version");
            foreach (var item in runtimeData.GpuPredictDenyList) {
                fs.WriteLine("{0},{1},{2},{3},{4}", item.DeviceName, item.DeviceId, item.VendorId, item.MinApi, item.DriverVersion);
            }
            fs.Close();
        }

        var socAllowCsvPath = Path.Combine(pathDirectory, $"{fileName}_soc_allow.csv");
        using (var fs = new StreamWriter(socAllowCsvPath)) {
            fs.WriteLine("soc,device_fingerprint");
            foreach (var item in runtimeData.DriverAllowList) {
                fs.WriteLine("{0},{1}", item.Soc, item.DriverFingerprint);
            }
            fs.Close();
        }

        var socDenyCsvPath = Path.Combine(pathDirectory, $"{fileName}_soc_deny.csv");
        using (var fs = new StreamWriter(socDenyCsvPath)) {
            fs.WriteLine("soc,device_fingerprint");
            foreach (var item in runtimeData.DriverDenyList) {
                fs.WriteLine("{0},{1}", item.Soc, item.DriverFingerprint);
            }
            fs.Close();
        }
    }

    private static void DoSaveChangesDialog(RuntimeData runtimeData)
    {
        if (runtimeData is {MainProject: not null, ProjectNeedsSave: true})
        {
            var savePressed = false;
            var save = new Button ("Save", is_default: true);
            save.Clicked += () => { savePressed = true; Application.RequestStop (); };
            var dontSave = new Button ("Don't Save");
            dontSave.Clicked += () => { Application.RequestStop (); };
            var d = new Dialog ("Save changes before closing?", 60, 20, save, dontSave);
            Application.Run(d);
            if (savePressed)
            {
                SaveProject(runtimeData);
            }
        }
    }
    public static void NewProject(RuntimeData runtimeData, EditorWindow? editorWindow)
    {
        DoSaveChangesDialog(runtimeData);
        
        var aTypes = new List<string> () { ".json" };
        var sd = new SaveDialog ("Save project file", "Choose a directory and filename for the project", aTypes);
        sd.DirectoryPath = runtimeData.UserPreferences.GetLastUsedPath();
        sd.FilePath = Path.Combine (runtimeData.UserPreferences.GetLastUsedPath(), "vkqualityproject.json");
        Application.Run (sd);

        var createNewProject = false;
        if (sd.Canceled || sd.FilePath == null) return;
        string? pathString = sd.FilePath.ToString();
        if (pathString == null) return;
        var pathDirectory = Path.GetDirectoryName(pathString);
        if (pathDirectory != null) runtimeData.UserPreferences.SetLastUsedPath(pathDirectory);

        if (File.Exists(pathString)) {
            if (MessageBox.Query ("Save File",
                    "File already exists. Overwrite anyway?", "No", "Ok") == 1)
            {
                createNewProject = true;
            }
        } 
        else
        {
            createNewProject = true;
        }

        if (!createNewProject) return;
        runtimeData.ProjectPath = pathString;
        runtimeData.MainProject = new DeviceListProject();
        NewProjectDefaults.SetNewProjectDefaults(runtimeData.MainProject);
        editorWindow?.UpdatePaths(runtimeData);
    }

    public static void OpenProject(RuntimeData runtimeData, EditorWindow? editorWindow)
    {
        DoSaveChangesDialog(runtimeData);
        
        var aTypes = new List<string> () { ".json" };
        var open = new OpenDialog ("Open project file", "Open a project file", aTypes) { AllowsMultipleSelection = false };
        open.DirectoryPath = runtimeData.UserPreferences.GetLastUsedPath();
        var openPath = Path.Combine (runtimeData.UserPreferences.GetLastUsedPath(), "vkqualityproject.json");
        open.FilePath = openPath;

        Application.Run (open);

        if (!open.Canceled) {
            foreach (var path in open.FilePaths) {
                if (string.IsNullOrEmpty (path) || !File.Exists (path)) {
                    continue;
                }

                var pathDirectory = Path.GetDirectoryName(path);
                if (pathDirectory != null) runtimeData.UserPreferences.SetLastUsedPath(pathDirectory);

                var jsonString = File.ReadAllText(path);
                try
                {
                    runtimeData.MainProject = JsonSerializer.Deserialize<DeviceListProject>(jsonString);
                    runtimeData.ProjectPath = path;
                    PlayDevicesCsvImporter.ImportDeviceCsvFile(runtimeData);
                }
                catch (JsonException)
                {
                    MessageBox.ErrorQuery ("Invalid JSON project", "JSON was not a valid project", "Ok");
                    return;
                }

                if (runtimeData.MainProject == null) return;
                if (runtimeData.MainProject.DeviceAllowList != null)
                {
                    runtimeData.DeviceAllowList = new List<DeviceAllowListRecord>(runtimeData.MainProject.DeviceAllowList);
                }

                if (runtimeData.MainProject.DriverAllowList != null)
                {
                    runtimeData.DriverAllowList = new List<DriverFingerprintRecord>(runtimeData.MainProject.DriverAllowList);
                }
                if (runtimeData.MainProject.DriverDenyList != null)
                {
                    runtimeData.DriverDenyList = new List<DriverFingerprintRecord>(runtimeData.MainProject.DriverDenyList);
                }
                if (runtimeData.MainProject.GpuPredictAllowList != null)
                {
                    runtimeData.GpuPredictAllowList = new List<GpuPredictRecord>(runtimeData.MainProject.GpuPredictAllowList);
                }
                if (runtimeData.MainProject.GpuPredictDenyList != null)
                {
                    runtimeData.GpuPredictDenyList = new List<GpuPredictRecord>(runtimeData.MainProject.GpuPredictDenyList);
                }
                editorWindow?.UpdatePaths(runtimeData);
            }
        }
    }

    public static void SaveProject(RuntimeData runtimeData)
    {
        if (runtimeData.MainProject != null)
        {
            runtimeData.MainProject.DeviceAllowList = runtimeData.DeviceAllowList.ToArray();
            runtimeData.MainProject.DriverAllowList = runtimeData.DriverAllowList.ToArray();
            runtimeData.MainProject.DriverDenyList = runtimeData.DriverDenyList.ToArray();
            runtimeData.MainProject.GpuPredictAllowList = runtimeData.GpuPredictAllowList.ToArray();
            runtimeData.MainProject.GpuPredictDenyList = runtimeData.GpuPredictDenyList.ToArray();
            var jsonString = JsonSerializer.Serialize(runtimeData.MainProject);
            File.WriteAllText(runtimeData.ProjectPath, jsonString);
        }
        runtimeData.ProjectNeedsSave = false;
    }

    private static string? SelectCsvFile(RuntimeData runtimeData, EditorWindow? editorWindow, string message)
    {
        var aTypes = new List<string>() {".csv"};
        var open = new OpenDialog("Open CSV file", message, aTypes) {AllowsMultipleSelection = false};
        open.DirectoryPath = runtimeData.UserPreferences.GetLastUsedPath();

        Application.Run(open);

        if (!open.Canceled)
        {
            foreach (var path in open.FilePaths)
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    continue;
                }

                if (File.Exists(path)) return path;
            }
        }

        return null;
    }
    private static string? SelectVkqFile(RuntimeData runtimeData, EditorWindow? editorWindow, string message)
    {
        var aTypes = new List<string>() { ".vkq" };
        var open = new OpenDialog("Open VkQuality file", message, aTypes) { AllowsMultipleSelection = false };
        open.DirectoryPath = runtimeData.UserPreferences.GetLastUsedPath();

        Application.Run(open);

        if (!open.Canceled) {
            foreach (var path in open.FilePaths) {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
                    continue;
                }

                if (File.Exists(path)) return path;
            }
        }

        return null;
    }

    public static void ImportDeviceListCsv(RuntimeData runtimeData, EditorWindow? editorWindow)
    {
        var csvPath = SelectCsvFile(runtimeData, editorWindow, "Open a device list CSV file");
        if (string.IsNullOrEmpty(csvPath)) return;
        DeviceListCsvImporter.ImportDeviceListCsvFile(runtimeData, csvPath);
    }

    public static void ImportDriverAllowListCsv(RuntimeData runtimeData, EditorWindow? editorWindow)
    {
        var csvPath = SelectCsvFile(runtimeData, editorWindow, "Open a driver allow list CSV file");
        if (string.IsNullOrEmpty(csvPath)) return;
        DriverFingerprintCsvImporter.ImportDriverFingerprintCsvFile(runtimeData, csvPath, true);
    }

    public static void ImportDriverDenyListCsv(RuntimeData runtimeData, EditorWindow? editorWindow)
    {
        var csvPath = SelectCsvFile(runtimeData, editorWindow, "Open a driver deny list CSV file");
        if (string.IsNullOrEmpty(csvPath)) return;
        DriverFingerprintCsvImporter.ImportDriverFingerprintCsvFile(runtimeData, csvPath, false);
    }
    
    public static void ImportGpuAllowListCsv(RuntimeData runtimeData, EditorWindow? editorWindow)
    {
        var csvPath = SelectCsvFile(runtimeData, editorWindow, "Open a GPU allow list CSV file");
        if (string.IsNullOrEmpty(csvPath)) return;
        GpuListCsvImporter.ImportGpuListCsvFile(runtimeData, csvPath, true);
    }

    public static void ImportGpuDenyListCsv(RuntimeData runtimeData, EditorWindow? editorWindow)
    {
        var csvPath = SelectCsvFile(runtimeData, editorWindow, "Open a GPU deny list CSV file");
        if (string.IsNullOrEmpty(csvPath)) return;
        GpuListCsvImporter.ImportGpuListCsvFile(runtimeData, csvPath, false);
    }

    public static void ImportVkQuality(RuntimeData runtimeData, EditorWindow? editorWindow)
    {
        var dataPath = SelectVkqFile(runtimeData, editorWindow, "Open a vk quality data file");
        if (string.IsNullOrEmpty(dataPath)) return;
        VkQualityImporter.ImportVkQualityFile(runtimeData, dataPath);
    }
}
