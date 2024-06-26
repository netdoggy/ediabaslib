﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace BMW.Rheingold.Psdz.Model.Swt
{
    [DataContract]
    [KnownType(typeof(PsdzSwtEcu))]
    public class PsdzSwtAction : IPsdzSwtAction
    {
        [DataMember]
        public IEnumerable<IPsdzSwtEcu> SwtEcus { get; set; }
    }
}
