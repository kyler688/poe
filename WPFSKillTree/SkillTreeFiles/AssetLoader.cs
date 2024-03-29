﻿using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using PoESkillTree.Engine.Utils;
using PoESkillTree.Engine.Utils.Extensions;
using PoESkillTree.Utils;
using PoESkillTree.Utils.Extensions;
using PoESkillTree.ViewModels.PassiveTree;

namespace PoESkillTree.SkillTreeFiles
{
    /// <summary>
    /// Contains methods to download all assets required for the skill tree
    /// (skill tree file, opts file, asset images and sprite images) and methods
    /// to manage backups and temp folders for the files.
    /// </summary>
    public class AssetLoader
    {
        private const string SpriteUrl = "http://www.pathofexile.com/image/passive-skill/";

        public static string SkillTreeFile = "SkillTree.json";
        public static string OptsFile = "Opts.json";
        private const string AssetsFolder = "Assets/";

        private const string TempFolder = "Temp/";
        private const string BackupFolder = "Backup/";

        private readonly bool _useTempDir;
        private readonly string _path;

        private readonly string _skillTreePath;
        private readonly string _optsPath;
        private readonly string _assetsPath;

        private readonly string _tempSkillTreePath;
        private readonly string _tempOptsPath;
        private readonly string _tempAssetsPath;

        private readonly HttpClient _httpClient;

        /// <param name="httpClient">HttpClient instance used for downloading.</param>
        /// <param name="dataDirPath">The path to the "Data" folder were files are stored  (including "Data").</param>
        /// <param name="useTempDir">Whether to download the files to a temp folder instead of the provided one.
        /// </param>
        public AssetLoader(HttpClient httpClient, string dataDirPath, bool useTempDir)
        {
            _httpClient = httpClient;
            _path = dataDirPath.EnsureTrailingDirectorySeparator();
            _useTempDir = useTempDir;
            _skillTreePath = _path + SkillTreeFile;
            _optsPath = _path + OptsFile;
            _assetsPath = _path + AssetsFolder;
            var tempPath = _useTempDir ? _path + TempFolder : _path;
            _tempSkillTreePath = tempPath + SkillTreeFile;
            _tempOptsPath = tempPath + OptsFile;
            _tempAssetsPath = tempPath + AssetsFolder;
            Directory.CreateDirectory(tempPath);
        }

        /// <summary>
        /// Downloads the skill tree Json file asynchronously. Overwrites an existing file.
        /// </summary>
        /// <returns>The contents of the skill tree file.</returns>
        public async Task<string> DownloadSkillTreeToFileAsync()
        {
            var code = await _httpClient.GetStringAsync(Constants.TreeAddress);
            var start = "var passiveSkillTreeData = ";
            var regex = new Regex($"{start}{{(?>[^{{}}]|(?<open>){{|(?<-open>)}})*}}(?(o)(?!))");
            var skillTreeObj = regex.Match(code).Value.Replace("\\/", "/");
            skillTreeObj = skillTreeObj.Substring(start.Length, skillTreeObj.Length - start.Length);
            await FileUtils.WriteAllTextAsync(_tempSkillTreePath, skillTreeObj);
            return skillTreeObj;
        }

        /// <summary>
        /// Downloads the node sprite images mentioned in the provided tree asynchronously.
        /// Existing files are not overriden.
        /// </summary>
        /// <param name="inTree"></param>
        /// <param name="reportProgress">If specified, it is called to set this method's progress as a value
        /// from 0 to 1.</param>
        /// <returns></returns>
        internal async Task DownloadSkillNodeSpritesAsync(PassiveTreeViewModel inTree,
            Action<double>? reportProgress = null)
        {
            Directory.CreateDirectory(_tempAssetsPath);
            var perSpriteProgress = 1.0 / inTree.SkillSprites.Count;
            var progress = 0.0;
            foreach (var obj in inTree.SkillSprites)
            {
                var sprite = obj.Value[inTree.MaxImageZoomLevelIndex];
                var filename = sprite.FileName.Replace("https://web.poecdn.com/image/passive-skill/", string.Empty);
                var path = _tempAssetsPath + filename;
                var url = SpriteUrl + filename;
                if (path.Contains('?'))
                    path = path.Remove(path.IndexOf('?'));
                await DownloadAsync(url, path);
                progress += perSpriteProgress;
                reportProgress?.Invoke(progress);
            }
        }

        /// <summary>
        /// Downloads the asset images mentioned in the provided tree asynchronously.
        /// Existing files are not overriden.
        /// </summary>
        /// <param name="inTree"></param>
        /// <param name="reportProgress">If specified, it is called to set this method's progress as a value
        /// from 0 to 1.</param>
        /// <returns></returns>
        internal async Task DownloadAssetsAsync(PassiveTreeViewModel inTree, Action<double>? reportProgress = null)
        {
            Directory.CreateDirectory(_tempAssetsPath);
            var zoomLevel = inTree.ImageZoomLevels[inTree.MaxImageZoomLevelIndex].ToString(CultureInfo.InvariantCulture);
            var perAssetProgress = 1.0 / inTree.Assets.Count;
            var progress = 0.0;
            foreach (var asset in inTree.Assets)
            {
                var path = _tempAssetsPath + asset.Key + ".png";
                var url = asset.Value.GetValueOrDefault(zoomLevel, () => asset.Value.Values.First());
                await DownloadAsync(url, path);
                progress += perAssetProgress;
                reportProgress?.Invoke(progress);
            }
        }

        private async Task DownloadAsync(string url, string path)
        {
            if (File.Exists(path))
                return;
            using (var writer = File.Create(path))
            using (var response = await _httpClient.GetAsync(url))
            {
                await response.Content.CopyToAsync(writer);
            }
        }

        /// <summary>
        /// Downloads all files asynchronously.
        /// </summary>
        public async Task DownloadAllAsync()
        {
            var skillTreeTask = DownloadSkillTreeToFileAsync();

            var treeString = await skillTreeTask;
            var inTree = new PassiveTreeViewModel(treeString);
            var spritesTask = DownloadSkillNodeSpritesAsync(inTree);
            var assetsTask = DownloadAssetsAsync(inTree);

            await Task.WhenAll(spritesTask, assetsTask);
        }

        /// <summary>
        /// Moves the existing files to a backup folder.
        /// </summary>
        public void MoveToBackup()
        {
            var backupPath = _path + BackupFolder;
            DirectoryEx.DeleteIfExists(backupPath, true);
            Directory.CreateDirectory(backupPath);
            DirectoryEx.MoveIfExists(_assetsPath, backupPath + AssetsFolder, true);
            FileUtils.MoveIfExists(_skillTreePath, backupPath + SkillTreeFile, true);
            FileUtils.MoveIfExists(_optsPath, backupPath + OptsFile, true);
        }

        /// <summary>
        /// Restores the backup folder created by <see cref="MoveToBackup"/>.
        /// Existing files in the data folder are overwritten by backup files.
        /// </summary>
        public void RestoreBackup()
        {
            var backupPath = _path + BackupFolder;
            DirectoryEx.MoveIfExists(backupPath + AssetsFolder, _assetsPath, true);
            FileUtils.MoveIfExists(backupPath + SkillTreeFile, _skillTreePath, true);
            FileUtils.MoveIfExists(backupPath + OptsFile, _optsPath, true);
            DirectoryEx.DeleteIfExists(backupPath);
        }

        /// <summary>
        /// Deletes the backup folder and its contents.
        /// </summary>
        public void DeleteBackup()
        {
            DirectoryEx.DeleteIfExists(_path + BackupFolder, true);
        }

        /// <summary>
        /// Deletes the temp folder and its contents.
        /// This instance must have been set to use temporary files.
        /// </summary>
        public void DeleteTemp()
        {
            if (!_useTempDir)
                throw new InvalidOperationException("This instance doesn't use temp directories");
            DirectoryEx.DeleteIfExists(_path + TempFolder, true);
        }

        /// <summary>
        /// Moves the files from the temp folder to the data folder.
        /// This instance must have been set to use temporary files.
        /// </summary>
        public void MoveTemp()
        {
            if (!_useTempDir)
                throw new InvalidOperationException("This instance doesn't use temp directories");
            DirectoryEx.MoveIfExists(_tempAssetsPath, _assetsPath, true);
            FileUtils.MoveIfExists(_tempSkillTreePath, _skillTreePath, true);
            FileUtils.MoveIfExists(_tempOptsPath, _optsPath, true);
            DirectoryEx.DeleteIfExists(_path + TempFolder);
        }
    }
}