﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BMW.Rheingold.Psdz.Model.Ecu;

namespace BMW.Rheingold.Psdz.Model
{
    public interface IPsdzStandardSvt
    {
        string AsString { get; }

        IEnumerable<IPsdzEcu> Ecus { get; }

        byte[] HoSignature { get; }

        DateTime HoSignatureDate { get; }

        int Version { get; }
    }
}
