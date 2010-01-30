﻿//
// Copyright (c) 2008-2010, Kenneth Bell
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
using System.Management.Automation;
using DiscUtils.Ntfs;

namespace DiscUtils.PowerShell
{
    public enum FileSystemType
    {
        Ntfs = 0
    }

    [Cmdlet(VerbsCommon.Format, "Volume")]
    public class FormatVolumeCommand : PSCmdlet
    {
        [Parameter(ValueFromPipeline = true)]
        public PSObject InputObject { get; set; }

        [Parameter]
        public FileSystemType Filesystem { get; set; }

        [Parameter]
        public string Label { get; set; }

        public FormatVolumeCommand()
        {
        }

        protected override void ProcessRecord()
        {
            VolumeInfo volInfo = null;

            if (InputObject != null)
            {
                volInfo = InputObject.BaseObject as VolumeInfo;
            }
            if (volInfo == null)
            {
                WriteError(new ErrorRecord(
                    new ArgumentException("No volume specified"),
                    "NoVolumeSpecified",
                    ErrorCategory.InvalidArgument,
                    null));
                return;
            }

            if(Filesystem != FileSystemType.Ntfs)
            {
                WriteError(new ErrorRecord(
                    new ArgumentException("Unknown filesystem type"),
                    "BadFilesystem",
                    ErrorCategory.InvalidArgument,
                    null));
                return;
            }

            NtfsFileSystem.Format(volInfo, Label);
        }
    }
}
