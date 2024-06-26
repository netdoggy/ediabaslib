﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace BMW.Rheingold.Psdz.Model.Certificate
{
    [DataContract]
    public class PsdzBindingDetailsStatus
    {
        [DataMember]
        public PsdzEcuCertCheckingStatus? BindingStatus { get; set; }

        [DataMember]
        public PsdzEcuCertCheckingStatus? CertificateStatus { get; set; }

        [DataMember]
        public string RollenName { get; set; }
    }
}
