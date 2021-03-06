﻿using System;
using System.Linq;
using System.Runtime.InteropServices;
using NtfsSharp.Exceptions;
using NtfsSharp.Helpers;
using System.Collections.Generic;
using NtfsSharp.FileRecords.Attributes;
using NtfsSharp.FileRecords.Attributes.Base;
using NtfsSharp.FileRecords.Attributes.Base.NonResident;
using NtfsSharp.FileRecords.Attributes.Shared;

namespace NtfsSharp.FileRecords
{
    /// <summary>
    /// Represents a FILE record
    /// </summary>
    public class FileRecord : Fixupable, IComparer<FileRecord>, IComparable<FileRecord>, IEquatable<FileRecord>
    {
        private uint _currentOffset;
        private readonly byte[] _data;
        private bool _hasReadAttributes = false;

        public readonly Volume Volume;

        public FILE_RECORD_HEADER_NTFS Header { get; private set; }
        public readonly List<AttributeBase> Attributes = new List<AttributeBase>();

        

        public string Filename
        {
            get
            {
                var defaultFilename = string.Empty;

                foreach (
                    var attr in
                    FindAttributesByType(AttributeHeaderBase.NTFS_ATTR_TYPE.FILE_NAME))
                {
                    var fileNameAttr = attr.Body as FileNameAttribute;

                    if (fileNameAttr == null)
                        continue;

                    if (fileNameAttr.FileName.Data.Namespace != FileName.NTFS_NAMESPACE.Dos)
                        return fileNameAttr.FileName.Filename;

                    defaultFilename = fileNameAttr.FileName.Filename;
                }

                return defaultFilename;
            }
        }

        public DataStream FileStream
        {
            get
            {
                var dataAttr = FindAttributeByType(AttributeHeaderBase.NTFS_ATTR_TYPE.DATA);

                if (dataAttr == null)
                    return null;

                return new DataStream(dataAttr);
            }
        }

        /// <summary>
        /// Reads file record from bytes
        /// </summary>
        /// <param name="data">Bytes with data for file record and attributes</param>
        /// <param name="vol">Volume containing file record</param>
        /// <exception cref="ArgumentNullException">Thrown if Volume is null</exception>
        /// <exception cref="InvalidFileRecordException">Thrown if unable to read file record</exception>
        public FileRecord(byte[] data, Volume vol)
        {
            if (vol == null)
                throw new ArgumentNullException(nameof(vol), "Volume cannot be null");

            Volume = vol;
            _data = data;

            ParseHeader();
            if (!Fixup(_data, Header.UpdateSequenceOffset, Header.UpdateSequenceSize, Volume.BytesPerSector, out short invalidSector))
                throw new InvalidFileRecordException(nameof(EndTag), $"Last 2 bytes of sector {invalidSector} don't match update sequence array end tag.", this);
        }

        /// <summary>
        /// Reads a file record at specified number in the volume
        /// </summary>
        /// <param name="recordNum">File record number</param>
        /// <param name="vol">Volume containing file record</param>
        /// <exception cref="ArgumentNullException">Thrown if Volume is null</exception>
        /// <exception cref="InvalidFileRecordException">Thrown if unable to read file record</exception>
        public FileRecord(ulong recordNum, Volume vol)
        {
            if (vol == null)
                throw new ArgumentNullException(nameof(vol), "Volume cannot be null");

            vol.Disk.Move(vol.LcnToOffset(vol.BootSector.MFTLCN) + vol.BytesPerFileRecord * recordNum);
            var data = vol.Disk.ReadFile(vol.BytesPerFileRecord);

            Volume = vol;
            _data = data;

            ParseHeader();
            if (!Fixup(_data, Header.UpdateSequenceOffset, Header.UpdateSequenceSize, Volume.BytesPerSector, out short invalidSector))
                throw new InvalidFileRecordException(nameof(EndTag), $"Last 2 bytes of sector {invalidSector} don't match update sequence array end tag.", this);
        }

        /// <summary>
        /// Parses the file record
        /// </summary>
        /// <exception cref="InvalidFileRecordException">Thrown if magic number is not FILE</exception>
        private void ParseHeader()
        {
            Header = _data.ToStructure<FILE_RECORD_HEADER_NTFS>();
            _currentOffset = (uint)Marshal.SizeOf<FILE_RECORD_HEADER_NTFS>();

            if (!Header.Magic.SequenceEqual(new byte[] { 0x46, 0x49, 0x4C, 0x45 }))
                throw new InvalidFileRecordException(nameof(Header.Magic), this);

            if (Header.UpdateSequenceSize - 1 > Volume.SectorsPerMFTRecord)
                throw new InvalidFileRecordException(nameof(Header.UpdateSequenceSize), "Update sequence size exceeds number of sectors in file record", this);
        }
        
        /// <summary>
        /// Reads attributes from file record
        /// </summary>
        /// <remarks>Current offset must be set back if calling this more than once</remarks>
        public void ReadAttributes()
        {
            _currentOffset = Header.FirstAttributeOffset;

            while (_currentOffset < _data.Length && BitConverter.ToUInt32(_data, (int) _currentOffset) != 0xffffffff)
            {
                var newData = new byte[_data.Length - _currentOffset];
                Array.Copy(_data, _currentOffset, newData, 0, newData.Length);

                var attr = new AttributeBase(newData, this);
                Attributes.Add(attr);

                _currentOffset += attr.Header.Header.Length;

            }

            _hasReadAttributes = true;
        }

        /// <summary>
        /// Checks if file record has attribute with type
        /// </summary>
        /// <param name="attrType">Attribute type</param>
        /// <returns>True if file record has any attributes of type</returns>
        public bool HasAttribute(AttributeHeaderBase.NTFS_ATTR_TYPE attrType)
        {
            return Attributes.Any(attr => attr.Header.Header.Type == attrType);
        }

        /// <summary>
        /// Finds attributes with specified type and returns list of ones matching it
        /// </summary>
        /// <param name="attrType">Attribute type</param>
        /// <returns>Matching attributes or empty list if none found</returns>
        public IEnumerable<AttributeBase> FindAttributesByType(AttributeHeaderBase.NTFS_ATTR_TYPE attrType)
        {
            return Attributes.Where(attr => attr.Header.Header.Type == attrType);
        }

        /// <summary>
        /// Finds first attribute with specified type
        /// </summary>
        /// <param name="attrType">Attribute type</param>
        /// <returns>First matching attribute or null none found</returns>
        public AttributeBase FindAttributeByType(AttributeHeaderBase.NTFS_ATTR_TYPE attrType)
        {
            return Attributes.FirstOrDefault(attr => attr.Header.Header.Type == attrType);
        }

        /// <summary>
        /// Finds body of first attribute with specified type
        /// </summary>
        /// <param name="attrType">Attribute type</param>
        /// <returns>First matching attributes body or null none found</returns>
        public AttributeBodyBase FindAttributeBodyByType(AttributeHeaderBase.NTFS_ATTR_TYPE attrType)
        {
            var attr = Attributes.FirstOrDefault(a => a.Header.Header.Type == attrType);

            return attr?.Body;
        }

        /// <summary>
        /// Tries to find an attribute in the file record
        /// </summary>
        /// <param name="attrNum">Attribute number</param>
        /// <param name="attrType">Attribute type</param>
        /// <param name="name">Name to match in attribute</param>
        /// <returns>Matching AttributeBase or null if it wasn't found</returns>
        public AttributeBase FindAttribute(ushort attrNum, AttributeHeaderBase.NTFS_ATTR_TYPE attrType, string name)
        {
            if (!_hasReadAttributes)
            {
                var found = false;

                while (_currentOffset < _data.Length && BitConverter.ToUInt32(_data, (int) _currentOffset) != 0xffffffff)
                {
                    var newData = new byte[_data.Length - _currentOffset];
                    Array.Copy(_data, _currentOffset, newData, 0, newData.Length);

                    var attr = new AttributeBase(newData, this);

                    if (attr.Header.Header.Type == attrType)
                    {
                        if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(attr.Header.Name) &&
                            attr.Header.Header.AttributeID == attrNum)
                            found = true;
                        else if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(attr.Header.Name))
                        {
                            if (name == attr.Header.Name)
                                found = true;
                        }

                    }

                    _currentOffset += attr.Header.Header.Length;

                    if (found)
                        return attr.ReadBody();
                }
            }
            else
            {
                foreach (var attr in Attributes)
                {
                    if (attr.Header.Header.Type != attrType)
                        continue;

                    if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(attr.Header.Name) &&
                        attr.Header.Header.AttributeID == attrNum)
                        return attr;

                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(attr.Header.Name) &&
                        name == attr.Header.Name)
                        return attr;
                }
            }
            
            return null;
        }

        [Flags]
        public enum Flags : ushort
        {
            InUse = 1 << 0,
            IsDirectory = 1 << 1
        }

        public int Compare(FileRecord x, FileRecord y)
        {
            if (x == null)
                return -1;

            if (y == null)
                return 1;

            return (int) (x.Header.MFTRecordNumber - y.Header.MFTRecordNumber);
        }

        public int CompareTo(FileRecord other)
        {
            if (other == null)
                return -1;

            return (int) (Header.MFTRecordNumber - other.Header.MFTRecordNumber);
        }

        public bool Equals(FileRecord other)
        {
            return CompareTo(other) == 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FILE_RECORD_HEADER_NTFS
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public readonly byte[] Magic;
            public readonly ushort UpdateSequenceOffset;
            public readonly ushort UpdateSequenceSize;
            public readonly ulong LogFileSequenceNumber;
            public readonly ushort SequenceNumber;
            public readonly ushort HardLinkCount;
            public readonly ushort FirstAttributeOffset;
            public readonly Flags Flags;
            public readonly uint UsedSize;
            public readonly uint AllocateSize;
            public readonly ulong FileReference;
            public readonly ushort NextAttributeID;
            public readonly ushort Align;
            public readonly uint MFTRecordNumber;
        }
    }
}
