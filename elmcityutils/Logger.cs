using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;

namespace ElmcityUtils
{
	public class LogMsg
	{
		public string type { get; set; }
		public string title { get; set; }
		public string blurb { get; set; }

		public LogMsg(string type, string title, string blurb)
		{
			this.type = type;
			this.title = GenUtils.MakeLogMsgTitle(title);
			this.blurb = blurb;
		}
	}

	public class Logger
	{
		private TableStorage ts { get; set; }

		private int wait_milliseconds = 50;

		private int max_messages = 100;

		private static string hostname = Dns.GetHostName(); // for status/error reporting

		private Queue<LogMsg> log_queue = new Queue<LogMsg>();

		public Logger()
		{
			this.ts = TableStorage.MakeDefaultTableStorage();
			this.Start();
		}

		public Logger(int milliseconds, int max_messages)
		{
			this.ts = TableStorage.MakeDefaultTableStorage();
			this.wait_milliseconds = milliseconds;
			this.max_messages = max_messages;
			this.Start();
		}

		public Logger(int milliseconds, int max_messages, TableStorage ts)
		{
			this.ts = ts;
			this.wait_milliseconds = milliseconds;
			this.max_messages = max_messages;
			this.Start();
		}

		private void Start()
		{
			var dequeue_thread = new Thread(new ThreadStart(LogThreadMethod));
			dequeue_thread.Start();
		}

		public void LogMsg(string type, string title, string blurb)
		{
			var msg = new LogMsg(type: type, title: title, blurb: blurb);
			log_queue.Enqueue(msg);
		}

		public void LogHttpRequest(System.Web.Mvc.ControllerContext c)
		{
			var msg = HttpUtils.MakeHttpLogMsg(c);
			log_queue.Enqueue(msg);
		}

		// todo: flesh this out with extra info
		public void LogHttpRequestEx(System.Web.Mvc.ControllerContext c)
		{
			var msg = HttpUtils.MakeHttpLogMsg(c);
			var r = c.HttpContext.Request;
			var extra = new Dictionary<string, string>();
			// msg += JsonConvert(...)
			log_queue.Enqueue(msg);
		}

		private void LogThreadMethod()
		{
			while (true)
			{
				//if (this.log_queue.Count > this.max_messages)
				//	GenUtils.PriorityLogMsg("warning", "Logger", String.Format("{0} messages", this.log_queue.Count));

				if (this.log_queue.Count > 0)
				{
					var msg = log_queue.Dequeue();
					this.ts.WriteLogMessage(msg.type, msg.title, msg.blurb);
				}

				Thread.Sleep(wait_milliseconds);
			}
		}
	}
}
