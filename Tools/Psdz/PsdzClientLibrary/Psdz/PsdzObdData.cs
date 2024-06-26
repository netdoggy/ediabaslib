﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace BMW.Rheingold.Psdz.Model.Obd
{
    [DataContract]
    [KnownType(typeof(PsdzObdTripleValue))]
    public class PsdzObdData : IPsdzObdData
    {
        [DataMember]
        public IEnumerable<IPsdzObdTripleValue> ObdTripleValues { get; set; }
    }
}
