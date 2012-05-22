using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace disParity
{

  public enum FileStatus
  {
    Unknown,
    HashVerified,
    HashFailed
  }

  public class FileRecord
  {
    public string name;
    public long length;
    public FileAttributes attributes;
    public DateTime creationTime;
    public DateTime lastWriteTime;
    public UInt32 startBlock;
    public byte[] hashCode;
    public bool exists;
    public bool seen;
    public bool skipped; // skipped during a create because of an error when opening
    public Int32 drive;
    public FileRecord replacement; // for edited files, this is the new
                                   // record that will replace the old one
    public FileStatus status;

    static byte[] dummyHash = new byte[16];

    private FileRecord()
    {
      startBlock = 0;
      exists = true;
      status = FileStatus.Unknown;
      skipped = false;
    }

    static string StripRoot(string root, string path)
    {
      if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        return path;
      path = path.Remove(0, root.Length);
      while (path[0] == Path.DirectorySeparatorChar)
        path = path.Remove(0, 1);
      return path;
    }

    public FileRecord(FileInfo info, string path = "", Int32 drive = -1)
    {
      // what was this for?
      //if (path == "")
      //  path = info.FullName;
      if (Path.IsPathRooted(info.Name))
        name = StripRoot(path, info.Name);
      else
        name = Program.MakeFullPath(path, info.Name);
      length = info.Length;
      attributes = info.Attributes;
      creationTime = info.CreationTime;
      lastWriteTime = info.LastWriteTime;
      startBlock = 0;
      exists = true;
      this.drive = drive;
      status = FileStatus.Unknown;
      skipped = false;
    }

    public bool Refresh(string fullPath)
    {
      if (!File.Exists(fullPath))
        return false;
      FileInfo info = new FileInfo(fullPath);
      length = info.Length;
      attributes = info.Attributes;
      creationTime = info.CreationTime;
      lastWriteTime = info.LastWriteTime;
      return true;
    }

    public static FileRecord LoadFromFile(FileStream f)
    {
      FileRecord rec = new FileRecord();
      rec.name = ReadString(f);
      rec.length = ReadLong(f);
      rec.attributes = (FileAttributes)ReadUInt32(f);
      rec.creationTime = ReadDateTime(f);
      rec.lastWriteTime = ReadDateTime(f);
      rec.startBlock = ReadUInt32(f);
      rec.hashCode = new byte[16];
      rec.seen = false;
      if (f.Read(rec.hashCode, 0, 16) != 16)
        throw new Exception(String.Format("Could not read from {0}", f.Name));
      if (rec.name[0] == '\\')
        rec.name = rec.name.TrimStart('\\');

      return rec;
    }

    public void WriteToFile(FileStream f)
    {
      WriteString(f, name);
      WriteLong(f, length);
      WriteUInt32(f, (UInt32)attributes);
      WriteDateTime(f, creationTime);
      WriteDateTime(f, lastWriteTime);
      WriteUInt32(f, startBlock);
      if (length == 0)
        f.Write(dummyHash, 0, 16);
      else
        f.Write(hashCode, 0, 16);
    }

    public long RecordSize
    {
      get
      {
        return 2 + name.Length + 8 + 4 + 8 + 8 + 4 + 16;
      }
    }

    public static int CompareByStartBlock(FileRecord r1, FileRecord r2)
    {
      if (r1.startBlock < r2.startBlock)
        return -1;
      else
        return 1;
    }

    public bool ContainsBlock(UInt32 block)
    {
      UInt32 endBlock = startBlock + LengthInBlocks;
      if (block >= startBlock && block < endBlock)
        return true;
      else
        return false;
    }

    public UInt32 LengthInBlocks
    {
      get
      {
        UInt32 result = (UInt32)(length / Parity.BlockSize);
        if (result * Parity.BlockSize < length)
          result++;
        return result;
      }
    }

    static string ReadString(FileStream f)
    {
      int length = ReadUInt16(f);
      byte[] data = new byte[length];
      if (f.Read(data, 0, length) != length)
        throw new Exception(String.Format("Could not read from {0}", f.Name));
      return Encoding.UTF8.GetString(data);
    }

    static void WriteString(FileStream f, string s)
    {
      byte[] data = Encoding.UTF8.GetBytes(s);
      WriteUInt16(f, (UInt16)data.Length);
      f.Write(data, 0, data.Length);
    }

    static byte ReadByte(FileStream f)
    {
      Int32 result = f.ReadByte();
      if (result == -1)
        throw new Exception(String.Format("Could not read from {0}", f.Name));
      return (byte)result;
    }

    static UInt16 ReadUInt16(FileStream f)
    {
      UInt16 result = ReadByte(f);
      result <<= 8;
      result += ReadByte(f);
      return result;
    }

    static void WriteUInt16(FileStream f, UInt16 n)
    {
      f.WriteByte((Byte)(n >> 8));
      f.WriteByte((Byte)(n & 0xff));
    }

    public static UInt32 ReadUInt32(FileStream f)
    {
      UInt32 result = ReadByte(f);
      for (int i = 0; i < 3; i++) {
        result <<= 8;
        result += ReadByte(f);
      }
      return result;
    }

    public static void WriteUInt32(FileStream f, UInt32 n)
    {
      f.WriteByte((Byte)(n >> 24));
      f.WriteByte((Byte)((n & 0xff0000) >> 16));
      f.WriteByte((Byte)((n & 0xff00) >> 8));
      f.WriteByte((Byte)(n & 0xff));
    }

    static DateTime ReadDateTime(FileStream f)
    {
      long bits = ReadLong(f);
      return DateTime.FromBinary(bits);
    }

    static void WriteDateTime(FileStream f, DateTime dateTime)
    {
      WriteLong(f, dateTime.ToBinary());
    }

    static void WriteLong(FileStream f, long n)
    {
      byte[] data = BitConverter.GetBytes(n);
      f.Write(data, 0, data.Length);
    }

    static long ReadLong(FileStream f)
    {
      byte[] data = new byte[8];
      f.Read(data, 0, 8);
      return BitConverter.ToInt64(data, 0);
    }

  }
}
