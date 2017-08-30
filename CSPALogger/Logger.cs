using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CSPALogger
{
	public class Logger
	{
		private readonly string _configFilePath = @"C:\CSPALoggerConfig.xml";
		private string _previousSeqNo;
		private bool _enabled;

		private CSPAContext _db;
		private readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
		private XDocument _doc = new XDocument();
		private object _o = new object();
		private readonly string _point = "FS";
		private const string _source = "ARM";
		private readonly string _securityLogFile = "security.exe.log.xml";	//hardcode  

		public Logger()
		{
			//no file - no logs
			if (!File.Exists(_configFilePath))
			{
				WriteToEventLogger("Не задан файл конфигурации. Определите файл конфигурации и перезапустите службу.");
				return;
			}

			var configFile = XDocument.Load(_configFilePath);

			//get all pathes to monitor
			var xmq = configFile.Element("config")?.Elements("directory")
				.Select(e => new
				{
					Path = e.Element("path")?.Value.Trim(),
					ExcludeFilter = e.Element("filterExclude")?.Value.Trim()
				});

			//initialize all watchers
			foreach(var i in xmq)
			{
				var path = i.Path;
				//var excludeFilter = i.ExcludeFilter;	//todo implenetm excludeFilter
				var filter = Path.GetExtension(path) == "" ? "" : path;	//if path is a concrete file than watch just for it's changes

				var w = new FileSystemWatcher
				{
					Path = Directory.Exists(path) ? path : Path.GetDirectoryName(path),
					Filter = Path.GetFileName(filter)
				};

				w.Changed += Logger_Changed;
				w.Created += Logger_Created;
				w.Deleted += Logger_Deleted;
				w.Renamed += Logger_Renamed;

				_watchers.Add(w);
			}
		}

		public void Start()
		{
			_enabled = true;

			_db = new CSPAContext();

			foreach (var w in _watchers)
			{
				w.EnableRaisingEvents = true;
			}

			while(_enabled)
			{
				Thread.Sleep(1000);
			}
		}

		public void Stop()
		{
			foreach(var w in _watchers)
			{
				w.EnableRaisingEvents = false;
			}

			_db.Dispose();
			_enabled = false;
		}

		#region Events

		public void Logger_Changed(object sender, FileSystemEventArgs e)
		{
			string msg, source = "";

			if(e.Name.Contains(_securityLogFile))	//if security event file changed
			{
				var securityEventMessage = GetSecurityEventMessage();
				//if(securityEventMessage == null) return;

				//msg = securityEventMessage;
				msg = $"проверка на файл прошла: {securityEventMessage}";
				source = "Security";
			}
			else
			{
				string filePath = e.FullPath;

				msg = $"файл {filePath} был изменен";
			}

			maintable entry;
			if(source != "")
				entry = MakeMaintableEntry(msg, source);
			else
				entry = MakeMaintableEntry(msg);

			PutInDb(entry);
		}

		public void Logger_Created(object sender, FileSystemEventArgs e)
		{
			string filePath = e.FullPath;
			string msg = $"файл {filePath} был создан";

			var entry = MakeMaintableEntry(msg);

			PutInDb(entry);
		}

		public void Logger_Deleted(object sender, FileSystemEventArgs e)
		{
			string filePath = e.FullPath;
			string msg = $"файл {filePath} был удален";

			var entry = MakeMaintableEntry(msg);

			PutInDb(entry);
		}

		public void Logger_Renamed(object sender, RenamedEventArgs e)
		{
			string msg = $"файл {e.OldFullPath} был переименован в {e.FullPath}";

			var entry = MakeMaintableEntry(msg);

			PutInDb(entry);
		}

		#endregion

		#region SecurityEvents

		//message from inconics security events
		private string GetSecurityEventMessage()
		{
			try
			{
				_doc = XDocument.Load(_securityLogFile);
			}
			catch
			{
				return null;
			}			

			var xmq = _doc.Element("Trace")
				.Elements("record")
				.OrderByDescending(e => long.Parse(e.Attribute("SeqNo").Value.Trim()))
				.FirstOrDefault(e => e.Element("message").Value.Contains("DeleteUser :")
				                     || e.Element("message").Value.Contains("AddUser :"));

			string currSeqNo = xmq.Attribute("SeqNo").Value.Trim();
							  
			if (currSeqNo == _previousSeqNo)
				return null;	//если нашлась предыдущая запись

			_previousSeqNo = currSeqNo;
		
			string msgNodeText = xmq.Element("message").Value.Trim();
			string userName = msgNodeText.Substring(msgNodeText.LastIndexOf(":") + 1).Trim();

			string msg;
			if (msgNodeText.Contains("Add"))
			{
				msg = "Создан пользователь с именем " + userName + ".";
			}
			else if (msgNodeText.Contains("Delete"))
			{
				msg = "Удален пользователь с именем " + userName + ".";
			}
			else
			{
				msg = "Непредвиденное событие.";
			}

			return msg;
		}

		#endregion

		//prepares maintable entity to write it to db
		private maintable MakeMaintableEntry(string messageParam, string sourceParam = _source)
		{
			if(messageParam == "") return null;	//bug redundant

			var lastRecord = _db.maintables.OrderByDescending(e => e.id).FirstOrDefault();
			string userRole = lastRecord.role;
			string userName = lastRecord.username;

			//return new maintable
			//{
			//	point = _point,
			//	role = userRole,
			//	username = userName,
			//	source = sourceParam,
			//	message = messageParam,
			//	timestamp = DateTime.Now
			//};
			return new maintable
			{
				point = "asdf",
				role = "asdf",
				username = "asdf",
				source = "asdf",
				message = messageParam,
				timestamp = DateTime.Now
			};
		}

		//write event message to db
		private void PutInDb(maintable e)
		{
			lock(_o)
			{
				if(e == null) return;

				_db.maintables.Add(e);
				_db.SaveChanges();
			}
		}

		//write to Event Log
		private void WriteToEventLogger(string msg)
		{
			string source = "CSPALogger";	  

			if(!EventLog.SourceExists(source))
				EventLog.CreateEventSource(source, "Application");

			EventLog.WriteEntry(source, msg, EventLogEntryType.Error);
		}
	}
}
