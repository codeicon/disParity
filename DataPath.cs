using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;

namespace disParity
{
  class DataPath
  {
    /* This is the master list of all files that have been committed to
     * parity.  It will initially be empty on a create. */
    public List<FileRecord> fileList;
    public List<FileRecord> newFiles;
    Dictionary<string, FileRecord> fileNames; // maps file names to
                                              // entries in fileList
    public List<FileRecord> deletedFiles;
    public FileSystemWatcher watcher;
    public ArrayList eventQueue;
    Int32 currentFile;
    FileStream currentOpenFile;
    public string root;
    MD5 hash;
    string metaFileName;
    public UInt32 maxBlock;
    public UInt32 movedCount;
    bool fileListChanged;
    Int32 driveNum;

    const UInt32 META_FILE_VERSION = 1;

    public DataPath(Int32 num, string root, Command cmd)
    {
      driveNum = num;
      fileList = new List<FileRecord>();
      fileNames = new Dictionary<string, FileRecord>();
      newFiles = new List<FileRecord>();
      deletedFiles = new List<FileRecord>();
      this.root = root;
      currentFile = 0;
      currentOpenFile = null;
      if (cmd == Command.Create)
        fileListChanged = true;
      else
        fileListChanged = false;
      hash = MD5.Create();
      metaFileName = Parity.Dir + "files" + num.ToString() + ".dat";
      movedCount = 0;
      if (cmd != Command.Create) {
        if (!File.Exists(metaFileName)) {
          if (cmd == Command.Update)
            Program.logFile.Write("{0} not found.  Assuming {1} is a new " +
              "drive to be added to parity.\r\n", metaFileName, root);
          else 
            throw new Exception(String.Format("{0} not found.", metaFileName));
        } else if (!LoadFileList(cmd))
          throw new Exception(String.Format("Could not read {0}",
            metaFileName));
      }
      if (cmd != Command.Test && cmd != Command.Recover && cmd != Command.List &&
        cmd != Command.Stats && cmd != Command.HashCheck) {
        Program.logFile.Write("Scanning {0}...\r\n", root);
        FindNewFiles("", new DirectoryInfo(root));
        CheckForUnseen();
        if (cmd == Command.Create)
          Program.logFile.Write("{0} files found.\r\n", newFiles.Count);
        else
          Program.logFile.Write("Adds: {0} Moves: {1} Deletes: {2}\r\n", 
            newFiles.Count, movedCount, deletedFiles.Count);
      }
    }

    public void DumpFileList()
    {
      Program.logFile.Write("Protected files for {0}:\r\n", root);
      foreach (FileRecord r in fileList)
        Program.logFile.Write("{0}\r\n", r.name);
    }

    bool LoadFileList(Command cmd)
    {
      Program.logFile.Write("Loading file data for {0}...", root);
      FileStream metaData = new FileStream(metaFileName, FileMode.Open,
        FileAccess.Read);
      UInt32 version = FileRecord.ReadUInt32(metaData);
      if (version != META_FILE_VERSION) {
        Program.logFile.Write("file version mismatch!\r\n");
        return false;
      }
      UInt32 count = FileRecord.ReadUInt32(metaData);
      fileList.Capacity = (int)count;
      for (int i = 0; i < count; i++) {
        FileRecord r;
        try {
          r = FileRecord.LoadFromFile(metaData);
        }
        catch {
          metaData.Close();
          return false;
        }
        r.drive = driveNum;
        if (cmd != Command.List && cmd != Command.Stats)
          r.exists = File.Exists(root + "\\" + r.name);
        fileNames.Add(r.name.ToLower(), r);
        fileList.Add(r);
        if (!r.exists)
          deletedFiles.Add(r);
      }
      metaData.Close();
      CalculateMaxBlock();
      Program.logFile.Write("{0} record{1} loaded.\r\n", fileList.Count,
        fileList.Count == 1 ? "" : "s");
      return true;
    }

    void CheckForUnseen()
    {
      foreach (FileRecord r in fileList)
        if (!r.seen && !deletedFiles.Contains(r))
          deletedFiles.Add(r);
    }

    void FindNewFiles(string path, DirectoryInfo dir)
    {
      DirectoryInfo[] subDirs;
      try {
        subDirs = dir.GetDirectories();
      }
      catch (Exception e) {
        Program.logFile.Write("Warning: Could not enumerate subdirectories of {0}\r\n: {1}",
          path, e.Message);
        Program.logFile.Write("Directory will be skipped.\r\n");
        return;
      }
      foreach (DirectoryInfo d in subDirs) {
        if (Program.ignoreHidden &&
          (d.Attributes & FileAttributes.Hidden) != 0) {
          if (Program.logFile.Verbose)
            Program.logFile.Write("Skipping {0} because it is a Hidden " +
              "folder.\r\n", d.FullName);
          continue;
        }
        if ((d.Attributes & FileAttributes.System) == 0)
          FindNewFiles(Program.MakeFullPath(path, d.Name), d);
      }
      FileInfo[] files;
      try {
        files = dir.GetFiles();
      }
      catch (Exception e) {
        Program.logFile.Write("Warning: Could not enumerate contents of {0}\r\n: {1}",
          path, e.Message);
        Program.logFile.Write("Files in this directory will be skipped.\r\n");
        return;
      }
      foreach (FileInfo f in files) {
        string name = Program.MakeFullPath(path, f.Name);
        if (f.Attributes == (FileAttributes)(-1)) {
          Program.logFile.Write("Could not query attributes of {0}\r\n", name);
          continue;
        }
        if (Program.ignoreHidden && 
          (f.Attributes & FileAttributes.Hidden) != 0) {
          if (Program.logFile.Verbose)
            Program.logFile.Write("Skipping {0} because it is a Hidden " +
              "file.\r\n", name);
          continue;
        }
        if ((f.Attributes & FileAttributes.System) != 0) {
          if (Program.logFile.Verbose)
            Program.logFile.Write("Skipping {0} because it is a System " +
              "file.\r\n", name);
          continue;
        }
        /* Only add this file if it is actually new. */
        FileRecord r;
        if (!fileNames.TryGetValue(name.ToLower(), out r)) {
          /* Possibly a new file, or it could be a move/rename.  Check
           * for that first. */
          if (CheckForMoveOrRename(f, path))
            movedCount++;
          else
            newFiles.Add(new FileRecord(f, path, driveNum));
        } else if (CheckForEdit(f, path, r)) {

          /* Any edited files go into both the deleted files
           * list and the added list.  This way they automatically get
           * removed and then re-added to the parity files.  Nifty! */

          r.seen = true;
          deletedFiles.Add(r);
          r.replacement = new FileRecord(f, path, driveNum);
          newFiles.Add(r.replacement);

          /* Also treat the file as not existing.  That way we don't try to
           * read from it when processing other files during an update. */
          r.exists = false;
          if (Program.logFile.Verbose)
            Program.logFile.Write("File edit detected: {0}\r\n" +
              "Length: {1} -> {2}\r\n" +
              "Creation time: {3} -> {4}\r\n" +
              "Last Write Time: {5} -> {6}\r\n", f.FullName, r.length,
              f.Length, r.creationTime, f.CreationTime, r.lastWriteTime,
              f.LastWriteTime);

        } else
          r.seen = true;
      }
    }

    public static bool HashCodesMatch(byte[] h1, byte[] h2)
    {
      if (h1.Length != h2.Length)
        return false;
      for (int i = 0; i < h1.Length; i++)
        if (h1[i] != h2[i]) 
          return false;
      return true;
    }

    bool CheckForEdit(FileInfo f, string path, FileRecord r)
    {
      if (f.Length != r.length)
        /* length changed, trivial case, file must have been edited */
        return true;
      if (f.CreationTime != r.creationTime || f.LastWriteTime !=
        r.lastWriteTime) {
        /* file create/write times have changed, file may have been edited,
         * need to compute hash code to see (except for zero-length files). */
        if (f.Length > 0 && (f.LastWriteTime != r.lastWriteTime)) {
          try {
            FileStream s = new FileStream(f.FullName, FileMode.Open,
              FileAccess.Read);
            hash.ComputeHash(s);
            s.Close();
          }
          catch {
            Program.logFile.Write("Warning: {0} appears to have changed but " +
              "could not be accessed.  It will be ignored.\r\n", f.FullName);
            return false;
          }
          if (!HashCodesMatch(r.hashCode, hash.Hash))
            return true;
        }
        /* File data didn't change, but we still need to be sure to update
         * the file meta data with the new timestamps. */
        fileListChanged = true;
        r.creationTime = f.CreationTime;
        r.lastWriteTime = f.LastWriteTime;
      }
      return false;
    }

    bool CheckForMoveOrRename(FileInfo f, string path)
    {
      byte[] hashCode = null;
      /* If this file is a move or rename, its previous incarnation
       * will show in the list of deleted files. */
      foreach (FileRecord r in deletedFiles)
        if (r.length == f.Length /*&& r.creationTime == f.CreationTime*/ &&
          r.lastWriteTime == f.LastWriteTime) {
          /* Almost certainly a match, but we need to check the hash code to 
           * really be sure. */
          if (hashCode == null) {
            FileStream s = new FileStream(f.FullName, FileMode.Open,
              FileAccess.Read);
            hash.ComputeHash(s);
            s.Close();
            hashCode = hash.Hash;
          }
          if (HashCodesMatch(hashCode, r.hashCode)) {
            string newName = Program.MakeFullPath(path, f.Name);
            Program.logFile.Write("{0} moved to {1}\r\n", r.name,
              newName);
            r.name = newName;
            r.exists = true;
            fileListChanged = true;
            deletedFiles.Remove(r);
            return true;
          }
        }
      return false;
    }

    public void RemoveFile(FileRecord r)
    {
      fileList.Remove(r);
      fileListChanged = true;
      /* Master file list was just changed, so we need to clear any cached
       * open file. */
      CloseFile();
      /* Also need to update maxBlock */
      CalculateMaxBlock();
    }

    public void AddFile(FileRecord r)
    {
      fileList.Add(r);
      fileListChanged = true;
      /* Master file list was just changed, so we need to clear any cached
       * open file. */
      CloseFile();
      /* Also need to update maxBlock */
      CalculateMaxBlock();
    }

    void CalculateMaxBlock()
    {
      maxBlock = 0;
      foreach (FileRecord r in fileList) {
        UInt32 endBlock = r.startBlock + r.LengthInBlocks;
        if (endBlock > maxBlock)
          maxBlock = endBlock;
      }
    }

    public static string StripRoot(string root, string path)
    {
      if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        return path;
      path = path.Remove(0, root.Length);
      while (path[0] == Path.DirectorySeparatorChar)
        path = path.Remove(0, 1);
      return path;
    }

    public FileRecord FindFile(string fullPath)
    {
      if (Path.IsPathRooted(fullPath))
        fullPath = StripRoot(root, fullPath);
      foreach (FileRecord r in fileList)
        if (String.Compare(r.name, fullPath, true) == 0)
          return r;
      return null;
    }

    /* Called from Monitor in response to a real time rename event */
    public void RenameFile(FileRecord r, string newPath)
    {
      string fullPath = Program.MakeFullPath(root, r.name);
      string newPathR = StripRoot(root, newPath);
      if (newPath == newPathR) {
        Program.logFile.Write("Can't rename {0} to {1}: new name does not begin with {2}\r\n",
         fullPath, newPath, root);
        return;
      }
      if (!File.Exists(newPath)) {
        Program.logFile.Write("Can't rename {0} to {1}: new file does not exist\r\n",
          fullPath, newPath);
        return;
      }
      r.name = newPathR;
      fileListChanged = true;
      SaveFileList();
    }

    /* Called during a create only.  Reads a block from the current list
     * of new files. */
    public int ReadData(byte[] buf, UInt32 block)
    {
      if (currentOpenFile == null) {
        if (currentFile == newFiles.Count) {
          SaveFileList();
          return 0;
        }
        string fullName = Program.MakeFullPath(root, 
          newFiles[currentFile].name);
        try {
          currentOpenFile = new FileStream(fullName,
            FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception e) {
          Program.logFile.Write("Warning: Could not read file {0} (Error message was \"{1}\")\r\n",
            fullName, e.Message);
          Program.logFile.Write("File will NOT be added to parity!\r\n");
          newFiles.RemoveAt(currentFile);
          return ReadData(buf, block);
        }
        Program.logFile.Write("Reading {0}\r\n", fullName);
        newFiles[currentFile].startBlock = block;
        hash.Initialize();
      }
      Int32 bytesRead = currentOpenFile.Read(buf, 0, buf.Length);
      for (int i = bytesRead; i < buf.Length; i++)
        buf[i] = 0;
      if (currentOpenFile.Position < currentOpenFile.Length)
        hash.TransformBlock(buf, 0, bytesRead, buf, 0);
      else
        hash.TransformFinalBlock(buf, 0, bytesRead);
      if (currentOpenFile.Position >= currentOpenFile.Length) {
        newFiles[currentFile].hashCode = hash.Hash;
        /* File has now been committed to parity, so place it in the master
         * file list. */
        fileList.Add(newFiles[currentFile]);
        currentOpenFile.Close();
        currentOpenFile = null;
        currentFile++;
        if (bytesRead == 0)
          return ReadData(buf, block);
      }
      return bytesRead;
    }

    /* Finds the file containing this block */
    public Int32 FileFromBlock(UInt32 block)
    {
      Int32 fileNum = 0;
      while (fileNum < fileList.Count) {
        if (fileList[fileNum].ContainsBlock(block))
          break;
        fileNum++;
      }
      if (fileNum == fileList.Count)
        return -1;
      return fileNum;
    }

    /* Called during an update, verify, or recover.  Reads a block of data from
     * a file that has already been committed to parity. */
    public bool ReadFileData(UInt32 block, byte[] data)
    {
      if (block >= maxBlock)
        return false;
      int fileNum;
      if ((currentFile < fileList.Count) && 
          fileList[currentFile].ContainsBlock(block))
        fileNum = currentFile;
      else {
        fileNum = FileFromBlock(block);
        if (fileNum == -1)
          return false; // couldn't find any file on this drive for this block
      }
      FileRecord rec = fileList[fileNum];
      if (!rec.exists)
        return false; // file was deleted
      if (fileNum != currentFile || currentOpenFile == null) {
        if (currentOpenFile != null)
          currentOpenFile.Close();
        currentOpenFile = new FileStream(Program.MakeFullPath(root, rec.name), 
          FileMode.Open, FileAccess.Read, FileShare.Read);
        currentFile = fileNum;
      }
      long position = (block - rec.startBlock) * Parity.BlockSize;
      if (currentOpenFile.Position != position)
        currentOpenFile.Position = position;
      Int32 bytesRead = currentOpenFile.Read(data, 0, data.Length);
      while (bytesRead < data.Length)
        data[bytesRead++] = 0;
      return true;
    }

    /* Close the file that is currently being held open as an optimzation for
     * ReadData and ReadFileData. */
    public void CloseFile()
    {
      if (currentOpenFile != null) {
        currentOpenFile.Close();
        currentOpenFile = null;
      }
    }

    public void SaveFileList()
    {
      if (fileListChanged) {
        DateTime start = DateTime.Now;
        if (Program.logFile.Verbose)
          Program.logFile.Write("Saving file data for {0}...\r\n", root);
        string backup = "";
        if (File.Exists(metaFileName)) {
          backup = metaFileName + ".BAK";
          File.Move(metaFileName, backup);
        }
        FileStream f = new FileStream(metaFileName, FileMode.Create,
          FileAccess.Write);
        FileRecord.WriteUInt32(f, META_FILE_VERSION);
        FileRecord.WriteUInt32(f, (UInt32)fileList.Count);
        foreach (FileRecord r in fileList)
          r.WriteToFile(f);
        f.Close();
        fileListChanged = false;
        if (backup != "")
          File.Delete(backup);
        if (Program.logFile.Verbose) {
          TimeSpan elapsed = DateTime.Now - start;
          Program.logFile.Write("{0} records saved in {1:F2} sec\r\n",
            fileList.Count, elapsed.TotalSeconds);
        }
      }
    }

    /* Generates a "free list" of unused blocks in the existing parity data 
     * for this drive which we can then re-use for adds, so that we don't grow 
     * the parity data unnecessarily. */
    public List<FreeNode> GetFreeList()
    {
      bool[] blockMap = new bool[maxBlock];
      foreach (FileRecord r in fileList) 
        if (r.exists) {
          UInt32 endBlock = r.startBlock + r.LengthInBlocks;
          for (UInt32 i = r.startBlock; i < endBlock; i++)
            blockMap[i] = true;
        }

      List<FreeNode> freeList = new List<FreeNode>();
      UInt32 block = 0;
      while (block < maxBlock)
        if (!blockMap[block]) {
          FreeNode n = new FreeNode();
          n.start = block++;
          n.length = 1;
          while (block < maxBlock && (!blockMap[block])) {
            n.length++;
            block++;
          }
          freeList.Add(n);
        } else
          block++;

      return freeList;
    }

  }

  public class FreeNode
  {
    public UInt32 start;
    public UInt32 length; // in blocks
    public const UInt32 INVALID_BLOCK = 0xFFFFFFFF;

    public static UInt32 FindBest(List<FreeNode> list, UInt32 blocks)
    {
      FreeNode best = null;
      foreach (FreeNode n in list)
        if (n.length == blocks) {
          best = n;
          break;
        } else if (n.length > blocks)
          if ((best == null) || (n.length < best.length))
            best = n;
      if (best == null)
        return INVALID_BLOCK;
      UInt32 result = best.start;
      if (best.length == blocks)
        list.Remove(best);
      else {
        best.start += blocks;
        best.length -= blocks;
      }
      return result;
    }

  }

}
