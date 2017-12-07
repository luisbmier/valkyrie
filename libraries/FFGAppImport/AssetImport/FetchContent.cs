﻿using System.Collections.Generic;
using Unity_Studio;
using System;
using System.Linq;
using System.IO;
using ValkyrieTools;

namespace FFGAppImport
{

    // Used to import content from FFG apps
    public class FetchContent
    {
        FFGImport importData;
        string gameType;
        string contentPath;
        AppFinder finder = null;
        public bool importAvailable;
        string logFile;

        // Set up and check availability
        //public FetchContent(GameType type, string path)
        public FetchContent(FFGImport import)
        {
            importData = import;
            contentPath = import.path;
            if (import.type == GameType.D2E)
            {
                finder = new RtLFinder(import.platform);
                gameType = "D2E";
            }
            else if (import.type == GameType.MoM)
            {
                finder = new MoMFinder(import.platform);
                gameType = "MoM";
            }
            else
            {
                return;
            }

            string appVersion = finder.RequiredFFGVersion();
            // Open up the assets to get the actual version number
            string ffgVersion = fetchAppVersion();

            // Add version found to log
            if (ffgVersion.Length != 0)
            {
                ValkyrieDebug.Log("FFG " + gameType + " Version Found: " + ffgVersion + System.Environment.NewLine);
            }
            else
            {
                ValkyrieDebug.Log("FFG " + gameType + " not found." + System.Environment.NewLine);
            }

            // Check if version is acceptable for import
            importAvailable = VersionNewerOrEqual(appVersion, ffgVersion);

            if (importData.platform == Platform.Android)
            {
                importAvailable = File.Exists(finder.ObbPath());
            }
        }

        // Check if an import is required
        public bool NeedImport()
        {
            // Read the import log
            logFile = Path.Combine(contentPath, "import.ini");
            IniData log = IniRead.ReadFromIni(logFile);

            // If no import log, import is required
            if (log == null)
            {
                return true;
            }

            bool appVersionOK = false;
            bool valkVersionOK = false;

            // Check that the FFG app version in import is new enough
            string lastImport = log.Get("Import", "FFG");
            appVersionOK = VersionNewerOrEqual(finder.RequiredFFGVersion(), lastImport);

            // Check that the Valkyrie version in import is new enough
            int lastValkVersion = 0;
            int.TryParse(log.Get("Import", "Valkyrie"), out lastValkVersion);
            valkVersionOK = (FFGImport.version == lastValkVersion);
            return !appVersionOK || !valkVersionOK;
        }

        // Determine FFG app version
        public string fetchAppVersion()
        {
            string appVersion = "";
            try
            {
                if (importData.platform == Platform.Android)
                {
                    return finder.ExtractObbVersion();
                }
                string resourcesAssets = finder.location + "/resources.assets";
                if (!File.Exists(resourcesAssets))
                {
                    ValkyrieDebug.Log("Could not find main assets file: " + resourcesAssets);
                    return appVersion;
                }

                // We assume that the version asset is in resources.assets
                var assetsFile = new AssetsFile(resourcesAssets, new EndianStream(File.OpenRead(resourcesAssets), EndianType.BigEndian));

                // Look through all listed assets
                foreach (var asset in assetsFile.preloadTable.Values)
                {
                    // Check all text assets
                    if (asset.Type2 == 49) //TextAsset
                    {
                        // Load as text
                        var textAsset = new Unity_Studio.TextAsset(asset, false);
                        // Check if called _version
                        if (asset.Text.Equals("_version"))
                        {
                            // Read asset content
                            textAsset = new Unity_Studio.TextAsset(asset, true);
                            // Extract version
                            appVersion = System.Text.Encoding.UTF8.GetString(textAsset.m_Script);
                        }
                    }
                }

                // Trim content from #
                if (appVersion.IndexOf("#") != -1)
                {
                    appVersion = appVersion.Substring(0, appVersion.IndexOf("#"));
                }
            }
            catch(Exception ex)
            {
                ValkyrieDebug.Log("fetchAppVersion caused " + ex.GetType().Name + ": " + ex.Message + " " + ex.StackTrace);
            }

            return appVersion;
        }

        // Import from app
        public void Import()
        {
            try
            {
                finder.ExtractObb(); // Utilized by Android

                // Attempt to clean up old import
                if (!CleanImport()) return;
                
                // List all assets files
                var files = Directory.GetFiles(finder.location);

                // get all asset files
                var assetFiles = new List<string>();
                assetFiles.AddRange(files.Where(x => x.EndsWith(".assets"))); // load '*.assets'
                // TODO: Sort level, so that level2, level3 and level20 are in the right order
                assetFiles.AddRange(files.Where(x => !x.EndsWith(".assets") && !x.StartsWith("level"))); // random named files like 'd18b80b6f1c734d1eb70d521a2dbeda9'
                assetFiles.AddRange(files.Where(x => !x.EndsWith(".assets") && x.StartsWith("level")));  // load 'level*' files last.

                // Import from all assets 
                //var assetFiles = Directory.GetFiles(finder.location, filter).ToList();

                // Get all asset content
                BuildAssetStructures(assetFiles);
                // Write log
                WriteImportLog(logFile);

                // Find any streaming asset files
                string streamDir = Path.Combine(finder.location, "StreamingAssets");
                if (Directory.Exists(streamDir))
                {
                    string[] streamFiles = Directory.GetFiles(streamDir, "*", SearchOption.AllDirectories);
                    ImportStreamAssets(streamFiles);
                }
                else
                {
                    ValkyrieDebug.Log("StreamingAssets dir '" + streamDir + "' not found");
                }
            }
            catch (Exception ex)
            {
                ValkyrieDebug.Log("Import caused " + ex.GetType().Name + ": " + ex.Message + " " + ex.StackTrace);
            }
        }

        // Write log of import
        private void WriteImportLog(string logFile)
        {
            string[] log = new string[3];
            log[0] = "[Import]";
            log[1] = "Valkyrie=" + FFGImport.version;
            log[2] = "FFG=" + fetchAppVersion();
            // Write out data
            try
            {
                Directory.CreateDirectory(contentPath);

                logFile = Path.Combine(contentPath, "import.ini");

                if (File.Exists(logFile))
                {
                    File.Delete(logFile);
                }
                File.WriteAllLines(logFile, log);
            }
            catch
            {
                ValkyrieDebug.Log("Warning: Unable to create import log");
            }
        }

        //Clean old fetched data
        private bool CleanImport()
        {
            if (!Directory.Exists(contentPath)) return true;
            try
            {
                Directory.Delete(contentPath, true);
            }
            catch
            {
                ValkyrieDebug.Log("Warning: Unable to remove temporary files.");
                return false;
            }
            return !Directory.Exists(contentPath);
        }

        // Import asset contents
        private void BuildAssetStructures(IEnumerable<string> unityFiles)
        {
            if (unityFiles == null) throw new ArgumentNullException("unityFiles");
            // All asset files connected to the one openned
            unityFiles.ToList().ForEach(file => ImportAssetFile(file));
        }

        private void ImportAssetFile(string file)
        {
            if (file == null) throw new ArgumentNullException("file");

            using (FileStream fs = File.OpenRead(file))
            {
                using (var endianStream = new EndianStream(fs, EndianType.BigEndian))
                {
                    var assetFile = new AssetsFile(file, endianStream);

                    // All assets
                    assetFile.preloadTable.Values.ToList().ForEach(assetPreloadData => ImportAssetPreloadData(assetPreloadData));
                }
            }
        }

        private void ImportAssetPreloadData(AssetPreloadData assetPreloadData)
        {
            if (assetPreloadData == null) throw new ArgumentNullException("assetPreloadData");

            switch (assetPreloadData.Type2)
            {
                //case 1: //GameObject
                //case 4: //Transform
                //case 224: //RectTransform
                //case 21: //Material
                case 28: //Texture2D
                    {
                        ExportTexture(assetPreloadData);
                        break;
                    }
                //case 48: //Shader
                case 49: //TextAsset
                    {
                        ExportText(assetPreloadData);
                        break;
                    }
                case 83: //AudioClip
                    {
                        ExportAudioClip(assetPreloadData);
                        break;
                    }
                //case 89: //CubeMap
                case 128: //Font
                    {
                        ExportFont(assetPreloadData);
                        break;
                    }
                //case 129: //PlayerSettings
                case 0:
                    break;
            }
        }

        // Save texture to disk
        private void ExportTexture(AssetPreloadData asset)
        {
            if (asset == null)
                throw new ArgumentNullException("asset");
            Texture2D texture2D = new Texture2D(asset, false);
            texture2D = new Texture2D(asset, true);
            Directory.CreateDirectory(contentPath);
            Directory.CreateDirectory(contentPath + "/img");
            // Default file name
            string fileCandidate = contentPath + "/img/" + asset.Text;
            // This should apends a postfix to the name to avoid collisions
            string fileName = GetAvailableFileName(fileCandidate, asset.extension);
            ValkyrieDebug.Log("ExportTexture: '" + fileName + "' format: '" + texture2D.m_TextureFormat + "'");
            
            switch (texture2D.m_TextureFormat)
            {
                #region DDS
                case 1: //Alpha8
                case 2: //A4R4G4B4
                case 3: //B8G8R8 //confirmed on X360, iOS //PS3 unsure
                case 4: //G8R8A8B8 //confirmed on X360, iOS
                case 5: //B8G8R8A8 //confirmed on X360, PS3, Web, iOS
                case 7: //R5G6B5 //confirmed switched on X360; confirmed on iOS
                case 10: //DXT1
                case 12: //DXT5
                case 13: //R4G4B4A4, iOS (only?)
                    using (var fs = File.Open(fileName, FileMode.Create))
                    {
                        using (var writer = new BinaryWriter(fs))
                        {
                            // We have to manually add a header because unity doesn't have it
                            writer.Write(0x20534444);
                            writer.Write(0x7C);
                            writer.Write(texture2D.dwFlags);
                            writer.Write(texture2D.m_Height);
                            writer.Write(texture2D.m_Width);
                            writer.Write(texture2D.dwPitchOrLinearSize); //should be main tex size without mips);
                            writer.Write((int)0); //dwDepth not implemented
                            writer.Write(texture2D.dwMipMapCount);
                            writer.Write(new byte[44]); //dwReserved1[11]
                            writer.Write(texture2D.dwSize);
                            writer.Write(texture2D.dwFlags2);
                            writer.Write(texture2D.dwFourCC);
                            writer.Write(texture2D.dwRGBBitCount);
                            writer.Write(texture2D.dwRBitMask);
                            writer.Write(texture2D.dwGBitMask);
                            writer.Write(texture2D.dwBBitMask);
                            writer.Write(texture2D.dwABitMask);
                            writer.Write(texture2D.dwCaps);
                            writer.Write(texture2D.dwCaps2);
                            writer.Write(new byte[12]); //dwCaps3&4 & dwReserved2

                            // Write image data
                            writer.Write(texture2D.image_data);
                        }
                    }
                    break;
                #endregion
                #region PVR
                case 30: //PVRTC_RGB2
                case 31: //PVRTC_RGBA2
                case 32: //PVRTC_RGB4
                case 33: //PVRTC_RGBA4
                case 34: //ETC_RGB4
                case 47: //ETC2_RGBA8
                    // We put the image data in a PVR container. See the specification for details
                    // http://cdn.imgtec.com/sdk-documentation/PVR+File+Format.Specification.pdf
                    using (var fs = File.Open(fileName, FileMode.Create))
                    {
                        using (var writer = new BinaryWriter(fs))
                        {
                            // We have to manually add a header because unity doesn't have it
                            writer.Write(texture2D.pvrVersion);
                            writer.Write(texture2D.pvrFlags);
                            writer.Write(texture2D.pvrPixelFormat);
                            writer.Write(texture2D.pvrColourSpace);
                            writer.Write(texture2D.pvrChannelType);
                            writer.Write(texture2D.m_Height);
                            writer.Write(texture2D.m_Width);
                            writer.Write(texture2D.pvrDepth);
                            writer.Write(texture2D.pvrNumSurfaces);
                            writer.Write(texture2D.pvrNumFaces);
                            writer.Write(texture2D.dwMipMapCount);
                            writer.Write(texture2D.pvrMetaDataSize);

                            // Write image data
                            writer.Write(texture2D.image_data);
                        }
                    }
                    break;
                #endregion
                case 28: //DXT1 Crunched
                case 29: //DXT1 Crunched
                default:
                    File.WriteAllBytes(fileName, texture2D.image_data);
                    break;
            }
        }

        // Save audio to file
        private void ExportAudioClip(Unity_Studio.AssetPreloadData asset)
        {
            var audioClip = new Unity_Studio.AudioClip(asset, false);
            Directory.CreateDirectory(contentPath);
            Directory.CreateDirectory(contentPath + "/audio");

            string fileCandidate = contentPath + "/audio/" + asset.Text;

            // This should apends a postfix to the name to avoid collisions
            string fileName = GetAvailableFileName(fileCandidate, ".ogg");

            // Pass to FSB Export
            audioClip = new Unity_Studio.AudioClip(asset, true);
            FSBExport.Write(audioClip.m_AudioData, fileName);
        }

        // Save text to file
        private void ExportText(Unity_Studio.AssetPreloadData asset)
        {
            var textAsset = new Unity_Studio.TextAsset(asset, false);
            Directory.CreateDirectory(contentPath);
            Directory.CreateDirectory(contentPath + "/text");
            string fileCandidate = contentPath + "/text/" + asset.Text;
            
            textAsset = new Unity_Studio.TextAsset(asset, true);

            // This should apends a postfix to the name to avoid collisions
            string fileName = GetAvailableFileName(fileCandidate, asset.extension);

            // Write to disk, pass the Deobfuscate key to decrypt
            File.WriteAllBytes(fileName, textAsset.Deobfuscate(finder.ObfuscateKey()));

            // Need to trim new lines from old D2E format
            if (asset.Text.Equals("Localization"))
            {
                string[] locFix = File.ReadAllLines(fileName);
                var locOut = new List<string>();
                string currentLine = "";
                for (int i = 0; i < locFix.Length; i++)
                {
                    if (locFix[i].Split('\"').Length % 2 == 0)
                    {
                        if (currentLine.Length == 0)
                        {
                            currentLine = locFix[i];
                        }
                        else
                        {
                            locOut.Add(currentLine + "\\n" + locFix[i]);
                            currentLine = "";
                        }
                    }
                    else
                    {
                        if (currentLine.Length == 0)
                        {
                            locOut.Add(locFix[i]);
                        }
                        else
                        {
                            currentLine += "\\n" + locFix[i];
                        }
                    }
                }
                File.WriteAllLines(fileName, locOut.ToArray());
            }
        }

        // Save TTF font to dist
        private void ExportFont(Unity_Studio.AssetPreloadData asset)
        {
            var font = new Unity_Studio.unityFont(asset, false);
            Directory.CreateDirectory(contentPath);
            Directory.CreateDirectory(contentPath + "/fonts");
            string fileCandidate = contentPath + "/fonts/" + asset.Text;

            font = new Unity_Studio.unityFont(asset, true);
            
            if (font.m_FontData == null) return;

            // This should apends a postfix to the name to avoid collisions
            string fileName = GetAvailableFileName(fileCandidate, ".ttf");

            // Write to disk
            File.WriteAllBytes(fileName, font.m_FontData);
        }

        // Test version of the form a.b.c is newer or equal
        public static bool VersionNewerOrEqual(string oldVersion, string newVersion)
        {
            string oldS = System.Text.RegularExpressions.Regex.Replace(oldVersion, "[^0-9]", "");
            string newS = System.Text.RegularExpressions.Regex.Replace(newVersion, "[^0-9]", "");
            // If numbers are the same they are equal
            if (oldS.Equals(newS)) return true;
            return VersionNewer(oldVersion, newVersion);
        }

        // Test version of the form a.b.c is newer
        public static bool VersionNewer(string oldVersion, string newVersion)
        {
            if (oldVersion == null)
                throw new ArgumentNullException("oldVersion");
            if (newVersion == null)
                throw new ArgumentNullException("newVersion");
            
            if (newVersion.Equals("")) return false;

            if (oldVersion.Equals("")) return true;

            // Split into components
            string[] oldV = oldVersion.Split('.');
            string[] newV = newVersion.Split('.');

            // Different number of components
            if (oldV.Length != newV.Length) return true;
            
            // Check each component
            for (int i = 0; i < oldV.Length; i++)
            {
                // Strip for only numbers
                string oldS = System.Text.RegularExpressions.Regex.Replace(oldV[i], "[^0-9]", "");
                string newS = System.Text.RegularExpressions.Regex.Replace(newV[i], "[^0-9]", "");
                try
                {
                    if (int.Parse(oldS) < int.Parse(newS)) return true;
                    if (int.Parse(oldS) > int.Parse(newS)) return false;
                }
                catch
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Find asset bundles and create a list of them in a file.  Invalid files ignored.
        /// </summary>
        /// <param name="streamFiles"></param>
        protected void ImportStreamAssets(string[] streamFiles)
        {
            if (streamFiles == null)
                throw new ArgumentNullException("streamFiles");
            var bundles = new List<string>();

            foreach (string file in streamFiles)
            {
                try
                {
                    string header = null;
                    using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read))
                    {
                        byte[] buffer = new byte[8];
                        fs.Read(buffer, 0, buffer.Length);
                        header = System.Text.Encoding.Default.GetString(buffer);
                    }
                    if (header.StartsWith("UnityFS"))
                    {
                        bundles.Add(file);
                    }
                }
                catch (Exception ex)
                {
                    ValkyrieDebug.Log("ImportStreamAssets on file '" + file + "' caused " + ex.GetType().Name + ": " + ex.Message + " " + ex.StackTrace);
                    continue;
                }
            }

            // We can't extract these here because this isn't the main thread and unity doesn't handle that
            string bundlesFile = Path.Combine(contentPath, "bundles.txt");
            ValkyrieDebug.Log("Writing '" + bundlesFile + "' containing " + bundles.Count + "' items");
            File.WriteAllLines(bundlesFile, bundles.ToArray());
        }

        /// <summary>
        /// Checks if the file is already present on the file system and returns a file path that is available.
        /// For example, if c:\temp\myfile.dds already exists, it will return the available file c:\temp\myfile_000002.dds and increment to c:\temp\myfile_000003.dds if that file would exist.
        /// </summary>
        /// <param name="fileCandidate">path prefix without extension</param>
        /// <param name="extension">extension of the resulting file including the dot like '.ogg' for audio files</param>
        /// <returns>File path, where no file exists.</returns>
        private static string GetAvailableFileName(string fileCandidate, string extension)
        {
            if (fileCandidate == null) throw new ArgumentNullException("fileCandidate");
            if (extension == null) throw new ArgumentNullException("extension");
            string fileName = fileCandidate + extension;
            int count = 1;
            while (File.Exists(fileName))
            {
                count++;
                fileName = fileCandidate + "_" + count.ToString().PadLeft(6, '0') + extension;
            }
            return fileName;
        }
    }
}