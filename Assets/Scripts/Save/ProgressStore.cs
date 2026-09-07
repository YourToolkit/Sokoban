using System;
using System.Collections.Generic;
using System.IO;
using Sokoban.Core;
using UnityEngine;

namespace Sokoban.Runtime
{
    [Serializable]
    public sealed class ProgressRecord
    {
        public string LevelId;
        public int LayoutVersion;
        public string GameplaySignature = "";
        public bool Completed;
        public int BestSteps;
        public int BestPushes;
        public ProgressRecord Clone() => (ProgressRecord)MemberwiseClone();
    }

    /// <summary>Small local progress store. Catalog ordering never identifies a room.</summary>
    public sealed class ProgressStore
    {
        [Serializable]
        private sealed class SaveData
        {
            public int Version = 3;
            public List<ProgressRecord> Records = new List<ProgressRecord>();
        }
        private readonly string path;
        private SaveData data;
#if UNITY_EDITOR
        // Explicit editor acceptance runs can isolate progress before GameController.Awake.
        public static string DefaultDirectoryOverride { get; set; }
#endif
        public string LastError { get; private set; } = "";
        public ProgressStore(string directory = null)
        {
#if UNITY_EDITOR
            directory = directory ?? DefaultDirectoryOverride;
#endif
            path = Path.Combine(directory ?? Application.persistentDataPath, "progress.json");
            Reload();
        }
        public void Reload()
        {
            LastError = "";
            data = new SaveData();
            if (!File.Exists(path)) return;
            try
            {
                var loaded = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
                if (loaded == null || (loaded.Version != 1 && loaded.Version != 2 && loaded.Version != 3) || loaded.Records == null)
                    throw new FormatException("Unsupported progress format.");
                var ids = new HashSet<string>();
                foreach (var record in loaded.Records)
                {
                    if (record == null || string.IsNullOrWhiteSpace(record.LevelId) || !ids.Add(record.LevelId)
                        || record.LayoutVersion < 0 || record.BestSteps < 0 || record.BestPushes < 0 || record.BestPushes > record.BestSteps)
                        throw new FormatException("Invalid room record.");
                }
                // Version 1 did not store layout versions; missing fields deserialize to zero.
                loaded.Version = 3;
                data = loaded;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is FormatException)
            {
                LastError = "无法读取成绩，仍可继续游戏。原文件已保留供恢复。";
                Debug.LogWarning("读取推箱子成绩失败：" + ex);
                // Keep the original bytes for recovery before a later successful save replaces the file.
                try { File.Copy(path, path + ".unreadable", true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        public ProgressRecord Get(string levelId, int layoutVersion = 0) =>
            data.Records.Find(r => r.LevelId == levelId && r.LayoutVersion == layoutVersion)?.Clone();

        /// <summary>Scores are valid only for the effective rules frozen into this level session.</summary>
        public ProgressRecord Get(LevelDefinition level, ElementRegistry registry = null)
        {
            if (level == null) return null;
            registry = registry ?? ElementRegistry.BuiltIns();
            if (ElementValidation.Validate(level, registry).Count > 0) return null;
            var record = data.Records.Find(r => r.LevelId == level.Id && r.LayoutVersion == level.LayoutVersion);
            if (record == null) return null;
            string signature = GameplayFingerprint.Compute(level, registry);
            if (string.IsNullOrEmpty(record.GameplaySignature))
            {
                // Old files may be claimed only by an unchanged basic-rule level.
                if (!GameplayFingerprint.IsLegacyCompatible(level, registry)) return null;
                record.GameplaySignature = signature;
                Save();
            }
            return record.GameplaySignature == signature ? record.Clone() : null;
        }

        public bool RecordWin(LevelDefinition level, int steps, int pushes, ElementRegistry registry = null)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            registry = registry ?? ElementRegistry.BuiltIns();
            if (ElementValidation.Validate(level, registry).Count > 0) throw new ArgumentException("不能为结构或机制配置无效的关卡记录成绩。", nameof(level));
            return RecordWinWithSignature(level.Id, steps, pushes, level.LayoutVersion, GameplayFingerprint.Compute(level, registry));
        }

        public bool RecordWin(string id, int steps, int pushes, int layoutVersion = 0)
            => RecordWinWithSignature(id, steps, pushes, layoutVersion, "");

        private bool RecordWinWithSignature(string id, int steps, int pushes, int layoutVersion, string signature)
        {
            if (string.IsNullOrWhiteSpace(id) || layoutVersion < 0 || steps < 0 || pushes < 0 || pushes > steps)
                throw new ArgumentException("Invalid completed-room score.");
            var record = data.Records.Find(r => r.LevelId == id);
            if (record == null)
            {
                record = new ProgressRecord { LevelId = id, LayoutVersion = layoutVersion, GameplaySignature = signature, Completed = true, BestSteps = steps, BestPushes = pushes };
                data.Records.Add(record);
            }
            else
            {
                if (record.LayoutVersion != layoutVersion || record.GameplaySignature != signature)
                {
                    record.LayoutVersion = layoutVersion;
                    record.GameplaySignature = signature;
                    record.BestSteps = steps;
                    record.BestPushes = pushes;
                }
                record.Completed = true;
                // Best pushes belongs to the same best-step solution; ties prefer fewer pushes.
                if (steps < record.BestSteps || (steps == record.BestSteps && pushes < record.BestPushes))
                { record.BestSteps = steps; record.BestPushes = pushes; }
            }
            return Save();
        }

        private bool Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(data, true));
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
                else File.Move(temporary, path);
                LastError = "";
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            { LastError = "成绩保存失败，本次通关结果仍保留在当前游戏中。"; Debug.LogWarning("保存推箱子成绩失败：" + ex); return false; }
        }
    }
}
