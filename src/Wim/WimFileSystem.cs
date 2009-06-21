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
using System.Globalization;
using System.IO;
using System.Security.AccessControl;
using System.Text.RegularExpressions;

namespace DiscUtils.Wim
{
    /// <summary>
    /// Provides access to the file system within a WIM file image.
    /// </summary>
    public class WimFileSystem : ReadOnlyDiscFileSystem
    {
        private WimFile _file;
        private List<FileSecurity> _securityDescriptors;
        private Dictionary<long, List<DirectoryEntry>> _directories;

        internal WimFileSystem(WimFile file, int index)
        {
            _file = file;

            ShortResourceHeader metaDataFileInfo = _file.LocateImage(index);
            if (metaDataFileInfo == null)
            {
                throw new ArgumentException("No such image: " + index, "index");
            }

            using(Stream metaDataStream = _file.OpenResourceStream(metaDataFileInfo))
            {
                ReadMetaData(metaDataStream);
            }
        }

        #region Public interface
        /// <summary>
        /// Provides a friendly description of the file system type.
        /// </summary>
        public override string FriendlyName
        {
            get { return "Microsoft WIM"; }
        }

        /// <summary>
        /// Indicates if a directory exists.
        /// </summary>
        /// <param name="path">The path to test</param>
        /// <returns>true if the directory exists</returns>
        public override bool DirectoryExists(string path)
        {
            DirectoryEntry dirEntry = GetEntry(path);
            return dirEntry != null && (dirEntry.Attributes & FileAttributes.Directory) != 0;
        }

        /// <summary>
        /// Indicates if a file exists.
        /// </summary>
        /// <param name="path">The path to test</param>
        /// <returns>true if the file exists</returns>
        public override bool FileExists(string path)
        {
            DirectoryEntry dirEntry = GetEntry(path);
            return dirEntry != null && (dirEntry.Attributes & FileAttributes.Directory) == 0;
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
            DirectoryEntry parentDirEntry = GetEntry(path);
            if (parentDirEntry == null)
            {
                throw new DirectoryNotFoundException(string.Format(CultureInfo.InvariantCulture, "The directory '{0}' does not exist", path));
            }

            List<DirectoryEntry> parentDir = _directories[parentDirEntry.SubdirOffset];

            return Utilities.Map<DirectoryEntry, string>(parentDir, (m) => Utilities.CombinePaths(path, m.FileName));
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

            DirectoryEntry parentDirEntry = GetEntry(path);
            if (parentDirEntry == null)
            {
                throw new DirectoryNotFoundException(string.Format(CultureInfo.InvariantCulture, "The directory '{0}' does not exist", path));
            }

            List<DirectoryEntry> parentDir = _directories[parentDirEntry.SubdirOffset];

            List<string> result = new List<string>();
            foreach (DirectoryEntry dirEntry in parentDir)
            {
                if (re.IsMatch(dirEntry.FileName))
                {
                    result.Add(Path.Combine(path, dirEntry.FileName));
                }
            }
            return result.ToArray();
        }

        /// <summary>
        /// Opens the specified file.
        /// </summary>
        /// <param name="path">The full path of the file to open.</param>
        /// <param name="mode">The file mode for the created stream.</param>
        /// <param name="access">The access permissions for the created stream.</param>
        /// <returns>The new stream.</returns>
        public override Stream OpenFile(string path, FileMode mode, FileAccess access)
        {
            if (mode != FileMode.Open && mode != FileMode.OpenOrCreate)
            {
                throw new NotSupportedException("No write support for WIM files");
            }
            if (access != FileAccess.Read)
            {
                throw new NotSupportedException("No write support for WIM files");
            }

            byte[] streamHash = GetFileHash(path);
            ShortResourceHeader hdr = _file.LocateResource(streamHash);
            if (hdr == null)
            {
                if (Utilities.IsAllZeros(streamHash, 0, streamHash.Length))
                {
                    return new ZeroStream(0);
                }

                throw new IOException("Unable to locate file contents");
            }

            return _file.OpenResourceStream(hdr);
        }

        /// <summary>
        /// Gets the attributes of a file or directory.
        /// </summary>
        /// <param name="path">The file or directory to inspect</param>
        /// <returns>The attributes of the file or directory</returns>
        public override FileAttributes GetAttributes(string path)
        {
            DirectoryEntry dirEntry = GetEntry(path);
            if (dirEntry == null)
            {
                throw new FileNotFoundException("No such file or directory", path);
            }
            return dirEntry.Attributes;
        }

        /// <summary>
        /// Gets the creation time (in UTC) of a file or directory.
        /// </summary>
        /// <param name="path">The path of the file or directory.</param>
        /// <returns>The creation time.</returns>
        public override DateTime GetCreationTimeUtc(string path)
        {
            DirectoryEntry dirEntry = GetEntry(path);
            if (dirEntry == null)
            {
                throw new FileNotFoundException("No such file or directory", path);
            }
            return DateTime.FromFileTimeUtc(dirEntry.CreationTime);
        }

        /// <summary>
        /// Gets the last access time (in UTC) of a file or directory.
        /// </summary>
        /// <param name="path">The path of the file or directory</param>
        /// <returns>The last access time</returns>
        public override DateTime GetLastAccessTimeUtc(string path)
        {
            DirectoryEntry dirEntry = GetEntry(path);
            if (dirEntry == null)
            {
                throw new FileNotFoundException("No such file or directory", path);
            }
            return DateTime.FromFileTimeUtc(dirEntry.LastAccessTime);
        }

        /// <summary>
        /// Gets the last modification time (in UTC) of a file or directory.
        /// </summary>
        /// <param name="path">The path of the file or directory</param>
        /// <returns>The last write time</returns>
        public override DateTime GetLastWriteTimeUtc(string path)
        {
            DirectoryEntry dirEntry = GetEntry(path);
            if (dirEntry == null)
            {
                throw new FileNotFoundException("No such file or directory", path);
            }
            return DateTime.FromFileTimeUtc(dirEntry.LastWriteTime);
        }

        /// <summary>
        /// Gets the length of a file.
        /// </summary>
        /// <param name="path">The path to the file</param>
        /// <returns>The length in bytes</returns>
        public override long GetFileLength(string path)
        {
            byte[] hash = GetFileHash(path);
            if (Utilities.IsAllZeros(hash, 0, hash.Length))
            {
                throw new FileNotFoundException("No such file or directory", path);
            }

            return _file.LocateResource(hash).OriginalSize;
        }
        #endregion

        /// <summary>
        /// Gets the SHA-1 hash of a file's contents.
        /// </summary>
        /// <param name="path">The path to the file</param>
        /// <returns>The 160-bit hash</returns>
        /// <remarks>The WIM file format internally stores the SHA-1 hash of files.
        /// This method provides access to the stored hash.  Callers can use this
        /// value to compare against the actual hash of the byte stream to validate
        /// the integrity of the file contents.</remarks>
        public byte[] GetFileHash(string path)
        {
            string filePart = path;
            string altStreamPart = "";
            int streamSepPos = path.IndexOf(":");

            if (streamSepPos >= 0)
            {
                filePart = path.Substring(0, streamSepPos);
                altStreamPart = path.Substring(streamSepPos + 1);
            }

            DirectoryEntry dirEntry = GetEntry(filePart);
            if (dirEntry == null)
            {
                throw new FileNotFoundException("No such file or directory", path);
            }

            return dirEntry.GetStreamHash(altStreamPart);
        }

        private void ReadMetaData(Stream stream)
        {
            LittleEndianDataReader reader = new LittleEndianDataReader(stream);

            ReadSecurityDescriptors(reader);
            ReadDirectories(reader);
        }

        private void ReadDirectories(LittleEndianDataReader reader)
        {
            _directories = new Dictionary<long, List<DirectoryEntry>>();
            long dirId = 0;

            while (reader.Position < reader.Length)
            {
                List<DirectoryEntry> dir = new List<DirectoryEntry>();

                DirectoryEntry entry = DirectoryEntry.ReadFrom(reader);
                while (entry != null)
                {
                    dir.Add(entry);
                    entry = DirectoryEntry.ReadFrom(reader);
                }
                _directories.Add(dirId, dir);
                dirId = reader.Position;
            }
        }

        private void ReadSecurityDescriptors(LittleEndianDataReader reader)
        {
            long startPos = reader.Position;

            uint totalLength = reader.ReadUInt32();
            uint numEntries = reader.ReadUInt32();
            ulong[] sdLengths = new ulong[numEntries];
            for (uint i = 0; i < numEntries; ++i)
            {
                sdLengths[i] = reader.ReadUInt64();
            }

            _securityDescriptors = new List<FileSecurity>((int)numEntries);
            FileSecurity[] securityDescriptors = new FileSecurity[numEntries];
            for (uint i = 0; i < numEntries; ++i)
            {
                FileSecurity descriptor = new FileSecurity();
                descriptor.SetSecurityDescriptorBinaryForm(reader.ReadBytes((int)sdLengths[i]), AccessControlSections.All);
                _securityDescriptors.Add(descriptor);
            }

            if (reader.Position < startPos + totalLength)
            {
                reader.Skip((int)(startPos + totalLength - reader.Position));
            }
        }

        private DirectoryEntry GetEntry(string path)
        {
            if (path.EndsWith(@"\", StringComparison.Ordinal))
            {
                path = path.Substring(0, path.Length - 1);
            }
            if (!string.IsNullOrEmpty(path) && !path.StartsWith(@"\", StringComparison.OrdinalIgnoreCase))
            {
                path = @"\" + path;
            }

            return GetEntry(_directories[0], path.Split('\\'));
        }

        private DirectoryEntry GetEntry(List<DirectoryEntry> dir, string[] path)
        {
            List<DirectoryEntry> currentDir = dir;
            DirectoryEntry nextEntry = null;

            for (int i = 0; i < path.Length; ++i)
            {
                nextEntry = null;

                foreach (var entry in currentDir)
                {
                    if (path[i].Equals(entry.FileName, StringComparison.OrdinalIgnoreCase)
                        || (!string.IsNullOrEmpty(entry.ShortName) && path[i].Equals(entry.ShortName, StringComparison.OrdinalIgnoreCase)))
                    {
                        nextEntry = entry;
                        break;
                    }
                }

                if (nextEntry == null)
                {
                    return null;
                }
                else if (nextEntry.SubdirOffset != 0)
                {
                    currentDir = _directories[nextEntry.SubdirOffset];
                }
            }

            return nextEntry;
        }

        private void DoSearch(List<string> results, string path, Regex regex, bool subFolders, bool dirs, bool files)
        {
            DirectoryEntry parentDirEntry = GetEntry(path);

            if (parentDirEntry.SubdirOffset == 0)
            {
                return;
            }

            List<DirectoryEntry> parentDir = _directories[parentDirEntry.SubdirOffset];

            foreach (DirectoryEntry de in parentDir)
            {
                bool isDir = ((de.Attributes & FileAttributes.Directory) != 0);

                if ((isDir && dirs) || (!isDir && files))
                {
                    if (regex.IsMatch(de.SearchName))
                    {
                        results.Add(Path.Combine(path, de.FileName));
                    }
                }

                if (subFolders && isDir)
                {
                    DoSearch(results, Path.Combine(path, de.FileName), regex, subFolders, dirs, files);
                }
            }
        }
    }
}