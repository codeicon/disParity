using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.IO;

namespace disParity
{

  public class FileRecord
  {

    private static byte[] dummyHash = new byte[16];

    private string root;

    private FileRecord(string root)
    {
      StartBlock = 0;
      this.root = root;
    }

    /// <summary>
    /// Returns the full path to the file, minus the drive root
    /// </summary>
    internal string Name { get; set; }

    internal long Length { get; private set; }

    internal DateTime CreationTime { get; private set; }

    internal DateTime LastWriteTime { get; set; }

    internal FileAttributes Attributes { get; private set; }

    internal UInt32 StartBlock { get; set; }

    /// <summary>
    /// The position (file offset) of this record in the .dat file
    /// </summary>
    internal UInt32 Position { get; set; }

    /// <summary>
    /// The length of this record in the .dat file
    /// </summary>
    internal UInt16 RecLength { get; set; }

    internal byte[] HashCode { get; set; }

    internal bool Seen { get; set; } // whether file was "seen" this scan

    internal bool Deleted { get; private set; } // whether this file record has been marked as deleted

    private static string StripRoot(string root, string path)
    {
      if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        return path;
      path = path.Remove(0, root.Length);
      while (path[0] == Path.DirectorySeparatorChar)
        path = path.Remove(0, 1);
      return path;
    }

    internal FileRecord(FileInfo info, string path, string root)
      : this(root)
    {
      Initialize(info, Utils.MakeFullPath(path, info.Name), root);
    }

    internal FileRecord(string filePath, string root)
      : this(root)
    {
      FileInfo fi = new FileInfo(filePath);
      Initialize(fi, Utils.StripRoot(root, filePath), root);
    }

    internal FileRecord(FileInfo info, string root)
      : this(root)
    {
      Initialize(info, Utils.StripRoot(root, info.FullName), root);
    }

    private void Initialize(FileInfo info, string name, string root)
    {
      Name = name;
      Length = info.Length;
      Attributes = info.Attributes;
      CreationTime = info.CreationTime;
      LastWriteTime = info.LastWriteTime;
      StartBlock = 0;
    }

    internal bool RefreshAttributes()
    {
      if (!File.Exists(FullPath))
        return false;
      try
      {
        FileInfo info = new FileInfo(FullPath);
        Length = info.Length;
        Attributes = info.Attributes;
        CreationTime = info.CreationTime;
        LastWriteTime = info.LastWriteTime;
      }
      catch
      {
        return false;
      }
      return true;
    }

    internal static FileRecord LoadFromOldVerion(Stream f, string root, UInt32 version)
    {
      FileRecord rec = new FileRecord(root);
      if (version == 1)
      {
        rec.Name = ReadString(f);
        if (rec.Name.Length == 0)
          return null;
        rec.Length = ReadLong(f);
        rec.Attributes = (FileAttributes)ReadUInt32(f);
        rec.CreationTime = ReadDateTime(f);
        rec.LastWriteTime = ReadDateTime(f);
        rec.StartBlock = ReadUInt32(f);
        rec.HashCode = new byte[16];
        if (f.Read(rec.HashCode, 0, 16) != 16)
          return null;
        if (rec.Name[0] == '\\')
          rec.Name = rec.Name.TrimStart('\\');
        return rec;
      }
      else
        throw new Exception(String.Format("Unsupport file version {0}", version));
    }

    internal static FileRecord LoadFromFile(FileStream f, string root)
    {
      FileRecord rec = new FileRecord(root);
      rec.Position = (UInt32)f.Position;
      UInt16 mask = ReadUInt16(f);
      bool deleted = (mask & 0x8000) != 0;
      rec.RecLength = (UInt16)(mask & 0x0fff);
      if (deleted)
      {
        rec.Deleted = true;
        f.Position += rec.RecLength - 2;
        return rec;
      }
      rec.Name = ReadString(f);
      if (rec.Name.Length == 0)
        return null;
      rec.Length = ReadLong(f);
      rec.Attributes = (FileAttributes)ReadUInt32(f);
      rec.CreationTime = ReadDateTime(f);
      rec.LastWriteTime = ReadDateTime(f);
      rec.StartBlock = ReadUInt32(f);
      rec.HashCode = new byte[16];
      if (f.Read(rec.HashCode, 0, 16) != 16)
        return null;
      if (rec.Name[0] == '\\')
        rec.Name = rec.Name.TrimStart('\\');

      return rec;
    }

    internal MemoryStream Encode()
    {
      Debug.Assert(!Deleted);
      MemoryStream ms = new MemoryStream(256);
      WriteUInt16(ms, 0); // reserve space for header
      WriteString(ms, Name);
      WriteLong(ms, Length);
      WriteUInt32(ms, (UInt32)Attributes);
      WriteDateTime(ms, CreationTime);
      WriteDateTime(ms, LastWriteTime);
      WriteUInt32(ms, StartBlock);
      if (Length == 0 || HashCode == null)
        ms.Write(dummyHash, 0, 16);
      else
        ms.Write(HashCode, 0, 16);
      Debug.Assert(ms.Length <= 0xfff);
      RecLength = (UInt16)ms.Length;
      // go back and write record length
      ms.Position = 0;
      WriteUInt16(ms, RecLength);
      return ms;
    }

    internal void Write(Stream f)
    {
      using (MemoryStream ms = Encode())
        ms.WriteTo(f);
    }

    /// <summary>
    /// Attempts to detect whether the file has been modified by checking the length and LastWriteTime
    /// </summary>
    internal bool Modified
    {
      get
      {
        if (!File.Exists(FullPath))
          return true; // a deleted file is considered modified
        FileInfo info = new FileInfo(FullPath);
        return (info.Length != Length) || (info.LastWriteTime != LastWriteTime);
      }
    }

    /// <summary>
    /// Returns the full path to the file including the root portion
    /// </summary>
    public string FullPath
    {
      get
      {
        return Utils.MakeFullPath(root, Name);
      }
    }

    internal static int CompareByStartBlock(FileRecord r1, FileRecord r2)
    {
      if (r1.StartBlock < r2.StartBlock)
        return -1;
      else
        return 1;
    }

    internal bool ContainsBlock(UInt32 block)
    {
      UInt32 endBlock = StartBlock + LengthInBlocks;
      if (block >= StartBlock && block < endBlock)
        return true;
      else
        return false;
    }

    internal UInt32 LengthInBlocks
    {
      get
      {
        return Parity.LengthInBlocks(Length);
      }
    }

    static string ReadString(Stream f)
    {
      int length = ReadUInt16(f);
      byte[] data = new byte[length];
      if (f.Read(data, 0, length) != length)
        throw new Exception(String.Format("FileRecord.ReadString failed (expected={0})", length));
      return Encoding.UTF8.GetString(data);
    }

    static void WriteString(Stream f, string s)
    {
      byte[] data = Encoding.UTF8.GetBytes(s);
      WriteUInt16(f, (UInt16)data.Length);
      f.Write(data, 0, data.Length);
    }

    static byte ReadByte(Stream f)
    {
      Int32 result = f.ReadByte();
      if (result == -1)
        throw new Exception("Unexpected EOF in ReadByte");
      return (byte)result;
    }

    static UInt16 ReadUInt16(Stream f)
    {
      UInt16 result = ReadByte(f);
      result <<= 8;
      result += ReadByte(f);
      return result;
    }

    internal static void WriteUInt16(Stream f, UInt16 n)
    {
      f.WriteByte((Byte)(n >> 8));
      f.WriteByte((Byte)(n & 0xff));
    }

    internal static UInt32 ReadUInt32(Stream f)
    {
      UInt32 result = ReadByte(f);
      for (int i = 0; i < 3; i++)
      {
        result <<= 8;
        result += ReadByte(f);
      }
      return result;
    }

    internal static void WriteUInt32(Stream f, UInt32 n)
    {
      f.WriteByte((Byte)(n >> 24));
      f.WriteByte((Byte)((n & 0xff0000) >> 16));
      f.WriteByte((Byte)((n & 0xff00) >> 8));
      f.WriteByte((Byte)(n & 0xff));
    }

    static DateTime ReadDateTime(Stream f)
    {
      long bits = ReadLong(f);
      return DateTime.FromBinary(bits);
    }

    static void WriteDateTime(Stream f, DateTime dateTime)
    {
      WriteLong(f, dateTime.ToBinary());
    }

    static void WriteLong(Stream f, long n)
    {
      byte[] data = BitConverter.GetBytes(n);
      f.Write(data, 0, data.Length);
    }

    static long ReadLong(Stream f)
    {
      byte[] data = new byte[8];
      f.Read(data, 0, 8);
      return BitConverter.ToInt64(data, 0);
    }

  }
}
