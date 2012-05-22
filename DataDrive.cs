using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.IO;

namespace disParity
{

  public class DataDrive
  {

    private string root;
    private string metaFileName;
    private FileRecord[] oldFiles;  // list of files previously protected on this drive, as loaded from files.dat
    private List<FileRecord> files; // list of current files to be protected on this drive, as of the last Scan
    private UInt32 maxBlock;
    private MD5 hash;

    const UInt32 META_FILE_VERSION = 2;

    public DataDrive(string root, string metaFileName)
    {
      this.root = root;
      this.metaFileName = metaFileName;

      if (File.Exists(metaFileName))
        LoadFileData();

      hash = MD5.Create();
    }

    /// <summary>
    /// Clears all state of the DataDrive, resetting to empty (deletes on-disk
    /// meta data as well.)
    /// </summary>
    public void Clear()
    {
      oldFiles = null;
      files = null;
      maxBlock = 0;
      if (File.Exists(metaFileName))
        File.Delete(metaFileName);
    }

    /// <summary>
    /// Scans the drive from 'root' down, generating the current list of files on the drive.
    /// Files that should not be protected (e.g. Hidden files) are not included.
    /// </summary>
    public void Scan()
    {
      files = new List<FileRecord>();
      LogFile.Log("Scanning {0}...", root);
      Scan(new DirectoryInfo(root));
      long totalSize = 0;
      foreach (FileRecord f in files)
        totalSize += f.length;
      LogFile.Log("Found {0} file{1} ({2} total)", files.Count, 
        files.Count == 1 ? "" : "s", Utils.SmartSize(totalSize));
    }

    private void Scan(DirectoryInfo dir)
    {
      DirectoryInfo[] subDirs;
      try {
        subDirs = dir.GetDirectories();
      }
      catch (Exception e) {
        LogFile.Log("Warning: Could not enumerate subdirectories of {0}: {1}", dir.FullName, e.Message);
        return;
      }
      FileInfo[] fileInfos;
      try {
        fileInfos = dir.GetFiles();
      }
      catch (Exception e) {
        LogFile.Log("Warning: Could not enumerate files in {0}: {1}", dir.FullName, e.Message);
        return;
      }
      foreach (DirectoryInfo d in subDirs) {
        if (IgnoreHidden && (d.Attributes & FileAttributes.Hidden) != 0)
          continue;
        if ((d.Attributes & FileAttributes.System) != 0)
          continue;
        Scan(d);
      }
      string relativePath = Utils.StripRoot(root, dir.FullName);
      foreach (FileInfo f in fileInfos) {
        if (f.Attributes == (FileAttributes)(-1))
          continue;
        if (IgnoreHidden && (f.Attributes & FileAttributes.Hidden) != 0)
          continue;
        if ((f.Attributes & FileAttributes.System) != 0)
          continue;
        files.Add(new FileRecord(f, relativePath));
      }
    }

    private List<FileRecord> adds;
    private List<FileRecord> edits;
    private Dictionary<FileRecord, FileRecord> moves;
    private List<FileRecord> deletes;

    /// <summary>
    /// Compare the old list of files with the new list in order to
    /// determine which files had been added, removed, moved, or edited.
    public void Compare()
    {
      // build dictionaries of file names for fast lookup
      Dictionary<string, FileRecord> oldFileNames = new Dictionary<string, FileRecord>();
      foreach (FileRecord r in oldFiles)
        oldFileNames[r.name.ToLower()] = r;
      Dictionary<string, FileRecord> newFileNames = new Dictionary<string, FileRecord>();
      foreach (FileRecord r in files)
        oldFileNames[r.name.ToLower()] = r;

      // build list of new files we haven't seen before (adds)
      adds = new List<FileRecord>();
      foreach (FileRecord r in files)
        if (!oldFileNames.ContainsKey(r.name.ToLower()))
          adds.Add(r);

      // build list of old files we don't see now (deletes)
      deletes = new List<FileRecord>();
      foreach (FileRecord r in oldFiles)
        if (!newFileNames.ContainsKey(r.name.ToLower()))
          deletes.Add(r);
      
      // some of the files in add/delete list might actually be moves, check for that
      moves = new Dictionary<FileRecord, FileRecord>();
      foreach (FileRecord a in adds) {
        byte[] hashCode = null;
        foreach (FileRecord d in deletes)
          if (a.length == d.length && a.lastWriteTime == d.lastWriteTime) {
            // probably the same file, but we need to check the hash to be sure
            if (hashCode == null)
              hashCode = ComputeHash(a);
            if (Utils.HashCodesMatch(hashCode, d.hashCode)) {
              LogFile.Log("{0} moved to {1}", Utils.MakeFullPath(root, d.name),
                Utils.MakeFullPath(root, a.name));
              moves[d] = a;
            }
          }
      }
      // remove the moved files from the add and delete lists
      foreach (var kvp in moves) {
        deletes.Remove(kvp.Key);
        adds.Remove(kvp.Value);
      }

      // now check for edits
      foreach (FileRecord o in oldFiles) {
        FileRecord n;
        if (newFileNames.TryGetValue(o.name.ToLower(), out n)) {
          if (o.length != n.length)
            edits.Add(o); // trivial case, length changed
          else if (o.creationTime != n.creationTime || o.lastWriteTime != n.lastWriteTime) {
            // probable edit, compare hash codes to be sure
            if (!Utils.HashCodesMatch(o.hashCode, ComputeHash(n)))
              edits.Add(o);
          }

        }
      }

      LogFile.Log("Adds: {0} Deletes: {1} Moves: {2} Edits: {3}", adds.Count,
        deletes.Count, moves.Count, edits.Count);

    }

    public void ProcessMoves()
    {
      if (moves.Count == 0)
        return;
      foreach (var kvp in moves) {
        FileRecord r = kvp.Key; // entry in oldFiles list
        r.name = kvp.Value.name;
      }

    }

    private byte[] ComputeHash(FileRecord r)
    {
      using (FileStream s = new FileStream(Utils.MakeFullPath(root, r.name), FileMode.Open, FileAccess.Read))
        hash.ComputeHash(s);
      return hash.Hash;
    }

    /// <summary>
    /// Returns true if there was a files.dat file present for this drive
    /// </summary>
    public bool HasFileData()
    {
      return (oldFiles != null);
    }

    /// <summary>
    /// Specifies whether files and folders with the Hidden attribute set should
    /// be ignored
    /// </summary>
    public bool IgnoreHidden { get; set; }

    private UInt32 enumBlock;
    private FileStream enumFile;
    private List<FileRecord>.Enumerator enumerator;

    public void BeginFileEnum()
    {
      enumBlock = 0;
      enumFile = null;
      enumerator = files.GetEnumerator();
    }

    public bool GetNextBlock(byte[] buf)
    {
      if (enumFile == null) {
        if (!enumerator.MoveNext())
          return false;
        // TODO: Handle zero-length file here?
        string fullName = Utils.MakeFullPath(root, enumerator.Current.name);
        try {
          enumFile = new FileStream(fullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception e) {
          LogFile.Log("Error opening {0} for reading: {1}", fullName, e.Message);
          enumerator.Current.skipped = true;
          enumerator.MoveNext();
          return GetNextBlock(buf);
        }
        LogFile.Log("Reading {0}", fullName);
        hash.Initialize();
        enumerator.Current.startBlock = enumBlock;
      }
      int bytesRead = enumFile.Read(buf, 0, buf.Length);
      if (enumFile.Position < enumFile.Length)
        hash.TransformBlock(buf, 0, bytesRead, buf, 0);
      else {
        // reached end of this file
        hash.TransformFinalBlock(buf, 0, bytesRead);
        enumerator.Current.hashCode = hash.Hash;
        AppendFileRecord(enumerator.Current);
        enumFile.Close();
        enumFile.Dispose();
        enumFile = null;
        Array.Clear(buf, bytesRead, buf.Length - bytesRead);
      }
      return true;
    }    

    public void EndFileEnum()
    {
      enumerator.Dispose();
    }

    private void SaveFileList()
    {
      DateTime start = DateTime.Now;
      LogFile.VerboseLog("Saving file data for {0}...\r\n", root);
      string backup = "";
      if (File.Exists(metaFileName)) {
        backup = metaFileName + ".BAK";
        File.Move(metaFileName, backup);
      }
      FileStream f = new FileStream(metaFileName, FileMode.Create,
        FileAccess.Write);
      FileRecord.WriteUInt32(f, META_FILE_VERSION);
      foreach (FileRecord r in oldFiles)
        r.WriteToFile(f);
      f.Close();
      if (backup != "")
        File.Delete(backup);
      TimeSpan elapsed = DateTime.Now - start;
      LogFile.VerboseLog("{0} records saved in {1:F2} sec\r\n",
        oldFiles.Length, elapsed.TotalSeconds);
    }

    private void AppendFileRecord(FileRecord r)
    {
      if (!File.Exists(metaFileName))
        using (FileStream fNew = new FileStream(metaFileName, FileMode.Create, FileAccess.Write))
          FileRecord.WriteUInt32(fNew, META_FILE_VERSION);
      using (FileStream f = new FileStream(metaFileName, FileMode.Append, FileAccess.Write))
        r.WriteToFile(f); 
    }
    
    /// <summary>
    /// Loads the files.dat file containing the record of any existing protected file data
    /// </summary>
    private void LoadFileData()
    {
      using (FileStream metaData = new FileStream(metaFileName, FileMode.Open, FileAccess.Read)) {
        UInt32 version = FileRecord.ReadUInt32(metaData);
        if (version == 1)
          // skip past unused count field
          FileRecord.ReadUInt32(metaData);
        else if (version != META_FILE_VERSION)
          throw new Exception("file version mismatch: " + metaFileName);
        List<FileRecord> records = new List<FileRecord>();
        while (metaData.Position < metaData.Length)
          records.Add(FileRecord.LoadFromFile(metaData));
        oldFiles = records.ToArray();
      }
      CalculateMaxBlock();
    }

    /// <summary>
    /// Determines the index of the first unused 64K parity block for this drive.
    /// </summary>
    private void CalculateMaxBlock()
    {
      maxBlock = 0;
      foreach (FileRecord r in oldFiles) {
        UInt32 endBlock = r.startBlock + r.LengthInBlocks;
        if (endBlock > maxBlock)
          maxBlock = endBlock;
      }
    }



  }

}
