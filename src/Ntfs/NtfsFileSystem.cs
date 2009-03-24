//
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

namespace DiscUtils.Ntfs
{
    /// <summary>
    /// Class for accessing NTFS file systems.
    /// </summary>
    public sealed class NtfsFileSystem : ClusterBasedFileSystem, IDiagnosticTraceable
    {
        private NtfsContext _context;

        // Top-level file system structures


        // Working state
        private ObjectCache<long, File> _fileCache;

        /// <summary>
        /// Creates a new instance from a stream.
        /// </summary>
        /// <param name="stream">The stream containing the NTFS file system</param>
        public NtfsFileSystem(Stream stream)
            : base(new NtfsOptions())
        {
            _context = new NtfsContext();
            _context.RawStream = stream;
            _context.Options = NtfsOptions;

            _context.GetFile = GetFile;
            _context.GetDirectory = GetDirectory;
            _context.GetDirectoryByIndex = GetDirectory;
            _context.AllocateFile = AllocateFile;

            _fileCache = new ObjectCache<long, File>();

            stream.Position = 0;
            byte[] bytes = Utilities.ReadFully(stream, 512);


            _context.BiosParameterBlock = BiosParameterBlock.FromBytes(bytes, 0);

            // Bootstrap the Master File Table
            _context.Mft = new MasterFileTable();
            File mftFile = new File(_context, MasterFileTable.GetBootstrapRecord(stream, _context.BiosParameterBlock));
            _fileCache[MasterFileTable.MftIndex] = mftFile;
            _context.Mft.Initialize(mftFile);

            // Initialize access to the other well-known metadata files
            _context.ClusterBitmap = new ClusterBitmap(GetFile(MasterFileTable.BitmapIndex));
            _context.AttributeDefinitions = new AttributeDefinitions(GetFile(MasterFileTable.AttrDefIndex));
            _context.UpperCase = new UpperCase(GetFile(MasterFileTable.UpCaseIndex));
            _context.SecurityDescriptors = new SecurityDescriptors(GetFile(MasterFileTable.SecureIndex));
            _context.ObjectIds = new ObjectIds(GetFile(GetDirectoryEntry(@"$Extend\$ObjId").Reference));

#if false
            byte[] buffer = new byte[1024];
            for (int i = 0; i < buffer.Length; ++i)
            {
                buffer[i] = 0xFF;
            }

            using (Stream s = OpenFile("$LogFile", FileMode.Open, FileAccess.ReadWrite))
            {
                while (s.Position != s.Length)
                {
                    s.Write(buffer, 0, (int)Math.Min(buffer.Length, s.Length - s.Position));
                }
            }
#endif
        }

        /// <summary>
        /// Gets the options that control how the file system is interpreted.
        /// </summary>
        public NtfsOptions NtfsOptions
        {
            get { return (NtfsOptions)Options; }
        }

        /// <summary>
        /// Opens the Master File Table as a raw stream.
        /// </summary>
        /// <returns></returns>
        public Stream OpenMasterFileTable()
        {
            return OpenRawAttribute("$MFT", AttributeType.Data, null, FileAccess.Read);
        }

        /// <summary>
        /// Gets the friendly name for the file system.
        /// </summary>
        public override string FriendlyName
        {
            get { return "Microsoft NTFS"; }
        }

        /// <summary>
        /// Indicates if the file system supports write operations.
        /// </summary>
        public override bool CanWrite
        {
            // For now, we don't...
            get { return false; }
        }

        #region Cluster Information
        /// <summary>
        /// Gets the size of each cluster (in bytes).
        /// </summary>
        public override long ClusterSize
        {
            get { return _context.BiosParameterBlock.BytesPerCluster; }
        }

        /// <summary>
        /// Gets the total number of clusters managed by the file system.
        /// </summary>
        public override long TotalClusters
        {
            get { return Utilities.Ceil(_context.BiosParameterBlock.TotalSectors64, _context.BiosParameterBlock.SectorsPerCluster); }
        }

        public override Range<long, long>[] PathToClusters(string path)
        {
            string plainPath;
            string attributeName;
            SplitPath(path, out plainPath, out attributeName);


            DirectoryEntry dirEntry = GetDirectoryEntry(plainPath);
            File file = GetFile(dirEntry.Reference);

            NtfsAttribute attr = file.GetAttribute(AttributeType.Data, attributeName);
            return attr.GetClusters();
        }

        public override StreamExtent[] PathToExtents(string path)
        {
            throw new NotImplementedException();
        }

        public override ClusterMap GetClusterMap()
        {
            throw new NotImplementedException();
        }
        #endregion

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
                return (dirEntry != null && (dirEntry.Details.FileAttributes & FileAttributes.Directory) != 0);
            }
        }

        /// <summary>
        /// Indicates if a file exists.
        /// </summary>
        /// <param name="path">The path to test</param>
        /// <returns>true if the file exists</returns>
        public override bool FileExists(string path)
        {
            DirectoryEntry dirEntry = GetDirectoryEntry(path);
            return (dirEntry != null && (dirEntry.Details.FileAttributes & FileAttributes.Directory) == 0);
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
        /// <returns>Array of files and subdirectories.</returns>
        public override string[] GetFileSystemEntries(string path)
        {
            DirectoryEntry parentDirEntry = GetDirectoryEntry(path);
            if (parentDirEntry == null)
            {
                throw new DirectoryNotFoundException(string.Format(CultureInfo.InvariantCulture, "The directory '{0}' does not exist", path));
            }

            Directory parentDir = GetDirectory(parentDirEntry.Reference);

            return Utilities.Map<DirectoryEntry, string>(parentDir.GetAllEntries(), (m) => Utilities.CombinePaths(path, m.Details.FileName));
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
            // TODO: Be smarter, use the B*Tree for better performance when the start of the pattern is known
            // characters
            Regex re = Utilities.ConvertWildcardsToRegEx(searchPattern);

            DirectoryEntry parentDirEntry = GetDirectoryEntry(path);
            if (parentDirEntry == null)
            {
                throw new DirectoryNotFoundException(string.Format(CultureInfo.InvariantCulture, "The directory '{0}' does not exist", path));
            }

            Directory parentDir = GetDirectory(parentDirEntry.Reference);

            List<string> result = new List<string>();
            foreach (DirectoryEntry dirEntry in parentDir.GetAllEntries())
            {
                if (re.IsMatch(dirEntry.Details.FileName))
                {
                    result.Add(Path.Combine(path, dirEntry.Details.FileName));
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
            string fileName = Utilities.GetFileFromPath(path);
            string attributeName = null;

            int streamSepPos = fileName.IndexOf(':');
            if (streamSepPos >= 0)
            {
                attributeName = fileName.Substring(streamSepPos + 1);
            }

            DirectoryEntry entry = GetDirectoryEntry(Path.Combine(Path.GetDirectoryName(path),fileName));
            if (entry == null)
            {
                if (mode == FileMode.Open)
                {
                    throw new FileNotFoundException("No such file", path);
                }
                else
                {
                    File file = File.CreateNew(_context);

                    DirectoryEntry dirDirEntry = GetDirectoryEntry(Path.GetDirectoryName(path));
                    Directory destDir = GetDirectory(dirDirEntry.Reference);
                    entry = destDir.AddEntry(file, Path.GetFileName(path));
                }
            }


            if ((entry.Details.FileAttributes & FileAttributes.Directory) != 0)
            {
                throw new IOException("Attempt to open directory as a file");
            }
            else
            {
                File file = GetFile(entry.Reference);
                NtfsAttribute attr = file.GetAttribute(AttributeType.Data, attributeName);

                if (attr == null)
                {
                    if (mode == FileMode.Create || mode == FileMode.OpenOrCreate)
                    {
                        file.CreateAttribute(AttributeType.Data, attributeName);
                    }
                    else
                    {
                        throw new FileNotFoundException("No such attribute on file", path);
                    }
                }

                SparseStream stream = new NtfsFileStream(this, entry, AttributeType.Data, attributeName, access);

                if (mode == FileMode.Create || mode == FileMode.Truncate)
                {
                    stream.SetLength(0);
                }

                return stream;
            }
        }

        /// <summary>
        /// Opens an existing attribute.
        /// </summary>
        /// <param name="file">The file containing the attribute</param>
        /// <param name="type">The type of the attribute</param>
        /// <param name="name">The name of the attribute</param>
        /// <param name="access">The desired access to the attribute</param>
        /// <returns>A stream with read access to the attribute</returns>
        public Stream OpenRawAttribute(string file, AttributeType type, string name, FileAccess access)
        {
            DirectoryEntry entry = GetDirectoryEntry(file);
            if (entry == null)
            {
                throw new FileNotFoundException("No such file", file);
            }

            File fileObj = GetFile(entry.Reference);
            return fileObj.OpenAttribute(type, name, access);
        }

        public override void CopyFile(string sourceFile, string destinationFile, bool overwrite)
        {
            throw new NotImplementedException();
        }

        public override void CreateDirectory(string path)
        {
            throw new NotImplementedException();
        }

        public override void DeleteDirectory(string path)
        {
            throw new NotImplementedException();
        }

        public override void DeleteFile(string path)
        {
            throw new NotImplementedException();
        }

        public override void MoveDirectory(string sourceDirectoryName, string destinationDirectoryName)
        {
            throw new NotImplementedException();
        }

        public override void MoveFile(string sourceName, string destinationName, bool overwrite)
        {
            throw new NotImplementedException();
        }

        public override void SetAttributes(string path, FileAttributes newValue)
        {
            throw new NotImplementedException();
        }

        public override void SetCreationTimeUtc(string path, DateTime newTime)
        {
            throw new NotImplementedException();
        }

        public override void SetLastAccessTimeUtc(string path, DateTime newTime)
        {
            throw new NotImplementedException();
        }

        public override void SetLastWriteTimeUtc(string path, DateTime newTime)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Gets the security descriptor associated with the file or directory.
        /// </summary>
        /// <param name="path">The file or directory to inspect.</param>
        /// <returns>The security descriptor.</returns>
        public FileSystemSecurity GetAccessControl(string path)
        {
            DirectoryEntry dirEntry = GetDirectoryEntry(path);
            if (dirEntry == null)
            {
                throw new FileNotFoundException("File not found", path);
            }
            else
            {
                File file = GetFile(dirEntry.Reference);

                NtfsAttribute legacyAttr = file.GetAttribute(AttributeType.SecurityDescriptor);
                if (legacyAttr != null)
                {
                    return ((StructuredNtfsAttribute<SecurityDescriptor>)legacyAttr).Content.Descriptor;
                }

                StandardInformation si = file.GetAttributeContent<StandardInformation>(AttributeType.StandardInformation);
                return _context.SecurityDescriptors.GetDescriptorById(si.SecurityId);
            }
        }

        /// <summary>
        /// Gets the attributes of a file or directory.
        /// </summary>
        /// <param name="path">The file or directory to inspect</param>
        /// <returns>The attributes of the file or directory</returns>
        public override FileAttributes GetAttributes(string path)
        {
            DirectoryEntry dirEntry = GetDirectoryEntry(path);
            if (dirEntry == null)
            {
                throw new FileNotFoundException("File not found", path);
            }
            else
            {
                return dirEntry.Details.FileAttributes;
            }
        }

        /// <summary>
        /// Gets the creation time (in UTC) of a file or directory.
        /// </summary>
        /// <param name="path">The path of the file or directory.</param>
        /// <returns>The creation time.</returns>
        public override DateTime GetCreationTimeUtc(string path)
        {
            DirectoryEntry dirEntry = GetDirectoryEntry(path);
            if (dirEntry == null)
            {
                throw new FileNotFoundException("File not found", path);
            }
            else
            {
                return dirEntry.Details.CreationTime;
            }
        }

        /// <summary>
        /// Gets the last access time (in UTC) of a file or directory.
        /// </summary>
        /// <param name="path">The path of the file or directory</param>
        /// <returns>The last access time</returns>
        public override DateTime GetLastAccessTimeUtc(string path)
        {
            DirectoryEntry dirEntry = GetDirectoryEntry(path);
            if (dirEntry == null)
            {
                throw new FileNotFoundException("File not found", path);
            }
            else
            {
                return dirEntry.Details.LastAccessTime;
            }
        }

        /// <summary>
        /// Gets the last modification time (in UTC) of a file or directory.
        /// </summary>
        /// <param name="path">The path of the file or directory</param>
        /// <returns>The last write time</returns>
        public override DateTime GetLastWriteTimeUtc(string path)
        {
            DirectoryEntry dirEntry = GetDirectoryEntry(path);
            if (dirEntry == null)
            {
                throw new FileNotFoundException("File not found", path);
            }
            else
            {
                return dirEntry.Details.ModificationTime;
            }
        }

        /// <summary>
        /// Gets the length of a file.
        /// </summary>
        /// <param name="path">The path to the file</param>
        /// <returns>The length in bytes</returns>
        public override long GetFileLength(string path)
        {
            DirectoryEntry dirEntry = GetDirectoryEntry(path);
            if (dirEntry == null)
            {
                throw new FileNotFoundException("File not found", path);
            }
            return (long)dirEntry.Details.RealSize;
        }

        /// <summary>
        /// Creates an NTFS hard link to an existing file.
        /// </summary>
        /// <param name="sourceName">An existing name of the file.</param>
        /// <param name="destinationName">The name of the new hard link to the file.</param>
        public void CreateHardLink(string sourceName, string destinationName)
        {
            DirectoryEntry sourceDirEntry = GetDirectoryEntry(sourceName);
            if (sourceDirEntry == null)
            {
                throw new FileNotFoundException("Source file not found", sourceName);
            }

            string destinationDirName = Path.GetDirectoryName(destinationName);
            DirectoryEntry destinationDirDirEntry = GetDirectoryEntry(destinationDirName);
            if (destinationDirDirEntry == null || (destinationDirDirEntry.Details.FileAttributes & FileAttributes.Directory) == 0)
            {
                throw new FileNotFoundException("Destination directory not found", destinationDirName);
            }

            Directory destinationDir = GetDirectory(destinationDirDirEntry.Reference);
            if (destinationDir == null)
            {
                throw new FileNotFoundException("Destination directory not found", destinationDirName);
            }

            DirectoryEntry destinationDirEntry = GetDirectoryEntry(destinationDir, Path.GetFileName(destinationName));
            if (destinationDirEntry != null)
            {
                throw new IOException("A file with this name already exists: " + destinationName);
            }

            File file = GetFile(sourceDirEntry.Reference);
            destinationDir.AddEntry(file, Path.GetFileName(destinationName));
        }

        #region File access
        internal Directory GetDirectory(long index)
        {
            return (Directory)GetFile(index);
        }

        internal Directory GetDirectory(FileReference fileReference)
        {
            return (Directory)GetFile(fileReference);
        }

        internal File GetFile(FileReference fileReference)
        {
            FileRecord record = _context.Mft.GetRecord(fileReference);
            if (record == null)
            {
                return null;
            }

            File file = _fileCache[fileReference.MftIndex];

            if (file != null && file.MftReference.SequenceNumber != record.SequenceNumber)
            {
                file = null;
            }

            if (file == null)
            {
                if ((record.Flags & FileRecordFlags.IsDirectory) != 0)
                {
                    file = new Directory(_context, _context.Mft, record);
                }
                else
                {
                    file = new File(_context, record);
                }
                _fileCache[fileReference.MftIndex] = file;
            }

            return file;
        }

        internal File GetFile(long index)
        {
            FileRecord record = _context.Mft.GetRecord(index, false);
            if (record == null)
            {
                return null;
            }

            File file = _fileCache[index];

            if (file != null && file.MftReference.SequenceNumber != record.SequenceNumber)
            {
                file = null;
            }

            if (file == null)
            {
                if ((record.Flags & FileRecordFlags.IsDirectory) != 0)
                {
                    file = new Directory(_context, _context.Mft, record);
                }
                else
                {
                    file = new File(_context, record);
                }
                _fileCache[index] = file;
            }

            return file;
        }

        internal File AllocateFile()
        {
            File file = new File(_context, _context.Mft.AllocateRecord());
            _fileCache[file.MftReference.MftIndex] = file;
            return file;
        }
        #endregion

        /// <summary>
        /// Writes a diagnostic dump of key NTFS structures.
        /// </summary>
        /// <param name="writer">The writer to receive the dump.</param>
        /// <param name="linePrefix">The indent to apply to the start of each line of output.</param>
        public void Dump(TextWriter writer, string linePrefix)
        {
            writer.WriteLine(linePrefix + "NTFS File System Dump");
            writer.WriteLine(linePrefix + "=====================");

            _context.Mft.Dump(writer, linePrefix);

            writer.WriteLine(linePrefix);
            _context.SecurityDescriptors.Dump(writer, linePrefix);

            writer.WriteLine(linePrefix);
            _context.ObjectIds.Dump(writer, linePrefix);

            writer.WriteLine(linePrefix);
            GetDirectory(MasterFileTable.RootDirIndex).Dump(writer, linePrefix);

            writer.WriteLine(linePrefix);
            writer.WriteLine(linePrefix + "FULL FILE LISTING");
            foreach (var record in _context.Mft.Records)
            {
                // Don't go through cache - these are short-lived, and this is (just!) diagnostics
                File f = new File(_context, record);
                f.Dump(writer, linePrefix);
            }

            writer.WriteLine(linePrefix);
            writer.WriteLine(linePrefix + "DIRECTORY TREE");
            writer.WriteLine(linePrefix + @"\ (5)");
            DumpDirectory(GetDirectory(MasterFileTable.RootDirIndex), writer, linePrefix);  // 5 = Root Dir
        }

        internal DirectoryEntry GetDirectoryEntry(string path)
        {
            return GetDirectoryEntry(GetDirectory(MasterFileTable.RootDirIndex), path);
        }

        private DirectoryEntry GetDirectoryEntry(Directory dir, string path)
        {
            string[] pathElements = path.Split(new char[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            return GetDirectoryEntry(dir, pathElements, 0);
        }

        private void DoSearch(List<string> results, string path, Regex regex, bool subFolders, bool dirs, bool files)
        {
            DirectoryEntry parentDirEntry = GetDirectoryEntry(path);
            Directory parentDir = GetDirectory(parentDirEntry.Reference);

            foreach (DirectoryEntry de in parentDir.GetAllEntries())
            {
                bool isDir = ((de.Details.FileAttributes & FileAttributes.Directory) != 0);

                if ((isDir && dirs) || (!isDir && files))
                {
                    if (regex.IsMatch(de.Details.FileName))
                    {
                        results.Add(Path.Combine(path, de.Details.FileName));
                    }
                }

                if (subFolders && isDir)
                {
                    DoSearch(results, Path.Combine(path, de.Details.FileName), regex, subFolders, dirs, files);
                }
            }
        }

        private DirectoryEntry GetDirectoryEntry(Directory dir, string[] pathEntries, int pathOffset)
        {
            DirectoryEntry entry;

            if (pathEntries.Length == 0)
            {
                return dir.DirectoryEntry;
            }
            else
            {
                entry = dir.GetEntryByName(pathEntries[pathOffset]);
                if (entry != null)
                {
                    if (pathOffset == pathEntries.Length - 1)
                    {
                        return entry;
                    }
                    else if ((entry.Details.FileAttributes & FileAttributes.Directory) != 0)
                    {
                        return GetDirectoryEntry(GetDirectory(entry.Reference), pathEntries, pathOffset + 1);
                    }
                    else
                    {
                        throw new IOException(string.Format(CultureInfo.InvariantCulture, "{0} is a file, not a directory", pathEntries[pathOffset]));
                    }
                }
                else
                {
                    return null;
                }
            }
        }

        private void DumpDirectory(Directory dir, TextWriter writer, string indent)
        {
            foreach (DirectoryEntry dirEntry in dir.GetAllEntries())
            {
                File file = GetFile(dirEntry.Reference);
                Directory asDir = file as Directory;
                writer.WriteLine(indent + "+-" + file.ToString() + " (" + file.IndexInMft + ")");

                // Recurse - but avoid infinite recursion via the root dir...
                if (asDir != null && file.IndexInMft != 5)
                {
                    DumpDirectory(asDir, writer, indent + "| ");
                }
            }
        }

        private static void SplitPath(string path, out string plainPath, out string attributeName)
        {
            plainPath = path;
            string fileName = Utilities.GetFileFromPath(path);
            attributeName = null;


            int streamSepPos = fileName.IndexOf(':');
            if (streamSepPos >= 0)
            {
                attributeName = fileName.Substring(streamSepPos + 1);
                plainPath = plainPath.Substring(0, path.Length - (fileName.Length - streamSepPos));
            }
        }

    }
}
