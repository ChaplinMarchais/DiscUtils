﻿//
// Copyright (c) 2008, Kenneth Bell
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
//

using System;
using System.IO;
using System.Text;

namespace DiscUtils.Iso9660
{
    internal struct PathTableRecord
    {
        public byte ExtendedAttributeRecordLength;
        public uint LocationOfExtent;
        public ushort ParentDirectoryNumber;
        public string DirectoryIdentifier;

        public static int ReadFrom(byte[] src, int offset, bool byteSwap, Encoding enc, out PathTableRecord record)
        {
            byte directoryIdentifierLength = src[offset + 0];
            record.ExtendedAttributeRecordLength = src[offset + 1];
            record.LocationOfExtent = BitConverter.ToUInt32(src, offset + 2);
            record.ParentDirectoryNumber = BitConverter.ToUInt16(src, offset + 6);
            record.DirectoryIdentifier = IsoUtilities.ReadChars(src, offset + 8, directoryIdentifierLength, enc);

            if (byteSwap)
            {
                record.LocationOfExtent = IsoUtilities.ByteSwap(record.LocationOfExtent);
                record.ParentDirectoryNumber = IsoUtilities.ByteSwap(record.ParentDirectoryNumber);
            }

            return directoryIdentifierLength + 8 + (((directoryIdentifierLength & 1) == 1) ? 1 : 0);
        }

        internal void WriteTo(Stream stream, bool byteSwap, Encoding enc)
        {
            int nameBytes = enc.GetByteCount(DirectoryIdentifier);
            byte[] data = new byte[8 + nameBytes + (((nameBytes & 0x1) == 1) ? 1 : 0)];
            Write(byteSwap, enc, data, 0);
            stream.Write(data, 0, data.Length);
        }

        internal static uint CalcLength(string name, Encoding enc)
        {
            int nameBytes = enc.GetByteCount(name);
            return (uint)(8 + nameBytes + (((nameBytes & 0x1) == 1) ? 1 : 0));
        }

        internal int Write(bool byteSwap, Encoding enc, byte[] buffer, int offset)
        {
            int nameBytes = enc.GetByteCount(DirectoryIdentifier);

            buffer[offset + 0] = (byte)nameBytes;
            buffer[offset + 1] = ExtendedAttributeRecordLength;
            IsoUtilities.ToBytesFromUInt32(buffer, offset + 2, byteSwap ? IsoUtilities.ByteSwap(LocationOfExtent) : LocationOfExtent);
            IsoUtilities.ToBytesFromUInt16(buffer, offset + 6, byteSwap ? IsoUtilities.ByteSwap(ParentDirectoryNumber) : ParentDirectoryNumber);
            IsoUtilities.WriteString(buffer, offset + 8, nameBytes, false, DirectoryIdentifier, enc);
            if ((nameBytes & 1) == 1)
            {
                buffer[offset + 33 + nameBytes] = 0;
            }

            return (int)(8 + nameBytes + (((nameBytes & 0x1) == 1) ? 1 : 0));
        }
    }

}
