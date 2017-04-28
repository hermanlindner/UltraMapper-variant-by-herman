﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UltraMapper.Conventions
{
    public interface IStringSplittingRule
    {
        bool RemoveSplitChar { get; }
        bool IsSplitChar( char c );
    }
}