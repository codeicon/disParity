using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;

namespace disParity
{

  internal class ParityBlock
  {

    private byte[] data;
    private Parity parity;

    public ParityBlock(Parity parity)
    {
      this.parity = parity;
      data = new byte[Parity.BLOCK_SIZE];
    }

    public byte[] Data { get { return data; } set { data = value; } }

    public void Clear()
    {
      Array.Clear(data, 0, data.Length);
    }

    public bool Load(UInt32 block)
    {
      return parity.ReadBlock(block, data);
    }

    public void Add(byte[] data)
    {
      Utils.FastXOR(this.data, data);
    }

    public bool Write(UInt32 block)
    {
      return parity.WriteBlock(block, data);
    }

    public bool Equals(ParityBlock block)
    {
      for (int i = 0; i < Parity.BLOCK_SIZE; i++)
        if (data[i] != block.Data[i])
          return false;
      return true;
    }

  }

}
