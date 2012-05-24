using System;
using System.Collections.Generic;
using System.Text;

namespace disParity
{

  internal class ParityBlock
  {

    private byte[] data;

    public ParityBlock()
    {
      data = new byte[Parity.BlockSize];
    }

    public byte[] Data { get { return data; } }

    public void Clear()
    {
      Array.Clear(data, 0, data.Length);
    }

    public void Load(UInt32 block)
    {
      Parity.ReadBlock(block, data);
    }

    public void Add(byte[] data)
    {
      Parity.FastXOR(this.data, data);
    }

    public void Write(UInt32 block)
    {
      Parity.WriteBlock(block, data);
    }

  }

}
