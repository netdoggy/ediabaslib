﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PsdzClient
{
	public interface IPsdzService
	{
#if false
		IConfigurationService ConfigurationService { get; }
#endif
		IConnectionFactoryService ConnectionFactoryService { get; }

		IConnectionManagerService ConnectionManagerService { get; }
#if false
		IEcuService EcuService { get; }

		IEventManagerService EventManagerService { get; }

		IIndividualDataRestoreService IndividualDataRestoreService { get; }

		ILogService LogService { get; }

		ILogicService LogicService { get; }

		IMacrosService MacrosService { get; }
#endif
		IObjectBuilderService ObjectBuilderService { get; }
#if false
		IProgrammingService ProgrammingService { get; }

		ITalExecutionService TalExecutionService { get; }

		IVcmService VcmService { get; }

		ICbbTlsConfiguratorService CbbTlsConfiguratorService { get; }

		ICertificateManagementService CertificateManagementService { get; }

		ISecureFeatureActivationService SecureFeatureActivationService { get; }

		ISecurityManagementService SecurityManagementService { get; }

		ISecureCodingService SecureCodingService { get; }

		IKdsService KdsService { get; }
#endif
	}
}
