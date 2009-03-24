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
using System.Linq;
using System.Text;

namespace DiscUtils
{
    /// <summary>
    /// Represents a range of values.
    /// </summary>
    /// <typeparam name="Toffset">The type of the offset element</typeparam>
    /// <typeparam name="Tcount">The type of the size element</typeparam>
    public class Range<Toffset, Tcount>
    {
        private Toffset _offset;
        private Tcount _count;

        /// <summary>
        /// Creates a new instance.
        /// </summary>
        /// <param name="offset">The offset (i.e. start) of the range</param>
        /// <param name="count">The size of the range</param>
        public Range(Toffset offset, Tcount count)
        {
            _offset = offset;
            _count = count;
        }

        /// <summary>
        /// Gets the offset (i.e. start) of the range
        /// </summary>
        public Toffset Offset
        {
            get { return _offset; }
        }

        /// <summary>
        /// Gets the size of the range.
        /// </summary>
        public Tcount Count
        {
            get { return _count; }
        }

        /// <summary>
        /// Returns a string representation of the extent as [start:+length].
        /// </summary>
        /// <returns>The string representation</returns>
        public override string ToString()
        {
            return "[" + _offset + ":+" + _count + "]";
        }
    }
}
