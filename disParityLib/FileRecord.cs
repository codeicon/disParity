﻿using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace disParity
{

  public class FileRecord
  {
    private DataDrive drive;

    static byte[] dummyHash = new byte[16];

    private FileRecord()
    {
      StartBlock = 0;
    }

    /// <summary>
    /// Returns the full path to the file, minus the drive root
    /// </summary>
    public string Name { get; set; }

    public long Length { get; private set; }

    public DateTime CreationTime { get; private set; }

    public DateTime LastWriteTime { get; set; }

    public FileAttributes Attributes { get; private set; }

    public DataDrive Drive { get { return drive; } }

    public UInt32 StartBlock { get; set; }

    public byte[] HashCode { get; set; }

    static string StripRoot(string root, string path)
    {
      if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        return path;
      path = path.Remove(0, root.Length);
      while (path[0] == Path.DirectorySeparatorChar)
        path = path.Remove(0, 1);
      return path;
    }

    public FileRecord(FileInfo info, string path, DataDrive drive)
    {
      Initialize(info, Utils.MakeFullPath(path, info.Name), drive);
    }

    public FileRecord(string filePath, DataDrive drive)
    {
      FileInfo fi = new FileInfo(filePath);
      Initialize(fi, Utils.StripRoot(drive.Root, filePath), drive);
    }

    private void Initialize(FileInfo info, string name, DataDrive drive)
    {
      Name = name;
      Length = info.Length;
      Attributes = info.Attributes;
      CreationTime = info.CreationTime;
      LastWriteTime = info.LastWriteTime;
      StartBlock = 0;
      this.drive = drive;
    }

    public bool RefreshAttributes()
    {
      if (!File.Exists(FullPath))
        return false;
      try {
        FileInfo info = new FileInfo(FullPath);
        Length = info.Length;
        Attributes = info.Attributes;
        CreationTime = info.CreationTime;
        LastWriteTime = info.LastWriteTime;
      }
      catch {
        return false;
      }
      return true;
    }

    public static FileRecord LoadFromFile(FileStream f, DataDrive drive)
    {
      FileRecord rec = new FileRecord();
      rec.Name = ReadString(f);
      if (rec.Name.Length == 0)
        return null;
      rec.Length = ReadLong(f);
      rec.Attributes = (FileAttributes)ReadUInt32(f);
      rec.CreationTime = ReadDateTime(f);
      rec.LastWriteTime = ReadDateTime(f);
      rec.StartBlock = ReadUInt32(f);
      rec.HashCode = new byte[16];
      rec.drive = drive;
      if (f.Read(rec.HashCode, 0, 16) != 16)
        return null;
      if (rec.Name[0] == '\\')
        rec.Name = rec.Name.TrimStart('\\');

      return rec;
    }

    public void WriteToFile(FileStream f)
    {
      WriteString(f, Name);
      WriteLong(f, Length);
      WriteUInt32(f, (UInt32)Attributes);
      WriteDateTime(f, CreationTime);
      WriteDateTime(f, LastWriteTime);
      WriteUInt32(f, StartBlock);
      if (Length == 0)
        f.Write(dummyHash, 0, 16);
      else
        f.Write(HashCode, 0, 16);
    }

    /// <summary>
    /// Attempts to detect whether the file has been modified by checking the length and LastWriteTime
    /// </summary>
    public bool Modified
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
        return Utils.MakeFullPath(drive.Root, Name);
      }
    }

    public long RecordSize
    {
      get
      {
        return 2 + Encoding.UTF8.GetBytes(Name).Length + 8 + 4 + 8 + 8 + 4 + 16;
      }
    }

    public static int CompareByStartBlock(FileRecord r1, FileRecord r2)
    {
      if (r1.StartBlock < r2.StartBlock)
        return -1;
      else
        return 1;
    }

    public bool ContainsBlock(UInt32 block)
    {
      UInt32 endBlock = StartBlock + LengthInBlocks;
      if (block >= StartBlock && block < endBlock)
        return true;
      else
        return false;
    }

    public UInt32 LengthInBlocks
    {
      get
      {
        return Parity.LengthInBlocks(Length);
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
