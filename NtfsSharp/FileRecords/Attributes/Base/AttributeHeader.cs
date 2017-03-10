﻿using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NtfsSharp.FileRecords.Attributes.Base
{
    public abstract class AttributeHeader : AttributeBase
    {
        public static uint HeaderSize => (uint)Marshal.SizeOf<NTFS_ATTRIBUTE_HEADER>();

        public NTFS_ATTRIBUTE_HEADER Header { get; private set; }
        public string Name { get; private set; }

        /// <summary>
        /// Constructor for AttributeBase
        /// </summary>
        /// <param name="header">Header of attribute</param>
        /// <param name="data">Bytes of data (including header)</param>
        protected AttributeHeader(NTFS_ATTRIBUTE_HEADER header, byte[] data) : base(data)
        {
            Header = header;
            CurrentOffset += HeaderSize;
        }

        /// <summary>
        /// Reads the name (if NameLength > 0)
        /// </summary>
        protected void ReadName()
        {
            if (Header.NameLength <= 0)
                return;

            Name = Encoding.Unicode.GetString(GetBytesFromCurrentOffset(Header.Length * 2));
            CurrentOffset += (uint) Header.NameLength * 2;
        }
        
        public enum NTFS_ATTR_TYPE : uint
        {
            STANDARD_INFORMATION = 0x10,
            ATTRIBUTE_LIST = 0x20,
            FILE_NAME = 0x30,
            OBJECT_ID = 0x40,
            SECURITY_DESCRIPTOR = 0x50,
            VOLUME_NAME = 0x60,
            VOLUME_INFORMATION = 0x70,
            DATA = 0x80,
            INDEX_ROOT = 0x90,
            INDEX_ALLOCATION = 0xA0,
            BITMAP = 0xB0,
            REPARSE_POINT = 0xC0,
            EA_INFORMATION = 0xD0,
            EA = 0xE0,
            PROPERTY_SET = 0xF0,
            LOGGED_UTILITY_STREAM = 0x100
        }

        [Flags]
        public enum Flags : ushort
        {
            IsCompressed = 1 << 0,
            // Skip 13 bits
            IsEncrypted = 1 << 14,
            IsSparse = 1 << 15
        }

        public struct NTFS_ATTRIBUTE_HEADER
        {
            public readonly NTFS_ATTR_TYPE Type;
            public readonly uint Length;
            [MarshalAs(UnmanagedType.U1)]
            public readonly bool NonResident;
            public readonly byte NameLength;
            public readonly ushort NameOffset;
            public readonly Flags Flags;
            public readonly ushort AttributeID;
        }
    }
}