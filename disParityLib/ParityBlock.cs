using System;
using System.Collections.Generic;
using System.Text;

namespace disParity
{

  internal class ParityBlock
  {

    private byte[] data;
    private Parity parity;

    public ParityBlock(Parity parity)
    {
      this.parity = parity;
      data = new byte[Parity.BlockSize];
    }

    public byte[] Data { get { return data; } }

    public void Clear()
    {
      Array.Clear(data, 0, data.Length);
    }

    public void Load(UInt32 block)
    {
      parity.ReadBlock(block, data);
    }

    public void Add(byte[] data)
    {
      Utils.FastXOR(this.data, data);
    }

    public void Write(UInt32 block)
    {
      parity.WriteBlock(block, data);
    }

  }

}
