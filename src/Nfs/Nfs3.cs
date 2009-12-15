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

using System.IO;

namespace DiscUtils.Nfs
{
    internal enum Nfs3Status {
        Ok = 0,
        NotOwner = 1,
        NoSuchEntity = 2,
        IoError = 5,
        NoSuchDeviceOrAddress = 6,
        AccessDenied = 13,
        FileExists = 17,
        AttemptedCrossDeviceHardLink = 18,
        NoSuchDevice = 19,
        NotDirectory = 20,
        IsADirectory = 21,
        InvalidArgument = 22,
        FileTooLarge = 27,
        NoSpaceAvailable = 28,
        ReadOnlyFilesystem = 30,
        TooManyHardLinks = 31,
        NameTooLong = 63,
        DirectoryNotEmpty = 66,
        QuotaHardLimitExceeded = 69,
        StaleFileHandle = 70,
        TooManyRemoteAccessLevels = 71,
        BadFileHandle = 10001,
        UpdateSynchronizationError = 10002,
        StaleCookie = 10003,
        NotSupported = 10004,
        TooSmall = 10005,
        ServerFault = 10006,
        BadType = 10007,
        SlowJukeBox = 10008
    };



    internal sealed class Nfs3 : RpcProgram
    {
        public const int ProgramIdentifier = 100003;
        public const int ProgramVersion = 3;

        public const int MaxFileHandleSize = 64;
        public const int CookieVerifierSize = 8;
        public const int CreateVerifierSize = 8;
        public const int WriteVerifierSize = 8;

        public Nfs3(RpcClient client)
            : base(client)
        {
        }

        public override int Identifier
        {
            get { return ProgramIdentifier; }
        }

        public override int Version
        {
            get { return ProgramVersion; }
        }



        public Nfs3SetAttributesResult SetAttributes(Nfs3FileHandle dirHandle, Nfs3SetAttributes newAttributes)
        {
            MemoryStream ms = new MemoryStream();
            XdrDataWriter writer = StartCallMessage(ms, _client.Credentials, 2);
            dirHandle.Write(writer);
            newAttributes.Write(writer);
            writer.Write(false);

            RpcReply reply = DoSend(ms);
            if (reply.Header.IsSuccess)
            {
                return new Nfs3SetAttributesResult(reply.BodyReader);
            }
            else
            {
                throw new RpcException(reply.Header.ReplyHeader);
            }
        }

        public Nfs3LookupResult Lookup(Nfs3FileHandle dir, string name)
        {
            MemoryStream ms = new MemoryStream();
            XdrDataWriter writer = StartCallMessage(ms, _client.Credentials, 3);
            dir.Write(writer);
            writer.Write(name);

            RpcReply reply = DoSend(ms);
            if (reply.Header.IsSuccess)
            {
                return new Nfs3LookupResult(reply.BodyReader);
            }
            else
            {
                throw new RpcException(reply.Header.ReplyHeader);
            }
        }

        public Nfs3AccessResult Access(Nfs3FileHandle handle, Nfs3AccessPermissions requested)
        {
            MemoryStream ms = new MemoryStream();
            XdrDataWriter writer = StartCallMessage(ms, _client.Credentials, 4);
            handle.Write(writer);
            writer.Write((int)requested);

            RpcReply reply = DoSend(ms);
            if (reply.Header.IsSuccess)
            {
                return new Nfs3AccessResult(reply.BodyReader);
            }
            else
            {
                throw new RpcException(reply.Header.ReplyHeader);
            }
        }

        public Nfs3ReadResult Read(Nfs3FileHandle handle, long position, int count)
        {
            MemoryStream ms = new MemoryStream();
            XdrDataWriter writer = StartCallMessage(ms, _client.Credentials, 6);
            handle.Write(writer);
            writer.Write(position);
            writer.Write(count);

            RpcReply reply = DoSend(ms);
            if (reply.Header.IsSuccess)
            {
                return new Nfs3ReadResult(reply.BodyReader);
            }
            else
            {
                throw new RpcException(reply.Header.ReplyHeader);
            }
        }

        public Nfs3WriteResult Write(Nfs3FileHandle handle, long position, byte[] buffer, int bufferOffset, int count)
        {
            MemoryStream ms = new MemoryStream();
            XdrDataWriter writer = StartCallMessage(ms, _client.Credentials, 7);
            handle.Write(writer);
            writer.Write(position);
            writer.Write(count);
            writer.Write((int)0); // UNSTABLE
            writer.WriteBuffer(buffer, bufferOffset, count);

            RpcReply reply = DoSend(ms);
            if (reply.Header.IsSuccess)
            {
                return new Nfs3WriteResult(reply.BodyReader);
            }
            else
            {
                throw new RpcException(reply.Header.ReplyHeader);
            }
        }

        public Nfs3CreateResult Create(Nfs3FileHandle dirHandle, string name, bool createNew, Nfs3SetAttributes attributes)
        {
            MemoryStream ms = new MemoryStream();
            XdrDataWriter writer = StartCallMessage(ms, _client.Credentials, 8);
            dirHandle.Write(writer);
            writer.Write(name);
            writer.Write((int)(createNew ? 1 : 0));
            attributes.Write(writer);

            RpcReply reply = DoSend(ms);
            if (reply.Header.IsSuccess)
            {
                return new Nfs3CreateResult(reply.BodyReader);
            }
            else
            {
                throw new RpcException(reply.Header.ReplyHeader);
            }
        }

        public Nfs3SetAttributesResult Remove(Nfs3FileHandle dirHandle, string name)
        {
            MemoryStream ms = new MemoryStream();
            XdrDataWriter writer = StartCallMessage(ms, _client.Credentials, 12);
            dirHandle.Write(writer);
            writer.Write(name);

            RpcReply reply = DoSend(ms);
            if (reply.Header.IsSuccess)
            {
                return new Nfs3SetAttributesResult(reply.BodyReader);
            }
            else
            {
                throw new RpcException(reply.Header.ReplyHeader);
            }
        }

        public Nfs3ReadDirPlusResult ReadDirPlus(Nfs3FileHandle dir, ulong cookie, byte[] cookieVerifier, uint dirCount, uint maxCount)
        {
            MemoryStream ms = new MemoryStream();
            XdrDataWriter writer = StartCallMessage(ms, _client.Credentials, 17);
            dir.Write(writer);
            writer.Write(cookie);
            writer.WriteBytes(cookieVerifier ?? new byte[Nfs3.CookieVerifierSize]);
            writer.Write(dirCount);
            writer.Write(maxCount);

            RpcReply reply = DoSend(ms);
            if (reply.Header.IsSuccess)
            {
                return new Nfs3ReadDirPlusResult(reply.BodyReader);
            }
            else
            {
                throw new RpcException(reply.Header.ReplyHeader);
            }
        }

        public Nfs3FileSystemInfoResult FileSystemInfo(Nfs3FileHandle fileHandle)
        {
            MemoryStream ms = new MemoryStream();
            XdrDataWriter writer = StartCallMessage(ms, _client.Credentials, 19);
            fileHandle.Write(writer);

            RpcReply reply = DoSend(ms);
            if (reply.Header.IsSuccess)
            {
                Nfs3FileSystemInfoResult fsiReply = new Nfs3FileSystemInfoResult(reply.BodyReader);
                if (fsiReply.Status == Nfs3Status.Ok)
                {
                    return fsiReply;
                }
                else
                {
                    throw new Nfs3Exception(fsiReply.Status);
                }
            }
            else
            {
                throw new RpcException(reply.Header.ReplyHeader);
            }
        }
    }
}
