﻿//
// Copyright (c) 2008-2009, Kenneth Bell
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
using System.Collections.Generic;
using System.Security.AccessControl;

namespace DiscUtils.Registry
{
    /// <summary>
    /// The per-key flags present on registry keys.
    /// </summary>
    [Flags]
    public enum RegistryKeyFlags : short
    {
        /// <summary>
        /// Unknown purpose.
        /// </summary>
        Unknown_0001 = 0x0001,

        /// <summary>
        /// Unknown purpose.
        /// </summary>
        Unknown_0002 = 0x0002,

        /// <summary>
        /// The key is the root key in the registry hive.
        /// </summary>
        Root = 0x0004,

        /// <summary>
        /// Unknown purpose.
        /// </summary>
        Unknown_0008 = 0x0008,

        /// <summary>
        /// The key is a link to another key.
        /// </summary>
        Link = 0x0010,

        /// <summary>
        /// This is a normal key.
        /// </summary>
        Normal = 0x0020,

        /// <summary>
        /// Unknown purpose.
        /// </summary>
        Unknown_0040 = 0x0040,

        /// <summary>
        /// Unknown purpose.
        /// </summary>
        Unknown_0080 = 0x0080,

        /// <summary>
        /// Unknown purpose.
        /// </summary>
        Unknown_0100 = 0x0100,

        /// <summary>
        /// Unknown purpose.
        /// </summary>
        Unknown_0200 = 0x0200,

        /// <summary>
        /// Unknown purpose.
        /// </summary>
        Unknown_0400 = 0x0400,

        /// <summary>
        /// Unknown purpose.
        /// </summary>
        Unknown_0800 = 0x0800,

        /// <summary>
        /// Unknown purpose.
        /// </summary>
        Unknown_1000 = 0x1000,

        /// <summary>
        /// Unknown purpose.
        /// </summary>
        Unknown_2000 = 0x2000,

        /// <summary>
        /// Unknown purpose.
        /// </summary>
        Unknown_4000 = 0x4000,

        /// <summary>
        /// Unknown purpose.
        /// </summary>
        Unknown_8000 = unchecked((short)0x8000)
    }

    /// <summary>
    /// The types of registry values.
    /// </summary>
    public enum RegistryValueType : int
    {
        /// <summary>
        /// Unknown type.
        /// </summary>
        None = 0x00,

        /// <summary>
        /// Unicode string.
        /// </summary>
        String = 0x01,

        /// <summary>
        /// String containing environment variables.
        /// </summary>
        ExpandString = 0x02,

        /// <summary>
        /// Binary data.
        /// </summary>
        Binary = 0x03,

        /// <summary>
        /// 32-bit integer.
        /// </summary>
        Dword = 0x04,

        /// <summary>
        /// 32-bit integer.
        /// </summary>
        DwordBigEndian = 0x05,

        /// <summary>
        /// Link.
        /// </summary>
        Link = 0x06,

        /// <summary>
        /// A multistring.
        /// </summary>
        MultiString = 0x07,

        /// <summary>
        /// An unknown binary format.
        /// </summary>
        ResourceList = 0x08,

        /// <summary>
        /// An unknown binary format.
        /// </summary>
        FullResourceDescriptor = 0x09,

        /// <summary>
        /// An unknown binary format.
        /// </summary>
        ResourceRequirementsList = 0x0A,

        /// <summary>
        /// A 64-bit integer.
        /// </summary>
        QWord = 0x0B,
    }

    [Flags]
    internal enum ValueFlags : ushort
    {
        Named = 0x0001,
        Unknown_0002 = 0x0002,
        Unknown_0004 = 0x0004,
        Unknown_0008 = 0x0008,
        Unknown_0010 = 0x0010,
        Unknown_0020 = 0x0020,
        Unknown_0040 = 0x0040,
        Unknown_0080 = 0x0080,
        Unknown_0100 = 0x0100,
        Unknown_0200 = 0x0200,
        Unknown_0400 = 0x0400,
        Unknown_0800 = 0x0800,
        Unknown_1000 = 0x1000,
        Unknown_2000 = 0x2000,
        Unknown_4000 = 0x4000,
        Unknown_8000 = 0x8000
    }


    internal abstract class Cell : IByteArraySerializable
    {
        public Cell()
        {
        }

        internal static Cell Parse(byte[] buffer, int pos)
        {
            string type = Utilities.BytesToString(buffer, pos, 2);

            Cell result = null;

            switch(type)
            {
                case "nk":
                    result = new KeyNodeCell();
                    break;

                case "sk":
                    result = new SecurityCell();
                    break;

                case "vk":
                    result = new ValueCell();
                    break;

                case "lh":
                case "lf":
                    result = new SubKeyHashedListCell();
                    break;

                case "ri":
                    result = new SubKeyIndirectListCell();
                    break;

                default:
                    Console.WriteLine("Unknown cell type: {0:X2} {1:X2}", buffer[pos], buffer[pos + 1]);
                    return null;
                    //throw new NotImplementedException("Unknown cell type '" + type + "'");
            }

            result.ReadFrom(buffer, pos);
            return result;
        }

        #region IByteArraySerializable Members

        public abstract void ReadFrom(byte[] buffer, int offset);
        public abstract void WriteTo(byte[] buffer, int offset);
        public abstract int Size
        {
            get;
        }

        #endregion
    }

    internal class KeyNodeCell : Cell
    {
        private RegistryKeyFlags _flags;
        private DateTime _timestamp;
        private int _parentIndex;
        private int _numSubKeys;
        private int _subKeysIndex;
        private int _numValues;
        private int _valueListIndex;
        private int _securityIndex;
        private int _classNameIndex;
        private int _classNameLength;
        private string _name;

        public RegistryKeyFlags Flags
        {
            get { return _flags; }
        }

        public DateTime Timestamp
        {
            get { return _timestamp; }
        }

        public int ParentIndex
        {
            get { return _parentIndex; }
        }

        public int NumSubKeys
        {
            get { return _numSubKeys; }
        }

        public int SubKeysIndex
        {
            get { return _subKeysIndex; }
        }

        public int NumValues
        {
            get { return _numValues; }
        }

        public int ValueListIndex
        {
            get { return _valueListIndex; }
        }

        public int SecurityIndex
        {
            get { return _securityIndex; }
        }

        public int ClassNameIndex
        {
            get { return _classNameIndex; }
        }

        public int ClassNameLength
        {
            get { return _classNameLength; }
        }

        public string Name
        {
            get { return _name; }
        }

        public override void ReadFrom(byte[] buffer, int offset)
        {
            _flags = (RegistryKeyFlags)Utilities.ToUInt16LittleEndian(buffer, offset + 0x02);
            _timestamp = DateTime.FromFileTimeUtc(Utilities.ToInt64LittleEndian(buffer, offset + 0x04));
            _parentIndex = Utilities.ToInt32LittleEndian(buffer, offset + 0x10);
            _numSubKeys = Utilities.ToInt32LittleEndian(buffer, offset + 0x14);
            _subKeysIndex = Utilities.ToInt32LittleEndian(buffer, offset + 0x1C);
            _numValues = Utilities.ToInt32LittleEndian(buffer, offset + 0x24);
            _valueListIndex = Utilities.ToInt32LittleEndian(buffer, offset + 0x28);
            _securityIndex = Utilities.ToInt32LittleEndian(buffer, offset + 0x2C);
            _classNameIndex = Utilities.ToInt32LittleEndian(buffer, offset + 0x30);
            int nameLength = Utilities.ToInt16LittleEndian(buffer, offset + 0x48);
            _classNameLength = Utilities.ToInt16LittleEndian(buffer, offset + 0x4A);
            _name = Utilities.BytesToString(buffer, offset + 0x4C, nameLength);
        }

        public override void WriteTo(byte[] buffer, int offset)
        {
            throw new NotImplementedException();
        }

        public override int Size
        {
            get { throw new NotImplementedException(); }
        }

        public override string ToString()
        {
            return "Key:" + _name + "[" + _flags + "] <" + _timestamp + ">";
        }
    }

    internal class SubKeyIndirectListCell : Cell
    {
        private int _numElements;
        private int[] _listIndexes;

        public int ListCount
        {
            get { return _numElements; }
        }

        public IEnumerable<int> Lists
        {
            get { return _listIndexes; }
        }

        public override void ReadFrom(byte[] buffer, int offset)
        {
            _numElements = Utilities.ToInt16LittleEndian(buffer, offset + 2);

            _listIndexes = new int[_numElements];
            for (int i = 0; i < _numElements; ++i)
            {
                _listIndexes[i] = Utilities.ToInt32LittleEndian(buffer, offset + 0x4 + (i * 0x4));
            }

        }

        public override void WriteTo(byte[] buffer, int offset)
        {
            throw new NotImplementedException();
        }

        public override int Size
        {
            get { throw new NotImplementedException(); }
        }
    }

    internal class SubKeyHashedListCell : Cell
    {
        private string _hashType;
        private int _numElements;
        private int[] _subKeyIndexes;
        private uint[] _nameHashes;

        public int SubKeyCount
        {
            get { return _numElements; }
        }

        public IEnumerable<int> SubKeys
        {
            get { return _subKeyIndexes; }
        }

        public override void ReadFrom(byte[] buffer, int offset)
        {
            _hashType = Utilities.BytesToString(buffer, offset, 2);
            _numElements = Utilities.ToInt16LittleEndian(buffer, offset + 2);

            _subKeyIndexes = new int[_numElements];
            _nameHashes = new uint[_numElements];
            for (int i = 0; i < _numElements; ++i)
            {
                _subKeyIndexes[i] = Utilities.ToInt32LittleEndian(buffer, offset + 0x4 + (i * 0x8));
                _nameHashes[i] = Utilities.ToUInt32LittleEndian(buffer, offset + 0x4 + (i * 0x8) + 0x4);
            }

        }

        public override void WriteTo(byte[] buffer, int offset)
        {
            throw new NotImplementedException();
        }

        public override int Size
        {
            get { throw new NotImplementedException(); }
        }

        internal int Find(string name, int start)
        {
            if (_hashType == "lh")
            {
                return FindByHash(name, start);
            }
            else
            {
                return FindByPrefix(name, start);
            }
        }

        private int FindByHash(string name, int start)
        {
            uint hash = 0;
            for (int i = 0; i < name.Length; ++i)
            {
                hash *= 37;
                hash += char.ToUpper(name[i]);
            }

            for (int i = start; i < _nameHashes.Length; ++i)
            {
                if (_nameHashes[i] == hash)
                {
                    return _subKeyIndexes[i];
                }
            }

            return -1;
        }

        private int FindByPrefix(string name, int start)
        {
            throw new NotImplementedException();
        }
    }

    internal class SecurityCell : Cell
    {
        private int _prevIndex;
        private int _nextIndex;
        private int _usageCount;
        private RegistrySecurity _secDesc;

        public int PreviousIndex
        {
            get { return _prevIndex; }
        }

        public int NextIndex
        {
            get { return _nextIndex; }
        }

        public int UsageCount
        {
            get { return _usageCount; }
        }

        public RegistrySecurity SecurityDescriptor
        {
            get { return _secDesc; }
        }

        public override void ReadFrom(byte[] buffer, int offset)
        {
            _prevIndex = Utilities.ToInt32LittleEndian(buffer, offset + 0x04);
            _nextIndex = Utilities.ToInt32LittleEndian(buffer, offset + 0x08);
            _usageCount = Utilities.ToInt32LittleEndian(buffer, offset + 0x0C);
            int secDescSize = Utilities.ToInt32LittleEndian(buffer, offset + 0x10);

            byte[] secDesc = new byte[secDescSize];
            Array.Copy(buffer, offset + 0x14, secDesc, 0, secDescSize);
            _secDesc = new RegistrySecurity();
            _secDesc.SetSecurityDescriptorBinaryForm(secDesc);
        }

        public override void WriteTo(byte[] buffer, int offset)
        {
            throw new NotImplementedException();
        }

        public override int Size
        {
            get { throw new NotImplementedException(); }
        }

        public override string ToString()
        {
            return "SecDesc:" + _secDesc.GetSecurityDescriptorSddlForm(AccessControlSections.All) + " (refCount:" + _usageCount + ")";
        }
    }

    internal class ValueCell : Cell
    {
        private int _dataLength;
        private int _dataIndex;
        private RegistryValueType _type;
        private ValueFlags _flags;
        private string _name;

        public int DataLength
        {
            get { return _dataLength; }
        }

        public int DataIndex
        {
            get { return _dataIndex; }
        }

        public RegistryValueType Type
        {
            get { return _type; }
        }

        public ValueFlags Flags
        {
            get { return _flags; }
        }

        public string Name
        {
            get { return _name; }
        }

        public override void ReadFrom(byte[] buffer, int offset)
        {
            int nameLen = Utilities.ToUInt16LittleEndian(buffer, offset + 0x02);
            _dataLength = Utilities.ToInt32LittleEndian(buffer, offset + 0x04);
            _dataIndex = Utilities.ToInt32LittleEndian(buffer, offset + 0x08);
            _type = (RegistryValueType)Utilities.ToInt32LittleEndian(buffer, offset + 0x0C);
            _flags = (ValueFlags)Utilities.ToUInt16LittleEndian(buffer, offset + 0x10);

            if ((_flags & ValueFlags.Named) != 0)
            {
                _name = Utilities.BytesToString(buffer, offset + 0x14, nameLen).Trim('\0');
            }
        }

        public override void WriteTo(byte[] buffer, int offset)
        {
            throw new NotImplementedException();
        }

        public override int Size
        {
            get { throw new NotImplementedException(); }
        }
    }
}