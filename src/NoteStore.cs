using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;

namespace WinStickies
{
    public sealed class NoteStore
    {
        public readonly string FilePath;
        public string LoadWarning { get; private set; }
        public bool IsNew { get; private set; }
        public string MigrationBackupPath { get; private set; }
        public NoteStore(string folder) { FilePath = Path.Combine(folder, "notes.json"); }

        public Notebook Load()
        {
            CleanupRuntimeFiles();
            IsNew = !File.Exists(FilePath) && !File.Exists(FilePath + ".bak") && !File.Exists(FilePath + ".tmp");
            if (IsNew) return new Notebook();
            Exception original;
            try { return Read(FilePath); }
            catch (Exception ex) { if (!IsDataError(ex)) throw; original = ex; }
            // Preserve damaged input before any future autosave can replace it.
            if (File.Exists(FilePath))
            {
                string snapshot = FilePath + ".damaged-" + FileHash(FilePath);
                if (!File.Exists(snapshot)) File.Copy(FilePath, snapshot);
                CleanupRuntimeFiles();
            }
            try
            {
                var restored = Read(FilePath + ".bak");
                LoadWarning = "已从上次备份恢复便签。原文件已保留。";
                return restored;
            }
            catch (Exception ex)
            {
                if (!IsDataError(ex)) throw;
                // A completed temporary save may be the only readable copy after
                // an interrupted first save. Validate it before recovering it.
                if (File.Exists(FilePath + ".tmp"))
                {
                    var pending = Read(FilePath + ".tmp");
                    if (File.Exists(FilePath)) File.Replace(FilePath + ".tmp", FilePath, FilePath + ".bak", true);
                    else File.Move(FilePath + ".tmp", FilePath);
                    LoadWarning = "已恢复上次未完成的保存，临时文件已清理。";
                    return pending;
                }
                // Do not silently overwrite both damaged copies with an empty notebook.
                throw new InvalidDataException("便签文件和备份均无法读取。原文件已保留在：" + FilePath, original);
            }
        }

        private static bool IsDataError(Exception ex)
        {
            return ex is IOException || ex is SerializationException || ex is System.Xml.XmlException || ex is FormatException;
        }

        private static Notebook Read(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                var book = new DataContractJsonSerializer(typeof(Notebook)).ReadObject(stream) as Notebook;
                if (book == null || book.Notes == null || book.Version != 1 || book.Notes.Any(n => n == null))
                    throw new SerializationException("不支持的便签文件格式。");
                foreach (var note in book.Notes) note.Normalize();
                if (book.Notes.Select(n => n.Id).Distinct().Count() != book.Notes.Count)
                    throw new SerializationException("便签 ID 重复。");
                return book;
            }
        }

        public void Save(Notebook book)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temporary = FilePath + ".tmp";
            bool opened = false, complete = false;
            try
            {
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    opened = true;
                    new DataContractJsonSerializer(typeof(Notebook)).WriteObject(stream, book);
                    stream.Flush(true); complete = true;
                }
                if (File.Exists(FilePath)) File.Replace(temporary, FilePath, FilePath + ".bak", true);
                else File.Move(temporary, FilePath);
            }
            finally { if (opened && !complete) TryDelete(temporary); }
        }

        private static string FileHash(string path)
        {
            using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        private static void TryDelete(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        private static bool IsReadableNotebook(string path)
        {
            try { Read(path); return true; }
            catch (Exception ex) { if (IsDataError(ex) || ex is UnauthorizedAccessException) return false; throw; }
        }
        internal void CleanupRuntimeFiles()
        {
            // Only known runtime files in this notebook's directory are touched.
            // Distinct recovery snapshots, notes and migration backups are user data.
            try
            {
                string folder = Path.GetDirectoryName(FilePath);
                if (!Directory.Exists(folder)) return;
                string temporary = FilePath + ".tmp";
                if (File.Exists(temporary))
                {
                    try
                    {
                        Read(temporary);
                        string hash = FileHash(temporary);
                        if ((File.Exists(FilePath) && FileHash(FilePath) == hash) ||
                            (File.Exists(FilePath + ".bak") && FileHash(FilePath + ".bak") == hash)) TryDelete(temporary);
                    }
                    catch (Exception ex)
                    {
                        if (ex is IOException || ex is UnauthorizedAccessException) { /* May be in use; retry later. */ }
                        else if (IsDataError(ex))
                        {
                            // Keep even partial bytes if they are the only surviving copy.
                            if (IsReadableNotebook(FilePath) || IsReadableNotebook(FilePath + ".bak")) TryDelete(temporary);
                        }
                        else throw;
                    }
                }
                var seen = new System.Collections.Generic.HashSet<string>();
                foreach (string file in Directory.GetFiles(folder, "notes.json.damaged-*").OrderByDescending(File.GetLastWriteTimeUtc))
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
                    if (!seen.Add(FileHash(file))) TryDelete(file);
                }
                string log = Path.Combine(folder, "error.log");
                if (File.Exists(log) && File.GetLastWriteTimeUtc(log) < DateTime.UtcNow.AddDays(-7)) TryDelete(log);
                else if (File.Exists(log) && new FileInfo(log).Length > 65536)
                {
                    byte[] tail = new byte[65536];
                    using (var stream = new FileStream(log, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        stream.Seek(-tail.Length, SeekOrigin.End); int read = 0;
                        while (read < tail.Length) { int count = stream.Read(tail, read, tail.Length - read); if (count == 0) break; read += count; }
                        stream.Position = 0; stream.Write(tail, 0, read); stream.SetLength(read);
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        internal int MigrateTrayStartup(Notebook book)
        {
            if (book.StartupRevision >= 1) return 0;
            var examples = book.Notes.Where(LegacySamples.Matches).ToArray();
            if (examples.Length > 0)
            {
                // This permanent snapshot is not rotated by ordinary autosaves.
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                string backup = Path.Combine(Path.GetDirectoryName(FilePath), "notes.before-tray-update-" +
                    DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".json");
                using (var stream = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    // Restoring this snapshot must not immediately remove its examples again.
                    var snapshot = new Notebook { Version = book.Version, StartupRevision = 1, Notes = book.Notes.ToList() };
                    new DataContractJsonSerializer(typeof(Notebook)).WriteObject(stream, snapshot);
                    stream.Flush(true);
                }
                MigrationBackupPath = backup;
                foreach (var note in examples) book.Notes.Remove(note);
            }
            book.StartupRevision = 1;
            Save(book);
            return examples.Length;
        }
    }
}
