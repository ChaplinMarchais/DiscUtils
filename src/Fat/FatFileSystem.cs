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
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;

namespace DiscUtils.Fat
{
    /// <summary>
    /// Class for accessing FAT file systems.
    /// </summary>
    public class FatFileSystem : DiscFileSystem, IDisposable
    {
        private TimeZoneInfo _timeZone;
        private Stream _data;
        private byte[] _bootSector;
        private FileAllocationTable _fat;
        private ClusterReader _clusterReader;
        private Directory _rootDir;
        private Dictionary<uint, Directory> _dirCache;


        private FatType _type;
        private string _bpbOEMName;
        private ushort _bpbBytesPerSec;
        private byte _bpbSecPerClus;
        private ushort _bpbRsvdSecCnt;
        private byte _bpbNumFATs;
        private ushort _bpbRootEntCnt;
        private ushort _bpbTotSec16;
        private byte _bpbMedia;
        private ushort _bpbFATSz16;
        private ushort _bpbSecPerTrk;
        private ushort _bpbNumHeads;
        private uint _bpbHiddSec;
        private uint _bpbTotSec32;

        private byte _bsDrvNum;
        private byte _bsBootSig;
        private uint _bsVolId;
        private string _bsVolLab;
        private string _bsFilSysType;

        private uint _bpbFATSz32;
        private ushort _bpbExtFlags;
        private ushort _bpbFSVer;
        private uint _bpbRootClus;
        private ushort _bpbFSInfo;
        private ushort _bpbBkBootSec;

        /// <summary>
        /// The Epoch for FAT file systems (1st Jan, 1980).
        /// </summary>
        public static readonly DateTime Epoch = new DateTime(1980, 1, 1);

        /// <summary>
        /// Creates a new instance, with local time as effective timezone
        /// </summary>
        /// <param name="data">The stream containing the file system.</param>
        public FatFileSystem(Stream data)
        {
            _dirCache = new Dictionary<uint, Directory>();
            _timeZone = TimeZoneInfo.Local;
            Initialize(data);
        }

        /// <summary>
        /// Creates a new instance, with a specific timezone
        /// </summary>
        /// <param name="data">The stream containing the file system.</param>
        /// <param name="timeZone">The timezone of the new instance.</param>
        public FatFileSystem(Stream data, TimeZoneInfo timeZone)
        {
            _dirCache = new Dictionary<uint, Directory>();
            _timeZone = timeZone;
            Initialize(data);
        }

        /// <summary>
        /// Gets the FAT variant of the file system.
        /// </summary>
        public FatType FatVariant
        {
            get { return _type; }
        }

        /// <summary>
        /// Gets the friendly name for the file system, including FAT variant.
        /// </summary>
        public override string FriendlyName
        {
            get
            {
                switch (_type)
                {
                    case FatType.Fat12: return "Microsoft FAT12";
                    case FatType.Fat16: return "Microsoft FAT16";
                    case FatType.Fat32: return "Microsoft FAT32";
                    default: return "Unknown FAT";
                }
            }
        }

        /// <summary>
        /// Gets the OEM name from the file system.
        /// </summary>
        public string OemName
        {
            get { return _bpbOEMName; }
        }

        /// <summary>
        /// Gets the number of bytes per sector (as stored in the file-system meta data).
        /// </summary>
        public int BytesPerSector
        {
            get { return _bpbBytesPerSec; }
        }

        /// <summary>
        /// Gets the number of contiguous sectors that make up one cluster.
        /// </summary>
        public byte SectorsPerCluster
        {
            get { return _bpbSecPerClus; }
        }

        /// <summary>
        /// Gets the number of reserved sectors at the start of the disk.
        /// </summary>
        public int ReservedSectorCount
        {
            get { return _bpbRsvdSecCnt; }
        }

        /// <summary>
        /// Gets the number of FATs present.
        /// </summary>
        public byte FatCount
        {
            get { return _bpbNumFATs; }
        }

        /// <summary>
        /// Gets the maximum number of root directory entries (on FAT variants that have a limit).
        /// </summary>
        public int MaxRootDirectoryEntries
        {
            get { return _bpbRootEntCnt; }
        }

        /// <summary>
        /// Gets the total number of sectors on the disk.
        /// </summary>
        public long TotalSectors
        {
            get { return (_bpbTotSec16 != 0) ? _bpbTotSec16 : _bpbTotSec32; }
        }

        /// <summary>
        /// Gets the Media marker byte, which indicates fixed or removable media.
        /// </summary>
        public byte Media
        {
            get { return _bpbMedia; }
        }

        /// <summary>
        /// Gets the size of a single FAT, in sectors.
        /// </summary>
        public long FatSize
        {
            get { return (_bpbFATSz16 != 0) ? _bpbFATSz16 : _bpbFATSz32; }
        }

        /// <summary>
        /// Gets the number of sectors per logical track.
        /// </summary>
        public int SectorsPerTrack
        {
            get { return _bpbSecPerTrk; }
        }

        /// <summary>
        /// Gets the number of logical heads.
        /// </summary>
        public int Heads
        {
            get { return _bpbNumHeads; }
        }

        /// <summary>
        /// Gets the number of hidden sectors, hiding partition tables, etc.
        /// </summary>
        public long HiddenSectors
        {
            get { return _bpbHiddSec; }
        }

        /// <summary>
        /// BIOS drive number for BIOS Int 13h calls.
        /// </summary>
        public byte BiosDriveNumber
        {
            get { return _bsDrvNum; }
        }

        /// <summary>
        /// Indicates if the VolumeId, VolumeLabel and FileSystemType fields are valid.
        /// </summary>
        public bool ExtendedBootSignaturePresent
        {
            get { return _bsBootSig == 0x29; }
        }

        /// <summary>
        /// Gets the volume serial number.
        /// </summary>
        public int VolumeId
        {
            get { return (int)_bsVolId; }
        }

        /// <summary>
        /// Gets the volume label.
        /// </summary>
        public string VolumeLabel
        {
            get { return _bsVolLab; }
        }

        /// <summary>
        /// Gets the (informational only) file system type recorded in the meta-data.
        /// </summary>
        public string FileSystemType
        {
            get { return _bsFilSysType; }
        }

        /// <summary>
        /// Gets the active FAT (zero-based index).
        /// </summary>
        public byte ActiveFat
        {
            get { return (byte)(((_bpbExtFlags & 0x08) != 0) ? _bpbExtFlags & 0x7 : 0); }
        }

        /// <summary>
        /// Gets whether FAT changes are mirrored to all copies of the FAT.
        /// </summary>
        public bool MirrorFat
        {
            get { return ((_bpbExtFlags & 0x08) == 0); }
        }

        /// <summary>
        /// Gets the file-system version (usually 0)
        /// </summary>
        public int Version
        {
            get { return _bpbFSVer; }
        }

        /// <summary>
        /// Gets the cluster number of the first cluster of the root directory (FAT32 only).
        /// </summary>
        public long RootDirectoryCluster
        {
            get { return _bpbRootClus; }
        }

        /// <summary>
        /// Gets the sector location of the FSINFO structure (FAT32 only).
        /// </summary>
        public int FSInfoSector
        {
            get { return _bpbFSInfo; }
        }

        /// <summary>
        /// Sector location of the backup boot sector (FAT32 only).
        /// </summary>
        public int BackupBootSector
        {
            get { return _bpbBkBootSec; }
        }

        /// <summary>
        /// Indicates if this file system is read-only or read-write.
        /// </summary>
        /// <returns></returns>
        public override bool CanWrite
        {
            get { return _data.CanWrite; }
        }

        /// <summary>
        /// Gets the root directory of the file system.
        /// </summary>
        public override DiscDirectoryInfo Root
        {
            get { return new FatDirectoryInfo(this, ""); }
        }

        /// <summary>
        /// Opens a file for reading and/or writing.
        /// </summary>
        /// <param name="path">The full path to the file</param>
        /// <param name="mode">The file mode</param>
        /// <param name="access">The desired access</param>
        /// <returns>The stream to the opened file</returns>
        public override Stream OpenFile(string path, FileMode mode, FileAccess access)
        {
            Directory parent;
            long entryId = GetDirectoryEntry(_rootDir, path, out parent);

            if (parent == null)
            {
                throw new FileNotFoundException("Could not locate file", path);
            }

            if (entryId < 0)
            {
                string[] pathEntries = path.Split(new string[] { "\\" }, StringSplitOptions.RemoveEmptyEntries);
                string normFileName = FatUtilities.NormalizeFileName(pathEntries[pathEntries.Length - 1]);
                return parent.OpenFile(normFileName, mode, access);
            }

            DirectoryEntry dirEntry = parent.GetEntry(entryId);

            if ((dirEntry.Attributes & FatAttributes.Directory) != 0)
            {
                throw new IOException("Attempt to open directory as a file");
            }
            else
            {
                return parent.OpenFile(dirEntry.NormalizedName, mode, access);
            }
        }

        /// <summary>
        /// Gets the attributes of a file or directory.
        /// </summary>
        /// <param name="path">The file or directory to inspect</param>
        /// <returns>The attributes of the file or directory</returns>
        public override FileAttributes GetAttributes(string path)
        {
            // Luckily, FAT and .NET FileAttributes match, bit-for-bit
            return (FileAttributes)GetDirectoryEntry(path).Attributes;
        }

        /// <summary>
        /// Sets the attributes of a file or directory.
        /// </summary>
        /// <param name="path">The file or directory to change</param>
        /// <param name="newValue">The new attributes of the file or directory</param>
        public override void SetAttributes(string path, FileAttributes newValue)
        {
            Directory parent;
            long id = GetDirectoryEntry(path, out parent);
            DirectoryEntry dirEntry = parent.GetEntry(id);

            FatAttributes newFatAttr = (FatAttributes)newValue;

            if ((newFatAttr & FatAttributes.Directory) != (dirEntry.Attributes & FatAttributes.Directory))
            {
                throw new ArgumentException("Attempted to change the directory attribute");
            }

            dirEntry.Attributes = newFatAttr;
            parent.UpdateEntry(id, dirEntry);

            // For directories, need to update their 'self' entry also
            if ((dirEntry.Attributes & FatAttributes.Directory) != 0)
            {
                Directory dir = GetDirectory(path);
                dirEntry = dir.SelfEntry;
                dirEntry.Attributes = newFatAttr;
                dir.SelfEntry = dirEntry;
            }
        }

        /// <summary>
        /// Gets the creation time (in local time) of a file or directory.
        /// </summary>
        /// <param name="path">The path of the file or directory</param>
        /// <returns>The creation time.</returns>
        public override DateTime GetCreationTime(string path)
        {
            return GetDirectoryEntry(path).CreationTime;
        }

        /// <summary>
        /// Sets the creation time (in local time) of a file or directory.
        /// </summary>
        /// <param name="path">The path of the file or directory.</param>
        /// <param name="newTime">The new time to set.</param>
        public override void SetCreationTime(string path, DateTime newTime)
        {
            UpdateDirEntry(path, (e) => { e.CreationTime = newTime; });
        }

        /// <summary>
        /// Gets the creation time (in UTC) of a file or directory.
        /// </summary>
        /// <param name="path">The path of the file or directory.</param>
        /// <returns>The creation time.</returns>
        public override DateTime GetCreationTimeUtc(string path)
        {
            return ConvertToUtc(GetDirectoryEntry(path).CreationTime);
        }

        /// <summary>
        /// Sets the creation time (in UTC) of a file or directory.
        /// </summary>
        /// <param name="path">The path of the file or directory.</param>
        /// <param name="newTime">The new time to set.</param>
        public override void SetCreationTimeUtc(string path, DateTime newTime)
        {
            UpdateDirEntry(path, (e) => { e.CreationTime = ConvertFromUtc(newTime); });
        }

        /// <summary>
        /// Gets the last access time (in local time) of a file or directory.
        /// </summary>
        /// <param name="path">The path of the file or directory</param>
        /// <returns></returns>
        public override DateTime GetLastAccessTime(string path)
        {
            return GetDirectoryEntry(path).LastAccessTime;
        }

        /// <summary>
        /// Sets the last access time (in local time) of a file or directory.
        /// </summary>
        /// <param name="path">The path of the file or directory.</param>
        /// <param name="newTime">The new time to set.</param>
        public override void SetLastAccessTime(string path, DateTime newTime)
        {
            UpdateDirEntry(path, (e) => { e.LastAccessTime = newTime; });
        }

        /// <summary>
        /// Gets the last access time (in UTC) of a file or directory.
        /// </summary>
        /// <param name="path">The path of the file or directory</param>
        /// <returns></returns>
        public override DateTime GetLastAccessTimeUtc(string path)
        {
            return ConvertToUtc(GetDirectoryEntry(path).LastAccessTime);
        }

        /// <summary>
        /// Sets the last access time (in UTC) of a file or directory.
        /// </summary>
        /// <param name="path">The path of the file or directory.</param>
        /// <param name="newTime">The new time to set.</param>
        public override void SetLastAccessTimeUtc(string path, DateTime newTime)
        {
            UpdateDirEntry(path, (e) => { e.LastAccessTime = ConvertFromUtc(newTime); });
        }

        /// <summary>
        /// Gets the last modification time (in local time) of a file or directory.
        /// </summary>
        /// <param name="path">The path of the file or directory</param>
        /// <returns></returns>
        public override DateTime GetLastWriteTime(string path)
        {
            return GetDirectoryEntry(path).LastWriteTime;
        }

        /// <summary>
        /// Sets the last modification time (in local time) of a file or directory.
        /// </summary>
        /// <param name="path">The path of the file or directory.</param>
        /// <param name="newTime">The new time to set.</param>
        public override void SetLastWriteTime(string path, DateTime newTime)
        {
            UpdateDirEntry(path, (e) => { e.LastWriteTime = newTime; });
        }

        /// <summary>
        /// Gets the last modification time (in UTC) of a file or directory.
        /// </summary>
        /// <param name="path">The path of the file or directory</param>
        /// <returns></returns>
        public override DateTime GetLastWriteTimeUtc(string path)
        {
            return ConvertToUtc(GetDirectoryEntry(path).LastWriteTime);
        }

        /// <summary>
        /// Sets the last modification time (in UTC) of a file or directory.
        /// </summary>
        /// <param name="path">The path of the file or directory.</param>
        /// <param name="newTime">The new time to set.</param>
        public override void SetLastWriteTimeUtc(string path, DateTime newTime)
        {
            UpdateDirEntry(path, (e) => { e.LastWriteTime = ConvertFromUtc(newTime); });
        }

        /// <summary>
        /// Copies an existing file to a new file, allowing overwriting of an existing file.
        /// </summary>
        /// <param name="sourceFile">The source file</param>
        /// <param name="destinationFile">The destination file</param>
        /// <param name="overwrite">Whether to permit over-writing of an existing file.</param>
        public override void CopyFile(string sourceFile, string destinationFile, bool overwrite)
        {
            Directory sourceDir;
            long sourceEntryId = GetDirectoryEntry(sourceFile, out sourceDir);

            if (sourceDir == null || sourceEntryId < 0)
            {
                throw new FileNotFoundException(string.Format(CultureInfo.InvariantCulture, "The source file '{0}' was not found", sourceFile));
            }

            DirectoryEntry sourceEntry = sourceDir.GetEntry(sourceEntryId);

            if ((sourceEntry.Attributes & FatAttributes.Directory) != 0)
            {
                throw new IOException("The source file is a directory");
            }


            DirectoryEntry newEntry = new DirectoryEntry(sourceEntry);
            newEntry.NormalizedName = FatUtilities.NormalizedFileNameFromPath(destinationFile);
            newEntry.FirstCluster = 0;


            Directory destDir;
            long destEntryId = GetDirectoryEntry(destinationFile, out destDir);

            if (destDir == null)
            {
                throw new DirectoryNotFoundException(string.Format(CultureInfo.InvariantCulture, "The destination directory for '{0}' was not found", destinationFile));
            }

            // If the destination is a directory, use the old file name to construct a full path.
            if (destEntryId >= 0)
            {
                DirectoryEntry destEntry = destDir.GetEntry(destEntryId);
                if ((destEntry.Attributes & FatAttributes.Directory) != 0)
                {
                    newEntry.NormalizedName = FatUtilities.NormalizedFileNameFromPath(sourceFile);
                    destinationFile = Utilities.CombinePaths(destinationFile, Utilities.GetFileFromPath(sourceFile));

                    destEntryId = GetDirectoryEntry(destinationFile, out destDir);
                }
            }

            // If there's an existing entry...
            if (destEntryId >= 0)
            {
                DirectoryEntry destEntry = destDir.GetEntry(destEntryId);

                if ((destEntry.Attributes & FatAttributes.Directory) != 0)
                {
                    throw new IOException("Destination file is an existing directory");
                }

                if (!overwrite)
                {
                    throw new IOException("Destination file already exists");
                }

                // Remove the old file
                destDir.DeleteEntry(destEntryId, true);
            }

            // Add the new file's entry
            destEntryId = destDir.AddEntry(newEntry);

            // Copy the contents...
            using (Stream sourceStream = new FatFileStream(this, sourceDir, sourceEntryId, FileAccess.Read),
                destStream = new FatFileStream(this, destDir, destEntryId, FileAccess.Write))
            {
                Utilities.PumpStreams(sourceStream, destStream);
            }
        }

        /// <summary>
        /// Creates a directory.
        /// </summary>
        /// <param name="path">The directory to create.</param>
        public override void CreateDirectory(string path)
        {
            string[] pathElements = path.Split(new char[]{'\\'}, StringSplitOptions.RemoveEmptyEntries);

            Directory focusDir = _rootDir;

            for (int i = 0; i < pathElements.Length; ++i)
            {
                string normalizedName;
                try
                {
                    normalizedName = FatUtilities.NormalizeFileName(pathElements[i]);
                }
                catch (ArgumentException ae)
                {
                    throw new IOException("Invalid path", ae);
                }

                Directory child = focusDir.GetChildDirectory(normalizedName);
                if (child == null)
                {
                    child = focusDir.CreateChildDirectory(normalizedName);
                }
                focusDir = child;
            }
        }

        /// <summary>
        /// Deletes a directory, optionally with all descendants.
        /// </summary>
        /// <param name="path">The path of the directory to delete.</param>
        public override void DeleteDirectory(string path)
        {
            Directory dir = GetDirectory(path);
            if (dir == null)
            {
                throw new DirectoryNotFoundException(String.Format(CultureInfo.InvariantCulture, "No such directory: {0}", path));
            }

            if (!dir.IsEmpty)
            {
                throw new IOException("Unable to delete non-empty directory");
            }

            Directory parent;
            long id = GetDirectoryEntry(path, out parent);
            if (parent == null && id == 0)
            {
                throw new IOException("Unable to delete root directory");
            }
            else if (parent != null && id >= 0)
            {
                DirectoryEntry deadEntry = parent.GetEntry(id);
                parent.DeleteEntry(id, true);
                ForgetDirectory(deadEntry);
            }
            else
            {
                throw new DirectoryNotFoundException("No such directory: " + path);
            }
        }

        /// <summary>
        /// Deletes a file.
        /// </summary>
        /// <param name="path">The path of the file to delete.</param>
        public override void DeleteFile(string path)
        {
            Directory parent;
            long id = GetDirectoryEntry(path, out parent);
            if (parent == null || id < 0)
            {
                throw new IOException("No such file: " + path);
            }

            DirectoryEntry entry = parent.GetEntry(id);
            if (entry == null || (entry.Attributes & FatAttributes.Directory) != 0)
            {
                throw new IOException("No such file: " + path);
            }

            parent.DeleteEntry(id, true);
        }

        /// <summary>
        /// Indicates if a directory exists.
        /// </summary>
        /// <param name="path">The path to test</param>
        /// <returns>true if the directory exists</returns>
        public override bool DirectoryExists(string path)
        {
            // Special case - root directory
            if (String.IsNullOrEmpty(path))
            {
                return true;
            }
            else
            {
                DirectoryEntry dirEntry = GetDirectoryEntry(path);
                return (dirEntry != null && (dirEntry.Attributes & FatAttributes.Directory) != 0);
            }
        }

        /// <summary>
        /// Indicates if a file exists.
        /// </summary>
        /// <param name="path">The path to test</param>
        /// <returns>true if the file exists</returns>
        public override bool FileExists(string path)
        {
            // Special case - root directory
            if (String.IsNullOrEmpty(path))
            {
                return true;
            }
            else
            {
                DirectoryEntry dirEntry = GetDirectoryEntry(path);
                return (dirEntry != null && (dirEntry.Attributes & FatAttributes.Directory) == 0);
            }
        }

        /// <summary>
        /// Indicates if a file or directory exists.
        /// </summary>
        /// <param name="path">The path to test</param>
        /// <returns>true if the file or directory exists</returns>
        public override bool Exists(string path)
        {
            // Special case - root directory
            if (String.IsNullOrEmpty(path))
            {
                return true;
            }
            else
            {
                return GetDirectoryEntry(path) != null;
            }
        }

        /// <summary>
        /// Gets the names of subdirectories in a specified directory.
        /// </summary>
        /// <param name="path">The path to search.</param>
        /// <returns>Array of directories.</returns>
        public override string[] GetDirectories(string path)
        {
            Directory dir = GetDirectory(path);
            DirectoryEntry[] entries = dir.GetDirectories();
            List<string> dirs = new List<string>(entries.Length);
            foreach (DirectoryEntry dirEntry in entries)
            {
                dirs.Add(Path.Combine(path, dirEntry.Name));
            }
            return dirs.ToArray();
        }

        /// <summary>
        /// Gets the names of subdirectories in a specified directory matching a specified
        /// search pattern, using a value to determine whether to search subdirectories.
        /// </summary>
        /// <param name="path">The path to search.</param>
        /// <param name="searchPattern">The search string to match against.</param>
        /// <param name="searchOption">Indicates whether to search subdirectories.</param>
        /// <returns>Array of directories matching the search pattern.</returns>
        public override string[] GetDirectories(string path, string searchPattern, SearchOption searchOption)
        {
            Regex re = Utilities.ConvertWildcardsToRegEx(searchPattern);

            List<string> dirs = new List<string>();
            DoSearch(dirs, path, re, searchOption == SearchOption.AllDirectories, true, false);
            return dirs.ToArray();
        }

        /// <summary>
        /// Gets the names of files in a specified directory.
        /// </summary>
        /// <param name="path">The path to search.</param>
        /// <returns>Array of files.</returns>
        public override string[] GetFiles(string path)
        {
            Directory dir = GetDirectory(path);
            DirectoryEntry[] entries = dir.GetFiles();

            List<string> files = new List<string>(entries.Length);
            foreach (DirectoryEntry dirEntry in entries)
            {
                files.Add(Path.Combine(path, dirEntry.Name));
            }

            return files.ToArray();
        }

        /// <summary>
        /// Gets the names of files in a specified directory matching a specified
        /// search pattern, using a value to determine whether to search subdirectories.
        /// </summary>
        /// <param name="path">The path to search.</param>
        /// <param name="searchPattern">The search string to match against.</param>
        /// <param name="searchOption">Indicates whether to search subdirectories.</param>
        /// <returns>Array of files matching the search pattern.</returns>
        public override string[] GetFiles(string path, string searchPattern, SearchOption searchOption)
        {
            Regex re = Utilities.ConvertWildcardsToRegEx(searchPattern);

            List<string> results = new List<string>();
            DoSearch(results, path, re, searchOption == SearchOption.AllDirectories, false, true);
            return results.ToArray();
        }

        /// <summary>
        /// Gets the names of all files and subdirectories in a specified directory.
        /// </summary>
        /// <param name="path">The path to search.</param>
        /// <returns>Array of files and subdirectories matching the search pattern.</returns>
        public override string[] GetFileSystemEntries(string path)
        {
            Directory dir = GetDirectory(path);
            DirectoryEntry[] entries = dir.Entries;

            List<string> result = new List<string>(entries.Length);
            foreach (DirectoryEntry dirEntry in entries)
            {
                result.Add(Path.Combine(path, dirEntry.Name));
            }
            return result.ToArray();
        }

        /// <summary>
        /// Gets the names of files and subdirectories in a specified directory matching a specified
        /// search pattern.
        /// </summary>
        /// <param name="path">The path to search.</param>
        /// <param name="searchPattern">The search string to match against.</param>
        /// <returns>Array of files and subdirectories matching the search pattern.</returns>
        public override string[] GetFileSystemEntries(string path, string searchPattern)
        {
            Regex re = Utilities.ConvertWildcardsToRegEx(searchPattern);

            Directory dir = GetDirectory(path);
            DirectoryEntry[] entries = dir.Entries;

            List<string> result = new List<string>(entries.Length);
            foreach (DirectoryEntry dirEntry in entries)
            {
                if (re.IsMatch(dirEntry.SearchName))
                {
                    result.Add(Path.Combine(path, dirEntry.Name));
                }
            }
            return result.ToArray();
        }

        /// <summary>
        /// Moves a directory.
        /// </summary>
        /// <param name="sourceDirectoryName">The directory to move.</param>
        /// <param name="destinationDirectoryName">The target directory name.</param>
        public override void MoveDirectory(string sourceDirectoryName, string destinationDirectoryName)
        {
            if (string.IsNullOrEmpty(destinationDirectoryName))
            {
                if (destinationDirectoryName == null)
                {
                    throw new ArgumentNullException("destinationDirectoryName");
                }
                else
                {
                    throw new ArgumentException("Invalid destination name (empty string)");
                }
            }

            Directory destParent;
            long destId = GetDirectoryEntry(destinationDirectoryName, out destParent);
            if (destParent == null)
            {
                throw new DirectoryNotFoundException("Target directory doesn't exist");
            }
            else if (destId >= 0)
            {
                throw new IOException("Target directory already exists");
            }

            Directory sourceParent;
            long sourceId = GetDirectoryEntry(sourceDirectoryName, out sourceParent);
            if (sourceParent == null || sourceId < 0)
            {
                throw new IOException("Source directory doesn't exist");
            }

            destParent.AttachChildDirectory(FatUtilities.NormalizedFileNameFromPath(destinationDirectoryName), GetDirectory(sourceDirectoryName));
            sourceParent.DeleteEntry(sourceId, false);
        }

        /// <summary>
        /// Moves a file, allowing an existing file to be overwritten.
        /// </summary>
        /// <param name="sourceName">The file to move.</param>
        /// <param name="destinationName">The target file name.</param>
        /// <param name="overwrite">Whether to permit a destination file to be overwritten</param>
        public override void MoveFile(string sourceName, string destinationName, bool overwrite)
        {
            Directory sourceDir;
            long sourceEntryId = GetDirectoryEntry(sourceName, out sourceDir);

            if (sourceDir == null || sourceEntryId < 0)
            {
                throw new FileNotFoundException(string.Format(CultureInfo.InvariantCulture, "The source file '{0}' was not found", sourceName));
            }

            DirectoryEntry sourceEntry = sourceDir.GetEntry(sourceEntryId);

            if ((sourceEntry.Attributes & FatAttributes.Directory) != 0)
            {
                throw new IOException("The source file is a directory");
            }


            DirectoryEntry newEntry = new DirectoryEntry(sourceEntry);
            newEntry.NormalizedName = FatUtilities.NormalizedFileNameFromPath(destinationName);


            Directory destDir;
            long destEntryId = GetDirectoryEntry(destinationName, out destDir);

            if (destDir == null)
            {
                throw new DirectoryNotFoundException(string.Format(CultureInfo.InvariantCulture, "The destination directory for '{0}' was not found", destinationName));
            }

            // If the destination is a directory, use the old file name to construct a full path.
            if (destEntryId >= 0)
            {
                DirectoryEntry destEntry = destDir.GetEntry(destEntryId);
                if ((destEntry.Attributes & FatAttributes.Directory) != 0)
                {
                    newEntry.NormalizedName = FatUtilities.NormalizedFileNameFromPath(sourceName);
                    destinationName = Utilities.CombinePaths(destinationName, Utilities.GetFileFromPath(sourceName));

                    destEntryId = GetDirectoryEntry(destinationName, out destDir);
                }
            }

            // If there's an existing entry...
            if (destEntryId >= 0)
            {
                DirectoryEntry destEntry = destDir.GetEntry(destEntryId);

                if ((destEntry.Attributes & FatAttributes.Directory) != 0)
                {
                    throw new IOException("Destination file is an existing directory");
                }

                if (!overwrite)
                {
                    throw new IOException("Destination file already exists");
                }

                // Remove the old file
                destDir.DeleteEntry(destEntryId, true);
            }

            // Add the new file's entry and remove the old link to the file's contents
            destDir.AddEntry(newEntry);
            sourceDir.DeleteEntry(sourceEntryId, false);
        }

        /// <summary>
        /// Gets an object representing a possible file.
        /// </summary>
        /// <param name="path">The file path</param>
        /// <returns>The representing object</returns>
        /// <remarks>The file does not need to exist</remarks>
        public override DiscFileInfo GetFileInfo(string path)
        {
            return new FatFileInfo(this, path);
        }

        /// <summary>
        /// Gets an object representing a possible directory.
        /// </summary>
        /// <param name="path">The directory path</param>
        /// <returns>The representing object</returns>
        /// <remarks>The directory does not need to exist</remarks>
        public override DiscDirectoryInfo GetDirectoryInfo(string path)
        {
            return new FatDirectoryInfo(this, path);
        }

        /// <summary>
        /// Gets an object representing a possible file system object (file or directory).
        /// </summary>
        /// <param name="path">The file system path</param>
        /// <returns>The representing object</returns>
        /// <remarks>The file system object does not need to exist</remarks>
        public override DiscFileSystemInfo GetFileSystemInfo(string path)
        {
            return new FatFileSystemInfo(this, path);
        }

        #region Disk Formatting
        /// <summary>
        /// Creates a formatted floppy disk image in a stream.
        /// </summary>
        /// <param name="stream">The stream to write the blank image to</param>
        /// <param name="type">The type of floppy to create</param>
        /// <param name="label">The volume label for the floppy (or null)</param>
        /// <returns>An object that provides access to the newly created floppy disk image</returns>
        public static FatFileSystem FormatFloppy(Stream stream, FloppyDiskType type, string label)
        {
            long pos = stream.Position;

            long ticks = DateTime.UtcNow.Ticks;
            uint volId = (uint)((ticks & 0xFFFF) | (ticks >> 32));

            // Write the BIOS Parameter Block (BPB) - a single sector
            byte[] bpb = new byte[512];
            uint sectors;
            if (type == FloppyDiskType.DoubleDensity)
            {
                sectors = 1440;
                WriteBPB(bpb, sectors, FatType.Fat12, 224, 0, 1, 1, new DiskGeometry(80, 2, 9), true, volId, label);
            }
            else if (type == FloppyDiskType.HighDensity)
            {
                sectors = 2880;
                WriteBPB(bpb, sectors, FatType.Fat12, 224, 0, 1, 1, new DiskGeometry(80, 2, 18), true, volId, label);
            }
            else if (type == FloppyDiskType.Extended)
            {
                sectors = 5760;
                WriteBPB(bpb, sectors, FatType.Fat12, 224, 0, 1, 1, new DiskGeometry(80, 2, 36), true, volId, label);
            }
            else
            {
                throw new ArgumentException("Unrecognised Floppy Disk type", "type");
            }
            stream.Write(bpb, 0, bpb.Length);

            // Write both FAT copies
            uint fatSize = CalcFatSize(sectors, FatType.Fat12, 1);
            byte[] fat = new byte[fatSize * Utilities.SectorSize];
            FatBuffer fatBuffer = new FatBuffer(FatType.Fat12, fat);
            fatBuffer.SetNext(0, 0xFFFFFFF0);
            fatBuffer.SetEndOfChain(1);
            stream.Write(fat, 0, fat.Length);
            stream.Write(fat, 0, fat.Length);

            // Write the (empty) root directory
            uint rootDirSectors = ((224 * 32) + Utilities.SectorSize - 1) / Utilities.SectorSize;
            byte[] rootDir = new byte[rootDirSectors * Utilities.SectorSize];
            stream.Write(rootDir, 0, rootDir.Length);

            // Write a single byte at the end of the disk to ensure the stream is at least as big
            // as needed for this disk image.
            stream.Position = pos + (sectors * Utilities.SectorSize) - 1;
            stream.WriteByte(0);

            // Give the caller access to the new file system
            stream.Position = pos;
            return new FatFileSystem(stream);
        }

        /// <summary>
        /// Formats a virtual hard disk partition.
        /// </summary>
        /// <param name="disk">The disk containing the partition</param>
        /// <param name="partitionIndex">The index of the partition on the disk</param>
        /// <param name="label">The volume label for the partition (or null)</param>
        /// <returns>An object that provides access to the newly created partition file system</returns>
        public static FatFileSystem FormatPartition(VirtualDisk disk, int partitionIndex, string label)
        {
            using (Stream partitionStream = disk.OpenPartition(disk.Partitions[partitionIndex]))
            {
                return FormatPartition(
                    partitionStream,
                    label,
                    disk.Geometry,
                    (int)disk.Partitions[0].FirstSector,
                    (int)(1 + disk.Partitions[0].LastSector - disk.Partitions[0].FirstSector),
                    (short)disk.Geometry.SectorsPerTrack);
            }
        }

        /// <summary>
        /// Creates a formatted hard disk partition in a stream.
        /// </summary>
        /// <param name="stream">The stream to write the blank image to</param>
        /// <param name="label">The volume label for the partition (or null)</param>
        /// <param name="diskGeometry">The geometry of the disk containing the partition</param>
        /// <param name="firstSector">The starting sector number of this partition (hide's sectors in other partitions)</param>
        /// <param name="sectorCount">The number of sectors in this partition</param>
        /// <param name="reservedSectors">The number of reserved sectors at the start of the partition</param>
        /// <returns>An object that provides access to the newly created partition file system</returns>
        public static FatFileSystem FormatPartition(
            Stream stream,
            string label,
            DiskGeometry diskGeometry,
            int firstSector,
            int sectorCount,
            short reservedSectors)
        {
            if (reservedSectors < 1)
            {
                throw new ArgumentOutOfRangeException("reservedSectors", reservedSectors, "Must have at least one reserved sector, for the BPB");
            }

            long pos = stream.Position;

            long ticks = DateTime.UtcNow.Ticks;
            uint volId = (uint)((ticks & 0xFFFF) | (ticks >> 32));

            byte sectorsPerCluster;
            FatType fatType;
            ushort maxRootEntries;

            // Write the BIOS Parameter Block (BPB) - a single sector
            byte[] bpb = new byte[512];
            if (sectorCount <= 8400)
            {
                throw new ArgumentException("Requested size is too small for a partition");
            }
            else if (sectorCount < 1024 * 1024)
            {
                fatType = FatType.Fat16;
                maxRootEntries = 512;
                if (sectorCount <= 32680)
                {
                    sectorsPerCluster = 2;
                }
                else if (sectorCount <= 262144)
                {
                    sectorsPerCluster = 4;
                }
                else if (sectorCount <= 524288)
                {
                    sectorsPerCluster = 8;
                }
                else
                {
                    sectorsPerCluster = 16;
                }
            }
            else
            {
                fatType = FatType.Fat32;
                maxRootEntries = 0;
                if (sectorCount <= 532480)
                {
                    sectorsPerCluster = 1;
                }
                else if (sectorCount <= 16777216)
                {
                    sectorsPerCluster = 8;
                }
                else if (sectorCount <= 33554432)
                {
                    sectorsPerCluster = 16;
                }
                else if (sectorCount <= 67108864)
                {
                    sectorsPerCluster = 32;
                }
                else
                {
                    sectorsPerCluster = 64;
                }
            }
            WriteBPB(bpb, (uint)sectorCount, fatType, maxRootEntries, (uint)firstSector, (ushort)reservedSectors, sectorsPerCluster, diskGeometry, false, volId, label);
            stream.Write(bpb, 0, bpb.Length);

            // Skip the reserved sectors
            stream.Position = pos + (((ushort)reservedSectors) * Utilities.SectorSize);

            // Write both FAT copies
            byte[] fat = new byte[CalcFatSize((uint)sectorCount, fatType, sectorsPerCluster) * Utilities.SectorSize];
            FatBuffer fatBuffer = new FatBuffer(fatType, fat);
            fatBuffer.SetNext(0, 0xFFFFFFF8);
            fatBuffer.SetEndOfChain(1);
            if (fatType >= FatType.Fat32)
            {
                // Mark cluster 2 as End-of-chain (i.e. root directory
                // is a single cluster in length)
                fatBuffer.SetEndOfChain(2);
            }
            stream.Write(fat, 0, fat.Length);
            stream.Write(fat, 0, fat.Length);

            // Write the (empty) root directory
            uint rootDirSectors;
            if (fatType < FatType.Fat32)
            {
                rootDirSectors = (uint)(((maxRootEntries * 32) + Utilities.SectorSize - 1) / Utilities.SectorSize);
            }
            else
            {
                rootDirSectors = sectorsPerCluster;
            }
            byte[] rootDir = new byte[rootDirSectors * Utilities.SectorSize];
            stream.Write(rootDir, 0, rootDir.Length);

            // Make sure the stream is at least as large as the partition requires.
            if (stream.Length < pos + sectorCount * Utilities.SectorSize)
            {
                stream.SetLength(pos + sectorCount * Utilities.SectorSize);
            }

            // Give the caller access to the new file system
            stream.Position = pos;
            return new FatFileSystem(stream);
        }
        #endregion

        internal FileAllocationTable Fat
        {
            get { return _fat; }
        }

        internal ClusterReader ClusterReader
        {
            get { return _clusterReader; }
        }

        private void Initialize(Stream data)
        {
            _data = data;
            _data.Position = 0;
            _bootSector = Utilities.ReadSector(_data);

            _type = DetectFATType(_bootSector);

            ReadBPB();

            LoadFAT();

            LoadClusterReader();

            LoadRootDirectory();
        }

        private void LoadClusterReader()
        {
            int rootDirSectors = ((_bpbRootEntCnt * 32) + (_bpbBytesPerSec - 1)) / _bpbBytesPerSec;
            int firstDataSector = (int)(_bpbRsvdSecCnt + (_bpbNumFATs * FatSize) + rootDirSectors);
            _clusterReader = new ClusterReader(_data, firstDataSector, _bpbSecPerClus, _bpbBytesPerSec);
        }

        private void LoadRootDirectory()
        {
            Stream fatStream;
            if (_type != FatType.Fat32)
            {
                fatStream = new SubStream(_data, (_bpbRsvdSecCnt + (_bpbNumFATs * _bpbFATSz16)) * _bpbBytesPerSec, _bpbRootEntCnt * 32);
            }
            else
            {
                fatStream = new ClusterStream(this, FileAccess.ReadWrite, _bpbRootClus, uint.MaxValue);
            }
            _rootDir = new Directory(this, fatStream);
        }

        private void LoadFAT()
        {
            int fatStart;
            if (_type == FatType.Fat32)
            {
                fatStart = (int)((_bpbRsvdSecCnt + (ActiveFat * FatSize)) * BytesPerSector);
            }
            else
            {
                fatStart = _bpbRsvdSecCnt * BytesPerSector;
            }
            _fat = new FileAllocationTable(_type, _data, _bpbRsvdSecCnt, (uint)FatSize, (byte)FatCount, ActiveFat);
        }

        private void ReadBPB()
        {
            _bpbOEMName = Encoding.ASCII.GetString(_bootSector, 3, 8).TrimEnd('\0');
            _bpbBytesPerSec = Utilities.ToUInt16LittleEndian(_bootSector, 11);
            _bpbSecPerClus = _bootSector[13];
            _bpbRsvdSecCnt = Utilities.ToUInt16LittleEndian(_bootSector, 14);
            _bpbNumFATs = _bootSector[16];
            _bpbRootEntCnt = Utilities.ToUInt16LittleEndian(_bootSector, 17);
            _bpbTotSec16 = Utilities.ToUInt16LittleEndian(_bootSector, 19);
            _bpbMedia = _bootSector[21];
            _bpbFATSz16 = Utilities.ToUInt16LittleEndian(_bootSector, 22);
            _bpbSecPerTrk = Utilities.ToUInt16LittleEndian(_bootSector, 24);
            _bpbNumHeads = Utilities.ToUInt16LittleEndian(_bootSector, 26);
            _bpbHiddSec = Utilities.ToUInt32LittleEndian(_bootSector, 28);
            _bpbTotSec32 = Utilities.ToUInt32LittleEndian(_bootSector, 32);

            if (_type != FatType.Fat32)
            {
                ReadBS(36);
            }
            else
            {
                _bpbFATSz32 = Utilities.ToUInt32LittleEndian(_bootSector, 36);
                _bpbExtFlags = Utilities.ToUInt16LittleEndian(_bootSector, 40);
                _bpbFSVer = Utilities.ToUInt16LittleEndian(_bootSector, 42);
                _bpbRootClus = Utilities.ToUInt32LittleEndian(_bootSector, 44);
                _bpbFSInfo = Utilities.ToUInt16LittleEndian(_bootSector, 48);
                _bpbBkBootSec = Utilities.ToUInt16LittleEndian(_bootSector, 50);
                ReadBS(64);
            }
        }

        /// <summary>
        /// Writes a FAT12/FAT16 BPB.
        /// </summary>
        /// <param name="bootSector">The buffer to fill</param>
        /// <param name="sectors">The total capacity of the disk (in sectors)</param>
        /// <param name="fatType">The number of bits in each FAT entry</param>
        /// <param name="maxRootEntries">The maximum number of root directory entries</param>
        /// <param name="hiddenSectors">The number of hidden sectors before this file system (i.e. partition offset)</param>
        /// <param name="reservedSectors">The number of reserved sectors before the FAT</param>
        /// <param name="sectorsPerCluster">The number of sectors per cluster</param>
        /// <param name="diskGeometry">The geometry of the disk containing the Fat file system</param>
        /// <param name="isFloppy">Indicates if the disk is a removable media (a floppy disk)</param>
        /// <param name="volId">The disk's volume Id</param>
        /// <param name="label">The disk's label (or null)</param>
        private static void WriteBPB(
            byte[] bootSector,
            uint sectors,
            FatType fatType,
            ushort maxRootEntries,
            uint hiddenSectors,
            ushort reservedSectors,
            byte sectorsPerCluster,
            DiskGeometry diskGeometry,
            bool isFloppy,
            uint volId,
            string label)
        {
            uint fatSectors = CalcFatSize(sectors, fatType, sectorsPerCluster);

            bootSector[0] = 0xEB;
            bootSector[1] = 0x3C;
            bootSector[2] = 0x90;

            // OEM Name
            Utilities.StringToBytes("DISCUTIL", bootSector, 3, 8);

            // Bytes Per Sector (512)
            bootSector[11] = 0;
            bootSector[12] = 2;

            // Sectors Per Cluster
            bootSector[13] = sectorsPerCluster;

            // Reserved Sector Count
            Utilities.WriteBytesLittleEndian(reservedSectors, bootSector, 14);

            // Number of FATs
            bootSector[16] = 2;

            // Number of Entries in the root directory
            Utilities.WriteBytesLittleEndian((ushort)maxRootEntries, bootSector, 17);

            // Total number of sectors (small)
            Utilities.WriteBytesLittleEndian((ushort)(sectors < 0x10000 ? sectors : 0), bootSector, 19);

            // Media
            bootSector[21] = (byte)(isFloppy ? 0xF0 : 0xF8);

            // FAT size (FAT12/FAT16)
            Utilities.WriteBytesLittleEndian((ushort)(fatType < FatType.Fat32 ? fatSectors : 0), bootSector, 22);

            // Sectors Per Track
            Utilities.WriteBytesLittleEndian((ushort)diskGeometry.SectorsPerTrack, bootSector, 24);

            // Sectors Per Track
            Utilities.WriteBytesLittleEndian((ushort)diskGeometry.HeadsPerCylinder, bootSector, 26);

            // Hidden Sectors
            Utilities.WriteBytesLittleEndian((uint)hiddenSectors, bootSector, 28);

            // Total number of sectors (large)
            Utilities.WriteBytesLittleEndian((uint)(sectors >= 0x10000 ? sectors : 0), bootSector, 32);

            if (fatType < FatType.Fat32)
            {
                WriteBS(bootSector, 36, isFloppy, volId, label, fatType);
            }
            else
            {
                // FAT size (FAT32)
                Utilities.WriteBytesLittleEndian((uint)fatSectors, bootSector, 36);

                // Ext flags: 0x80 = FAT 1 (i.e. Zero) active, no mirroring
                bootSector[40] = 0x80;
                bootSector[41] = 0x00;

                // Filesystem version (0.0)
                bootSector[42] = 0;
                bootSector[43] = 0;

                // First cluster of the root directory, always 2 since we don't do bad sectors...
                Utilities.WriteBytesLittleEndian((uint)2, bootSector, 44);

                // Sector number of FSINFO
                Utilities.WriteBytesLittleEndian((uint)1, bootSector, 48);

                // Sector number of the Backup Boot Sector
                Utilities.WriteBytesLittleEndian((uint)6, bootSector, 50);

                // Reserved area - must be set to 0
                Array.Clear(bootSector, 52, 12);

                WriteBS(bootSector, 64, isFloppy, volId, label, fatType);
            }

        }

        private static uint CalcFatSize(uint sectors, FatType fatType, byte sectorsPerCluster)
        {
            ushort numClusters = (ushort)(sectors / sectorsPerCluster);
            ushort fatBytes = (ushort)((numClusters * (ushort)fatType) / 8);
            return (uint)((fatBytes + Utilities.SectorSize - 1) / Utilities.SectorSize);
        }

        private void ReadBS(int offset)
        {
            _bsDrvNum = _bootSector[offset];
            _bsBootSig = _bootSector[offset + 2];
            _bsVolId = Utilities.ToUInt32LittleEndian(_bootSector, offset + 3);
            _bsVolLab = Encoding.ASCII.GetString(_bootSector, offset + 7, 11);
            _bsFilSysType = Encoding.ASCII.GetString(_bootSector, offset + 18, 8);
        }

        private static void WriteBS(byte[] bootSector, int offset, bool isFloppy, uint volId, string label, FatType fatType)
        {
            if (string.IsNullOrEmpty(label))
            {
                label = "NO NAME    ";
            }

            string fsType = "FAT     ";
            if (fatType == FatType.Fat12)
            {
                fsType = "FAT12   ";
            }
            else if (fatType == FatType.Fat16)
            {
                fsType = "FAT16   ";
            }

            // Drive Number (for BIOS)
            bootSector[offset + 0] = (byte)(isFloppy ? 0x00 : 0x80);

            // Reserved
            bootSector[offset + 1] = 0;

            // Boot Signature (indicates next 3 fields present)
            bootSector[offset + 2] = 0x29;

            // Volume Id
            Utilities.WriteBytesLittleEndian(volId, bootSector, offset + 3);

            // Volume Label
            Utilities.StringToBytes(label, bootSector, offset + 7, 11);

            // File System Type
            Utilities.StringToBytes(fsType, bootSector, offset + 18, 8);
        }

        private static FatType DetectFATType(byte[] bpb)
        {
            uint bpbBytesPerSec = Utilities.ToUInt16LittleEndian(bpb, 11);
            uint bpbRootEntCnt = Utilities.ToUInt16LittleEndian(bpb, 17);
            uint bpbFATSz16 = Utilities.ToUInt16LittleEndian(bpb, 22);
            uint bpbFATSz32 = Utilities.ToUInt32LittleEndian(bpb, 36);
            uint bpbTotSec16 = Utilities.ToUInt16LittleEndian(bpb, 19);
            uint bpbTotSec32 = Utilities.ToUInt32LittleEndian(bpb, 32);
            uint bpbResvdSecCnt = Utilities.ToUInt16LittleEndian(bpb, 14);
            uint bpbNumFATs = bpb[16];
            uint bpbSecPerClus = bpb[13];

            uint rootDirSectors = ((bpbRootEntCnt * 32) + bpbBytesPerSec - 1) / bpbBytesPerSec;
            uint fatSz = (bpbFATSz16 != 0) ? (uint)bpbFATSz16 : bpbFATSz32;
            uint totalSec = (bpbTotSec16 != 0) ? (uint)bpbTotSec16 : bpbTotSec32;

            uint dataSec = totalSec - (bpbResvdSecCnt + (bpbNumFATs * fatSz) + rootDirSectors);
            uint countOfClusters = dataSec / bpbSecPerClus;

            if (countOfClusters < 4085)
            {
                return FatType.Fat12;
            }
            else if (countOfClusters < 65525)
            {
                return FatType.Fat16;
            }
            else
            {
                return FatType.Fat32;
            }
        }

        internal DateTime ConvertToUtc(DateTime dateTime)
        {
            return TimeZoneInfo.ConvertTimeToUtc(dateTime, _timeZone);
        }

        internal DateTime ConvertFromUtc(DateTime dateTime)
        {
            return TimeZoneInfo.ConvertTimeFromUtc(dateTime, _timeZone);
        }

        internal Directory GetDirectory(string path)
        {
            Directory parent;

            if (string.IsNullOrEmpty(path) || path == "\\")
            {
                return _rootDir;
            }

            long id = GetDirectoryEntry(_rootDir, path, out parent);
            if (id >= 0)
            {
                return GetDirectory(parent, id);
            }
            else
            {
                return null;
            }
        }

        internal Directory GetDirectory(Directory parent, long parentId)
        {
            if (parent == null)
            {
                return _rootDir;
            }

            DirectoryEntry dirEntry = parent.GetEntry(parentId);

            // If we have this one cached, return it
            Directory result;
            if (_dirCache.TryGetValue(dirEntry.FirstCluster, out result))
            {
                return result;
            }

            // Not cached - create a new one.
            result = new Directory(parent, parentId);
            _dirCache[dirEntry.FirstCluster] = result;
            return result;
        }

        internal void ForgetDirectory(DirectoryEntry entry)
        {
            uint index = entry.FirstCluster;
            if (index != 0 && _dirCache.ContainsKey(index))
            {
                Directory dir = _dirCache[index];
                _dirCache.Remove(index);
                dir.Dispose();
            }
        }

        internal DirectoryEntry GetDirectoryEntry(string path)
        {
            Directory parent;

            long id = GetDirectoryEntry(_rootDir, path, out parent);
            if (parent == null || id < 0)
            {
                return null;
            }

            return parent.GetEntry(id);
        }

        internal long GetDirectoryEntry(string path, out Directory parent)
        {
            return GetDirectoryEntry(_rootDir, path, out parent);
        }

        private long GetDirectoryEntry(Directory dir, string path, out Directory parent)
        {
            string[] pathElements = path.Split(new char[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            return GetDirectoryEntry(dir, pathElements, 0, out parent);
        }

        private long GetDirectoryEntry(Directory dir, string[] pathEntries, int pathOffset, out Directory parent)
        {
            long entryId;

            if (pathEntries.Length == 0)
            {
                // Looking for root directory, simulate the directory entry in its parent...
                parent = null;
                return 0;
            }
            else
            {
                entryId = dir.FindEntryByNormalizedName(FatUtilities.NormalizeFileName(pathEntries[pathOffset]));
                if (entryId >= 0)
                {
                    if (pathOffset == pathEntries.Length - 1)
                    {
                        parent = dir;
                        return entryId;
                    }
                    else
                    {
                        return GetDirectoryEntry(GetDirectory(dir, entryId), pathEntries, pathOffset + 1, out parent);
                    }
                }
                else if (pathOffset == pathEntries.Length - 1)
                {
                    parent = dir;
                    return -1;
                }
                else
                {
                    parent = null;
                    return -1;
                }
            }
        }

        private void DoSearch(List<string> results, string path, Regex regex, bool subFolders, bool dirs, bool files)
        {
            Directory dir = GetDirectory(path);
            DirectoryEntry[] entries = dir.Entries;

            foreach (DirectoryEntry de in entries)
            {
                bool isDir = ((de.Attributes & FatAttributes.Directory) != 0);

                if ((isDir && dirs) || (!isDir && files))
                {
                    if (regex.IsMatch(de.SearchName))
                    {
                        results.Add(Path.Combine(path, de.Name));
                    }
                }

                if (subFolders && isDir)
                {
                    DoSearch(results, Path.Combine(path, de.Name), regex, subFolders, dirs, files);
                }
            }
        }

        private delegate void EntryUpdateAction(DirectoryEntry entry);

        private void UpdateDirEntry(string path, EntryUpdateAction action)
        {
            Directory parent;
            long id = GetDirectoryEntry(path, out parent);
            DirectoryEntry entry = parent.GetEntry(id);
            action(entry);
            parent.UpdateEntry(id, entry);

            if ((entry.Attributes & FatAttributes.Directory) != 0)
            {
                Directory dir = GetDirectory(path);
                DirectoryEntry selfEntry = dir.SelfEntry;
                action(selfEntry);
                dir.SelfEntry = selfEntry;
            }
        }

        /// <summary>
        /// Disposes of this instance.
        /// </summary>
        /// <param name="disposing">true if Disposing</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (Directory dir in _dirCache.Values)
                {
                    dir.Dispose();
                }
                _rootDir.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}