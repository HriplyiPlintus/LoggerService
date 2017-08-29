using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration.Install;
using System.Linq;
using System.ServiceProcess;
using System.Threading.Tasks;

namespace CSPALogger
{
	[RunInstaller(true)]
	public partial class Installer1 : System.Configuration.Install.Installer
	{
		private ServiceInstaller _serviceInstaller;
		private ServiceProcessInstaller _serviceProcessInstaller;

		public Installer1()
		{
			InitializeComponent();

			_serviceInstaller = new ServiceInstaller
			{
				ServiceName = "CSPALogger",
				StartType = ServiceStartMode.Manual
			};

			_serviceProcessInstaller = new ServiceProcessInstaller
			{
				Account = ServiceAccount.LocalSystem
			};

			Installers.Add(_serviceProcessInstaller);
			Installers.Add(_serviceInstaller);
		}
	}
}
