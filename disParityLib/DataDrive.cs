using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Diagnostics;

namespace disParity
{

  public enum DriveStatus
  {
    ScanRequired,
    UpdateRequired,
    UpToDate,
    AccessError
  }

  public class DataDrive
  {

    private string root;
    private string metaFileName;
    private Dictionary<string, FileRecord> files;     // Master list of protected files; should reflect what is currently in files.dat at all times
    private List<FileRecord> scanFiles; // List of current files on the drive as seen this scan
    private MD5 hash;
    private bool ignoreHidden;
    private List<Regex> ignores;
    private ProgressReporter scanProgress;

    const UInt32 META_FILE_VERSION = 1;

    public event EventHandler<ScanProgressEventArgs> ScanProgress;
    public event EventHandler<StatusChangedEventArgs> StatusChanged;
    public event EventHandler<UpdateProgressEventArgs> UpdateProgress;

    public DataDrive(string root, string metaFileName, bool ignoreHidden, List<string> ignore)
    {
      this.root = root;
      this.metaFileName = metaFileName;

      files = new Dictionary<string, FileRecord>();
      if (File.Exists(metaFileName))
        LoadFileList();

      hash = MD5.Create();
      ignores = new List<Regex>();
      Status = DriveStatus.ScanRequired;
      this.ignoreHidden = ignoreHidden;
      ignores.Clear();
      foreach (string i in ignore) {
        string pattern = Regex.Escape(i.ToLower());       // Escape the original string
        pattern = pattern.Replace(@"\?", ".");  // Replace all \? with .
        pattern = pattern.Replace(@"\*", ".*"); // Replace all \* with .*
        ignores.Add(new Regex(pattern));
      }
    }

    public string Root { get { return root; } }

    public UInt32 MaxBlock { get; private set; }

    public IEnumerable<FileRecord> Files { get {  return files.Values; } }

    public DriveStatus Status { get; private set; }

    public bool Busy { get; private set; }

    public int FileCount { get { return files.Count; } }

    /// <summary>
    /// Total size, in bytes, of all protected files on this drive
    /// </summary>
    public long TotalSize { get; private set; } 

    /// <summary>
    /// Clears all state of the DataDrive, resetting to empty (deletes on-disk
    /// meta data as well.)
    /// </summary>
    public void Clear()
    {
      files.Clear();
      scanFiles = null;
      MaxBlock = 0;
      if (File.Exists(metaFileName))
        File.Delete(metaFileName);
    }

    /// <summary>
    /// Scans the drive from 'root' down, generating the current list of files on the drive.
    /// Files that should not be protected (e.g. Hidden files) are not included.
    /// </summary>
    public void Scan()
    {
      Busy = true;
      try {
        scanFiles = new List<FileRecord>();
        LogFile.Log("Scanning {0}...", root);
        scanProgress = null;
        Scan(new DirectoryInfo(root));
        long totalSize = 0;
        foreach (FileRecord f in scanFiles)
          totalSize += f.Length;
        LogFile.Log("Found {0} file{1} ({2} total)", scanFiles.Count,
          scanFiles.Count == 1 ? "" : "s", Utils.SmartSize(totalSize));
        FireScanProgress("Scan complete. Analyzing results...", 100.0);
        Compare();
        if (adds.Count > 0 || deletes.Count > 0 || moves.Count > 0)
          Status = DriveStatus.UpdateRequired;
        else
          Status = DriveStatus.UpToDate;
        FireStatusChanged();
      }
      finally {
        Busy = false;
      }
    }

    private void Scan(DirectoryInfo dir, ProgressReporter progress = null)
    {
      if (scanProgress != null)
        FireScanProgress("Scanning " + dir.FullName + "...", scanProgress.Progress);
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

      ProgressReporter folderProgress;
      if (scanProgress == null) {
        scanProgress = new ProgressReporter();
        scanProgress.Reset(subDirs.Length);
        folderProgress = scanProgress;
      }
      else
        folderProgress = progress.BeginSubPhase(subDirs.Length);

      foreach (DirectoryInfo d in subDirs) {
        if ((ignoreHidden && (d.Attributes & FileAttributes.Hidden) != 0) ||
            ((d.Attributes & FileAttributes.System) != 0)) {
          folderProgress.EndPhase();
          continue;
        }
        Scan(d, folderProgress);
        folderProgress.EndPhase();
      }
      FireScanProgress("", scanProgress.Progress);
      string relativePath = Utils.StripRoot(root, dir.FullName);
      foreach (FileInfo f in fileInfos) {
        if (f.Attributes == (FileAttributes)(-1))
          continue;
        if (ignoreHidden && (f.Attributes & FileAttributes.Hidden) != 0)
          continue;
        if ((f.Attributes & FileAttributes.System) != 0)
          continue;
        bool ignore = false;
        foreach (Regex regex in ignores)
          if (regex.IsMatch(f.Name.ToLower())) {
            ignore = true;
            break;
          }
        if (ignore) {
          LogFile.Log("Skipping {0} because it matches an ignore...", f.FullName);
          continue;
        }
        scanFiles.Add(new FileRecord(f, relativePath, this));
      }
    }

    private void FireScanProgress(string status, double progress)
    {
      if (ScanProgress != null)
        ScanProgress(this, new ScanProgressEventArgs(status, progress));
    }

    private void FireStatusChanged()
    {
      if (StatusChanged != null)
        StatusChanged(this, new StatusChangedEventArgs(Status, adds.Count, deletes.Count, moves.Count));
    }

    public void FireUpdateProgress(string file, int count, long size, double progress)
    {
      if (UpdateProgress != null)
        UpdateProgress(this, new UpdateProgressEventArgs(file, count, size, progress));
    }

    // the "deletes" list contains FileRecords from the master files list
    private List<FileRecord> deletes = new List<FileRecord>();
    public List<FileRecord> Deletes { get { return deletes; } }

    // the "adds" list contains FileRecords from the new scanFiles list
    private List<FileRecord> adds = new List<FileRecord>();
    public List<FileRecord> Adds { get { return adds; } }

    // the "moves" dictionary maps FilesRecords from the master files list to the new scanFiles list
    private Dictionary<string, FileRecord> moves = new Dictionary<string, FileRecord>();

    /// <summary>
    /// Compare the old list of files with the new list in order to
    /// determine which files had been added, removed, moved, or edited.
    /// </summary>
    private void Compare()
    {
      adds.Clear();
      deletes.Clear();
      moves.Clear();

      // build dictionary of seen file names for fast lookup
      Dictionary<string, FileRecord> seenFileNames = new Dictionary<string, FileRecord>();
      foreach (FileRecord r in scanFiles)
        seenFileNames[r.Name.ToLower()] = r;

      // build list of new files we haven't seen before (adds)
      foreach (FileRecord r in scanFiles)
        if (!files.ContainsKey(r.Name.ToLower()))
          adds.Add(r);

      // build list of old files we don't see now (deletes)
      foreach (var kvp in files)
        if (!seenFileNames.ContainsKey(kvp.Key))
          deletes.Add(kvp.Value);
      
      // some of the files in add/delete list might actually be moves, check for that
      foreach (FileRecord a in adds) {
        byte[] hashCode = null;
        if (a.Length > 0)
          foreach (FileRecord d in deletes)
            if (a.Length == d.Length && a.LastWriteTime == d.LastWriteTime) {
              // probably the same file, but we need to check the hash to be sure
              if (hashCode == null)
                hashCode = ComputeHash(a);
              if (Utils.HashCodesMatch(hashCode, d.HashCode)) {
                LogFile.Log("{0} moved to {1}", Utils.MakeFullPath(root, d.Name), Utils.MakeFullPath(root, a.Name));
                moves[d.Name.ToLower()] = a;
              }
            }
      }
      // remove the moved files from the add and delete lists
      foreach (var kvp in moves) {
        FileRecord delete = null;
        foreach (FileRecord r in deletes)
          if (String.Equals(kvp.Key, r.Name.ToLower())) {
            delete = r;
            break;
          }
        if (delete != null)
          deletes.Remove(delete);
        adds.Remove(kvp.Value);
      }

      // now check for edits
      foreach (var kvp in files) {
        FileRecord n;
        // can only be an edit if we saw the same file name this scan...
        if (seenFileNames.TryGetValue(kvp.Key, out n)) {
          // if we detect an edit, we add the "new" version of the file to the "edit" list, 
          // because it has the new attributes and we want those saved later
          if (kvp.Value.Length != n.Length) {
            deletes.Add(kvp.Value);
            adds.Add(n);
          } 
          else if (kvp.Value.CreationTime != n.CreationTime || kvp.Value.LastWriteTime != n.LastWriteTime)
            // probable edit, check hash code to be sure
            if (!HashCheck(kvp.Value)) {
              deletes.Add(kvp.Value);
              adds.Add(n);
            }
        }
      }

      LogFile.Log("Adds: {0} Deletes: {1} Moves: {2}", adds.Count, deletes.Count, moves.Count);

    }

    /// <summary>
    /// Process moves by updating their records to reflect the new locations
    /// </summary>
    public void ProcessMoves()
    {
      if (moves.Count == 0)
        return;
      // the moves dictionary maps old file names to new FileRecords
      LogFile.Log("Processing moves for {0}...", root);
      foreach (var kvp in moves) {
        // find the old entry
        FileRecord old;
        if (!files.TryGetValue(kvp.Key, out old))
          throw new Exception("Unable to locate moved file " + kvp.Key + " in master file table");
        // remove it from the table
        files.Remove(kvp.Key);
        // update new record to carry over meta data from the old one
        kvp.Value.StartBlock = old.StartBlock;
        kvp.Value.HashCode = old.HashCode;
        // store new record in master file table
        files[kvp.Value.Name.ToLower()] = kvp.Value;
      }
      // save updated master file table
      SaveFileList();
      // clear moves list, don't need it anymore
      moves.Clear();
    }

    /// <summary>
    /// Removes the file from the newFiles list and saves the new list to disk
    /// </summary>
    public void RemoveFile(FileRecord r)
    {
      string filename = r.Name.ToLower();
      Debug.Assert(files.ContainsKey(filename));
      files.Remove(filename);
      SaveFileList();
    }

    /// <summary>
    /// Adds the file to newFiles and saves the new list to disk
    /// </summary>
    public void AddFile(FileRecord r)
    {
      string filename = r.Name.ToLower();
      Debug.Assert(!files.ContainsKey(filename));
      files[filename] = r;
      SaveFileList();
    }

    public void ClearDeletes()
    {
      deletes.Clear();
      UpdateStatus();
    }

    public void ClearAdds()
    {
      adds.Clear();
      UpdateStatus();
    }

    private void UpdateStatus()
    {
      if (adds.Count > 0 || moves.Count > 0 || deletes.Count > 0)
        Status = DriveStatus.UpdateRequired;
      else
        Status = DriveStatus.UpToDate;
      FireStatusChanged();
    }

    /// <summary>
    /// Run a hash check on every file on this drive.  Return the number of failures.
    /// </summary>
    public int HashCheck()
    {
      Busy = true;
      try {
        int failures = 0;
        foreach (FileRecord r in files.Values)
          try {
            LogFile.Log("Checking hash for {0}...", r.FullPath);
            if (!HashCheck(r)) {
              LogFile.Log("Hash check FAILED");
              failures++;
            }
          }
          catch (Exception e) {
            LogFile.Log("Error opening {0} for hash check: {1}", r.FullPath, e.Message);
          }
        return failures;

      }
      finally {
        Busy = false;
      }
    }

    /// <summary>
    /// Verify that the hash on record for this file matches the actual file currently on disk
    /// </summary>
    public bool HashCheck(FileRecord r)
    {
      if (r.Length == 0)
        return true; // zero length files cannot fail a hash check
      else
       return Utils.HashCodesMatch(ComputeHash(r), r.HashCode);
    }

    /// <summary>
    /// Compute the hash code for the file on disk
    /// </summary>
    private byte[] ComputeHash(FileRecord r)
    {
      using (FileStream s = new FileStream(r.FullPath, FileMode.Open, FileAccess.Read))
        hash.ComputeHash(s);
      return hash.Hash;
    }

    private UInt32 enumBlock;
    private UInt32 enumBlocks;
    private int enumCount;
    private FileStream enumFile;
    private long enumSize;
    private List<FileRecord>.Enumerator enumerator;
    private bool enumComplete;

    public void BeginFileEnum()
    {
      enumBlock = 0;
      enumFile = null;
      enumComplete = false;
      enumBlocks = 0;
      foreach (FileRecord r in scanFiles)
        enumBlocks += r.LengthInBlocks;
      enumerator = scanFiles.GetEnumerator();
      enumCount = 0;
      enumSize = 0;
      Busy = true;
    }

    public bool GetNextBlock(byte[] buf)
    {
      if (buf.Length != Parity.BlockSize)
        throw new Exception("Invalid buffer size (must be " + Parity.BlockSize + " bytes)");
      if (enumFile == null) {
        if (enumComplete)
          return false;
        if (!enumerator.MoveNext()) {
          enumerator.Dispose();
          enumComplete = true;
          LoadFileList(); // this loads completed filesX.dat back into the master files list
          Status = DriveStatus.UpToDate;
          FireStatusChanged();
          Busy = false;
          return false;
        }
        // TODO: Handle zero-length file here?
        string fullName = Utils.MakeFullPath(root, enumerator.Current.Name);
        try {
          enumFile = new FileStream(fullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception e) {
          LogFile.Log("Error opening {0} for reading: {1}", fullName, e.Message);
          enumerator.Current.Skipped = true;
          enumerator.MoveNext();
          return GetNextBlock(buf);
        }
        FireUpdateProgress("Reading " + fullName, enumCount, enumSize, (double)enumBlock / (double)enumBlocks);
        LogFile.Log("Reading {0}", fullName);
        hash.Initialize();
        enumerator.Current.StartBlock = enumBlock;
      }
      int bytesRead = enumFile.Read(buf, 0, Parity.BlockSize);
      if (enumFile.Position < enumFile.Length)
        hash.TransformBlock(buf, 0, bytesRead, buf, 0);
      else {
        // reached end of this file
        hash.TransformFinalBlock(buf, 0, bytesRead);
        enumerator.Current.HashCode = hash.Hash;
        AppendFileRecord(enumerator.Current);
        enumFile.Close();
        enumFile.Dispose();
        enumFile = null;
        enumCount++;
        enumSize += enumerator.Current.Length;
        Array.Clear(buf, bytesRead, Parity.BlockSize - bytesRead);
      }
      enumBlock++;
      FireUpdateProgress("", enumCount, enumSize, (double)enumBlock / (double)enumBlocks);
      return true;
    }    

    public void EndFileEnum()
    {
      enumerator.Dispose();
    }

    public bool ReadBlock(UInt32 block, byte[] data)
    {
      FileRecord r = FindFileContaining(block);
      if (r == null)
        return false;
      string fullPath = r.FullPath;
      if (!File.Exists(fullPath))
        return false;
      // to do: what if the file has been edited?
      // Allow any I/O exceptions below to be caught by parent
      using (FileStream f = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read)) {
        f.Position = (block - r.StartBlock) * Parity.BlockSize;
        int bytesRead = f.Read(data, 0, data.Length);
        while (bytesRead < data.Length)
          data[bytesRead++] = 0;
        f.Close();
      }
      return true;
    }

    /// <summary>
    /// Returns a mask of used/unused blocks for this drive
    /// </summary>
    public BitArray BlockMask
    {
      get
      {
        BitArray blockMask = new BitArray((int)MaxBlock);
        foreach (FileRecord r in files.Values) {
          UInt32 endBlock = r.StartBlock + r.LengthInBlocks;
          for (int i = (int)r.StartBlock; i < endBlock; i++)
            blockMask.Set(i, true);
        }
        return blockMask;
      }
    }

    private FileRecord FindFileContaining(UInt32 block)
    {
      if (block < MaxBlock)
        foreach (FileRecord r in files.Values)
          if (r.ContainsBlock(block))
            return r;
      return null;
    }

    /// <summary>
    /// Loads the files.dat file containing the records of any existing protected file data
    /// </summary>
    private void LoadFileList()
    {
      using (FileStream metaData = new FileStream(metaFileName, FileMode.Open, FileAccess.Read)) {
        UInt32 version = FileRecord.ReadUInt32(metaData);
        if (version == 1)
          // skip past unused count field
          FileRecord.ReadUInt32(metaData);
        else if (version != META_FILE_VERSION)
          throw new Exception("file version mismatch: " + metaFileName);
        files.Clear();
        while (metaData.Position < metaData.Length) {
          FileRecord r = FileRecord.LoadFromFile(metaData, this);
          files[r.Name.ToLower()] = r;
        }
      }
      CalculateMaxBlock();
    }

    private void SaveFileList()
    {
      DateTime start = DateTime.Now;
      LogFile.VerboseLog("Saving file data for {0}...", root);
      string backup = "";
      if (File.Exists(metaFileName)) {
        backup = metaFileName + ".BAK";
        File.Move(metaFileName, backup);
      }
      using (FileStream f = new FileStream(metaFileName, FileMode.Create, FileAccess.Write)) {
        FileRecord.WriteUInt32(f, META_FILE_VERSION);
        FileRecord.WriteUInt32(f, (UInt32)files.Count);
        foreach (FileRecord r in files.Values)
          r.WriteToFile(f);
        f.Close();
      }
      if (backup != "")
        File.Delete(backup);
      TimeSpan elapsed = DateTime.Now - start;
      CalculateMaxBlock();
      LogFile.VerboseLog("{0} records saved in {1:F2} sec", files.Count, elapsed.TotalSeconds);
    }

    private void AppendFileRecord(FileRecord r)
    {
      if (!File.Exists(metaFileName))
        using (FileStream fNew = new FileStream(metaFileName, FileMode.Create, FileAccess.Write)) {
          FileRecord.WriteUInt32(fNew, META_FILE_VERSION);
          FileRecord.WriteUInt32(fNew, 0); // unknown count
        }
      using (FileStream f = new FileStream(metaFileName, FileMode.Append, FileAccess.Write))
        r.WriteToFile(f); 
    }
    
    /// <summary>
    /// Determines the index of the first unused 64K parity block for this drive.
    /// Also re-calculates TotalSize while we're at it.
    /// </summary>
    private void CalculateMaxBlock()
    {
      MaxBlock = 0;
      TotalSize = 0;
      foreach (FileRecord r in files.Values) {
        UInt32 endBlock = r.StartBlock + r.LengthInBlocks;
        if (endBlock > MaxBlock)
          MaxBlock = endBlock;
        TotalSize += r.Length;
      }
    }

    /// <summary>
    /// Generates a "free list" of unused blocks in the existing parity data 
    /// for this drive which we can then re-use for adds, so that we don't grow 
    /// the parity data unnecessarily.
    /// </summary>
    public List<FreeNode> GetFreeList()
    {
      BitArray blockMask = BlockMask;

      List<FreeNode> freeList = new List<FreeNode>();
      UInt32 block = 0;
      while (block < MaxBlock)
        if (!blockMask.Get((int)block)) {
          FreeNode n = new FreeNode();
          n.Start = block++;
          n.Length = 1;
          while (block < MaxBlock && (!blockMask.Get((int)block))) {
            n.Length++;
            block++;
          }
          freeList.Add(n);
        }
        else
          block++;

      return freeList;
    }

    public override string ToString()
    {
      return root;
    }

  }

  public class FreeNode
  {
    public UInt32 Start { get; set; }
    public UInt32 Length { get; set; }  // in blocks
    public const UInt32 INVALID_BLOCK = 0xFFFFFFFF;

    public static UInt32 FindBest(List<FreeNode> list, UInt32 blocks)
    {
      FreeNode best = null;
      foreach (FreeNode n in list)
        if (n.Length == blocks) {
          best = n;
          break;
        }
        else if (n.Length > blocks)
          if ((best == null) || (n.Length < best.Length))
            best = n;
      if (best == null)
        return INVALID_BLOCK;
      UInt32 result = best.Start;
      if (best.Length == blocks)
        list.Remove(best);
      else {
        best.Start += blocks;
        best.Length -= blocks;
      }
      return result;
    }

  }

  public class ScanProgressEventArgs : EventArgs
  {
    public ScanProgressEventArgs(string status, double progress)
    {
      Status = status;
      Progress = progress;
    }

    public string Status { get; private set; }
    public double Progress { get; private set; }
  }

  public class StatusChangedEventArgs : EventArgs
  {
    public StatusChangedEventArgs(DriveStatus status, int addCount, int deleteCount, int moveCount)
    {
      Status = status;
      AddCount = addCount;
      DeleteCount = deleteCount;
      MoveCount = moveCount;
    }

    public DriveStatus Status { get; private set; }
    public int AddCount { get; private set; }
    public int DeleteCount { get; private set; }
    public int MoveCount { get; private set; }
  }

  public class UpdateProgressEventArgs : EventArgs
  {
    public UpdateProgressEventArgs(string status, int files, long size, double progress)
    {
      Status = status;
      Files = files;
      Size = size;
      Progress = progress;
    }

    public string Status { get; private set; }
    public int Files { get; private set; }
    public long Size { get; private set; }
    public double Progress { get; private set; }
  }

}
