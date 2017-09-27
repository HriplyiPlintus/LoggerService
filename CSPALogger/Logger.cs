using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.Data.Entity;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CSPALogger
{
	/*
	 * Для установки необходимо установить .net framework 4.7
	 * mysql (версии на армах хватит)
	 * запустить из командной строки утилиту C:\Windows\Microsoft.NET\Framework64\v4.0.30319\InstallUtil.exe
	 * и передать ей в виде агрумента директорию exe файла C:\CSPALogger\CSPALogger\CSPALogger\bin\Release\CSPALogger.exe
	 * после этого открыть службы, и указать пароль и логин пользователя, от имени которого должна запускаться служба
	 * и запустить службу. Отлидочные сообщения можно наблюдать в журнале приложений.
	 * Для удаления службы необходимо утилине installUtil передать ту же директорию с ключем /u
	 * InstallUtil.exe C:\CSPALogger\CSPALogger\CSPALogger\bin\Release\CSPALogger.exe /u
	 */
	public class Logger
	{
		private string _configFilePath = @"CSPALoggerConfig.xml";
		//private string _previousSeqNo;
		private bool _enabled;

		private CSPAContext _db;
		private readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
		private XDocument _doc = new XDocument();
		private object _o = new object();
		private readonly string _point = "FS";
		private const string _source = "ARM";  
		private readonly string _securityLogFilePath, _securityLogFileName;

		public Logger()
		{			   
			//определение пути к файлу конфигурации
			_configFilePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "\\Content\\" + _configFilePath;
			//no file - no logs
			if (!File.Exists(_configFilePath))
			{
				WriteToEventLogger($"Не задан файл конфигурации. Определите файл конфигурации и перезапустите службу.\n" +
				                   $"{_configFilePath} не существует.");
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
			foreach (var i in xmq)
			{
				var path = i.Path;										  

				//var excludeFilter = i.ExcludeFilter;	//todo implenetm excludeFilter
				var filter = Path.GetExtension(path) == "" ? "" : path; //if path is a concrete file than watch just for it's changes

				//get security file path and name
				if(Directory.Exists(path) || File.Exists(path))
				{
					_securityLogFilePath = path;
					_securityLogFileName = Path.GetFileName(path);
				}
				else
				{
					continue;	//no path ? next entry in config file
				}

				var w = new FileSystemWatcher
				{
					Path = Directory.Exists(path) ? path : Path.GetDirectoryName(path),
					Filter = Path.GetFileName(filter),
					IncludeSubdirectories = filter == ""	//if filter == "" than we work with directory and need to check subdirectories
				};

				w.Changed += Logger_Changed;
				w.Created += Logger_Created;
				w.Deleted += Logger_Deleted;
				w.Renamed += Logger_Renamed;

				_watchers.Add(w);
			}

			var eventMessage = $"There is(are) {_watchers.Count} separate FileSystemWatcher(s) will log next folders and files: \n";
			foreach(var w in _watchers)
				eventMessage += $"Path: \"{w.Path}\"; Filter: \"{w.Filter}\";\n";

			WriteToEventLogger(eventMessage, EventLogEntryType.Information);
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
			DateTime timestamp;
			maintable entry;

			if (e.Name.Contains(_securityLogFileName))	//if security event file changed
			{
				var securityEvent = GetSecurityEventMessage();
				if(securityEvent.Message == null) return;

				msg = securityEvent.Message;
				timestamp = securityEvent.Timestamp;
				var source = "Security";

				entry = MakeMaintableEntry(msg, timestamp, source);
			}
			else
			{
				string filePath = e.FullPath;
				msg = $"[Файловая система] - файл \"{filePath}\" был изменен";

				entry = MakeMaintableEntry(msg);
			}

			PutInDb(entry);	//write to db
		}

		public void Logger_Created(object sender, FileSystemEventArgs e)
		{
			string filePath = e.FullPath;
			string msg = $"[Файловая система] - файл \"{filePath}\" был создан";

			var entry = MakeMaintableEntry(msg);
										 
			PutInDb(entry);
		}

		public void Logger_Deleted(object sender, FileSystemEventArgs e)
		{
			string filePath = e.FullPath;
			string msg = $"[Файловая система] - файл \"{filePath}\" был удален";

			var entry = MakeMaintableEntry(msg);
													
			PutInDb(entry);
		}

		public void Logger_Renamed(object sender, RenamedEventArgs e)
		{
			string msg = $"[Файловая система] - файл \"{e.OldFullPath}\" был переименован в {e.FullPath}";

			var entry = MakeMaintableEntry(msg);
													
			PutInDb(entry);
		}

		#endregion

		#region SecurityEvents

		//message from inconics security events
		private (string Message, DateTime Timestamp) GetSecurityEventMessage()
		{
			try
			{
				_doc = XDocument.Load(_securityLogFilePath);
			}
			catch
			{
				return (null, DateTime.Now);	//because DateTime is not nullable type
			}

			var xmq = _doc.Element("Trace")
				.Elements("record")
				.OrderByDescending(e => long.Parse(e.Attribute("SeqNo").Value.Trim()))
				.FirstOrDefault(e => e.Element("message").Value.Contains("DeleteUser :")
				                     || e.Element("message").Value.Contains("AddUser :"));

			DateTime time = DateTime.Parse(xmq.Attribute("timestamp").Value.Replace("T", " ").Trim());  //todo changed

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

			//if row in db already exists
			if (_db.maintables
				.FirstOrDefault(row => row.timestamp == time && row.message == msg) != null)
			{
				return (null, DateTime.Now);
			}

			return (msg, time);
		}

		#endregion

		//prepares maintable entity to write it to db
		private maintable MakeMaintableEntry(string messageParam, string sourceParam = _source)
		{	
			//if we don't have timestamp than we put there DateTime.Now
			return MakeMaintableEntry(messageParam, DateTime.Now,sourceParam);
		}

		private maintable MakeMaintableEntry(string messageParam, DateTime timeParam, string sourceParam = _source)
		{
			if (messageParam == "") return null;    //bug redundant

			var lastRecord = _db.maintables.OrderByDescending(e => e.id).FirstOrDefault();
			string userRole = lastRecord?.role;	//write null if no last row in db
			string userName = lastRecord?.username;

			return new maintable
			{
				point = _point,
				role = userRole,
				username = userName,
				source = sourceParam,
				message = messageParam,
				timestamp = timeParam
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
		private void WriteToEventLogger(string msg, EventLogEntryType eventType = EventLogEntryType.Error)
		{
			string source = "CSPALogger";	  

			if(!EventLog.SourceExists(source))
				EventLog.CreateEventSource(source, "Application");

			EventLog.WriteEntry(source, msg, eventType);
		}
	}
}
