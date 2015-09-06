﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace VitaDefiler.Modules
{
    public class Memory : IModule
    {
        public static readonly uint BLOCK_SIZE = 0x100;

        public bool Run(IDevice device, string cmd, string[] args)
        {
            Device dev = (Device)device;
            switch (cmd)
            {
                case "usbread":
                    if (args.Length >= 2)
                    {
                        USBRead(dev, args[0].ToVariable(dev).Data, args[1].ToDataSize(), args.Length > 2 ? args[2] : null);
                        return true;
                    }
                    break;
                case "read":
                    if (args.Length >= 1)
                    {
                        uint num;
                        Read(dev, args[0].ToVariable(dev).Data, args.Length > 1 ? args[1].ToDataSize() : args[0].ToVariable(dev).Size, out num, args.Length > 2 ? args[2] : null);
                        return true;
                    }
                    break;
                case "write":
                case "writecode":
                    bool forcecode = cmd == "writecode";
                    if (args.Length >= 3)
                    {
                        Variable var = args[0].ToVariable(dev);
                        Write(dev, var.Data, args[1].ToDataSize(), forcecode ? true : var.IsCode, 0, args[2]);
                        return true;
                    }
                    else if (args.Length == 2)
                    {
                        Variable var = args[0].ToVariable(dev);
                        Write(dev, var.Data, 0, forcecode ? true : var.IsCode, 0, args[1]);
                        return true;
                    }
                    break;
                case "allocate":
                case "alloc":
                    if (args.Length == 2)
                    {
                        Allocate(dev, args[1].ToDataSize(), args[0] == "code" ? true : false);
                        return true;
                    }
                    break;
                case "free":
                    if (args.Length == 1)
                    {
                        Free(dev, args[0].ToVariable(dev));
                        return true;
                    }
                    break;
            }
            return false;
        }

        public static void Read(IDevice device, uint addr, uint length, out uint num, string file = null)
        {
            Device dev = (Device)device;
            num = 0;
            if (addr == 0 || length == 0)
            {
                Defiler.ErrLine("Ignoring invalid read request. Are your params correct?");
                return;
            }
            try
            {
                FileStream fout = file != null ? File.Open(file, FileMode.Create) : null;
                byte[] data = new byte[BLOCK_SIZE];
                for (uint l = 0; l < length; l += BLOCK_SIZE)
                {
                    uint size = l + BLOCK_SIZE > length ? length % BLOCK_SIZE : BLOCK_SIZE;
                    Defiler.ErrLine("Dumping 0x{0:X}", addr + l);
                    if (dev.Network.RunCommand(Command.ReadData, new uint[]{addr+l, size}, out data) == Command.Error)
                    {
                        Defiler.ErrLine("Read failed.");
                        break;
                    }
                    if (file != null)
                    {
                        fout.Write(data, 0, (int)size);
                    }
                    else
                    {
                        num = BitConverter.ToUInt32(data, 0);
                        data.PrintHexDump(size, 16);
                    }
                }
                if (file != null)
                {
                    fout.Close();
                }
            }
            catch (IOException ex)
            {
                Defiler.ErrLine("Error writing to file: {0}", ex.Message);
            }
        }

        public static void USBRead(IDevice device, uint addr, uint length, string file = null)
        {
            Device dev = (Device)device;
            if (length == 0)
            {
                Defiler.ErrLine("Ignoring request to read 0 bytes. Are your params correct?");
                return;
            }
            FileStream fs = file == null ? null : File.OpenWrite(file);
            dev.Exploit.StartDump(addr, length, fs);
            if (fs != null)
            {
                fs.Close();
            }
        }

        public static void Write(IDevice device, uint addr, uint length, bool isCode, uint data = 0, string file = null)
        {
            Device dev = (Device)device;
            if (addr == 0)
            {
                Defiler.ErrLine("Ignoring invalid write request. Are your params correct?");
                return;
            }
            byte[] resp;
            if (file == null || !File.Exists(file))
            {
                if (dev.Network.RunCommand(isCode ? Command.WriteCode : Command.WriteData, new uint[] { addr, length, data }, out resp) == Command.Error)
                {
                    Defiler.ErrLine("Write failed.");
                }
                else
                {
                    Defiler.ErrLine("Wrote 0x{0:X} byte.", BitConverter.ToInt32(resp, 0));
                }
            }
            else
            {
                try
                {
                    FileStream fin = File.OpenRead(file);
                    byte[] buf = new byte[BLOCK_SIZE + 2 * sizeof(int)];
                    if (length == 0)
                    {
                        length = (uint)fin.Length;
                    }
                    for (uint l = 0; l < length; l += BLOCK_SIZE)
                    {
                        uint size = l + BLOCK_SIZE > length ? length % BLOCK_SIZE : BLOCK_SIZE;
                        int read = 0;
                        while (read < size)
                        {
                            if ((read += fin.Read(buf, 2 * sizeof(int) + read, (int)size - read)) == read)
                            {
                                size = (uint)read;
                                break;
                            }
                        }
                        if (size == 0)
                        {
                            break;
                        }
                        Defiler.ErrLine("Writing 0x{0:X}", addr + l);
                        Array.Copy(BitConverter.GetBytes(addr + l), 0, buf, 0, sizeof(int));
                        Array.Copy(BitConverter.GetBytes(size), 0, buf, sizeof(int), sizeof(int));
                        if (dev.Network.RunCommand(isCode ? Command.WriteCode : Command.WriteData, buf, out resp) == Command.Error)
                        {
                            Defiler.ErrLine("Write failed.");
                            break;
                        }
                        else
                        {
                            Defiler.ErrLine("Wrote 0x{0:X} byte.", BitConverter.ToInt32(resp, 0));
                        }
                    }
                    fin.Close();
                }
                catch (IOException ex)
                {
                    Defiler.ErrLine("Error writing to file: {0}", ex.Message);
                }
            }
        }

        public Variable Allocate(IDevice device, uint length, bool isCode)
        {
            Device dev = (Device)device;
            if (length == 0)
            {
                Defiler.ErrLine("Ignoring request to allocate 0 bytes. Are your params correct?");
                return Variable.Null;
            }
            uint addr = (uint)dev.Network.RunCommand(isCode ? Command.AllocateCode : Command.AllocateData, (int)length);
            if (addr > 0)
            {
                int idx = dev.CreateVariable(addr, length, isCode);
                dev.LastReturn = addr;
                return dev.Vars[idx];
            }
            else
            {
                Defiler.ErrLine("Allocate failed.");
                return Variable.Null;
            }
        }

        public void Free(IDevice device, Variable var)
        {
            Device dev = (Device)device;
            dev.Network.RunCommand(var.IsCode ? Command.FreeCode : Command.FreeData, (int)var.Data);
            dev.DeleteVariable(var);
        }
    }
}
