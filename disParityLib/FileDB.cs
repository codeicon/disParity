using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace disParity
{

  internal class FileDB
  {

    private const UInt32 META_FILE_VERSION = 2;

    private Dictionary<string, FileRecord> files;
    private List<FileRecord> deletes;
    private IEnvironment env;

    internal FileDB(string fileName, IEnvironment environment)
    {
      env = environment;
      files = new Dictionary<string, FileRecord>();
      deletes = new List<FileRecord>();
      FileName = fileName;
      // If the .dat file doesn't exist, create it
      if (!File.Exists(FileName))
        Write(false);
    }

    internal IEnumerable<FileRecord> Files { get { return files.Values; } }

    internal int FileCount { get { return files.Count; } }

    internal string FileName { get; private set; }

    /// <summary>
    /// Loads the files.dat file containing the records of any existing protected file data
    /// </summary>
    internal void Load(string root)
    {
      files.Clear();
      deletes.Clear();
      if (!File.Exists(FileName))
        return; // nothing more to do
      try
      {
        using (FileStream metaData = new FileStream(FileName, FileMode.Open, FileAccess.Read))
        {
          UInt32 version = FileRecord.ReadUInt32(metaData);
          if (version == 1)
          {
            metaData.Close();
            Upgrade();
            Load(root);
            return;
          }
          if (version >= 1)
            // skip past unused count field
            FileRecord.ReadUInt32(metaData);
          else if (version != META_FILE_VERSION)
            throw new Exception("file version mismatch: " + FileName);
          while (metaData.Position < metaData.Length)
          {
            FileRecord r = FileRecord.LoadFromFile(metaData, root);
            if (r.Deleted)
              deletes.Add(r);
            else
              files[r.Name.ToLower()] = r;
          }
        }
      }
      catch (Exception e)
      {
        env.LogCrash(e);
        LogFile.Error(String.Format("Error reading {0}: {1}", FileName, e.Message));
        files.Clear();
        throw new MetaFileLoadException(FileName, e);
      }
    }

    private void Upgrade()
    {
      LogFile.Log("Upgrading {0} to new format...", FileName);
      Backup();
      // read in old format...
      files.Clear();
      using (FileStream metaData = new FileStream(FileName, FileMode.Open, FileAccess.Read))
      {
        UInt32 version = FileRecord.ReadUInt32(metaData);
        Debug.Assert(version < META_FILE_VERSION);
        // skip count field
        metaData.Position = 8;
        while (metaData.Position < metaData.Length)
        {
          FileRecord r = FileRecord.LoadFromOldVerion(metaData, "", version);
          files[r.Name.ToLower()] = r;
        }
      }
      // write out new format
      Save();
      files.Clear();


    }

    internal void Save()
    {
      try
      {
        Write();
      }
      catch (Exception e)
      {
        // try to restore the backup
        Restore();
        throw e;
      }
    }

    internal void Clear()
    {
      if (File.Exists(FileName))
        File.Delete(FileName);
      if (File.Exists(Path.ChangeExtension(FileName, ".bak")))
        File.Delete(Path.ChangeExtension(FileName, ".bak"));
      files.Clear();
    }

    internal void MarkAllAsUnseen()
    {
      foreach (var kvp in files)
        kvp.Value.Seen = false;
    }

    internal FileRecord Find(string fileName)
    {
      Debug.Assert(fileName == fileName.ToLower());
      FileRecord r;
      if (files.TryGetValue(fileName, out r))
        return r;
      else
        return null;
    }

    /// <summary>
    /// Finds the smallest existing deleted record that is at least 'length' in size
    /// </summary>
    private FileRecord FindSmallestDelete(UInt16 length)
    {
      FileRecord smallest = null;
      foreach (FileRecord d in deletes)
        if (d.RecLength >= length)
          if (smallest == null || d.RecLength < smallest.RecLength)
            smallest = d;
      return smallest;
    }

    internal void Add(FileRecord r)
    {
      string filename = r.Name.ToLower();
      Debug.Assert(!files.ContainsKey(filename));
      files[filename] = r;
      using (MemoryStream ms = r.Encode())
      {
        // see if there is a deleted record we can overwrite
        FileRecord best = FindSmallestDelete(r.RecLength);
        if (best != null)
        {
          // we are recycling this record, so remove it from deletes
          deletes.Remove(best);
          r.Position = best.Position;
          using (FileStream f = new FileStream(FileName, FileMode.OpenOrCreate, FileAccess.Write))
          {
            f.Position = r.Position;
            ms.WriteTo(f);
          }
        }
        else
        {
          using (FileStream f = new FileStream(FileName, FileMode.Append, FileAccess.Write))
          {
            r.Position = (UInt32)f.Position;
            ms.WriteTo(f);
          }
        }
      }
    }

    private static UInt16 MakeDeleteMask(FileRecord r)
    {
      return (UInt16)(r.RecLength | 0x8000);
    }

    internal void Remove(FileRecord r)
    {
      string filename = r.Name.ToLower();
      Debug.Assert(files.ContainsKey(filename));
      files.Remove(filename);
      using (FileStream f = new FileStream(FileName, FileMode.OpenOrCreate, FileAccess.Write))
      {
        f.Position = r.Position;
        FileRecord.WriteUInt16(f, MakeDeleteMask(r));
      }
      deletes.Add(r);
    }

    /// <summary>
    /// Replace the record for the given file name with a new one (called for moved files)
    /// </summary>
    internal void Move(string fileName, FileRecord r)
    {
      // find the old entry
      FileRecord old = Find(fileName);
      if (old == null)
        throw new Exception("Unable to locate moved file " + fileName + " in master file table");
      // update new record to carry over meta data from the old one
      r.StartBlock = old.StartBlock;
      r.HashCode = old.HashCode;
      Remove(old); // mark the old record as deleted
      // FIXME: This makes another backup copy every time!!!
      if (!Extend(r)) // make room for the new record
        return;
      Add(r); // add the new record
      // remove the old entry from the in-memory dictionary and add the new one
      files.Remove(fileName);
      files[r.Name.ToLower()] = r;
    }

    internal IEnumerable<string> FilesInFolder(string folderPath)
    {
      return files.Keys.Where(x => x.StartsWith(folderPath));
    }

    /// <summary>
    /// Makes a backup copy of the file database
    /// </summary>
    internal bool Backup()
    {
      string backup = Path.ChangeExtension(FileName, ".bak");
      try
      {
        File.Copy(FileName, backup, true);
      }
      catch (Exception e)
      {
        LogFile.Error(String.Format("Could not backup {0}: {1}", FileName, e.Message));
        return false;
      }
      return true;
    }

    /// <summary>
    /// Restore the meta file from the backup copy
    /// </summary>
    private bool Restore()
    {
      string backup = Path.ChangeExtension(FileName, ".bak");
      try
      {
        File.Copy(backup, FileName, true);
      }
      catch (Exception e)
      {
        LogFile.Error(String.Format("Could not restore {0} from backup: {1}", FileName, e.Message));
        return false;
      }
      return true;
    }

    /// <summary>
    /// Ensures there is enough room in the existing .dat file to add the given record
    /// </summary>
    internal bool Extend(FileRecord newRecord)
    {
      using (MemoryStream ms = newRecord.Encode())
      {
        // if there is an existing deleted record that this one can replace, just use that
        if (FindSmallestDelete(newRecord.RecLength) != null)
          return true;
        // no deleted record that this one can replace, so we'll have to add it to the end.
        // back up the existing .dat file first
        if (!Backup())
          return false;
        try
        {
          // write the record to the end, then go back and mark it as deleted
          using (FileStream f = new FileStream(FileName, FileMode.Append, FileAccess.Write))
          {
            long pos = f.Position;
            ms.WriteTo(f);
            f.Position = pos;
            FileRecord.WriteUInt16(f, MakeDeleteMask(newRecord));
          }
          deletes.Add(newRecord);
        }
        catch (Exception e)
        {
          LogFile.Error(String.Format("Could not extend {0}: {1}", FileName, e.Message));
          Restore();
          return false;
        }
      }
      return true;
    }

    /// <summary>
    /// Append the given file record to the end of the .dat file.  Called only during creates; probably unnecessary.
    /// </summary>
    internal void Append(FileRecord r)
    {
      if (!File.Exists(FileName))
        using (FileStream fNew = new FileStream(FileName, FileMode.Create, FileAccess.Write))
        {
          WriteFileHeader(fNew, 0); // unknown count
        }
      using (FileStream f = new FileStream(FileName, FileMode.Append, FileAccess.Write))
        r.Write(f);
    }

    private void Write(bool logTime = true)
    {
      try
      {
        Stopwatch sw = new Stopwatch();
        sw.Start();
        using (FileStream f = new FileStream(FileName, FileMode.OpenOrCreate, FileAccess.Write))
        {
          WriteFileHeader(f, (UInt32)files.Count);
          foreach (FileRecord r in files.Values)
            r.Write(f);
          f.SetLength(f.Position);
          f.Close();
        }
        deletes.Clear(); // no deleted records now that we've re-writen the while file from scratch
        if (logTime)
          LogFile.Log(String.Format("{0} saved in {1}ms", FileName, sw.ElapsedMilliseconds));
      }
      catch (Exception e)
      {
        LogFile.Error(String.Format("Could not save {0}: {1}", FileName, e.Message));
        // try to delete it in case it got partly saved
        try
        {
          File.Delete(FileName);
        }
        catch { } // hide any errors trying to delete it
        throw new MetaFileSaveException(FileName, e);
      }
    }

    private void WriteFileHeader(FileStream f, UInt32 count)
    {
      FileRecord.WriteUInt32(f, META_FILE_VERSION);
      FileRecord.WriteUInt32(f, count);
    }


  }

}
