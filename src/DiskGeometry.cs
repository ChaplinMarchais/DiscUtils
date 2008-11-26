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

namespace DiscUtils
{
    /// <summary>
    /// Class whose instances represent disk geometries.
    /// </summary>
    /// <remarks>Instances of this class are immutable.</remarks>
    public sealed class DiskGeometry
    {
        private int _cylinders;
        private int _headsPerCylinder;
        private int _sectorsPerTrack;

        /// <summary>
        /// Creates a new instance.
        /// </summary>
        /// <param name="cylinders">The number of cylinders of the disk</param>
        /// <param name="headsPerCylinder">The number of heads (aka platters) of the disk</param>
        /// <param name="sectorsPerTrack">The number of sectors per track/cylinder of the disk</param>
        public DiskGeometry(int cylinders, int headsPerCylinder, int sectorsPerTrack)
        {
            _cylinders = cylinders;
            _headsPerCylinder = headsPerCylinder;
            _sectorsPerTrack = sectorsPerTrack;
        }

        /// <summary>
        /// Gets the number of cylinders.
        /// </summary>
        public int Cylinders
        {
            get { return _cylinders; }
        }

        /// <summary>
        /// Gets the number of heads (aka platters).
        /// </summary>
        public int HeadsPerCylinder
        {
            get { return _headsPerCylinder; }
        }

        /// <summary>
        /// Gets the number of sectors per track.
        /// </summary>
        public int SectorsPerTrack
        {
            get { return _sectorsPerTrack; }
        }

        /// <summary>
        /// Gets the number of bytes in each sector.
        /// </summary>
        public int BytesPerSector
        {
            get { return 512; }
        }

        /// <summary>
        /// Gets the total size of the disk (in sectors).
        /// </summary>
        public int TotalSectors
        {
            get { return Cylinders * HeadsPerCylinder * SectorsPerTrack; }
        }

        /// <summary>
        /// Gets the total capacity of the disk (in bytes).
        /// </summary>
        public long Capacity
        {
            get { return ((long)TotalSectors) * ((long)BytesPerSector); }
        }

        /// <summary>
        /// Converts a CHS (Cylinder,Head,Sector) address to a LBA (Logical Block Address).
        /// </summary>
        /// <param name="chsAddress">The CHS address to convert</param>
        /// <returns>The Logical Block Address (in sectors)</returns>
        public int ToLogicalBlockAddress(ChsAddress chsAddress)
        {
            return ToLogicalBlockAddress(chsAddress.Cylinder, chsAddress.Head, chsAddress.Sector);
        }

        /// <summary>
        /// Converts a CHS (Cylinder,Head,Sector) address to a LBA (Logical Block Address).
        /// </summary>
        /// <param name="cylinder">The cylinder of the address</param>
        /// <param name="head">The head of the address</param>
        /// <param name="sector">The sector of the address</param>
        /// <returns>The Logical Block Address (in sectors)</returns>
        public int ToLogicalBlockAddress(int cylinder, int head, int sector)
        {
            if (cylinder >= _cylinders)
            {
                throw new ArgumentOutOfRangeException("cylinder", cylinder, "cylinder number is larger than disk geometry");
            }
            if (cylinder < 0)
            {
                throw new ArgumentOutOfRangeException("cylinder", cylinder, "cylinder number is negative");
            }
            if (head >= _headsPerCylinder)
            {
                throw new ArgumentOutOfRangeException("head", head, "head number is larger than disk geometry");
            }
            if (head < 0)
            {
                throw new ArgumentOutOfRangeException("head", head, "head number is negative");
            }
            if (sector > _sectorsPerTrack)
            {
                throw new ArgumentOutOfRangeException("sector", sector, "sector number is larger than disk geometry");
            }
            if (sector < 1)
            {
                throw new ArgumentOutOfRangeException("sector", sector, "sector number is less than one (sectors are 1-based)");
            }

            return (((cylinder * _headsPerCylinder) + head) * _sectorsPerTrack) + sector - 1;
        }

        /// <summary>
        /// Converts a LBA (Logical Block Address) to a CHS (Cylinder, Head, Sector) address.
        /// </summary>
        /// <param name="logicalBlockAddress">The logical block address (in sectors)</param>
        public ChsAddress ToChsAddress(int logicalBlockAddress)
        {
            if (logicalBlockAddress < 0)
            {
                throw new ArgumentOutOfRangeException("logicalBlockAddress", logicalBlockAddress, "Logical Block Address is negative");
            }

            int cylinder = (logicalBlockAddress / (_headsPerCylinder * _sectorsPerTrack));
            int temp = (logicalBlockAddress % (_headsPerCylinder * _sectorsPerTrack));
            int head = temp / _sectorsPerTrack;
            int sector = (temp % _sectorsPerTrack) + 1;

            return new ChsAddress(cylinder, head, sector);
        }

        /// <summary>
        /// Gets the address of the last sector on the disk.
        /// </summary>
        public ChsAddress LastSector
        {
            get { return new ChsAddress(_cylinders - 1, _headsPerCylinder - 1, _sectorsPerTrack); }
        }

        /// <summary>
        /// Calculates a sensible disk geometry for a disk capacity using the VHD algorithm (errs under).
        /// </summary>
        /// <param name="capacity">The desired capacity of the disk</param>
        /// <returns>The appropriate disk geometry.</returns>
        /// <remarks>The geometry returned tends to produce a disk with less capacity
        /// than requested (an exact capacity is not always possible).</remarks>
        public static DiskGeometry FromCapacity(long capacity)
        {
            int totalSectors = (int)(capacity / 512);

            int cylinders;
            int headsPerCylinder;
            int sectorsPerTrack;

            // If more than ~128GB truncate at ~128GB
            if (totalSectors > 65535 * 16 * 255)
            {
                totalSectors = 65535 * 16 * 255;
            }

            // If more than ~32GB, break partition table compatibility.
            // Partition table has max 63 sectors per track.  Otherwise
            // we're looking for a geometry that's valid for both BIOS
            // and ATA.
            if (totalSectors > 65535 * 16 * 63)
            {
                sectorsPerTrack = 255;
                headsPerCylinder = 16;
            }
            else
            {
                sectorsPerTrack = 17;
                int cylindersTimesHeads = totalSectors / sectorsPerTrack;
                headsPerCylinder = (cylindersTimesHeads + 1023) / 1024;

                if (headsPerCylinder < 4)
                {
                    headsPerCylinder = 4;
                }

                // If we need more than 1023 cylinders, or 16 heads, try more sectors per track
                if (cylindersTimesHeads >= (headsPerCylinder * 1024U) || headsPerCylinder > 16)
                {
                    sectorsPerTrack = 31;
                    headsPerCylinder = 16;
                    cylindersTimesHeads = totalSectors / sectorsPerTrack;
                }

                // We need 63 sectors per track to keep the cylinder count down
                if (cylindersTimesHeads >= (headsPerCylinder * 1024U))
                {
                    sectorsPerTrack = 63;
                    headsPerCylinder = 16;
                }

            }
            cylinders = (totalSectors / sectorsPerTrack) / headsPerCylinder;

            return new DiskGeometry(cylinders, headsPerCylinder, sectorsPerTrack);
        }

        /// <summary>
        /// Determines if this object is equivalent to another.
        /// </summary>
        /// <param name="obj">The object to test against.</param>
        /// <returns><code>true</code> if the <paramref name="obj"/> is equalivalent, else <code>false</code>.</returns>
        public override bool Equals(object obj)
        {
            if (obj == null || obj.GetType() != GetType())
            {
                return false;
            }

            DiskGeometry other = (DiskGeometry)obj;

            return _cylinders == other._cylinders && _headsPerCylinder == other._headsPerCylinder && _sectorsPerTrack == other._sectorsPerTrack;
        }

        /// <summary>
        /// Calculates the hash code for this object.
        /// </summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode()
        {
            return _cylinders.GetHashCode() ^ _headsPerCylinder.GetHashCode() ^ _sectorsPerTrack.GetHashCode();
        }

        /// <summary>
        /// Gets a string representation of this object, in the form (C/H/S).
        /// </summary>
        /// <returns>The string representation</returns>
        public override string ToString()
        {
            return "(" + _cylinders + "/" + _headsPerCylinder + "/" + _sectorsPerTrack + ")";
        }
    }
}