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
		private readonly List<FileSystemWatcher> _watchers;
		private XDocument _doc = new XDocument();
		private object _o = new object();
		private readonly string _point = "FS";
		private string _source = "ARM";
		private readonly string _file;
		private readonly string _folder;

		public Logger()
		{
			var configFile = XDocument.Load(_configFilePath);

			if(configFile == null)
			{
				WriteToEventLogger("Не задан файл конфигурации.");
				return;
			}

			_folder = configFile.Element("config").Element("directory").Value.Trim();
			_file = configFile.Element("config").Element("path").Value.Trim();

			_watchers = new List<FileSystemWatcher>
			{
				new FileSystemWatcher{Path = _folder, IncludeSubdirectories = true},
				new FileSystemWatcher{Path = Path.GetDirectoryName(_file), Filter = Path.GetFileName(_file)}
			};

			foreach(var w in _watchers)
			{
				w.Changed += Logger_Changed;
				w.Created += Logger_Created;
				w.Deleted += Logger_Deleted;
				w.Renamed += Logger_Renamed;
			}
			
		}

		//one point to register FileSystemWatchers
		private void RegisterWatchers(string path)
		{
			
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
			string msg;

			//если изменения в файле
			if(_file.Contains(e.Name))
			{
				var securityEventMessage = GetSecurityEventMessage();
				if(securityEventMessage == "") return;

				msg = securityEventMessage;
			}
			else
			{
				string filePath = e.FullPath;

				msg = $"файл {filePath} был изменен";
			}

			var entry = MakeMaintableEntry(msg);

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
			string msg = "";

			try
			{
			_doc = XDocument.Load(_file);

			if(_doc == null) return null;

			var xmq = _doc.Element("Trace")
				.Elements("record")
				.OrderByDescending(e => long.Parse(e.Attribute("SeqNo").Value.Trim()))
				.FirstOrDefault(e => e.Element("message").Value.Contains("DeleteUser :")
				                     || e.Element("message").Value.Contains("AddUser :"));

			string currSeqNo = xmq.Attribute("SeqNo").Value.Trim();

			//thts не надо проверять на отсутствие файла, потому что эта проверка должна быть при инициализации								  
			if (currSeqNo == _previousSeqNo)
				return null;	//если нашлась предыдущая запись

			_previousSeqNo = currSeqNo;
		
			string msgNodeText = xmq.Element("message").Value.Trim();
			string userName = msgNodeText.Substring(msgNodeText.LastIndexOf(":") + 1).Trim();
			
			if(msgNodeText.Contains("Add"))
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
			}

			catch { }

			return msg;
		}

		#endregion

		//prepares maintable entity to write it to db in the future
		private maintable MakeMaintableEntry(string messageParam)
		{
			if(messageParam == "") return null;

			var lastRecord = _db.maintables.OrderByDescending(e => e.id).FirstOrDefault();
			string userRole = lastRecord.role;
			string userName = lastRecord.username;

			return new maintable
			{
				point = _point,
				role = userRole,
				username = userName,
				source = _source,
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

		private void WriteToEventLogger(string msg)
		{
			string source = "CSPALogger";	  

			if(!EventLog.SourceExists(source))
				EventLog.CreateEventSource(source, "Application");

			EventLog.WriteEntry(source, msg, EventLogEntryType.Error);
		}
	}
}
