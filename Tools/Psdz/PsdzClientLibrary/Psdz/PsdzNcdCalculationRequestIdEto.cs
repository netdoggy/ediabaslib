﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace BMW.Rheingold.Psdz.Model.SecureCoding
{
    [DataContract]
    public class PsdzNcdCalculationRequestIdEto : IPsdzNcdCalculationRequestIdEto
    {
        [DataMember]
        public string RequestId { get; set; }
    }
}
